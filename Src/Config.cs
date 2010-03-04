using System;
using RT.Servers;
using RT.Util;

namespace Propeller
{
    [Serializable]
    public class PropellerConfig
    {
        public HttpServerOptions ServerOptions = new HttpServerOptions();
        public string PluginDirectory = "$(AppPath)\\plugins";
        public string PluginDirectoryExpanded { get { return SettingsUtil.ExpandPath(PluginDirectory); } }
    }
}
