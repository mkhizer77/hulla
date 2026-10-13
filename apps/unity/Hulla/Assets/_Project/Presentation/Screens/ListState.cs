using System;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens
{
    /// <summary>
    /// The three states every list has besides its rows (GDD 14.13, design §2): loading as three
    /// pulsing bars, empty as one sentence with one action, an error inline with Retry. Never a blank screen.
    /// </summary>
    public static class ListState
    {
        public static void Loading(VisualElement list)
        {
            var skeleton = new VisualElement();
            skeleton.AddToClassList("skeleton");
            for (var i = 0; i < 3; i++)
            {
                var bar = new VisualElement();
                bar.AddToClassList("skeleton__bar");
                bar.AddToClassList("skeleton__bar--" + i);
                skeleton.Add(bar);
            }

            list.Add(skeleton);
        }

        public static void Empty(VisualElement list, string text, string? actionLabel = null, Action? action = null)
        {
            var box = new VisualElement();
            box.AddToClassList("empty");
            var label = new Label(text);
            label.AddToClassList("empty__text");
            box.Add(label);
            if (actionLabel != null && action != null)
            {
                var button = new Button(action) { text = actionLabel };
                button.AddToClassList("button");
                button.AddToClassList("button--secondary");
                box.Add(button);
            }

            list.Add(box);
        }

        public static void Error(VisualElement list, string code, Action? retry)
        {
            var box = new VisualElement();
            box.AddToClassList("empty");
            box.AddToClassList("empty--error");
            var label = new Label(UiText.Error(code));
            label.AddToClassList("empty__text");
            box.Add(label);
            if (retry != null)
            {
                var button = new Button(retry) { text = UiText.Retry };
                button.AddToClassList("button");
                button.AddToClassList("button--secondary");
                box.Add(button);
            }

            list.Add(box);
        }
    }
}
