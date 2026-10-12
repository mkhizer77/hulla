using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Social;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>
    /// Friends on Home (GDD 14.10): refreshed every 5 s while Home shows; the first joinable friend's
    /// table becomes the hero ("Join Ali's table (2 seats open)"). Time comes in through Tick.
    /// </summary>
    public sealed class HomeFriendsViewModel
    {
        public const float RefreshSeconds = 5f;

        private readonly ISocialService _social;
        private readonly List<FriendPresence> _online = new List<FriendPresence>();
        private float _sinceRefresh = RefreshSeconds;
        private bool _refreshing;

        public HomeFriendsViewModel(ISocialService social)
        {
            _social = social;
        }

        public event Action? Changed;

        /// <summary>True while Home shows and the player is signed in.</summary>
        public bool Active { get; set; }

        /// <summary>The friend whose table to offer, or null.</summary>
        public FriendPresence? Hero { get; private set; }

        /// <summary>Friends who are online in any way, joinable lobbies first.</summary>
        public IReadOnlyList<FriendPresence> Online => _online;

        public void Tick(float deltaSeconds, CancellationToken cancellation)
        {
            if (!Active || _refreshing)
            {
                return;
            }

            _sinceRefresh += deltaSeconds;
            if (_sinceRefresh >= RefreshSeconds)
            {
                _sinceRefresh = 0f;
                _ = RefreshAsync(cancellation);
            }
        }

        public void RefreshSoon() => _sinceRefresh = RefreshSeconds;

        public async Awaitable RefreshAsync(CancellationToken cancellation)
        {
            _refreshing = true;
            try
            {
                var friends = await _social.PresenceAsync(cancellation);
                if (!friends.IsOk)
                {
                    return;
                }

                _online.Clear();
                Hero = null;
                foreach (var friend in friends.Value)
                {
                    if (friend.State == PresenceState.Offline || friend.State == PresenceState.Hidden)
                    {
                        continue;
                    }

                    _online.Add(friend);
                    if (Hero == null && friend.Joinable && friend.TableCode != null)
                    {
                        Hero = friend;
                    }
                }

                Changed?.Invoke();
            }
            finally
            {
                _refreshing = false;
            }
        }
    }
}
