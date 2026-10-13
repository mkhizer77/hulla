using Hulla.Presentation.TableKit;
using NUnit.Framework;

namespace Hulla.Presentation.Tests
{
    /// <summary>The turn timer's phases (GDD 14.5): amber at 5 s, red at 2 s, a tick at each.</summary>
    public sealed class TimerPhaseTests
    {
        [Test]
        public void CalmThenAmberAtFiveThenRedAtTwo()
        {
            Assert.AreEqual(TimerPhase.Phase.Calm, TimerPhase.For(12f));
            Assert.AreEqual(TimerPhase.Phase.Calm, TimerPhase.For(5.01f));
            Assert.AreEqual(TimerPhase.Phase.Warn, TimerPhase.For(5f));
            Assert.AreEqual(TimerPhase.Phase.Warn, TimerPhase.For(2.5f));
            Assert.AreEqual(TimerPhase.Phase.Critical, TimerPhase.For(2f));
            Assert.AreEqual(TimerPhase.Phase.Critical, TimerPhase.For(0f));
        }

        [Test]
        public void ThePulseStaysStillWhenCalmAndUnderReduceMotion()
        {
            for (var t = 0f; t < 2f; t += 0.1f)
            {
                Assert.AreEqual(1f, TimerPhase.Pulse(10f, t, false));
                Assert.AreEqual(1f, TimerPhase.Pulse(3f, t, true));
            }
        }

        [Test]
        public void ThePulseIsSmallWhenAmberAndLargerWhenRed()
        {
            var amber = 0f;
            var red = 0f;
            for (var t = 0f; t < 2f; t += 0.01f)
            {
                amber = System.Math.Max(amber, TimerPhase.Pulse(4f, t, false));
                red = System.Math.Max(red, TimerPhase.Pulse(1f, t, false));
            }

            Assert.That(amber, Is.InRange(1.05f, 1.07f));
            Assert.That(red, Is.InRange(1.09f, 1.11f));
        }

        [Test]
        public void TicksAtFiveAndTwoOnly()
        {
            Assert.IsTrue(TimerPhase.TickAt(5));
            Assert.IsTrue(TimerPhase.TickAt(2));
            Assert.IsFalse(TimerPhase.TickAt(4));
            Assert.IsFalse(TimerPhase.TickAt(1));
            Assert.IsFalse(TimerPhase.TickAt(10));
        }
    }
}
