#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MMO.EditorTools
{
    /// <summary>
    /// Exports the MMO Starter (Mirror) as a .unitypackage.
    /// Includes your MMO folder + known prefabs/scene if present.
    /// Mirror (UPM) is NOT included; install it in the target project.
    /// </summary>
    public static class ExportMmoStarter
    {
        // Adjust these if your paths vary
        const string RootFolder = "Assets/Hughes_Jeremiah_Assets/MMO";
        static readonly string[] OptionalAssets =
        {
            "Assets/Prefabs/Player/MmoPlayer.prefab",
            "Assets/Prefabs/Systems/MmoGame.prefab",
            "Assets/Scenes/SampleWorld.unity"
        };

        [MenuItem("Tools/MMO Starter/Export UnityPackage...", priority = 0)]
        public static void Export()
        {
            // Build the list of export paths
            var toExport = new List<string>();

            if (Directory.Exists(RootFolder))
                toExport.Add(RootFolder);
            else
            {
                EditorUtility.DisplayDialog("Export MMO Starter",
                    $"Could not find root folder:\n{RootFolder}\n\nPlease check the path in ExportMmoStarter.cs.",
                    "OK");
                return;
            }

            foreach (var p in OptionalAssets)
            {
                if (File.Exists(p) || Directory.Exists(p))
                    toExport.Add(p);
            }

            // Sanity: convert to unique asset GUID-based paths (avoids dupes)
            var finalList = new List<string>();
            var seen = new HashSet<string>();
            foreach (var path in toExport)
            {
                // Ensure path is an asset
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (obj != null)
                {
                    string ap = AssetDatabase.GetAssetPath(obj);
                    if (!string.IsNullOrEmpty(ap) && seen.Add(ap))
                        finalList.Add(ap);
                }
                else if (Directory.Exists(path))
                {
                    // Add folder directly; ExportPackage can recurse
                    if (seen.Add(path))
                        finalList.Add(path);
                }
            }

            if (finalList.Count == 0)
            {
                EditorUtility.DisplayDialog("Export MMO Starter",
                    "No exportable assets found. Make sure your files are inside the project’s Assets/ folder.",
                    "OK");
                return;
            }

            // Save location
            string defaultName = $"MMO_Starter_Mirror_{DateTime.Now:yyyyMMdd_HHmm}.unitypackage";
            string savePath = EditorUtility.SaveFilePanel(
                "Save UnityPackage",
                Directory.GetCurrentDirectory(),
                defaultName,
                "unitypackage");

            if (string.IsNullOrEmpty(savePath))
                return; // user canceled

            try
            {
                // Export with dependencies so required assets under Assets/ come along.
                AssetDatabase.ExportPackage(
                    finalList.ToArray(),
                    savePath,
                    ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies);

                Debug.Log($"[MMO Starter] Exported package:\n{savePath}\n" +
                          "Note: Mirror (UPM) is not included. In the target project: " +
                          "Package Manager → Add from git URL → https://github.com/MirrorNetworking/Mirror.git");
                EditorUtility.DisplayDialog("Export Complete",
                    $"Exported:\n{savePath}\n\nReminder: Install Mirror via UPM in the target project.",
                    "Great");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MMO Starter] Export failed: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("Export Failed", ex.Message, "OK");
            }
        }

        [MenuItem("Tools/MMO Starter/Verify Mirror Dependency", priority = 10)]
        public static void VerifyMirror()
        {
            bool found = Directory.Exists("Packages/com.mirror.networking")
                         || File.ReadAllText("Packages/manifest.json").Contains("Mirror");

            EditorUtility.DisplayDialog("Mirror Dependency",
                found
                    ? "Looks like Mirror is already referenced in this project."
                    : "Mirror not detected. In the target project, open Package Manager and add:\nhttps://github.com/MirrorNetworking/Mirror.git",
                "OK");
        }
    }
}
#endif
