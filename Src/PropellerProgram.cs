using System;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using RT.Services;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace Propeller
{
    class PropellerProgram
    {
        public static SingleSelfServiceProcess<PropellerService> ServiceProcess = new SingleSelfServiceProcess<PropellerService>();
        public static PropellerService Service = (PropellerService) ServiceProcess.Services.First();
        public static PropellerEngine Engine = new PropellerEngine();
        public static string PropellerLogFile = PathUtil.AppPathCombine("Propeller.log");
        public static MulticastLogger Log = new MulticastLogger();

        static void Main(string[] args)
        {
            Log.Loggers.Add("console", new ConsoleLogger { TimestampInUTC = true });
            Log.Loggers.Add("file", new FileAppendLogger(PropellerLogFile));
            Log.Info("");
            Log.Info("");
            Log.Info("");
            Log.Info("Propeller invoked with {0} argument(s): {1}".Fmt(args.Length, args.JoinString(separator: ", ", prefix: "\"", suffix: "\"")));

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
