using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using RT.Servers;
using RT.Util;
using RT.Util.Collections;
using RT.Util.ExtensionMethods;
using RT.Util.Xml;

namespace Propeller
{
    [Serializable]
    public class PropellerConfig
    {
        public HttpServerOptions ServerOptions = new HttpServerOptions();
        public string PluginDirectory = Path.Combine(PathUtil.AppPath, "plugins");
    }

    public class Program
    {
        static PropellerApi ActiveApi = null;
        static int ApiCount = 0;
        static object LockObject = new object();
        static LoggerBase Log;

        static void ListeningThreadFunction(int port)
        {
            lock (Log)
                Log.Info("Start listening on port " + port);
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            try
            {
                while (true)
                {
                    Socket sck = listener.AcceptSocket();
                    lock (LockObject)
                    {
                        try
                        {
                            if (ActiveApi != null)
                                // This creates a new thread for handling the connection and returns pretty immediately.
                                ActiveApi.HandleRequest(sck.DuplicateAndClose(Process.GetCurrentProcess().Id));
                            else
                                sck.Close();
                        }
                        catch { }
                    }
                }
            }
            finally
            {
                lock (Log)
                    Log.Info("Stop listening on port " + port);
                listener.Stop();
            }
        }

        static void Main(string[] args)
        {
            Log = new ConsoleLogger { TimestampInUTC = true };
            var inactiveDomains = new List<Tuple<AppDomain, PropellerApi>>();
            var configPath = Path.Combine(PathUtil.AppPath, @"Propeller.config.xml");
            DateTime configFileChangeTime = DateTime.MinValue;
            Thread currentListeningThread = null;
            var mustReinitServer = false;
            PropellerConfig currentConfig = null;
            bool first = true;
            Tuple<string, DateTime>[] listOfPlugins = null;
            AppDomain activeApiDomain = null;

            while (true)
            {
                if (first || !File.Exists(configPath) || currentConfig == null || configFileChangeTime < new FileInfo(configPath).LastWriteTimeUtc)
                {
                    // Read configuration file
                    mustReinitServer = true;
                    configFileChangeTime = new FileInfo(configPath).LastWriteTimeUtc;
                    lock (Log)
                        Log.Info((first ? "Loading config file: " : "Reloading config file: ") + configPath);
                    PropellerConfig newConfig;
                    try
                    {
                        newConfig = XmlClassify.LoadObjectFromXmlFile<PropellerConfig>(configPath);
                    }
                    catch
                    {
                        lock (Log)
                            Log.Warn("Config file could not be loaded; using default config.");
                        newConfig = new PropellerConfig();
                        if (!File.Exists(configPath))
                        {
                            try
                            {
                                XmlClassify.SaveObjectToXmlFile(newConfig, configPath);
                                lock (Log)
                                    Log.Info("Default config saved to {0}.".Fmt(configPath));
                                configFileChangeTime = new FileInfo(configPath).LastWriteTimeUtc;
                            }
                            catch (Exception e)
                            {
                                lock (Log)
                                    Log.Warn("Attempt to save default config to {0} failed: {1}".Fmt(configPath, e.Message));
                            }
                        }
                    }

                    // If port number is different from previous port number, create a new listening thread and kill the old one
                    if (first || currentConfig == null || (newConfig.ServerOptions.Port != currentConfig.ServerOptions.Port))
                    {
                        if (!first)
                            lock (Log)
                                Log.Info("Switching from port {0} to port {1}.".Fmt(currentConfig.ServerOptions.Port, newConfig.ServerOptions.Port));
                        if (currentListeningThread != null)
                            currentListeningThread.Abort();
                        int port = newConfig.ServerOptions.Port;
                        currentListeningThread = new Thread(() => ListeningThreadFunction(port));
                        currentListeningThread.Start();
                    }

                    currentConfig = newConfig;
                }

                if (!Directory.Exists(currentConfig.PluginDirectory))
                    try { Directory.CreateDirectory(currentConfig.PluginDirectory); }
                    catch (Exception e)
                    {
                        lock (Log)
                        {
                            Log.Error(e.Message);
                            Log.Error("Directory {0} cannot be created. Make sure the location is writable and try again, or edit the config file to change the path.");
                        }
                        return;
                    }

                // Detect if any DLL file has been added, deleted, renamed, or its date/time has changed
                var newListOfPlugins = new DirectoryInfo(currentConfig.PluginDirectory).GetFiles("*.dll").OrderBy(fi => fi.FullName).Select(fi => new Tuple<string, DateTime>(fi.FullName, fi.LastWriteTimeUtc)).ToArray();
                if (listOfPlugins == null || !listOfPlugins.SequenceEqual(newListOfPlugins))
                {
                    if (listOfPlugins != null)
                        lock (Log)
                            Log.Info(@"Change in plugin directory detected.");
                    mustReinitServer = true;
                    listOfPlugins = newListOfPlugins;
                }

                // Check whether any of the plugins reports that they need to be reinitialised.
                if (!mustReinitServer && ActiveApi != null)
                    mustReinitServer = ActiveApi.MustReinitServer();

                if (mustReinitServer)
                {
                    lock (Log)
                        Log.Info(first ? "Starting Propeller..." : "Restarting Propeller...");

                    // Try to clean up old folders we've created before
                    var tempPath = Path.GetTempPath();
                    foreach (var pth in Directory.GetDirectories(tempPath, "propeller-tmp-*"))
                    {
                        foreach (var file in Directory.GetFiles(pth))
                            try { File.Delete(file); }
                            catch { }
                        try { Directory.Delete(pth); }
                        catch { }
                    }

                    // Find a new folder to put the DLL files into
                    int j = 1;
                    var copyToPath = Path.Combine(tempPath, "propeller-tmp-" + j);
                    while (Directory.Exists(copyToPath))
                    {
                        j++;
                        copyToPath = Path.Combine(tempPath, "propeller-tmp-" + j);
                    }
                    Directory.CreateDirectory(copyToPath);

                    // Copy the DLLs into the new folder and simultaneously create the list of DllInfo objects for them.
                    var dlls = new List<DllInfo>();
                    foreach (var plugin in listOfPlugins)
                    {
                        var dll = new DllInfo
                        {
                            OrigDllPath = plugin.E1,
                            TempDllPath = Path.Combine(copyToPath, Path.GetFileName(plugin.E1))
                        };
                        try
                        {
                            File.Copy(dll.OrigDllPath, dll.TempDllPath);
                        }
                        catch (Exception e)
                        {
                            lock (Log)
                                Log.Error(@"Unable to copy file ""{0}"" to ""{1}"": {2} - Ignoring plugin.".Fmt(dll.OrigDllPath, dll.TempDllPath, e.Message));
                            continue;
                        }
                        dlls.Add(dll);
                    }

                    AppDomain newApiDomain = AppDomain.CreateDomain("Propeller API " + (ApiCount++), null, new AppDomainSetup
                    {
                        ApplicationBase = PathUtil.AppPath,
                        PrivateBinPath = copyToPath,
                    });
                    PropellerApi newApi = (PropellerApi) newApiDomain.CreateInstanceAndUnwrap("PropellerApi", "Propeller.PropellerApi");

                    lock (Log)
                        newApi.Init(currentConfig.ServerOptions, dlls, Log);

                    lock (LockObject)
                    {
                        if (activeApiDomain != null)
                            inactiveDomains.Add(new Tuple<AppDomain, PropellerApi>(activeApiDomain, ActiveApi));
                        activeApiDomain = newApiDomain;
                        ActiveApi = newApi;
                    }

                    lock (Log)
                        Log.Info("Propeller initialisation successful.");
                }

                first = false;

                var newInactiveDomains = new List<Tuple<AppDomain, PropellerApi>>();
                foreach (var entry in inactiveDomains)
                {
                    if (entry.E2.ActiveHandlers() == 0)
                    {
                        entry.E2.Shutdown();
                        AppDomain.Unload(entry.E1);
                    }
                    else
                        newInactiveDomains.Add(entry);
                }
                inactiveDomains = newInactiveDomains;

#if DEBUG
                Thread.Sleep(1000);
#else
                Thread.Sleep(10000);
#endif

                mustReinitServer = false;
            }
        }
    }
}
