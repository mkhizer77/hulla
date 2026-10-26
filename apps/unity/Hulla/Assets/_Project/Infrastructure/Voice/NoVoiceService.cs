using System;
using System.Threading;
using Hulla.Application.Voice;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Infrastructure.Voice
{
    /// <summary>A build without the voice SDK: the table hides voice and play goes on.</summary>
    public sealed class NoVoiceService : IVoiceService
    {
        public bool Supported => false;

        public event Action<string, bool>? Speaking { add { } remove { } }

        public event Action? Dropped { add { } remove { } }

        public event Action? TokenExpiring { add { } remove { } }

        public async Awaitable<bool> AskMicrophoneAsync()
        {
            await Awaitable.NextFrameAsync();
            return false;
        }

        public async Awaitable<Result<bool>> JoinAsync(VoiceTicket ticket, bool publish, CancellationToken cancellation)
        {
            await Awaitable.NextFrameAsync(cancellation);
            return Result<bool>.Fail("voice.unavailable", "no_sdk");
        }

        public async Awaitable LeaveAsync() => await Awaitable.NextFrameAsync();

        public void RenewToken(string token)
        {
        }

        public void SetTransmit(bool on)
        {
        }

        public void SetPeerMuted(string account, bool muted)
        {
        }
    }
}
