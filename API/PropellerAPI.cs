using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RT.Servers;
using RT.Util.ExtensionMethods;
using System.IO;
using System.Reflection;
using RT.Util;
using RT.Util.Collections;
using System.Net.Sockets;

namespace Propeller
{
    public interface IPropellerModule
    {
        PropellerModuleInitResult Init(string configFilePath, LoggerBase log);
        void Shutdown();
    }

    public class PropellerModuleInitResult : MarshalByRefObject
    {
        public IEnumerable<HttpRequestHandlerHook> HandlerHooks;
        public IEnumerable<string> FilesToMonitor;
        public IEnumerable<string> FoldersToMonitor;
    }

    [Serializable]
    public class PropellerInternalInitResult
    {
        public string[] FilesToMonitor;
        public string[] FoldersToMonitor;
    }

    [Serializable]
    public class PropellerAPI : MarshalByRefObject
    {
        private HttpServer _server;
        private List<IPropellerModule> _modules;

        public PropellerInternalInitResult Init(HttpServerOptions options, IEnumerable<FileInfo> dllFiles, LoggerBase log, Dictionary<string, string> configFilePaths)
        {
            _server = new HttpServer(options);
            _modules = new List<IPropellerModule>();
            var filesToMon = Enumerable.Empty<string>();
            var foldersToMon = Enumerable.Empty<string>();

            foreach (FileInfo f in dllFiles)
            {
                if (!File.Exists(f.FullName))
                    continue;
                Assembly a = Assembly.LoadFile(f.FullName);
                foreach (var tp in a.GetExportedTypes())
                {
                    if (!typeof(IPropellerModule).IsAssignableFrom(tp))
                        continue;
                    IPropellerModule module;
                    try
                    {
                        module = (IPropellerModule) Activator.CreateInstance(tp);
                        var configPath = configFilePaths.ContainsKey(tp.FullName) ? configFilePaths[tp.FullName] : Path.Combine(f.DirectoryName, Path.GetFileNameWithoutExtension(f.Name) + ".config.xml");
                        var result = module.Init(configPath, log);
                        if (result.HandlerHooks != null)
                            foreach (var handler in result.HandlerHooks)
                                _server.RequestHandlerHooks.Add(handler);
                        if (result.FilesToMonitor != null)
                            filesToMon = filesToMon.Concat(result.FilesToMonitor);
                        if (result.FoldersToMonitor != null)
                            foldersToMon = foldersToMon.Concat(result.FoldersToMonitor);
                        _modules.Add(module);
                    }
                    catch (Exception e)
                    {
                        log.Error("Error initialising module {0}: {1}", tp.FullName, e.Message);
                        continue;
                    }
                }
            }
            return new PropellerInternalInitResult
            {
                FilesToMonitor = filesToMon.ToArray(),
                FoldersToMonitor = foldersToMon.ToArray()
            };
        }

        public void Shutdown()
        {
            foreach (var module in _modules)
                module.Shutdown();
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
