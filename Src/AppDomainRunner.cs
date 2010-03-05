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

        /// <summary>Path to the original DLL in the plugins folder. We are not supposed to touch the DLL file itself, but we may need to know its path.</summary>
        public string OrigDllPath;

        /// <summary>Path to the DLL in the temp folder where <see cref="AppDomainRunner"/> has copied it.</summary>
        public string TempDllPath;

        /// <summary>The name of this module, for use in the UI such as log messages.</summary>
        public string UiName { get { return Path.GetFileNameWithoutExtension(OrigDllPath); } }
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

        public void Init(HttpServerOptions options, string originalDllPath, string tempDllPath, LoggerBase log)
        {
            _log = log;
            _server = new HttpServer(options);
            _dlls = new List<DllInfo>();

            // Copy the DLLs into the new folder and simultaneously create the list of DllInfo objects for them.
            foreach (var plugin in new DirectoryInfo(originalDllPath).GetFiles("*.dll"))
            {
                var dll = new DllInfo
                {
                    OrigDllPath = plugin.FullName,
                    TempDllPath = Path.Combine(tempDllPath, plugin.Name)
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
                _dlls.Add(dll);
            }

            lock (_log)
            {
                _log.Info("Attempting to initialise {0} module(s):".Fmt(_dlls.Count));
                foreach (var dll in _dlls)
                    _log.Info("   {0} from {1}".Fmt(dll.UiName, dll.OrigDllPath));
            }

            addFileSystemWatcher(originalDllPath, "*.dll");

            foreach (var dll in _dlls)
            {
                if (!File.Exists(dll.TempDllPath))
                    continue;

                Assembly assembly = Assembly.LoadFile(dll.TempDllPath);
                foreach (var type in assembly.GetExportedTypes())
                {
                    if (!typeof(IPropellerModule).IsAssignableFrom(type))
                        continue;
                    if (dll.Module != null)
                    {
                        lock (log)
                            log.Error("Plugin {0} contains more than one module. Each plugin is only allowed to contain one module. The module {1} is used, all other modules are ignored.".Fmt(Path.GetFileName(dll.OrigDllPath), dll.Module.GetType().FullName));
                        break;
                    }

                    IPropellerModule module;
                    PropellerModuleInitResult result;
                    string thrownBy = "constructor";
                    try
                    {
                        module = (IPropellerModule) Activator.CreateInstance(type);
                        thrownBy = "Init()";
                        result = module.Init(dll.OrigDllPath, dll.TempDllPath, log);
                    }
                    catch (Exception e)
                    {
                        logException(e, dll, thrownBy);
                        continue;
                    }

                    if (result.FileFiltersToBeMonitoredForChanges == null)
                    {
                        logError("The Init() method of the \"{0}\" plugin returned an invalid result: FileFiltersToBeMonitoredForChanges is null.".Fmt(dll.UiName));
                        continue;
                    }
                    if (result.HandlerHooks == null)
                    {
                        logError("The Init() method of the \"{0}\" plugin returned an invalid result: HandlerHooks is null.".Fmt(dll.UiName));
                        continue;
                    }

                    if (result.HandlerHooks != null)
                        foreach (var handler in result.HandlerHooks)
                            _server.RequestHandlerHooks.Add(handler);

                    try
                    {
                        foreach (var filter in result.FileFiltersToBeMonitoredForChanges)
                            addFileSystemWatcher(Path.GetDirectoryName(filter), Path.GetFileName(filter));
                    }
                    catch (Exception e)
                    {
                        logException(e, dll, "FileSystemWatcher on FileFiltersToBeMonitoredForChanges");
                        continue;
                    }

                    dll.Module = module;
                }
            }

            _dlls = _dlls.Where(d => d.Module != null).ToList();

            lock (_log)
                _log.Info("{0} module(s) are active: {1}".Fmt(_dlls.Count, _dlls.Select(dll => dll.UiName).JoinString(", ")));
        }

        private void logException(Exception e, DllInfo dll, string thrownBy)
        {
            lock (_log)
            {
                _log.Error(@"Error in plugin ""{0}"": {1} ({2} thrown by {3})", dll.UiName, e.Message, e.GetType().FullName, thrownBy);
                while (e.InnerException != null)
                {
                    e = e.InnerException;
                    _log.Error(" -- Inner exception: {0} ({1})", e.Message, e.GetType().FullName);
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
                    logException(e, dll, "MustReinitServer()");
                }
            }
            return false;
        }

        public void Shutdown()
        {
            foreach (var dll in _dlls.Where(d => d.Module != null))
            {
                try { dll.Module.Shutdown(); }
                catch (Exception e) { logException(e, dll, "Shutdown()"); }
            }
        }

        public void HandleRequest(SocketInformation sckInfo)
        {
            Socket sck = new Socket(sckInfo);
            _server.HandleRequest(sck, false);
        }

        public int ActiveHandlers()
        {
            return _server.ActiveHandlers;
        }
    }
}
