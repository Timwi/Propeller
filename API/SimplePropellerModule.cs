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
    public abstract class SimplePropellerModule<TSettings> : IPropellerModule where TSettings : class, new()
    {
        public abstract string Name { get; }

        protected TSettings Settings { get; private set; }

        void IPropellerModule.Init(LoggerBase log, JsonValue settings, ISettingsSaver saver)
        {
            Settings = ClassifyJson.Deserialize<TSettings>(settings) ?? new TSettings();
            saver.SaveSettings(ClassifyJson.Serialize(Settings));
            Init(log);
        }

        public virtual void Init(LoggerBase log) { }

        public virtual string[] FileFiltersToBeMonitoredForChanges { get { return null; } }

        public abstract HttpResponse Handle(HttpRequest req);

        public virtual bool MustReinitialize { get { return false; } }

        public virtual void Shutdown() { }
    }
}
