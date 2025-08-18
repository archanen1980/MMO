// Assets/MMO/Economy/VendorStation.cs
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using MMO.Inventory;      // PlayerInventory
using MMO.Shared.Item;    // ItemDef

namespace MMO.Economy
{
    /// <summary>
    /// Place this on a world object and assign a VendorDef.
    /// UI should call RequestBuy(...) (which calls CmdBuy with an explicit times value).
    /// </summary>
    public class VendorStation : NetworkBehaviour
    {
        [Header("Definition")]
        public VendorDef def;

        // Runtime stock for finite items (-1 means infinite). Server-side authority.
        [SyncVar] bool _initialized;
        [SyncVar] int[] _quantities;

        public override void OnStartServer()
        {
            if (_initialized || !def) return;

            _quantities = new int[def.items.Count];
            for (int i = 0; i < def.items.Count; i++)
                _quantities[i] = def.items[i].infiniteStock ? -1 : def.items[i].startingQuantity;

            _initialized = true;
        }

        /// <summary>
        /// Client-side convenience (NOT a Command). Calls CmdBuy with a default of 1.
        /// </summary>
        public void RequestBuy(int index, ushort times = 1)
        {
            // You can add local checks here (e.g., UI cooldowns) before sending the Command.
            CmdBuy(index, times);
        }

        /// <summary>Convenience wrapper for a single purchase.</summary>
        public void RequestBuyOnce(int index) => CmdBuy(index, 1);

        /// <summary>
        /// Server-authoritative purchase. Mirror forbids optional parameters on Commands,
        /// so 'times' has no default; callers must pass it explicitly.
        /// </summary>
        [Command(requiresAuthority = false)]
        public void CmdBuy(int index, ushort times)
        {
            if (!isServer || !def) return;
            if (index < 0 || index >= def.items.Count) return;
            if (times == 0) return;

            // Resolve buyer components from the connection
            var identity = connectionToClient?.identity;
            var buyerInv = identity ? identity.GetComponent<PlayerInventory>() : null;
            var wallet = identity ? identity.GetComponent<PlayerWallet>() : null;
            if (!buyerInv || !wallet) return;

            // Range check to prevent remote purchases
            if (Vector3.Distance(buyerInv.transform.position, transform.position) > def.interactionRange)
                return;

            var entry = def.items[index];
            if (!entry.item) return;

            // Stock check (server-side)
            bool infinite = entry.infiniteStock || (_quantities != null && _quantities[index] < 0);
            int totalUnits = entry.amountPerBuy * times;
            if (!infinite && _quantities[index] < totalUnits) return;

            // Aggregate required currencies
            var required = new Dictionary<string, long>();
            foreach (var p in entry.prices)
            {
                if (p?.currency == null) continue;
                string id = p.currency.currencyId;
                if (string.IsNullOrWhiteSpace(id)) continue;

                long add = p.amount * (long)times;  // base units for that currency
                if (add <= 0) continue;

                required.TryGetValue(id, out var cur);
                required[id] = cur + add;
            }

            // Verify funds for ALL currencies first
            foreach (var kv in required)
                if (!wallet.Has(kv.Key, kv.Value))
                    return;

            // Spend (atomic enough for MVP; if you want stricter atomicity, snapshot then rollback on failure)
            foreach (var kv in required)
                wallet.TrySpend(kv.Key, kv.Value);

            // Grant items
            buyerInv.ServerAdd(entry.item, totalUnits);

            // Reduce finite stock
            if (!infinite && _quantities != null)
                _quantities[index] = Mathf.Max(0, _quantities[index] - totalUnits);
        }
    }
}
