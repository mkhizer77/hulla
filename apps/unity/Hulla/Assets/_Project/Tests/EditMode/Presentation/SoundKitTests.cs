using System;
using Hulla.Presentation.Audio;
using NUnit.Framework;

namespace Hulla.Presentation.Tests
{
    /// <summary>The placeholder sounds (GDD 16.2) are short, within level, and not silent.</summary>
    public sealed class SoundKitTests
    {
        [Test]
        public void EverySoundIsShortLoudEnoughAndNeverClips()
        {
            foreach (Sound sound in Enum.GetValues(typeof(Sound)))
            {
                var samples = SoundKit.Samples(sound);
                Assert.Greater(samples.Length, 0, sound.ToString());
                Assert.LessOrEqual(samples.Length, SoundKit.SampleRate / 2, sound + " stays under half a second");
                var peak = 0f;
                foreach (var s in samples)
                {
                    peak = Math.Max(peak, Math.Abs(s));
                }

                Assert.LessOrEqual(peak, 1f, sound + " does not clip");
                Assert.Greater(peak, 0.2f, sound + " is audible");
            }
        }

        [Test]
        public void ATrickClimbsAPentatonicScaleAndStopsAtTheOctave()
        {
            Assert.AreEqual(1f, SoundKit.TrickPitch(0));
            Assert.Less(SoundKit.TrickPitch(1), SoundKit.TrickPitch(2));
            Assert.Less(SoundKit.TrickPitch(4), SoundKit.TrickPitch(5));
            Assert.AreEqual(2f, SoundKit.TrickPitch(5));
            Assert.AreEqual(2f, SoundKit.TrickPitch(9), "a six-seat trick holds the octave");
        }
    }
}
