using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RT.Servers;
using System.IO;
using RT.Util.XMLClassify;
using RT.Util;
using System.Threading;
using System.Reflection;
using RT.Util.Collections;
using System.Net.Sockets;
using System.Net;
using System.Diagnostics;

namespace Propeller
{
    public class PropellerConfig
    {
        public HTTPServerOptions ServerOptions = new HTTPServerOptions();
        public string PluginDirectory = Path.Combine(PathUtil.AppPath, "plugins");
    }

    class Program
    {
        static AppDomain ActiveAPIDomain = null;
        static PropellerAPI ActiveAPI = null;
        static int APICount = 0;
        static TcpListener Listener;
        static object LockObject = new object();

        static void ListeningThreadFunction()
        {
            Listener.Start();
            while (true)
            {
                Socket sck = Listener.AcceptSocket();
                lock (LockObject)
                {
                    try
                    {
                        if (ActiveAPI != null)
                            ActiveAPI.HandleRequest(sck.DuplicateAndClose(Process.GetCurrentProcess().Id));
                        else
                            sck.Close();
                    }
                    catch { }
                }
            }
        }

        static void Main(string[] args)
        {
            var log = new ConsoleLogger { TimestampInUTC = true };
            var inactiveDomains = new List<Tuple<AppDomain, PropellerAPI>>();
            var fileChangeTime = new Dictionary<string, DateTime>();

            PropellerConfig cfg;
            try
            {
                cfg = XMLClassify.ReadObjectFromXMLFile<PropellerConfig>(Path.Combine(PathUtil.AppPath, @"Propeller.config.xml"));
            }
            catch
            {
                cfg = new PropellerConfig();
                XMLClassify.SaveObjectAsXML(cfg, Path.Combine(PathUtil.AppPath, @"Propeller.config.xml"));
            }

            Listener = new TcpListener(IPAddress.Any, cfg.ServerOptions.Port);
            new Thread(ListeningThreadFunction).Start();

            var mustReinitServer = true;

            while (true)
            {
                var plugins = new List<FileInfo>();

                foreach (var fi in new DirectoryInfo(cfg.PluginDirectory).GetFiles().Where(f => f.Extension == ".dll"))
                {
                    if (!File.Exists(fi.FullName))
                        continue;
                    if (!fileChangeTime.ContainsKey(fi.FullName) || fileChangeTime[fi.FullName] < fi.LastWriteTimeUtc)
                    {
                        mustReinitServer = true;
                        fileChangeTime[fi.FullName] = fi.LastWriteTimeUtc;
                    }
                    plugins.Add(fi);
                }
                foreach (var fi in fileChangeTime.Keys)
                {
                    if (!File.Exists(fi))
                    {
                        fileChangeTime.Remove(fi);
                        mustReinitServer = true;
                    }
                }

                if (mustReinitServer)
                {
                    lock (LockObject)
                    {
                        if (ActiveAPIDomain != null)
                            inactiveDomains.Add(new Tuple<AppDomain, PropellerAPI>(ActiveAPIDomain, ActiveAPI));

                        ActiveAPIDomain = AppDomain.CreateDomain("Propeller API " + (APICount++), null, new AppDomainSetup
                        {
                            ApplicationBase = PathUtil.AppPath,
                            PrivateBinPath = Path.GetDirectoryName(cfg.PluginDirectory).Substring(
                                Path.GetDirectoryName(cfg.PluginDirectory).LastIndexOf(Path.DirectorySeparatorChar) + 1),
                            CachePath = Path.Combine(cfg.PluginDirectory, "cache"),
                            ShadowCopyFiles = "true",
                            ShadowCopyDirectories = cfg.PluginDirectory
                        });
                        ActiveAPI = (PropellerAPI) ActiveAPIDomain.CreateInstanceAndUnwrap("PropellerAPI", "Propeller.PropellerAPI");
                        ActiveAPI.Init(cfg.ServerOptions, plugins, log);
                    }
                }

                var newInactiveDomains = new List<Tuple<AppDomain, PropellerAPI>>();
                foreach (var entry in inactiveDomains)
                {
                    if (entry.E2.ActiveHandlers() == 0)
                        AppDomain.Unload(entry.E1);
                    else
                        newInactiveDomains.Add(entry);
                }
                inactiveDomains = newInactiveDomains;

                Thread.Sleep(10000);
                mustReinitServer = false;
            }
        }
    }
}
