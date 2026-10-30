using System;
using System.Collections.Generic;
using System.IO;
using Facebook.Unity.Settings;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEngine;
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace Hulla.BuildTools
{
    /// <summary>
    /// Sign-in setup before every build (docs/runbooks/sign-in-setup.md): Meta's settings asset and Android
    /// manifest take the Facebook app id from Infrastructure/Social/Resources/HullaSocial.json and the client
    /// token from HULLA_FACEBOOK_CLIENT_TOKEN, else the git-ignored HullaSocial.local.json beside Assets.
    /// BuildCommands clears the token again after the build so it is never committed. The Xcode
    /// project links AuthenticationServices and Contacts, says why it asks for contacts and, once the paid
    /// Apple account exists, adds Sign in with Apple.
    /// </summary>
    public sealed class SocialSetup : IPreprocessBuildWithReport
    {
        public const string ConfigPath = "Assets/_Project/Infrastructure/Social/Resources/HullaSocial.json";
        private const string FacebookSettingsPath = "Assets/FacebookSDK/SDK/Resources/FacebookSettings.asset";
        private const string LocalTokenPath = "HullaSocial.local.json";
        private const string Manifest = "Assets/Plugins/Android/AndroidManifest.xml";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Apply();

        private static JObject Config() => File.Exists(ConfigPath) ? JObject.Parse(File.ReadAllText(ConfigPath)) : new JObject();

        private static string ClientToken()
        {
            var token = Environment.GetEnvironmentVariable("HULLA_FACEBOOK_CLIENT_TOKEN");
            if (string.IsNullOrEmpty(token) && File.Exists(LocalTokenPath))
            {
                token = (string?)JObject.Parse(File.ReadAllText(LocalTokenPath))["facebookClientToken"];
            }

            return token ?? "";
        }

        [MenuItem("Hulla/Apply sign-in settings")]
        public static void Apply() => Apply(ClientToken());

        /// <summary>Takes the client token back out of Meta's settings and the manifest after a build.</summary>
        public static void ClearToken() => Apply("");

        private static void Apply(string clientToken)
        {
            var config = Config();
            var appId = (string?)config["facebookAppId"] ?? "";
            if (appId.Length == 0)
            {
                return;
            }

            var settings = FacebookSettings.NullableInstance;
            if (settings == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FacebookSettingsPath)!);
                settings = ScriptableObject.CreateInstance<FacebookSettings>();
                AssetDatabase.CreateAsset(settings, FacebookSettingsPath);
            }

            FacebookSettings.AppIds = new List<string> { appId };
            if (clientToken.Length == 0 && BuildPipeline.isBuildingPlayer)
            {
                Debug.LogWarning("Facebook sign-in needs the client token: set HULLA_FACEBOOK_CLIENT_TOKEN or HullaSocial.local.json (docs/runbooks/sign-in-setup.md)");
            }

            FacebookSettings.ClientTokens = new List<string> { clientToken };
            FacebookSettings.AppLabels = new List<string> { "Hulla" };
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Facebook.Unity.Editor.ManifestMod.GenerateManifest();
            // Meta's manifest template marks the app debuggable; Unity sets that per build type itself.
            if (File.Exists(Manifest))
            {
                File.WriteAllText(Manifest, File.ReadAllText(Manifest).Replace(" android:debuggable=\"true\"", ""));
            }
        }

#if UNITY_IOS
        [PostProcessBuild(4)]
        public static void OnPostProcessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }

            var projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "AuthenticationServices.framework", false);
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "Contacts.framework", false);
            project.WriteToFile(projectPath);

            // Why the app asks for contacts (people you may know); iOS refuses the request without it.
            var plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("NSContactsUsageDescription", "Hulla checks which of your contacts play, so you can add them as friends. Numbers are only compared, never stored.");
            plist.WriteToFile(plistPath);
            if ((bool?)Config()["appleSignIn"] == true)
            {
                // The capability needs the paid Apple Developer account; a personal team cannot sign with it.
                var capabilities = new ProjectCapabilityManager(projectPath, "Unity-iPhone.entitlements", null, project.GetUnityMainTargetGuid());
                capabilities.AddSignInWithApple();
                capabilities.WriteToFile();
            }
        }
#endif
    }
}
