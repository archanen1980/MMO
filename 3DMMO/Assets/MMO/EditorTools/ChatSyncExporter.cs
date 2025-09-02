#if UNITY_EDITOR
using System;
using IO_ = System.IO;                     // Alias for clarity
using IOC = System.IO.Compression;        // Avoid UnityEngine.CompressionLevel name clash
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MMO.EditorTools
{
    /// <summary>
    /// Export a ZIP of your C# files so you can share the latest code quickly.
    /// Paths are preserved under Assets/ for easy re-import/diffing.
    /// </summary>
    public static class ChatSyncExporter
    {
        // Adjust to taste
        static readonly string[] Roots =
        {
            "Assets/MMO",
            "Assets/mmo",
            "Assets/Hughes_Jeremiah_Assets/MMO"
        };

        [MenuItem("Tools/MMO Starter/Export/Code Bundle for ChatGPT…", priority = 21)]
        public static void ExportCodeZip()
        {
            try
            { 
                var files = Roots
                    .Where(IO_.Directory.Exists)
                    .SelectMany(root => IO_.Directory.GetFiles(root, "*.cs", IO_.SearchOption.AllDirectories))
                    .Distinct()
                    .ToArray();
                
                if (files.Length == 0)
                {
                    EditorUtility.DisplayDialog("Code Bundle", "No .cs files found under configured roots.", "OK");
                    return;
                }

                string defaultName = $"ChatSync_{DateTime.Now:yyyyMMdd_HHmm}.zip";
                string savePath = EditorUtility.SaveFilePanel("Save Code Bundle (.zip)", IO_.Directory.GetCurrentDirectory(), defaultName, "zip");
                if (string.IsNullOrEmpty(savePath)) return;

                string tempDir = IO_.Path.Combine(Application.temporaryCachePath, "ChatSyncExport");
                if (IO_.Directory.Exists(tempDir)) IO_.Directory.Delete(tempDir, true);
                IO_.Directory.CreateDirectory(tempDir);

                foreach (var f in files)
                {
                    string rel = f.Replace("\\", "/");
                    int i = rel.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
                    if (i < 0) continue;
                    rel = rel.Substring(i); 

                    string outPath = IO_.Path.Combine(tempDir, rel);
                    IO_.Directory.CreateDirectory(IO_.Path.GetDirectoryName(outPath));
                    IO_.File.Copy(f, outPath, overwrite: true);
                }

                // Fully qualify CompressionLevel to avoid ambiguity with UnityEngine.CompressionLevel
                if (IO_.File.Exists(savePath)) IO_.File.Delete(savePath);
                IOC.ZipFile.CreateFromDirectory(tempDir, savePath, IOC.CompressionLevel.Optimal, includeBaseDirectory: false);

                try { IO_.Directory.Delete(tempDir, true); } catch { /* ignore */ }

                EditorUtility.DisplayDialog("Code Bundle", $"Saved:\n{savePath}", "Great");
                EditorUtility.RevealInFinder(savePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ChatSyncExporter] Failed: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("Export Failed", ex.Message, "OK");
            }
        }
    }
}
#endif
