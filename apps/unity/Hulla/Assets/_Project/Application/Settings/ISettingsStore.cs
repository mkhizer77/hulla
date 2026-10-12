namespace Hulla.Application.Settings
{
    /// <summary>Where settings live between runs (player prefs on device; a dictionary in tests).</summary>
    public interface ISettingsStore
    {
        int GetInt(string key, int fallback);

        void SetInt(string key, int value);

        void Save();
    }
}
