using System;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Navigation
{
    /// <summary>
    /// A bottom sheet over the dimmed screen (design: sheets for secondary actions, questions and
    /// pickers): the grabber, an optional title, then whatever the caller adds. It hangs off the
    /// shell root, so the dim reaches under the notch; the shell keeps its content in the safe area.
    /// A tap on the dim closes it.
    /// </summary>
    public static class Sheet
    {
        public const string Name = "shell-sheet";

        /// <summary>Opens a sheet (closing any open one) and returns the panel to fill.</summary>
        public static VisualElement Open(VisualElement anyInside, string? title, Action? closed = null, bool closeOnDim = true)
        {
            var root = RootOf(anyInside);
            root.Q(Name)?.RemoveFromHierarchy();
            var scrim = new VisualElement { name = Name };
            scrim.AddToClassList("overlay");
            scrim.AddToClassList("sheet-scrim");
            var panel = new VisualElement();
            panel.AddToClassList("sheet");
            var grabber = new VisualElement { pickingMode = PickingMode.Ignore };
            grabber.AddToClassList("sheet__grabber");
            panel.Add(grabber);
            if (!string.IsNullOrEmpty(title))
            {
                var label = new Label(title) { name = "sheet-title" };
                label.AddToClassList("sheet__title");
                panel.Add(label);
            }

            scrim.Add(panel);
            if (closeOnDim)
            {
                scrim.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.target == scrim)
                    {
                        scrim.RemoveFromHierarchy();
                        closed?.Invoke();
                    }
                });
            }

            scrim.userData = closed;
            root.Add(scrim);
            return panel;
        }

        /// <summary>Closes the sheet that holds <paramref name="inside"/> (or the open one).</summary>
        public static void Close(VisualElement? inside)
        {
            var current = inside;
            while (current != null && current.name != Name)
            {
                current = current.parent;
            }

            if (current == null && inside != null)
            {
                current = RootOf(inside).Q(Name);
            }

            if (current == null)
            {
                return;
            }

            var closed = current.userData as Action;
            current.RemoveFromHierarchy();
            closed?.Invoke();
        }

        /// <summary>True while a sheet is open over the screen that holds <paramref name="inside"/>.</summary>
        public static bool IsOpen(VisualElement inside) => RootOf(inside).Q(Name) != null;

        /// <summary>A row of answers or actions side by side, safe first; the caller adds the buttons.</summary>
        public static VisualElement Answers(VisualElement panel)
        {
            var row = new VisualElement();
            row.AddToClassList("sheet__answers");
            panel.Add(row);
            return row;
        }

        /// <summary>A sheet button: <c>yes</c> wears yellow, <c>danger</c> red, <c>teal</c> the share colour, else cream.</summary>
        public static Button Button(string text, Action onClick, string kind = "")
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("sheet__answer");
            if (kind.Length > 0)
            {
                button.AddToClassList("sheet__answer--" + kind);
            }

            return button;
        }

        /// <summary>A plain line of text in the sheet.</summary>
        public static Label Line(string text, string? extraClass = null)
        {
            var label = new Label(text);
            label.AddToClassList("sheet__body");
            if (extraClass != null)
            {
                label.AddToClassList(extraClass);
            }

            return label;
        }

        private static VisualElement RootOf(VisualElement element)
        {
            var current = element;
            VisualElement top = element;
            while (current != null)
            {
                if (current.ClassListContains("shell-root") || current.ClassListContains(Theme.ThemeClasses.Skinned))
                {
                    return current;
                }

                top = current;
                current = current.parent;
            }

            return top;
        }
    }
}
