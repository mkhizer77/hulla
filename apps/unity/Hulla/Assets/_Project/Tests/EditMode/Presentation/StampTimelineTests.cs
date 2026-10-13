using Hulla.Presentation.TableKit;
using NUnit.Framework;

namespace Hulla.Presentation.Tests
{
    public sealed class StampTimelineTests
    {
        [Test]
        public void TheStampWaitsForTheSlamThenOvershootsHoldsAndFades()
        {
            var stamp = new StampTimeline();
            stamp.Play(false);
            Assert.IsFalse(stamp.Visible, "nothing but the shake in the first 120 ms (GDD 14.6)");
            Assert.AreEqual(0f, stamp.Opacity);
            stamp.Advance(StampTimeline.DelaySeconds + StampTimeline.InSeconds);
            Assert.IsTrue(stamp.Visible);
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
            Assert.LessOrEqual(StampTimeline.Total, 1.4f);
            Assert.AreEqual(0.12f, StampTimeline.DelaySeconds, 0.0001f, "the stamp lands 120 ms after the slam");
        }

        [Test]
        public void TheShakeIsTwoPixelsForEightyMilliseconds()
        {
            Assert.AreEqual(2f, StampTimeline.ShakePixels);
            Assert.AreEqual(0.08f, StampTimeline.ShakeSeconds, 0.0001f);
            var stamp = new StampTimeline();
            stamp.Play(false);
            stamp.Advance(0.01f);
            Assert.AreNotEqual(0f, stamp.ShakeX);
            Assert.LessOrEqual(System.Math.Abs(stamp.ShakeX), StampTimeline.ShakePixels);
            stamp.Advance(StampTimeline.ShakeSeconds);
            Assert.AreEqual(0f, stamp.ShakeX);
        }

        [Test]
        public void ReduceMotionOnlyFades()
        {
            var stamp = new StampTimeline();
            stamp.Play(true);
            for (var i = 0; i < 40; i++)
            {
                stamp.Advance(0.02f);
                Assert.AreEqual(1f, stamp.Scale);
                Assert.AreEqual(0f, stamp.ShakeX);
            }
        }
    }
}
