using System;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// The thulla moment's opening (GDD 14.6, 15.7, the art pack's 1,200 ms choreography): the slam jolts
    /// the table 4 px vertically for 80 ms at once, the stamp slams down 120 ms later from 1.6 times its
    /// size, lands with a small squash, holds and leaves, all within 1.2 s. With reduce-motion the stamp
    /// only fades and nothing shakes. Pure timing so it can be tested without a frame loop.
    /// </summary>
    public sealed class StampTimeline
    {
        public const float DelaySeconds = 0.12f;
        public const float InSeconds = 0.12f;
        public const float HoldSeconds = 0.76f;
        public const float OutSeconds = 0.2f;
        public const float ShakeSeconds = 0.08f;
        public const float ShakePixels = 4f;
        private const float Start = 1.6f;
        private const float Squash = 0.94f;

        private float _elapsed = float.MaxValue;
        private bool _reduceMotion;

        public bool Active => _elapsed < Total;

        /// <summary>True while the stamp itself is on screen (after the delay).</summary>
        public bool Visible => Active && _elapsed >= DelaySeconds;

        public float Scale { get; private set; } = 1f;

        public float Opacity { get; private set; }

        /// <summary>The table's vertical jolt in pixels.</summary>
        public float ShakeY { get; private set; }

        public static float Total => DelaySeconds + InSeconds + HoldSeconds + OutSeconds;

        public void Play(bool reduceMotion)
        {
            _reduceMotion = reduceMotion;
            _elapsed = 0f;
            Sample();
        }

        public void Advance(float deltaSeconds)
        {
            if (!Active)
            {
                return;
            }

            _elapsed += deltaSeconds;
            Sample();
        }

        private void Sample()
        {
            if (!Active)
            {
                Opacity = 0f;
                Scale = 1f;
                ShakeY = 0f;
                return;
            }

            ShakeY = _reduceMotion || _elapsed >= ShakeSeconds
                ? 0f
                : (float)Math.Sin(_elapsed * 160f) * ShakePixels * (1f - _elapsed / ShakeSeconds);

            var t = _elapsed - DelaySeconds;
            if (t < 0f)
            {
                Opacity = 0f;
                Scale = 1f;
            }
            else if (t < InSeconds)
            {
                var k = t / InSeconds;
                Opacity = k;
                Scale = _reduceMotion ? 1f : Start + (Squash - Start) * EaseIn(k);
            }
            else if (t < InSeconds + HoldSeconds)
            {
                var settle = Math.Min(1f, (t - InSeconds) / 0.1f);
                Opacity = 1f;
                Scale = _reduceMotion ? 1f : Squash + (1f - Squash) * settle;
            }
            else
            {
                var k = (t - InSeconds - HoldSeconds) / OutSeconds;
                Opacity = 1f - k;
                Scale = 1f;
            }
        }

        // A slam accelerates into the felt.
        private static float EaseIn(float k) => k * k;
    }
}
