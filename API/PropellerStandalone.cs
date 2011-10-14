using System;
using System.IO;
using RT.PropellerApi;
using RT.Servers;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace RT.Propeller
{
    /// <summary>
    /// Provides methods to run a Propeller module in standalone mode.
    /// </summary>
    public static class PropellerStandalone
    {
        /// <summary>
        /// Loads Propeller settings from the appropriate location. Using this method ensures that settings are loaded
        /// in exactly the same way and from the same place as the Propeller engine would load them.
        /// </summary>
        /// <param name="log">Information about how the settings were loaded is logged here. Must not be null.</param>
        /// <param name="firstRunEver">Adjusts the log messages for improved human readability.</param>
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
            }
            catch
            {
                lock (log)
                    log.Warn("Config file could not be loaded; using default config.");
                result = new PropellerSettings();
                if (!File.Exists(configPath))
                {
                    try
                    {
                        result.Save();
                        lock (log)
                            log.Info("Default config saved to {0}.".Fmt(configPath));
                    }
                    catch (Exception e)
                    {
                        lock (log)
                            log.Warn("Attempt to save default config to {0} failed: {1}".Fmt(configPath, e.Message));
                    }
                }
            }
            return result;
        }

        /// <summary>Executes a Propeller module in standalone mode (as opposed to being hosted by the Propeller engine).</summary>
        /// <param name="module">An instance of the module to be executed.</param>
        /// <param name="settings">Custom Propeller settings. Leave unspecified to load settings in exactly the same way
        /// as the Propeller engine would load them had the module been hosted by it.</param>
        public static void Run(IPropellerModule module, PropellerSettings settings = null)
        {
            var logger = new ConsoleLogger();
            logger.Info("Running Propeller module {0} in standalone mode.".Fmt(module.GetName()));
            if (settings == null)
                settings = LoadSettings(logger, true);
            else
                logger.Info("Using custom Propeller settings supplied by the standalone module.");
            logger.ConfigureVerbosity(settings.LogVerbosity);
            var server = new HttpServer(settings.ServerOptions);
            var result = module.Init(PathUtil.AppPath, PathUtil.AppPath, logger);
            if (result != null && result.HandlerHooks != null)
                server.RequestHandlerHooks.AddRange(result.HandlerHooks);
            logger.Info(string.Format("Starting server on port {0} (Propeller module in standalone mode)", settings.ServerOptions.Port));
            server.StartListening(true);
        }
    }
}
