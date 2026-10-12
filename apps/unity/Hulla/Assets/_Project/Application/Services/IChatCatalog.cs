using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Hulla.Application.Services
{
    /// <summary>The quick-chat phrases and emote ids the server accepts, loaded from the shipped content.</summary>
    public interface IChatCatalog
    {
        IReadOnlyList<ChatPhrase> Phrases { get; }

        IReadOnlyList<string> Emotes { get; }

        string? TextFor(string phraseId);

        Awaitable LoadAsync(CancellationToken cancellation);
    }
}
