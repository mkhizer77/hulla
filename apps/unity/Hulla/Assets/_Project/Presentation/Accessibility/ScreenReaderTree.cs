using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Accessibility;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Accessibility
{
    /// <summary>
    /// The screen reader's view of a UI Toolkit panel (GDD 27): every visible control and text in
    /// reading order, with its label, role, place on the panel and what a double tap does. Unity's
    /// screen reader reads an <see cref="AccessibilityHierarchy"/>, not the panel, so
    /// <see cref="ScreenReader"/> publishes these items as one.
    /// </summary>
    public static class ScreenReaderTree
    {
        public sealed class Item
        {
            public Item(string label, AccessibilityRole role, Rect panelRect, Action? invoke, bool disabled, bool selected, string value = "")
            {
                Label = label;
                Role = role;
                PanelRect = panelRect;
                Invoke = invoke;
                Disabled = disabled;
                Selected = selected;
                Value = value;
            }

            public string Label { get; }

            public AccessibilityRole Role { get; }

            /// <summary>Where it is on the panel, in panel units with the origin top left.</summary>
            public Rect PanelRect { get; }

            public Action? Invoke { get; }

            public bool Disabled { get; }

            public bool Selected { get; }

            public string Value { get; }
        }

        public static List<Item> Read(VisualElement root)
        {
            var items = new List<Item>();
            Walk(root, items);
            return items;
        }

        /// <summary>Visible buttons the reader would skip because they have no words (an icon with no tooltip).</summary>
        public static List<Button> Unlabelled(VisualElement root)
        {
            var silent = new List<Button>();
            root.Query<Button>().ForEach(button =>
            {
                for (VisualElement? up = button; up != null; up = up.hierarchy.parent)
                {
                    if (Hidden(up) || Accessible.Of(up) != null && up != button)
                    {
                        return;
                    }
                }

                if (Accessible.Of(button) == null && LabelOf(button).Length == 0)
                {
                    silent.Add(button);
                }
            });
            return silent;
        }

        /// <summary>A short fingerprint of what the reader would say, to rebuild only when it changes.</summary>
        public static string Signature(IReadOnlyList<Item> items)
        {
            var text = new StringBuilder();
            foreach (var item in items)
            {
                text.Append(item.Label).Append('|').Append((int)item.Role).Append('|').Append(item.Disabled).Append(item.Selected).Append(item.Value)
                    .Append('|').Append(Mathf.RoundToInt(item.PanelRect.x)).Append(',').Append(Mathf.RoundToInt(item.PanelRect.y)).Append('\n');
            }

            return text.ToString();
        }

        private static bool Hidden(VisualElement element) =>
            element.resolvedStyle.display == DisplayStyle.None ||
            element.resolvedStyle.visibility == Visibility.Hidden ||
            element.resolvedStyle.opacity <= 0.01f;

        private static void Walk(VisualElement element, List<Item> items)
        {
            if (Hidden(element))
            {
                return;
            }

            var rect = element.worldBound;
            var tagged = Accessible.Of(element);
            if (tagged != null)
            {
                items.Add(new Item(tagged.Label, tagged.Role, rect, tagged.Invoke, !element.enabledInHierarchy, false));
                return;
            }

            switch (element)
            {
                case Button button:
                    var label = LabelOf(button);
                    if (label.Length > 0)
                    {
                        items.Add(new Item(label, AccessibilityRole.Button, rect, () => Press(button), !button.enabledInHierarchy, false));
                    }

                    return;
                case Toggle toggle:
                    items.Add(new Item(toggle.label ?? toggle.text ?? "", AccessibilityRole.Toggle, rect, () => toggle.value = !toggle.value, !toggle.enabledInHierarchy, toggle.value));
                    return;
                case TextField field:
                    items.Add(new Item(field.label ?? field.textEdition.placeholder ?? "", AccessibilityRole.TextField, rect, () => field.Focus(), !field.enabledInHierarchy, false, field.value ?? ""));
                    return;
                case Slider slider:
                    items.Add(new Item(slider.label ?? "", AccessibilityRole.Slider, rect, null, !slider.enabledInHierarchy, false, Mathf.RoundToInt(slider.value).ToString()));
                    return;
                case TextElement text when !string.IsNullOrWhiteSpace(text.text):
                    var heading = text.name == "title" || text.ClassListContains("title") || text.ClassListContains("screen__title");
                    items.Add(new Item(text.text.Trim(), heading ? AccessibilityRole.Header : AccessibilityRole.StaticText, rect, null, false, false));
                    return;
            }

            foreach (var child in element.hierarchy.Children())
            {
                Walk(child, items);
            }
        }

        /// <summary>A button's words: its text, else the text inside it, else its tooltip.</summary>
        private static string LabelOf(Button button)
        {
            if (!string.IsNullOrWhiteSpace(button.text))
            {
                return button.text.Trim();
            }

            var inner = new StringBuilder();
            button.Query<TextElement>().ForEach(text =>
            {
                if (text != button && !string.IsNullOrWhiteSpace(text.text) && !Hidden(text))
                {
                    inner.Append(inner.Length > 0 ? ", " : "").Append(text.text.Trim());
                }
            });
            return inner.Length > 0 ? inner.ToString() : (button.tooltip ?? "").Trim();
        }

        /// <summary>Presses a button the way the keyboard or a gamepad would, so its own click handler runs.</summary>
        private static void Press(Button button)
        {
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }
    }
}
