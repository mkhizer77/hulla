using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Theme
{
    /// <summary>
    /// The emote stickers (GDD 11.3): an element showing an emote carries <c>emote-art</c> and
    /// <c>emote-art--{id}</c>, and Basant.uss ("Emotes") gives that class the picture. An emote from a pack
    /// is sent only by the pack's owners; everyone sees it.
    /// </summary>
    public static class EmoteArt
    {
        public const string Class = "emote-art";
        public const string Prefix = "emote-art--";

        /// <summary>The starter pack everyone owns; the client's guess until the shop catalog has loaded.</summary>
        public const string FreePack = "emotes_launch";

        /// <summary>A picture of the emote, for a picker tile, a bubble or a chat line.</summary>
        public static VisualElement Picture(string id)
        {
            var picture = new VisualElement { pickingMode = PickingMode.Ignore };
            Apply(picture, id);
            return picture;
        }

        /// <summary>Shows the emote's picture on the element, or takes it off for null.</summary>
        public static void Apply(VisualElement element, string? id)
        {
            var keep = string.IsNullOrEmpty(id) ? null : Prefix + id;
            foreach (var name in new List<string>(element.GetClasses()))
            {
                if (name.StartsWith(Prefix) && name != keep)
                {
                    element.RemoveFromClassList(name);
                }
            }

            element.EnableInClassList(Class, keep != null);
            if (keep != null)
            {
                element.AddToClassList(keep);
            }
        }

        /// <summary>
        /// A picker tile: the sticker, its name for screen readers, and a lock while its pack is not owned.
        /// A locked tile still answers a tap, so the picker can say where to get the pack.
        /// </summary>
        public static Button Tile(string id, string name, bool locked, System.Action onTap)
        {
            var tile = new Button(onTap) { text = "", tooltip = name, name = "emote-" + id };
            tile.AddToClassList("emote-tile");
            tile.Add(Picture(id));
            var lockIcon = new VisualElement { pickingMode = PickingMode.Ignore };
            lockIcon.AddToClassList("emote-tile__lock");
            tile.Add(lockIcon);
            SetLocked(tile, locked);
            return tile;
        }

        public static void SetLocked(VisualElement tile, bool locked) => tile.EnableInClassList("emote-tile--locked", locked);

        /// <summary>A line at the top of the emote grid saying which shop pack a locked emote comes from.</summary>
        public static void ShowLockNote(VisualElement grid, string text)
        {
            var note = grid.Q<Label>("emote-lock-note");
            if (note == null)
            {
                note = new Label { name = "emote-lock-note", pickingMode = PickingMode.Ignore };
                note.AddToClassList("emote-lock-note");
                grid.Insert(0, note);
            }

            note.text = text;
        }
    }
}
