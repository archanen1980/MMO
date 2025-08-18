using System;
using System.Collections.Generic;
using UnityEngine;
using MMO.Shared.Item;
using System.Linq;

namespace MMO.Economy
{
    [CreateAssetMenu(menuName = "MMO/Vendor Definition")]
    public class VendorDef : ScriptableObject
    {
        [Serializable]
        public class Price
        {
            public CurrencyDef currency;
            [Min(0)] public long amount;     // base units of that currency

            [Header("If currency is denominated (coins), optional convenience authoring:")]
            public bool useDenoms = false;
            [Min(0)] public int copper, silver, gold, platinum; // assumes 4-coin ladder

            public void RecalcFromDenoms()
            {
                if (!currency || !currency.isDenominated || !useDenoms) return;

                // Collect in ascending order (matching your denominations list)
                var units = new List<long>();
                for (int i = 0; i < currency.denominations.Count; i++)
                {
                    var name = currency.denominations[i].name.ToLowerInvariant();
                    long v = 0;
                    if (name.Contains("copper")) v = copper;
                    else if (name.Contains("silver")) v = silver;
                    else if (name.Contains("gold")) v = gold;
                    else if (name.Contains("platinum")) v = platinum;
                    units.Add(v);
                }
                amount = CurrencyMath.FromDenoms(currency, units);
            }
        }

        [Serializable]
        public class Stock
        {
            public ItemDef item;
            [Min(1)] public ushort amountPerBuy = 1;
            public bool infiniteStock = true;
            [Min(0)] public int startingQuantity = 0;
            [Tooltip("E.g., 2g 50s + 3 honor")]
            public List<Price> prices = new();
        }

        public List<Stock> items = new();
        public float interactionRange = 4f;

#if UNITY_EDITOR
        void OnValidate()
        {
            foreach (var s in items)
                if (s != null)
                    foreach (var p in s.prices)
                        p?.RecalcFromDenoms();
        }
#endif
    }
}
