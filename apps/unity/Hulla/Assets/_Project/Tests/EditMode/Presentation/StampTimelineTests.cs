using Hulla.Presentation.TableKit;
using NUnit.Framework;

namespace Hulla.Presentation.Tests
{
    public sealed class StampTimelineTests
    {
        [Test]
        public void TheStampWaitsForTheSlamThenSlamsDownSettlesAndFades()
        {
            var stamp = new StampTimeline();
            stamp.Play(false);
            Assert.IsFalse(stamp.Visible, "nothing but the shake in the first 120 ms (GDD 14.6)");
            Assert.AreEqual(0f, stamp.Opacity);
            stamp.Advance(StampTimeline.DelaySeconds + StampTimeline.InSeconds * 0.5f);
            Assert.Greater(stamp.Scale, 1f, "comes down from above the felt");
            stamp.Advance(StampTimeline.InSeconds * 0.5f);
            Assert.IsTrue(stamp.Visible);
            Assert.AreEqual(1f, stamp.Opacity, 0.001f);
            Assert.Less(stamp.Scale, 1f, "squashed as it lands at the end of the in phase");
            stamp.Advance(0.5f);
            Assert.AreEqual(1f, stamp.Scale, 0.001f, "settled during the hold");
            Assert.AreEqual(0f, stamp.ShakeY, "the shake is over");
            stamp.Advance(StampTimeline.HoldSeconds + StampTimeline.OutSeconds);
            Assert.IsFalse(stamp.Active);
            Assert.AreEqual(0f, stamp.Opacity);
        }

        [Test]
        public void TheWholeMomentStaysUnderTheThullaDelayBudget()
        {
            Assert.AreEqual(1.2f, StampTimeline.Total, 0.0001f, "the art pack's 1,200 ms moment");
            Assert.AreEqual(0.12f, StampTimeline.DelaySeconds, 0.0001f, "the stamp lands 120 ms after the slam");
        }

        [Test]
        public void TheShakeIsFourPixelsVerticallyForEightyMilliseconds()
        {
            Assert.AreEqual(4f, StampTimeline.ShakePixels);
            Assert.AreEqual(0.08f, StampTimeline.ShakeSeconds, 0.0001f);
            var stamp = new StampTimeline();
            stamp.Play(false);
            stamp.Advance(0.01f);
            Assert.AreNotEqual(0f, stamp.ShakeY);
            Assert.LessOrEqual(System.Math.Abs(stamp.ShakeY), StampTimeline.ShakePixels);
            stamp.Advance(StampTimeline.ShakeSeconds);
            Assert.AreEqual(0f, stamp.ShakeY);
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
                Assert.AreEqual(0f, stamp.ShakeY);
            }
        }
    }
}
