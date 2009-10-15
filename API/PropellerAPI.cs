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
        IEnumerable<HttpRequestHandlerHook> Init(string configFilePath);
    }

    [Serializable]
    public class PropellerAPI : MarshalByRefObject
    {
        private HttpServer _server;

        public void Init(HttpServerOptions options, IEnumerable<FileInfo> dllFiles, LoggerBase log, Dictionary<string, string> configFilePaths)
        {
            _server = new HttpServer(options);

            foreach (FileInfo f in dllFiles)
            {
                if (!File.Exists(f.FullName))
                    continue;
                string filename = Path.GetFileNameWithoutExtension(f.FullName);
                Assembly a = Assembly.Load(filename);
                foreach (var tp in a.GetExportedTypes())
                {
                    if (!typeof(IPropellerModule).IsAssignableFrom(tp))
                        continue;
                    IPropellerModule module;
                    try
                    {
                        module = (IPropellerModule) Activator.CreateInstance(tp);
                    }
                    catch (Exception e)
                    {
                        log.Error("Error initialising module {0}: {1}", tp.FullName, e.Message);
                        continue;
                    }
                    var configPath = configFilePaths.ContainsKey(tp.FullName) ? configFilePaths[tp.FullName] : PathUtil.AppPathCombine(Path.GetFileNameWithoutExtension(f.Name) + ".config.xml");
                    foreach (var handler in module.Init(configPath))
                        _server.RequestHandlerHooks.Add(handler);
                }
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
