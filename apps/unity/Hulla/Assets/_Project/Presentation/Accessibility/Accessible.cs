using System;
using System.Runtime.CompilerServices;
using UnityEngine.Accessibility;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Accessibility
{
    /// <summary>
    /// What the screen reader says for an element the stylesheet draws as art (a card in the hand, an
    /// icon button), and what a double tap does (GDD 27). Controls with their own text need nothing:
    /// <see cref="ScreenReaderTree"/> reads buttons, toggles, fields and labels as they are.
    /// </summary>
    public static class Accessible
    {
        public sealed class Info
        {
            public Info(string label, AccessibilityRole role, Action? invoke)
            {
                Label = label;
                Role = role;
                Invoke = invoke;
            }

            public string Label { get; }

            public AccessibilityRole Role { get; }

            public Action? Invoke { get; }
        }

        private static readonly ConditionalWeakTable<VisualElement, Info> Table = new ConditionalWeakTable<VisualElement, Info>();

        public static void Set(VisualElement element, string label, AccessibilityRole role = AccessibilityRole.Button, Action? invoke = null)
        {
            Table.Remove(element);
            Table.Add(element, new Info(label, role, invoke));
        }

        public static Info? Of(VisualElement element) => Table.TryGetValue(element, out var info) ? info : null;
    }
}
