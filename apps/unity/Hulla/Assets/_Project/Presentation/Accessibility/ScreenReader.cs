using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Accessibility;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Accessibility
{
    /// <summary>
    /// TalkBack and VoiceOver for the app (GDD 27): while the phone's screen reader is on, the shell's
    /// panel is read every quarter second and published as Unity's accessibility hierarchy whenever
    /// what it would say changes; table moments are spoken as announcements. Off, it costs nothing.
    /// </summary>
    public sealed class ScreenReader
    {
        private const float Interval = 0.25f;

        private readonly VisualElement _root;
        private AccessibilityHierarchy? _hierarchy;
        private string _signature = "";
        private float _wait;
        private VisualElement? _lastScreen;

        public ScreenReader(VisualElement root)
        {
            _root = root;
        }

        /// <summary>True while the phone's screen reader is on.</summary>
        public static bool On => AssistiveSupport.isScreenReaderEnabled;

        /// <summary>Speaks a line now, when the screen reader is on ("Sara played King of Hearts").</summary>
        public static void Say(string text)
        {
            if (On && !string.IsNullOrEmpty(text))
            {
                AssistiveSupport.notificationDispatcher.SendAnnouncement(text);
            }
        }

        /// <param name="screen">The screen showing now; a new one is announced as a screen change.</param>
        public void Tick(float deltaSeconds, VisualElement? screen)
        {
            if (!On)
            {
                if (_hierarchy != null)
                {
                    AssistiveSupport.activeHierarchy = null;
                    _hierarchy = null;
                    _signature = "";
                }

                return;
            }

            _wait -= deltaSeconds;
            if (_wait > 0f && screen == _lastScreen)
            {
                return;
            }

            _wait = Interval;
            var items = ScreenReaderTree.Read(_root);
            var signature = ScreenReaderTree.Signature(items);
            var newScreen = screen != _lastScreen;
            _lastScreen = screen;
            if (signature == _signature && !newScreen)
            {
                return;
            }

            _signature = signature;
            _hierarchy = Build(items, _root.panel);
            AssistiveSupport.activeHierarchy = _hierarchy;
            if (newScreen)
            {
                AssistiveSupport.notificationDispatcher.SendScreenChanged(null);
            }
            else
            {
                AssistiveSupport.notificationDispatcher.SendLayoutChanged(null);
            }
        }

        public static AccessibilityHierarchy Build(IReadOnlyList<ScreenReaderTree.Item> items, IPanel? panel)
        {
            var hierarchy = new AccessibilityHierarchy();
            foreach (var item in items)
            {
                var node = hierarchy.AddNode(item.Label);
                node.role = item.Role;
                node.value = item.Value;
                var state = AccessibilityState.None;
                if (item.Disabled)
                {
                    state |= AccessibilityState.Disabled;
                }

                if (item.Selected)
                {
                    state |= AccessibilityState.Selected;
                }

                node.state = state;
                node.frame = ToScreen(item.PanelRect, panel);
                if (item.Invoke != null && !item.Disabled)
                {
                    var invoke = item.Invoke;
                    node.invoked += () =>
                    {
                        invoke();
                        return true;
                    };
                }
            }

            return hierarchy;
        }

        /// <summary>Panel units to screen pixels, origin top left as the screen reader's frames are.</summary>
        public static Rect ToScreen(Rect panelRect, IPanel? panel)
        {
            var panelWidth = panel?.visualTree.layout.width ?? 0f;
            var scale = panelWidth > 0f ? Screen.width / panelWidth : 1f;
            return new Rect(panelRect.x * scale, panelRect.y * scale, panelRect.width * scale, panelRect.height * scale);
        }
    }
}
