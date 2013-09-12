using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RT.PropellerApi;
using RT.Util.Json;

namespace RT.PropellerApi
{
    /// <summary>Provides a simple implementation for the <see cref="ISettingsSaver"/> interface.</summary>
    public sealed class SettingsSaver : MarshalByRefObject, ISettingsSaver
    {
        private Action<JsonValue> _saver;

        /// <summary>
        ///     Constructor.</summary>
        /// <param name="saver">
        ///     Delegate that saves the settings.</param>
        public SettingsSaver(Action<JsonValue> saver)
        {
            if (saver == null)
                throw new ArgumentNullException("saver");
            _saver = saver;
        }

        /// <summary>
        ///     Saves the settings. (Implements <see cref="ISettingsSaver.SaveSettings"/>.)</summary>
        /// <param name="settings">
        ///     Settings to save.</param>
        public void SaveSettings(JsonValue settings)
        {
            _saver(settings);
        }
    }
}
