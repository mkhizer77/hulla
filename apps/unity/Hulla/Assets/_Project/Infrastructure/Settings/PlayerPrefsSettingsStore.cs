using Hulla.Application.Settings;
using UnityEngine;

namespace Hulla.Infrastructure.Settings
{
    /// <summary>Settings in Unity's player prefs; nothing here is secret or personal.</summary>
    public sealed class PlayerPrefsSettingsStore : ISettingsStore
    {
        public int GetInt(string key, int fallback) => PlayerPrefs.GetInt(key, fallback);

        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);

        public void Save() => PlayerPrefs.Save();
    }
}
