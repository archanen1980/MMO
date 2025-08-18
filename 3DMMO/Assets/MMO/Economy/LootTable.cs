using System;
using System.Collections.Generic;
using UnityEngine;
using MMO.Shared.Item;

namespace MMO.Economy
{
    [CreateAssetMenu(menuName = "MMO/Loot Table")]
    public class LootTable : ScriptableObject
    {
        [Serializable]
        public class ItemEntry
        {
            public ItemDef item;
            [Min(1)] public int min = 1;
            [Min(1)] public int max = 1;
            [Range(0, 1)] public float chance = 1f;
            [Min(0)] public float weight = 1f;
        }

        [Serializable]
        public class CurrencyEntry
        {
            public CurrencyDef currency;
            [Min(0)] public long minBase = 0;
            [Min(0)] public long maxBase = 0;

            [Header("If denominated currency (coins), optional authoring:")]
            public bool useDenoms = false;
            public int copper, silver, gold, platinum;

            public void RecalcFromDenoms()
            {
                if (currency && currency.isDenominated && useDenoms)
                {
                    var units = new List<long>();
                    for (int i = 0; i < currency.denominations.Count; i++)
                    {
                        var n = currency.denominations[i].name.ToLowerInvariant();
                        long v = n.Contains("copper") ? copper :
                                 n.Contains("silver") ? silver :
                                 n.Contains("gold") ? gold :
                                 n.Contains("platinum") ? platinum : 0;
                        units.Add(v);
                    }
                    long total = CurrencyMath.FromDenoms(currency, units);
                    minBase = total; maxBase = total;
                }
            }
        }

        public int itemRolls = 1;
        public List<ItemEntry> items = new();

        public List<CurrencyEntry> currencies = new();

#if UNITY_EDITOR
        void OnValidate()
        {
            foreach (var c in currencies) c?.RecalcFromDenoms();
        }
#endif

        public List<(ItemDef def, int amount)> RollItems(System.Random rng = null)
        {
            rng ??= new System.Random();
            var results = new List<(ItemDef, int)>();
            if (items.Count == 0) return results;

            for (int r = 0; r < Math.Max(1, itemRolls); r++)
            {
                // gate candidates by chance
                var candidates = new List<ItemEntry>();
                float totalW = 0f;
                foreach (var e in items)
                {
                    if (!e.item) continue;
                    if (e.chance >= 1f || rng.NextDouble() <= e.chance)
                    { candidates.Add(e); totalW += Mathf.Max(0.0001f, e.weight); }
                }
                if (candidates.Count == 0) continue;

                // weighted pick
                double pick = rng.NextDouble() * totalW;
                foreach (var e in candidates)
                {
                    pick -= Mathf.Max(0.0001f, e.weight);
                    if (pick <= 0)
                    {
                        int amt = UnityEngine.Random.Range(e.min, e.max + 1);
                        if (amt > 0) results.Add((e.item, amt));
                        break;
                    }
                }
            }
            return results;
        }

        public List<(CurrencyDef def, long amount)> RollCurrencies()
        {
            var results = new List<(CurrencyDef, long)>();
            foreach (var c in currencies)
            {
                if (!c?.currency) continue;
                if (c.maxBase <= 0) continue;
                long amt = UnityEngine.Random.Range((int)c.minBase, (int)c.maxBase + 1);
                if (amt > 0) results.Add((c.currency, amt));
            }
            return results;
        }
    }
}
