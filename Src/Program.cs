using System;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using RT.Services;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace Propeller
{
    class Program
    {
        public static SingleSelfServiceProcess<PropellerService> ServiceProcess = new SingleSelfServiceProcess<PropellerService>();
        public static PropellerService Service = (PropellerService) ServiceProcess.Services.First();
        public static PropellerEngine Engine = new PropellerEngine();
        public static string ConfigPath = PathUtil.AppPathCombine("Propeller.config.xml");
        public static string PropellerLogFile = PathUtil.AppPathCombine("Propeller.log");
        public static MulticastLogger Log = new MulticastLogger();

        static void Main(string[] args)
        {
            Log.Loggers.Add("console", new ConsoleLogger { TimestampInUTC = true });
            try { Log.Loggers.Add("file", new StreamLogger(File.Open(PropellerLogFile, FileMode.Append, FileAccess.Write, FileShare.Read))); }
            catch { Log.Warn("Could not open Propeller log file for writing; will log to console only. File: {0}".Fmt(PropellerLogFile)); }
            Log.Info("");
            Log.Info("");
            Log.Info("");
            Log.Info("Propeller invoked with {0} argument(s): {1}".Fmt(args.Length, args.JoinString("\"", "\"", ", ")));

            if (args.Length == 0)
                Service.RunAsStandalone(args);
            else if (args[0] == "service")
                ServiceProcess.ExecuteServices();

            else if (args[0] == "install")
                ServiceProcess.Install(ServiceAccount.NetworkService, "service");
            else if (args[0] == "uninstall")
                ServiceProcess.Uninstall();

            else if (args[0] == "start")
                ServiceProcess.StartAll();
            else if (args[0] == "stop")
                ServiceProcess.StopAll();

            else
                throw new InvalidOperationException("Unknown arguments. Valid arguments are: service, install, uninstall, start, stop. Or no arguments.");
        }
    }
}
