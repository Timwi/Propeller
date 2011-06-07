using RT.Servers;
using RT.Util;

namespace RT.PropellerApi
{
    /// <summary>
    /// Contains settings which are related to Propeller engine, and not to any individual module.
    /// </summary>
    [Settings("Propeller", SettingsKind.Global)]
    public sealed class PropellerSettings : SettingsBase
    {
        /// <summary>HttpServer configuration.</summary>
        public HttpServerOptions ServerOptions = new HttpServerOptions();
        /// <summary>Propeller loads modules from this path. May contain unexpanded <see cref="PathUtil.ExpandPath"/> tokens. Not applicable to standalone mode.</summary>
        public string PluginDirectory = "$(AppPath)\\plugins";
        /// <summary>Propeller loads modules from this path. Not applicable to standalone mode.</summary>
        public string PluginDirectoryExpanded { get { return PathUtil.ExpandPath(PluginDirectory); } }
        /// <summary>Specifies log verbosity settings. For more details, see <see cref="LoggerBase.ConfigureVerbosity"/>.</summary>
        public string LogVerbosity = "1d0";
    }
}
