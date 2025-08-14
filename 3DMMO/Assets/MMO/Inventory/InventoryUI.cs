using Mirror;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MMO.Inventory
{
    public class InventoryUI : MonoBehaviour
    {
        [Header("Scene Refs (assign in Inspector)")]
        [SerializeField] Transform backpackGrid;            // InventoryGrid (inside ScrollRect/Viewport)
        [SerializeField] Transform equipmentGrid;           // EquipmentGrid
        [SerializeField] InventorySlotView slotPrefab;      // SlotView prefab with InventorySlotView
        [SerializeField] Canvas canvas;                     // Canvas this UI is under
        [SerializeField] Image dragGhost;                   // Image used as drag ghost (Raycast Target OFF)

        const int MAX_UI_SLOTS = 400; // safety cap to avoid spawning thousands of slots

        PlayerInventory inv;

        void Start()
        {
            if (NetworkClient.active && NetworkClient.connection != null)
            {
                var player = NetworkClient.connection.identity;
                if (player) inv = player.GetComponent<PlayerInventory>();
            }

            if (!inv)
            {
                Debug.LogWarning("InventoryUI: no PlayerInventory found (not connected/spawned yet?). Open panel after local player spawns.");
                return;
            }

            if (!CheckRefs()) return;

            inv.OnClientInventoryChanged += RebuildUI;
            RebuildUI();
        }

        void OnDestroy()
        {
            if (inv != null) inv.OnClientInventoryChanged -= RebuildUI;
        }

        bool CheckRefs()
        {
            if (!backpackGrid) { Debug.LogError("InventoryUI: 'backpackGrid' not assigned."); return false; }
            if (!equipmentGrid) { Debug.LogError("InventoryUI: 'equipmentGrid' not assigned."); return false; }
            if (!slotPrefab) { Debug.LogError("InventoryUI: 'slotPrefab' not assigned."); return false; }
            if (!canvas) { Debug.LogError("InventoryUI: 'canvas' not assigned."); return false; }
            if (!dragGhost) { Debug.LogError("InventoryUI: 'dragGhost' Image not assigned."); return false; }
            return true;
        }

        void RebuildUI()
        {
            if (!inv) return;
            if (!CheckRefs()) return;

            int bpCount = Mathf.Clamp(inv.Backpack.Count, 0, MAX_UI_SLOTS);
            int eqCount = Mathf.Clamp(inv.Equipment.Count, 0, MAX_UI_SLOTS);

            BuildGrid(backpackGrid, bpCount);
            BuildGrid(equipmentGrid, eqCount);

            // Fill Backpack visuals
            for (int i = 0; i < bpCount; i++)
            {
                var child = backpackGrid.GetChild(i);
                var v = child.GetComponent<InventorySlotView>();
                if (!v)
                {
                    Debug.LogError($"InventoryUI: Child '{child.name}' missing InventorySlotView.");
                    continue;
                }
                v.Bind(inv, ContainerKind.Backpack, i, dragGhost, canvas);
            }

            // Fill Equipment visuals
            for (int i = 0; i < eqCount; i++)
            {
                var child = equipmentGrid.GetChild(i);
                var v = child.GetComponent<InventorySlotView>();
                if (!v)
                {
                    Debug.LogError($"InventoryUI: Child '{child.name}' missing InventorySlotView.");
                    continue;
                }
                v.Bind(inv, ContainerKind.Equipment, i, dragGhost, canvas);
            }
        }

        void BuildGrid(Transform parent, int count)
        {
            if (!parent || !slotPrefab) return;

            // Grow
            for (int i = parent.childCount; i < count; i++)
            {
                var go = Instantiate(slotPrefab, parent);
                go.name = $"Slot_{i:D2}";
            }
            // Shrink
            for (int i = parent.childCount - 1; i >= count; i--)
                Destroy(parent.GetChild(i).gameObject);
        }
    }
}
