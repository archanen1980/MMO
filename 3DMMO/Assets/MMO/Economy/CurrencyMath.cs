using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MMO.Economy
{
    public static class CurrencyMath
    {
        public static long FromDenoms(CurrencyDef def, IList<long> units)
        {
            if (!def || !def.isDenominated || def.denominations.Count == 0)
                return (units != null && units.Count > 0) ? units[0] : 0;

            long total = 0;
            int n = Mathf.Min(units.Count, def.denominations.Count);
            for (int i = 0; i < n; i++)
                total += units[i] * def.denominations[i].unitValue;
            return total;
        }

        // Returns ascending order (0 = smallest unit)
        public static List<long> ToDenoms(CurrencyDef def, long baseAmount)
        {
            var outUnits = new List<long>();
            if (!def || !def.isDenominated || def.denominations.Count == 0)
            {
                outUnits.Add(baseAmount);
                return outUnits;
            }

            // Compute largest → smallest, then insert at front to return ascending
            for (int i = def.denominations.Count - 1; i >= 0; i--)
            {
                var d = def.denominations[i];
                long q = baseAmount / d.unitValue;
                baseAmount -= q * d.unitValue;
                outUnits.Insert(0, q);
            }
            return outUnits;
        }

        public static string Format(CurrencyDef def, long baseAmount)
        {
            if (!def) return baseAmount.ToString();

            if (!def.isDenominated || def.denominations.Count == 0)
                return string.IsNullOrEmpty(def.shortCode) ? baseAmount.ToString() : $"{baseAmount} {def.shortCode}";

            var sb = new StringBuilder();
            long rem = baseAmount;
            for (int i = def.denominations.Count - 1; i >= 0; i--)
            {
                var d = def.denominations[i];
                long q = rem / d.unitValue;
                rem -= q * d.unitValue;
                if (q > 0 || (i == 0 && sb.Length == 0))
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(q).Append(d.symbol);
                }
            }
            return sb.ToString();
        }
    }
}
