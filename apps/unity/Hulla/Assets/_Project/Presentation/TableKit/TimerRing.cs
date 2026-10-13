using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// The turn timer (GDD 14.5): a ring that empties as the deadline nears, amber in the last 5 s and
    /// red in the last 2 s (<see cref="TimerPhase"/>). The caller pulses it through its scale.
    /// </summary>
    public sealed class TimerRing : VisualElement
    {
        public const float WarnSeconds = TimerPhase.WarnSeconds;
        private static readonly Color Track = new Color(1f, 1f, 1f, 0.15f);

        // The palette's saffron, mustard and thulla red (GDD 15.2); painter2D cannot read USS variables.
        private static readonly Color Calm = new Color32(0xF4, 0xA3, 0x00, 0xFF);
        private static readonly Color Warn = new Color32(0xE0, 0xB0, 0x00, 0xFF);
        private static readonly Color Critical = new Color32(0xE5, 0x39, 0x35, 0xFF);
        private float _fraction;
        private TimerPhase.Phase _phase;

        public TimerRing()
        {
            AddToClassList("timer-ring");
            generateVisualContent += Draw;
        }

        /// <param name="fraction">Time left as a share of the turn, 1 at the start.</param>
        public void Show(float fraction, float secondsLeft)
        {
            var phase = TimerPhase.For(secondsLeft);
            fraction = Mathf.Clamp01(fraction);
            if (Mathf.Abs(fraction - _fraction) < 0.005f && phase == _phase)
            {
                return;
            }

            _fraction = fraction;
            _phase = phase;
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

            painter.strokeColor = _phase == TimerPhase.Phase.Critical ? Critical : _phase == TimerPhase.Phase.Warn ? Warn : Calm;
            painter.BeginPath();
            painter.Arc(centre, radius, -90f, -90f + 360f * _fraction);
            painter.Stroke();
        }
    }
}
