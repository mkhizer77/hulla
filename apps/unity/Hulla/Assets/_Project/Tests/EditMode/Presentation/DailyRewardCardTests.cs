using Hulla.Presentation.Screens.Home;
using NUnit.Framework;

namespace Hulla.Presentation.Tests
{
    public sealed class DailyRewardCardTests
    {
        [Test]
        public void ThePotliBalanceReadsThroughSeparators()
        {
            Assert.AreEqual(1240, DailyRewardCard.Balance("1,240"));
            Assert.AreEqual(12480, DailyRewardCard.Balance("12.480"));
            Assert.AreEqual(0, DailyRewardCard.Balance(""));
        }
    }
}
