using System;
using RT.Json;

namespace RT.PropellerApi
{
    /// <summary>Provides a simple implementation for the <see cref="ISettingsSaver"/> interface.</summary>
    public sealed class SettingsSaver : MarshalByRefObject, ISettingsSaver
    {
        private readonly Action<JsonValue> _saver;

        /// <summary>
        ///     Constructor.</summary>
        /// <param name="saver">
        ///     Delegate that saves the settings.</param>
        public SettingsSaver(Action<JsonValue> saver)
        {
            _saver = saver ?? throw new ArgumentNullException(nameof(saver));
        }

        /// <summary>
        ///     Saves the settings. (Implements <see cref="ISettingsSaver.SaveSettings"/>.)</summary>
        /// <param name="settings">
        ///     Settings to save.</param>
        public void SaveSettings(JsonValue settings)
        {
            lock (_saver)
                _saver(settings);
        }
    }
}
