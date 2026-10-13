using System;
using Hulla.Application.Settings;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Theme
{
    /// <summary>
    /// Turns the visual settings into USS classes on the shell root, so every screen picks them up
    /// live: the table theme, the four-colour deck, high contrast, text size, large cards and the
    /// left-handed layout.
    /// </summary>
    public static class ThemeClasses
    {
        public static void Apply(VisualElement root, SettingsModel settings)
        {
            root.AddToClassList(Skinned);
            foreach (TableSkin skin in Enum.GetValues(typeof(TableSkin)))
            {
                root.EnableInClassList(ClassOf(skin), skin == settings.Skin);
            }

            root.EnableInClassList("deck--four", settings.FourColourDeck);
            root.EnableInClassList("theme--contrast", settings.HighContrast);
            root.EnableInClassList("cards--large", settings.LargeCards);
            root.EnableInClassList("hand--left", settings.LeftHanded);
            var scale = settings.TextScale;
            root.EnableInClassList("text--125", scale == TextScale.Large);
            root.EnableInClassList("text--150", scale == TextScale.Larger);
            root.EnableInClassList("text--200", scale == TextScale.Largest);
        }

        /// <summary>On the root whenever a theme applies; the rules every theme shares hang off it.</summary>
        public const string Skinned = "skinned";

        /// <summary>The root class a theme's stylesheet (Skins.uss) hangs off: skin--club and so on.</summary>
        public static string ClassOf(TableSkin skin) => "skin--" + skin.ToString().ToLowerInvariant();
    }
}
