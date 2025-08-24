using UnityEngine;

namespace MMO.Shared.Item
{
    public enum ItemRarity
    {
        Common,
        Uncommon,
        Rare,
        Heroic,
        Divine,
        Epic,
        Legendary,
        Mythic,
        Ancient,
        Artifact
    }

    public static class ItemRarityUtil
    {
        // Hex map (fallback -> Common)
        public static string Hex(ItemRarity r) => r switch
        {
            ItemRarity.Common => "#9DA3A6",
            ItemRarity.Uncommon => "#1EFF00",
            ItemRarity.Rare => "#0070DD",
            ItemRarity.Heroic => "#A335EE",
            ItemRarity.Divine => "#FF8000",
            ItemRarity.Epic => "#FFD700",
            ItemRarity.Legendary => "#FF4040",
            ItemRarity.Mythic => "#CD7F32",
            ItemRarity.Ancient => "#00E5FF",
            ItemRarity.Artifact => "#E6CC80",
            _ => "#9DA3A6"
        };

        public static Color32 Color(ItemRarity r)
        {
            ColorUtility.TryParseHtmlString(Hex(r), out var c);
            return c;
        }

        /// Returns a non-hyphenated, colored (and optional underlined) name for display.
        public static string WrapName(string rawName, ItemRarity rarity, bool underline = false)
        {
            string safe = string.IsNullOrWhiteSpace(rawName) ? "Item" : rawName.Replace("-", " ");
            string hex = Hex(rarity);
            return underline
                ? $"<color={hex}><u>{safe}</u></color>"
                : $"<color={hex}>{safe}</color>";
        }
    }
}
