using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using RT.Util;
using RT.Util.Collections;
using RT.Util.ExtensionMethods;
using RT.Util.Xml;

namespace Propeller
{
    class PropellerEngine : Periodic
    {
        private CrossAppDomainApi _activeApi = null;
        private int _apiCount = 0;
        private object _lockObject = new object();
        private List<Tuple<AppDomain, CrossAppDomainApi>> inactiveDomains = new List<Tuple<AppDomain, CrossAppDomainApi>>();
        private DateTime configFileChangeTime = DateTime.MinValue;
        private ListeningThread currentListeningThread = null;
        private PropellerConfig currentConfig = null;
        private bool first = true;
        private Tuple<string, DateTime>[] listOfPlugins = null;
        private AppDomain activeApiDomain = null;

        protected override TimeSpan FirstInterval { get { return TimeSpan.Zero; } }
#if DEBUG
        protected override TimeSpan SubsequentInterval { get { return TimeSpan.FromSeconds(1); } }
#else
            protected override TimeSpan SubsequentInterval { get { return TimeSpan.FromSeconds(10); } }
#endif

        protected override void PeriodicActivity()
        {
            bool mustReinitServer = false;

            if (first || !File.Exists(Program.ConfigPath) || currentConfig == null || configFileChangeTime < File.GetLastWriteTimeUtc(Program.ConfigPath))
            {
                mustReinitServer = true;
                refreshConfig();
            }

            if (!Directory.Exists(currentConfig.PluginDirectoryExpanded))
                try { Directory.CreateDirectory(currentConfig.PluginDirectoryExpanded); }
                catch (Exception e)
                {
                    lock (Program.Log)
                    {
                        Program.Log.Error(e.Message);
                        Program.Log.Error("Directory {0} cannot be created. Make sure the location is writable and try again, or edit the config file to change the path.".Fmt(currentConfig.PluginDirectoryExpanded));
                    }
                    Program.Service.Shutdown();
                    return;
                }

            // Detect if any DLL file has been added, deleted, renamed, or its date/time has changed
            var newListOfPlugins = new DirectoryInfo(currentConfig.PluginDirectoryExpanded).GetFiles("*.dll").OrderBy(fi => fi.FullName).Select(fi => new Tuple<string, DateTime>(fi.FullName, fi.LastWriteTimeUtc)).ToArray();
            if (listOfPlugins == null || !listOfPlugins.SequenceEqual(newListOfPlugins))
            {
                if (listOfPlugins != null)
                    lock (Program.Log)
                        Program.Log.Info(@"Change in plugin directory detected.");
                mustReinitServer = true;
                listOfPlugins = newListOfPlugins;
            }

            // Check whether any of the plugins reports that they need to be reinitialised.
            if (!mustReinitServer && _activeApi != null)
                mustReinitServer = _activeApi.MustReinitServer();

            if (mustReinitServer)
                reinitServer();

            first = false;

            var newInactiveDomains = new List<Tuple<AppDomain, CrossAppDomainApi>>();
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
        }

        private void refreshConfig()
        {
            // Read configuration file
            configFileChangeTime = new FileInfo(Program.ConfigPath).LastWriteTimeUtc;
            lock (Program.Log)
                Program.Log.Info((first ? "Loading config file: " : "Reloading config file: ") + Program.ConfigPath);
            PropellerConfig newConfig;
            try
            {
                newConfig = XmlClassify.LoadObjectFromXmlFile<PropellerConfig>(Program.ConfigPath);
            }
            catch
            {
                lock (Program.Log)
                    Program.Log.Warn("Config file could not be loaded; using default config.");
                newConfig = new PropellerConfig();
                if (!File.Exists(Program.ConfigPath))
                {
                    try
                    {
                        XmlClassify.SaveObjectToXmlFile(newConfig, Program.ConfigPath);
                        lock (Program.Log)
                            Program.Log.Info("Default config saved to {0}.".Fmt(Program.ConfigPath));
                        configFileChangeTime = new FileInfo(Program.ConfigPath).LastWriteTimeUtc;
                    }
                    catch (Exception e)
                    {
                        lock (Program.Log)
                            Program.Log.Warn("Attempt to save default config to {0} failed: {1}".Fmt(Program.ConfigPath, e.Message));
                    }
                }
            }

            // If port number is different from previous port number, create a new listening thread and kill the old one
            if (first || currentConfig == null || (newConfig.ServerOptions.Port != currentConfig.ServerOptions.Port))
            {
                if (!first)
                    lock (Program.Log)
                        Program.Log.Info("Switching from port {0} to port {1}.".Fmt(currentConfig.ServerOptions.Port, newConfig.ServerOptions.Port));
                if (currentListeningThread != null)
                    currentListeningThread.ShouldExit = true;
                currentListeningThread = new ListeningThread(this, newConfig.ServerOptions.Port);
            }

            currentConfig = newConfig;
        }

        private void reinitServer()
        {
            lock (Program.Log)
                Program.Log.Info(first ? "Starting Propeller..." : "Restarting Propeller...");

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
                    lock (Program.Log)
                        Program.Log.Error(@"Unable to copy file ""{0}"" to ""{1}"": {2} - Ignoring plugin.".Fmt(dll.OrigDllPath, dll.TempDllPath, e.Message));
                    continue;
                }
                dlls.Add(dll);
            }

            AppDomain newApiDomain = AppDomain.CreateDomain("Propeller API " + (_apiCount++), null, new AppDomainSetup
            {
                ApplicationBase = PathUtil.AppPath,
                PrivateBinPath = copyToPath,
            });
            CrossAppDomainApi newApi = (CrossAppDomainApi) newApiDomain.CreateInstanceAndUnwrap("Propeller", "Propeller.CrossAppDomainApi");

            lock (Program.Log)
                newApi.Init(currentConfig.ServerOptions, dlls, Program.Log);

            lock (_lockObject)
            {
                if (activeApiDomain != null)
                    inactiveDomains.Add(new Tuple<AppDomain, CrossAppDomainApi>(activeApiDomain, _activeApi));
                activeApiDomain = newApiDomain;
                _activeApi = newApi;
            }

            lock (Program.Log)
                Program.Log.Info("Propeller initialisation successful.");
        }

        public override bool Shutdown(bool waitForExit)
        {
            if (currentListeningThread != null)
            {
                currentListeningThread.ShouldExit = true;
                if (waitForExit)
                    currentListeningThread.WaitExited();
            }

            return base.Shutdown(waitForExit);
        }

        private class ListeningThread : ThreadExiter
        {
            private Thread _listeningThread;
            private PropellerEngine _super;
            private int _port;

            public ListeningThread(PropellerEngine super, int port)
            {
                _super = super;
                _port = port;
                _listeningThread = new Thread(listeningThreadFunction);
                _listeningThread.Start();
            }

            private void listeningThreadFunction()
            {
                lock (Program.Log)
                    Program.Log.Info("Start listening on port " + _port);
                TcpListener listener = new TcpListener(IPAddress.Any, _port);
                try
                {
                    listener.Start();
                }
                catch (SocketException e)
                {
                    lock (Program.Log)
                        Program.Log.Error("Cannot bind to port {0}: {1}".Fmt(_port, e.Message));
                    SignalExited();
                    return;
                }
                try
                {
                    while (!ShouldExit)
                    {
                        if (listener.Pending())
                            listener.BeginAcceptSocket(acceptConnection, listener);
                        else
                            Thread.Sleep(1);
                    }
                }
                finally
                {
                    lock (Program.Log)
                        Program.Log.Info("Stop listening on port " + _port);
                    try { listener.Stop(); }
                    catch { }
                    SignalExited();
                }
            }

            private void acceptConnection(IAsyncResult res)
            {
                var listener = (TcpListener) res.AsyncState;
                Socket sck = listener.EndAcceptSocket(res);
                lock (_super._lockObject)
                {
                    try
                    {
                        if (_super._activeApi != null)
                            // This creates a new thread for handling the connection and returns pretty immediately.
                            _super._activeApi.HandleRequest(sck.DuplicateAndClose(Process.GetCurrentProcess().Id));
                        else
                            sck.Close();
                    }
                    catch { }
                }
            }
        }
    }
}
