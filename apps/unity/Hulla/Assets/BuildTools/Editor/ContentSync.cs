using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Hulla.BuildTools
{
    /// <summary>
    /// Copies the content the client ships with (quick chat, emotes) from the repository's content/
    /// folder into StreamingAssets, on every editor load and before every build. The copy is
    /// git-ignored; content/ stays the only source (CODING_RULES 8).
    /// </summary>
    [InitializeOnLoad]
    public sealed class ContentSync : IPreprocessBuildWithReport
    {
        private static readonly string[] Files =
        {
            "quick-chat/v1.en.json",
            "emotes/v1.json",
            "presets/thulla/standard.json",
            "presets/thulla/family.json",
            "presets/thulla/hardcore.json",
            "presets/thulla/classicPagat.json",
        };

        static ContentSync() => Copy();

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Copy();

        public static void Copy()
        {
            var source = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "..", "..", "..", "content"));
            var target = Path.Combine(UnityEngine.Application.streamingAssetsPath, "content");
            foreach (var file in Files)
            {
                var from = Path.Combine(source, file);
                if (!File.Exists(from))
                {
                    Debug.LogWarning("content file missing: " + from);
                    continue;
                }

                var to = Path.Combine(target, file);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                if (!File.Exists(to) || File.ReadAllText(to) != File.ReadAllText(from))
                {
                    File.Copy(from, to, true);
                }
            }
        }
    }
}
