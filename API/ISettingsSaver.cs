using RT.Util.Json;

namespace RT.PropellerApi
{
    /// <summary>Provides an interface for Propeller modules to save their settings.</summary>
    public interface ISettingsSaver
    {
        /// <summary>
        ///     Saves the specified settings.</summary>
        /// <param name="settings">
        ///     Settings to save.</param>
        void SaveSettings(JsonValue settings);
    }
}
