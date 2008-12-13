using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RT.Servers;
using System.IO;
using System.Reflection;
using RT.Util;
using RT.Util.Collections;
using System.Net.Sockets;

namespace Propeller
{
    [global::System.AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class PropellerModuleAttribute : Attribute { }

    [Serializable]
    public class PropellerAPI : MarshalByRefObject
    {
        private HttpServer Server;

        public void Init(HttpServerOptions Options, IEnumerable<FileInfo> DLLFiles, LoggerBase Log)
        {
            Server = new HttpServer(Options);

            foreach (FileInfo f in DLLFiles)
            {
                if (!File.Exists(f.FullName))
                    continue;
                string filename = Path.GetFileNameWithoutExtension(f.FullName);
                Assembly a = Assembly.Load(filename);
                foreach (var tp in a.GetExportedTypes())
                {
                    if (!tp.GetCustomAttributes(typeof(PropellerModuleAttribute), true).Any())
                        continue;
                    var sm = tp.GetMethod("Init", new Type[] { });
                    if (sm == null || !sm.IsStatic || sm.ReturnType != typeof(IEnumerable<HttpRequestHandlerHook>))
                    {
                        Log.Log(1, LogType.Warning,
                            "The module {0} has a type {1} that uses the [PropellerModule] attribute, but it does not have a static Init() method with no parameters that returns a {2}, so it is ignored.",
                            f, tp, typeof(HttpRequestHandlerHook[]).FullName);
                        continue;
                    }

                    foreach (var ThisHandler in (IEnumerable<HttpRequestHandlerHook>) sm.Invoke(null, new object[] { }))
                        Server.RequestHandlerHooks.Add(ThisHandler);
                }
            }
        }

        public void HandleRequest(SocketInformation sckInfo)
        {
            Socket sck = new Socket(sckInfo);
            Server.HandleRequest(sck, false);
        }

        public int ActiveHandlers()
        {
            return Server.ActiveHandlers;
        }
    }
}
