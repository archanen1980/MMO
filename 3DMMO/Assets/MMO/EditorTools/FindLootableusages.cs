// Assets/MMO/Loot/Editor/FindLootTableUsages.cs
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using MMO.Loot;

static class FindLootTableUsages
{
    [MenuItem("Assets/MMO/Loot/Find Lootables Using This Table", true)]
    static bool ValidateFind() => Selection.activeObject is LootTable;

    [MenuItem("Assets/MMO/Loot/Find Lootables Using This Table")]
    static void Find()
    {
        var table = Selection.activeObject as LootTable;
        if (!table) return;

        // Find any MonoBehaviour that implements ILootable (active scene, includes inactive)
#if UNITY_2023_1_OR_NEWER
        var mbs = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        var mbs = Object.FindObjectsOfType<MonoBehaviour>(true);
#endif
        var hits = new List<GameObject>();

        foreach (var mb in mbs)
        {
            if (mb == null || mb is not ILootable) continue;

            var so = new SerializedObject(mb);
            foreach (var f in GetLootTableFields(mb))
            {
                var p = so.FindProperty(f.Name);
                if (p == null) continue;

                if (p.propertyType == SerializedPropertyType.ObjectReference)
                {
                    if (p.objectReferenceValue == table)
                    {
                        AddUnique(hits, mb.gameObject);
                        break;
                    }
                }
                else if (p.isArray)
                {
                    for (int i = 0; i < p.arraySize; i++)
                    {
                        var el = p.GetArrayElementAtIndex(i);
                        if (el.propertyType == SerializedPropertyType.ObjectReference &&
                            el.objectReferenceValue == table)
                        {
                            AddUnique(hits, mb.gameObject);
                            break;
                        }
                    }
                }
            }
        }

        if (hits.Count > 0)
        {
            Selection.objects = hits.ToArray();
            EditorGUIUtility.PingObject(hits[0]);
        }
        EditorUtility.DisplayDialog("LootTable Usage",
            hits.Count > 0
                ? $"Selected {hits.Count} lootable(s) in the Hierarchy."
                : "No lootables in the active scene reference this table.",
            "OK");
    }

    static IEnumerable<FieldInfo> GetLootTableFields(MonoBehaviour comp)
    {
        var t = comp.GetType();
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var f in t.GetFields(flags))
        {
            if (f.IsStatic) continue;

            // LootTable
            if (typeof(LootTable).IsAssignableFrom(f.FieldType))
            { yield return f; continue; }

            // LootTable[]
            if (f.FieldType.IsArray && typeof(LootTable).IsAssignableFrom(f.FieldType.GetElementType()))
            { yield return f; continue; }

            // List<LootTable>
            if (f.FieldType.IsGenericType &&
                f.FieldType.GetGenericTypeDefinition() == typeof(List<>) &&
                typeof(LootTable).IsAssignableFrom(f.FieldType.GetGenericArguments()[0]))
            { yield return f; continue; }
        }
    }

    static void AddUnique(List<GameObject> list, GameObject go)
    {
        if (go && !list.Contains(go)) list.Add(go);
    }
}
#endif
