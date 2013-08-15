using System;
using System.IO;
using RT.Servers;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace RT.PropellerApi
{
    /// <summary>Provides methods to run a Propeller module in standalone mode.</summary>
    public static class PropellerStandalone
    {
        /// <summary>
        ///     Loads Propeller settings from the appropriate location. Using this method ensures that settings are loaded in
        ///     exactly the same way and from the same place as the Propeller engine would load them.</summary>
        /// <param name="log">
        ///     Information about how the settings were loaded is logged here. Must not be null.</param>
        /// <param name="firstRunEver">
        ///     Adjusts the log messages for improved human readability.</param>
        public static PropellerSettings LoadSettings(LoggerBase log, bool firstRunEver)
        {
            var configPath = SettingsUtil.GetAttribute<PropellerSettings>().GetFileName();

            lock (log)
                log.Info((firstRunEver ? "Loading config file: " : "Reloading config file: ") + configPath);
            PropellerSettings result;
            try
            {
                if (!SettingsUtil.LoadSettings(out result))
                    throw new Exception(); // will be caught straight away
                result.SaveQuiet();
            }
            catch (Exception e)
            {
                lock (log)
                    log.Warn("Config file could not be loaded: {0}. Using default config.".Fmt(e.Message));
                result = new PropellerSettings();
                if (!File.Exists(configPath))
                {
                    try
                    {
                        result.Save();
                        lock (log)
                            log.Info("Default config saved to {0}.".Fmt(configPath));
                    }
                    catch (Exception ex)
                    {
                        lock (log)
                            log.Warn("Attempt to save default config to {0} failed: {1}".Fmt(configPath, ex.Message));
                    }
                }
            }
            return result;
        }

        /// <summary>
        ///     Returns a logger in accordance with the specified <paramref name="settings"/>, or a <see
        ///     cref="ConsoleLogger"/> if <paramref name="settings"/> is <c>null</c>.</summary>
        public static LoggerBase GetLogger(PropellerSettings settings = null)
        {
            var consoleLogger = new ConsoleLogger();
            if (settings == null || settings.LogFile == null)
            {
                consoleLogger.ConfigureVerbosity(settings.LogVerbosity);
                return consoleLogger;
            }

            var logger = new MulticastLogger();
            logger.Loggers["file"] = new FileAppendLogger(settings.LogFile) { SharingVioWait = TimeSpan.FromSeconds(2) };
            logger.Loggers["console"] = consoleLogger;
            logger.ConfigureVerbosity(settings.LogVerbosity);
            return logger;
        }

        /// <summary>Logs an exception.</summary>
        /// <param name="log">Logger to log the exception to.</param>
        /// <param name="e">The exception to log.</param>
        /// <param name="pluginName">The name of the plugin that threw the exception.</param>
        /// <param name="thrownBy">The name of a method, property or object that is responsible for the exception.</param>
        public static void LogException(LoggerBase log, Exception e, string pluginName, string thrownBy)
        {
            lock (log)
            {
                var p = pluginName == null ? "Propeller" : @"plugin ""{0}""".Fmt(pluginName);
                log.Error(@"Error in {0}: {1} ({2} thrown by {3})".Fmt(p, e.Message, e.GetType().FullName, thrownBy));
                log.Error(e.StackTrace);
                while (e.InnerException != null)
                {
                    e = e.InnerException;
                    log.Error(" -- Inner exception: {0} ({1})".Fmt(e.Message, e.GetType().FullName));
                    log.Error(e.StackTrace);
                }
            }
        }

        /// <summary>
        ///     Executes a Propeller module in standalone mode (as opposed to being hosted by the Propeller engine).</summary>
        /// <param name="module">
        ///     An instance of the module to be executed.</param>
        /// <param name="settings">
        ///     Custom Propeller settings. Leave unspecified to load settings in exactly the same way as the Propeller engine
        ///     would load them had the module been hosted by it.</param>
        public static void Run(IPropellerModule module, PropellerSettings settings = null)
        {
            LoggerBase logger = new ConsoleLogger();
            if (settings == null)
                settings = LoadSettings(logger, true);
            else
                logger.Info("Using custom Propeller settings supplied by the standalone module.");
            logger = GetLogger(settings);
            logger.Info("Running Propeller module {0} in standalone mode.".Fmt(module.GetName()));
            var resolver = new UrlPathResolver();
            var server = new HttpServer(settings.ServerOptions) { Handler = resolver.Handle };
            var pretendPluginPath = PathUtil.AppPathCombine(module.GetName() + ".dll");

            PropellerModuleInitResult result;
            try
            {
                result = module.Init(pretendPluginPath, pretendPluginPath, logger);
            }
            catch (Exception e)
            {
                LogException(logger, e, module.GetName(), "Init()");
                return;
            }
            if (result == null)
            {
                logger.Error("The module’s Init() method returned null.");
                return;
            }

            if (result.UrlPathHooks != null)
                resolver.AddRange(result.UrlPathHooks);
            else
                logger.Warn("The module returned null UrlPathHooks. It will not be accessible through any URL.");

            logger.Info(string.Format("Starting server on port {0} (Propeller module in standalone mode)", settings.ServerOptions.Port));
            server.StartListening(true);
        }
    }
}
