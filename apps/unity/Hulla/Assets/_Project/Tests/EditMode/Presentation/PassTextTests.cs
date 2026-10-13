using System.Collections.Generic;
using Hulla.Application.Progress;
using Hulla.Presentation.Screens.Pass;
using NUnit.Framework;

namespace Hulla.Presentation.Tests
{
    public sealed class PassTextTests
    {
        [Test]
        public void RewardsAndSeasonNamesRead()
        {
            Assert.AreEqual("60 coins", PassView.Describe(new PassReward(60, 0, new List<string>(), null, false)));
            Assert.AreEqual("5 gems + Kites", PassView.Describe(new PassReward(0, 5, new List<string> { "back_kites" }, null, false)));
            Assert.AreEqual("title \"Basant raja\"", PassView.Describe(new PassReward(0, 0, new List<string>(), "basant_raja", false)));
            Assert.AreEqual("Basant 2026", PassView.SeasonName("season.basant_2026"));
        }
    }
}
