using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Social;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>
    /// The friends screen (GDD 10.3): requests in and out, friends with favourites first, blocks, and
    /// recent co-players who are not friends yet. Every action refreshes the lists from the server.
    /// </summary>
    public sealed class FriendsViewModel
    {
        private readonly ISocialService _social;
        private readonly List<FriendEntry> _received = new List<FriendEntry>();
        private readonly List<FriendEntry> _sent = new List<FriendEntry>();
        private readonly List<FriendEntry> _friends = new List<FriendEntry>();
        private readonly List<FriendEntry> _blocked = new List<FriendEntry>();
        private readonly List<FriendEntry> _recent = new List<FriendEntry>();

        public FriendsViewModel(ISocialService social)
        {
            _social = social;
        }

        public event Action? Changed;

        public bool Busy { get; private set; }

        public string? ErrorCode { get; private set; }

        /// <summary>Set after a successful add by code, so the screen can confirm it.</summary>
        public bool RequestSent { get; private set; }

        public IReadOnlyList<FriendEntry> Received => _received;

        public IReadOnlyList<FriendEntry> Sent => _sent;

        public IReadOnlyList<FriendEntry> Friends => _friends;

        public IReadOnlyList<FriendEntry> Blocked => _blocked;

        public IReadOnlyList<FriendEntry> Recent => _recent;

        public async Awaitable RefreshAsync(CancellationToken cancellation)
        {
            Busy = true;
            Changed?.Invoke();
            var friends = await _social.FriendsAsync(cancellation);
            var recent = await _social.RecentAsync(cancellation);
            _received.Clear();
            _sent.Clear();
            _friends.Clear();
            _blocked.Clear();
            _recent.Clear();
            var known = new HashSet<string>();
            if (friends.IsOk)
            {
                foreach (var entry in friends.Value)
                {
                    known.Add(entry.UserId);
                    switch (entry.State)
                    {
                        case FriendState.Received:
                            _received.Add(entry);
                            break;
                        case FriendState.Sent:
                            _sent.Add(entry);
                            break;
                        case FriendState.Blocked:
                            _blocked.Add(entry);
                            break;
                        default:
                            _friends.Add(entry);
                            break;
                    }
                }

                // Favourites first, then online, then by name.
                _friends.Sort((a, b) =>
                    a.Favourite != b.Favourite ? (a.Favourite ? -1 : 1)
                    : a.Online != b.Online ? (a.Online ? -1 : 1)
                    : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (recent.IsOk)
            {
                foreach (var entry in recent.Value)
                {
                    if (!known.Contains(entry.UserId))
                    {
                        _recent.Add(entry);
                    }
                }
            }

            ErrorCode = friends.IsOk ? recent.IsOk ? null : recent.Error.Code : friends.Error.Code;
            Busy = false;
            Changed?.Invoke();
        }

        public async Awaitable AddByCodeAsync(string code, CancellationToken cancellation)
        {
            RequestSent = false;
            var added = await _social.AddByCodeAsync(code, cancellation);
            RequestSent = added.IsOk;
            await AfterAsync(added, cancellation);
        }

        public async Awaitable AddAsync(string userId, CancellationToken cancellation) =>
            await AfterAsync(await _social.AddAsync(userId, cancellation), cancellation);

        public async Awaitable RemoveAsync(string userId, CancellationToken cancellation) =>
            await AfterAsync(await _social.RemoveAsync(userId, cancellation), cancellation);

        public async Awaitable BlockAsync(string userId, CancellationToken cancellation) =>
            await AfterAsync(await _social.BlockAsync(userId, cancellation), cancellation);

        public async Awaitable ToggleFavouriteAsync(FriendEntry friend, CancellationToken cancellation) =>
            await AfterAsync(await _social.SetFavouriteAsync(friend.UserId, !friend.Favourite, cancellation), cancellation);

        private async Awaitable AfterAsync(Result<bool> result, CancellationToken cancellation)
        {
            await RefreshAsync(cancellation);
            if (!result.IsOk)
            {
                ErrorCode = result.Error.Code;
                Changed?.Invoke();
            }
        }
    }
}
