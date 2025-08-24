using System;
using System.Reflection;
using UnityEngine;
using MMO.Shared.Item;      // ItemDef, ItemRarity

namespace MMO.Inventory.UI
{
    /// <summary>
    /// Single source of truth for item tooltip payloads and chat link formatting.
    /// Populates rarity label + color so the tooltip's dedicated rarity field is accurate.
    /// </summary>
    public static class ItemTooltipComposer
    {
        // ---------------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------------

        /// Build tooltip payload from a concrete ItemDef (preferred).
        public static ItemTooltipCursor.Payload Build(ItemDef def)
        {
            if (!def)
                return new ItemTooltipCursor.Payload { Title = "Item", Body = "", RarityName = "", RarityColor = Color.white };

            string name = string.IsNullOrWhiteSpace(def.displayName) ? def.name : def.displayName;
            name = DeHyphenate(name);

            string rarityHex = HexFor(def.rarity);
            Color rarityColor = ColorFromHex(rarityHex, Color.white);

            return new ItemTooltipCursor.Payload
            {
                Icon = def.icon,
                Title = WrapColored(name, rarityHex, underline: false), // colored item name (rich text)
                Body = def.description ?? TryInvokeString(def, "GetTooltip", "BuildTooltip", "GetDescription", "ToTooltip") ?? string.Empty,
                RarityName = ReadableRarity(def.rarity),               // plain label e.g. "Legendary"
                RarityColor = rarityColor
            };
        }

        /// Build tooltip payload by itemId, using optional lookup or Resources/Items.
        public static ItemTooltipCursor.Payload Build(string itemId, UnityEngine.Object optionalLookup = null, string resourcesFolder = "Items")
        {
            var def = Resolve(itemId, optionalLookup, resourcesFolder);
            return Build(def);
        }

        /// Format a colored (by rarity), optionally underlined item link for TMP chat.
        /// Default underline = false.
        public static string FormatChatLink(ItemDef def, bool underline = false)
        {
            if (!def)
            {
                var inner = underline ? "<u><color=#9DA3A6>Item</color></u>" : "<color=#9DA3A6>Item</color>";
                return $@"<link=""item:"">{inner}</link>";
            }

            string id = SafeId(def);
            string name = DeHyphenate(string.IsNullOrWhiteSpace(def.displayName) ? def.name : def.displayName);
            string hex = HexFor(def.rarity);
            string wrapped = WrapColored(name, hex, underline);
            return $@"<link=""item:{id}"">{wrapped}</link>";
        }

        /// Same as above when you only have primitives. Default underline = false.
        public static string FormatChatLink(string itemId, string displayName, ItemRarity rarity, bool underline = false)
        {
            string name = DeHyphenate(string.IsNullOrWhiteSpace(displayName) ? itemId : displayName);
            string hex = HexFor(rarity);
            string wrapped = WrapColored(name, hex, underline);
            return $@"<link=""item:{itemId}"">{wrapped}</link>";
        }

        // ---------------------------------------------------------------------
        // Resolution helpers
        // ---------------------------------------------------------------------

        public static ItemDef Resolve(string itemId, UnityEngine.Object optionalLookup, string folder)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return null;

            // 1) Optional lookup component via reflection (TryGetById / GetByIdOrNull).
            if (optionalLookup)
            {
                var t = optionalLookup.GetType();

                var mTry = t.GetMethod("TryGetById", new[] { typeof(string), typeof(ItemDef).MakeByRefType() });
                if (mTry != null)
                {
                    object[] args = new object[] { itemId, null };
                    bool ok = (bool)mTry.Invoke(optionalLookup, args);
                    if (ok) return (ItemDef)args[1];
                }

                var mGet = t.GetMethod("GetByIdOrNull", new[] { typeof(string) });
                if (mGet != null)
                {
                    var res = mGet.Invoke(optionalLookup, new object[] { itemId }) as ItemDef;
                    if (res) return res;
                }
            }

            // 2) Resources/{folder}
            var direct = Resources.Load<ItemDef>($"{folder}/{itemId}");
            if (direct) return direct;

            // 3) Fallback: scan folder for matching itemId (case-insensitive)
            var all = Resources.LoadAll<ItemDef>(folder);
            foreach (var d in all)
            {
                if (!d) continue;
                if (string.Equals(d.itemId, itemId, StringComparison.OrdinalIgnoreCase))
                    return d;
            }

            return null;
        }

        // ---------------------------------------------------------------------
        // Internals (rarity mapping + helpers)
        // ---------------------------------------------------------------------

        static string SafeId(ItemDef def)
            => !string.IsNullOrWhiteSpace(def.itemId) ? def.itemId : def.name;

        static string DeHyphenate(string s) => string.IsNullOrEmpty(s) ? s : s.Replace("-", " ");

        static string TryInvokeString(object obj, params string[] methodNames)
        {
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = obj.GetType();
            foreach (var n in methodNames)
            {
                var m = t.GetMethod(n, BF, null, Type.EmptyTypes, null);
                if (m != null && m.ReturnType == typeof(string))
                {
                    try
                    {
                        var v = m.Invoke(obj, null) as string;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                    catch { /* ignore */ }
                }
            }
            return null;
        }

        static string WrapColored(string text, string hex, bool underline)
        {
            if (string.IsNullOrEmpty(hex)) hex = "#FFFFFF";
            return underline
                ? $"<color={hex}>{text}</color>"
                : $"<color={hex}>{text}</color>";
        }

        static Color ColorFromHex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
        }

        static string ReadableRarity(ItemRarity r)
        {
            // Customize labels here if you want different casing/spacing
            return r.ToString();
        }

        // Your provided palette:
        // Common #9DA3A6, Uncommon #1EFF00, Rare #0070DD, Heroic #A335EE, Divine #FF8000,
        // Epic #FFD700, Legendary #FF4040, Mythic #CD7F32, Ancient #00E5FF, Artifact #E6CC80
        static string HexFor(ItemRarity r) => r switch
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
            _ => "#FFFFFF"
        };
    }
}
