using Hulla.Presentation.Screens.Leaderboard;
using NUnit.Framework;

namespace Hulla.Presentation.Tests
{
    public sealed class LeaderboardDurationTests
    {
        [TestCase(3L * 86_400_000 + 4 * 3_600_000 + 60_000, "3d 4h")]
        [TestCase(5L * 3_600_000 + 12 * 60_000, "5h 12m")]
        [TestCase(12L * 60_000, "12m")]
        [TestCase(-5L, "1m")]
        public void TheResetCountdownReadsShort(long ms, string expected) => Assert.AreEqual(expected, LeaderboardView.Duration(ms));
    }
}
