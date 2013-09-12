using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RT.Servers;
using RT.Util;
using RT.Util.Json;
using RT.Util.Serialization;

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

        /// <summary>
        ///     Gets or sets the module’s current settings.</summary>
        /// <remarks>
        ///     This property is automatically populated before <see cref="Init"/> is called.</remarks>
        protected TSettings Settings { get; private set; }

        void IPropellerModule.Init(LoggerBase log, JsonValue settings, ISettingsSaver saver)
        {
            Settings = ClassifyJson.Deserialize<TSettings>(settings) ?? new TSettings();
            saver.SaveSettings(ClassifyJson.Serialize(Settings));
            Init(log);
        }

        /// <summary>
        ///     When overridden in a derived class, initializes the module. The default implementation does nothing.</summary>
        /// <param name="log">
        ///     A logger that can be used to log messages in the Propeller log.</param>
        public virtual void Init(LoggerBase log) { }

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
