using System;
using System.IO;
using System.Linq;
using RT.Servers;
using RT.Util;
using RT.Util.ExtensionMethods;
using RT.Util.Json;

namespace RT.PropellerApi
{
    /// <summary>Contains helper methods relating to Propeller.</summary>
    public static class PropellerUtil
    {
        /// <summary>
        ///     Executes a Propeller module in standalone mode (as opposed to being hosted by the Propeller engine).</summary>
        /// <param name="module">
        ///     An instance of the module to be executed.</param>
        /// <param name="settingsPath">
        ///     Path and filename of the Propeller settings file. This file must contain a Propeller configuration containing
        ///     exactly one module configuration.</param>
        public static void RunStandalone(string settingsPath, IPropellerModule module)
        {
            var settings = LoadSettings(settingsPath, new ConsoleLogger(), true);

            if (settings.Modules.Length == 0)
            {
                settings.Modules = Ut.NewArray(new PropellerModuleSettings
                {
                    ModuleName = module.Name,
                    ModuleDll = null,
                    Settings = new JsonDict(),
                    Hooks = Ut.NewArray(new UrlHook(domain: "localhost", protocols: Protocols.All))
                });
            }

            if (settings.Modules.Length != 1)
                throw new InvalidOperationException("Propeller Standalone mode can only accept a settings file that has exactly one module configuration.");

            var log = GetLogger(true, settings.LogFile, settings.LogVerbosity);
            log.Info("Running Propeller module {0} in standalone mode.".Fmt(module.Name));

            var resolver = new UrlResolver();
            var server = new HttpServer(settings.ServerOptions) { Handler = resolver.Handle };
#if DEBUG
            server.PropagateExceptions = true;
#endif
            var pretendPluginPath = PathUtil.AppPathCombine(module.Name + ".dll");

            module.Init(log, settings.Modules[0].Settings, new SettingsSaver(s =>
            {
                settings.Modules[0].Settings = s;
                try { settings.Save(settingsPath); }
                catch (Exception e)
                {
                    log.Error("Error saving settings for module {0}:".Fmt(settings.Modules[0].ModuleName));
                    LogException(log, e);
                }
            }));

            if (settings.Modules[0].Hooks.Length == 0)
                log.Warn("The settings did not configure any UrlHook for the module. It will not be accessible through any URL.");
            else
            {
                foreach (var hook in settings.Modules[0].Hooks)
                    resolver.Add(new UrlMapping(hook, module.Handle));
                log.Info("Module URLs: " + settings.Modules[0].Hooks.JoinString("; "));
            }

            log.Info("Starting server on {0}.".Fmt(settings.ServerOptions.Endpoints.Select(ep => "port " + ep.Value.Port + (ep.Value.Secure ? " (HTTPS)" : " (HTTP)")).JoinString(", ")));
            settings.Save(settingsPath);
            server.StartListening(true);
        }

        /// <summary>
        ///     Loads Propeller settings from the appropriate location. Using this method ensures that settings are loaded in
        ///     exactly the same way and from the same place as the Propeller engine would load them.</summary>
        /// <param name="settingsPath">
        ///     Path and filename of the settings to load.</param>
        /// <param name="log">
        ///     Information about how the settings were loaded is logged here. Must not be <c>null</c>.</param>
        /// <param name="firstRunEver">
        ///     Adjusts the log messages for improved human readability.</param>
        public static PropellerSettings LoadSettings(string settingsPath, LoggerBase log, bool firstRunEver)
        {
            settingsPath = settingsPath ?? SettingsUtil.GetAttribute<PropellerSettings>().GetFileName();
            log.Info((firstRunEver ? "Loading settings file: " : "Reloading settings file: ") + settingsPath);

            PropellerSettings settings;
            var success = false;

            try
            {
                success = SettingsUtil.LoadSettings(out settings, settingsPath);
                if (success)
                {
                    settings.SaveQuiet(settingsPath);
                    return settings;
                }
            }
            catch (Exception e)
            {
                settings = new PropellerSettings();
                log.Error("Settings file could not be loaded: {0} ({1}). Using default settings.".Fmt(e.Message, e.GetType().FullName));
            }

            if (!success)
            {
                if (!File.Exists(settingsPath))
                {
                    try
                    {
                        settings.Save(settingsPath);
                        log.Info("Default settings saved to {0}.".Fmt(settingsPath));
                    }
                    catch (Exception ex)
                    {
                        log.Warn("Attempt to save default settings to {0} failed: {1} ({2})".Fmt(settingsPath, ex.Message, ex.GetType().FullName));
                    }
                }
            }
            return settings;
        }

        /// <summary>
        ///     Returns a logger in accordance with the specified settings.</summary>
        /// <param name="console">
        ///     If <c>true</c>, the resulting logger will log to the console.</param>
        /// <param name="file">
        ///     If non-<c>null</c>, the resulting logger will log to the specified file.</param>
        /// <param name="logVerbosity">
        ///     Configures the verbosity of the resulting logger.</param>
        /// <remarks>
        ///     <para>
        ///         Uses <see cref="ConsoleLogger"/> amd <see cref="FileAppendLogger"/> as appropriate.</para>
        ///     <para>
        ///         If both logging mechanisms are specified, uses a <see cref="MulticastLogger"/> to combine the two.</para></remarks>
        public static LoggerBase GetLogger(bool console, string file, string logVerbosity)
        {
            if (!console && file == null)
                return new NullLogger();

            ConsoleLogger consoleLogger = null;
            if (console)
            {
                consoleLogger = new ConsoleLogger();
                consoleLogger.ConfigureVerbosity(logVerbosity);
                if (file == null)
                    return consoleLogger;
            }

            FileAppendLogger fileLogger = null;
            if (file != null)
            {
                fileLogger = new FileAppendLogger(file) { SharingVioWait = TimeSpan.FromSeconds(2) };
                fileLogger.ConfigureVerbosity(logVerbosity);
                if (!console)
                    return fileLogger;
            }

            var logger = new MulticastLogger();
            logger.Loggers["file"] = fileLogger;
            logger.Loggers["console"] = consoleLogger;
            logger.ConfigureVerbosity(logVerbosity);
            return logger;
        }

        /// <summary>
        ///     Logs an exception.</summary>
        /// <param name="log">
        ///     Logger to log the exception to.</param>
        /// <param name="e">
        ///     The exception to log.</param>
        public static void LogException(LoggerBase log, Exception e)
        {
            lock (log)
            {
                log.Error(@"Exception: {0} ({1})".Fmt(e.Message, e.GetType().FullName));
                log.Error(e.StackTrace);
                var indent = 0;
                while (e.InnerException != null)
                {
                    indent += 4;
                    e = e.InnerException;
                    log.Error(" -- Inner exception: {0} ({1})".Fmt(e.Message, e.GetType().FullName).Indent(indent));
                    log.Error(e.StackTrace.NullOr(st => st.Indent(indent)));
                }
            }
        }
    }
}
