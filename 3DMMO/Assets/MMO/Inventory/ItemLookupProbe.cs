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
#if UNITY_EDITOR
    // Only run when explicitly playing in the Editor AND throttle the log spam.
    if (!Application.isPlaying) return;

    var lookup = MMO.Inventory.ResourcesItemLookup.Instance;
    if (!lookup) { Debug.LogWarning("[Probe] No ResourcesItemLookup in scene."); return; }

    foreach (var id in testIds)
    {
        var def = lookup.GetById(id);
        Debug.Log($"[Probe] Lookup.GetById({id}) -> {(def ? def.displayName : "NULL")}");
    }

    // Avoid scanning ALL resources; if you still want a snapshot, cap it:
    var all = Resources.LoadAll<MMO.Shared.Item.ItemDef>("Items"); // scope to your folder
    int show = Mathf.Min(20, all.Length);
    Debug.Log($"[Probe] Resources/*Items* has {all.Length} ItemDefs. Showing first {show}: " +
              string.Join(", ", all.Take(show).Select(a => $"{a.itemId}:{a.name}")));
#else
        // No-op in player builds
#endif
    }
}
