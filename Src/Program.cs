using System;
using RT.Util.ExtensionMethods;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RT.Servers;
using System.IO;
using RT.Util;
using System.Threading;
using System.Reflection;
using RT.Util.Collections;
using System.Net.Sockets;
using System.Net;
using System.Diagnostics;
using RT.Util.Xml;

namespace Propeller
{
    public class PropellerConfig
    {
        public HttpServerOptions ServerOptions = new HttpServerOptions();
        public string PluginDirectory = Path.Combine(PathUtil.AppPath, "plugins");
        public Dictionary<string, string> PluginConfigs = new Dictionary<string, string>();
    }

    public class Program
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
                cfg = XmlClassify.LoadObjectFromXmlFile<PropellerConfig>(Path.Combine(PathUtil.AppPath, @"Propeller.config.xml"));
            }
            catch
            {
                var path = Path.Combine(PathUtil.AppPath, @"Propeller.config.xml");
                log.Warn("Config file could not be loaded; creating default config at: {0}".Fmt(path));
                cfg = new PropellerConfig();
                XmlClassify.SaveObjectToXmlFile(cfg, path);
            }

            log.Warn("Starting to listen on port: {0}".Fmt(cfg.ServerOptions.Port));
            Listener = new TcpListener(IPAddress.Any, cfg.ServerOptions.Port);
            new Thread(ListeningThreadFunction).Start();

            var mustReinitServer = true;

            while (true)
            {
                var plugins = new List<FileInfo>();

                foreach (var fi in new DirectoryInfo(cfg.PluginDirectory).GetFiles("*.dll"))
                {
                    if (!fileChangeTime.ContainsKey(fi.FullName) || fileChangeTime[fi.FullName] < fi.LastWriteTimeUtc)
                    {
                        mustReinitServer = true;
                        fileChangeTime[fi.FullName] = fi.LastWriteTimeUtc;
                        log.Info("Plugin changed: " + fi.FullName);
                    }
                    plugins.Add(fi);
                }
                foreach (var fi in fileChangeTime.Keys)
                {
                    if (!File.Exists(fi))
                    {
                        fileChangeTime.Remove(fi);
                        mustReinitServer = true;
                        log.Info("Plugin removed: " + fi);
                    }
                }
#warning TODO: Check if the server config file or any of the plugin config files have changed

                if (mustReinitServer)
                {
                    lock (LockObject)
                    {
                        log.Info("Reinitialising plugins...");

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

#if DEBUG
                Thread.Sleep(1000);
#else
                Thread.Sleep(10000);
#endif

                mustReinitServer = false;
            }
        }
    }
}
