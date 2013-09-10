using RT.Util.Json;

namespace RT.PropellerApi
{
    public interface ISettingsSaver
    {
        void SaveSettings(JsonValue settings);
    }
}
