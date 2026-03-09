using System.Reflection;
using System.Runtime.Loader;
using RT.PropellerApi;
using RT.Servers;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace RT.Propeller
{
    sealed class PropellerAssemblyLoadContext : AssemblyLoadContext
    {
        public UrlMapping[] UrlMappings { get; private set; }
        public IPropellerModule Module { get; private set; }
        public PropellerModuleSettings ModuleSettings { get; private set; }
        public ISettingsSaver Saver { get; private set; }

        private int _activeConnections = 0;
        private int _filesChangedCount = 0;
        private string _fileChanged = null;
        private readonly LoggerBase _log;
        private readonly List<FileSystemWatcher> _watchers = [];

        protected override Assembly Load(AssemblyName assemblyName)
        {
            // Assemblies that must be shared with Propeller
            if (assemblyName.Name == "PropellerApi")
                return typeof(IPropellerModule).Assembly;
            if (assemblyName.Name == "RT.Servers")
                return typeof(HttpRequest).Assembly;
            if (assemblyName.Name == "RT.Util.Core")
                return typeof(IEnumerableExtensions).Assembly;

            // Try to load the assembly from the same folder as the module DLL
            var folder = Path.GetDirectoryName(ModuleSettings.ModuleDll);
            foreach (var ext in new[] { ".dll", ".exe" })
            {
                var dllPath = Path.Combine(folder, assemblyName.Name + ext);
                if (File.Exists(dllPath))
                    return LoadFromStream(new MemoryStream(File.ReadAllBytes(dllPath)));
            }

            // Defer to default resolution
            return null;
        }

        public PropellerAssemblyLoadContext(LoggerBase log, PropellerModuleSettings moduleSettings, ISettingsSaver saver) : base(isCollectible: true)
        {
            ModuleSettings = moduleSettings;
            Saver = saver;
            _log = log;
            _activeConnections = 0;

            var assembly = Load(new AssemblyName(Path.GetFileNameWithoutExtension(ModuleSettings.ModuleDll)));

            Type moduleType;
            if (ModuleSettings.ModuleType != null)
            {
                moduleType = assembly.GetType(ModuleSettings.ModuleType);
                if (moduleType == null)
                    throw new ModuleInitializationException("The specified CLR type {0} is invalid.".Fmt(ModuleSettings.ModuleType));
            }
            else
            {
                var candidates = assembly.GetExportedTypes().Where(type => typeof(IPropellerModule).IsAssignableFrom(type) && !type.IsAbstract).Take(2).ToArray();
                if (candidates.Length == 0)
                    throw new ModuleInitializationException("The file {0} does not contain a Propeller module.".Fmt(ModuleSettings.ModuleDll));
                else if (candidates.Length == 2)
                    throw new ModuleInitializationException("The file {0} contains multiple Propeller modules ({1} and {2}). Specify the desired module type explicitly.".Fmt(ModuleSettings.ModuleDll, candidates[0].FullName, candidates[1].FullName));
                moduleType = candidates[0];
            }

            Module = (IPropellerModule) Activator.CreateInstance(moduleType);
            Module.Init(log, ModuleSettings.Settings, saver);

            var filters = moduleSettings.MonitorFilters ?? Enumerable.Empty<string>();
            if (Module.FileFiltersToBeMonitoredForChanges != null)
                filters = filters.Concat(Module.FileFiltersToBeMonitoredForChanges);
            foreach (var filter in filters.Concat(moduleSettings.ModuleDll))
                addFileSystemWatcher(Path.GetDirectoryName(filter), Path.GetFileName(filter));

            UrlMappings = moduleSettings.Hooks.Select(hook => new UrlMapping(hook, Handle, true)).ToArray();

            _log.Info("Module {0} URLs: {1}".Fmt(moduleSettings.ModuleName, moduleSettings.Hooks.JoinString("; ")));
        }

        public HttpResponse Handle(HttpRequest req)
        {
            Interlocked.Increment(ref _activeConnections);
            req.CleanUpCallback += () => Interlocked.Decrement(ref _activeConnections);
            return Module.Handle(req);
        }

        private void addFileSystemWatcher(string path, string filter)
        {
            var watcher = new FileSystemWatcher();
            watcher.Path = path;
            watcher.Filter = filter;
            watcher.Changed += fileSystemChangeDetected;
            watcher.Created += fileSystemChangeDetected;
            watcher.Deleted += fileSystemChangeDetected;
            watcher.Renamed += fileSystemChangeDetected;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }

        private void fileSystemChangeDetected(object sender, FileSystemEventArgs e)
        {
            if (Directory.Exists(e.FullPath))
                return;
            _filesChangedCount++;
            _fileChanged = e.FullPath;
        }

        public bool MustReinitialize
        {
            get
            {
                if (_filesChangedCount > 0)
                {
                    _log.Info(@"Module {2}: Detected {0} changes to the filesystem, including ""{1}"".".Fmt(_filesChangedCount, _fileChanged, ModuleSettings.ModuleName));
                    _filesChangedCount = 0;
                    return true;
                }

                if (Module.MustReinitialize)
                {
                    _log.Info(@"Module {0} asks to be reinitialized.".Fmt(ModuleSettings.ModuleName));
                    return true;
                }

                return false;
            }
        }

        public bool HasActiveConnections => _activeConnections > 0;

        public void DoUnload()
        {
            foreach (var watcher in _watchers)
                try { watcher.Dispose(); }
                catch { }
            _watchers.Clear();
            Unload();
        }
    }
}
