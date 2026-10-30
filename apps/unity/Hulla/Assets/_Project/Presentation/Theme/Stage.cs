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

        public static void Fit(VisualElement holder, VisualElement stage, float width, float height)
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
            });
        }
    }
}
