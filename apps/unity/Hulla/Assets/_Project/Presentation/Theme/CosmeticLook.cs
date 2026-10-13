using UnityEngine;

namespace Hulla.Presentation.Theme
{
    /// <summary>
    /// Stand-in looks for catalog items until their art lands (#66 to #68): a colour per table
    /// surface and card back, so buying and equipping already shows at the table.
    /// </summary>
    public static class CosmeticLook
    {
        public static Color? TableColour(string? id) => id switch
        {
            "table_charpai" => Hex(0x1F2A2E),
            "table_dastarkhwan" => Hex(0x5B2333),
            "table_truck_art" => Hex(0x0E5E6F),
            "table_marble" => Hex(0x55606A),
            "table_lahore_rooftop" => Hex(0x3B2F5C),
            "table_karachi_beach_hut" => Hex(0x1D5C7A),
            "table_dhaka_rickshaw" => Hex(0x2E5E2E),
            "table_basant_night" => Hex(0x2A1B4A),
            _ => null,
        };

        public static Color Swatch(string id, string category)
        {
            if (category == "table" && TableColour(id) is Color table)
            {
                return table;
            }

            return id switch
            {
                "back_classic_blue" => Hex(0x1E4E9C),
                "back_classic_red" => Hex(0xB3261E),
                "back_ajrak" => Hex(0x7A1F1F),
                "back_chunri" => Hex(0xC2185B),
                "back_truck_art" => Hex(0xF4A300),
                "back_phulkari" => Hex(0xE65100),
                "back_kites" => Hex(0x00897B),
                "back_jaali" => Hex(0x8D6E63),
                "back_minar" => Hex(0x546E7A),
                "back_mughal" => Hex(0x4A148C),
                "back_monsoon" => Hex(0x1565C0),
                "back_diwali" => Hex(0xFF8F00),
                _ => Fallback(id),
            };
        }

        private static Color Fallback(string id)
        {
            var hash = 17;
            foreach (var c in id)
            {
                hash = (hash * 31) + c;
            }

            return Color.HSVToRGB((hash & 0xFFFF) / 65535f, 0.45f, 0.6f);
        }

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
