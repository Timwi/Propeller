using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using RT.Propeller;
using RT.PropellerApi;
using RT.Servers;
using RT.Util;
using RT.Util.ExtensionMethods;
using RT.Util.Threading;

namespace RT.Propeller
{
    sealed class PropellerEngine : PeriodicMultiple
    {
        public static int AppDomainCount = 0;    // only used to give each AppDomain a unique name

        public PropellerSettings CurrentSettings { get; private set; }

        private object _lockObject = new object();
        private string _settingsPath;
        private bool _settingsSavedByModule = false;
        private DateTime _settingsLastChangedTime = DateTime.MinValue;
        private HttpServer _server;
        private LoggerBase _log;
        private UrlResolver _resolver;
        private HashSet<AppDomainInfo> _activeAppDomains = new HashSet<AppDomainInfo>();
        private HashSet<AppDomainInfo> _inactiveAppDomains = new HashSet<AppDomainInfo>();

        private bool reinitialize()
        {
            // If we are already initialized and the settings file hasn’t changed, we don’t need to do anything.
            var firstRunEver = _server == null;
            if (_server != null && File.GetLastWriteTimeUtc(_settingsPath) <= _settingsLastChangedTime)
                return false;

            // This may load *and re-write* the settings file...
            var newSettings = PropellerUtil.LoadSettings(_settingsPath, firstRunEver ? new ConsoleLogger() : _log, firstRunEver);
            // ... so remember the file date/time stamp *after* the writing
            _settingsLastChangedTime = File.GetLastWriteTimeUtc(_settingsPath);

            _log = PropellerUtil.GetLogger(newSettings);
            _log.Info(firstRunEver ? "Initializing Propeller" : "Reinitializing Propeller");

            // If either port number or the bind-to address have changed, stop and restart the server’s listener.
            var startListening = false;
            if (_server == null || CurrentSettings == null ||
                CurrentSettings.ServerOptions.Port != newSettings.ServerOptions.Port ||
                CurrentSettings.ServerOptions.SecurePort != newSettings.ServerOptions.SecurePort ||
                CurrentSettings.ServerOptions.BindAddress != newSettings.ServerOptions.BindAddress)
            {
                foreach (var secure in new[] { false, true })
                {
                    var oldPort = CurrentSettings.NullOr(cs => secure ? cs.ServerOptions.SecurePort : cs.ServerOptions.Port);
                    var newPort = secure ? newSettings.ServerOptions.SecurePort : newSettings.ServerOptions.Port;
                    var protocol = secure ? "HTTPS" : "HTTP";
                    if (oldPort == null && newPort == null)
                        continue;
                    else if (oldPort == null)
                        _log.Info("Enabling {1} on port {0}.".Fmt(newPort, protocol));
                    else if (newPort == null)
                        _log.Info("Disabling {1} on port {0}.".Fmt(oldPort, protocol));
                    else if (oldPort != newPort)
                        _log.Info("Switching {2} from port {0} to port {1}.".Fmt(oldPort, newPort, protocol));
                }

                if (_server == null)
                    _server = new HttpServer
                    {
                        Options = newSettings.ServerOptions,
                        ErrorHandler = errorHandler,
                        ResponseExceptionHandler = responseExceptionHandler
                    };
                else
                    _server.StopListening();
                startListening = true;
            }

            CurrentSettings = newSettings;

            // Create a new instance of all the modules
            var newAppDomains = new HashSet<AppDomainInfo>();
            foreach (var module in newSettings.Modules)
            {
                _log.Info("Initializing module: " + module.ModuleName);
                var inf = new AppDomainInfo(_log, newSettings, module, new SettingsSaver(s =>
                {
                    module.Settings = s;
                    _settingsSavedByModule = true;
                }));
                newAppDomains.Add(inf);
            }

            AppDomainInfo[] inactives;

            // Switcheroo!
            lock (_lockObject)
            {
                _inactiveAppDomains.AddRange(_activeAppDomains);
                _activeAppDomains = newAppDomains;
                _server.Options = newSettings.ServerOptions;
                _server.Handler = createResolver().Handle;
                if (startListening)
                    _server.StartListening();
                inactives = _inactiveAppDomains.ToArray();
            }

            // Try to clean up as many inactive AppDomains as possible
            foreach (var inactive in inactives)
                if (inactive.Runner.Shutdown())
                {
                    lock (_lockObject)
                        _inactiveAppDomains.Remove(inactive);
                    inactive.Dispose();
                }

            // Delete any remaining temp folders no longer in use
            HashSet<string> tempFoldersInUse;
            lock (_lockObject)
                tempFoldersInUse = _activeAppDomains.Concat(_inactiveAppDomains).Select(ad => ad.TempPathUsed).ToHashSet();
            foreach (var tempFolder in Directory.EnumerateDirectories(CurrentSettings.TempFolder, "propeller-tmp-*"))
            {
                if (tempFoldersInUse.Contains(tempFolder))
                    continue;
                try { Directory.Delete(tempFolder, recursive: true); }
                catch { }
            }

            return true;
        }

        private void responseExceptionHandler(HttpRequest req, Exception exception, HttpResponse resp)
        {
            lock (_log)
            {
                if (req != null && resp != null)
                    _log.Error("Error in response for request {0} from {1}, status code {2}:".Fmt(req.Url.ToFull(), req.SourceIP, (int) resp.Status));
                PropellerUtil.LogException(_log, exception);
            }
        }

        private HttpResponse errorHandler(HttpRequest req, Exception exception)
        {
            exception.IfType(
                (HttpException httpExc) =>
                {
                    _log.Info("Request {0} from {1} failure code {2}.".Fmt(req.Url.ToFull(), req.SourceIP, (int) httpExc.StatusCode));
                },
                exc =>
                {
                    lock (_log)
                    {
                        _log.Error("Error in handler for request {0} from {1}:".Fmt(req.Url.ToFull(), req.SourceIP));
                        PropellerUtil.LogException(_log, exception);
                    }
                });
            throw exception;
        }

        private UrlResolver createResolver()
        {
            lock (_lockObject)
                return new UrlResolver(_activeAppDomains.SelectMany(inf => inf.UrlMappings));
        }

        private void checkSettingsChanges()
        {
            try
            {
                // ① If the server settings have changed, reinitialize everything.
                if (reinitialize())
                    return;

                // ② If a module rewrote its settings, save the settings file.
                if (_settingsSavedByModule)
                {
                    try
                    {
                        lock (_lockObject)
                            CurrentSettings.Save(_settingsPath);
                    }
                    catch (Exception e)
                    {
                        _log.Error("Error saving Propeller settings:");
                        PropellerUtil.LogException(_log, e);
                    }
                    _settingsSavedByModule = false;
                    _settingsLastChangedTime = File.GetLastWriteTimeUtc(_settingsPath);
                }

                // ③ If any module wants to reinitialize, do it
                AppDomainInfo[] actives;
                lock (_lockObject)
                    actives = _activeAppDomains.ToArray();
                foreach (var active in actives)
                {
                    if (!active.MustReinitialize)   // this adds a log message if it returns true
                        continue;
                    var newAppDomain = new AppDomainInfo(_log, CurrentSettings, active.ModuleSettings, active.Saver);
                    lock (_lockObject)
                    {
                        _inactiveAppDomains.Add(active);
                        _activeAppDomains.Remove(active);
                        _activeAppDomains.Add(newAppDomain);
                        _server.Handler = createResolver().Handle;
                    }
                }
            }
            catch (Exception e)
            {
                PropellerUtil.LogException(_log, e);
            }
        }

        public override void Start(bool backgroundThread = false)
        {
            Start(null, backgroundThread);
        }

        public void Start(string settingsPath, bool backgroundThread = false)
        {
            _settingsPath = settingsPath ?? SettingsUtil.GetAttribute<PropellerSettings>().GetFileName();

            // Do one reinitialization outside of the periodic schedule so that if the first initialization fails, the service doesn’t start
            try
            {
                reinitialize();
            }
            catch (Exception e)
            {
                PropellerUtil.LogException(_log ?? new ConsoleLogger(), e);
                throw;
            }

            // Now start the periodic checking that might trigger reinitialization
            base.Start(backgroundThread);
        }

        public override bool Shutdown(bool waitForExit)
        {
            if (_server != null)
            {
                _server.StopListening();
                _server.ShutdownComplete.WaitOne(TimeSpan.FromSeconds(10));
            }
            foreach (var domain in _activeAppDomains)
            {
                domain.Runner.Shutdown();
                domain.Dispose();
            }
            return base.Shutdown(waitForExit);
        }

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
                new Task { Action = logHeartbeat, MinInterval = TimeSpan.FromMinutes(5) },
                new Task { Action = checkSettingsChanges, MinInterval = checkInterval },
            };
        }

        private void logHeartbeat()
        {
            _log.Debug("Heartbeat");
        }
    }
}
