using System;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// The stamp's motion (GDD 15.7): 120 ms in with a 1.08 overshoot, a 900 ms hold, 200 ms out, and
    /// a short shake of the table under it. With reduce-motion the stamp only fades and nothing shakes.
    /// Pure timing so it can be tested without a frame loop.
    /// </summary>
    public sealed class StampTimeline
    {
        public const float InSeconds = 0.12f;
        public const float HoldSeconds = 0.9f;
        public const float OutSeconds = 0.2f;
        public const float ShakeSeconds = 0.3f;
        public const float ShakePixels = 10f;
        private const float Overshoot = 1.08f;

        private float _elapsed = float.MaxValue;
        private bool _reduceMotion;

        public bool Active => _elapsed < Total;

        public float Scale { get; private set; } = 1f;

        public float Opacity { get; private set; }

        public float ShakeX { get; private set; }

        private static float Total => InSeconds + HoldSeconds + OutSeconds;

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

            var t = _elapsed;
            if (t < InSeconds)
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

            ShakeX = _reduceMotion || t >= ShakeSeconds
                ? 0f
                : (float)Math.Sin(t * 70f) * ShakePixels * (1f - t / ShakeSeconds);
        }

        private static float EaseOut(float k) => 1f - (1f - k) * (1f - k) * (1f - k);
    }
}
