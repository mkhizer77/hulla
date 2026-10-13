using System;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// The thulla moment's opening (GDD 14.6, 15.7): the slam shakes the table 2 px for 80 ms at once,
    /// the stamp follows 120 ms later with a 1.08 overshoot, holds 900 ms and leaves in 200 ms. With
    /// reduce-motion the stamp only fades and nothing shakes. Pure timing so it can be tested without
    /// a frame loop.
    /// </summary>
    public sealed class StampTimeline
    {
        public const float DelaySeconds = 0.12f;
        public const float InSeconds = 0.12f;
        public const float HoldSeconds = 0.9f;
        public const float OutSeconds = 0.2f;
        public const float ShakeSeconds = 0.08f;
        public const float ShakePixels = 2f;
        private const float Overshoot = 1.08f;

        private float _elapsed = float.MaxValue;
        private bool _reduceMotion;

        public bool Active => _elapsed < Total;

        /// <summary>True while the stamp itself is on screen (after the delay).</summary>
        public bool Visible => Active && _elapsed >= DelaySeconds;

        public float Scale { get; private set; } = 1f;

        public float Opacity { get; private set; }

        public float ShakeX { get; private set; }

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
                ShakeX = 0f;
                return;
            }

            ShakeX = _reduceMotion || _elapsed >= ShakeSeconds
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
                Scale = _reduceMotion ? 1f : 0.6f + (Overshoot - 0.6f) * EaseOut(k);
            }
            else if (t < InSeconds + HoldSeconds)
            {
                var settle = Math.Min(1f, (t - InSeconds) / 0.1f);
                Opacity = 1f;
                Scale = _reduceMotion ? 1f : Overshoot + (1f - Overshoot) * settle;
            }
            else
            {
                var k = (t - InSeconds - HoldSeconds) / OutSeconds;
                Opacity = 1f - k;
                Scale = 1f;
            }
        }

        private static float EaseOut(float k) => 1f - (1f - k) * (1f - k) * (1f - k);
    }
}
