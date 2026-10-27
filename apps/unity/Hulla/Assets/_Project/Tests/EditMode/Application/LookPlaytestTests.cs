using System.Collections.Generic;
using Hulla.Application.Settings;
using Hulla.Application.ViewModels;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    public sealed class LookPlaytestTests
    {
        private sealed class MemoryStore : ISettingsStore
        {
            private readonly Dictionary<string, int> _values = new Dictionary<string, int>();

            public int GetInt(string key, int fallback) => _values.TryGetValue(key, out var value) ? value : fallback;

            public void SetInt(string key, int value) => _values[key] = value;

            public void Save()
            {
            }
        }

        [Test]
        public void AsksOnlyAfterARoundInATestLook()
        {
            var playtest = new LookPlaytest(new MemoryStore());
            Assert.IsFalse(playtest.Due(TableSkin.Basant, true));
            playtest.RoundFinished(TableSkin.Club);
            Assert.IsFalse(playtest.Due(TableSkin.Club, true));
            playtest.RoundFinished(TableSkin.Basant);
            Assert.IsTrue(playtest.Due(TableSkin.Basant, true));
            Assert.IsFalse(playtest.Due(TableSkin.Pop, true));
            Assert.IsFalse(playtest.Due(TableSkin.Basant, false), "the remote switch turns it off");
            Assert.AreEqual(1, playtest.Rounds(TableSkin.Basant));
        }

        [Test]
        public void AnAnswerOrNotNowStopsTheQuestion()
        {
            var store = new MemoryStore();
            var playtest = new LookPlaytest(store);
            playtest.RoundFinished(TableSkin.Pop);
            playtest.Snooze();
            Assert.IsFalse(playtest.Due(TableSkin.Pop, true));
            var nextLaunch = new LookPlaytest(store);
            Assert.IsTrue(nextLaunch.Due(TableSkin.Pop, true), "not now asks again next launch");
            nextLaunch.MarkAnswered(TableSkin.Pop);
            Assert.IsFalse(new LookPlaytest(store).Due(TableSkin.Pop, true));
        }

        [Test]
        public void RemembersTheLookBeforeTheTest()
        {
            var playtest = new LookPlaytest(new MemoryStore());
            Assert.AreEqual(TableSkin.Club, playtest.Before);
            playtest.Switched(TableSkin.Haveli, TableSkin.Basant);
            playtest.Switched(TableSkin.Basant, TableSkin.Pop);
            Assert.AreEqual(TableSkin.Haveli, playtest.Before, "moving between test looks keeps the old one");
        }

        [Test]
        public void TheCardStaysUntilBothLooksHaveAnAnswer()
        {
            var playtest = new LookPlaytest(new MemoryStore());
            Assert.IsTrue(playtest.ShowCard(true));
            Assert.IsFalse(playtest.ShowCard(false));
            playtest.MarkAnswered(TableSkin.Basant);
            Assert.IsTrue(playtest.ShowCard(true));
            playtest.MarkAnswered(TableSkin.Pop);
            Assert.IsFalse(playtest.ShowCard(true));
            Assert.AreEqual("basant", LookPlaytest.Id(TableSkin.Basant));
        }
    }
}
