using System;
using RT.Util.ExtensionMethods;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RT.Servers;
using System.IO;
using RT.Util;
using System.Threading;
using System.Reflection;
using RT.Util.Collections;
using System.Net.Sockets;
using System.Net;
using System.Diagnostics;
using RT.Util.Xml;

namespace Propeller
{
    public class PropellerConfig
    {
        public HttpServerOptions ServerOptions = new HttpServerOptions();
        public string PluginDirectory = Path.Combine(PathUtil.AppPath, "plugins");
        public Dictionary<string, string> PluginConfigs = new Dictionary<string, string>();
    }

    public class Program
    {
        static AppDomain ActiveAPIDomain = null;
        static PropellerAPI ActiveAPI = null;
        static int APICount = 0;
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
                            if (ActiveAPI != null)
                                ActiveAPI.HandleRequest(sck.DuplicateAndClose(Process.GetCurrentProcess().Id));
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
            var inactiveDomains = new List<Tuple<AppDomain, PropellerAPI>>();
            var fileChangeTime = new Dictionary<string, DateTime>();
            var checkFolders = new List<string>();
            var checkFoldersFilesFound = new Dictionary<string, bool>();
            var configPath = Path.Combine(PathUtil.AppPath, @"Propeller.config.xml");
            Thread currentListeningThread = null;
            int currentPort = -1;
            var mustReinitServer = false;
            PropellerConfig currentConfig = null;

            while (true)
            {
                var cfgFi = new FileInfo(configPath);
                if (!File.Exists(configPath) || !fileChangeTime.ContainsKey(cfgFi.FullName) || fileChangeTime[cfgFi.FullName] < cfgFi.LastWriteTimeUtc)
                {
                    mustReinitServer = true;
                    fileChangeTime[cfgFi.FullName] = cfgFi.LastWriteTimeUtc;
                    lock (Log)
                        Log.Info("Reloading config file: " + configPath);
                    PropellerConfig cfg;
                    try
                    {
                        cfg = XmlClassify.LoadObjectFromXmlFile<PropellerConfig>(configPath);
                    }
                    catch
                    {
                        lock (Log)
                            Log.Warn("Config file could not be loaded; using default config.");
                        cfg = new PropellerConfig();
                        if (!File.Exists(configPath))
                        {
                            try
                            {
                                XmlClassify.SaveObjectToXmlFile(cfg, configPath);
                                lock (Log)
                                    Log.Info("Default config saved to {0}.".Fmt(configPath));
                                var fi = new FileInfo(configPath);
                                fileChangeTime[fi.FullName] = fi.LastWriteTimeUtc;
                            }
                            catch (Exception e)
                            {
                                lock (Log)
                                    Log.Warn("Attempt to save default config to {0} failed: {1}".Fmt(configPath, e.Message));
                            }
                        }
                    }
                    if (cfg.ServerOptions.Port != currentPort)
                    {
                        if (currentListeningThread != null)
                            currentListeningThread.Abort();
                        int port = currentPort = cfg.ServerOptions.Port;
                        currentListeningThread = new Thread(() => ListeningThreadFunction(port));
                        currentListeningThread.Start();
                    }
                    currentConfig = cfg;
                }

                var plugins = new List<FileInfo>();

                if (!Directory.Exists(currentConfig.PluginDirectory))
                    try { Directory.CreateDirectory(currentConfig.PluginDirectory); }
                    catch (Exception e)
                    {
                        lock (Log)
                        {
                            Log.Error(e.Message);
                            Log.Error("Directory {0} cannot be created. Make sure the location is writable and try again.");
                        }
                        return;
                    }

                foreach (var fi in new DirectoryInfo(currentConfig.PluginDirectory).GetFiles("*.dll"))
                {
                    if (!fileChangeTime.ContainsKey(fi.FullName))
                    {
                        mustReinitServer = true;
                        fileChangeTime[fi.FullName] = fi.LastWriteTimeUtc;
                        lock (Log)
                            Log.Info("New plugin detected: " + fi.FullName);
                    }
                    plugins.Add(fi);
                }

                // Check if any of the monitored files have changed or been removed
                foreach (var key in fileChangeTime.Keys.ToArray())
                {
                    FileInfo fi;
                    if (!File.Exists(key))
                    {
                        fileChangeTime.Remove(key);
                        mustReinitServer = true;
                        lock (Log)
                            Log.Info("File removed: " + key);
                    }
                    else if (fileChangeTime[key] < (fi = new FileInfo(key)).LastWriteTimeUtc)
                    {
                        mustReinitServer = true;
                        fileChangeTime[key] = fi.LastWriteTimeUtc;
                        lock (Log)
                            Log.Info("File changed: " + key);
                    }
                }

                // Check if there are any new files in the folders to be monitored
                foreach (var dir in checkFolders.Where(d => Directory.Exists(d) && Directory.GetFiles(d).Any(f => !checkFoldersFilesFound.ContainsKey(f))).Take(1))
                {
                    lock (Log)
                        Log.Info("New file detected in folder: " + dir);
                    mustReinitServer = true;
                }

                // Check if any of the files in the folders to be monitored have been deleted
                foreach (var file in checkFoldersFilesFound.Keys.Where(f => !File.Exists(f)).Take(1))
                {
                    lock (Log)
                        Log.Info("File got deleted: " + file);
                    mustReinitServer = true;
                }

                if (mustReinitServer)
                {
                    lock (Log)
                        Log.Info("Initialising Propeller...");
                    fileChangeTime = plugins.ToDictionary(fi => fi.FullName, fi => fi.LastWriteTimeUtc);
                    fileChangeTime[configPath] = new FileInfo(configPath).LastWriteTimeUtc;
                    checkFolders = new List<string>();
                    checkFoldersFilesFound = new Dictionary<string, bool>();

                    AppDomain newAPIDomain;
                    PropellerAPI newAPI;

                    newAPIDomain = AppDomain.CreateDomain("Propeller API " + (APICount++), null, new AppDomainSetup
                    {
                        ApplicationBase = PathUtil.AppPath,
                        PrivateBinPath = currentConfig.PluginDirectory,
                        CachePath = Path.Combine(currentConfig.PluginDirectory, "cache"),
                        ShadowCopyFiles = "true",
                        ShadowCopyDirectories = currentConfig.PluginDirectory
                    });
                    var objRaw = newAPIDomain.CreateInstanceAndUnwrap("PropellerAPI", "Propeller.PropellerAPI");
                    newAPI = (PropellerAPI) objRaw;
                    lock (Log)
                    {
                        var result = newAPI.Init(currentConfig.ServerOptions, plugins, Log, currentConfig.PluginConfigs);
                        if (result != null)
                        {
                            if (result.FilesToMonitor != null)
                                foreach (var ftm in result.FilesToMonitor.Where(f => File.Exists(f)).Select(f => new FileInfo(f)))
                                    if (!fileChangeTime.ContainsKey(ftm.FullName))
                                        fileChangeTime.Add(ftm.FullName, ftm.LastWriteTimeUtc);
                            if (result.FoldersToMonitor != null)
                                foreach (var ftm in result.FoldersToMonitor)
                                {
                                    checkFolders.Add(ftm);
                                    if (Directory.Exists(ftm))
                                        foreach (var ff in new DirectoryInfo(ftm).GetFiles().Select(f => f.FullName))
                                            checkFoldersFilesFound[ff] = true;
                                }
                        }
                    }
                    lock (LockObject)
                    {
                        if (ActiveAPIDomain != null)
                            inactiveDomains.Add(new Tuple<AppDomain, PropellerAPI>(ActiveAPIDomain, ActiveAPI));
                        ActiveAPIDomain = newAPIDomain;
                        ActiveAPI = newAPI;
                    }

                    lock (Log)
                        Log.Info("Propeller initialisation successful.");
                }

                var newInactiveDomains = new List<Tuple<AppDomain, PropellerAPI>>();
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
