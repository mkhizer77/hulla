using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Hulla.Infrastructure.Content
{
    /// <summary>content/manifest.json (ADR-0011): every content file with its version, SHA-256 and size.</summary>
    public sealed class ContentManifest
    {
        private readonly Dictionary<string, Entry> _byPath;

        private ContentManifest(long version, string digest, Dictionary<string, Entry> byPath)
        {
            Version = version;
            Digest = digest;
            _byPath = byPath;
        }

        public long Version { get; }

        public string Digest { get; }

        public IEnumerable<Entry> Files => _byPath.Values;

        public Entry? Find(string path) => _byPath.TryGetValue(path, out var entry) ? entry : null;

        /// <summary>Null when the text is not a content manifest.</summary>
        public static ContentManifest? Parse(string json)
        {
            try
            {
                var body = JObject.Parse(json);
                if ((string?)body["id"] != "content-manifest" || body["files"] is not JArray files)
                {
                    return null;
                }

                var byPath = new Dictionary<string, Entry>(StringComparer.Ordinal);
                foreach (var file in files)
                {
                    var path = (string?)file["path"];
                    var sha = (string?)file["sha256"];
                    if (path == null || sha == null || !SafePath(path) || sha.Length != 64)
                    {
                        return null;
                    }

                    byPath[path] = new Entry(path, sha.ToLowerInvariant(), (long?)file["bytes"] ?? -1);
                }

                return new ContentManifest((long?)body["version"] ?? 0, (string?)body["digest"] ?? "", byPath);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Relative paths inside content/ only: no rooted paths, no parent steps.</summary>
        private static bool SafePath(string path) =>
            path.Length > 0 && !path.StartsWith("/") && !path.Contains("..") && !path.Contains("\\") && path.EndsWith(".json");

        public sealed class Entry
        {
            public Entry(string path, string sha256, long bytes)
            {
                Path = path;
                Sha256 = sha256;
                Bytes = bytes;
            }

            public string Path { get; }

            public string Sha256 { get; }

            public long Bytes { get; }
        }
    }
}
