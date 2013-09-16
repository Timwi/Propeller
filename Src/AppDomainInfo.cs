using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using RT.PropellerApi;
using RT.Servers;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace RT.Propeller
{
    sealed class AppDomainInfo : IDisposable
    {
        public AppDomain AppDomain { get; private set; }
        public UrlMapping[] UrlMappings { get; private set; }
        public AppDomainRunner RunnerProxy { get; private set; }
        public PropellerModuleSettings ModuleSettings { get; private set; }
        public ISettingsSaver Saver { get; private set; }
        public string TempPathUsed { get; private set; }

        private int _activeConnections = 0;
        private int _filesChangedCount = 0;
        private string _fileChanged = null;
        private LoggerBase _log;
        private List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();

        private static int _appDomainCount = 0;     // only used to give each AppDomain a unique name

        public AppDomainInfo(LoggerBase log, PropellerSettings settings, PropellerModuleSettings moduleSettings, ISettingsSaver saver)
        {
            ModuleSettings = moduleSettings;
            Saver = saver;
            _log = log;
            _activeConnections = 0;

            // Determine the temporary folder that DLLs will be copied to
            var tempFolder = settings.TempFolder ?? Path.GetTempPath();
            Directory.CreateDirectory(tempFolder);

            // Find a new folder to put the DLL/EXE files into
            int j = 1;
            do { TempPathUsed = Path.Combine(tempFolder, "propeller-tmp-" + (j++)); }
            while (Directory.Exists(TempPathUsed));
            Directory.CreateDirectory(TempPathUsed);

            // Copy all the DLLs/EXEs to the temporary folder
            foreach (var sourceFile in
                new[] { typeof(PropellerEngine), typeof(IPropellerModule), typeof(HttpServer), typeof(Ut) }.Select(type => type.Assembly.Location).Concat(
                Directory.EnumerateFiles(Path.GetDirectoryName(moduleSettings.ModuleDll), "*.exe").Concat(
                Directory.EnumerateFiles(Path.GetDirectoryName(moduleSettings.ModuleDll), "*.dll"))))
            {
                var destFile = Path.Combine(TempPathUsed, Path.GetFileName(sourceFile));
                if (File.Exists(destFile))
                    _log.Warn(2, "Skipping file {0} because destination file {1} already exists.".Fmt(sourceFile, destFile));
                else
                {
                    _log.Info(2, "Copying file {0} to {1}".Fmt(sourceFile, destFile));
                    File.Copy(sourceFile, destFile);
                }
            }

            // Create an AppDomain
            var setup = new AppDomainSetup { ApplicationBase = TempPathUsed, PrivateBinPath = TempPathUsed };
            AppDomain = AppDomain.CreateDomain("Propeller AppDomain #{0}, module {1}".Fmt(_appDomainCount++, moduleSettings.ModuleName), null, setup);
            RunnerProxy = (AppDomainRunner) AppDomain.CreateInstanceAndUnwrap("Propeller", "RT.Propeller.AppDomainRunner");
            RunnerProxy.Init(
                Path.Combine(TempPathUsed, Path.GetFileName(moduleSettings.ModuleDll)),
                moduleSettings.ModuleType,
                moduleSettings.ModuleName,
                moduleSettings.Settings,
                _log,
                saver);

            IEnumerable<string> filters = moduleSettings.MonitorFilters ?? Enumerable.Empty<string>();
            if (RunnerProxy.FileFiltersToBeMonitoredForChanges != null)
                filters = filters.Concat(RunnerProxy.FileFiltersToBeMonitoredForChanges);
            foreach (var filter in filters.Concat(Path.Combine(Path.GetDirectoryName(moduleSettings.ModuleDll), "*")))
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
                req.CleanUpCallback += () =>
                {
                    Interlocked.Decrement(ref _activeConnections);
                };
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

        public bool CanShutdown { get { return _activeConnections == 0; } }

        public void Dispose()
        {
            foreach (var watcher in _watchers)
                try { watcher.Dispose(); }
                catch { }
            _watchers.Clear();
            AppDomain.Unload(AppDomain);
            try { Directory.Delete(TempPathUsed, recursive: true); }
            catch { }
        }
    }
}
