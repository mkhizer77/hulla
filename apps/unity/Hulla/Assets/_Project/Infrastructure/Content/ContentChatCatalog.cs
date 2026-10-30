using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Services;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Content
{
    /// <summary>
    /// Reads content/quick-chat and content/emotes through the content store, so a newer signed pack
    /// replaces the bundled one from the next launch (ADR-0011). Missing files leave the lists empty.
    /// </summary>
    public sealed class ContentChatCatalog : IChatCatalog
    {
        private const string PhrasesFile = "quick-chat/v1.en.json";
        private const string EmotesFile = "emotes/v1.json";
        private readonly IContentStore _content;
        private readonly List<ChatPhrase> _phrases = new List<ChatPhrase>();
        private readonly Dictionary<string, string> _byId = new Dictionary<string, string>();
        private readonly List<string> _emotes = new List<string>();
        private readonly Dictionary<string, string> _packs = new Dictionary<string, string>();

        public ContentChatCatalog(IContentStore content)
        {
            _content = content;
        }

        public IReadOnlyList<ChatPhrase> Phrases => _phrases;

        public IReadOnlyList<string> Emotes => _emotes;

        public string? TextFor(string phraseId) => _byId.TryGetValue(phraseId, out var text) ? text : null;

        public string? EmotePack(string emoteId) => _packs.TryGetValue(emoteId, out var pack) ? pack : null;

        public async Awaitable LoadAsync(CancellationToken cancellation)
        {
            _phrases.Clear();
            _byId.Clear();
            _emotes.Clear();
            _packs.Clear();
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

                    _phrases.Add(new ChatPhrase(id, text, (string?)item["translation"] ?? "", (string?)item["tab"] ?? "", (string?)item["context"] ?? "any"));
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
                        if ((string?)item["pack"] is string pack)
                        {
                            _packs[id] = pack;
                        }
                    }
                }
            }
        }

        private async Awaitable<JObject?> ReadAsync(string path, CancellationToken cancellation)
        {
            var text = await _content.ReadTextAsync(path, cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (text == null)
            {
                Debug.LogWarning($"content missing: {path}");
                return null;
            }

            try
            {
                return JObject.Parse(text);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"content unreadable: {path} ({error.Message})");
                return null;
            }
        }
    }
}
