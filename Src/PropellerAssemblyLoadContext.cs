using System.Reflection;
using System.Runtime.Loader;
using RT.PropellerApi;
using RT.Servers;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace RT.Propeller
{
    internal sealed class PropellerAssemblyLoadContext : AssemblyLoadContext, IDisposable
    {
        public UrlMapping[] UrlMappings { get; private set; }
        public PropellerModuleSettings ModuleSettings { get; private set; }
        public ISettingsSaver Saver { get; private set; }
        public IPropellerModule Module { get; private set; }
        public string TempPathUsed { get; private set; }

        private readonly AssemblyDependencyResolver _resolver;
        private readonly List<FileSystemWatcher> _watchers = [];
        private readonly LoggerBase _log;
        private int _activeConnections = 0;
        private int _filesChangedCount = 0;
        private string _fileChanged = null;

        protected override Assembly Load(AssemblyName assemblyName) => assemblyName.Name switch
        {
            "RT.Servers" => typeof(HttpServer).Assembly,
            "RT.Util.Core" => typeof(Ut).Assembly,
            "PropellerApi" => typeof(IPropellerModule).Assembly,
            _ => _resolver.ResolveAssemblyToPath(assemblyName).NullOr(LoadFromAssemblyPath),
        };

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
            _resolver.ResolveUnmanagedDllToPath(unmanagedDllName).NullOr(LoadUnmanagedDllFromPath) ?? IntPtr.Zero;

        public PropellerAssemblyLoadContext(LoggerBase log, PropellerSettings settings, PropellerModuleSettings moduleSettings, ISettingsSaver saver) : base(isCollectible: true)
        {
            ModuleSettings = moduleSettings;
            Saver = saver;
            _log = log;
            _activeConnections = 0;

            // Determine the temporary folder that DLLs will be copied to
            var tempFolder = settings.TempFolder ?? Path.GetTempPath();
            Directory.CreateDirectory(tempFolder);

            // Find a new folder to put the DLL/EXE files into
            var j = 1;
            do { TempPathUsed = Path.Combine(tempFolder, "propeller-tmp-" + (j++)); }
            while (Directory.Exists(TempPathUsed));
            Directory.CreateDirectory(TempPathUsed);

            // Copy all the files to the temporary folder
            var basePath = Path.GetDirectoryName(moduleSettings.ModuleDll);
            var fileCount = 0;
            foreach (var sourceFile in Directory.EnumerateFiles(basePath, "*", SearchOption.AllDirectories))
            {
                var destFile = Path.Combine(TempPathUsed, PathUtil.ToggleRelative(basePath, sourceFile));
                if (File.Exists(destFile))
                    _log.Warn(2, $"Skipping file {sourceFile} because destination file {destFile} already exists.");
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destFile));
                    File.Copy(sourceFile, destFile);
                    fileCount++;
                }
            }
            _log.Info(2, $"{fileCount} file(s) copied from {basePath} to {TempPathUsed}.");

            _resolver = new AssemblyDependencyResolver(moduleSettings.ModuleDll);

            var assembly = LoadFromAssemblyPath(moduleSettings.ModuleDll);
            Type moduleType;
            if (moduleSettings.ModuleType != null)
            {
                moduleType = Type.GetType(moduleSettings.ModuleType);
                if (moduleType == null)
                    throw new ModuleInitializationException("The specified CLR type {0} is invalid.".Fmt(moduleSettings.ModuleType));
            }
            else
            {
                var candidates = assembly.GetExportedTypes().Where(type => typeof(IPropellerModule).IsAssignableFrom(type) && !type.IsAbstract).Take(2).ToArray();
                if (candidates.Length == 0)
                    throw new ModuleInitializationException("The file {0} does not contain a Propeller module.".Fmt(moduleSettings.ModuleDll));
                else if (candidates.Length == 2)
                    throw new ModuleInitializationException("The file {0} contains multiple Propeller modules ({1} and {2}). Specify the desired module type explicitly.".Fmt(moduleSettings.ModuleDll, candidates[0].FullName, candidates[1].FullName));
                moduleType = candidates[0];
            }

            Module = (IPropellerModule) Activator.CreateInstance(moduleType);
            Module.Init(log, moduleSettings.Settings, saver);

            var filters = moduleSettings.MonitorFilters ?? Enumerable.Empty<string>();
            if (Module.FileFiltersToBeMonitoredForChanges != null)
                filters = filters.Concat(Module.FileFiltersToBeMonitoredForChanges);
            foreach (var filter in filters.Concat(moduleSettings.ModuleDll))
                addFileSystemWatcher(Path.GetDirectoryName(filter), Path.GetFileName(filter));

            UrlMappings = moduleSettings.Hooks.Select(hook => new UrlMapping(hook, Handle, true)).ToArray();

            _log.Info($"Module {moduleSettings.ModuleName} URLs: {moduleSettings.Hooks.JoinString("; ")}");
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
                    _log.Info($@"Module {ModuleSettings.ModuleName}: Detected {_filesChangedCount} changes to the filesystem, including ""{_fileChanged}"".");
                    _filesChangedCount = 0;
                    return true;
                }

                if (Module.MustReinitialize)
                {
                    _log.Info($"Module {ModuleSettings.ModuleName} asks to be reinitialized.");
                    return true;
                }

                return false;
            }
        }

        public bool HasActiveConnections => _activeConnections > 0;

        public void Dispose()
        {
            foreach (var watcher in _watchers)
                try { watcher.Dispose(); }
                catch { }
            _watchers.Clear();
            Unload();
            try { Directory.Delete(TempPathUsed, recursive: true); }
            catch { }
        }
    }
}
