using System;
using RT.Servers;
using RT.Util;

namespace Propeller
{
    [Settings("Propeller", SettingsKind.Global)]
    sealed class PropellerSettings : SettingsBase
    {
        public HttpServerOptions ServerOptions = new HttpServerOptions();
        public string PluginDirectory = "$(AppPath)\\plugins";
        public string PluginDirectoryExpanded { get { return PathUtil.ExpandPath(PluginDirectory); } }
        public string LogVerbosity = "1d0";
    }
}
