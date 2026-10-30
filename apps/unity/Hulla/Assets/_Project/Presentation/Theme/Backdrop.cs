using UnityEngine.UIElements;

namespace Hulla.Presentation.Theme
{
    /// <summary>The scene behind a screen (docs/design/ux-screens: Home and loading get the festival, the rest the calm sky).</summary>
    public enum BackdropKind
    {
        Festival,
        Calm,
        Felt,
    }

    /// <summary>
    /// Paints the scene on the shell root, so it reaches under the notch and the home bar while the
    /// screens themselves stay inside the safe area with transparent backgrounds.
    /// </summary>
    public static class Backdrop
    {
        public const string CalmClass = "backdrop--calm";
        public const string FeltClass = "backdrop--felt";

        /// <summary>Sets the scene on whatever element carries the skin classes above <paramref name="inside"/>.</summary>
        public static void Set(VisualElement? inside, BackdropKind kind)
        {
            var root = RootOf(inside);
            if (root == null)
            {
                return;
            }

            root.EnableInClassList(CalmClass, kind == BackdropKind.Calm);
            root.EnableInClassList(FeltClass, kind == BackdropKind.Felt);
        }

        /// <summary>The scene a freshly shown screen asks for: festival for Home and loading, calm otherwise.</summary>
        public static BackdropKind For(VisualElement screen)
        {
            var face = screen.ClassListContains("screen") ? screen : screen.Q(className: "screen");
            if (face == null)
            {
                return BackdropKind.Calm;
            }

            return face.ClassListContains("home") || face.ClassListContains("loading") ? BackdropKind.Festival : BackdropKind.Calm;
        }

        private static VisualElement? RootOf(VisualElement? element)
        {
            var current = element;
            while (current != null)
            {
                if (current.ClassListContains(ThemeClasses.Skinned) || current.ClassListContains("shell-root"))
                {
                    return current;
                }

                current = current.parent;
            }

            return null;
        }
    }
}
