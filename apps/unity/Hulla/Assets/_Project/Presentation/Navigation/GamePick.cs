using System;
using Hulla.Application.Progress;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Navigation
{
    /// <summary>
    /// The All, Thulla, Rung filter under the Trophies segments (docs/design/two-games.md). One choice
    /// for missions, achievements and the leaderboard, kept while the app runs.
    /// </summary>
    public static class GamePick
    {
        /// <summary>Off until the second game is live: no bar, and everything shows.</summary>
        public static bool Enabled { get; set; }

        public static GameFilter Chosen { get; private set; } = GameFilter.All;

        public static void Reset() => Chosen = GameFilter.All;

        /// <summary>Puts the bar under the screen's segment host; <paramref name="changed"/> runs after a tap.</summary>
        public static void Mount(VisualElement root, Action changed)
        {
            var host = root.Q<VisualElement>("segment");
            if (!Enabled || host?.parent == null || root.Q<VisualElement>("game-pick") != null)
            {
                return;
            }

            var bar = new VisualElement { name = "game-pick" };
            bar.AddToClassList("mini-seg");
            bar.AddToClassList("game-pick");
            Add(bar, "all", UiText.FilterAll, GameFilter.All, changed);
            Add(bar, "thulla", UiText.GameThulla, GameFilter.Thulla, changed);
            Add(bar, "rung", UiText.GameRung, GameFilter.Rung, changed);
            host.parent.Insert(host.parent.IndexOf(host) + 1, bar);
        }

        private static void Add(VisualElement bar, string name, string label, GameFilter game, Action changed)
        {
            var item = new Button { name = "game-" + name, text = label };
            item.AddToClassList("mini-seg__item");
            item.EnableInClassList("mini-seg__item--on", Chosen == game);
            item.clicked += () =>
            {
                if (Chosen == game)
                {
                    return;
                }

                Chosen = game;
                foreach (var child in bar.Children())
                {
                    child.EnableInClassList("mini-seg__item--on", child == item);
                }

                changed();
            };
            bar.Add(item);
        }
    }
}
