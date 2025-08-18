using System.Collections.Generic;
using UnityEngine;

namespace MMO.Economy
{
    [CreateAssetMenu(menuName = "MMO/Currency Registry")]
    public class CurrencyRegistry : ScriptableObject
    {
        public List<CurrencyDef> currencies = new();

        static CurrencyRegistry _cached;
        public static CurrencyRegistry Instance
        {
            get
            {
                if (!_cached) _cached = Resources.Load<CurrencyRegistry>("CurrencyRegistry");
                return _cached;
            }
        }

        public CurrencyDef Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            foreach (var c in currencies)
                if (c && c.currencyId == id) return c;
            return null;
        }
    }
}
