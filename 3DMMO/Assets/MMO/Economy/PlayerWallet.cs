using System;
using Mirror;
using UnityEngine;

namespace MMO.Economy
{
    /// Multi-currency wallet (stats). Keys are currencyId (e.g., "coins", "honor").
    /// For coins, store total in base copper units.
    public class PlayerWallet : NetworkBehaviour
    {
        [Serializable] public class CurrencyMap : SyncDictionary<string, long> { }
        public CurrencyMap balances = new CurrencyMap();

        [Header("Optional: pre-create keys at 0 on server")]
        public CurrencyDef[] initializeWith;

        public event Action<string, long> CurrencyChanged; // (id, newValue)

        public override void OnStartClient()
        {
            balances.OnChange += OnBalancesChanged;   // Mirror’s current API
        }

        public override void OnStopClient()
        {
            balances.OnChange -= OnBalancesChanged;
        }

        void OnBalancesChanged(SyncDictionary<string, long>.Operation op, string key, long value)
        {
            CurrencyChanged?.Invoke(key, Get(key));
        }

        public override void OnStartServer()
        {
            if (initializeWith != null)
            {
                foreach (var c in initializeWith)
                {
                    if (!c || string.IsNullOrWhiteSpace(c.currencyId)) continue;
                    if (!balances.ContainsKey(c.currencyId)) balances[c.currencyId] = 0;
                }
            }
        }

        // -------- Server API --------
        [Server]
        public void Give(string id, long amount)
        {
            if (string.IsNullOrWhiteSpace(id) || amount == 0) return;
            long current = balances.TryGetValue(id, out var v) ? v : 0;
            long next = current + amount;
            if (next < 0) next = 0;
            balances[id] = next;
        }

        [Server]
        public bool TrySpend(string id, long amount)
        {
            if (string.IsNullOrWhiteSpace(id) || amount <= 0) return true;
            long current = balances.TryGetValue(id, out var v) ? v : 0;
            if (current < amount) return false;
            balances[id] = current - amount;
            return true;
        }

        // -------- Convenience (safe on client/server) --------
        public long Get(string id) { balances.TryGetValue(id, out var v); return v; }
        public bool Has(string id, long amount) => Get(id) >= amount;
    }
}
