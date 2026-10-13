namespace Hulla.Presentation.Text
{
    /// <summary>English names for catalog items and categories until the string tables land (#65).</summary>
    public static class ItemText
    {
        private static readonly string[] Prefixes = { "back_", "face_", "table_", "emotes_", "vo_", "celebration_", "sounds_" };

        /// <summary>"back_truck_art" → "Truck art".</summary>
        public static string Name(string id)
        {
            var name = id;
            foreach (var prefix in Prefixes)
            {
                if (name.StartsWith(prefix))
                {
                    name = name.Substring(prefix.Length);
                    break;
                }
            }

            name = name.Replace('_', ' ');
            return name.Length == 0 ? id : char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        public static string Category(string category) => category switch
        {
            "cardBack" => "Card backs",
            "cardFace" => "Card faces",
            "table" => "Tables",
            "emotePack" => "Emotes",
            "celebration" => "Celebrations",
            "soundSet" => "Sounds",
            "voPack" => "Voices",
            _ => category,
        };

        public static string Rarity(string rarity) => rarity switch
        {
            "rare" => "Rare",
            "epic" => "Epic",
            "legendary" => "Legendary",
            _ => "Common",
        };
    }
}
