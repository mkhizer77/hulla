using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Theme
{
    /// <summary>
    /// The cosmetics' own art (GDD 12.11): an item with a picture puts the classes <c>cosmetic</c> and
    /// <c>cosmetic--{id}</c> on the element that shows it, and Basant.uss ("Cosmetics") gives that
    /// class the picture. The art ships in the binary until the content CDN streams the cosmetics
    /// groups (#56). Card faces are drawn, not pictured: their style is a class on an ancestor.
    /// </summary>
    public static class CosmeticArt
    {
        public const string Class = "cosmetic";
        public const string Prefix = "cosmetic--";
        public const string LargeIndexClass = "face--large-index";
        public const string DesiCourtClass = "face--desi";

        /// <summary>Every catalog item with its own picture (content/catalog/v1.json).</summary>
        private static readonly HashSet<string> PicturedSet = new HashSet<string>
        {
            "back_classic_blue", "back_classic_red", "back_ajrak", "back_chunri", "back_truck_art", "back_phulkari",
            "back_kites", "back_jaali", "back_minar", "back_mughal", "back_monsoon", "back_diwali",
            "back_ranked_gold", "back_ranked_platinum", "back_ranked_diamond", "back_ranked_ustad",
            "table_charpai", "table_dastarkhwan", "table_truck_art", "table_marble", "table_lahore_rooftop",
            "table_karachi_beach_hut", "table_dhaka_rickshaw", "table_basant_night",
            "emotes_launch", "emotes_cricket", "emotes_shaadi", "vo_urdu_classic", "vo_punjabi_banter",
            "celebration_thappad", "celebration_dhol", "celebration_patakha", "sounds_classic", "sounds_dhaba",
        };

        public static IReadOnlyCollection<string> Pictured => PicturedSet;

        public static bool Has(string? id) => id != null && PicturedSet.Contains(id);

        /// <summary>Shows the item's picture on the element, or takes the last one off for an unknown or empty id.</summary>
        public static void Apply(VisualElement element, string? id)
        {
            var keep = Has(id) ? Prefix + id : null;
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

        /// <summary>The card face style for the cards under the element: classic, the large index or the Desi court.</summary>
        public static void ApplyFace(VisualElement element, string? id)
        {
            element.EnableInClassList(LargeIndexClass, id == "face_large_index");
            element.EnableInClassList(DesiCourtClass, id == "face_desi_court");
        }
    }
}
