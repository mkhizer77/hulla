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

        public static void AndroidDevelopment() =>
            Build(BuildTarget.Android, Path.Combine("Builds", "Android", "hulla-dev.apk"));

        public static void MacDevelopment() =>
            Build(BuildTarget.StandaloneOSX, Path.Combine("Builds", "macOS", "Hulla.app"));

        private static void Build(BuildTarget target, string defaultPath)
        {
            var path = Environment.GetEnvironmentVariable("HULLA_BUILD_OUT") ?? defaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            EditorUserBuildSettings.buildAppBundle = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = path,
                target = target,
                options = BuildOptions.Development,
            });
            Debug.Log($"build {target}: {report.summary.result}, {report.summary.totalErrors} errors, {path}");
            if (UnityEngine.Application.isBatchMode)
            {
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
        }
    }
}
