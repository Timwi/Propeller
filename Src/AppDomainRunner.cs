using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting;
using System.Security.Permissions;
using RT.PropellerApi;
using RT.Servers;
using RT.Util;
using RT.Util.ExtensionMethods;
using RT.Util.Json;

namespace RT.Propeller
{
    /// <summary>Contains the code that runs in an AppDomain separate from the main Propeller code. There is an AppDomainRunner for each module.</summary>
    [Serializable]
    class AppDomainRunner : MarshalByRefObject
    {
        private string _moduleName;
        private LoggerBase _log;
        private IPropellerModule _module;

        /// <summary>See base.</summary>
        [SecurityPermissionAttribute(SecurityAction.Demand, Flags = SecurityPermissionFlag.Infrastructure)]
        public override object InitializeLifetimeService()
        {
            return null;
        }

        public void Init(string modulePath, string moduleClrType, string moduleName, JsonValue moduleSettings, LoggerBase log, ISettingsSaver saver)
        {
            _log = log;
            _moduleName = moduleName;

            var assembly = Assembly.LoadFile(modulePath);
            Type moduleType;
            if (moduleClrType != null)
            {
                moduleType = Type.GetType(moduleClrType);
                if (moduleType == null)
                    throw new ModuleInitializationException("The specified CLR type {0} is invalid.".Fmt(moduleClrType));
            }
            else
            {
                var candidates = assembly.GetExportedTypes().Where(type => typeof(IPropellerModule).IsAssignableFrom(type) && !type.IsAbstract).Take(2).ToArray();
                if (candidates.Length == 0)
                    throw new ModuleInitializationException("The file {0} does not contain a Propeller module.".Fmt(modulePath));
                else if (candidates.Length == 2)
                    throw new ModuleInitializationException("The file {0} contains multiple Propeller modules ({1} and {2}). Specify the desired module type explicitly.".Fmt(modulePath, candidates[0].FullName, candidates[1].FullName));
                moduleType = candidates[0];
            }

            _module = (IPropellerModule) Activator.CreateInstance(moduleType);
            _module.Init(log, moduleSettings, saver);
        }

        public HttpResponse Handle(HttpRequest req)
        {
            return _module.Handle(req);
        }

        public string[] FileFiltersToBeMonitoredForChanges { get { return _module.FileFiltersToBeMonitoredForChanges; } }
        public bool MustReinitialize { get { return _module.MustReinitialize; } }

        public void Shutdown()
        {
            RemotingServices.Disconnect(this);
            _module.Shutdown();
        }
    }
}
