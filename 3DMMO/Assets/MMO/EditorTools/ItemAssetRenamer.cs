#if UNITY_EDITOR
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using MMO.Shared.Item; // <- adjust if your ItemDef namespace differs

namespace MMO.EditorTools
{
    public static class ItemAssetRenamer
    {
        // Configure your items root & the "new_" prefix here:
        public static string ItemsRoot = "Assets/Resources/Items";
        public static string NewPrefix = "new_";

        /// <summary>
        /// Renames all ItemDef .asset files under ItemsRoot whose filenames start with NewPrefix
        /// to the item's display name (fallback: itemId → object.name), keeping them in-place.
        /// </summary>
        public static void RenameNewPrefixedUnder(string itemsRoot = null, string newPrefix = null, bool preferDisplayName = true)
        {
            itemsRoot ??= ItemsRoot;
            newPrefix ??= NewPrefix;

            if (!AssetDatabase.IsValidFolder(itemsRoot))
            {
                Debug.LogWarning($"[ItemAssetRenamer] Folder not found: {itemsRoot}");
                return;
            }

            var guids = AssetDatabase.FindAssets("t:ItemDef", new[] { itemsRoot });
            var toRename = new List<(ItemDef def, string path)>(guids.Length);

            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var file = Path.GetFileName(path);
                if (file.StartsWith(newPrefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    var def = AssetDatabase.LoadAssetAtPath<ItemDef>(path);
                    if (def) toRename.Add((def, path));
                }
            }

            if (toRename.Count == 0)
            {
                Debug.Log("[ItemAssetRenamer] No files starting with prefix found to rename.");
                return;
            }

            AssetDatabase.StartAssetEditing();
            try
            {
                int ok = 0;
                foreach (var (def, path) in toRename)
                {
                    string dir = Path.GetDirectoryName(path)!.Replace('\\', '/');

                    string raw = preferDisplayName && !string.IsNullOrWhiteSpace(def.displayName)
                               ? def.displayName
                               : (!string.IsNullOrWhiteSpace(def.itemId) ? def.itemId : def.name);

                    string safe = ToSafeFileName(raw);
                    string currentNoExt = Path.GetFileNameWithoutExtension(path);

                    if (string.Equals(currentNoExt, safe, System.StringComparison.Ordinal))
                        continue;

                    string desired = $"{dir}/{safe}.asset";
                    desired = AssetDatabase.GenerateUniqueAssetPath(desired); // avoid collisions

                    string err = AssetDatabase.MoveAsset(path, desired);      // same-folder "rename"
                    if (!string.IsNullOrEmpty(err))
                    {
                        Debug.LogWarning($"[ItemAssetRenamer] Rename '{path}' → '{desired}' failed: {err}");
                    }
                    else ok++;
                }

                Debug.Log($"[ItemAssetRenamer] Renamed {ok} file(s) under {itemsRoot}.");
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        // Quick menu actions
        [MenuItem("Tools/MMO Starter/Rename 'new_' Items To Their Names")]
        public static void Menu_RenameNewPrefixed() => RenameNewPrefixedUnder();

        // Utility: sanitize filename
        static readonly Regex Unsafe = new Regex(@"[^A-Za-z0-9\-_]+", RegexOptions.Compiled);
        static string ToSafeFileName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "item";
            s = s.Trim().Replace(' ', '-');
            s = Unsafe.Replace(s, "-");
            while (s.Contains("--")) s = s.Replace("--", "-");
            return s.Trim('-', '_');
        }
    }
}
#endif
