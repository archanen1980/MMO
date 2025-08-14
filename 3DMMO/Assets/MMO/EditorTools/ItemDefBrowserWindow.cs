#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MMO.Shared.Item; // Your ItemDef base type

public class ItemDefBrowserWindow : EditorWindow
{
    private class Row
    {
        public ItemDef def;
        public string assetPath;

        public string rawItemId;
        public int parsedItemId;

        public string displayName;
        public int maxStack;
        public string equipMaskText;

        public bool inResources;
        public string resourcesPath;

        public Texture2D iconTex;   // item icon column (from ItemDef.icon/sprite/etc, fallback to preview)
    }

    private Vector2 _scroll;
    private string _search = "";
    private bool _onlyResources = false;
    private bool _problemsOnly = false;

    private readonly List<Row> _rows = new List<Row>();
    private readonly HashSet<int> _dupes = new HashSet<int>();

    // --- Menu entries: quick + Add Tab ---
    [MenuItem("Tools/MMO Starter/Item Browser")]
    [MenuItem("Window/MMO/Item Browser")]
    public static void Open()
    {
        var w = GetWindow<ItemDefBrowserWindow>();
        w.titleContent = new GUIContent("Item Browser", GetWindowIcon());
        w.minSize = new Vector2(1024, 340);
        w.Refresh();
        w.Show();
    }

    private void OnEnable()
    {
        // keep title after domain reload
        titleContent = new GUIContent("Item Browser", GetWindowIcon());
        Refresh();
    }

    private static Texture2D GetWindowIcon()
    {
        // Pick a built-in editor icon
        var c = EditorGUIUtility.IconContent("d_Project");
        return (Texture2D)c?.image;
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("Search", GUILayout.Width(50));
            string ns = GUILayout.TextField(_search, EditorStyles.toolbarTextField, GUILayout.MinWidth(220));
            if (ns != _search) _search = ns;

            _onlyResources = GUILayout.Toggle(_onlyResources, "Only Resources", EditorStyles.toolbarButton);
            _problemsOnly = GUILayout.Toggle(_problemsOnly, "Only Problems", EditorStyles.toolbarButton);

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(80)))
                Refresh();
        }

        if (_rows.Count == 0)
        {
            EditorGUILayout.HelpBox("No ItemDef assets (assignable to MMO.Shared.Item.ItemDef) were found.", MessageType.Info);
            return;
        }

        // Header
        using (new EditorGUILayout.HorizontalScope())
        {
            Bold("Icon", 54);
            Bold("itemId", 80);
            Bold("Parsed", 60);
            Bold("Name", 240);
            Bold("Max", 50);
            Bold("Equip Mask", 180);
            Bold("Resources Path", 320);
            Bold("Asset Path", 520);
        }
        var line = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(line, new Color(0, 0, 0, 0.3f));

        // Rows
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var r in Filtered())
            DrawRow(r);
        EditorGUILayout.EndScrollView();

        int total = _rows.Count;
        int inRes = _rows.Count(x => x.inResources);
        int bad = _rows.Count(x => x.parsedItemId <= 0);
        int dups = _dupes.Count;

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox("Total: " + total +
                                " | In Resources: " + inRes +
                                " | Bad numeric ids: " + bad +
                                " | Duplicate numeric ids: " + dups, MessageType.Info);
    }

    private IEnumerable<Row> Filtered()
    {
        IEnumerable<Row> e = _rows;

        if (!string.IsNullOrEmpty(_search))
        {
            string s = _search.ToLowerInvariant();
            e = e.Where(r =>
                (r.rawItemId ?? "").ToLowerInvariant().Contains(s) ||
                r.parsedItemId.ToString().Contains(s) ||
                (r.displayName ?? "").ToLowerInvariant().Contains(s) ||
                (r.resourcesPath ?? "").ToLowerInvariant().Contains(s) ||
                (r.assetPath ?? "").ToLowerInvariant().Contains(s));
        }

        if (_onlyResources) e = e.Where(r => r.inResources);
        if (_problemsOnly) e = e.Where(r => r.parsedItemId <= 0 || _dupes.Contains(r.parsedItemId) || !r.inResources);

        return e.OrderBy(r => r.parsedItemId)
                .ThenBy(r => r.displayName);
    }

    private void DrawRow(Row r)
    {
        bool bad = r.parsedItemId <= 0;
        bool dupe = _dupes.Contains(r.parsedItemId);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(r.iconTex ? r.iconTex : Texture2D.grayTexture, GUILayout.Width(54), GUILayout.Height(40));

            GUILayout.Label(r.rawItemId ?? "(null)", GUILayout.Width(80));

            var idStyle = new GUIStyle(EditorStyles.label);
            if (bad) idStyle.normal.textColor = Color.red;
            else if (dupe) idStyle.normal.textColor = new Color(1f, 0.5f, 0f);
            GUILayout.Label(r.parsedItemId.ToString(), idStyle, GUILayout.Width(60));

            GUILayout.Label(r.displayName ?? r.def.name, GUILayout.Width(240));
            GUILayout.Label(r.maxStack.ToString(), GUILayout.Width(50));
            GUILayout.Label(string.IsNullOrEmpty(r.equipMaskText) ? "(n/a)" : r.equipMaskText, GUILayout.Width(180));

            var rpStyle = new GUIStyle(EditorStyles.label);
            if (!r.inResources) rpStyle.normal.textColor = Color.red;
            GUILayout.Label(r.inResources ? r.resourcesPath : "(not in Resources)", rpStyle, GUILayout.Width(320));

            var small = new GUIStyle(EditorStyles.label) { fontSize = 10 };
            GUILayout.Label(r.assetPath, small, GUILayout.Width(520));
        }

        var line = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(line, new Color(0, 0, 0, 0.08f));
    }

    private void Refresh()
    {
        _rows.Clear();
        _dupes.Clear();

        string[] guids = AssetDatabase.FindAssets("t:ItemDef");
        var seen = new HashSet<int>();

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var def = AssetDatabase.LoadAssetAtPath<ItemDef>(path);
            if (def == null) continue;

            var row = new Row();
            row.def = def;
            row.assetPath = path;
            row.inResources = TryResourcesPath(path, out row.resourcesPath);

            // item icon from common fields/properties (Sprite/Texture2D), fallback to preview
            row.iconTex = GetItemIconTexture(def) ?? AssetPreview.GetMiniThumbnail(def);

            // itemId (string or int)
            object rawId = GetMemberValue(def, new[] { "itemId", "id" });
            row.rawItemId = rawId != null ? rawId.ToString() : null;

            int parsed = 0;
            if (rawId is int ii) parsed = ii;
            else if (rawId is string ss) int.TryParse(ss, out parsed);
            row.parsedItemId = parsed;

            // display name
            object disp = GetMemberValue(def, new[] { "displayName", "title", "label" });
            row.displayName = disp as string;
            if (string.IsNullOrEmpty(row.displayName)) row.displayName = def.name;

            // max stack
            object max = GetMemberValue(def, new[] { "maxStack", "stackSize", "stackLimit", "stackCap" });
            row.maxStack = ToInt(max, fallback: 1);

            // equip mask (stringify any enum/int/flags you use)
            object mask = GetMemberValue(def, new[] { "equipSlotsMask", "equipMask", "allowedSlots", "slotMask", "equipmentSlot", "equipmentSlots" });
            row.equipMaskText = mask != null ? mask.ToString() : "";

            _rows.Add(row);

            if (parsed > 0)
            {
                if (seen.Contains(parsed)) _dupes.Add(parsed);
                else seen.Add(parsed);
            }
        }

        Repaint();
    }

    // ----- helpers -----

    private static Texture2D GetItemIconTexture(ItemDef def)
    {
        object iconObj = GetMemberValue(def, new[] { "icon", "sprite", "uiIcon", "inventoryIcon", "thumbnail" });
        if (iconObj is Texture2D tex) return tex;
        if (iconObj is Sprite spr) return spr.texture != null ? spr.texture : null;
        return null;
    }

    private static object GetMemberValue(object obj, string[] candidateNames)
    {
        if (obj == null || candidateNames == null) return null;
        var t = obj.GetType();

        foreach (var name in candidateNames)
        {
            var f = t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (f != null) return f.GetValue(obj);

            var p = t.GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (p != null && p.CanRead) return p.GetValue(obj, null);
        }
        return null;
    }

    private static int ToInt(object o, int fallback)
    {
        if (o == null) return fallback;
        if (o is int i) return i;
        if (o is uint ui) return (int)ui;
        if (o is short s) return s;
        if (o is ushort us) return us;
        if (o is byte b) return b;
        if (o is string str && int.TryParse(str, out var parsed)) return parsed;
        return fallback;
    }

    private static bool TryResourcesPath(string assetPath, out string loadPath)
    {
        loadPath = string.Empty;
        if (string.IsNullOrEmpty(assetPath)) return false;

        string norm = assetPath.Replace('\\', '/');
        int idx = norm.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;

        string sub = norm.Substring(idx + "/Resources/".Length);
        if (sub.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            sub = sub.Substring(0, sub.Length - ".asset".Length);

        loadPath = sub;
        return true;
    }

    private static void Bold(string label, float width)
    {
        var st = new GUIStyle(EditorStyles.boldLabel);
        GUILayout.Label(label, st, GUILayout.Width(width));
    }
}
#endif
