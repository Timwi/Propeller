using System.Runtime.Loader;
using RT.PropellerApi;
using RT.Servers;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace RT.Propeller
{
    internal sealed class AssemblyLoadContextInfo : IDisposable
    {
        public UrlMapping[] UrlMappings { get; private set; }
        public AssemblyLoadContextRunner RunnerProxy { get; private set; }
        public PropellerModuleSettings ModuleSettings { get; private set; }
        public ISettingsSaver Saver { get; private set; }
        public string TempPathUsed { get; private set; }

        private int _activeConnections = 0;
        private int _filesChangedCount = 0;
        private string _fileChanged = null;
        private readonly LoggerBase _log;
        private readonly List<FileSystemWatcher> _watchers = [];

        private static readonly int _appDomainCount = 0;     // only used to give each AppDomain a unique name

        public AssemblyLoadContextInfo(LoggerBase log, PropellerSettings settings, PropellerModuleSettings moduleSettings, ISettingsSaver saver)
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

            // Create an AssemblyLoadContext
            RunnerProxy = new AssemblyLoadContextRunner();
            RunnerProxy.Init(
                Path.Combine(TempPathUsed, Path.GetFileName(moduleSettings.ModuleDll)),
                moduleSettings.ModuleType,
                moduleSettings.Settings,
                _log,
                saver);

            var filters = moduleSettings.MonitorFilters ?? Enumerable.Empty<string>();
            if (RunnerProxy.FileFiltersToBeMonitoredForChanges != null)
                filters = filters.Concat(RunnerProxy.FileFiltersToBeMonitoredForChanges);
            foreach (var filter in filters.Concat(moduleSettings.ModuleDll))
                addFileSystemWatcher(Path.GetDirectoryName(filter), Path.GetFileName(filter));

            UrlMappings = moduleSettings.Hooks.Select(hook => new UrlMapping(hook, Handle, true)).ToArray();

            _log.Info("Module {0} URLs: {1}".Fmt(moduleSettings.ModuleName, moduleSettings.Hooks.JoinString("; ")));
        }

        public HttpResponse Handle(HttpRequest req)
        {
            Interlocked.Increment(ref _activeConnections);

            try
            {
                // Must call the handle method first because this is a call across the AppDomain boundary
                return RunnerProxy.Handle(req);
            }
            finally
            {
                // THEN add the callback, which would otherwise not be serializable
                req.CleanUpCallback += () => Interlocked.Decrement(ref _activeConnections);
            }
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

                if (RunnerProxy.MustReinitialize)
                {
                    _log.Info(@"Module {0} asks to be reinitialized.".Fmt(ModuleSettings.ModuleName));
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
            RunnerProxy.Unload();
            try { Directory.Delete(TempPathUsed, recursive: true); }
            catch { }
        }
    }
}
