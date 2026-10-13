using System.Collections.Generic;
using Hulla.Application.Settings;
using Hulla.Application.Social;
using Hulla.Protocol;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    public sealed class PushTests
    {
        private sealed class MemoryStore : ISettingsStore
        {
            private readonly Dictionary<string, int> _values = new Dictionary<string, int>();

            public int GetInt(string key, int fallback) => _values.TryGetValue(key, out var v) ? v : fallback;

            public void SetInt(string key, int value) => _values[key] = value;

            public void Save()
            {
            }
        }

        [Test]
        public void NoAskBeforeTheFirstFinishedRound()
        {
            var prompt = new PushPrompt(new MemoryStore());
            Assert.IsFalse(prompt.ShouldAsk(deviceCanAsk: true, day: 100));

            prompt.RoundFinished();
            Assert.IsTrue(prompt.ShouldAsk(deviceCanAsk: true, day: 100));
        }

        [Test]
        public void NoAskWhenTheDeviceHasNothingToAsk()
        {
            var prompt = new PushPrompt(new MemoryStore());
            prompt.RoundFinished();
            Assert.IsFalse(prompt.ShouldAsk(deviceCanAsk: false, day: 100));
        }

        [Test]
        public void NotNowAsksAgainAWeekLaterAndTwiceAtMost()
        {
            var prompt = new PushPrompt(new MemoryStore());
            prompt.RoundFinished();
            prompt.Asked(100);
            Assert.IsFalse(prompt.ShouldAsk(true, 106));
            Assert.IsTrue(prompt.ShouldAsk(true, 107));

            prompt.Asked(107);
            Assert.IsFalse(prompt.ShouldAsk(true, 200));
        }

        [Test]
        public void AnInviteTapCarriesTheRoomCode()
        {
            var tap = PushTap.From(new Dictionary<string, string> { ["route"] = "lobby", ["type"] = "table_invite", ["code"] = "k7q2mx" });
            Assert.AreEqual(PushRoute.Lobby, tap!.Value.Route);
            Assert.AreEqual("K7Q2MX", tap.Value.Code);
        }

        [TestCase("friends", PushRoute.Friends)]
        [TestCase("home", PushRoute.Home)]
        [TestCase("pass", PushRoute.Pass)]
        public void EachRouteOpensItsScreen(string route, PushRoute expected)
        {
            Assert.AreEqual(expected, PushTap.From(new Dictionary<string, string> { ["route"] = route })!.Value.Route);
        }

        [Test]
        public void OtherDataIsIgnored()
        {
            Assert.IsNull(PushTap.From(null));
            Assert.IsNull(PushTap.From(new Dictionary<string, string>()));
            Assert.IsNull(PushTap.From(new Dictionary<string, string> { ["route"] = "shop" }));
            Assert.IsNull(PushTap.From(new Dictionary<string, string> { ["route"] = "lobby" }), "an invite without a code");
            Assert.IsNull(PushTap.From(new Dictionary<string, string> { ["route"] = "lobby", ["code"] = "nope" }));
        }
    }
}
