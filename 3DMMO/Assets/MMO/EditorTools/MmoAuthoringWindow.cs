#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using MMO.Shared.Item;  // ItemDef base
using MMO.Inventory;    // EquipSlot flags

public class MMOAuthoringWindow : EditorWindow
{
    // Config
    DefaultAsset targetFolder;       // where to create
    string suggestedFolder = "Assets/Resources/Items"; // default

    // Item type
    Type[] concreteItemTypes;
    string[] concreteItemTypeNames;
    int selectedTypeIndex = 0;

    // Fields to write (via reflection)
    string itemIdText = "";
    string displayName = "New Item";
    int    maxStack = 1;
    EquipSlot equipMask = EquipSlot.None;
    Sprite iconSprite;
    Texture2D iconTexture;

    // --- Menu entries: quick + Add Tab ---
    [MenuItem("MMO/MMO Authoring")]
    [MenuItem("Window/MMO/MMO Authoring")]
    public static void Open()
    {
        var w = GetWindow<MMOAuthoringWindow>();
        w.titleContent = new GUIContent("MMO Authoring", GetWindowIcon());
        w.minSize = new Vector2(520, 520);
        w.InitTypes();
        w.Show();
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("MMO Authoring", GetWindowIcon());
        InitTypes();
    }

    private static Texture2D GetWindowIcon()
    {
        var c = EditorGUIUtility.IconContent("d_UnityEditor.InspectorWindow");
        return (Texture2D)c?.image;
    }

    void InitTypes()
    {
        var all = TypeCache.GetTypesDerivedFrom<ItemDef>();
        var list = new List<Type>();
        foreach (var t in all)
        {
            if (t.IsAbstract) continue;
            if (t.IsGenericType) continue;
            list.Add(t);
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        concreteItemTypes = list.ToArray();
        concreteItemTypeNames = concreteItemTypes.Select(t => t.Name).ToArray();

        // default folder asset
        if (targetFolder == null)
        {
            var def = AssetDatabase.LoadAssetAtPath<DefaultAsset>(suggestedFolder);
            if (def != null) targetFolder = def;
        }

        // suggest next ID
        if (string.IsNullOrEmpty(itemIdText))
            itemIdText = (FindNextNumericId()).ToString();
    }

    void OnGUI()
    {
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Target Folder", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            targetFolder = (DefaultAsset)EditorGUILayout.ObjectField(targetFolder, typeof(DefaultAsset), false);
            if (GUILayout.Button("Use Default", GUILayout.Width(110)))
            {
                var def = AssetDatabase.LoadAssetAtPath<DefaultAsset>(suggestedFolder);
                if (def != null) targetFolder = def;
                else EditorUtility.DisplayDialog("Missing Folder", $"Create folder:\n{suggestedFolder}", "OK");
            }
        }
        EditorGUILayout.HelpBox("Tip: For runtime loading via Resources, ensure the folder is under a 'Resources/' directory.", MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Item Type", EditorStyles.boldLabel);
        if (concreteItemTypes == null || concreteItemTypes.Length == 0)
        {
            EditorGUILayout.HelpBox("No non-abstract types derived from ItemDef were found.", MessageType.Warning);
            if (GUILayout.Button("Refresh Types")) InitTypes();
            return;
        }
        selectedTypeIndex = EditorGUILayout.Popup("Create As", selectedTypeIndex, concreteItemTypeNames);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Common Fields", EditorStyles.boldLabel);
        itemIdText   = EditorGUILayout.TextField("itemId", itemIdText);
        displayName  = EditorGUILayout.TextField("Display Name", displayName);
        maxStack     = Mathf.Clamp(EditorGUILayout.IntField("Max Stack", maxStack), 1, 65535);
        equipMask    = (EquipSlot)EditorGUILayout.EnumFlagsField("Equip Mask", equipMask);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Icon (optional)", EditorStyles.boldLabel);
        iconSprite  = (Sprite)EditorGUILayout.ObjectField("Sprite", iconSprite, typeof(Sprite), false);
        iconTexture = (Texture2D)EditorGUILayout.ObjectField("Texture2D", iconTexture, typeof(Texture2D), false);

        EditorGUILayout.Space(8);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Suggest Next ID", GUILayout.Height(26)))
                itemIdText = (FindNextNumericId()).ToString();

            if (GUILayout.Button("Create Item Asset", GUILayout.Height(26)))
                CreateItemAsset();
        }

        EditorGUILayout.Space();
        if (targetFolder != null)
        {
            string f = AssetDatabase.GetAssetPath(targetFolder);
            EditorGUILayout.HelpBox("Folder: " + f, MessageType.None);
        }
    }

    int FindNextNumericId()
    {
        int max = 0;
        string[] guids = AssetDatabase.FindAssets("t:ItemDef");
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var def = AssetDatabase.LoadAssetAtPath<ItemDef>(path);
            if (def == null) continue;
            if (TryGetNumeric(def, new[] { "itemId", "id" }, out int val))
                if (val > max) max = val;
        }
        return Mathf.Max(1, max + 1);
    }

    void CreateItemAsset()
    {
        if (targetFolder == null)
        {
            EditorUtility.DisplayDialog("No Folder", "Assign a target folder (ideally under Assets/Resources).", "OK");
            return;
        }

        // parse numeric id (we'll set both numeric or string as needed)
        if (!int.TryParse(itemIdText, out int numericId) || numericId <= 0)
        {
            EditorUtility.DisplayDialog("Invalid itemId", "itemId must be a positive integer.", "OK");
            return;
        }

        string folderPath = AssetDatabase.GetAssetPath(targetFolder);
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            EditorUtility.DisplayDialog("Bad Folder", "Target must be a valid project folder.", "OK");
            return;
        }

        // create instance
        var type = concreteItemTypes[Mathf.Clamp(selectedTypeIndex, 0, concreteItemTypes.Length - 1)];
        var instance = ScriptableObject.CreateInstance(type);
        if (instance == null)
        {
            EditorUtility.DisplayDialog("Create Failed", "Couldn't instantiate ItemDef type.", "OK");
            return;
        }

        // set fields via reflection
        SetMemberValue(instance, new[] { "itemId", "id" }, (object)numericId, preferString: true);  // will store as string if member is string
        SetMemberValue(instance, new[] { "displayName", "title", "label" }, displayName);
        SetMemberValue(instance, new[] { "maxStack", "stackSize", "stackLimit", "stackCap" }, maxStack);
        SetEquipMask(instance, equipMask);
        if (iconSprite != null)  SetMemberValue(instance, new[] { "icon", "sprite", "uiIcon", "inventoryIcon", "thumbnail" }, iconSprite);
        else if (iconTexture != null) SetMemberValue(instance, new[] { "icon", "sprite", "uiIcon", "inventoryIcon", "thumbnail" }, iconTexture);

        // save asset
        string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(displayName) ? ("Item_" + numericId) : displayName);
        string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folderPath, safeName + ".asset"));
        AssetDatabase.CreateAsset(instance, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorGUIUtility.PingObject(instance);
        Selection.activeObject = instance;

        EditorUtility.DisplayDialog("Created", $"New {type.Name} created at:\n{path}", "OK");
    }

    // --- reflection helpers ---

    static bool TryGetNumeric(ItemDef def, string[] names, out int val)
    {
        val = 0;
        if (def == null) return false;
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var t = def.GetType();

        foreach (var n in names)
        {
            var f = t.GetField(n, F);
            if (f != null)
            {
                var v = f.GetValue(def);
                if (v is int i) { val = i; return true; }
                if (v is string s && int.TryParse(s, out val)) return true;
            }
            var p = t.GetProperty(n, F);
            if (p != null && p.CanRead)
            {
                var v = p.GetValue(def, null);
                if (v is int i) { val = i; return true; }
                if (v is string s && int.TryParse(s, out val)) return true;
            }
        }
        return false;
    }

    static void SetMemberValue(object obj, string[] names, object value, bool preferString = false)
    {
        if (obj == null || names == null) return;
        var t = obj.GetType();
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var n in names)
        {
            var f = t.GetField(n, F);
            if (f != null)
            {
                f.SetValue(obj, ConvertFor(f.FieldType, value, preferString));
                return;
            }
            var p = t.GetProperty(n, F);
            if (p != null && p.CanWrite)
            {
                p.SetValue(obj, ConvertFor(p.PropertyType, value, preferString), null);
                return;
            }
        }
    }

    static object ConvertFor(Type dest, object value, bool preferString)
    {
        if (value == null) return null;

        // sprite/texture convenience
        if (dest == typeof(Sprite))
        {
            if (value is Sprite s) return s;
            if (value is Texture2D t) return null; // cannot auto-convert Texture2D to Sprite without slicing
        }
        if (dest == typeof(Texture2D))
        {
            if (value is Texture2D t) return t;
            if (value is Sprite s) return s.texture;
        }

        // numeric/string for itemId
        if (dest == typeof(string))
        {
            if (preferString && value is int i) return i.ToString();
            return value.ToString();
        }
        if (dest == typeof(int))
        {
            if (value is int i) return i;
            if (value is string ss && int.TryParse(ss, out var parsed)) return parsed;
        }

        // generic
        try
        {
            if (dest.IsEnum)
            {
                if (value is int ei) return Enum.ToObject(dest, ei);
                if (value is string es) return Enum.Parse(dest, es, true);
            }
        }
        catch { /* ignore */ }

        return value;
    }

    static void SetEquipMask(object obj, EquipSlot mask)
    {
        // try a handful of common names/types
        SetMemberValue(obj, new[]
        {
            "equipSlotsMask", "equipMask", "allowedSlots", "allowedEquipSlots",
            "slotMask", "equipSlot", "slot", "equipmentSlot", "equipmentSlots"
        }, (int)mask);
    }

    static string SanitizeFileName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Trim();
    }
}
#endif
