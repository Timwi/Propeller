using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RT.PropellerApi;
using RT.Util.Json;

namespace RT.PropellerApi
{
    public sealed class SettingsSaver : MarshalByRefObject, ISettingsSaver
    {
        private Action<JsonValue> _saver;

        public SettingsSaver(Action<JsonValue> saver)
        {
            if (saver == null)
                throw new ArgumentNullException("saver");
            _saver = saver;
        }

        public void SaveSettings(JsonValue settings)
        {
            _saver(settings);
        }
    }
}
