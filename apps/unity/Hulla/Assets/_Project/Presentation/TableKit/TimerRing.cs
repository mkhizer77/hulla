using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// The turn timer (GDD 14.5, the art pack's avatar pod): a recessed track between two brass rims that
    /// fills clockwise from the top as the turn runs out, saffron, then amber in the last 5 s and red in
    /// the last 2 s (<see cref="TimerPhase"/>). It never changes size; only the fill moves.
    /// </summary>
    public sealed class TimerRing : VisualElement
    {
        public const float WarnSeconds = TimerPhase.WarnSeconds;
        private static readonly Color Track = new Color32(0x1B, 0x12, 0x0B, 0xE6);
        private static readonly Color Rim = new Color32(0xD4, 0x9B, 0x35, 0xFF);
        private static readonly Color RimShadow = new Color32(0x8C, 0x5D, 0x14, 0xFF);
        private const float TrackWidth = 14f;
        private const float FillWidth = 10f;
        private const float RimWidth = 3f;

        // A recessed walnut track, then saffron, amber and danger red (art pack, avatar pod); painter2D cannot read USS variables.
        private static readonly Color Calm = new Color32(0xF9, 0xA8, 0x25, 0xFF);
        private static readonly Color Warn = new Color32(0xFF, 0x8F, 0x00, 0xFF);
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

        /// <summary>How much of the ring is filled: 0 when the turn starts, 1 at the deadline.</summary>
        public float Filled => 1f - _fraction;

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            var radius = Mathf.Min(rect.width, rect.height) * 0.5f - TrackWidth * 0.5f - RimWidth - 1f;
            if (radius <= 0f)
            {
                return;
            }

            var centre = rect.center;
            var painter = context.painter2D;
            painter.lineCap = LineCap.Butt;
            Circle(painter, centre, radius, TrackWidth, Track);
            Circle(painter, centre, radius + TrackWidth * 0.5f + RimWidth * 0.5f, RimWidth, Rim);
            Circle(painter, centre, radius - TrackWidth * 0.5f - RimWidth * 0.5f, RimWidth, RimShadow);
            var filled = Filled;
            if (filled <= 0.001f)
            {
                return;
            }

            painter.lineWidth = FillWidth;
            painter.lineCap = LineCap.Round;
            painter.strokeColor = _phase == TimerPhase.Phase.Critical ? Critical : _phase == TimerPhase.Phase.Warn ? Warn : Calm;
            painter.BeginPath();
            painter.Arc(centre, radius, -90f, -90f + 360f * Mathf.Min(filled, 0.999f));
            painter.Stroke();
        }

        private static void Circle(Painter2D painter, Vector2 centre, float radius, float width, Color colour)
        {
            painter.lineWidth = width;
            painter.strokeColor = colour;
            painter.BeginPath();
            painter.Arc(centre, radius, 0f, 360f);
            painter.Stroke();
        }
    }
}
