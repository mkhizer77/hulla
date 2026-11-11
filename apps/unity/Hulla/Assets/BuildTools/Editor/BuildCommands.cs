using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Hulla.BuildTools
{
    /// <summary>
    /// Local builds from the command line. Development builds for play-testing are signed with Unity's
    /// debug key:
    /// <c>Unity -batchmode -quit -projectPath apps/unity/Hulla -executeMethod Hulla.BuildTools.BuildCommands.AndroidDevelopment</c>.
    /// <see cref="AndroidRelease"/> makes the signed Play bundle (docs/runbooks/play-release.md).
    /// </summary>
    public static class BuildCommands
    {
        private static readonly string[] Scenes =
        {
            "Assets/Scenes/Boot.unity",
            "Assets/Scenes/Main.unity",
            "Assets/Scenes/Table.unity",
        };

        /// <summary>
        /// The development APK. It starts pointed at the server from <see cref="DevServer"/>, by default
        /// this Mac's first private IPv4 address on port 7350, so a fresh install connects without typing.
        /// </summary>
        public static void AndroidDevelopment() =>
            ExitIfBatch(WithDevServer(() => Build(BuildTarget.Android, Path.Combine("Builds", "Android", "hulla-dev.apk"))));

        /// <summary>
        /// The signed Android App Bundle for Google Play. It needs an https server (HULLA_DEV_SERVER or
        /// HullaServer.local.json), the upload keystore (HULLA_KEYSTORE, default
        /// ~/Documents/hulla-signing/hulla-upload.keystore; HULLA_KEYSTORE_PASS; HULLA_KEYALIAS, default
        /// hulla-upload; HULLA_KEYALIAS_PASS, default the store password) and HULLA_VERSION_CODE, with
        /// HULLA_VERSION for the version name. Project settings are put back after the build.
        /// </summary>
        public static void AndroidRelease()
        {
            var (server, key) = DevServer();
            var keystore = Environment.GetEnvironmentVariable("HULLA_KEYSTORE")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "hulla-signing", "hulla-upload.keystore");
            var storePass = Environment.GetEnvironmentVariable("HULLA_KEYSTORE_PASS") ?? "";
            var alias = Environment.GetEnvironmentVariable("HULLA_KEYALIAS") ?? "hulla-upload";
            var aliasPass = Environment.GetEnvironmentVariable("HULLA_KEYALIAS_PASS") ?? storePass;
            var versionName = Environment.GetEnvironmentVariable("HULLA_VERSION") ?? PlayerSettings.bundleVersion;
            if (!server.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || key.Length == 0)
            {
                Debug.LogError("release build: set an https server and its key (HULLA_DEV_SERVER and HULLA_DEV_SERVER_KEY, or HullaServer.local.json)");
                ExitIfBatch(false);
                return;
            }

            if (!File.Exists(keystore) || storePass.Length == 0)
            {
                Debug.LogError("release build: the upload keystore or HULLA_KEYSTORE_PASS is missing");
                ExitIfBatch(false);
                return;
            }

            if (!int.TryParse(Environment.GetEnvironmentVariable("HULLA_VERSION_CODE"), out var versionCode) || versionCode < 1)
            {
                Debug.LogError("release build: set HULLA_VERSION_CODE to a number above the last upload");
                ExitIfBatch(false);
                return;
            }

            var saved = (
                PlayerSettings.Android.useCustomKeystore,
                PlayerSettings.Android.keystoreName,
                PlayerSettings.Android.keyaliasName,
                PlayerSettings.Android.bundleVersionCode,
                PlayerSettings.bundleVersion,
                EditorUserBuildSettings.androidCreateSymbols);
            var succeeded = false;
            try
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystore;
                PlayerSettings.Android.keystorePass = storePass;
                PlayerSettings.Android.keyaliasName = alias;
                PlayerSettings.Android.keyaliasPass = aliasPass;
                PlayerSettings.Android.bundleVersionCode = versionCode;
                PlayerSettings.bundleVersion = versionName;
                // Play shows native crashes with function names only when the bundle carries symbols.
                EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Public;
                var path = Path.Combine("Builds", "Android", $"hulla-{versionName}-{versionCode}.aab");
                succeeded = WithServer(server, key, () => Build(BuildTarget.Android, path, BuildOptions.None, appBundle: true));
            }
            finally
            {
                PlayerSettings.Android.useCustomKeystore = saved.useCustomKeystore;
                PlayerSettings.Android.keystoreName = saved.keystoreName;
                PlayerSettings.Android.keystorePass = "";
                PlayerSettings.Android.keyaliasName = saved.keyaliasName;
                PlayerSettings.Android.keyaliasPass = "";
                PlayerSettings.Android.bundleVersionCode = saved.bundleVersionCode;
                PlayerSettings.bundleVersion = saved.bundleVersion;
                EditorUserBuildSettings.androidCreateSymbols = saved.androidCreateSymbols;
            }

            ExitIfBatch(succeeded);
        }

        /// <summary>
        /// The development Xcode project, pointed at this Mac like the APK. Signing uses
        /// HULLA_IOS_TEAM (automatic signing); Xcode builds and installs it on the phone.
        /// </summary>
        public static void IOSDevelopment()
        {
            var team = Environment.GetEnvironmentVariable("HULLA_IOS_TEAM");
            if (!string.IsNullOrEmpty(team))
            {
                PlayerSettings.iOS.appleDeveloperTeamID = team;
                PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            }

            ExitIfBatch(WithDevServer(() => Build(BuildTarget.iOS, Path.Combine("Builds", "iOS", "Xcode"))));
        }

        /// <summary>
        /// The server a development build starts at: HULLA_DEV_SERVER (with HULLA_DEV_SERVER_KEY), else
        /// the git-ignored HullaServer.local.json (<c>{"server": "https://api…", "serverKey": "…"}</c>,
        /// for the phase 0 box), else this Mac's LAN address with the local key.
        /// </summary>
        private static (string Server, string Key) DevServer()
        {
            var server = Environment.GetEnvironmentVariable("HULLA_DEV_SERVER");
            var key = Environment.GetEnvironmentVariable("HULLA_DEV_SERVER_KEY");
            if (string.IsNullOrEmpty(server) && File.Exists(DevServerLocalFile))
            {
                var local = JsonUtility.FromJson<DevServerFile>(File.ReadAllText(DevServerLocalFile));
                server = local.server;
                key = string.IsNullOrEmpty(key) ? local.serverKey : key;
            }

            return string.IsNullOrEmpty(server) ? (LanAddress() + ":7350", "") : (server!, key ?? "");
        }

        [Serializable]
        private sealed class DevServerFile
        {
            public string server = "";
            public string serverKey = "";
        }

        private const string DevServerLocalFile = "HullaServer.local.json";

        private static bool WithDevServer(Func<bool> build)
        {
            var (server, key) = DevServer();
            return WithServer(server, key, build);
        }

        private static bool WithServer(string server, string key, Func<bool> build)
        {
            Debug.Log($"build for {server}");
            Directory.CreateDirectory(Path.GetDirectoryName(DevServerAsset)!);
            File.WriteAllText(DevServerAsset, key.Length > 0 ? server + "\n" + key : server);
            AssetDatabase.ImportAsset(DevServerAsset);
            var succeeded = false;
            try
            {
                succeeded = build();
            }
            finally
            {
                AssetDatabase.DeleteAsset(DevServerAsset);
                var folder = Path.GetDirectoryName(DevServerAsset)!;
                if (AssetDatabase.IsValidFolder(folder) && AssetDatabase.FindAssets("", new[] { folder }).Length == 0)
                {
                    AssetDatabase.DeleteAsset(folder);
                }
            }

            return succeeded;
        }

        /// <summary>Read by AppConfig; written only for the build, never committed.</summary>
        private const string DevServerAsset = "Assets/Resources/hulla-dev-server.txt";

        private static string LanAddress()
        {
            // Wi-Fi and Ethernet (en0, en1) before VPN tunnels and bridges.
            var nics = System.Linq.Enumerable.OrderBy(
                System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces(),
                nic => nic.Name.StartsWith("en", StringComparison.Ordinal) ? 0 : 1);
            foreach (var nic in nics)
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                {
                    continue;
                }

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    var bytes = unicast.Address.GetAddressBytes();
                    var isPrivate = bytes.Length == 4 && (bytes[0] == 10 || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31));
                    if (isPrivate)
                    {
                        return unicast.Address.ToString();
                    }
                }
            }

            return "127.0.0.1";
        }

        /// <summary>
        /// The development Mac app, Apple Silicon only: the voice SDK ships one dylib per CPU, and a
        /// universal build includes neither, so voice would fail to load.
        /// </summary>
        public static void MacDevelopment()
        {
#if UNITY_EDITOR_OSX
            // The macOS build module, and with it this setting, exists only in a Mac editor.
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.ARM64;
#endif
            ExitIfBatch(Build(BuildTarget.StandaloneOSX, Path.Combine("Builds", "macOS", "Hulla.app")));
        }

        private static void ExitIfBatch(bool succeeded)
        {
            if (UnityEngine.Application.isBatchMode)
            {
                EditorApplication.Exit(succeeded ? 0 : 1);
            }
        }

        private static bool Build(BuildTarget target, string defaultPath, BuildOptions options = BuildOptions.Development, bool appBundle = false)
        {
            var path = Environment.GetEnvironmentVariable("HULLA_BUILD_OUT") ?? defaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            EditorUserBuildSettings.buildAppBundle = appBundle;
            // Addressables builds its bundles for the active platform. Switching here reloads the
            // scripts mid-build and the content build fails, so the editor must start on the target
            // (-buildTarget Android or -buildTarget OSXUniversal).
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                Debug.LogError($"build {target}: the editor is on {EditorUserBuildSettings.activeBuildTarget}; start it with -buildTarget for {target}");
                return false;
            }

            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = Scenes,
                    locationPathName = path,
                    target = target,
                    options = options,
                });
            }
            finally
            {
                SocialSetup.ClearToken();
                AdsSetup.ClearKey();
                EditorUserBuildSettings.buildAppBundle = false;
            }

            Debug.Log($"build {target}: {report.summary.result}, {report.summary.totalErrors} errors, {path}");
            return report.summary.result == BuildResult.Succeeded;
        }
    }
}
