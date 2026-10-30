using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// The turn timer (GDD 14.5): a ring that empties as the deadline nears, amber in the last 5 s and
    /// red in the last 2 s (<see cref="TimerPhase"/>). It hugs the face's outline at one thickness for every
    /// seat, with a soft glow under the part that is left; the warning pulse brightens the glow and never
    /// changes the ring's size.
    /// </summary>
    public sealed class TimerRing : VisualElement
    {
        public const float WarnSeconds = TimerPhase.WarnSeconds;
        public const float LineWidth = 10f;
        private static readonly Color Track = new Color32(0xFF, 0xE6, 0xA6, 0xE6);
        private static readonly Color Halo = new Color32(0xFF, 0xC8, 0x50, 0xFF);

        // The palette's saffron, mustard and thulla red (GDD 15.2); painter2D cannot read USS variables.
        private static readonly Color Calm = new Color32(0xF4, 0xA3, 0x00, 0xFF);
        private static readonly Color Warn = new Color32(0xE0, 0xB0, 0x00, 0xFF);
        private static readonly Color Critical = new Color32(0xE5, 0x39, 0x35, 0xFF);
        private float _fraction;
        private TimerPhase.Phase _phase;
        private float _pulse = 1f;

        public TimerRing()
        {
            AddToClassList("timer-ring");
            generateVisualContent += Draw;
        }

        /// <param name="fraction">Time left as a share of the turn, 1 at the start.</param>
        /// <param name="pulse">From <see cref="TimerPhase.Pulse"/>: 1 is still, more brightens the glow.</param>
        public void Show(float fraction, float secondsLeft, float pulse = 1f)
        {
            var phase = TimerPhase.For(secondsLeft);
            fraction = Mathf.Clamp01(fraction);
            if (Mathf.Abs(fraction - _fraction) < 0.005f && phase == _phase && Mathf.Abs(pulse - _pulse) < 0.005f)
            {
                return;
            }

            _fraction = fraction;
            _phase = phase;
            _pulse = pulse;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            var radius = Mathf.Min(rect.width, rect.height) * 0.5f - LineWidth * 0.5f;
            if (radius <= 0f)
            {
                return;
            }

            var centre = rect.center;
            var painter = context.painter2D;

            // A warm halo all round (the concept's glow), kept outside the face's dark outline, then the pale
            // track the time drains into.
            painter.lineCap = LineCap.Butt;
            Stroke(painter, centre, radius + 6f, 0f, 360f, 16f, new Color(Halo.r, Halo.g, Halo.b, 0.2f));
            Stroke(painter, centre, radius + 4f, 0f, 360f, 10f, new Color(Halo.r, Halo.g, Halo.b, 0.35f));
            Stroke(painter, centre, radius, 0f, 360f, LineWidth, Track);
            if (_fraction <= 0f)
            {
                return;
            }

            var colour = _phase == TimerPhase.Phase.Critical ? Critical : _phase == TimerPhase.Phase.Warn ? Warn : Calm;
            var end = -90f + 360f * _fraction;

            // The time left, with its own glow that brightens at each warning pulse.
            var glow = Mathf.Clamp01(0.22f + (_pulse - 1f) * 4f);
            painter.lineCap = LineCap.Round;
            Stroke(painter, centre, radius, -90f, end, LineWidth + 8f, new Color(colour.r, colour.g, colour.b, glow));
            Stroke(painter, centre, radius, -90f, end, LineWidth, colour);
        }

        private static void Stroke(Painter2D painter, Vector2 centre, float radius, float start, float end, float width, Color colour)
        {
            painter.lineWidth = width;
            painter.strokeColor = colour;
            painter.BeginPath();
            painter.Arc(centre, radius, start, end);
            painter.Stroke();
        }
    }
}
