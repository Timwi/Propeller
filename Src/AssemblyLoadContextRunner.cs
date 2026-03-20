using System.Reflection;
using System.Runtime.Loader;
using RT.Json;
using RT.PropellerApi;
using RT.Servers;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace RT.Propeller
{
    /// <summary>Contains the code that runs in an AssemblyLoadContext separate from the main Propeller code. There is an AssemblyLoadContextRunner for each module.</summary>
    [Serializable]
    class AssemblyLoadContextRunner : AssemblyLoadContext
    {
        private IPropellerModule _module;
        private AssemblyDependencyResolver _resolver;

        protected override Assembly Load(AssemblyName assemblyName) => assemblyName.Name switch
        {
            "RT.Servers" => typeof(HttpServer).Assembly,
            "RT.Util.Core" => typeof(Ut).Assembly,
            "PropellerApi" => typeof(IPropellerModule).Assembly,
            _ => _resolver.ResolveAssemblyToPath(assemblyName).NullOr(LoadFromAssemblyPath),
        };

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
            _resolver.ResolveUnmanagedDllToPath(unmanagedDllName).NullOr(LoadUnmanagedDllFromPath) ?? IntPtr.Zero;

        public void Init(string modulePath, string moduleClrType, JsonValue moduleSettings, LoggerBase log, ISettingsSaver saver)
        {
            _resolver = new AssemblyDependencyResolver(modulePath);

            var assembly = LoadFromAssemblyPath(modulePath);
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
        public bool MustReinitialize => _module.MustReinitialize;

        public void Shutdown()
        {
            _module.Shutdown();
        }
    }
}
