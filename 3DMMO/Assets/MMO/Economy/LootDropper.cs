using Mirror;
using UnityEngine;
using MMO.Inventory;

namespace MMO.Economy
{
    public class LootDropper : NetworkBehaviour
    {
        public LootTable table;

        [Server]
        public void ServerDropTo(PlayerInventory targetInv, PlayerWallet targetWallet)
        {
            if (!isServer || !table) return;

            // Items
            var items = table.RollItems();
            foreach (var (def, amt) in items)
                targetInv?.ServerAdd(def, amt);

            // Currencies
            var currs = table.RollCurrencies();
            foreach (var (cdef, amt) in currs)
                targetWallet?.Give(cdef.currencyId, amt);
        }
    }
}
