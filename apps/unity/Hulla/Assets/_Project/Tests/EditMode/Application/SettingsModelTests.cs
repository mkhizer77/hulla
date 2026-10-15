using System.Collections.Generic;
using Hulla.Application.Settings;
using Hulla.Application.Table;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    public sealed class SettingsModelTests
    {
        private sealed class MemoryStore : ISettingsStore
        {
            public readonly Dictionary<string, int> Values = new Dictionary<string, int>();

            public int Saves;

            public int GetInt(string key, int fallback) => Values.TryGetValue(key, out var v) ? v : fallback;

            public void SetInt(string key, int value) => Values[key] = value;

            public void Save() => Saves++;
        }

        [Test]
        public void DefaultsMatchTheGdd()
        {
            var settings = new SettingsModel(new MemoryStore());
            Assert.IsFalse(settings.OneTapPlay, "tap to raise is the default (14.4)");
            Assert.IsTrue(settings.Haptics, "the turn tick is on by default (14.5)");
            Assert.IsTrue(settings.ShowRulesRecap);
            Assert.AreEqual(TextScale.Normal, settings.TextScale);
            Assert.AreEqual(HandSort.SuitThenRank, settings.HandSort);
        }

        [Test]
        public void EveryChangePersistsAndIsAnnouncedOnce()
        {
            var store = new MemoryStore();
            var settings = new SettingsModel(store);
            var changes = 0;
            settings.Changed += () => changes++;
            settings.FourColourDeck = true;
            settings.FourColourDeck = true;
            settings.TextScale = TextScale.Largest;
            Assert.AreEqual(2, changes);
            Assert.AreEqual(2, store.Saves);
            var reloaded = new SettingsModel(store);
            Assert.IsTrue(reloaded.FourColourDeck);
            Assert.AreEqual(TextScale.Largest, reloaded.TextScale);
        }

        [Test]
        public void R8_TheHandCanSortByRankInstead()
        {
            var hand = new List<string> { "KH", "2S", "KS", "2H" };
            hand.Sort(CardOrder.CompareByRank);
            CollectionAssert.AreEqual(new[] { "2S", "2H", "KS", "KH" }, hand);
            hand.Sort(CardOrder.Compare);
            CollectionAssert.AreEqual(new[] { "2S", "KS", "2H", "KH" }, hand);
        }
    }
}
