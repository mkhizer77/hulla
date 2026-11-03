using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Hulla.BuildTools
{
    /// <summary>
    /// Ads setup before every build (docs/runbooks/push-and-purchases-setup.md): AppLovin's settings take the
    /// SDK key from HULLA_APPLOVIN_SDK_KEY, else the git-ignored HullaAds.local.json beside Assets.
    /// BuildCommands clears it again after the build so it is never committed. The Quality Service
    /// plugin stays off: it calls AppLovin during the build.
    /// </summary>
    public sealed class AdsSetup : IPreprocessBuildWithReport
    {
        private const string LocalKeyPath = "HullaAds.local.json";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Apply(SdkKey());

        private static string SdkKey()
        {
            var key = Environment.GetEnvironmentVariable("HULLA_APPLOVIN_SDK_KEY");
            if (string.IsNullOrEmpty(key) && File.Exists(LocalKeyPath))
            {
                key = (string?)JObject.Parse(File.ReadAllText(LocalKeyPath))["applovinSdkKey"];
            }

            return key ?? "";
        }

        /// <summary>Takes the SDK key back out of AppLovin's settings after a build.</summary>
        public static void ClearKey() => Apply("");

        private static void Apply(string sdkKey)
        {
            var settings = AppLovinSettings.Instance;
            if (settings.SdkKey == sdkKey && !settings.QualityServiceEnabled)
            {
                return;
            }

            settings.SdkKey = sdkKey;
            settings.QualityServiceEnabled = false;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
    }
}
