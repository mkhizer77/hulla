using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Protocol;
using TableSettings = Hulla.Protocol.Settings;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Content
{
    /// <summary>Reads content/presets/thulla through the content store (ADR-0011).</summary>
    public sealed class ContentPresetCatalog : IPresetCatalog
    {
        public static readonly string[] Ids = { "standard", "family", "hardcore", "classicPagat" };

        private readonly Dictionary<string, (ThullaRules Rules, TableSettings Settings)> _presets =
            new Dictionary<string, (ThullaRules Rules, TableSettings Settings)>();

        private readonly IContentStore _content;

        public ContentPresetCatalog(IContentStore content)
        {
            _content = content;
        }

        public (ThullaRules Rules, TableSettings Settings)? Find(string presetId) =>
            _presets.TryGetValue(presetId, out var preset) ? preset : ((ThullaRules, TableSettings)?)null;

        public async Awaitable LoadAsync(CancellationToken cancellation)
        {
            var serializer = ProtocolJson.CreateSerializer();
            foreach (var id in Ids)
            {
                var text = await _content.ReadTextAsync("presets/thulla/" + id + ".json", cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (text == null)
                {
                    Debug.LogWarning($"preset missing: {id}");
                    continue;
                }

                try
                {
                    var json = JObject.Parse(text);
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
