using System.Collections.Generic;
using Hulla.Application.Settings;
using Hulla.Application.ViewModels;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    public sealed class WelcomePromptsTests
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
        public void NothingBeforeTheFirstLiveRound()
        {
            var prompts = new WelcomePrompts(new MemoryStore());
            Assert.AreEqual(WelcomePrompts.Prompt.None, prompts.Next(false));
        }

        [Test]
        public void KeepProgressThenInviteThenNothing()
        {
            var store = new MemoryStore();
            var prompts = new WelcomePrompts(store);
            prompts.FirstRoundFinished();
            Assert.AreEqual(WelcomePrompts.Prompt.KeepProgress, prompts.Next(false));
            Assert.AreEqual(WelcomePrompts.Prompt.Invite, prompts.Next(false));
            Assert.AreEqual(WelcomePrompts.Prompt.None, prompts.Next(false));
            Assert.AreEqual(WelcomePrompts.Prompt.None, new WelcomePrompts(store).Next(false), "each shows once ever");
        }

        [Test]
        public void ALinkedPlayerGoesStraightToTheInvite()
        {
            var prompts = new WelcomePrompts(new MemoryStore());
            prompts.FirstRoundFinished();
            Assert.AreEqual(WelcomePrompts.Prompt.Invite, prompts.Next(true));
            Assert.AreEqual(WelcomePrompts.Prompt.None, prompts.Next(false));
        }
    }
}
