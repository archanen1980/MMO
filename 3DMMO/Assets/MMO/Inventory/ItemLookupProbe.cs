// Assets/Hughes_Jeremiah_Assets/MMO/Inventory/Debug/ItemLookupProbe.cs
using System.Linq;
using UnityEngine;
using MMO.Shared.Item; // <-- your ItemDef type
using MMO.Inventory; // for IItemLookup 

public class ItemLookupProbe : MonoBehaviour
{
    public int[] testIds = { 1, 2, 3 };

    void Start()
    {
        var lookup = ResourcesItemLookup.Instance;
        if (!lookup) { Debug.LogError("No ResourcesItemLookup in scene."); return; }

        foreach (var id in testIds)
        {
            var def = lookup.GetById(id);
            Debug.Log($"[Probe] Lookup.GetById({id}) -> {(def ? def.displayName : "NULL")}");
        }

        var all = Resources.LoadAll<ItemDef>(string.Empty); // search ALL Resources
        Debug.Log($"[Probe] Resources has {all.Length} ItemDefs: " +
                  string.Join(", ", all.Select(a => $"{a.itemId}:{a.name}")));
    }
}
