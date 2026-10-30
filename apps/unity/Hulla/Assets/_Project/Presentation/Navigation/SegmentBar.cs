using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Navigation
{
    /// <summary>
    /// The segmented control under a tab's title (docs/design/basant-tabs.md): Missions, Achievements
    /// and Leaderboard under Trophies; Shop and Pass under Shop. One cream track, the chosen part yellow.
    /// </summary>
    public sealed class SegmentBar : VisualElement
    {
        public readonly struct Entry
        {
            public Entry(string name, string label, Action open)
            {
                Name = name;
                Label = label;
                Open = open;
            }

            public string Name { get; }

            public string Label { get; }

            public Action Open { get; }
        }

        public SegmentBar(IReadOnlyList<Entry> entries, string active)
        {
            AddToClassList("segment");
            foreach (var entry in entries)
            {
                var item = new Button(entry.Open) { name = "segment-" + entry.Name, text = entry.Label };
                item.AddToClassList("segment__item");
                item.EnableInClassList("segment__item--on", entry.Name == active);
                Add(item);
            }
        }

        /// <summary>Puts the bar into the screen's <c>segment</c> host, when the screen has one.</summary>
        public static void Mount(VisualElement root, IReadOnlyList<Entry> entries, string active)
        {
            var host = root.Q<VisualElement>("segment");
            if (host == null)
            {
                return;
            }

            host.Clear();
            host.Add(new SegmentBar(entries, active));
        }
    }
}
