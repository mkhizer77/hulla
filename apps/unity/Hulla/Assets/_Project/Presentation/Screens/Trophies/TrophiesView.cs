using System;
using System.Collections.Generic;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Trophies
{
    /// <summary>
    /// The Trophies tab (docs/design/basant-home.md): one button per place the player earns things:
    /// missions, achievements, the leaderboard and, until the Shop gets its Pass tab, the season pass.
    /// A button with something waiting carries a count.
    /// </summary>
    public sealed class TrophiesView : IDisposable
    {
        /// <summary>One entry: its label, what a tap opens and how many things wait behind it.</summary>
        public readonly struct Entry
        {
            public Entry(string name, string label, Action open, int waiting = 0)
            {
                Name = name;
                Label = label;
                Open = open;
                Waiting = waiting;
            }

            public string Name { get; }

            public string Label { get; }

            public Action Open { get; }

            public int Waiting { get; }
        }

        public TrophiesView(VisualElement root, IReadOnlyList<Entry> entries)
        {
            root.Q<Label>("title").text = UiText.Trophies;
            var list = root.Q<VisualElement>("list");
            foreach (var entry in entries)
            {
                var button = new Button(entry.Open) { name = "trophies-" + entry.Name, text = entry.Label };
                button.AddToClassList("button");
                button.AddToClassList("button--secondary");
                button.AddToClassList("trophies__entry");
                if (entry.Waiting > 0)
                {
                    var badge = new Label(Navigation.NavBar.BadgeText(entry.Waiting));
                    badge.AddToClassList("nav__badge");
                    badge.AddToClassList("trophies__badge");
                    button.Add(badge);
                }

                list.Add(button);
            }
        }

        public void Dispose()
        {
        }
    }
}
