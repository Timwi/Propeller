using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using RT.PropellerApi;
using RT.Servers;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace Propeller
{
    /// <summary>Contains information about a loaded DLL.</summary>
    [Serializable]
    class DllInfo
    {
        /// <summary>Reference to the instantiated Propeller module.</summary>
        public IPropellerModule Module;

        /// <summary>Caches the result of <see cref="IPropellerModule.GetName"/>.</summary>
        public string ModuleName;

        /// <summary>Path to the original DLL in the plugins folder. We are not supposed to touch the DLL file itself, but we may need to know its path.</summary>
        public string OrigDllPath;

        /// <summary>Path to the DLL in the temp folder where <see cref="AppDomainRunner"/> has copied it.</summary>
        public string TempDllPath;
    }

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

        public void Init(HttpServerOptions options, string pluginDir, string tempDir, LoggerBase log)
        {
            _log = log;
            var resolver = new UrlPathResolver();
            _server = new HttpServer(options) { Handler = resolver.Handle };
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
                        _log.Error(@"Unable to copy file ""{0}"" to ""{1}"": {2} - Ignoring plugin.".Fmt(origDllPath, tempDllPath, e.Message));
                    continue;
                }

                if (!File.Exists(tempDllPath))
                {
                    lock (_log)
                        _log.Error(@"Unable to copy file ""{0}"" to ""{1}"": {2} - Ignoring plugin.".Fmt(origDllPath, tempDllPath, "Although the copy operation succeeded, the target file doesn't exist."));
                    continue;
                }

                IPropellerModule module = null;
                Assembly assembly = Assembly.LoadFile(tempDllPath);
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
                    }
                    catch (Exception e)
                    {
                        logException(e, moduleName, thrownBy);
                        continue;
                    }

                    if (result != null && result.UrlPathHooks != null)
                        resolver.AddRange(result.UrlPathHooks);

                    try
                    {
                        if (result != null && result.FileFiltersToBeMonitoredForChanges != null)
                            foreach (var filter in result.FileFiltersToBeMonitoredForChanges)
                                addFileSystemWatcher(Path.GetDirectoryName(filter), Path.GetFileName(filter));
                    }
                    catch (Exception e)
                    {
                        logException(e, moduleName, "FileSystemWatcher on FileFiltersToBeMonitoredForChanges");
                        continue;
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
        }

        private void logException(Exception e, string pluginName, string thrownBy)
        {
            lock (_log)
            {
                _log.Error(@"Error in plugin ""{0}"": {1} ({2} thrown by {3})".Fmt(pluginName, e.Message, e.GetType().FullName, thrownBy));
                while (e.InnerException != null)
                {
                    e = e.InnerException;
                    _log.Error(" -- Inner exception: {0} ({1})".Fmt(e.Message, e.GetType().FullName));
                }
            }
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
                    logException(e, dll.ModuleName, "MustReinitServer()");
                }
            }
            return false;
        }

        public void Shutdown()
        {
            foreach (var dll in _dlls.Where(d => d.Module != null))
            {
                try { dll.Module.Shutdown(); }
                catch (Exception e) { logException(e, dll.ModuleName, "Shutdown()"); }
            }
        }

        public void HandleRequest(SocketInformation sckInfo)
        {
            Socket sck = new Socket(sckInfo);
            _server.HandleConnection(sck);
        }

        public HttpServer.Statistics Stats
        {
            get
            {
                return _server.Stats;
            }
        }
    }
}
