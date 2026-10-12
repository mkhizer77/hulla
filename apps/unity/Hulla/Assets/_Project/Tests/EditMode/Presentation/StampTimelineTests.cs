using Hulla.Presentation.TableKit;
using NUnit.Framework;

namespace Hulla.Presentation.Tests
{
    public sealed class StampTimelineTests
    {
        [Test]
        public void TheStampOvershootsHoldsAndFadesWithinItsBudget()
        {
            var stamp = new StampTimeline();
            stamp.Play(false);
            stamp.Advance(StampTimeline.InSeconds);
            Assert.AreEqual(1f, stamp.Opacity, 0.001f);
            Assert.Greater(stamp.Scale, 1f, "overshoot at the end of the in phase");
            stamp.Advance(0.5f);
            Assert.AreEqual(1f, stamp.Scale, 0.001f, "settled during the hold");
            Assert.AreEqual(0f, stamp.ShakeX, "the shake is over");
            stamp.Advance(StampTimeline.HoldSeconds + StampTimeline.OutSeconds);
            Assert.IsFalse(stamp.Active);
            Assert.AreEqual(0f, stamp.Opacity);
        }

        [Test]
        public void TheWholeMomentStaysUnderTheThullaDelayBudget()
        {
            Assert.LessOrEqual(StampTimeline.InSeconds + StampTimeline.HoldSeconds + StampTimeline.OutSeconds, 1.25f);
        }

        [Test]
        public void ReduceMotionOnlyFades()
        {
            var stamp = new StampTimeline();
            stamp.Play(true);
            for (var i = 0; i < 30; i++)
            {
                stamp.Advance(0.02f);
                Assert.AreEqual(1f, stamp.Scale);
                Assert.AreEqual(0f, stamp.ShakeX);
            }
        }

        [Test]
        public void TheTableShakesOnlyAtTheStart()
        {
            var stamp = new StampTimeline();
            stamp.Play(false);
            stamp.Advance(0.05f);
            Assert.AreNotEqual(0f, stamp.ShakeX);
            stamp.Advance(StampTimeline.ShakeSeconds);
            Assert.AreEqual(0f, stamp.ShakeX);
        }
    }
}
