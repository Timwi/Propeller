using System;
using System.ServiceProcess;
using RT.Propeller;

namespace Propeller
{
    public class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
                PropellerService.RunServiceStandalone(args);
            else if (args[0] == "service")
                PropellerService.ServiceProcess.ExecuteServices();

            else if (args[0] == "install")
                PropellerService.ServiceProcess.Install(ServiceAccount.NetworkService, "service");
            else if (args[0] == "uninstall")
                PropellerService.ServiceProcess.Uninstall();

            else if (args[0] == "start")
                PropellerService.ServiceProcess.StartAll();
            else if (args[0] == "stop")
                PropellerService.ServiceProcess.StopAll();

            else
                throw new InvalidOperationException("Unknown arguments. Valid arguments are: service, install, uninstall, start, stop. Or no arguments.");
        }
    }
}
