using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Hulla.Application.Social
{
    /// <summary>
    /// The players this player has muted (GDD 11.6): their text, quick chat and emotes are hidden on
    /// this device. The list lives on the server so it follows the account.
    /// </summary>
    public sealed class MuteList
    {
        private readonly ISocialService _social;
        private readonly HashSet<string> _muted = new HashSet<string>();

        public MuteList(ISocialService social)
        {
            _social = social;
        }

        public event Action? Changed;

        public bool IsMuted(string? userId) => userId != null && _muted.Contains(userId);

        public async Awaitable RefreshAsync(CancellationToken cancellation) => Replace(await _social.MutesAsync(cancellation));

        public async Awaitable SetAsync(string userId, bool muted, CancellationToken cancellation)
        {
            // Hide at once; the server's answer then settles the list.
            if (muted)
            {
                _muted.Add(userId);
            }
            else
            {
                _muted.Remove(userId);
            }

            Changed?.Invoke();
            Replace(await _social.SetMuteAsync(userId, muted, cancellation));
        }

        private void Replace(Domain.Result<IReadOnlyList<string>> mutes)
        {
            if (!mutes.IsOk)
            {
                return;
            }

            _muted.Clear();
            foreach (var id in mutes.Value)
            {
                _muted.Add(id);
            }

            Changed?.Invoke();
        }
    }
}
