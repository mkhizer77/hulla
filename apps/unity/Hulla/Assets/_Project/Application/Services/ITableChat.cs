using System;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Services
{
    /// <summary>
    /// Free text on the table's channel (GDD 11.1, ADR-0018). The server refuses long messages, links,
    /// phone numbers and fast senders; a refusal comes back as the result's error code.
    /// </summary>
    public interface ITableChat
    {
        public const int MaxLength = 140;

        event Action<TextLine>? Received;

        Awaitable<Result<bool>> JoinAsync(string matchId, CancellationToken cancellation);

        Awaitable<Result<bool>> SendAsync(string text, CancellationToken cancellation);

        Awaitable LeaveAsync();
    }
}
