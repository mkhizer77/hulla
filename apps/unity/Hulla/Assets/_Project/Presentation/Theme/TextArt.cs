using UnityEngine.UIElements;

namespace Hulla.Presentation.Theme
{
    /// <summary>
    /// Lettered words drawn as art (docs/design/basant-home.md): a label marked with its key shows the
    /// language's text sprite where Basant.uss has one for the root's <c>lang--</c> class, and its own
    /// localised text everywhere else, so a language without art still reads.
    /// </summary>
    public static class TextArt
    {
        public const string ClassName = "text-art";

        public static T Mark<T>(T label, string key)
            where T : TextElement
        {
            label.AddToClassList(ClassName);
            label.AddToClassList(ClassName + "--" + key);
            return label;
        }

        /// <summary>The class on the shell root that picks the text sprites: lang--en, lang--ur and so on.</summary>
        public static string LanguageClass(string locale) => "lang--" + locale.ToLowerInvariant();
    }
}
