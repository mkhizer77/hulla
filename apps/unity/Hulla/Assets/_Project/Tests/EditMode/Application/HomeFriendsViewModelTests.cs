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

            public List<string> Mutes = new List<string>();

            public Awaitable<Result<IReadOnlyList<string>>> MutesAsync(CancellationToken cancellation) => Done(Result<IReadOnlyList<string>>.Ok(new List<string>(Mutes)));

            public Awaitable<Result<IReadOnlyList<string>>> SetMuteAsync(string userId, bool muted, CancellationToken cancellation)
            {
                if (muted && !Mutes.Contains(userId))
                {
                    Mutes.Add(userId);
                }

                if (!muted)
                {
                    Mutes.Remove(userId);
                }

                return Done(Result<IReadOnlyList<string>>.Ok(new List<string>(Mutes)));
            }
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
        public void MutesFollowTheServerList()
        {
            var social = new FakeSocial { Mutes = { "u7" } };
            var mutes = new MuteList(social);
            Assert.IsFalse(mutes.IsMuted("u7"), "nothing until loaded");
            _ = mutes.RefreshAsync(default);
            Assert.IsTrue(mutes.IsMuted("u7"));
            _ = mutes.SetAsync("u3", true, default);
            Assert.IsTrue(mutes.IsMuted("u3"));
            _ = mutes.SetAsync("u7", false, default);
            Assert.IsFalse(mutes.IsMuted("u7"));
            CollectionAssert.AreEquivalent(new[] { "u3" }, social.Mutes);
            Assert.IsFalse(mutes.IsMuted(null));
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
