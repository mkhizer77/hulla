using System;
using System.Collections;
using Hulla.Application.Social;
using Hulla.Application.ViewModels;
using Hulla.Infrastructure.Nakama;
using Hulla.Protocol;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hulla.Integration.Tests
{
    /// <summary>
    /// Profiles and friends against a running Nakama (GDD 10.1 to 10.3): a friend code adds a friend,
    /// the request is accepted, a favourite is pinned, your own code is refused and a handle can't be
    /// taken twice. Runs only when HULLA_NAKAMA_HOST is set.
    /// </summary>
    public sealed class SocialAgainstNakamaTests
    {
        private sealed class Person
        {
            public Person(string name, string host, int port, string stamp)
            {
                Connection = NakamaConnection.Create("http", host, port, "local-only-server-key");
                Session = new NakamaSessionService(Connection, "hulla-social-" + name + "-" + stamp);
                var social = new NakamaSocialService(Connection);
                Friends = new FriendsViewModel(social);
                Profile = new ProfileViewModel(social);
            }

            public NakamaConnection Connection { get; }

            public NakamaSessionService Session { get; }

            public FriendsViewModel Friends { get; }

            public ProfileViewModel Profile { get; }
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator FriendCodesRequestsFavouritesAndHandles()
        {
            var host = Environment.GetEnvironmentVariable("HULLA_NAKAMA_HOST");
            if (string.IsNullOrEmpty(host))
            {
                Assert.Ignore("HULLA_NAKAMA_HOST is not set");
            }

            var port = int.TryParse(Environment.GetEnvironmentVariable("HULLA_NAKAMA_PORT"), out var p) ? p : 7350;
            var awaiter = RunAsync(host!, port).GetAwaiter();
            while (!awaiter.IsCompleted)
            {
                yield return null;
            }

            awaiter.GetResult();
        }

        private static async Awaitable RunAsync(string host, int port)
        {
            var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var amna = new Person("amna", host, port, stamp);
            var basit = new Person("basit", host, port, stamp);
            try
            {
                Assert.IsTrue((await amna.Session.SignInAsync("Amna", default)).IsOk);
                Assert.IsTrue((await basit.Session.SignInAsync("Basit", default)).IsOk);
                await amna.Profile.RefreshAsync(default);
                var code = amna.Profile.Profile?.FriendCode;
                Assert.IsNotEmpty(code, "a new player has a friend code");

                await amna.Friends.AddByCodeAsync(code!, default);
                Assert.AreEqual("social.self", amna.Friends.ErrorCode, "your own code is refused");

                await basit.Friends.AddByCodeAsync(code!.ToLowerInvariant(), default);
                Assert.IsTrue(basit.Friends.RequestSent, "codes are read without regard to case");
                Assert.AreEqual(1, basit.Friends.Sent.Count);

                await amna.Friends.RefreshAsync(default);
                Assert.AreEqual(1, amna.Friends.Received.Count, "the request arrives");
                var request = amna.Friends.Received[0];
                await amna.Friends.AddAsync(request.UserId, default);
                Assert.AreEqual(1, amna.Friends.Friends.Count, "accepting makes a friend");
                await basit.Friends.RefreshAsync(default);
                Assert.AreEqual(FriendState.Friend, basit.Friends.Friends[0].State);

                await amna.Friends.ToggleFavouriteAsync(amna.Friends.Friends[0], default);
                Assert.IsTrue(amna.Friends.Friends[0].Favourite, "a friend can be pinned");

                var handle = "amna_" + stamp.Substring(stamp.Length - 6);
                await amna.Profile.SaveAsync("Amna K", handle, "Thulla queen", default);
                Assert.IsTrue(amna.Profile.Saved, amna.Profile.ErrorCode);
                Assert.AreEqual("Amna K", amna.Profile.Profile!.DisplayName);
                await basit.Profile.RefreshAsync(default);
                await basit.Profile.SaveAsync("Basit", handle, "", default);
                Assert.AreEqual("profile.handle_taken", basit.Profile.ErrorCode);
            }
            finally
            {
                amna.Connection.Close();
                basit.Connection.Close();
            }
        }
    }
}
