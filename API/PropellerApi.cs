using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using RT.Servers;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace RT.Propeller
{
    public interface IPropellerModule
    {
        PropellerModuleInitResult Init(string origDllPath, string tempDllPath, LoggerBase log);
        bool MustReinitServer();
        void Shutdown();
    }

    public class PropellerModuleInitResult
    {
        public IEnumerable<HttpRequestHandlerHook> HandlerHooks;
    }

    [Serializable]
    public class DllInfo
    {
        public IPropellerModule Module;

        // This is the path to the original DLL in the plugins folder. We are not supposed to touch the DLL file itself, but we may need to know its path.
        public string OrigDllPath;

        // This is the path to the DLL in the temp folder, which the main Propeller process has already copied there.
        // The main propeller process monitors changes to the original DLLs, so we don't have to.
        public string TempDllPath;

        public DateTime DllLastChange;
    }

    [Serializable]
    public class PropellerApi : MarshalByRefObject
    {
        private HttpServer _server;
        private List<DllInfo> _dlls;

        public void Init(HttpServerOptions options, List<DllInfo> dlls, LoggerBase log)
        {
            _server = new HttpServer(options);
            _dlls = dlls;

            foreach (var dll in _dlls)
            {
                if (!File.Exists(dll.TempDllPath))
                    continue;

                dll.DllLastChange = new FileInfo(dll.OrigDllPath).LastWriteTimeUtc;

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

        public bool MustReinitServer()
        {
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
