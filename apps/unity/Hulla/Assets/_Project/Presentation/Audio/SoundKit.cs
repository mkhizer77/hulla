using System;

namespace Hulla.Presentation.Audio
{
    /// <summary>The table's sound events (GDD 16.2).</summary>
    public enum Sound
    {
        Deal,
        CardTap,
        Slam,
        Stamp,
        Pickup,
        Escape,
        Sting,
        Tick,
        Ui,
    }

    /// <summary>
    /// Placeholder sound effects synthesised at start-up so the table has a voice before the recorded
    /// set lands (GDD 16.2; the audio track replaces the clips, not the calls). Pure sample maths, so
    /// the clips can be checked for length and level without an audio device.
    /// </summary>
    public static class SoundKit
    {
        public const int SampleRate = 22050;

        /// <summary>The pentatonic steps a trick climbs, one per card (design 7.4), as pitch multipliers.</summary>
        public static float TrickPitch(int cardIndex)
        {
            var steps = new[] { 1f, 1.125f, 1.25f, 1.5f, 1.6667f, 2f };
            return steps[Math.Clamp(cardIndex, 0, steps.Length - 1)];
        }

        /// <summary>The Dhaba sound set (catalog sounds_dhaba): wood, tabla, a steel cup and harmonium.</summary>
        public const string Dhaba = "sounds_dhaba";

        /// <summary>The clip for a sound in the player's equipped set; an unknown or null set is the classic one.</summary>
        public static float[] Samples(Sound sound, string? set)
        {
            if (set != Dhaba)
            {
                return Samples(sound);
            }

            switch (sound)
            {
                case Sound.CardTap: return Tone(0.09f, 140f, 30f, noise: 0.5f, noiseDecay: 90f);
                case Sound.Slam: return Tabla();
                case Sound.Stamp: return Cup();
                case Sound.Escape: return Harmonium(new[] { 392f, 494f, 587f }, 0.45f, 0f);
                case Sound.Sting: return Harmonium(new[] { 349f, 294f }, 0.5f, 0.22f);
                case Sound.Tick: return Tone(0.035f, 820f, 90f, noise: 0.15f, noiseDecay: 200f);
                default: return Samples(sound);
            }
        }

        public static float[] Samples(Sound sound)
        {
            switch (sound)
            {
                case Sound.Deal: return Deal();
                case Sound.CardTap: return Tone(0.07f, 190f, 38f, noise: 0.35f, noiseDecay: 140f);
                case Sound.Slam: return Tone(0.16f, 85f, 22f, noise: 0.6f, noiseDecay: 60f);
                case Sound.Stamp: return Tone(0.12f, 140f, 30f, noise: 0.2f, noiseDecay: 90f, square: true);
                case Sound.Pickup: return Swoosh(0.32f);
                case Sound.Escape: return Chime();
                case Sound.Sting: return Sting();
                case Sound.Tick: return Tone(0.03f, 1200f, 120f);
                default: return Tone(0.025f, 620f, 110f);
            }
        }

        private static float[] Tone(float seconds, float hertz, float decay, float noise = 0f, float noiseDecay = 100f, bool square = false)
        {
            var samples = new float[(int)(seconds * SampleRate)];
            var random = new Random(7);
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)SampleRate;
                var wave = (float)Math.Sin(2 * Math.PI * hertz * t);
                if (square)
                {
                    wave = Math.Sign(wave) * 0.6f + wave * 0.4f;
                }

                var value = wave * (float)Math.Exp(-t * decay);
                if (noise > 0f)
                {
                    value += noise * ((float)random.NextDouble() * 2f - 1f) * (float)Math.Exp(-t * noiseDecay);
                }

                samples[i] = Soften(value * 0.8f);
            }

            return samples;
        }

        private static float[] Deal()
        {
            var samples = new float[(int)(0.42f * SampleRate)];
            var random = new Random(11);
            const int clicks = 12;
            var spacing = samples.Length / clicks;
            for (var c = 0; c < clicks; c++)
            {
                var start = c * spacing;
                for (var i = 0; i < 300 && start + i < samples.Length; i++)
                {
                    samples[start + i] += 0.5f * ((float)random.NextDouble() * 2f - 1f) * (float)Math.Exp(-i / 60f);
                }
            }

            return samples;
        }

        private static float[] Swoosh(float seconds)
        {
            var samples = new float[(int)(seconds * SampleRate)];
            var random = new Random(3);
            var last = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var k = i / (float)samples.Length;
                // Low-passed noise that opens up as the cards gather: a rising, breathy sweep.
                var white = (float)random.NextDouble() * 2f - 1f;
                var alpha = 0.04f + 0.4f * k;
                last += alpha * (white - last);
                var envelope = (float)Math.Sin(Math.PI * k);
                samples[i] = Soften(last * envelope * 1.6f);
            }

            return samples;
        }

        private static float[] Chime()
        {
            var samples = new float[(int)(0.45f * SampleRate)];
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)SampleRate;
                var value = 0.5f * (float)Math.Sin(2 * Math.PI * 880f * t) + 0.3f * (float)Math.Sin(2 * Math.PI * 1320f * t);
                samples[i] = Soften(value * (float)Math.Exp(-t * 7f));
            }

            return samples;
        }

        /// <summary>Two falling notes with a little vibrato: the friendly "you picked up" sting.</summary>
        private static float[] Sting()
        {
            var samples = new float[(int)(0.5f * SampleRate)];
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)SampleRate;
                var second = t >= 0.22f;
                var hertz = (second ? 277f : 330f) * (1f + 0.012f * (float)Math.Sin(2 * Math.PI * 6f * t));
                var local = second ? t - 0.22f : t;
                var saw = (float)(2.0 * (hertz * t - Math.Floor(hertz * t + 0.5)));
                var sine = (float)Math.Sin(2 * Math.PI * hertz * t);
                var envelope = (float)Math.Min(1.0, local * 40.0) * (float)Math.Exp(-local * 6f);
                samples[i] = Soften((0.45f * sine + 0.25f * saw) * envelope);
            }

            return samples;
        }

        /// <summary>A tabla "dha": a low drum whose pitch falls as it rings, with a slap on top.</summary>
        private static float[] Tabla()
        {
            var samples = new float[(int)(0.3f * SampleRate)];
            var random = new Random(5);
            var phase = 0.0;
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)SampleRate;
                var hertz = 70f + 90f * (float)Math.Exp(-t * 18f);
                phase += 2 * Math.PI * hertz / SampleRate;
                var body = (float)Math.Sin(phase) * (float)Math.Exp(-t * 9f);
                var slap = 0.4f * ((float)random.NextDouble() * 2f - 1f) * (float)Math.Exp(-t * 120f);
                samples[i] = Soften((body + slap) * 0.9f);
            }

            return samples;
        }

        /// <summary>A steel chai cup struck once: bright, uneven partials that ring a little.</summary>
        private static float[] Cup()
        {
            var samples = new float[(int)(0.35f * SampleRate)];
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)SampleRate;
                var value = 0.4f * (float)Math.Sin(2 * Math.PI * 1870f * t) * (float)Math.Exp(-t * 11f)
                    + 0.25f * (float)Math.Sin(2 * Math.PI * 2740f * t) * (float)Math.Exp(-t * 16f)
                    + 0.15f * (float)Math.Sin(2 * Math.PI * 4310f * t) * (float)Math.Exp(-t * 25f);
                samples[i] = Soften(value);
            }

            return samples;
        }

        /// <summary>Harmonium notes, reedy and slightly detuned; with a step the notes follow each other.</summary>
        private static float[] Harmonium(float[] notes, float seconds, float step)
        {
            var samples = new float[(int)(seconds * SampleRate)];
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)SampleRate;
                var value = 0f;
                for (var n = 0; n < notes.Length; n++)
                {
                    var start = n * step;
                    if (t < start || (step > 0f && n < notes.Length - 1 && t >= start + step))
                    {
                        continue;
                    }

                    var local = t - start;
                    var envelope = (float)Math.Min(1.0, local * 25.0) * (float)Math.Exp(-local * 3.5f);
                    for (var d = -1; d <= 1; d += 2)
                    {
                        var hertz = notes[n] * (1f + d * 0.003f);
                        var cycle = hertz * t;
                        var reed = (float)(2.0 * Math.Abs(2.0 * (cycle - Math.Floor(cycle + 0.5))) - 1.0);
                        value += 0.5f * (0.6f * reed + 0.4f * (float)Math.Sin(2 * Math.PI * hertz * t)) * envelope;
                    }
                }

                samples[i] = Soften(value * (step > 0f ? 0.55f : 0.3f));
            }

            return samples;
        }

        /// <summary>Soft clip so stacked tones never exceed ±1.</summary>
        private static float Soften(float value) => (float)Math.Tanh(value);
    }
}
