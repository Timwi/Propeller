using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using Propeller;
using RT.Services;
using RT.Util;
using RT.Util.Collections;
using RT.Util.ExtensionMethods;
using RT.Util.Xml;

namespace RT.Propeller
{
    class PropellerService : SelfService
    {
        private static SingleSelfServiceProcess<PropellerService> _serviceProcess = new SingleSelfServiceProcess<PropellerService>();

        public static SelfServiceProcess ServiceProcess { get { return _serviceProcess; } }

        public static void RunServiceStandalone(string[] args)
        {
            var service = new PropellerService();
            if (!service.doStart(true))
                return;

            while (service.IsRunning)
                Thread.Sleep(10000);

            service.doStop();
        }

        public PropellerService()
        {
            ServiceName = "PropellerService";
            ServiceDisplayName = "PropellerService";
            ServiceDescription = "Provides powerful, flexible HTTP-based functionality on global enterprise systems by leveraging dynamic API synergy through an extensible architecture.";
            ServiceStartMode = ServiceStartMode.Manual;
        }

        private bool _isRunning = false;
        public bool IsRunning { get { return _isRunning && _mainThread.IsRunning; } }

        protected override void OnStart(string[] args)
        {
            doStart(false);
        }

        protected override void OnStop()
        {
            doStop();
        }

        private Mutex _mutex;
        private MainThread _mainThread;

        private bool doStart(bool standalone)
        {
            Console.WriteLine("Starting PropellerService...");

            _mutex = new Mutex(true, "PropellerService_1fb777c3b9e2369f5509");
            if (!_mutex.WaitOne(0, false))
            {
                if (!standalone)
                    StopSelf();
                return false;
            }

            _mainThread = new MainThread();
            _mainThread.Start();
            _isRunning = true;
            return true;
        }

        private void doStop()
        {
            if (_mainThread != null)
            {
                _mainThread.Shutdown();
                _mainThread = null;
            }
            _isRunning = false;
        }

        class MainThread : Periodic
        {
            private PropellerApi _activeApi = null;
            private int _apiCount = 0;
            private object _lockObject = new object();
            private LoggerBase _log = new ConsoleLogger { TimestampInUTC = true };
            private List<Tuple<AppDomain, PropellerApi>> inactiveDomains = new List<Tuple<AppDomain, PropellerApi>>();
            private string configPath = Path.Combine(PathUtil.AppPath, @"Propeller.config.xml");
            private DateTime configFileChangeTime = DateTime.MinValue;
            private ListeningThread currentListeningThread = null;
            private bool mustReinitServer = false;
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
                if (first || !File.Exists(configPath) || currentConfig == null || configFileChangeTime < new FileInfo(configPath).LastWriteTimeUtc)
                    refreshConfig();

                if (!Directory.Exists(currentConfig.PluginDirectory))
                    try { Directory.CreateDirectory(currentConfig.PluginDirectory); }
                    catch (Exception e)
                    {
                        lock (_log)
                        {
                            _log.Error(e.Message);
                            _log.Error("Directory {0} cannot be created. Make sure the location is writable and try again, or edit the config file to change the path.");
                        }
                        return;
                    }

                // Detect if any DLL file has been added, deleted, renamed, or its date/time has changed
                var newListOfPlugins = new DirectoryInfo(currentConfig.PluginDirectory).GetFiles("*.dll").OrderBy(fi => fi.FullName).Select(fi => new Tuple<string, DateTime>(fi.FullName, fi.LastWriteTimeUtc)).ToArray();
                if (listOfPlugins == null || !listOfPlugins.SequenceEqual(newListOfPlugins))
                {
                    if (listOfPlugins != null)
                        lock (_log)
                            _log.Info(@"Change in plugin directory detected.");
                    mustReinitServer = true;
                    listOfPlugins = newListOfPlugins;
                }

                // Check whether any of the plugins reports that they need to be reinitialised.
                if (!mustReinitServer && _activeApi != null)
                    mustReinitServer = _activeApi.MustReinitServer();

                if (mustReinitServer)
                    reinitServer();

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

                mustReinitServer = false;
            }

            private void refreshConfig()
            {
                // Read configuration file
                mustReinitServer = true;
                configFileChangeTime = new FileInfo(configPath).LastWriteTimeUtc;
                lock (_log)
                    _log.Info((first ? "Loading config file: " : "Reloading config file: ") + configPath);
                PropellerConfig newConfig;
                try
                {
                    newConfig = XmlClassify.LoadObjectFromXmlFile<PropellerConfig>(configPath);
                }
                catch
                {
                    lock (_log)
                        _log.Warn("Config file could not be loaded; using default config.");
                    newConfig = new PropellerConfig();
                    if (!File.Exists(configPath))
                    {
                        try
                        {
                            XmlClassify.SaveObjectToXmlFile(newConfig, configPath);
                            lock (_log)
                                _log.Info("Default config saved to {0}.".Fmt(configPath));
                            configFileChangeTime = new FileInfo(configPath).LastWriteTimeUtc;
                        }
                        catch (Exception e)
                        {
                            lock (_log)
                                _log.Warn("Attempt to save default config to {0} failed: {1}".Fmt(configPath, e.Message));
                        }
                    }
                }

                // If port number is different from previous port number, create a new listening thread and kill the old one
                if (first || currentConfig == null || (newConfig.ServerOptions.Port != currentConfig.ServerOptions.Port))
                {
                    if (!first)
                        lock (_log)
                            _log.Info("Switching from port {0} to port {1}.".Fmt(currentConfig.ServerOptions.Port, newConfig.ServerOptions.Port));
                    if (currentListeningThread != null)
                        currentListeningThread.ShouldExit = true;
                    currentListeningThread = new ListeningThread(this, newConfig.ServerOptions.Port);
                }

                currentConfig = newConfig;
            }

            private void reinitServer()
            {
                lock (_log)
                    _log.Info(first ? "Starting Propeller..." : "Restarting Propeller...");

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
                        lock (_log)
                            _log.Error(@"Unable to copy file ""{0}"" to ""{1}"": {2} - Ignoring plugin.".Fmt(dll.OrigDllPath, dll.TempDllPath, e.Message));
                        continue;
                    }
                    dlls.Add(dll);
                }

                AppDomain newApiDomain = AppDomain.CreateDomain("Propeller API " + (_apiCount++), null, new AppDomainSetup
                {
                    ApplicationBase = PathUtil.AppPath,
                    PrivateBinPath = copyToPath,
                });
                PropellerApi newApi = (PropellerApi) newApiDomain.CreateInstanceAndUnwrap("PropellerApi", "RT.Propeller.PropellerApi");

                lock (_log)
                    newApi.Init(currentConfig.ServerOptions, dlls, _log);

                lock (_lockObject)
                {
                    if (activeApiDomain != null)
                        inactiveDomains.Add(new Tuple<AppDomain, PropellerApi>(activeApiDomain, _activeApi));
                    activeApiDomain = newApiDomain;
                    _activeApi = newApi;
                }

                lock (_log)
                    _log.Info("Propeller initialisation successful.");
            }

            public override bool Shutdown()
            {
                if (currentListeningThread != null)
                {
                    currentListeningThread.ShouldExit = true;
                    currentListeningThread.WaitExited();
                }

                return base.Shutdown();
            }

            private class ListeningThread : ThreadExiter
            {
                private Thread _listeningThread;
                private MainThread _super;
                private int _port;

                public ListeningThread(MainThread super, int port)
                {
                    _super = super;
                    _port = port;
                    _listeningThread = new Thread(listeningThreadFunction);
                    _listeningThread.Start();
                }

                private void listeningThreadFunction()
                {
                    lock (_super._log)
                        _super._log.Info("Start listening on port " + _port);
                    TcpListener listener = new TcpListener(IPAddress.Any, _port);
                    try
                    {
                        listener.Start();
                    }
                    catch (SocketException e)
                    {
                        lock (_super._log)
                            _super._log.Error("Cannot bind to port {0}: {1}".Fmt(_port, e.Message));
                        SignalExited();
                        return;
                    }
                    try
                    {
                        while (!ShouldExit)
                        {
                            Socket sck = listener.AcceptSocket();
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
                    finally
                    {
                        lock (_super._log)
                            _super._log.Info("Stop listening on port " + _port);
                        try { listener.Stop(); }
                        catch { }
                        SignalExited();
                    }
                }
            }
        }
    }
}
