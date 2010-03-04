using System;
using System.Linq;
using System.ServiceProcess;
using RT.Propeller;
using RT.Services;

namespace Propeller
{
    class Program
    {
        public static SingleSelfServiceProcess<PropellerService> ServiceProcess = new SingleSelfServiceProcess<PropellerService>();
        public static PropellerService Service = (PropellerService) ServiceProcess.Services.First();
        public static PropellerEngine Engine = new PropellerEngine();

        static void Main(string[] args)
        {
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
