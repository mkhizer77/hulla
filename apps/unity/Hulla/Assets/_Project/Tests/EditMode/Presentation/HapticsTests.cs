using Hulla.Presentation.TableKit;
using NUnit.Framework;

namespace Hulla.Presentation.Tests
{
    /// <summary>The table's haptics (GDD 16.4): the iOS shim keys its feedback on the enum's numbers.</summary>
    public sealed class HapticsTests
    {
        [Test]
        public void TheEventNumbersMatchTheIosShim()
        {
            // Plugins/iOS/HullaHaptics.mm switches on these values; reorder both together.
            Assert.AreEqual(0, (int)Haptic.TurnStart);
            Assert.AreEqual(1, (int)Haptic.CardPlay);
            Assert.AreEqual(2, (int)Haptic.ThullaGiven);
            Assert.AreEqual(3, (int)Haptic.ThullaReceived);
            Assert.AreEqual(4, (int)Haptic.Escape);
            Assert.AreEqual(5, (int)Haptic.Bhabhi);
        }

        [Test]
        public void HeavierMomentsBuzzHarder()
        {
            static int Peak(Haptic kind)
            {
                var peak = 0;
                foreach (var amplitude in Haptics.Pattern(kind).Amplitudes)
                {
                    peak = System.Math.Max(peak, amplitude);
                }

                return peak;
            }

            Assert.Less(Peak(Haptic.CardPlay), Peak(Haptic.TurnStart));
            Assert.Less(Peak(Haptic.TurnStart), Peak(Haptic.ThullaGiven));
            Assert.Less(Peak(Haptic.ThullaGiven), Peak(Haptic.ThullaReceived));
        }
    }
}
