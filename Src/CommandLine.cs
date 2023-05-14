using RT.CommandLine;
using RT.PostBuild;
using RT.Util.Consoles;
using RT.Util.ExtensionMethods;

namespace RT.Propeller
{
    public enum Action
    {
        [CommandName("r", "run"), DocumentationLiteral("(default) Runs a standalone Propeller instance (without a service).")]
        RunAsStandalone,

        [CommandName("i", "install"), DocumentationLiteral("Installs the Propeller service.")]
        Install,
        [CommandName("u", "uninstall"), DocumentationLiteral("Removes the Propeller service from the system.")]
        Uninstall,

        [CommandName("s", "start"), DocumentationLiteral("Starts the Propeller service.")]
        Start,
        [CommandName("st", "stop"), DocumentationLiteral("Stops the Propeller service.")]
        Stop,

        [CommandName("service"), Undocumented]
        Service
    }

    [CommandLine, DocumentationLiteral("Runs, installs or uninstalls the Propeller HTTP service.")]
    public sealed class CommandLine : ICommandLineValidatable
    {
        [IsPositional, DocumentationLiteral("Specifies the action to perform.")]
        public Action Action = Action.RunAsStandalone;

        [Option("-s", "--settings"), DocumentationLiteral("Specifies the path and filename of the Propeller settings file. Propeller will create a new file if it doesn’t exist.")]
        public string SettingsPath;

        private static void PostBuildCheck(IPostBuildReporter rep)
        {
            CommandLineParser.PostBuildStep<CommandLine>(rep, null);
        }

        public ConsoleColoredString Validate() =>
            SettingsPath == null && (Action == Action.RunAsStandalone || Action == Action.Install || Action == Action.Service)
                ? "A settings file must be specified (even if it does not exist).".Color(System.ConsoleColor.Red)
                : null;
    }
}
