using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Navigation
{
    /// <summary>
    /// Keeps the field being typed in above the on-screen keyboard: the current screen slides up by
    /// just enough, and back down when the keyboard closes.
    /// </summary>
    public static class KeyboardAvoidance
    {
        /// <summary>Space left between the field and the keyboard, in panel units.</summary>
        public const float Margin = 16f;

        /// <summary>Used when the platform reports no keyboard height.</summary>
        public const float FallbackShare = 0.42f;

        /// <returns>How far to move the screen up, in panel units; 0 when the field is already clear.</returns>
        public static float Shift(float fieldBottom, float panelHeight, float keyboardHeight)
        {
            if (keyboardHeight <= 0f)
            {
                return 0f;
            }

            var visibleBottom = panelHeight - keyboardHeight - Margin;
            return Mathf.Max(0f, fieldBottom - visibleBottom);
        }

        /// <summary>The keyboard's height in panel units, or 0 when it is closed.</summary>
        public static float KeyboardHeight(float panelHeight)
        {
            if (!TouchScreenKeyboard.isSupported || !TouchScreenKeyboard.visible || Screen.height <= 0)
            {
                return 0f;
            }

            var pixels = TouchScreenKeyboard.area.height;
            var share = pixels > 0f ? pixels / Screen.height : FallbackShare;
            return share * panelHeight;
        }

        /// <summary>Moves <paramref name="screen"/> so a focused text field inside it stays visible.</summary>
        public static void Apply(VisualElement root, VisualElement? screen)
        {
            if (screen == null)
            {
                return;
            }

            var panelHeight = root.worldBound.height;
            var keyboard = KeyboardHeight(panelHeight);
            var shift = 0f;
            if (keyboard > 0f && root.focusController?.focusedElement is VisualElement focused && screen.Contains(focused) && IsTextInput(focused))
            {
                // The field's position without the current shift.
                var current = screen.resolvedStyle.translate.y;
                shift = Shift(focused.worldBound.yMax - current, panelHeight, keyboard);
            }

            if (!Mathf.Approximately(screen.resolvedStyle.translate.y, -shift))
            {
                screen.style.translate = new Translate(0, -shift);
            }
        }

        private static bool IsTextInput(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
            {
                if (e is TextField)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
