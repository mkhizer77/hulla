using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens
{
    /// <summary>A thin progress bar: a track with a fill for how far along something is (XP, a mission, a pass tier).</summary>
    public sealed class Meter : VisualElement
    {
        private readonly VisualElement _fill;

        public Meter()
        {
            AddToClassList("meter");
            pickingMode = PickingMode.Ignore;
            _fill = new VisualElement { pickingMode = PickingMode.Ignore };
            _fill.AddToClassList("meter__fill");
            Add(_fill);
        }

        public Meter(long value, long of) : this() => Set(value, of);

        public void Set(long value, long of)
        {
            var ratio = of <= 0 ? 0f : Mathf.Clamp01((float)value / of);
            _fill.style.width = Length.Percent(ratio * 100f);
            EnableInClassList("meter--full", ratio >= 1f);
        }
    }
}
