#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

using MMO.EditorTools; // Toolkit module base/attribute
using MMO.Loot;        // ILootable

namespace MMO.EditorTools
{
    /// <summary>
    /// MMOToolkit module that scans the current Scene for ILootable components,
    /// shows their referenced LootTable fields, and lets you edit/assign tables.
    ///
    /// This does NOT require a specific loot runtime — it discovers any ScriptableObject
    /// type named exactly "LootTable" (or *ends with* "LootTable").
    /// If none is found, the module still lists lootables and their GameObjects.
    /// </summary>
    [ToolkitModule("lootables", "Lootables", order: 15, icon: "d_Prefab Icon")]
    public class LootablesModule : MMOToolkitModuleBase
    {
        // ------------------------------- State -------------------------------
        private Vector2 _leftScroll, _rightScroll, _tablesScroll;
        private string _search = string.Empty;
        private Type _lootTableType;                     // discovered SO type
        private UnityEngine.Object[] _allProjectTables;  // cache of project assets of loot table type

        private readonly List<MonoBehaviour> _lootables = new();   // scene components implementing ILootable
        private readonly Dictionary<MonoBehaviour, List<FieldInfo>> _lootTableFields = new();
        private MonoBehaviour _selectedLootable;
        private SerializedObject _selectedLootableSO;

        // Reorderable list for multiple table fields (rare but supported)
        private ReorderableList _fieldList;

        private enum Tab { SceneLootables, Tables }
        private Tab _tab = Tab.SceneLootables;

        // ------------------------------ Lifecycle ----------------------------
        public override void OnEnable()
        {
            DiscoverLootTableType();
            RefreshSceneLootables();
            RefreshProjectTables();
        }

        public override void OnDisable()
        {
            _selectedLootableSO = null;
            _fieldList = null;
        }

        // ------------------------------- GUI ---------------------------------
        public override void OnGUI()
        {
            DrawHeaderBar();
            EditorGUILayout.Space(4);

            switch (_tab)
            {
                case Tab.SceneLootables: DrawSceneLootablesTab(); break;
                case Tab.Tables: DrawTablesTab(); break;
            }
        }

        void DrawHeaderBar()
        {
            using (new EditorGUILayout.HorizontalScope("box"))
            {
                _tab = (Tab)GUILayout.Toolbar((int)_tab, new[] { "Scene Lootables", "Tables" }, GUILayout.Width(280));
                GUILayout.Space(10);

                if (GUILayout.Button("Refresh", GUILayout.Width(90), GUILayout.Height(22)))
                {
                    DiscoverLootTableType();
                    RefreshSceneLootables();
                    RefreshProjectTables();
                }

                GUILayout.FlexibleSpace();
                string newSearch = EditorGUILayout.TextField("Search", _search, GUILayout.MaxWidth(320));
                if (newSearch != _search) { _search = newSearch; }
                if (!string.IsNullOrEmpty(_search) && GUILayout.Button("×", GUILayout.Width(24))) _search = "";
            }

            if (_lootTableType == null)
            {
                EditorGUILayout.HelpBox("No ScriptableObject type named 'LootTable' (or *LootTable) found in the project. The module will still list ILootables, but inline table editing/creation will be disabled.", MessageType.Info);
            }
        }

        // ---------------------------- Scene Lootables ------------------------
        void DrawSceneLootablesTab()
        {
            EditorGUILayout.BeginHorizontal();

            // Left: list of lootables
            EditorGUILayout.BeginVertical(GUILayout.Width(340));
            using (var sv = new EditorGUILayout.ScrollViewScope(_leftScroll))
            {
                _leftScroll = sv.scrollPosition;

                foreach (var mb in FilteredLootables())
                {
                    if (!mb) continue;
                    var go = mb.gameObject;
                    string label = go.name;

                    // status badge via ILootable.IsAvailable if property exists
                    string status = TryGetIsAvailable(mb, out bool avail) ? (avail ? "  • Available" : "  • Not available") : "";

                    using (new EditorGUILayout.HorizontalScope("box"))
                    {
                        if (GUILayout.Toggle(_selectedLootable == mb, label + status, "Button"))
                        {
                            if (_selectedLootable != mb)
                            {
                                _selectedLootable = mb;
                                _selectedLootableSO = new SerializedObject(_selectedLootable);
                                BuildFieldList();
                            }
                        }

                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("Ping", GUILayout.Width(44))) EditorGUIUtility.PingObject(go);
                        if (GUILayout.Button("Sel", GUILayout.Width(36))) Selection.activeObject = go;
                    }
                }

                if (_lootables.Count == 0)
                    EditorGUILayout.HelpBox("No ILootable components found in the current Scene.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();

            // Right: inspector/editor
            EditorGUILayout.BeginVertical();
            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

            if (_selectedLootable == null)
            {
                EditorGUILayout.HelpBox("Select a lootable on the left.", MessageType.Info);
            }
            else
            {
                DrawSelectedLootableInspector();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        void DrawSelectedLootableInspector()
        {
            var go = _selectedLootable.gameObject;
            EditorGUILayout.LabelField(go.name, EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select GameObject", GUILayout.Width(150))) Selection.activeObject = go;
                if (GUILayout.Button("Ping", GUILayout.Width(60))) EditorGUIUtility.PingObject(go);
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Loot Table References", EditorStyles.boldLabel);

            var fields = _lootTableFields.TryGetValue(_selectedLootable, out var list) ? list : null;
            if (fields == null || fields.Count == 0)
            {
                EditorGUILayout.HelpBox(_lootTableType == null
                        ? "No fields to show. LootTable type not found in project."
                        : "No fields of type '" + _lootTableType.Name + "' found on this component.",
                    MessageType.Info);
            }
            else
            {
                // Draw each field as a SerializedProperty
                _selectedLootableSO.Update();
                foreach (var fi in fields)
                {
                    var prop = _selectedLootableSO.FindProperty(fi.Name);
                    if (prop == null) continue;

                    using (new EditorGUILayout.VerticalScope("box"))
                    {
                        EditorGUILayout.PropertyField(prop, new GUIContent(fi.Name), true);

                        // If single reference, offer inline inspector & quick actions
                        if (prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue != null)
                        {
                            DrawInlineLootTableInspector(prop.objectReferenceValue);
                        }
                        else if (prop.isArray)
                        {
                            // show counts for arrays/lists
                            EditorGUILayout.LabelField("Count: " + prop.arraySize);
                        }

                        DrawQuickAssignBar(prop);
                    }
                }
                if (_selectedLootableSO.ApplyModifiedProperties()) EditorUtility.SetDirty(_selectedLootable);
            }
        }

        void DrawQuickAssignBar(SerializedProperty prop)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = _lootTableType != null;
                if (GUILayout.Button("Create New", GUILayout.Width(100))) CreateAndAssignNewTable(prop);
                if (GUILayout.Button("Assign From Project…", GUILayout.Width(170))) ShowAssignMenu(prop);
                GUI.enabled = true;
            }
        }

        void DrawInlineLootTableInspector(UnityEngine.Object lootTable)
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("LootTable Inspector", EditorStyles.boldLabel);
                var ed = Editor.CreateEditor(lootTable);
                if (ed != null)
                {
                    ed.OnInspectorGUI();
                    EditorGUILayout.Space(2);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Open", GUILayout.Width(80))) AssetDatabase.OpenAsset(lootTable);
                        if (GUILayout.Button("Ping", GUILayout.Width(80))) EditorGUIUtility.PingObject(lootTable);
                    }
                    UnityEngine.Object.DestroyImmediate(ed);
                }
                else
                {
                    EditorGUILayout.HelpBox("No custom inspector for LootTable; drawing default.", MessageType.None);
                }
            }
        }

        // ------------------------------- Tables Tab ---------------------------
        void DrawTablesTab()
        {
            if (_lootTableType == null)
            {
                EditorGUILayout.HelpBox("LootTable type not found — nothing to show here.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Scan Scene", GUILayout.Width(110))) RefreshSceneLootables();
                    if (GUILayout.Button("Scan Project", GUILayout.Width(110))) RefreshProjectTables();
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Create LootTable Asset…", GUILayout.Width(190))) CreateLootTableAsset();
                }
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Tables In Scene", EditorStyles.boldLabel);
            _tablesScroll = EditorGUILayout.BeginScrollView(_tablesScroll, GUILayout.MinHeight(140));

            var map = BuildTableToLootablesMap();
            if (map.Count == 0)
            {
                EditorGUILayout.HelpBox("No LootTable references found in the current Scene.", MessageType.Info);
            }
            else
            {
                foreach (var kvp in map)
                {
                    var table = kvp.Key;
                    var users = kvp.Value;
                    using (new EditorGUILayout.VerticalScope("box"))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label(table.name, EditorStyles.boldLabel);
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("Open", GUILayout.Width(80))) AssetDatabase.OpenAsset(table);
                            if (GUILayout.Button("Ping", GUILayout.Width(60))) EditorGUIUtility.PingObject(table);
                        }

                        foreach (var user in users)
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                GUILayout.Label("• " + user.gameObject.name);
                                GUILayout.FlexibleSpace();
                                if (GUILayout.Button("Select", GUILayout.Width(70))) Selection.activeObject = user.gameObject;
                            }
                        }
                    }
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Tables In Project (" + (_allProjectTables?.Length ?? 0) + ")", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope("box"))
            {
                if (_allProjectTables == null || _allProjectTables.Length == 0)
                {
                    EditorGUILayout.HelpBox("No LootTable assets found in the project.", MessageType.Info);
                }
                else
                {
                    int cols = 2;
                    int idx = 0;
                    while (idx < _allProjectTables.Length)
                    {
                        EditorGUILayout.BeginHorizontal();
                        for (int c = 0; c < cols && idx < _allProjectTables.Length; c++, idx++)
                        {
                            var t = _allProjectTables[idx];
                            using (new EditorGUILayout.VerticalScope("box"))
                            {
                                GUILayout.Label(t.name);
                                using (new EditorGUILayout.HorizontalScope())
                                {
                                    if (GUILayout.Button("Open", GUILayout.Width(80))) AssetDatabase.OpenAsset(t);
                                    if (GUILayout.Button("Ping", GUILayout.Width(60))) EditorGUIUtility.PingObject(t);
                                }
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
        }

        // ---------------------------- Discovery / Refresh ---------------------
        void DiscoverLootTableType()
        {
            _lootTableType = TypeCache.GetTypesDerivedFrom<ScriptableObject>()
                .FirstOrDefault(t => t.Name == "LootTable" || t.Name.EndsWith("LootTable", StringComparison.Ordinal));
        }

        void RefreshSceneLootables()
        {
            _lootables.Clear();
            _lootTableFields.Clear();

            // Find MonoBehaviours implementing ILootable
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (mb == null) continue;
                if (mb is ILootable)
                {
                    _lootables.Add(mb);
                    CacheLootTableFields(mb);
                }
            }

            // Keep selection sane
            if (_selectedLootable == null || !_lootables.Contains(_selectedLootable))
            {
                _selectedLootable = _lootables.FirstOrDefault();
                _selectedLootableSO = _selectedLootable ? new SerializedObject(_selectedLootable) : null;
                BuildFieldList();
            }
        }

        void RefreshProjectTables()
        {
            if (_lootTableType == null)
            {
                _allProjectTables = Array.Empty<UnityEngine.Object>();
                return;
            }

            string typeStr = _lootTableType.Name;
            string[] guids = AssetDatabase.FindAssets($"t:{typeStr}");
            _allProjectTables = guids
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Select(p => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p))
                .Where(a => a != null)
                .ToArray();
        }

        IEnumerable<MonoBehaviour> FilteredLootables()
        {
            var q = _lootables.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(_search))
            {
                string s = _search.Trim();
                q = q.Where(mb => mb && mb.gameObject.name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            return q;
        }

        void CacheLootTableFields(MonoBehaviour mb)
        {
            var list = new List<FieldInfo>();
            if (_lootTableType != null)
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var fi in mb.GetType().GetFields(flags))
                {
                    if (fi.IsInitOnly) continue;
                    if (fi.IsLiteral) continue;

                    // direct field of LootTable type
                    if (fi.FieldType == _lootTableType)
                    {
                        list.Add(fi);
                        continue;
                    }

                    // array of LootTable
                    if (fi.FieldType.IsArray && fi.FieldType.GetElementType() == _lootTableType)
                    {
                        list.Add(fi);
                        continue;
                    }

                    // List<LootTable>
                    if (fi.FieldType.IsGenericType)
                    {
                        var gen = fi.FieldType.GetGenericArguments();
                        if (gen.Length == 1 && gen[0] == _lootTableType && typeof(System.Collections.IList).IsAssignableFrom(fi.FieldType))
                        {
                            list.Add(fi);
                            continue;
                        }
                    }
                }
            }
            _lootTableFields[mb] = list;
        }

        void BuildFieldList()
        {
            _fieldList = null; // currently drawing fields directly; reserved for future multi-field UI
        }

        Dictionary<UnityEngine.Object, List<MonoBehaviour>> BuildTableToLootablesMap()
        {
            var map = new Dictionary<UnityEngine.Object, List<MonoBehaviour>>();
            if (_lootTableType == null) return map;

            foreach (var mb in _lootables)
            {
                if (!_lootTableFields.TryGetValue(mb, out var fields) || fields == null) continue;
                var so = new SerializedObject(mb);
                foreach (var fi in fields)
                {
                    var p = so.FindProperty(fi.Name);
                    if (p == null) continue;

                    if (p.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        var obj = p.objectReferenceValue;
                        if (obj) Add(map, obj, mb);
                    }
                    else if (p.isArray)
                    {
                        for (int i = 0; i < p.arraySize; i++)
                        {
                            var el = p.GetArrayElementAtIndex(i);
                            if (el.propertyType == SerializedPropertyType.ObjectReference)
                            {
                                var obj = el.objectReferenceValue;
                                if (obj) Add(map, obj, mb);
                            }
                        }
                    }
                }
            }
            return map;

            static void Add(Dictionary<UnityEngine.Object, List<MonoBehaviour>> dict, UnityEngine.Object key, MonoBehaviour user)
            {
                if (!dict.TryGetValue(key, out var list)) dict[key] = list = new List<MonoBehaviour>();
                if (!list.Contains(user)) list.Add(user);
            }
        }

        // ----------------------------- Actions --------------------------------
        void CreateAndAssignNewTable(SerializedProperty targetProp)
        {
            if (_lootTableType == null) return;

            string folder = "Assets/MMO/Loot/LootTables";
            EnsureFolder(folder);

            var asset = ScriptableObject.CreateInstance(_lootTableType);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/LootTable.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            AssignToProperty(targetProp, asset);
            EditorGUIUtility.PingObject(asset);
        }

        void ShowAssignMenu(SerializedProperty prop)
        {
            var menu = new GenericMenu();
            if (_allProjectTables == null || _allProjectTables.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("No LootTable assets found"));
            }
            else
            {
                foreach (var t in _allProjectTables)
                {
                    var capture = t;
                    menu.AddItem(new GUIContent(capture.name), false, () => AssignToProperty(prop, capture));
                }
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Create New…"), false, () => CreateAndAssignNewTable(prop));
            menu.ShowAsContext();
        }

        void AssignToProperty(SerializedProperty prop, UnityEngine.Object asset)
        {
            if (prop.propertyType == SerializedPropertyType.ObjectReference)
            {
                prop.objectReferenceValue = asset;
            }
            else if (prop.isArray)
            {
                int i = prop.arraySize;
                prop.InsertArrayElementAtIndex(i);
                var el = prop.GetArrayElementAtIndex(i);
                el.objectReferenceValue = asset;
            }
            prop.serializedObject.ApplyModifiedProperties();
        }

        void CreateLootTableAsset()
        {
            if (_lootTableType == null) return;
            string folder = EditorUtility.OpenFolderPanel("Choose folder inside Assets/", Application.dataPath, "");
            if (string.IsNullOrEmpty(folder)) return;
            if (!folder.StartsWith(Application.dataPath))
            {
                EditorUtility.DisplayDialog("Invalid Folder", "Please choose a folder inside the project's Assets/ directory.", "OK");
                return;
            }
            string rel = "Assets" + folder.Substring(Application.dataPath.Length);
            EnsureFolder(rel);

            var asset = ScriptableObject.CreateInstance(_lootTableType);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{rel}/LootTable.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(asset);
            RefreshProjectTables();
        }

        static bool TryGetIsAvailable(MonoBehaviour mb, out bool value)
        {
            value = false;
            try
            {
                if (mb is ILootable il)
                {
                    // Access via reflection to avoid hard dependency if property signature changes
                    var prop = il.GetType().GetProperty("IsAvailable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
                    if (prop != null && prop.PropertyType == typeof(bool))
                    {
                        object v = prop.GetValue(il, null);
                        if (v is bool b) { value = b; return true; }
                    }
                }
            }
            catch { }
            return false;
        }

        static void EnsureFolder(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return;
            if (AssetDatabase.IsValidFolder(rel)) return;
            var parts = rel.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{cur}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
#endif
