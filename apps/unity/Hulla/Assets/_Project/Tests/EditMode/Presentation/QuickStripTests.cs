using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Presentation.TableKit;
using NUnit.Framework;

namespace Hulla.Presentation.Tests
{
    /// <summary>The contextual quick-chat strip (GDD 14.3, OQ-038).</summary>
    public sealed class QuickStripTests
    {
        private static readonly ChatPhrase[] Pack =
        {
            new ChatPhrase("T01", "THULLA!", "", "thulla", "gave_thulla"),
            new ChatPhrase("T02", "Le lo bhai!", "", "thulla", "gave_thulla"),
            new ChatPhrase("R01", "Haan bhai", "", "courtesy", "any"),
            new ChatPhrase("R02", "Shabash", "", "courtesy", "any"),
            new ChatPhrase("R03", "Chalo", "", "courtesy", "any"),
            new ChatPhrase("S01", "Jaldi khel", "", "call", "opponent_slow"),
        };

        [Test]
        public void TheContextsPhrasesComeFirstAndAnyFillsTheRest()
        {
            var picks = QuickStrip.Pick(Pack, "gave_thulla", 0);
            Assert.AreEqual(3, picks.Count);
            Assert.AreEqual("T01", picks[0].Id);
            Assert.AreEqual("T02", picks[1].Id);
            Assert.AreEqual("any", picks[2].Context);
        }

        [Test]
        public void AnUnknownContextShowsOnlyGeneralPhrases()
        {
            var picks = QuickStrip.Pick(Pack, "lobby", 0);
            Assert.AreEqual(3, picks.Count);
            foreach (var phrase in picks)
            {
                Assert.AreEqual("any", phrase.Context);
            }
        }

        [Test]
        public void TheSeedRotatesThePicksSoARoundIsNotRepetitive()
        {
            var first = QuickStrip.Pick(Pack, "any", 0);
            var second = QuickStrip.Pick(Pack, "any", 1);
            Assert.AreNotEqual(first[0].Id, second[0].Id);
            CollectionAssert.AreEquivalent(new[] { "R01", "R02", "R03" }, new[] { second[0].Id, second[1].Id, second[2].Id });
        }

        [Test]
        public void AnEmptyPackGivesAnEmptyStrip()
        {
            Assert.AreEqual(0, QuickStrip.Pick(new ChatPhrase[0], "any", 3).Count);
        }
    }
}
