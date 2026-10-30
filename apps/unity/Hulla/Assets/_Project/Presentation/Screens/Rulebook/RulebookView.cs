using System;
using System.Collections.Generic;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Rulebook
{
    /// <summary>A game's rulebook over the table (GDD 14.11); it never pauses play and can open at a section.</summary>
    public sealed class RulebookView
    {
        private readonly ScrollView _scroll;
        private readonly Dictionary<string, VisualElement> _sections = new Dictionary<string, VisualElement>();

        public RulebookView(VisualElement root, string title, IReadOnlyList<RulebookSection> sections, Action close, string? openAt = null)
        {
            root.Q<Label>("title").text = title;
            var closeButton = root.Q<Button>("close");
            closeButton.text = UiText.Done;
            closeButton.clicked += close;
            _scroll = root.Q<ScrollView>("scroll");
            var list = root.Q<VisualElement>("list");
            // The contents as a chip row (design: Rulebook): a tap scrolls to the section.
            var contents = root.Q<VisualElement>("contents");
            foreach (var section in sections)
            {
                var box = new VisualElement();
                box.AddToClassList("rulebook__section");
                var heading = new Label(section.Title);
                heading.AddToClassList("turn");
                box.Add(heading);
                foreach (var line in section.Lines)
                {
                    var text = new Label(line);
                    text.AddToClassList("rulebook__line");
                    box.Add(text);
                }

                list.Add(box);
                _sections[section.Id] = box;
                if (contents != null)
                {
                    var chip = new Button(() => _scroll.ScrollTo(box)) { text = section.Title };
                    chip.AddToClassList("chip");
                    chip.AddToClassList("rulebook__chip");
                    contents.Add(chip);
                }
            }

            if (openAt != null && _sections.TryGetValue(openAt, out var target))
            {
                target.RegisterCallback<GeometryChangedEvent>(Jump);
            }

            void Jump(GeometryChangedEvent evt)
            {
                ((VisualElement)evt.target).UnregisterCallback<GeometryChangedEvent>(Jump);
                _scroll.ScrollTo((VisualElement)evt.target);
            }
        }
    }
}
