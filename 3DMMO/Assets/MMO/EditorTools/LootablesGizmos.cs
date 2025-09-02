#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

using MMO.Loot; // ILootable

namespace MMO.EditorTools
{
    /// <summary>
    /// Scene-view gizmos for any MonoBehaviour that implements ILootable.
    /// - Green ring = available
    /// - Red ring   = unavailable
    /// Optional name/label over the object and the linked LootTable name (if discoverable via reflection/serialized field).
    /// Toggle from menu: Tools ▸ MMO Starter ▸ Loot ▸ Show Lootable Gizmos
    /// </summary>
    [InitializeOnLoad]
    public static class LootablesGizmos
    {
        const string PrefEnabled = "MMO.LootablesGizmos.Enabled";
        const string PrefLabels = "MMO.LootablesGizmos.Labels";
        const string PrefOnlyVisible = "MMO.LootablesGizmos.OnlyVisible"; // cull by camera frustum

        static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefEnabled, true);
            set => EditorPrefs.SetBool(PrefEnabled, value);
        }
        static bool ShowLabels
        {
            get => EditorPrefs.GetBool(PrefLabels, true);
            set => EditorPrefs.SetBool(PrefLabels, value);
        }
        static bool OnlyWhenVisible
        {
            get => EditorPrefs.GetBool(PrefOnlyVisible, false);
            set => EditorPrefs.SetBool(PrefOnlyVisible, value);
        }

        static LootablesGizmos()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        [MenuItem("Tools/MMO Starter/Loot/Show Lootable Gizmos", priority = 1010)]
        public static void ToggleGizmos()
        {
            Enabled = !Enabled;
            SceneView.RepaintAll();
        }
        [MenuItem("Tools/MMO Starter/Loot/Show Lootable Gizmos", true)]
        public static bool ToggleGizmosValidate()
        {
            Menu.SetChecked("Tools/MMO Starter/Loot/Show Lootable Gizmos", Enabled);
            return true;
        }

        [MenuItem("Tools/MMO Starter/Loot/Show Lootable Labels", priority = 1011)]
        public static void ToggleLabels()
        {
            ShowLabels = !ShowLabels;
            SceneView.RepaintAll();
        }
        [MenuItem("Tools/MMO Starter/Loot/Show Lootable Labels", true)]
        public static bool ToggleLabelsValidate()
        {
            Menu.SetChecked("Tools/MMO Starter/Loot/Show Lootable Labels", ShowLabels);
            return true;
        }

        [MenuItem("Tools/MMO Starter/Loot/Cull Gizmos Outside Camera", priority = 1012)]
        public static void ToggleCull()
        {
            OnlyWhenVisible = !OnlyWhenVisible;
            SceneView.RepaintAll();
        }
        [MenuItem("Tools/MMO Starter/Loot/Cull Gizmos Outside Camera", true)]
        public static bool ToggleCullValidate()
        {
            Menu.SetChecked("Tools/MMO Starter/Loot/Cull Gizmos Outside Camera", OnlyWhenVisible);
            return true;
        }

        static void OnSceneGUI(SceneView sv)
        {
            if (!Enabled) return;

            Camera cam = sv.camera;
            var mbs = FindAllLootableBehaviours();

            Handles.zTest = CompareFunction.LessEqual;
            foreach (var mb in mbs)
            {
                if (!mb) continue;
                var t = mb.transform;
                if (!t) continue;

                if (OnlyWhenVisible && cam && !GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(cam), new Bounds(t.position, Vector3.one)))
                    continue;

                bool available = true;
                try { available = ((ILootable)mb).IsAvailable; } catch { /* fallback true */ }

                float size = HandleUtility.GetHandleSize(t.position) * 0.35f;
                var col = available ? s_Green : s_Red;
                using (new Handles.DrawingScope(col))
                {
                    // base ring
                    Handles.DrawWireDisc(t.position, Vector3.up, size);
                    // crosshair
                    Handles.DrawLine(t.position + Vector3.right * size * 0.8f, t.position - Vector3.right * size * 0.8f);
                    Handles.DrawLine(t.position + Vector3.forward * size * 0.8f, t.position - Vector3.forward * size * 0.8f);
                }

                if (ShowLabels)
                {
                    DrawLabel(t.position, BuildLabel(mb));
                }
            }

            DrawLegend(sv.position);
        }

        static List<MonoBehaviour> FindAllLootableBehaviours()
        {
#if UNITY_2023_1_OR_NEWER
            var all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var all = Object.FindObjectsOfType<MonoBehaviour>(true);
#endif
            var list = new List<MonoBehaviour>(128);
            foreach (var mb in all)
            {
                if (mb == null) continue;
                if (mb is ILootable) list.Add(mb);
            }
            return list;
        }

        static string BuildLabel(MonoBehaviour mb)
        {
            string name = mb.gameObject.name;
            string table = TryGetLootTableName(mb);
            return string.IsNullOrEmpty(table) ? name : $"{name}\n<color=#AAAAAA>{table}</color>";
        }

        static string TryGetLootTableName(MonoBehaviour mb)
        {
            // Prefer a serialized field named 'lootTable'
            try
            {
                var so = new SerializedObject(mb);
                var sp = so.FindProperty("lootTable");
                if (sp != null && sp.propertyType == SerializedPropertyType.ObjectReference && sp.objectReferenceValue)
                {
                    return sp.objectReferenceValue.name;
                }
            }
            catch { }
            return string.Empty;
        }

        static void DrawLabel(Vector3 worldPos, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var guiPoint = HandleUtility.WorldToGUIPoint(worldPos + Vector3.up * 0.0f);

            Handles.BeginGUI();
            var size = LabelStyle.CalcSize(new GUIContent(text));
            var rect = new Rect(guiPoint.x - size.x * 0.5f, guiPoint.y - size.y - 6f, size.x + 8f, size.y + 4f);
            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.Box(rect, GUIContent.none, s_Background);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 4, rect.y + 2, rect.width - 8, rect.height - 4), text, LabelStyle);
            Handles.EndGUI();
        }

        static void DrawLegend(Rect sceneViewRect)
        {
            const float pad = 8f;
            var r = new Rect(sceneViewRect.width - 190 - pad, sceneViewRect.height - 66 - pad, 190, 66);

            Handles.BeginGUI();
            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.Box(r, GUIContent.none, s_Background);
            GUI.color = Color.white;

            var row = new Rect(r.x + 8, r.y + 6, r.width - 16, 18);
            GUI.Label(row, "Lootables Legend", BoldStyle);
            row.y += 18;
            RowDot(row, s_Green, "Available");
            row.y += 18;
            RowDot(row, s_Red, "Unavailable");
            Handles.EndGUI();
        }

        static void RowDot(Rect r, Color c, string label)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y + 3, 12, 12), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(r.x + 16, r.y, r.width - 16, r.height), label, EditorStyles.miniLabel);
        }

        static GUIStyle s_LabelStyle;
        static readonly GUIStyle s_Background = "SelectionRect";

        // Lazy styles to avoid touching EditorStyles in the static constructor
        static GUIStyle LabelStyle => s_LabelStyle ??= new GUIStyle(EditorStyles.whiteMiniLabel)
        {
            richText = true,
            alignment = TextAnchor.UpperLeft
        };
        static GUIStyle BoldStyle => s_Bold ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 10 };
        static GUIStyle s_Bold;

        static readonly Color s_Green = new Color(0.3f, 1f, 0.4f, 0.95f);
        static readonly Color s_Red = new Color(1f, 0.35f, 0.35f, 0.95f);
    }
}
#endif
