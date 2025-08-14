#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using MMO.Shared.Item;

namespace MMO.EditorTools
{
    /// <summary>
    /// Tools → MMO Starter → AI Icon Generator…
    /// Generate PNG icons via Automatic1111 (txt2img or img2img) or a custom REST endpoint
    /// and assign them to ItemDef.icon. Supports a global reference image (img2img).
    /// </summary>
    public class AIIconGeneratorWindow : EditorWindow
    {
        // ---------- Provider selection ----------
        enum Provider { StableDiffusion_A1111, CustomREST_JSON }

        [MenuItem("Tools/MMO Starter/AI Icon Generator…")]
        public static void Open()
        {
            var w = GetWindow<AIIconGeneratorWindow>("AI Icon Generator");
            w.minSize = new Vector2(820, 560);
            w.RefreshItems();
        }

        // ---------- UI state ----------
        Vector2 _leftScroll, _rightScroll, _logScroll;
        Provider _provider = Provider.StableDiffusion_A1111;

        // Common settings
        string _outputFolder = "Assets/Art/Icons/Generated";
        int _width = 512;
        int _height = 512;
        bool _assignSprite = true;
        bool _overwriteExisting = false;

        // Prompt templating
        string _stylePreset = "2D game icon, isometric, flat shading, clean outline, centered single object, high contrast, crisp edges, simple background, vector-like, UI asset";
        string _promptTemplate =
            "{displayName}, {style}. " +
            "sprite icon, symmetrical composition, single subject, no text, plain background";
        string _negativePrompt =
            "photorealistic, photo, realistic, 3d render, scene, landscape, environment, building, people, hands, text, logo, watermark, clutter, low contrast, blurry, grain, depth of field";

        // Item selection
        ItemDef[] _items = Array.Empty<ItemDef>();
        bool[] _itemChecks = Array.Empty<bool>();
        string _filter = "";

        // Progress / logs
        bool _isGenerating = false;
        readonly StringBuilder _log = new StringBuilder();

        // ---------- A1111 (txt2img + img2img) ----------
        string _a1111BaseUrl = "http://127.0.0.1:7860";
        string _a1111ModelHint = ""; // exact checkpoint name shown in WebUI dropdown (optional)
        string _a1111Sampler = "DPM++ 2M Karras";
        int _a1111Steps = 28;
        float _a1111Cfg = 6.5f;
        int _a1111Seed = -1; // -1 = random

        // img2img reference
        bool _useReferenceImg = false;
        Texture2D _referenceTexture;              // drag an asset (preferred)
        string _referenceExternalPath = "";       // or choose a PNG file outside Assets
        float _img2imgDenoise = 0.35f;            // lower = stick closer to reference
        int _img2imgResizeMode = 0;               // 0 Just resize, 1 Crop and resize, 2 Resize and fill

        // ---------- Custom REST template (expects raw PNG bytes) ----------
        string _customUrl = "http://localhost:5000/api/generate";
        string _customAuthHeader = ""; // e.g., "Authorization: Bearer sk-***"
        string _customJsonTemplate =
@"{
  ""prompt"": ""{prompt}"",
  ""width"": {width},
  ""height"": {height},
  ""seed"": {seed}
}";

        void OnEnable() => RefreshItems();

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLeft();
                DrawRight();
            }
            DrawFooter();
        }

        void DrawLeft()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(330)))
            {
                EditorGUILayout.LabelField("Items (Resources/Items)", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    string newFilter = EditorGUILayout.TextField("Filter", _filter);
                    if (newFilter != _filter) { _filter = newFilter; RefreshItems(); }
                    if (GUILayout.Button("Refresh", GUILayout.Width(70))) RefreshItems();
                }

                using (var scroll = new EditorGUILayout.ScrollViewScope(_leftScroll))
                {
                    _leftScroll = scroll.scrollPosition;

                    if (_items.Length == 0)
                    {
                        EditorGUILayout.HelpBox("No ItemDef assets found in Resources/Items.\nCreate some with the Authoring window.", MessageType.Info);
                    }
                    else
                    {
                        for (int i = 0; i < _items.Length; i++)
                        {
                            var it = _items[i];
                            if (!it) continue;
                            using (new EditorGUILayout.HorizontalScope("box"))
                            {
                                _itemChecks[i] = EditorGUILayout.Toggle(_itemChecks[i], GUILayout.Width(20));
                                GUILayout.Label(it.displayName, GUILayout.Width(180));
                                GUILayout.FlexibleSpace();
                                GUILayout.Label($"[{it.itemId}]", EditorStyles.miniLabel);
                            }
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("All")) Array.Fill(_itemChecks, true);
                    if (GUILayout.Button("None")) Array.Fill(_itemChecks, false);
                }

                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Provider", EditorStyles.boldLabel);
                _provider = (Provider)EditorGUILayout.EnumPopup(_provider);

                switch (_provider)
                {
                    case Provider.StableDiffusion_A1111:
                        DrawA1111Settings();
                        break;
                    case Provider.CustomREST_JSON:
                        DrawCustomSettings();
                        break;
                }
            }
        }

        void DrawRight()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField("Prompt", EditorStyles.boldLabel);

                _stylePreset = EditorGUILayout.TextField("Style Preset", _stylePreset);
                EditorGUILayout.LabelField("Prompt Template");
                _promptTemplate = EditorGUILayout.TextArea(_promptTemplate, GUILayout.MinHeight(60));
                _negativePrompt = EditorGUILayout.TextField("Negative Prompt", _negativePrompt);

                using (new EditorGUILayout.HorizontalScope())
                {
                    _width = Mathf.Max(64, EditorGUILayout.IntField("Width", _width));
                    _height = Mathf.Max(64, EditorGUILayout.IntField("Height", _height));
                }

                _assignSprite = EditorGUILayout.ToggleLeft("Assign generated Sprite to ItemDef.icon", _assignSprite);
                _overwriteExisting = EditorGUILayout.ToggleLeft("Overwrite existing PNG if present", _overwriteExisting);

                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.enabled = !_isGenerating;
                    if (GUILayout.Button("Generate for Selected", GUILayout.Height(28)))
                        _ = GenerateForSelectionAsync();
                    if (GUILayout.Button("Generate for All", GUILayout.Height(28)))
                    {
                        Array.Fill(_itemChecks, true);
                        _ = GenerateForSelectionAsync();
                    }
                    GUI.enabled = true;
                }

                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _outputFolder = EditorGUILayout.TextField("Folder", _outputFolder);
                    if (GUILayout.Button("…", GUILayout.Width(30)))
                    {
                        string picked = EditorUtility.OpenFolderPanel("Choose output folder (inside project)", Application.dataPath, "");
                        if (!string.IsNullOrEmpty(picked))
                        {
                            if (!picked.StartsWith(Application.dataPath))
                                EditorUtility.DisplayDialog("Invalid Folder", "Folder must be inside your project's Assets/.", "OK");
                            else
                                _outputFolder = "Assets" + picked.Substring(Application.dataPath.Length);
                        }
                    }
                }

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Logs", EditorStyles.boldLabel);
                using (var scroll = new EditorGUILayout.ScrollViewScope(_logScroll, GUILayout.ExpandHeight(true)))
                {
                    _logScroll = scroll.scrollPosition;
                    EditorGUILayout.HelpBox(_log.ToString(), MessageType.None);
                }
            }
        }

        void DrawFooter()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Placeholders: {displayName}, {itemId}, {style}", EditorStyles.miniBoldLabel);
        }

        // --------- A1111 settings (txt2img + img2img) ----------
        void DrawA1111Settings()
        {
            EditorGUILayout.LabelField("Stable Diffusion (Automatic1111)", EditorStyles.boldLabel);
            _a1111BaseUrl = EditorGUILayout.TextField("Base URL", _a1111BaseUrl);
            _a1111ModelHint = EditorGUILayout.TextField("Model (optional)", _a1111ModelHint); // exact checkpoint string
            _a1111Sampler = EditorGUILayout.TextField("Sampler", _a1111Sampler);
            _a1111Steps = Mathf.Clamp(EditorGUILayout.IntField("Steps", _a1111Steps), 1, 80);
            _a1111Cfg = EditorGUILayout.Slider("CFG Scale", _a1111Cfg, 1f, 15f);
            _a1111Seed = EditorGUILayout.IntField("Seed (-1 random)", _a1111Seed);

            EditorGUILayout.Space(6);
            _useReferenceImg = EditorGUILayout.ToggleLeft("Use Reference Image (img2img)", _useReferenceImg);
            if (_useReferenceImg)
            {
                _referenceTexture = (Texture2D)EditorGUILayout.ObjectField("Reference Texture", _referenceTexture, typeof(Texture2D), false);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _referenceExternalPath = EditorGUILayout.TextField("Reference File (PNG)", _referenceExternalPath);
                    if (GUILayout.Button("Pick…", GUILayout.Width(60)))
                    {
                        string p = EditorUtility.OpenFilePanel("Pick reference PNG", "", "png");
                        if (!string.IsNullOrEmpty(p)) _referenceExternalPath = p;
                    }
                }
                _img2imgDenoise = EditorGUILayout.Slider("Denoising Strength", _img2imgDenoise, 0.1f, 0.8f);
                _img2imgResizeMode = EditorGUILayout.Popup("Resize Mode", _img2imgResizeMode,
                    new[] { "Just resize", "Crop and resize", "Resize and fill" });
                EditorGUILayout.HelpBox("With a good reference icon, try denoise ~0.25–0.45 to keep composition/style and just change details.",
                    MessageType.Info);
            }
        }

        // --------- Custom REST template ----------
        void DrawCustomSettings()
        {
            EditorGUILayout.LabelField("Custom REST (template)", EditorStyles.boldLabel);
            _customUrl = EditorGUILayout.TextField("Endpoint URL", _customUrl);
            _customAuthHeader = EditorGUILayout.TextField("Auth Header (optional)", _customAuthHeader);
            EditorGUILayout.LabelField("JSON Body Template (use {prompt}, {width}, {height}, {seed})");
            _customJsonTemplate = EditorGUILayout.TextArea(_customJsonTemplate, GUILayout.MinHeight(80));
            EditorGUILayout.HelpBox("This expects the service to return raw PNG bytes. Adjust the body/headers to fit your API.",
                MessageType.Info);
        }

        // ---------- Actions ----------
        void RefreshItems()
        {
            string[] guids = AssetDatabase.FindAssets("t:ItemDef", new[] { "Assets/Resources/Items" });
            var list = guids.Select(g => AssetDatabase.LoadAssetAtPath<ItemDef>(AssetDatabase.GUIDToAssetPath(g)))
                            .Where(a => a != null).ToList();

            if (!string.IsNullOrWhiteSpace(_filter))
                list = list.Where(i =>
                    i.displayName.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    i.itemId.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            _items = list.ToArray();
            _itemChecks = new bool[_items.Length];
        }

        async Task GenerateForSelectionAsync()
        {
            if (_isGenerating) return;
            _isGenerating = true;
            try
            {
                EnsureFolder(_outputFolder);

                // Build selection upfront for accurate progress
                var sel = new List<ItemDef>();
                for (int i = 0; i < _items.Length; i++)
                    if (_itemChecks[i] && _items[i] != null) sel.Add(_items[i]);

                int total = sel.Count;
                int done = 0;

                foreach (var item in sel)
                {
                    string path = Path.Combine(_outputFolder, $"{Safe(item.itemId)}.png").Replace("\\", "/");
                    if (File.Exists(path) && !_overwriteExisting)
                    {
                        Log($"Skip (exists): {path}");
                        if (_assignSprite) AssignIconIfFound(item, path);
                        done++;
                        EditorUtility.DisplayProgressBar("AI Icon Generator", $"{done}/{total} processed…", done / (float)Mathf.Max(1, total));
                        continue;
                    }

                    string prompt = BuildPrompt(item);
                    int seed = (_a1111Seed == -1) ? UnityEngine.Random.Range(1, int.MaxValue) : _a1111Seed;

                    Log($"Generating '{item.displayName}' → {path}\nPrompt: {prompt}");

                    byte[] png = null;
                    switch (_provider)
                    {
                        case Provider.StableDiffusion_A1111:
                            if (_useReferenceImg)
                                png = await GenerateA1111Img2ImgAsync(prompt, _negativePrompt, seed, _width, _height);
                            else
                                png = await GenerateA1111Txt2ImgAsync(prompt, _negativePrompt, seed, _width, _height);
                            break;

                        case Provider.CustomREST_JSON:
                            png = await GenerateCustomAsync(prompt, seed, _width, _height);
                            break;
                    }

                    if (png == null || png.Length == 0)
                    {
                        Log($"❌ Generation failed for {item.itemId}");
                    }
                    else
                    {
                        File.WriteAllBytes(path, png);
                        AssetDatabase.ImportAsset(path);
                        ConfigureSpriteImporter(path);
                        if (_assignSprite) AssignIconIfFound(item, path);
                        Log($"✅ Saved: {path}");
                    }

                    done++;
                    EditorUtility.DisplayProgressBar("AI Icon Generator", $"{done}/{total} processed…", done / (float)Mathf.Max(1, total));
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Log("Completed.");
            }
            catch (Exception ex)
            {
                Log($"Error: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                _isGenerating = false;
                EditorUtility.ClearProgressBar();
            }
        }

        string BuildPrompt(ItemDef item)
        {
            return _promptTemplate
                .Replace("{displayName}", item.displayName)
                .Replace("{itemId}", item.itemId)
                .Replace("{style}", _stylePreset);
        }

        // ---------- A1111 models (txt2img + img2img) ----------
        [Serializable] class A1111OverrideSettings { public string sd_model_checkpoint; }
        [Serializable]
        class A1111Txt2ImgRequest
        {
            public string prompt;
            public string negative_prompt;
            public int width;
            public int height;
            public int steps;
            public float cfg_scale;
            public int seed;
            public string sampler_name;
            public string sampler_index;
            public bool enable_hr = false;
            public bool do_not_save_samples = true;
            public bool do_not_save_grid = true;
            public A1111OverrideSettings override_settings;
        }
        [Serializable] class A1111Txt2ImgResponse { public string[] images; }

        [Serializable]
        class A1111Img2ImgRequest
        {
            public string prompt;
            public string negative_prompt;
            public int steps;
            public float cfg_scale;
            public int seed;
            public string sampler_name;
            public string sampler_index;
            public int width;
            public int height;
            public float denoising_strength;
            public int resize_mode; // 0 Just resize, 1 Crop and resize, 2 Resize and fill
            public string[] init_images; // base64 PNG strings (no data: prefix)
            public bool do_not_save_samples = true;
            public bool do_not_save_grid = true;
            public A1111OverrideSettings override_settings;
        }
        [Serializable] class A1111Img2ImgResponse { public string[] images; }

        async Task<byte[]> GenerateA1111Txt2ImgAsync(string prompt, string negative, int seed, int w, int h)
        {
            string url = _a1111BaseUrl.TrimEnd('/') + "/sdapi/v1/txt2img";
            var body = new A1111Txt2ImgRequest
            {
                prompt = prompt,
                negative_prompt = negative,
                width = w,
                height = h,
                steps = _a1111Steps,
                cfg_scale = _a1111Cfg,
                seed = seed,
                sampler_name = _a1111Sampler,
                sampler_index = _a1111Sampler,
                enable_hr = false,
                do_not_save_samples = true,
                do_not_save_grid = true,
                override_settings = string.IsNullOrWhiteSpace(_a1111ModelHint) ? null
                    : new A1111OverrideSettings { sd_model_checkpoint = _a1111ModelHint }
            };

            string json = JsonUtility.ToJson(body, false);
            return await PostA1111AndGetPngAsync<A1111Txt2ImgResponse>(url, json);
        }

        async Task<byte[]> GenerateA1111Img2ImgAsync(string prompt, string negative, int seed, int w, int h)
        {
            // Collect reference PNG bytes (Texture asset preferred; else external file)
            byte[] refPng = null;
            if (_referenceTexture != null)
            {
                // Try original asset file (works even if texture is not readable)
                string ap = AssetDatabase.GetAssetPath(_referenceTexture);
                if (!string.IsNullOrEmpty(ap) && File.Exists(ap))
                    refPng = File.ReadAllBytes(ap);
                else
                    refPng = _referenceTexture.EncodeToPNG(); // requires readable
            }
            if (refPng == null && !string.IsNullOrWhiteSpace(_referenceExternalPath) && File.Exists(_referenceExternalPath))
                refPng = File.ReadAllBytes(_referenceExternalPath);

            if (refPng == null)
            {
                Log("❌ No reference image set for img2img.");
                return null;
            }

            string url = _a1111BaseUrl.TrimEnd('/') + "/sdapi/v1/img2img";
            var body = new A1111Img2ImgRequest
            {
                prompt = prompt,
                negative_prompt = negative,
                steps = _a1111Steps,
                cfg_scale = _a1111Cfg,
                seed = seed,
                sampler_name = _a1111Sampler,
                sampler_index = _a1111Sampler,
                width = w,
                height = h,
                denoising_strength = _img2imgDenoise,
                resize_mode = _img2imgResizeMode,
                init_images = new[] { Convert.ToBase64String(refPng) },
                do_not_save_samples = true,
                do_not_save_grid = true,
                override_settings = string.IsNullOrWhiteSpace(_a1111ModelHint) ? null
                    : new A1111OverrideSettings { sd_model_checkpoint = _a1111ModelHint }
            };

            string json = JsonUtility.ToJson(body, false);
            return await PostA1111AndGetPngAsync<A1111Img2ImgResponse>(url, json);
        }

        async Task<byte[]> PostA1111AndGetPngAsync<TResp>(string url, string json) where TResp : class
        {
            using (var req = new UnityWebRequest(url, "POST"))
            {
                byte[] payload = Encoding.UTF8.GetBytes(json);
                req.uploadHandler = new UploadHandlerRaw(payload);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");

                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Log($"HTTP error: {req.responseCode} {req.error}");
                    return null;
                }

                try
                {
                    // Both txt2img and img2img return { images: ["<base64png>", ...], ... }
                    var text = req.downloadHandler.text;
                    if (typeof(TResp) == typeof(A1111Txt2ImgResponse))
                    {
                        var resp = JsonUtility.FromJson<A1111Txt2ImgResponse>(text);
                        if (resp != null && resp.images != null && resp.images.Length > 0)
                            return Convert.FromBase64String(resp.images[0]);
                    }
                    else
                    {
                        var resp = JsonUtility.FromJson<A1111Img2ImgResponse>(text);
                        if (resp != null && resp.images != null && resp.images.Length > 0)
                            return Convert.FromBase64String(resp.images[0]);
                    }
                }
                catch (Exception ex)
                {
                    Log("Parse error: " + ex.Message);
                }
                return null;
            }
        }

        // ---------- Custom REST ----------
        async Task<byte[]> GenerateCustomAsync(string prompt, int seed, int w, int h)
        {
            string json = _customJsonTemplate
                .Replace("{prompt}", EscapeJson(prompt))
                .Replace("{width}", w.ToString())
                .Replace("{height}", h.ToString())
                .Replace("{seed}", seed.ToString());

            using (var req = new UnityWebRequest(_customUrl, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrWhiteSpace(_customAuthHeader))
                {
                    var idx = _customAuthHeader.IndexOf(':');
                    if (idx > 0)
                        req.SetRequestHeader(_customAuthHeader[..idx].Trim(), _customAuthHeader[(idx + 1)..].Trim());
                }

                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Log($"Custom REST HTTP error: {req.responseCode} {req.error}");
                    return null;
                }
                return req.downloadHandler.data; // expects raw PNG
            }
        }

        // ---------- Import & assign ----------
        void ConfigureSpriteImporter(string assetPath)
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

        void AssignIconIfFound(ItemDef item, string assetPath)
        {
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sp == null) return;
            item.icon = sp;
            EditorUtility.SetDirty(item);
        }

        // ---------- Utils ----------
        void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string[] parts = path.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{cur}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        void Log(string msg) { _log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {msg}"); Repaint(); }

        static string Safe(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }

        static string EscapeJson(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
#endif
