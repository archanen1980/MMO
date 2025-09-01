using UnityEngine;

namespace MMO.Loot
{
    /// <summary>Minimal contract for something the player can loot.</summary>
    public interface ILootable
    {
        /// <summary>Is this source currently lootable?</summary>
        bool IsAvailable { get; }

        /// <summary>Server-authoritative attempt to loot. Returns true if anything was granted.</summary>
        bool ServerTryLoot(GameObject looter);

        /// <summary>World transform of this lootable (used by UI/FX/targeting).</summary>
        Transform GetTransform();
    }
}
