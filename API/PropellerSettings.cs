using RT.Json;
using RT.Serialization;
using RT.Servers;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace RT.PropellerApi
{
    /// <summary>Contains settings which are related to the Propeller engine, and not to any individual module.</summary>
    [Settings("Propeller", SettingsKind.Global, SettingsSerializer.ClassifyJson)]
    public sealed class PropellerSettings : SettingsBase
    {
        /// <summary>HttpServer configuration.</summary>
        public HttpServerOptions ServerOptions = new HttpServerOptions();

        /// <summary>Contains the configuration of all the Propeller modules.</summary>
        public PropellerModuleSettings[] Modules = new PropellerModuleSettings[0];

        /// <summary>If not <c>null</c>, every HTTP request is logged to this file.</summary>
        public string HttpAccessLogFile = null;

        /// <summary>If <c>true</c>, all HTTP requests are logged to the console.</summary>
        public bool HttpAccessLogToConsole = false;

        /// <summary>
        ///     Specifies log verbosity for the HTTP access log. For usage, see <see cref="LoggerBase.ConfigureVerbosity"/>.</summary>
        public string HttpAccessLogVerbosity = "1d0";

        /// <summary>Specifies the path and filename for the Propeller log, or <c>null</c> to keep no log.</summary>
        public string LogFile = null;

        /// <summary>
        ///     Specifies log verbosity for the Propeller log. For usage, see <see cref="LoggerBase.ConfigureVerbosity"/>.</summary>
        public string LogVerbosity = "1d0";

        /// <summary>
        ///     The folder into which Propeller can place a copy of the module DLLs. Propeller will actually create (and later
        ///     clean up) numbered subfolders in this folder.</summary>
        /// <remarks>
        ///     If omitted, <c>Path.GetTempPath()</c> is used.</remarks>
        public string TempFolder = null;
    }

    /// <summary>Encapsulates the settings pertaining to a module running within Propeller, including its name and URL hooks.</summary>
    public sealed class PropellerModuleSettings
    {
        /// <summary>
        ///     Provides a name for this instance of this module. You can use this to give different names to different
        ///     instances of the same module.</summary>
        public string ModuleName = "MyModule";

        /// <summary>The path and filename of the DLL file containing the Propeller module.</summary>
        [ClassifyNotNull]
        public string ModuleDll = PathUtil.ExpandPath(@"$(AppPath)\MyModule\MyModule.dll");

        /// <summary>
        ///     Specifies a set of file filters (e.g. “C:\Folder\*.xml”) Propeller should monitor for file changes. If any
        ///     matching file is added, deleted or modified, the module is reinitialized. The folder that contains <see
        ///     cref="ModuleDll"/> is already automatically monitored and does not need to be listed here.</summary>
        [ClassifyNotNull, ClassifyIgnoreIfDefault, ClassifyIgnoreIfEmpty]
        public string[] MonitorFilters = { };

        /// <summary>
        ///     The CLR type name of the Propeller module. This may be <c>null</c> if the DLL file contains only a single type
        ///     that implements <see cref="IPropellerModule"/>.</summary>
        [ClassifyIgnoreIfDefault]
        public string ModuleType = null;

        /// <summary>The URL hooks to hook this module to.</summary>
        [ClassifyNotNull]
        public UrlHook[] Hooks = new UrlHook[] { new UrlHook(domain: "mymodule.com", path: "/mymodule") };

        /// <summary>Settings for this module. (Stored as JSON and passed to the module to be deserialized there.)</summary>
        public JsonValue Settings = null;

        /// <summary>Overrides <c>object.Tostring()</c>.</summary>
        public override string ToString()
        {
            return "{{Settings for {0}{1}}}".Fmt(ModuleName, ModuleType == null ? "" : " ({0})".Fmt(ModuleType));
        }
    }
}
