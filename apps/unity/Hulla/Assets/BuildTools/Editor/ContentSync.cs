using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Hulla.BuildTools
{
    /// <summary>
    /// Copies the base content the client ships with (ADR-0011) from the repository's content/
    /// folder into StreamingAssets, on every editor load and before every build: the manifest and
    /// every file it lists. The copy is git-ignored; content/ stays the only source (CODING_RULES 8).
    /// </summary>
    [InitializeOnLoad]
    public sealed class ContentSync : IPreprocessBuildWithReport
    {
        private const string Manifest = "manifest.json";

        static ContentSync() => Copy();

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Copy();

        public static void Copy()
        {
            var source = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "..", "..", "..", "content"));
            var target = Path.Combine(UnityEngine.Application.streamingAssetsPath, "content");
            var manifestPath = Path.Combine(source, Manifest);
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning("content manifest missing: " + manifestPath);
                return;
            }

            var files = new List<string> { Manifest };
            foreach (var entry in JObject.Parse(File.ReadAllText(manifestPath))["files"] ?? new JArray())
            {
                files.Add((string)entry["path"]!);
            }

            foreach (var file in files)
            {
                var from = Path.Combine(source, file);
                if (!File.Exists(from))
                {
                    Debug.LogWarning("content file missing: " + from);
                    continue;
                }

                var to = Path.Combine(target, file);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                if (!File.Exists(to) || !File.ReadAllBytes(to).AsSpan().SequenceEqual(File.ReadAllBytes(from)))
                {
                    File.Copy(from, to, true);
                }
            }
        }
    }
}
