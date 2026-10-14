using UnityEngine;

namespace Hulla.Presentation.Text
{
    /// <summary>
    /// Which table to use at start-up (GDD 26.2): a saved choice first (the language setting, or a
    /// pseudo-locale on a development build), else the device language when the build has a table
    /// for it, else English.
    /// </summary>
    public static class LocaleChoice
    {
        public const string PrefKey = "hulla.locale";

        public static string Pick(string? saved, SystemLanguage device, bool developmentBuild)
        {
            if (!string.IsNullOrEmpty(saved) && (developmentBuild || !IsPseudo(saved!)))
            {
                return saved!;
            }

            return FromDevice(device);
        }

        public static bool IsPseudo(string locale) => locale.EndsWith("-XA") || locale.EndsWith("-XB");

        /// <summary>Unity names no Urdu, Hindi or Punjabi; those come from the language setting (#100).</summary>
        public static string FromDevice(SystemLanguage device) => device switch
        {
            SystemLanguage.Arabic => "ar",
            SystemLanguage.Dutch => "nl",
            SystemLanguage.French => "fr",
            _ => Strings.Fallback,
        };

        /// <summary>Applies the choice: the table, or English when the build has none for it.</summary>
        public static void Apply()
        {
            var saved = PlayerPrefs.GetString(PrefKey, "");
            var env = System.Environment.GetEnvironmentVariable("HULLA_LOCALE");
            Strings.Use(Pick(string.IsNullOrEmpty(env) ? saved : env, UnityEngine.Application.systemLanguage, Debug.isDebugBuild));
        }
    }
}
