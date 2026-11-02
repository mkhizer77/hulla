using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Callbacks;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace Hulla.BuildTools
{
    /// <summary>
    /// Firebase starts with collection off on both platforms (GDD 23.6): no Analytics, no Crashlytics and
    /// no advertising id until FirebaseServices.SetConsent hears the player's yes. Ad storage, ad user
    /// data and ad personalisation stay denied; the app runs no ads through Firebase.
    /// </summary>
    public sealed class FirebaseConsentDefaults : IPostGenerateGradleAndroidProject
    {
        private const string Android = "http://schemas.android.com/apk/res/android";

        private static readonly Dictionary<string, string> AndroidMeta = new Dictionary<string, string>
        {
            ["firebase_analytics_collection_enabled"] = "false",
            ["firebase_crashlytics_collection_enabled"] = "false",
            ["google_analytics_adid_collection_enabled"] = "false",
            ["google_analytics_default_allow_analytics_storage"] = "false",
            ["google_analytics_default_allow_ad_storage"] = "false",
            ["google_analytics_default_allow_ad_user_data"] = "false",
            ["google_analytics_default_allow_ad_personalization_signals"] = "false",
        };

        private static readonly string[] IosOff =
        {
            "FIREBASE_ANALYTICS_COLLECTION_ENABLED",
            "FirebaseCrashlyticsCollectionEnabled",
            "GOOGLE_ANALYTICS_ADID_COLLECTION_ENABLED",
            "GOOGLE_ANALYTICS_DEFAULT_ALLOW_ANALYTICS_STORAGE",
            "GOOGLE_ANALYTICS_DEFAULT_ALLOW_AD_STORAGE",
            "GOOGLE_ANALYTICS_DEFAULT_ALLOW_AD_USER_DATA",
            "GOOGLE_ANALYTICS_DEFAULT_ALLOW_AD_PERSONALIZATION_SIGNALS",
        };

        public int callbackOrder => 3;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            var document = new XmlDocument();
            document.Load(manifestPath);
            var application = document.SelectSingleNode("/manifest/application")!;
            foreach (var pair in AndroidMeta)
            {
                XmlElement? meta = null;
                foreach (XmlElement node in application.SelectNodes("meta-data")!)
                {
                    if (node.GetAttribute("name", Android) == pair.Key)
                    {
                        meta = node;
                    }
                }

                if (meta == null)
                {
                    meta = document.CreateElement("meta-data");
                    meta.SetAttribute("name", Android, pair.Key);
                    application.AppendChild(meta);
                }

                meta.SetAttribute("value", Android, pair.Value);
            }

            document.Save(manifestPath);
        }

        [PostProcessBuild(6)]
        public static void OnPostProcessBuild(BuildTarget target, string path)
        {
#if UNITY_IOS
            if (target != BuildTarget.iOS)
            {
                return;
            }

            var plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            foreach (var key in IosOff)
            {
                plist.root.SetBoolean(key, false);
            }

            plist.WriteToFile(plistPath);
#endif
        }
    }
}
