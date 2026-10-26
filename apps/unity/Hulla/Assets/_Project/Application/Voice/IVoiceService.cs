using System;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Voice
{
    /// <summary>
    /// The voice provider behind one narrow interface (ADR-0012): one channel at a time, the mic sent or
    /// held, other players muted on this device, and who is speaking. Callbacks arrive on the main thread.
    /// Only Infrastructure knows the provider.
    /// </summary>
    public interface IVoiceService
    {
        /// <summary>False on a build without a voice SDK; the table then hides voice.</summary>
        bool Supported { get; }

        /// <summary>An account starts (true) or stops (false) speaking; the player's own account included.</summary>
        event Action<string, bool>? Speaking;

        /// <summary>The channel was lost and the provider gave up reconnecting.</summary>
        event Action? Dropped;

        /// <summary>The token runs out soon; renew it with a fresh one.</summary>
        event Action? TokenExpiring;

        /// <summary>Asks for the microphone just in time (GDD 11.4); true when it may be used.</summary>
        Awaitable<bool> AskMicrophoneAsync();

        /// <param name="publish">A seat may speak; a spectator only listens.</param>
        Awaitable<Result<bool>> JoinAsync(VoiceTicket ticket, bool publish, CancellationToken cancellation);

        Awaitable LeaveAsync();

        void RenewToken(string token);

        /// <summary>Sends the microphone (true) or holds it (false) while staying in the channel.</summary>
        void SetTransmit(bool on);

        /// <summary>Silences one account on this device; it holds for accounts not in the channel yet.</summary>
        void SetPeerMuted(string account, bool muted);
    }
}
