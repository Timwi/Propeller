using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RT.Servers;
using RT.Util;

namespace RT.Propeller
{
    [Serializable]
    public class PropellerConfig
    {
        public HttpServerOptions ServerOptions = new HttpServerOptions();
        public string PluginDirectory = Path.Combine(PathUtil.AppPath, "plugins");
    }
}
