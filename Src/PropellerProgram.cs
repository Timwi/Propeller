using System;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using RT.Services;
using RT.Util;
using RT.Util.CommandLine;
using RT.Util.ExtensionMethods;

namespace RT.Propeller
{
    public static class PropellerProgram
    {
        public static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--post-build-check")
                return Ut.RunPostBuildChecks(args[1], System.Reflection.Assembly.GetExecutingAssembly());

            CommandLine cmdLine;
            try
            {
                cmdLine = CommandLineParser<CommandLine>.Parse(args);
            }
            catch (CommandLineParseException e)
            {
                e.WriteUsageInfoToConsole();
                return 1;
            }

            var serviceProcess = new SingleSelfServiceProcess<PropellerService>();
            var service = (PropellerService) serviceProcess.Services.First();
            service.SettingsPath = cmdLine.SettingsPath;

            Console.WriteLine("Propeller invoked with action: " + cmdLine.Action);
            Console.WriteLine("Settings file: " + (cmdLine.SettingsPath ?? "(default)"));

            switch (cmdLine.Action)
            {
                case Action.RunAsStandalone:
                    service.RunAsStandalone(args);
                    break;

                case Action.Install:
                    var arguments = "service";
                    if (cmdLine.SettingsPath != null)
                        arguments += @" -s ""{0}""".Fmt(cmdLine.SettingsPath);
                    serviceProcess.Install(ServiceAccount.NetworkService, arguments);
                    break;

                case Action.Uninstall:
                    serviceProcess.Uninstall();
                    break;

                case Action.Start:
                    serviceProcess.StartAll();
                    break;

                case Action.Stop:
                    serviceProcess.StopAll();
                    break;

                case Action.Service:
                    serviceProcess.ExecuteServices();
                    break;

                default:
                    Console.WriteLine("Unknown arguments.");
                    return 1;
            }

            return 0;
        }
    }
}
