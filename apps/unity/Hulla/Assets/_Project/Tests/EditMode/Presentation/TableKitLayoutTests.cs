using Hulla.Presentation.TableKit;
using NUnit.Framework;

namespace Hulla.Presentation.Tests
{
    public sealed class TableKitLayoutTests
    {
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void OpponentsReadLeftToRightInPlayOrderAndStayOnScreen(int seats)
        {
            var previous = -1f;
            for (var i = 0; i < seats - 1; i++)
            {
                var at = SeatArc.Position(seats, i);
                Assert.Greater(at.x, previous, "seat after the player sits furthest left");
                Assert.That(at.x, Is.InRange(0.05f, 0.95f));
                Assert.That(at.y, Is.InRange(0.05f, 0.95f));
                previous = at.x;
            }
        }

        [Test]
        public void SeatIndexCountsFromTheSeatAfterThePlayer()
        {
            Assert.AreEqual(0, SeatArc.IndexOf(3, 2, 4));
            Assert.AreEqual(1, SeatArc.IndexOf(0, 2, 4));
            Assert.AreEqual(2, SeatArc.IndexOf(1, 2, 4));
        }

        [Test]
        public void AnEighteenCardHandFitsOneRowOnAPhone()
        {
            var fan = HandFan.For(18, 1032f, 120f);
            Assert.AreEqual(1, fan.Rows);
            Assert.GreaterOrEqual(fan.Strip, HandFan.MinStrip);
            Assert.LessOrEqual(120f + 17 * fan.Strip, 1032f + 0.01f);
        }

        [Test]
        public void AHugeHandWrapsSoEveryCardKeepsItsStrip()
        {
            var fan = HandFan.For(52, 1032f, 120f);
            Assert.Greater(fan.Rows, 1);
            Assert.GreaterOrEqual(fan.Strip, HandFan.MinStrip);
            Assert.GreaterOrEqual(fan.Rows * fan.PerRow, 52);
        }

        [Test]
        public void AShortHandDoesNotSpreadWiderThanACardApart()
        {
            var fan = HandFan.For(3, 1032f, 120f);
            Assert.LessOrEqual(fan.Strip, 128f);
        }
    }
}
