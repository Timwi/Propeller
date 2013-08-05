using System;
using RT.Servers;
using RT.Util;

namespace RT.PropellerApi
{
    /// <summary>
    /// Contains settings which are related to the Propeller engine, and not to any individual module.
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

        /// <summary>Propeller stores temporary files, including the shadow-copied DLLs, in this path. If <c>null</c>, uses <c>Path.GetTempPath()</c>. Not applicable to standalone mode.</summary>
        public string TempDirectory = null;

        /// <summary>If not <c>null</c>, every HTTP request is logged to this file.</summary>
        public string HttpAccessLogFile = null;

        /// <summary>If <c>true</c>, all HTTP requests are logged to the console.</summary>
        public bool HttpAccessLogToConsole = false;

        /// <summary>Specifies log verbosity for the HTTP access log. For usage, see <see cref="LoggerBase.ConfigureVerbosity"/>.</summary>
        public string HttpAccessLogVerbosity = "1d0";

        /// <summary>Specifies the path and filename for the Propeller log, or <c>null</c> to keep no log.</summary>
        public string LogFile = null;

        /// <summary>Specifies log verbosity for the Propeller log. For usage, see <see cref="LoggerBase.ConfigureVerbosity"/>.</summary>
        public string LogVerbosity = "1d0";
    }
}
