using System;
using System.IO;
using RT.Servers;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace RT.PropellerApi
{
    public static class PropellerUtil
    {
        /// <summary>
        ///     Executes a Propeller module in standalone mode (as opposed to being hosted by the Propeller engine).</summary>
        /// <param name="module">
        ///     An instance of the module to be executed.</param>
        /// <param name="settings">
        ///     Custom Propeller settings. Leave unspecified to load settings in exactly the same way as the Propeller engine
        ///     would load them had the module been hosted by it.</param>
        public static void RunStandalone(string settingsPath, IPropellerModule module)
        {
            var settings = LoadSettings(settingsPath, new ConsoleLogger(), true);

            if (settings.Modules.Length != 1)
                throw new InvalidOperationException("Propeller Standalone mode can only accept a settings file that has exactly one module configuration.");

            var log = GetLogger(true, settings.LogFile, settings.LogVerbosity);
            log.Info("Running Propeller module {0} in standalone mode.".Fmt(module.Name));

            var resolver = new UrlResolver();
            var server = new HttpServer(settings.ServerOptions) { Handler = resolver.Handle };
            var pretendPluginPath = PathUtil.AppPathCombine(module.Name + ".dll");

            module.Init(log, settings.Modules[0].Settings, new SettingsSaver(s =>
            {
                settings.Modules[0].Settings = s;
                try { settings.Save(settingsPath); }
                catch (Exception e)
                {
                    log.Error("Error saving settings for module {0}:".Fmt(settings.Modules[0].ModuleName));
                    PropellerUtil.LogException(log, e);
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

            if (settings.ServerOptions.Port != null)
                log.Info("Starting server on port {0} (HTTP).".Fmt(settings.ServerOptions.Port));
            if (settings.ServerOptions.SecurePort != null)
                log.Info("Starting server on port {0} (HTTPS).".Fmt(settings.ServerOptions.SecurePort));

            server.StartListening(true);
        }

        /// <summary>
        ///     Loads Propeller settings from the appropriate location. Using this method ensures that settings are loaded in
        ///     exactly the same way and from the same place as the Propeller engine would load them.</summary>
        /// <param name="log">
        ///     Information about how the settings were loaded is logged here. Must not be null.</param>
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
        ///     Returns a logger in accordance with the specified <paramref name="settings"/>, or a <see
        ///     cref="ConsoleLogger"/> if <paramref name="settings"/> is <c>null</c>.</summary>
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

        /// <summary>Logs an exception.</summary>
        /// <param name="log">Logger to log the exception to.</param>
        /// <param name="e">The exception to log.</param>
        /// <param name="source">The source of the exception, e.g. <c>"Propeller"</c>, <c>"a handler"</c>, <c>"the plugin “{0}”".Fmt(pluginName)</c>.</param>
        /// <param name="thrownBy">The name of a method, property or object that is responsible for the exception.</param>
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
