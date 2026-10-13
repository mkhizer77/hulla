using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// Draws a card face with vector pips (GDD 15.4) until the Classic and Desi Court decks land:
    /// the standard pip layouts for 2 to 10, one large pip for the ace, and a framed band with a
    /// lattice for the court cards. Suits are bezier shapes, so they stay crisp at any card size.
    /// </summary>
    public static class CardFace
    {
        /// <summary>Pip centres in a unit box (x across, y down) for ranks 2 to 10; flipped pips are below the middle.</summary>
        private static readonly Dictionary<char, Vector2[]> Layouts = new Dictionary<char, Vector2[]>
        {
            ['2'] = new[] { V(0.5f, 0.2f), V(0.5f, 0.8f) },
            ['3'] = new[] { V(0.5f, 0.2f), V(0.5f, 0.5f), V(0.5f, 0.8f) },
            ['4'] = new[] { V(0.3f, 0.2f), V(0.7f, 0.2f), V(0.3f, 0.8f), V(0.7f, 0.8f) },
            ['5'] = new[] { V(0.3f, 0.2f), V(0.7f, 0.2f), V(0.5f, 0.5f), V(0.3f, 0.8f), V(0.7f, 0.8f) },
            ['6'] = new[] { V(0.3f, 0.2f), V(0.7f, 0.2f), V(0.3f, 0.5f), V(0.7f, 0.5f), V(0.3f, 0.8f), V(0.7f, 0.8f) },
            ['7'] = new[] { V(0.3f, 0.2f), V(0.7f, 0.2f), V(0.5f, 0.35f), V(0.3f, 0.5f), V(0.7f, 0.5f), V(0.3f, 0.8f), V(0.7f, 0.8f) },
            ['8'] = new[] { V(0.3f, 0.2f), V(0.7f, 0.2f), V(0.5f, 0.35f), V(0.3f, 0.5f), V(0.7f, 0.5f), V(0.5f, 0.65f), V(0.3f, 0.8f), V(0.7f, 0.8f) },
            ['9'] = new[] { V(0.3f, 0.2f), V(0.7f, 0.2f), V(0.3f, 0.4f), V(0.7f, 0.4f), V(0.5f, 0.5f), V(0.3f, 0.6f), V(0.7f, 0.6f), V(0.3f, 0.8f), V(0.7f, 0.8f) },
            ['T'] = new[] { V(0.3f, 0.2f), V(0.7f, 0.2f), V(0.5f, 0.3f), V(0.3f, 0.4f), V(0.7f, 0.4f), V(0.3f, 0.6f), V(0.7f, 0.6f), V(0.5f, 0.7f), V(0.3f, 0.8f), V(0.7f, 0.8f) },
        };

        private static Vector2 V(float x, float y) => new Vector2(x, y);

        /// <param name="area">The pip field inside the card, below the corner indices.</param>
        public static void Draw(Painter2D painter, Rect area, char rank, char suit, Color colour, Color frame)
        {
            if (rank == 'A')
            {
                Pip(painter, area.center, Mathf.Min(area.width, area.height) * 0.42f, suit, colour);
                return;
            }

            if (rank == 'J' || rank == 'Q' || rank == 'K')
            {
                Court(painter, area, suit, colour, frame);
                return;
            }

            if (!Layouts.TryGetValue(rank, out var layout))
            {
                return;
            }

            var size = area.width * 0.21f;
            foreach (var at in layout)
            {
                var centre = new Vector2(area.xMin + at.x * area.width, area.yMin + at.y * area.height);
                Pip(painter, centre, size, suit, colour, flipped: at.y > 0.5f);
            }
        }

        /// <summary>The court band: a frame, a lattice, and the suit at each end; the big letter sits over it as a label.</summary>
        private static void Court(Painter2D painter, Rect area, char suit, Color colour, Color frame)
        {
            var band = new Rect(area.xMin + area.width * 0.08f, area.yMin + area.height * 0.06f, area.width * 0.84f, area.height * 0.88f);
            painter.lineWidth = 2f;
            painter.strokeColor = frame;
            painter.fillColor = new Color(frame.r, frame.g, frame.b, 0.12f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(band.xMin, band.yMin));
            painter.LineTo(new Vector2(band.xMax, band.yMin));
            painter.LineTo(new Vector2(band.xMax, band.yMax));
            painter.LineTo(new Vector2(band.xMin, band.yMax));
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();

            // A diamond lattice in the band, light so the letter reads over it.
            painter.strokeColor = new Color(frame.r, frame.g, frame.b, 0.35f);
            painter.lineWidth = 1f;
            var cell = band.width / 4f;
            for (var y = band.yMin; y < band.yMax; y += cell)
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(band.xMin, y));
                painter.LineTo(new Vector2(band.xMax, Mathf.Min(band.yMax, y + band.width)));
                painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(new Vector2(band.xMax, y));
                painter.LineTo(new Vector2(band.xMin, Mathf.Min(band.yMax, y + band.width)));
                painter.Stroke();
            }

            var pip = band.width * 0.22f;
            Pip(painter, new Vector2(band.xMin + pip * 0.8f, band.yMin + pip * 0.8f), pip, suit, colour);
            Pip(painter, new Vector2(band.xMax - pip * 0.8f, band.yMax - pip * 0.8f), pip, suit, colour, flipped: true);
        }

        /// <summary>One suit symbol of <paramref name="size"/> across, centred.</summary>
        public static void Pip(Painter2D painter, Vector2 centre, float size, char suit, Color colour, bool flipped = false)
        {
            painter.fillColor = colour;
            painter.strokeColor = colour;
            painter.lineWidth = 0f;
            var s = size / 2f;
            Vector2 P(float x, float y) => flipped ? new Vector2(centre.x - x * s, centre.y - y * s) : new Vector2(centre.x + x * s, centre.y + y * s);
            painter.BeginPath();
            switch (suit)
            {
                case 'H':
                    painter.MoveTo(P(0f, 1f));
                    painter.BezierCurveTo(P(-1.1f, 0.1f), P(-1.05f, -0.9f), P(-0.5f, -0.9f));
                    painter.BezierCurveTo(P(-0.2f, -0.9f), P(0f, -0.65f), P(0f, -0.45f));
                    painter.BezierCurveTo(P(0f, -0.65f), P(0.2f, -0.9f), P(0.5f, -0.9f));
                    painter.BezierCurveTo(P(1.05f, -0.9f), P(1.1f, 0.1f), P(0f, 1f));
                    break;
                case 'D':
                    painter.MoveTo(P(0f, -1f));
                    painter.BezierCurveTo(P(0.45f, -0.4f), P(0.65f, -0.2f), P(0.9f, 0f));
                    painter.BezierCurveTo(P(0.65f, 0.2f), P(0.45f, 0.4f), P(0f, 1f));
                    painter.BezierCurveTo(P(-0.45f, 0.4f), P(-0.65f, 0.2f), P(-0.9f, 0f));
                    painter.BezierCurveTo(P(-0.65f, -0.2f), P(-0.45f, -0.4f), P(0f, -1f));
                    break;
                case 'S':
                    painter.MoveTo(P(0f, -1f));
                    painter.BezierCurveTo(P(0.6f, -0.3f), P(1.05f, 0.05f), P(0.95f, 0.45f));
                    painter.BezierCurveTo(P(0.85f, 0.9f), P(0.3f, 0.9f), P(0.1f, 0.55f));
                    painter.BezierCurveTo(P(0.15f, 0.8f), P(0.35f, 0.95f), P(0.45f, 1f));
                    painter.LineTo(P(-0.45f, 1f));
                    painter.BezierCurveTo(P(-0.35f, 0.95f), P(-0.15f, 0.8f), P(-0.1f, 0.55f));
                    painter.BezierCurveTo(P(-0.3f, 0.9f), P(-0.85f, 0.9f), P(-0.95f, 0.45f));
                    painter.BezierCurveTo(P(-1.05f, 0.05f), P(-0.6f, -0.3f), P(0f, -1f));
                    break;
                default:
                    // Club: three lobes and a stem.
                    painter.Arc(P(0f, -0.5f), 0.42f * s, 0f, 360f);
                    painter.ClosePath();
                    painter.Fill();
                    painter.BeginPath();
                    painter.Arc(P(-0.45f, 0.2f), 0.42f * s, 0f, 360f);
                    painter.ClosePath();
                    painter.Fill();
                    painter.BeginPath();
                    painter.Arc(P(0.45f, 0.2f), 0.42f * s, 0f, 360f);
                    painter.ClosePath();
                    painter.Fill();
                    painter.BeginPath();
                    painter.MoveTo(P(-0.12f, 0.1f));
                    painter.LineTo(P(0.12f, 0.1f));
                    painter.LineTo(P(0.4f, 1f));
                    painter.LineTo(P(-0.4f, 1f));
                    break;
            }

            painter.ClosePath();
            painter.Fill();
        }
    }
}
