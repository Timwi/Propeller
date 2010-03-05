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
    class AppDomainInfo
    {
        public AppDomain AppDomain;
        public AppDomainRunner Runner;
    }

    class PropellerEngine : Periodic
    {
        private AppDomainInfo _activeAppDomain = null;
        private List<AppDomainInfo> _inactiveAppDomains = new List<AppDomainInfo>();
        private int _apiCount = 0;
        private object _lockObject = new object();
        private ListeningThread _currentListeningThread = null;
        private PropellerConfig _currentConfig = null;
        private bool _firstRunEver = true;
        private DateTime _configFileChangeTime = DateTime.MinValue;

        protected override TimeSpan FirstInterval { get { return TimeSpan.Zero; } }
#if DEBUG
        protected override TimeSpan SubsequentInterval { get { return TimeSpan.FromSeconds(1); } }
#else
            protected override TimeSpan SubsequentInterval { get { return TimeSpan.FromSeconds(10); } }
#endif

        protected override void PeriodicActivity()
        {
            bool mustReinitServer = false;

            if (_firstRunEver || !File.Exists(Program.ConfigPath) || _currentConfig == null || _configFileChangeTime < File.GetLastWriteTimeUtc(Program.ConfigPath))
            {
                mustReinitServer = true;
                refreshConfig();
            }

            if (!Directory.Exists(_currentConfig.PluginDirectoryExpanded))
            {
                try { Directory.CreateDirectory(_currentConfig.PluginDirectoryExpanded); }
                catch (Exception e)
                {
                    lock (Program.Log)
                    {
                        Program.Log.Error(e.Message);
                        Program.Log.Error("Directory {0} cannot be created. Make sure the location is writable and try again, or edit the config file to change the path.".Fmt(_currentConfig.PluginDirectoryExpanded));
                    }
                    Program.Service.Shutdown();
                    return;
                }
            }

            // Check whether any of the plugins reports that they need to be reinitialised.
            if (!mustReinitServer && _activeAppDomain.Runner != null)
                mustReinitServer = _activeAppDomain.Runner.MustReinitServer();

            if (mustReinitServer)
                reinitServer();

            _firstRunEver = false;

            var newInactiveDomains = new List<AppDomainInfo>();
            foreach (var entry in _inactiveAppDomains)
            {
                if (entry.Runner.ActiveHandlers() == 0)
                {
                    entry.Runner.Shutdown();
                    AppDomain.Unload(entry.AppDomain);
                }
                else
                    newInactiveDomains.Add(entry);
            }
            _inactiveAppDomains = newInactiveDomains;
        }

        private void refreshConfig()
        {
            // Read configuration file
            _configFileChangeTime = new FileInfo(Program.ConfigPath).LastWriteTimeUtc;
            lock (Program.Log)
                Program.Log.Info((_firstRunEver ? "Loading config file: " : "Reloading config file: ") + Program.ConfigPath);
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
                        _configFileChangeTime = new FileInfo(Program.ConfigPath).LastWriteTimeUtc;
                    }
                    catch (Exception e)
                    {
                        lock (Program.Log)
                            Program.Log.Warn("Attempt to save default config to {0} failed: {1}".Fmt(Program.ConfigPath, e.Message));
                    }
                }
            }

            // If port number is different from previous port number, create a new listening thread and kill the old one
            if (_firstRunEver || _currentConfig == null || (newConfig.ServerOptions.Port != _currentConfig.ServerOptions.Port))
            {
                if (!_firstRunEver)
                    lock (Program.Log)
                        Program.Log.Info("Switching from port {0} to port {1}.".Fmt(_currentConfig.ServerOptions.Port, newConfig.ServerOptions.Port));
                if (_currentListeningThread != null)
                    _currentListeningThread.ShouldExit = true;
                _currentListeningThread = new ListeningThread(this, newConfig.ServerOptions.Port);
            }

            _currentConfig = newConfig;
        }

        private void reinitServer()
        {
            lock (Program.Log)
                Program.Log.Info(_firstRunEver ? "Starting Propeller..." : "Restarting Propeller...");

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

            AppDomain newAppDomain = AppDomain.CreateDomain("Propeller AppDomainRunner " + (_apiCount++), null, new AppDomainSetup
            {
                ApplicationBase = PathUtil.AppPath,
                PrivateBinPath = copyToPath,
            });
            AppDomainRunner newRunner = (AppDomainRunner) newAppDomain.CreateInstanceAndUnwrap("Propeller", "Propeller.AppDomainRunner");

            lock (Program.Log)
                newRunner.Init(_currentConfig.ServerOptions, _currentConfig.PluginDirectoryExpanded, copyToPath, Program.Log);

            lock (_lockObject)
            {
                if (_activeAppDomain != null)
                    _inactiveAppDomains.Add(_activeAppDomain);
                _activeAppDomain = new AppDomainInfo { AppDomain = newAppDomain, Runner = newRunner };
            }

            lock (Program.Log)
                Program.Log.Info("Propeller initialisation successful.");
        }

        public override bool Shutdown(bool waitForExit)
        {
            if (_currentListeningThread != null)
            {
                _currentListeningThread.ShouldExit = true;
                if (waitForExit)
                    _currentListeningThread.WaitExited();
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
                        if (_super._activeAppDomain.Runner != null)
                            // This creates a new thread for handling the connection and returns pretty immediately.
                            _super._activeAppDomain.Runner.HandleRequest(sck.DuplicateAndClose(Process.GetCurrentProcess().Id));
                        else
                            sck.Close();
                    }
                    catch { }
                }
            }
        }
    }
}
