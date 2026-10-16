using System;
using System.Threading;
using Hulla.Application.Voice;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Infrastructure.Voice
{
    /// <summary>
    /// Voice without a provider, for the editor and `-hulla-fake-voice` builds: joins succeed, no audio
    /// moves, and the player's own account "speaks" while the mic is sent, so the mic button, the pill and
    /// the speaking ring can be checked without the SDK or an Agora project.
    /// </summary>
    public sealed class FakeVoiceService : IVoiceService
    {
        private string? _account;
        private bool _transmitting;

        public bool Supported => true;

        public event Action<string, bool>? Speaking;

        public event Action? Dropped;

        public event Action? TokenExpiring;

        public bool InChannel => _account != null;

        public async Awaitable<bool> AskMicrophoneAsync()
        {
            await Awaitable.NextFrameAsync();
            return true;
        }

        public async Awaitable<Result<bool>> JoinAsync(VoiceTicket ticket, bool publish, CancellationToken cancellation)
        {
            await Awaitable.NextFrameAsync(cancellation);
            _account = ticket.Account;
            return Result<bool>.Ok(true);
        }

        public async Awaitable LeaveAsync()
        {
            SetTransmit(false);
            _account = null;
            await Awaitable.NextFrameAsync();
        }

        public void RenewToken(string token)
        {
        }

        public void SetTransmit(bool on)
        {
            if (_account == null || _transmitting == on)
            {
                return;
            }

            _transmitting = on;
            Speaking?.Invoke(_account, on);
        }

        public void SetPeerMuted(string account, bool muted)
        {
        }

        /// <summary>Simulates the provider giving up on the channel.</summary>
        public void Drop()
        {
            _account = null;
            _transmitting = false;
            Dropped?.Invoke();
        }

        /// <summary>Simulates the token nearing its end.</summary>
        public void ExpireSoon() => TokenExpiring?.Invoke();
    }
}
