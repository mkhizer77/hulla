using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Protocol;
using TableSettings = Hulla.Protocol.Settings;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Hulla.Infrastructure.Content
{
    /// <summary>Reads content/presets/thulla from StreamingAssets (copied by BuildTools/Editor/ContentSync).</summary>
    public sealed class StreamingPresetCatalog : IPresetCatalog
    {
        public static readonly string[] Ids = { "standard", "family", "hardcore", "classicPagat" };

        private readonly Dictionary<string, (ThullaRules Rules, TableSettings Settings)> _presets =
            new Dictionary<string, (ThullaRules Rules, TableSettings Settings)>();

        public (ThullaRules Rules, TableSettings Settings)? Find(string presetId) =>
            _presets.TryGetValue(presetId, out var preset) ? preset : ((ThullaRules, TableSettings)?)null;

        public async Awaitable LoadAsync(CancellationToken cancellation)
        {
            var serializer = ProtocolJson.CreateSerializer();
            foreach (var id in Ids)
            {
                var path = Path.Combine(UnityEngine.Application.streamingAssetsPath, "content/presets/thulla", id + ".json");
                using var request = UnityWebRequest.Get(path.Contains("://") ? path : "file://" + path);
                await request.SendWebRequest();
                cancellation.ThrowIfCancellationRequested();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"preset missing: {id} ({request.error})");
                    continue;
                }

                try
                {
                    var json = JObject.Parse(request.downloadHandler.text);
                    var rules = (json["overrides"] as JObject ?? new JObject()).ToObject<ThullaRules>(serializer) ?? new ThullaRules();
                    var settings = (json["settings"] as JObject ?? new JObject()).ToObject<TableSettings>(serializer) ?? new TableSettings();
                    _presets[id] = (rules, settings);
                }
                catch (Exception error)
                {
                    Debug.LogWarning($"preset unreadable: {id} ({error.Message})");
                }
            }
        }
    }
}
