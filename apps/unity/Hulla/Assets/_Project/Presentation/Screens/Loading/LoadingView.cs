using Hulla.Presentation.Text;
using Hulla.Presentation.Theme;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Loading
{
    /// <summary>
    /// The first screen (docs/design/basant-play.md): the HULLA kite on the Home sky, a bar that fills as
    /// the launch steps finish and one word under it. It stays up until Home is ready.
    /// </summary>
    public sealed class LoadingView
    {
        public const float StageHeight = 1936f;

        private readonly VisualElement _fill;

        public LoadingView(VisualElement root)
        {
            Stage.Fit(root.Q<VisualElement>("stage-holder"), root.Q<VisualElement>("stage"), Stage.Width, StageHeight);
            _fill = root.Q<VisualElement>("bar-fill");
            root.Q<Label>("status").text = UiText.Loading;
            Set(0, 1);
        }

        /// <summary>How many of the launch steps are done.</summary>
        public void Set(int done, int of)
        {
            var ratio = of <= 0 ? 0f : Mathf.Clamp01((float)done / of);
            _fill.style.width = Length.Percent(ratio * 100f);
        }
    }
}
