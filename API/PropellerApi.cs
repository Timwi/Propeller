using System.Collections.Generic;
using RT.Servers;
using RT.Util;

namespace RT.PropellerApi
{
    public interface IPropellerModule
    {
        PropellerModuleInitResult Init(string origDllPath, string tempDllPath, LoggerBase log);
        bool MustReinitServer();
        void Shutdown();
    }

    public class PropellerModuleInitResult
    {
        public IEnumerable<HttpRequestHandlerHook> HandlerHooks;
    }
}
