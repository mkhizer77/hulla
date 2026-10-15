using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Hulla.BuildTools
{
    /// <summary>
    /// Local builds from the command line. Release builds run in CI from a tag (ADR-0015); these are
    /// development builds for play-testing, signed with Unity's debug key:
    /// <c>Unity -batchmode -quit -projectPath apps/unity/Hulla -executeMethod Hulla.BuildTools.BuildCommands.AndroidDevelopment</c>.
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
        /// The development APK. It starts pointed at this Mac (HULLA_DEV_SERVER, else the Mac's first
        /// private IPv4 address, port 7350) so a fresh install reaches the local server without typing.
        /// </summary>
        public static void AndroidDevelopment()
        {
            var server = Environment.GetEnvironmentVariable("HULLA_DEV_SERVER") ?? LanAddress() + ":7350";
            Directory.CreateDirectory(Path.GetDirectoryName(DevServerAsset)!);
            File.WriteAllText(DevServerAsset, server);
            AssetDatabase.ImportAsset(DevServerAsset);
            var succeeded = false;
            try
            {
                succeeded = Build(BuildTarget.Android, Path.Combine("Builds", "Android", "hulla-dev.apk"));
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

            ExitIfBatch(succeeded);
        }

        /// <summary>Read by AppConfig in development builds; written only for the build, never committed.</summary>
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

        public static void MacDevelopment() =>
            ExitIfBatch(Build(BuildTarget.StandaloneOSX, Path.Combine("Builds", "macOS", "Hulla.app")));

        private static void ExitIfBatch(bool succeeded)
        {
            if (UnityEngine.Application.isBatchMode)
            {
                EditorApplication.Exit(succeeded ? 0 : 1);
            }
        }

        private static bool Build(BuildTarget target, string defaultPath)
        {
            var path = Environment.GetEnvironmentVariable("HULLA_BUILD_OUT") ?? defaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            EditorUserBuildSettings.buildAppBundle = false;
            // Addressables builds its bundles for the active platform. Switching here reloads the
            // scripts mid-build and the content build fails, so the editor must start on the target
            // (-buildTarget Android or -buildTarget OSXUniversal).
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                Debug.LogError($"build {target}: the editor is on {EditorUserBuildSettings.activeBuildTarget}; start it with -buildTarget for {target}");
                return false;
            }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = path,
                target = target,
                options = BuildOptions.Development,
            });
            Debug.Log($"build {target}: {report.summary.result}, {report.summary.totalErrors} errors, {path}");
            return report.summary.result == BuildResult.Succeeded;
        }
    }
}
