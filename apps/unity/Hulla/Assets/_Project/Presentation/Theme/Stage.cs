using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Theme
{
    /// <summary>
    /// A screen drawn at its concept's size (docs/design/basant-home.md): the stage keeps its design
    /// size and scales to fit its holder, centred, so every phone and tablet keeps the concept's layout.
    /// </summary>
    public static class Stage
    {
        /// <summary>The design width of every Basant screen.</summary>
        public const float Width = 1080f;

        /// <summary>
        /// Scales the stage to fit, centred. On a phone taller than the stage the <paramref name="topRow"/>
        /// (the close or back button and the title beside it) rises by the spare height, so it sits on the
        /// same line as every other screen's top bar instead of floating lower with the centred stage.
        /// </summary>
        public static void Fit(VisualElement holder, VisualElement stage, float width, float height, params VisualElement?[] topRow)
        {
            holder.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var size = holder.contentRect.size;
                if (size.x <= 0f || size.y <= 0f)
                {
                    return;
                }

                var scale = Math.Min(size.x / width, size.y / height);
                stage.style.scale = new Scale(new Vector3(scale, scale, 1f));
                var lift = -Math.Max(0f, (size.y - (height * scale)) / 2f) / scale;
                foreach (var element in topRow)
                {
                    if (element != null)
                    {
                        element.style.translate = new Translate(0, lift);
                    }
                }
            });
        }
    }
}
