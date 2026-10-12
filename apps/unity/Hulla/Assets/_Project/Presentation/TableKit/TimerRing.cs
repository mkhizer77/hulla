using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>The turn timer (GDD 14.5): a ring that empties as the deadline nears and turns amber in the last 5 s.</summary>
    public sealed class TimerRing : VisualElement
    {
        public const float WarnSeconds = 5f;
        private static readonly Color Track = new Color(1f, 1f, 1f, 0.15f);
        private static readonly Color Normal = new Color(0.957f, 0.639f, 0f);
        private static readonly Color Warn = new Color(0.898f, 0.224f, 0.208f);
        private float _fraction;
        private bool _warn;

        public TimerRing()
        {
            AddToClassList("timer-ring");
            generateVisualContent += Draw;
        }

        /// <param name="fraction">Time left as a share of the turn, 1 at the start.</param>
        public void Show(float fraction, float secondsLeft)
        {
            var warn = secondsLeft <= WarnSeconds;
            fraction = Mathf.Clamp01(fraction);
            if (Mathf.Abs(fraction - _fraction) < 0.005f && warn == _warn)
            {
                return;
            }

            _fraction = fraction;
            _warn = warn;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            var radius = Mathf.Min(rect.width, rect.height) * 0.5f - 4f;
            if (radius <= 0f)
            {
                return;
            }

            var centre = rect.center;
            var painter = context.painter2D;
            painter.lineWidth = 6f;
            painter.lineCap = LineCap.Round;
            painter.strokeColor = Track;
            painter.BeginPath();
            painter.Arc(centre, radius, 0f, 360f);
            painter.Stroke();
            if (_fraction <= 0f)
            {
                return;
            }

            painter.strokeColor = _warn ? Warn : Normal;
            painter.BeginPath();
            painter.Arc(centre, radius, -90f, -90f + 360f * _fraction);
            painter.Stroke();
        }
    }
}
