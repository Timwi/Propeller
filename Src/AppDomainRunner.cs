using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using RT.Propeller;
using RT.PropellerApi;
using RT.Servers;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace Propeller
{
    /// <summary>Contains the code that runs in an AppDomain separate from the main Propeller code (<see cref="PropellerEngine"/>).</summary>
    [Serializable]
    class AppDomainRunner : MarshalByRefObject
    {
        private HttpServer _server;
        private List<DllInfo> _dlls;
        private List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private int _filesChangedCount = 0;
        private string _fileChanged = null;
        private LoggerBase _log;

        public bool Init(HttpServerOptions options, string pluginDir, string tempDir, LoggerBase log, string logFile, bool logToConsole, string logVerbosity)
        {
            _log = log;

            var resolver = new UrlPathResolver();
            _server = new HttpServer(options)
            {
                Handler = resolver.Handle,
                ErrorHandler = (req, e) =>
                {
                    PropellerStandalone.LogException(_log, e, null, "a handler");
                    return null;
                },
                ResponseExceptionHandler = (req, e, resp) =>
                {
                    PropellerStandalone.LogException(_log, e, null, "a handler's response object");
                }
            };

            if (logFile != null && logToConsole)
            {
                var multiLogger = new MulticastLogger();
                multiLogger.Loggers["file"] = new FileAppendLogger(logFile) { SharingVioWait = TimeSpan.FromSeconds(2) };
                multiLogger.Loggers["console"] = new ConsoleLogger();
                _server.Log = multiLogger;
            }
            else if (logFile != null)
                _server.Log = new FileAppendLogger(logFile) { SharingVioWait = TimeSpan.FromSeconds(2) };
            else if (logToConsole)
                _server.Log = new ConsoleLogger();
            _server.Log.ConfigureVerbosity(logVerbosity);

            _dlls = new List<DllInfo>();

            addFileSystemWatcher(pluginDir, "*.dll");
            addFileSystemWatcher(pluginDir, "*.exe");

            // Copy the DLLs into the new folder and simultaneously create the list of DllInfo objects for them.
            var dirinfo = new DirectoryInfo(pluginDir);
            foreach (var plugin in dirinfo.GetFiles("*.dll").Concat(dirinfo.GetFiles("*.exe")))
            {
                var origDllPath = plugin.FullName;
                var tempDllPath = Path.Combine(tempDir, plugin.Name);

                lock (_log)
                    _log.Info("Loading plugin: {0}".Fmt(origDllPath));

                try
                {
                    File.Copy(origDllPath, tempDllPath);
                }
                catch (Exception e)
                {
                    lock (_log)
                        _log.Error(@"Unable to copy file ""{0}"" to ""{1}"": {2}".Fmt(origDllPath, tempDllPath, e.Message));
                    return false;
                }

                if (!File.Exists(tempDllPath))
                {
                    lock (_log)
                        _log.Error(@"Unable to copy file ""{0}"" to ""{1}"": {2}".Fmt(origDllPath, tempDllPath, "Although the copy operation succeeded, the target file doesn't exist."));
                    return false;
                }

                Assembly assembly;
                try
                {
                    assembly = Assembly.LoadFile(tempDllPath);
                }
                catch (Exception e)
                {
                    PropellerStandalone.LogException(_log, e, null, tempDllPath);
                    return false;
                }

                IPropellerModule module = null;
                foreach (var type in assembly.GetExportedTypes())
                {
                    if (!typeof(IPropellerModule).IsAssignableFrom(type))
                        continue;
                    if (module != null)
                    {
                        lock (log)
                            log.Error("Plugin {0} contains more than one module. Each plugin is only allowed to contain one module. The module {1} is used, all other modules are ignored.".Fmt(Path.GetFileName(origDllPath), module.GetType().FullName));
                        break;
                    }

                    PropellerModuleInitResult result;
                    string thrownBy = "constructor";
                    string moduleName = type.Name;
                    try
                    {
                        module = (IPropellerModule) Activator.CreateInstance(type);
                        thrownBy = "GetName()";
                        moduleName = module.GetName();
                        thrownBy = "Init()";
                        result = module.Init(origDllPath, tempDllPath, log);
                        if (result == null)
                        {
                            log.Error(@"The plugin ""{0}""'s Init() method returned null.".Fmt(moduleName));
                            return false;
                        }
                    }
                    catch (Exception e)
                    {
                        PropellerStandalone.LogException(_log, e, moduleName, thrownBy);
                        return false;
                    }

                    if (result.UrlPathHooks != null)
                        resolver.AddRange(result.UrlPathHooks);
                    else
                    {
                        lock (log)
                            log.Warn(@"The module ""{0}"" returned null UrlPathHooks. It will not be accessible through any URL.".Fmt(moduleName));
                    }

                    try
                    {
                        if (result != null && result.FileFiltersToBeMonitoredForChanges != null)
                            foreach (var filter in result.FileFiltersToBeMonitoredForChanges)
                                addFileSystemWatcher(Path.GetDirectoryName(filter), Path.GetFileName(filter));
                    }
                    catch (Exception e)
                    {
                        PropellerStandalone.LogException(_log, e, moduleName, "FileSystemWatcher on FileFiltersToBeMonitoredForChanges");
                        return false;
                    }

                    _dlls.Add(new DllInfo
                    {
                        OrigDllPath = origDllPath,
                        TempDllPath = tempDllPath,
                        Module = module,
                        ModuleName = moduleName
                    });
                }
            }

            lock (_log)
                _log.Info("{0} plugin(s) are active: {1}".Fmt(_dlls.Count, _dlls.Select(dll => dll.ModuleName).JoinString(", ")));
            return true;
        }

        private void logError(string message)
        {
            lock (_log)
                _log.Error(message);
        }

        private void addFileSystemWatcher(string path, string filter)
        {
            var watcher = new FileSystemWatcher();
            watcher.Path = path;
            watcher.Filter = filter;
            watcher.Changed += fileSystemChangeDetected;
            watcher.Created += fileSystemChangeDetected;
            watcher.Deleted += fileSystemChangeDetected;
            watcher.Renamed += fileSystemChangeDetected;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }

        private void fileSystemChangeDetected(object sender, FileSystemEventArgs e)
        {
            _filesChangedCount++;
            _fileChanged = e.FullPath;
        }

        public bool MustReinitServer()
        {
            if (_filesChangedCount > 0)
            {
                lock (_log)
                    _log.Info(@"Detected {0} changes to the filesystem, including ""{1}"".".Fmt(_filesChangedCount, _fileChanged));
                _filesChangedCount = 0;
                return true;
            }
            foreach (var dll in _dlls.Where(d => d.Module != null))
            {
                try
                {
                    if (dll.Module.MustReinitServer())
                        return true;
                }
                catch (Exception e)
                {
                    PropellerStandalone.LogException(_log, e, dll.ModuleName, "MustReinitServer()");
                }
            }
            return false;
        }

        public void Shutdown()
        {
            foreach (var dll in _dlls.Where(d => d.Module != null))
            {
                try { dll.Module.Shutdown(); }
                catch (Exception e) { PropellerStandalone.LogException(_log, e, dll.ModuleName, "Shutdown()"); }
            }
        }

        public void HandleRequest(SocketInformation sckInfo, bool secure = false)
        {
            _server.HandleConnection(new Socket(sckInfo), secure);
        }

        public int ActiveHandlers
        {
            get
            {
                return _server.Stats.ActiveHandlers;
            }
        }

        public void ResetFileSystemWatchers()
        {
            _filesChangedCount = 0;
        }
    }
}
