using System;
using System.Collections.Generic;
using UnityEngine;

namespace MMO.Economy
{
    [CreateAssetMenu(menuName = "MMO/Currency Def")]
    public class CurrencyDef : ScriptableObject
    {
        [Tooltip("Stable key, e.g. 'coins', 'honor'")]
        public string currencyId = "coins";

        public string displayName = "Coins";
        public string shortCode = "";  // e.g. "honor" for non-denominated
        public Sprite icon;

        [Header("Denominations (use for coins)")]
        public bool isDenominated = false;

        [Serializable]
        public class Denomination
        {
            public string name = "Copper";   // display only
            public string symbol = "c";      // e.g., c, s, g, p
            [Min(1)] public long unitValue = 1; // base units (ascending; base first)
        }

        [Tooltip("Ascending by unitValue. Example: Copper=1, Silver=100, Gold=10_000, Platinum=1_000_000")]
        public List<Denomination> denominations = new();
    }
}
