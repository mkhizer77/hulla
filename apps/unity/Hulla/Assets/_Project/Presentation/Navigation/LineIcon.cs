using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Navigation
{
    public enum LineIconKind
    {
        Gear,
        Target,
        Trophy,
        Book,
    }

    /// <summary>
    /// A small icon drawn as strokes in the element's text colour, like the tab icons (NavIcon), for
    /// buttons that should not be words: settings, missions, the leaderboard, how to play.
    /// </summary>
    public sealed class LineIcon : VisualElement
    {
        private readonly LineIconKind _kind;

        public LineIcon(LineIconKind kind)
        {
            _kind = kind;
            AddToClassList("line-icon");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
        }

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            var unit = Mathf.Min(rect.width, rect.height) / 24f;
            if (unit <= 0f)
            {
                return;
            }

            var origin = new Vector2(rect.center.x - 12f * unit, rect.center.y - 12f * unit);
            Vector2 P(float x, float y) => origin + new Vector2(x * unit, y * unit);
            var painter = context.painter2D;
            painter.strokeColor = resolvedStyle.color;
            painter.lineWidth = 2.1f * unit;
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            switch (_kind)
            {
                case LineIconKind.Gear:
                    Circle(painter, P(12, 12), 3.2f * unit);
                    for (var i = 0; i < 8; i++)
                    {
                        var a = i * 45f * Mathf.Deg2Rad;
                        var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        Line(painter, P(12, 12) + dir * 7.2f * unit, P(12, 12) + dir * 9.6f * unit);
                    }

                    Circle(painter, P(12, 12), 7.2f * unit);
                    break;
                case LineIconKind.Target:
                    Circle(painter, P(12, 12), 9f * unit);
                    Circle(painter, P(12, 12), 5f * unit);
                    Circle(painter, P(12, 12), 1.2f * unit);
                    break;
                case LineIconKind.Trophy:
                    Path(painter, P(7, 4), P(17, 4), P(16.5f, 10), P(12, 14), P(7.5f, 10), P(7, 4));
                    Arc(painter, P(7, 7), 3f * unit, 90f, 270f);
                    Arc(painter, P(17, 7), 3f * unit, -90f, 90f);
                    Line(painter, P(12, 14), P(12, 18));
                    Line(painter, P(8, 20), P(16, 20));
                    break;
                default:
                    Path(painter, P(4, 5), P(11, 6.5f), P(11, 20), P(4, 18.5f), P(4, 5));
                    Path(painter, P(20, 5), P(13, 6.5f), P(13, 20), P(20, 18.5f), P(20, 5));
                    break;
            }
        }

        private static void Line(Painter2D painter, Vector2 a, Vector2 b) => Path(painter, a, b);

        private static void Path(Painter2D painter, params Vector2[] points)
        {
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (var i = 1; i < points.Length; i++)
            {
                painter.LineTo(points[i]);
            }

            painter.Stroke();
        }

        private static void Circle(Painter2D painter, Vector2 centre, float radius) => Arc(painter, centre, radius, 0f, 360f);

        private static void Arc(Painter2D painter, Vector2 centre, float radius, float from, float to)
        {
            painter.BeginPath();
            painter.Arc(centre, radius, from, to);
            painter.Stroke();
        }
    }
}
