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
                    try
                    {
                        var module = (IPropellerModule) Activator.CreateInstance(type);
                        var result = module.Init(dll.OrigDllPath, dll.TempDllPath, log);
                        if (result.HandlerHooks != null)
                            foreach (var handler in result.HandlerHooks)
                                _server.RequestHandlerHooks.Add(handler);

                        foreach (var filter in result.FileFiltersToBeMonitoredForChanges)
                            addFileSystemWatcher(Path.GetDirectoryName(filter), Path.GetFileName(filter));

                        dll.Module = module;
                    }
                    catch (Exception e)
                    {
                        lock (log)
                            log.Error("Error initialising plugin {0}: {1} - The plugin will be ignored.", Path.GetFileName(dll.OrigDllPath), e.Message);
                    }
                }
            }
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
                    _log.Info(@"Detected changes to {0} file(s), including ""{1}"".".Fmt(_filesChangedCount, _fileChanged));
                return true;
            }
            foreach (var dll in _dlls)
                if (dll.Module != null && dll.Module.MustReinitServer())
                    return true;
            return false;
        }

        public void Shutdown()
        {
            foreach (var dll in _dlls)
                if (dll.Module != null)
                    dll.Module.Shutdown();
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
