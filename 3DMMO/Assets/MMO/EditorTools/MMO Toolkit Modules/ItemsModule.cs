#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MMO.Shared.Item;

namespace MMO.EditorTools
{
    [ToolkitModule("items", "Items", order: 10, icon: "d_Project")]
    public class ItemsModule : MMOToolkitModuleBase
    {
        ItemDef[] _items = Array.Empty<ItemDef>();      // raw, unsorted
        ItemDef[] _view = Array.Empty<ItemDef>();       // filtered + sorted

        int _index = -1;                                // index within _view
        ItemDef _selected;

        Vector2 _left, _right;

        // --- New: sorting + search ---
        enum SortMode { Name, ItemIdNumeric, ItemIdLex, Kind, EquipmentFirst }
        SortMode _sortMode = SortMode.Name;
        bool _sortAsc = true;
        string _search = "";

        public override void OnEnable() { Refresh(); }
        public override void OnDisable() { }

        public override void OnGUI()
        {
            EditorGUILayout.LabelField("Items (Resources/Items)", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            // --- Controls row: sort + asc/desc + search ---
            using (new EditorGUILayout.HorizontalScope("box"))
            {
                _sortMode = (SortMode)EditorGUILayout.EnumPopup(new GUIContent("Sort"), _sortMode, GUILayout.MaxWidth(260));
                _sortAsc = GUILayout.Toggle(_sortAsc, _sortAsc ? "Asc" : "Desc", "Button", GUILayout.Width(60));

                GUILayout.Space(8);
                GUILayout.FlexibleSpace();

                // simple search box
                var newSearch = EditorGUILayout.TextField("Search", _search, GUILayout.MaxWidth(320));
                if (newSearch != _search)
                {
                    _search = newSearch;
                    RebuildView(); // live filter
                }
                if (!string.IsNullOrEmpty(_search) && GUILayout.Button("×", GUILayout.Width(24)))
                {
                    _search = "";
                    RebuildView();
                }
            }

            EditorGUILayout.BeginHorizontal();

            // Left: list
            EditorGUILayout.BeginVertical(GUILayout.Width(320));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("New Item…", GUILayout.Height(24)))
                    ShowNewItemMenu();
                if (GUILayout.Button("Refresh", GUILayout.Height(24), GUILayout.Width(80)))
                    Refresh();
            }

            using (var sv = new EditorGUILayout.ScrollViewScope(_left))
            {
                _left = sv.scrollPosition;

                for (int i = 0; i < _view.Length; i++)
                {
                    var it = _view[i];
                    if (!it) continue;

                    string name = string.IsNullOrWhiteSpace(it.displayName) ? it.name : it.displayName;
                    string label = $"{name}  [{it.itemId}]  • {it.kind}";
                    using (new EditorGUILayout.HorizontalScope("box"))
                    {
                        if (GUILayout.Toggle(_index == i, label, "Button"))
                        {
                            _index = i;
                            _selected = it;
                        }
                    }
                }
            }

            if (_selected)
            {
                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Duplicate", GUILayout.Height(22))) Duplicate(_selected);
                    if (GUILayout.Button("Reveal", GUILayout.Height(22))) EditorGUIUtility.PingObject(_selected);
                }
                if (GUILayout.Button("Delete", GUILayout.Height(22)))
                {
                    if (EditorUtility.DisplayDialog("Delete Item",
                        $"Delete '{_selected.displayName}'?", "Delete", "Cancel"))
                    {
                        AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(_selected));
                        _selected = null; _index = -1;
                        Refresh();
                    }
                }
            }

            EditorGUILayout.EndVertical();

            // Right: inspector
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Item Inspector", EditorStyles.boldLabel);

            _right = EditorGUILayout.BeginScrollView(_right);
            if (_selected == null)
            {
                EditorGUILayout.HelpBox("Select an item on the left, or click 'New Item…' to create one.", MessageType.Info);
            }
            else
            {
                EditorGUI.BeginChangeCheck();

                _selected.displayName = EditorGUILayout.TextField("Display Name", _selected.displayName);

                using (new EditorGUILayout.HorizontalScope())
                {
                    _selected.itemId = EditorGUILayout.TextField("Item ID", _selected.itemId);
                    if (GUILayout.Button("Auto", GUILayout.Width(60)))
                    {
                        _selected.itemId = NextAvailableNumericItemId();
                        EditorUtility.SetDirty(_selected);
                        RebuildView(); // resort if sorting by id
                    }
                }

                _selected.maxStack = Mathf.Max(1, EditorGUILayout.IntField("Max Stack", _selected.maxStack));
                _selected.icon = (Sprite)EditorGUILayout.ObjectField("Icon", _selected.icon, typeof(Sprite), false);

                EditorGUILayout.LabelField("Description");
                _selected.description = EditorGUILayout.TextArea(_selected.description, GUILayout.MinHeight(60));

                _selected.weight = EditorGUILayout.FloatField("Weight", _selected.weight);

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Classification", EditorStyles.boldLabel);

                _selected.kind = (ItemKind)EditorGUILayout.EnumPopup("Item Type", _selected.kind);

                using (new EditorGUI.DisabledScope(false))
                {
                    _selected.isCraftable = EditorGUILayout.Toggle("Craftable", _selected.isCraftable);
                }

                using (new EditorGUI.DisabledScope(_selected.kind != ItemKind.Equipment))
                {
                    var newMask = (EquipSlot)EditorGUILayout.EnumFlagsField("Equip Slots", _selected.equipSlots);
                    if (_selected.kind == ItemKind.Equipment) _selected.equipSlots = newMask;
                    else _selected.equipSlots = EquipSlot.None;
                }

                // Validation
                string idErr = ValidateUniqueId(_selected);
                if (!string.IsNullOrEmpty(idErr))
                    EditorGUILayout.HelpBox(idErr, MessageType.Error);

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(_selected);
                    RebuildView(); // keep list in-sync when fields change that affect sort/search
                }

                GUILayout.Space(6);
                if (GUILayout.Button("Save", GUILayout.Height(26)))
                {
                    AssetDatabase.SaveAssets();
                    Refresh();
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical(); // inspector
            EditorGUILayout.EndHorizontal();
        }

        // ---------- Create / Duplicate ----------
        void ShowNewItemMenu()
        {
            var menu = new GenericMenu();
            foreach (ItemKind k in Enum.GetValues(typeof(ItemKind)))
                menu.AddItem(new GUIContent(k.ToString()), false, () => CreateNewItem(k));
            menu.ShowAsContext();
        }

        void CreateNewItem(ItemKind kind)
        {
            ModuleUtil.EnsureFolder(ModuleUtil.ItemsFolder);

            var asset = ScriptableObject.CreateInstance<ItemDef>();
            asset.displayName = "New " + kind;
            asset.itemId = MakeUniqueId(ModuleUtil.Slugify(asset.displayName));
            asset.kind = kind;

            // sensible defaults per kind
            asset.maxStack = (kind == ItemKind.Equipment || kind == ItemKind.Tool) ? 1 : 99;
            asset.isCraftable = kind switch
            {
                ItemKind.Quest => false,
                ItemKind.Key => false,
                _ => true
            };
            asset.equipSlots = (kind == ItemKind.Equipment) ? EquipSlot.MainHand : EquipSlot.None;

            string path = AssetDatabase.GenerateUniqueAssetPath($"{ModuleUtil.ItemsFolder}/{asset.itemId}.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Refresh();

            _selected = asset;
            _index = Array.IndexOf(_view, asset);
            EditorGUIUtility.PingObject(asset);
        }

        void Duplicate(ItemDef src)
        {
            if (!src) return;
            ModuleUtil.EnsureFolder(ModuleUtil.ItemsFolder);
            var clone = UnityEngine.Object.Instantiate(src);
            clone.itemId = MakeUniqueId(src.itemId);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{ModuleUtil.ItemsFolder}/{clone.itemId}.asset");
            AssetDatabase.CreateAsset(clone, path);
            AssetDatabase.SaveAssets();
            Refresh();
            _selected = clone;
            _index = Array.IndexOf(_view, clone);
            EditorGUIUtility.PingObject(clone);
        }

        // ---------- Data / view ----------
        void Refresh()
        {
            ModuleUtil.EnsureFolder(ModuleUtil.ItemsFolder);
            string[] guids = AssetDatabase.FindAssets("t:ItemDef", new[] { ModuleUtil.ItemsFolder });
            _items = guids.Select(g => AssetDatabase.LoadAssetAtPath<ItemDef>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            RebuildView();
        }

        void RebuildView()
        {
            var q = _items.Where(a => a != null);

            // filter
            if (!string.IsNullOrWhiteSpace(_search))
            {
                string s = _search.Trim();
                q = q.Where(it =>
                {
                    string name = string.IsNullOrWhiteSpace(it.displayName) ? it.name : it.displayName;
                    return (name?.IndexOf(s, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                           || (it.itemId?.IndexOf(s, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                           || it.kind.ToString().IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
                });
            }

            // sort
            switch (_sortMode)
            {
                case SortMode.Name:
                    q = _sortAsc
                        ? q.OrderBy(NameKey, StringComparer.OrdinalIgnoreCase)
                        : q.OrderByDescending(NameKey, StringComparer.OrdinalIgnoreCase);
                    break;

                case SortMode.ItemIdNumeric:
                    q = _sortAsc
                        ? q.OrderBy(IdNumKey).ThenBy(NameKey, StringComparer.OrdinalIgnoreCase)
                        : q.OrderByDescending(IdNumKey).ThenByDescending(NameKey, StringComparer.OrdinalIgnoreCase);
                    break;

                case SortMode.ItemIdLex:
                    q = _sortAsc
                        ? q.OrderBy(it => it.itemId, StringComparer.OrdinalIgnoreCase)
                        : q.OrderByDescending(it => it.itemId, StringComparer.OrdinalIgnoreCase);
                    break;

                case SortMode.Kind:
                    q = _sortAsc
                        ? q.OrderBy(it => (int)it.kind).ThenBy(NameKey, StringComparer.OrdinalIgnoreCase)
                        : q.OrderByDescending(it => (int)it.kind).ThenByDescending(NameKey, StringComparer.OrdinalIgnoreCase);
                    break;

                case SortMode.EquipmentFirst:
                    // Equipment first, then by name
                    q = _sortAsc
                        ? q.OrderByDescending(it => it.kind == ItemKind.Equipment).ThenBy(NameKey, StringComparer.OrdinalIgnoreCase)
                        : q.OrderBy(it => it.kind == ItemKind.Equipment).ThenBy(NameKey, StringComparer.OrdinalIgnoreCase);
                    break;
            }

            _view = q.ToArray();

            // keep selection if possible
            if (_selected != null)
            {
                int newIdx = Array.IndexOf(_view, _selected);
                _index = newIdx;
                if (newIdx < 0) _selected = null;
            }
            else
            {
                _index = -1;
            }
        }

        static string NameKey(ItemDef it) => string.IsNullOrWhiteSpace(it.displayName) ? it.name : it.displayName;

        static int IdNumKey(ItemDef it)
        {
            if (int.TryParse(it.itemId?.Trim(), out int n)) return n;
            // non-numeric IDs sort after numerics
            return int.MaxValue;
        }

        string ValidateUniqueId(ItemDef target)
        {
            if (!target) return null;
            string id = (target.itemId ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(id)) return "Item ID cannot be empty.";
            foreach (var it in _items)
            {
                if (!it || it == target) continue;
                if (string.Equals(it.itemId?.Trim(), target.itemId?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return $"Duplicate itemId found in asset '{it.name}'.";
            }
            return null;
        }

        string MakeUniqueId(string baseId)
        {
            string id = (baseId ?? "item").Trim().ToLowerInvariant();
            var taken = _items.Where(a => a).Select(a => (a.itemId ?? "").Trim().ToLowerInvariant()).ToHashSet();
            if (!taken.Contains(id)) return id;
            int n = 2; while (taken.Contains(id + "_" + n)) n++;
            return id + "_" + n;
        }

        // Numeric next-id for the Auto button
        static string NextAvailableNumericItemId()
        {
            string[] guids = AssetDatabase.FindAssets("t:ItemDef", new[] { ModuleUtil.ItemsFolder });
            int max = 0;
            foreach (var g in guids)
            {
                var def = AssetDatabase.LoadAssetAtPath<ItemDef>(AssetDatabase.GUIDToAssetPath(g));
                if (def == null) continue;
                if (!string.IsNullOrWhiteSpace(def.itemId) && int.TryParse(def.itemId.Trim(), out int n))
                    if (n > max) max = n;
            }
            return (max + 1).ToString();
        }
    }
}
#endif
