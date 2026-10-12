using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Hulla.Application.Services;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Hulla.Infrastructure.Content
{
    /// <summary>
    /// Reads content/quick-chat and content/emotes from StreamingAssets, where the editor copies them
    /// at import and build time (BuildTools/Editor/ContentSync). Missing files leave the lists empty.
    /// </summary>
    public sealed class StreamingChatCatalog : IChatCatalog
    {
        private const string PhrasesFile = "content/quick-chat/v1.en.json";
        private const string EmotesFile = "content/emotes/v1.json";
        private readonly List<ChatPhrase> _phrases = new List<ChatPhrase>();
        private readonly Dictionary<string, string> _byId = new Dictionary<string, string>();
        private readonly List<string> _emotes = new List<string>();

        public IReadOnlyList<ChatPhrase> Phrases => _phrases;

        public IReadOnlyList<string> Emotes => _emotes;

        public string? TextFor(string phraseId) => _byId.TryGetValue(phraseId, out var text) ? text : null;

        public async Awaitable LoadAsync(CancellationToken cancellation)
        {
            _phrases.Clear();
            _byId.Clear();
            _emotes.Clear();
            var phrases = await ReadAsync(PhrasesFile, cancellation);
            if (phrases?["phrases"] is JArray list)
            {
                foreach (var item in list)
                {
                    var id = (string?)item["id"];
                    var text = (string?)item["roman"];
                    if (id == null || text == null)
                    {
                        continue;
                    }

                    _phrases.Add(new ChatPhrase(id, text, (string?)item["translation"] ?? "", (string?)item["tab"] ?? ""));
                    _byId[id] = text;
                }
            }

            var emotes = await ReadAsync(EmotesFile, cancellation);
            if (emotes?["emotes"] is JArray emoteList)
            {
                foreach (var item in emoteList)
                {
                    var id = (string?)item["id"];
                    if (id != null)
                    {
                        _emotes.Add(id);
                    }
                }
            }
        }

        private static async Awaitable<JObject?> ReadAsync(string relative, CancellationToken cancellation)
        {
            var path = Path.Combine(UnityEngine.Application.streamingAssetsPath, relative);
            var url = path.Contains("://") ? path : "file://" + path;
            using var request = UnityWebRequest.Get(url);
            await request.SendWebRequest();
            cancellation.ThrowIfCancellationRequested();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"content missing: {relative} ({request.error})");
                return null;
            }

            try
            {
                return JObject.Parse(request.downloadHandler.text);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"content unreadable: {relative} ({error.Message})");
                return null;
            }
        }
    }
}
