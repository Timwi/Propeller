using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using RT.Propeller;
using RT.PropellerApi;
using RT.Util;
using RT.Util.ExtensionMethods;
using RT.Util.Threading;

namespace Propeller
{
    sealed class PropellerEngine : PeriodicMultiple
    {
        private AppDomainInfo _activeAppDomain = null;
        private List<AppDomainInfo> _inactiveAppDomains = new List<AppDomainInfo>();
        private int _appDomainCount = 0;
        private object _lockObject = new object();
        private ListeningThread _currentListeningThread = null;
        private ListeningThread _currentSecureListeningThread = null;
        private PropellerSettings _currentConfig = null;
        private bool _firstRunEver = true;
        private DateTime _configFileChangeTime = DateTime.MinValue;

        protected override TimeSpan FirstInterval { get { return TimeSpan.Zero; } }

        public PropellerEngine()
        {
#if DEBUG
            var checkInterval = TimeSpan.FromSeconds(1);
#else
            var checkInterval = TimeSpan.FromSeconds(10);
#endif

            Tasks = new List<Task>
            {
                new Task() { Action = logHeartbeat, MinInterval = TimeSpan.FromMinutes(5) },
                new Task() { Action = checkAndProcessFileChanges, MinInterval = checkInterval },
            };
        }

        private void logHeartbeat()
        {
            lock (PropellerProgram.Log)
                PropellerProgram.Log.Debug("Heartbeat");
        }

        private static string configPath { get { return SettingsUtil.GetAttribute<PropellerSettings>().GetFileName(); } }

        private void checkAndProcessFileChanges()
        {
            try
            {
                bool mustReinitServer = false;
                bool needNewListener = false;
                bool needNewSecureListener = false;
                var oldPort = _currentConfig.NullOr(cfg => cfg.ServerOptions.Port);
                var oldSecurePort = _currentConfig.NullOr(cfg => cfg.ServerOptions.SecurePort);

                if (_firstRunEver || !File.Exists(configPath) || _currentConfig == null || _configFileChangeTime < File.GetLastWriteTimeUtc(configPath))
                {
                    mustReinitServer = true;

                    // Read configuration file
                    _currentConfig = PropellerStandalone.LoadSettings(PropellerProgram.Log, _firstRunEver);
                    // Determine the file date/time afterwards because a new file may have been created
                    _configFileChangeTime = new FileInfo(configPath).LastWriteTimeUtc;

                    if (_currentConfig.ServerOptions.SecurePort != null && (_currentConfig.ServerOptions.CertificatePath == null || !File.Exists(_currentConfig.ServerOptions.CertificatePath)))
                    {
                        lock (PropellerProgram.Log)
                            PropellerProgram.Log.Error("Cannot use HTTPS without a certificate. Set CertificatePath in the configuration to the path and filename of a valid X509 certificate.");
                        _currentConfig.ServerOptions.SecurePort = null;
                    }

                    // If port number is different from previous port number, create a new listening thread and kill the old one
                    needNewListener = _firstRunEver ? (_currentConfig.ServerOptions.Port != null) : (_currentConfig.ServerOptions.Port != oldPort);
                    needNewSecureListener = _firstRunEver ? (_currentConfig.ServerOptions.SecurePort != null) : (_currentConfig.ServerOptions.SecurePort != oldSecurePort);
                }

                if (!Directory.Exists(_currentConfig.PluginDirectoryExpanded))
                {
                    try { Directory.CreateDirectory(_currentConfig.PluginDirectoryExpanded); }
                    catch (Exception e)
                    {
                        lock (PropellerProgram.Log)
                        {
                            PropellerProgram.Log.Error(e.Message);
                            PropellerProgram.Log.Error("Directory {0} cannot be created. Make sure the location is writable and try again, or edit the config file to change the path.".Fmt(_currentConfig.PluginDirectoryExpanded));
                        }
                        return;
                    }
                }

                // Check whether any of the plugins reports that they need to be reinitialised.
                if (!mustReinitServer && _activeAppDomain.Runner != null)
                    mustReinitServer = _activeAppDomain.Runner.MustReinitServer();

                if (mustReinitServer)
                {
                    var result = reinitServer();
                    if (!result)
                    {
                        lock (PropellerProgram.Log)
                            PropellerProgram.Log.Error("Server initialization failed.");
                        if (_firstRunEver)
                            throw new PropellerInitializationFailedException();
                        return;
                    }

                    if (needNewListener)
                        _currentListeningThread = switchListener(this, "HTTP", _firstRunEver, oldPort, _currentConfig.ServerOptions.Port, _currentListeningThread, false);
                    if (needNewSecureListener)
                        _currentSecureListeningThread = switchListener(this, "HTTPS", _firstRunEver, oldSecurePort, _currentConfig.ServerOptions.SecurePort, _currentSecureListeningThread, true);
                }

                _firstRunEver = false;

                var newInactiveDomains = new List<AppDomainInfo>();
                foreach (var entry in _inactiveAppDomains)
                {
                    if (entry.Runner.ActiveHandlers == 0)
                    {
                        entry.Runner.Shutdown();
                        AppDomain.Unload(entry.AppDomain);
                    }
                    else
                        newInactiveDomains.Add(entry);
                }
                _inactiveAppDomains = newInactiveDomains;
            }
            catch (PropellerInitializationFailedException)
            {
                throw;
            }
            catch (Exception e)
            {
                PropellerStandalone.LogException(PropellerProgram.Log, e, "Propeller", "checkAndProcessFileChanges()");
            }
            finally
            {
                // A module may rewrite its own config file during initialisation. If initialisation failed, the FileSystemWatcher would
                // indicate this again as a config file change and immediately retrigger reinitialization. Avoid that.
                if (_activeAppDomain != null)
                    _activeAppDomain.Runner.ResetFileSystemWatchers();
            }
        }

        private static ListeningThread switchListener(PropellerEngine super, string protocol, bool firstRunEver, int? oldPort, int? newPort, ListeningThread prevListeningThread, bool secure)
        {
            lock (PropellerProgram.Log)
            {
                if (firstRunEver || oldPort == null)
                    PropellerProgram.Log.Info("Enabling {1} on port {0}.".Fmt(newPort, protocol));
                else if (newPort != null)
                    PropellerProgram.Log.Info("Switching {2} from port {0} to port {1}.".Fmt(oldPort, newPort, protocol));
                else if (newPort == null)
                    PropellerProgram.Log.Info("Disabling {1} on port {0}.".Fmt(oldPort, protocol));
            }
            if (prevListeningThread != null)
                prevListeningThread.RequestExit();
            return newPort.NullOr(port => new ListeningThread(super, port, secure));
        }

        private bool reinitServer()
        {
            lock (PropellerProgram.Log)
                PropellerProgram.Log.Info(_firstRunEver ? "Starting Propeller..." : "Restarting Propeller...");

            // Try to clean up old folders we've created before
            var tempPath = _currentConfig.TempDirectory ?? Path.GetTempPath();
            Directory.CreateDirectory(tempPath);
            foreach (var pth in Directory.GetDirectories(tempPath, "propeller-tmp-*"))
            {
                try { Directory.Delete(pth, true); }
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

            AppDomain newAppDomain = AppDomain.CreateDomain("Propeller AppDomainRunner " + (_appDomainCount++), null, new AppDomainSetup
            {
                ApplicationBase = PathUtil.AppPath,
                PrivateBinPath = copyToPath,
            });
            AppDomainRunner newRunner = (AppDomainRunner) newAppDomain.CreateInstanceAndUnwrap("Propeller", "Propeller.AppDomainRunner");

            PropellerProgram.Log = PropellerStandalone.GetLogger(_currentConfig);

            lock (PropellerProgram.Log)
            {
                try
                {
                    var result = newRunner.Init(_currentConfig.ServerOptions, _currentConfig.PluginDirectoryExpanded, copyToPath, PropellerProgram.Log, _currentConfig.HttpAccessLogFile, _currentConfig.HttpAccessLogToConsole, _currentConfig.HttpAccessLogVerbosity);

                    // If Init() returns false, then it has already logged an exception.
                    if (!result)
                    {
                        AppDomain.Unload(newAppDomain);
                        return false;
                    }
                }
                catch (Exception e)
                {
                    PropellerStandalone.LogException(PropellerProgram.Log, e, "Propeller", "AppDomainRunner.Init()");
                    AppDomain.Unload(newAppDomain);
                    return false;
                }
            }

            // Initialization of the new AppDomain was successful, so switch over!
            lock (_lockObject)
            {
                if (_activeAppDomain != null)
                    _inactiveAppDomains.Add(_activeAppDomain);
                _activeAppDomain = new AppDomainInfo { AppDomain = newAppDomain, Runner = newRunner };
            }

            lock (PropellerProgram.Log)
                PropellerProgram.Log.Info("Propeller initialisation successful.");
            return true;
        }

        public override bool Shutdown(bool waitForExit)
        {
            if (_currentListeningThread != null)
            {
                _currentListeningThread.RequestExit();
                if (waitForExit)
                    _currentListeningThread.WaitExited();
            }

            return base.Shutdown(waitForExit);
        }

        private sealed class ListeningThread
        {
            private Thread _listeningThread;
            private PropellerEngine _super;
            private int _port;
            private bool _secure;
            private CancellationTokenSource _cancel = new CancellationTokenSource();
            private ManualResetEventSlim _exited = new ManualResetEventSlim();

            public ListeningThread(PropellerEngine super, int port, bool secure)
            {
                _super = super;
                _port = port;
                _secure = secure;
                _listeningThread = new Thread(listeningThreadFunction);
                _listeningThread.Start();
            }

            public void WaitExited()
            {
                _exited.Wait();
            }

            public void RequestExit()
            {
                _cancel.Cancel();
            }

            private void listeningThreadFunction()
            {
                TcpListener listener = new TcpListener(IPAddress.Any, _port);
                try
                {
                    listener.Start();
                }
                catch (SocketException e)
                {
                    lock (PropellerProgram.Log)
                        PropellerProgram.Log.Error("Cannot bind to port {0}: {1}".Fmt(_port, e.Message));
                    _exited.Set();
                    return;
                }
                try
                {
                    while (!_cancel.IsCancellationRequested)
                    {
                        if (listener.Pending())
                            listener.BeginAcceptSocket(acceptConnection, listener);
                        else
                            Thread.Sleep(1);
                    }
                }
                finally
                {
                    lock (PropellerProgram.Log)
                        PropellerProgram.Log.Info("Stop listening on port " + _port);
                    try { listener.Stop(); }
                    catch { }
                    _exited.Set();
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
                            _super._activeAppDomain.Runner.HandleRequest(sck.DuplicateAndClose(Process.GetCurrentProcess().Id), _secure);
                        else
                            sck.Close();
                    }
                    catch (Exception e)
                    {
                        PropellerStandalone.LogException(PropellerProgram.Log, e, "Propeller", "HandleRequest()");
                    }
                }
            }
        }
    }
}
