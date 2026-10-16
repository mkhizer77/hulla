using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Voice
{
    /// <summary>The `voice_token` RPC: a short-lived token for the table's channel, or why there is none.</summary>
    public interface IVoiceTokens
    {
        Awaitable<Result<VoiceTicket>> TokenAsync(string matchId, CancellationToken cancellation);
    }
}
