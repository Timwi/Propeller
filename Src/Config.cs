using System;
using RT.Servers;
using RT.Util;

namespace Propeller
{
    [Serializable]
    class PropellerConfig
    {
        public HttpServerOptions ServerOptions = new HttpServerOptions();
        public string PluginDirectory = "$(AppPath)\\plugins";
        public string PluginDirectoryExpanded { get { return PathUtil.ExpandPath(PluginDirectory); } }
        public string LogVerbosity = "1d0";
    }
}
