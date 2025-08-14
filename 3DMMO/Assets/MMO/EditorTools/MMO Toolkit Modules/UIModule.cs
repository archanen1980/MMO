#if UNITY_EDITOR
using System; // <-- needed for DateTime
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.EditorTools
{
    [ToolkitModule("ui", "UI Generator", order: 30, icon: "d_UnityEditor.SceneView")]
    public class UIModule : MMOToolkitModuleBase
    {
        public override void OnGUI()
        {
            EditorGUILayout.LabelField("UI Generator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Generate a simple Inventory Panel prefab (Canvas + Panel + Grid + Slot label).", MessageType.Info);

            if (GUILayout.Button("Generate Inventory Panel Prefab...", GUILayout.Height(32)))
                GenerateInventoryPrefab();

            GUILayout.Space(8);
            if (GUILayout.Button("Open Export UnityPackage…", GUILayout.Height(24)))
                ExportUnityPackage();
        }

        void GenerateInventoryPrefab()
        {
            ModuleUtil.EnsureFolder(ModuleUtil.UiPrefabFolder);

            var canvasGO = new GameObject("InventoryCanvas_Generated", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var panel = new GameObject("InventoryPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasGO.transform, false);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(1, 0);
            prt.anchorMax = new Vector2(1, 0);
            prt.pivot = new Vector2(1, 0);
            prt.anchoredPosition = new Vector2(-10, 10);
            prt.sizeDelta = new Vector2(520, 360);
            panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.45f);

            var gridGO = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup));
            gridGO.transform.SetParent(panel.transform, false);
            var grt = gridGO.GetComponent<RectTransform>();
            grt.anchorMin = new Vector2(0, 0);
            grt.anchorMax = new Vector2(1, 1);
            grt.offsetMin = new Vector2(8, 8);
            grt.offsetMax = new Vector2(-8, -8);

            var grid = gridGO.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(96, 64);
            grid.spacing = new Vector2(6, 6);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;

            var slot = new GameObject("SlotTemplate", typeof(RectTransform), typeof(Image));
            slot.transform.SetParent(gridGO.transform, false);
            slot.GetComponent<RectTransform>().sizeDelta = new Vector2(96, 64);
            slot.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.65f);

            var label = new GameObject("Label", typeof(Text));
            label.transform.SetParent(slot.transform, false);
            var lrt = label.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(6, 6);
            lrt.offsetMax = new Vector2(-6, -6);
            var text = label.GetComponent<Text>();
            text.text = "Item x1";
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.fontSize = 14;
            text.font = ModuleUtil.ResolveFont();
            text.raycastTarget = false;

            string path = AssetDatabase.GenerateUniqueAssetPath($"{ModuleUtil.UiPrefabFolder}/InventoryPanel_Generated.prefab");
            var prefab = PrefabUtility.SaveAsPrefabAsset(canvasGO, path);
            UnityEngine.Object.DestroyImmediate(canvasGO);
            EditorGUIUtility.PingObject(prefab);
            EditorUtility.DisplayDialog("Inventory Prefab", $"Saved:\n{path}", "OK");
        }

        void ExportUnityPackage()
        {
            var files = new List<string>
            {
                "Assets/Hughes_Jeremiah_Assets/MMO/Shared/Items",
                "Assets/Hughes_Jeremiah_Assets/MMO/Shared/Crafting",
                "Assets/Hughes_Jeremiah_Assets/MMO/Editor",
                "Assets/Hughes_Jeremiah_Assets/MMO/Gameplay/Inventory",
                "Assets/Resources/Items",
                "Assets/Resources/Recipes",
                "Assets/Prefabs/UI"
            };
            files = files.Where(ModuleUtil.ExistsAny).ToList();
            string save = EditorUtility.SaveFilePanel(
                "Export MMO Authoring Package",
                Directory.GetCurrentDirectory(),
                $"MMO_Authoring_{DateTime.Now:yyyyMMdd_HHmm}.unitypackage",
                "unitypackage"
            );
            if (string.IsNullOrEmpty(save)) return;
            AssetDatabase.ExportPackage(files.ToArray(), save,
                ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies);
            EditorUtility.DisplayDialog("Export Complete", $"Exported:\n{save}", "Great");
        }
    }
}
#endif
