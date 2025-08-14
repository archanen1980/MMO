#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MMO.EditorTools
{
    public static class ModuleUtil
    {
        public const string ItemsFolder = "Assets/Resources/Items";
        public const string RecipesFolder = "Assets/Resources/Recipes";
        public const string UiPrefabFolder = "Assets/Prefabs/UI";

        public static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{cur}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        public static bool ExistsAny(string assetPath)
        {
            return Directory.Exists(assetPath) ||
                   File.Exists(assetPath) ||
                   AssetDatabase.FindAssets("", new[] { assetPath }).Length > 0;
        }

        public static string Slugify(string s, string fallback = "new_item")
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            s = s.ToLowerInvariant();
            var chars = s.Select(c =>
                char.IsLetterOrDigit(c) ? c :
                (c == ' ' || c == '-' || c == '_') ? '_' : '\0')
                .Where(c => c != '\0')
                .ToArray();
            var slug = new string(chars).Trim('_');
            return string.IsNullOrEmpty(slug) ? fallback : slug;
        }

        public static string SafeFile(string s, string fallback = "unnamed")
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }

        public static Font ResolveFont()
        {
            try
            {
                string[] candidates = { "Arial", "Segoe UI", "Helvetica", "Liberation Sans", "DejaVu Sans", "Tahoma" };
                var f = Font.CreateDynamicFontFromOSFont(candidates, 14);
                if (f) return f;
            }
            catch { }
            try
            {
                var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (f) return f;
            }
            catch { }
            return null;
        }

        public static void ConfigureSpriteImporter(string assetPath)
        {
            var ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (ti == null) return;

            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }

        public static void AssignIconIfFound(UnityEngine.Object asset, string spritePath, string fieldName = "icon")
        {
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if (sp == null || asset == null) return;

            var t = asset.GetType();
            var f = t.GetField(fieldName);
            if (f != null) { f.SetValue(asset, sp); EditorUtility.SetDirty(asset); return; }
            var p = t.GetProperty(fieldName);
            if (p != null && p.CanWrite) { p.SetValue(asset, sp, null); EditorUtility.SetDirty(asset); }
        }

        public static string EscapeJson(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
#endif
