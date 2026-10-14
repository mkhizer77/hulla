namespace Hulla.Presentation.Text
{
    /// <summary>Catalog item, category and rarity names from the string tables (GDD 12.11, 26.1).</summary>
    public static class ItemText
    {
        private static readonly string[] Prefixes = { "back_", "face_", "table_", "emotes_", "vo_", "celebration_", "sounds_" };

        /// <summary>item.&lt;id&gt;, or the id as words for an item the tables don't name yet: "back_truck_art" → "Truck art".</summary>
        public static string Name(string id)
        {
            if (Strings.Has("item." + id))
            {
                return Strings.Text("item." + id);
            }

            var name = id;
            foreach (var prefix in Prefixes)
            {
                if (name.StartsWith(prefix))
                {
                    name = name.Substring(prefix.Length);
                    break;
                }
            }

            return name.Length == 0 ? id : UiText.Words(name);
        }

        public static string Category(string category) =>
            Strings.Has("item.category." + category) ? Strings.Text("item.category." + category) : category;

        public static string Rarity(string rarity) =>
            Strings.Has("item.rarity." + rarity) ? Strings.Text("item.rarity." + rarity) : Strings.Text("item.rarity.common");
    }
}
