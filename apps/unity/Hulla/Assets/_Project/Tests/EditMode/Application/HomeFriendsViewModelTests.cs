using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Social;
using Hulla.Application.ViewModels;
using Hulla.Domain;
using Hulla.Protocol;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Application.Tests
{
    public sealed class HomeFriendsViewModelTests
    {
        private sealed class FakeSocial : ISocialService
        {
            public List<FriendPresence> Presence = new List<FriendPresence>();

            public int Calls;

            private static Awaitable<T> Done<T>(T value)
            {
                var source = new AwaitableCompletionSource<T>();
                source.SetResult(value);
                return source.Awaitable;
            }

            public Awaitable<Result<IReadOnlyList<FriendPresence>>> PresenceAsync(CancellationToken cancellation)
            {
                Calls++;
                return Done(Result<IReadOnlyList<FriendPresence>>.Ok(Presence));
            }

            public Awaitable<Result<Profile>> MyProfileAsync(CancellationToken cancellation) => throw new NotSupportedException();

            public Awaitable<Result<Profile>> UpdateProfileAsync(ProfileUpdate update, CancellationToken cancellation) => throw new NotSupportedException();

            public Awaitable<Result<IReadOnlyList<FriendEntry>>> FriendsAsync(CancellationToken cancellation) => throw new NotSupportedException();

            public Awaitable<Result<IReadOnlyList<FriendEntry>>> RecentAsync(CancellationToken cancellation) => throw new NotSupportedException();

            public Awaitable<Result<bool>> AddByCodeAsync(string friendCode, CancellationToken cancellation) => throw new NotSupportedException();

            public Awaitable<Result<bool>> AddAsync(string userId, CancellationToken cancellation) => throw new NotSupportedException();

            public Awaitable<Result<bool>> RemoveAsync(string userId, CancellationToken cancellation) => throw new NotSupportedException();

            public Awaitable<Result<bool>> BlockAsync(string userId, CancellationToken cancellation) => throw new NotSupportedException();

            public Awaitable<Result<bool>> SetFavouriteAsync(string userId, bool pinned, CancellationToken cancellation) => throw new NotSupportedException();

            public Awaitable<Result<bool>> SetAwayAsync(bool away, CancellationToken cancellation) => throw new NotSupportedException();
        }

        [Test]
        public void TheFirstJoinableTableBecomesTheHeroAndOfflineFriendsAreLeftOut()
        {
            var social = new FakeSocial
            {
                Presence =
                {
                    new FriendPresence("1", "Ali", PresenceState.Lobby, false, "K7Q2MX", 2, true),
                    new FriendPresence("2", "Sara", PresenceState.Round, false, "ABCDEF", 0, false),
                    new FriendPresence("3", "Noor", PresenceState.Offline, false, null, 0, false),
                    new FriendPresence("4", "Zain", PresenceState.Hidden, false, null, 0, false),
                },
            };
            var model = new HomeFriendsViewModel(social);
            _ = model.RefreshAsync(default);
            Assert.AreEqual("Ali", model.Hero?.Name);
            Assert.AreEqual("K7Q2MX", model.Hero?.TableCode);
            Assert.AreEqual(2, model.Online.Count);
        }

        [Test]
        public void NoJoinableTableMeansNoHero()
        {
            var social = new FakeSocial { Presence = { new FriendPresence("2", "Sara", PresenceState.Round, false, "ABCDEF", 0, false) } };
            var model = new HomeFriendsViewModel(social);
            _ = model.RefreshAsync(default);
            Assert.IsNull(model.Hero);
        }

        [Test]
        public void ItRefreshesEveryFiveSecondsOnlyWhileActive()
        {
            var social = new FakeSocial();
            var model = new HomeFriendsViewModel(social);
            model.Tick(10f, default);
            Assert.AreEqual(0, social.Calls, "inactive away from Home");
            model.Active = true;
            model.Tick(0.1f, default);
            Assert.AreEqual(1, social.Calls, "the first tick refreshes at once");
            model.Tick(4f, default);
            Assert.AreEqual(1, social.Calls);
            model.Tick(1.1f, default);
            Assert.AreEqual(2, social.Calls);
        }
    }
}
