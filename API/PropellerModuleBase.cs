using System;
using RT.Json;
using RT.Serialization;
using RT.Servers;
using RT.Util;

namespace RT.PropellerApi
{
    /// <summary>
    ///     Provides a default base implementation for Propeller modules.</summary>
    /// <typeparam name="TSettings">
    ///     The type the module uses to represent its settings.</typeparam>
    public abstract class PropellerModuleBase<TSettings> : IPropellerModule where TSettings : class, new()
    {
        /// <summary>When overridden in a derived class, returns the human-readable name of the module.</summary>
        public abstract string Name { get; }

        private ISettingsSaver SettingsSaver;

        /// <summary>Contains the logger provided by Propeller.</summary>
        protected LoggerBase Log;

        /// <summary>Saves the settings stored in <see cref="Settings"/>.</summary>
        protected void SaveSettings()
        {
            SettingsSaver.SaveSettings(ClassifyJson.Serialize(Settings));
        }

        /// <summary>
        ///     Gets or sets the module’s current settings.</summary>
        /// <remarks>
        ///     This property is automatically populated before <see cref="Init"/> is called.</remarks>
        protected TSettings Settings { get; private set; }

        void IPropellerModule.Init(LoggerBase log, JsonValue settings, ISettingsSaver saver)
        {
            SettingsSaver = saver;
            Log = log;
            try
            {
                Settings = ClassifyJson.Deserialize<TSettings>(settings) ?? new TSettings();
            }
            catch (Exception e)
            {
                Log.Exception(e);
                Settings = new TSettings();
            }
            SaveSettings();
            Init();
        }

        /// <summary>When overridden in a derived class, initializes the module. The default implementation does nothing.</summary>
        public virtual void Init() { }

        /// <summary>
        ///     When overridden in a derived class, implements <see
        ///     cref="IPropellerModule.FileFiltersToBeMonitoredForChanges"/>. The default implementation returns <c>null</c>.</summary>
        public virtual string[] FileFiltersToBeMonitoredForChanges { get { return null; } }

        /// <summary>
        ///     When overridden in a derived class, handles an HTTP request.</summary>
        /// <param name="req">
        ///     HTTP request to handle.</param>
        /// <returns>
        ///     HTTP response.</returns>
        public abstract HttpResponse Handle(HttpRequest req);

        /// <summary>
        ///     When overridden in a derived class, implements <see cref="IPropellerModule.MustReinitialize"/>. The default
        ///     implementation returns <c>false</c>.</summary>
        public virtual bool MustReinitialize { get { return false; } }

        /// <summary>
        ///     When overridden in a derived class, implements <see cref="IPropellerModule.Shutdown"/>. The default
        ///     implementation does nothing.</summary>
        public virtual void Shutdown() { }
    }
}
