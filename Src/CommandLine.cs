using RT.CommandLine;
using RT.PostBuild;

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
    public sealed class CommandLine
    {
        [IsPositional, DocumentationLiteral("Specifies the action to perform.")]
        public Action Action = Action.RunAsStandalone;

        [Option("-s", "--settings"), DocumentationLiteral("Specifies the path and filename of the Propeller settings file.")]
        public string SettingsPath;

        private static void PostBuildCheck(IPostBuildReporter rep)
        {
            CommandLineParser.PostBuildStep<CommandLine>(rep, null);
        }
    }
}
