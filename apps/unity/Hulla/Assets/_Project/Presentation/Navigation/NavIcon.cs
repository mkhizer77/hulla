using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Navigation
{
    /// <summary>
    /// A tab's icon drawn as strokes in the element's text colour, so the theme and the active state
    /// colour it like a label: a house, two friends, a cup and a bag, on a 24-unit grid.
    /// </summary>
    public sealed class NavIcon : VisualElement
    {
        private readonly NavTab _tab;

        public NavIcon(NavTab tab)
        {
            _tab = tab;
            AddToClassList("nav__glyph");
            AddToClassList("nav__icon");
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
            switch (_tab)
            {
                case NavTab.Home:
                    Path(painter, P(3, 11), P(12, 4), P(21, 11), P(21, 20), P(15, 20), P(15, 14), P(9, 14), P(9, 20), P(3, 20), P(3, 11));
                    break;
                case NavTab.Friends:
                    Circle(painter, P(9, 8), 3.6f * unit);
                    Arc(painter, P(9, 21), 7f * unit, 200f, 340f);
                    Arc(painter, P(16, 8), 3.4f * unit, -80f, 90f);
                    Arc(painter, P(17, 21.5f), 5.5f * unit, 250f, 330f);
                    break;
                case NavTab.Shop:
                    Path(painter, P(5, 8), P(19, 8), P(18, 21), P(6, 21), P(5, 8));
                    Arc(painter, P(12, 8), 3.2f * unit, 180f, 360f);
                    break;
                default:
                    Path(painter, P(7, 4), P(17, 4), P(16.5f, 11), P(12, 14.5f), P(7.5f, 11), P(7, 4));
                    Path(painter, P(12, 14.5f), P(12, 18));
                    Path(painter, P(8, 20.5f), P(16, 20.5f));
                    Arc(painter, P(7, 7.5f), 2.6f * unit, 90f, 270f);
                    Arc(painter, P(17, 7.5f), 2.6f * unit, -90f, 90f);
                    break;
            }
        }

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
