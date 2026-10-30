using System;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Services
{
    /// <summary>Guest sign-in with a device account and the realtime socket that goes with it (GDD 9.1).</summary>
    public interface ISessionService
    {
        PlayerSession? Current { get; }

        /// <summary>Whether the realtime socket is open; iOS drops it while the app is in the background.</summary>
        bool IsConnected { get; }

        /// <summary>A server notice on the socket: its subject and JSON content (an achievement unlock, the daily reward).</summary>
        event Action<string, string>? Notified;

        Awaitable<Result<PlayerSession>> SignInAsync(string displayName, CancellationToken cancellation);

        /// <summary>Opens a fresh socket for the signed-in player, refreshing the session if it expired.</summary>
        Awaitable<Result<bool>> ReconnectAsync(CancellationToken cancellation);
    }
}
