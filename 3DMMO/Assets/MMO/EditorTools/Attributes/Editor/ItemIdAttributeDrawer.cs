#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MMO.Shared.Item;

[CustomPropertyDrawer(typeof(ItemIdAttribute))]
public class ItemIdAttributeDrawer : PropertyDrawer
{
    ItemDef[] _defs = Array.Empty<ItemDef>();
    string[] _display = Array.Empty<string>();
    double _lastScanTime;

    void EnsureCache()
    {
        // Refresh at most once per second to keep it snappy
        if (EditorApplication.timeSinceStartup - _lastScanTime < 1.0 && _defs.Length > 0) return;
        _lastScanTime = EditorApplication.timeSinceStartup;

        // Find all ItemDefs (search whole project; fast enough, cached)
        var guids = AssetDatabase.FindAssets("t:ItemDef");
        _defs = guids
            .Select(g => AssetDatabase.LoadAssetAtPath<ItemDef>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(d => d != null)
            .OrderBy(d => d.displayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _display = _defs.Select(d =>
        {
            var id = string.IsNullOrEmpty(d.itemId) ? d.name : d.itemId;
            var name = string.IsNullOrEmpty(d.displayName) ? d.name : d.displayName;
            return $"{name} [{id}]";
        }).ToArray();
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        EnsureCache();
        var attr = (ItemIdAttribute)attribute;
        bool showNone = attr.showNone;

        string current = property.stringValue ?? "";
        int idx = Array.FindIndex(_defs, d => string.Equals(d.itemId, current, StringComparison.OrdinalIgnoreCase));

        // Build shown array (optional "None/Clear" at top)
        int extra = showNone ? 1 : 0;
        var shown = new string[_display.Length + extra];
        if (showNone) shown[0] = string.IsNullOrEmpty(current) ? "None" : "Clear";
        Array.Copy(_display, 0, shown, extra, _display.Length);

        int shownIndex = idx >= 0 ? idx + extra : (showNone ? 0 : -1);
        int newShownIndex = EditorGUI.Popup(position, label.text, shownIndex, shown);

        if (newShownIndex != shownIndex)
        {
            if (showNone && newShownIndex == 0)
            {
                // Clear
                property.stringValue = "";
                SetSiblingDef(property, null);
            }
            else
            {
                int defIndex = newShownIndex - extra;
                if (defIndex >= 0 && defIndex < _defs.Length)
                {
                    var def = _defs[defIndex];
                    property.stringValue = string.IsNullOrEmpty(def.itemId) ? def.name : def.itemId;
                    SetSiblingDef(property, def);
                }
            }
        }
    }

    static void SetSiblingDef(SerializedProperty itemIdProp, ItemDef def)
    {
        // Convert "items.Array.data[0].itemId" -> "items.Array.data[0].def"
        string path = itemIdProp.propertyPath;
        int lastDot = path.LastIndexOf('.');
        if (lastDot < 0) return;
        string parentPath = path.Substring(0, lastDot);
        string defPath = parentPath + ".def";

        var defProp = itemIdProp.serializedObject.FindProperty(defPath);
        if (defProp != null)
        {
            defProp.objectReferenceValue = def;
            itemIdProp.serializedObject.ApplyModifiedProperties();
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => EditorGUIUtility.singleLineHeight;
}
#endif
