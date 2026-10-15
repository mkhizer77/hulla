using System;
using System.Collections.Generic;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Inventory
{
    /// <summary>
    /// The Inventory tab (docs/design/home-concept.md): one board per place that holds what the
    /// player owns or is earning: the shop and their collection, the season pass, missions,
    /// achievements and the leaderboard. A board with something waiting carries a count.
    /// </summary>
    public sealed class InventoryView : IDisposable
    {
        /// <summary>One board: its label, what a tap opens and how many things wait behind it.</summary>
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

        public InventoryView(VisualElement root, IReadOnlyList<Entry> entries)
        {
            root.Q<Label>("title").text = UiText.Inventory;
            var list = root.Q<VisualElement>("list");
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var button = new Button(entry.Open) { name = "inventory-" + entry.Name, text = entry.Label };
                button.AddToClassList("button");
                button.AddToClassList("button--secondary");
                button.AddToClassList("plank");
                button.AddToClassList(i % 2 == 0 ? "plank--red" : "plank--blue");
                button.AddToClassList("inventory__entry");
                if (entry.Waiting > 0)
                {
                    var badge = new Label(Navigation.NavBar.BadgeText(entry.Waiting));
                    badge.AddToClassList("nav__badge");
                    badge.AddToClassList("inventory__badge");
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
