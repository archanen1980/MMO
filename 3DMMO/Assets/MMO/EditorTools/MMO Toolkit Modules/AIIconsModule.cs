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
    [ToolkitModule("aiicons", "AI Icons", order: 40, icon: "d_Folder Icon")]
    public class AIIconsModule : MMOToolkitModuleBase
    {
        // Selection
        string _searchFolder = ModuleUtil.ItemsFolder;
        string _filter = "";
        ItemDef[] _items = Array.Empty<ItemDef>();
        bool[] _checks = Array.Empty<bool>();

        // Output
        string _outFolder = "Assets/Art/Icons/Generated";
        int _width = 512, _height = 512;
        bool _assignSprite = true;
        bool _overwrite = false;

        // Prompts
        string _stylePreset = "2D game icon, isometric, flat shading, clean outline, centered single object, high contrast, crisp edges, simple background, vector-like, UI asset";
        string _promptTemplate = "{displayName}, {style}. sprite icon, symmetrical composition, single subject, no text, plain background";
        string _negativePrompt = "photorealistic, photo, realistic, 3d render, scene, landscape, text, watermark, clutter, low contrast, blurry";

        // Providers
        enum Provider { A1111, CustomJSON }
        Provider _provider = Provider.A1111;

        // A1111
        string _baseUrl = "http://127.0.0.1:7860";
        string _modelHint = "";
        string _sampler = "DPM++ 2M Karras";
        int _steps = 28;
        float _cfg = 6.5f;
        int _seed = -1;
        bool _useRef = false;
        Texture2D _refTex;
        string _refFile = "";
        float _denoise = 0.35f;
        int _resizeMode = 0;

        // Custom
        string _customUrl = "http://localhost:5000/api/generate";
        string _customAuthHeader = "";
        string _customJson =
@"{
  ""prompt"": ""{prompt}"",
  ""width"": {width},
  ""height"": {height},
  ""seed"": {seed}
}";

        // Logs
        bool _busy = false;
        readonly StringBuilder _log = new();
        Vector2 _logScroll;

        public override void OnEnable() { RefreshItems(); }
        public override void OnDisable() { }

        public override void OnGUI()
        {
            EditorGUILayout.LabelField("AI Icon Generator", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _searchFolder = EditorGUILayout.TextField("Search Folder", _searchFolder);
                    if (GUILayout.Button("Pick…", GUILayout.Width(60)))
                    {
                        string p = EditorUtility.OpenFolderPanel("Pick Items folder (inside Assets/)", Application.dataPath, "");
                        if (!string.IsNullOrEmpty(p) && p.StartsWith(Application.dataPath))
                            _searchFolder = "Assets" + p.Substring(Application.dataPath.Length);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    _filter = EditorGUILayout.TextField("Filter", _filter);
                    if (GUILayout.Button("Refresh", GUILayout.Width(80))) RefreshItems();
                }

                EditorGUILayout.Space(4);
                DrawList();
            }

            EditorGUILayout.Space(6);

            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Provider", EditorStyles.boldLabel);
                _provider = (Provider)EditorGUILayout.EnumPopup("Type", _provider);

                if (_provider == Provider.A1111)
                {
                    _baseUrl = EditorGUILayout.TextField("Base URL", _baseUrl);
                    _modelHint = EditorGUILayout.TextField("Model (optional)", _modelHint);
                    _sampler = EditorGUILayout.TextField("Sampler", _sampler);
                    _steps = Mathf.Clamp(EditorGUILayout.IntField("Steps", _steps), 1, 80);
                    _cfg = EditorGUILayout.Slider("CFG Scale", _cfg, 1f, 15f);
                    _seed = EditorGUILayout.IntField("Seed (-1 random)", _seed);

                    _useRef = EditorGUILayout.ToggleLeft("Use Reference Image (img2img)", _useRef);
                    if (_useRef)
                    {
                        _refTex = (Texture2D)EditorGUILayout.ObjectField("Reference Texture", _refTex, typeof(Texture2D), false);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            _refFile = EditorGUILayout.TextField("Reference File (PNG)", _refFile);
                            if (GUILayout.Button("Pick…", GUILayout.Width(60)))
                            {
                                string p = EditorUtility.OpenFilePanel("Pick reference PNG", "", "png");
                                if (!string.IsNullOrEmpty(p)) _refFile = p;
                            }
                        }
                        _denoise = EditorGUILayout.Slider("Denoising Strength", _denoise, 0.1f, 0.8f);
                        _resizeMode = EditorGUILayout.Popup("Resize Mode", _resizeMode, new[] { "Just resize", "Crop and resize", "Resize and fill" });
                    }
                }
                else
                {
                    _customUrl = EditorGUILayout.TextField("Endpoint URL", _customUrl);
                    _customAuthHeader = EditorGUILayout.TextField("Auth Header (optional)", _customAuthHeader);
                    EditorGUILayout.LabelField("JSON Body Template (use {prompt}, {width}, {height}, {seed})");
                    _customJson = EditorGUILayout.TextArea(_customJson, GUILayout.MinHeight(70));
                }
            }

            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Prompting & Output", EditorStyles.boldLabel);

                _stylePreset = EditorGUILayout.TextField("Style Preset", _stylePreset);
                EditorGUILayout.LabelField("Prompt Template");
                _promptTemplate = EditorGUILayout.TextArea(_promptTemplate, GUILayout.MinHeight(50));
                _negativePrompt = EditorGUILayout.TextField("Negative Prompt", _negativePrompt);

                using (new EditorGUILayout.HorizontalScope())
                {
                    _width = Mathf.Max(64, EditorGUILayout.IntField("Width", _width));
                    _height = Mathf.Max(64, EditorGUILayout.IntField("Height", _height));
                }

                _assignSprite = EditorGUILayout.ToggleLeft("Assign generated Sprite to ItemDef.icon", _assignSprite);
                _overwrite = EditorGUILayout.ToggleLeft("Overwrite existing PNG if present", _overwrite);

                EditorGUILayout.Space(6);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.enabled = !_busy;
                    if (GUILayout.Button("Generate for Selected", GUILayout.Height(26)))
                        _ = GenerateForSelectionAsync();
                    if (GUILayout.Button("Generate for All", GUILayout.Height(26)))
                    {
                        if (_checks != null && _checks.Length > 0) Array.Fill(_checks, true);
                        _ = GenerateForSelectionAsync();
                    }
                    GUI.enabled = true;
                }
            }

            EditorGUILayout.LabelField("Logs", EditorStyles.boldLabel);
            using (var sv = new EditorGUILayout.ScrollViewScope(_logScroll, GUILayout.MinHeight(120)))
            {
                _logScroll = sv.scrollPosition;
                EditorGUILayout.HelpBox(_log.ToString(), MessageType.None);
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Placeholders: {displayName}, {itemId}, {style}", EditorStyles.miniBoldLabel);
        }

        void DrawList()
        {
            if (_items.Length == 0)
            {
                EditorGUILayout.HelpBox("No ItemDef assets found. Adjust Search Folder or create items.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.VerticalScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("All", GUILayout.Width(60))) Array.Fill(_checks, true);
                    if (GUILayout.Button("None", GUILayout.Width(60))) Array.Fill(_checks, false);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"Found: {_items.Length}", EditorStyles.miniLabel);
                }

                EditorGUILayout.Space(2);
                int show = Mathf.Min(_items.Length, 400);
                for (int i = 0; i < show; i++)
                {
                    var it = _items[i];
                    if (!it) continue;
                    using (new EditorGUILayout.HorizontalScope("box"))
                    {
                        _checks[i] = EditorGUILayout.Toggle(_checks[i], GUILayout.Width(20));
                        GUILayout.Label((it.displayName ?? it.name), GUILayout.Width(240));
                        GUILayout.FlexibleSpace();
                        GUILayout.Label($"[{it.itemId}]", EditorStyles.miniLabel, GUILayout.Width(100));
                    }
                }
                if (_items.Length > show)
                    EditorGUILayout.HelpBox($"Showing first {show} items… refine your filter.", MessageType.None);
            }
        }

        // ---- SINGLE definition of RefreshItems (fix for CS0111) ----
        void RefreshItems()
        {
            ModuleUtil.EnsureFolder(_searchFolder);
            string[] guids = AssetDatabase.FindAssets("t:ItemDef", new[] { _searchFolder });
            var list = guids.Select(g => AssetDatabase.LoadAssetAtPath<ItemDef>(AssetDatabase.GUIDToAssetPath(g)))
                            .Where(a => a != null).ToList();
            if (!string.IsNullOrWhiteSpace(_filter))
                list = list.Where(i =>
                    (i.displayName ?? i.name).IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (i.itemId ?? "").IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            _items = list.ToArray();
            _checks = new bool[_items.Length];
        }

        async Task GenerateForSelectionAsync()
        {
            if (_busy) return;
            _busy = true;

            try
            {
                ModuleUtil.EnsureFolder(_outFolder);

                var sel = new List<ItemDef>();
                for (int i = 0; i < _items.Length; i++)
                    if (_checks[i] && _items[i] != null) sel.Add(_items[i]);

                int total = sel.Count;
                int done = 0;

                foreach (var item in sel)
                {
                    string file = Path.Combine(_outFolder, $"{ModuleUtil.SafeFile(item.itemId)}.png").Replace("\\", "/");
                    if (File.Exists(file) && !_overwrite)
                    {
                        Log($"Skip (exists): {file}");
                        if (_assignSprite) ModuleUtil.AssignIconIfFound(item, file);
                        done++;
                        EditorUtility.DisplayProgressBar("MMO Toolkit: Icon Gen", $"{done}/{total} processed…", done / Mathf.Max(1f, total));
                        continue;
                    }

                    string prompt = _promptTemplate
                        .Replace("{displayName}", item.displayName)
                        .Replace("{itemId}", item.itemId)
                        .Replace("{style}", _stylePreset);

                    int seed = (_seed == -1) ? UnityEngine.Random.Range(1, int.MaxValue) : _seed;

                    Log($"Generating '{(item.displayName ?? item.name)}' → {file}\nPrompt: {prompt}");

                    byte[] png = null;
                    switch (_provider)
                    {
                        case Provider.A1111:
                            if (_useRef) png = await A1111Img2ImgAsync(prompt, _negativePrompt, seed, _width, _height);
                            else png = await A1111Txt2ImgAsync(prompt, _negativePrompt, seed, _width, _height);
                            break;
                        case Provider.CustomJSON:
                            png = await CustomAsync(prompt, seed, _width, _height);
                            break;
                    }

                    if (png == null || png.Length == 0)
                    {
                        Log($"❌ Generation failed for {item.itemId}");
                    }
                    else
                    {
                        File.WriteAllBytes(file, png);
                        AssetDatabase.ImportAsset(file);
                        ModuleUtil.ConfigureSpriteImporter(file);
                        if (_assignSprite) ModuleUtil.AssignIconIfFound(item, file);
                        Log($"✅ Saved: {file}");
                    }

                    done++;
                    EditorUtility.DisplayProgressBar("MMO Toolkit: Icon Gen", $"{done}/{total} processed…", done / Mathf.Max(1f, total));
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
                _busy = false;
                EditorUtility.ClearProgressBar();
            }
        }

        // --- A1111 plumbing ---
        [Serializable] class A1111OverrideSettings { public string sd_model_checkpoint; }
        [Serializable]
        class A1111Txt2ImgRequest
        {
            public string prompt, negative_prompt, sampler_name, sampler_index;
            public int width, height, steps, seed;
            public float cfg_scale;
            public bool enable_hr = false, do_not_save_samples = true, do_not_save_grid = true;
            public A1111OverrideSettings override_settings;
        }
        [Serializable] class A1111Txt2ImgResponse { public string[] images; }

        [Serializable]
        class A1111Img2ImgRequest
        {
            public string prompt, negative_prompt, sampler_name, sampler_index;
            public int steps, seed, width, height, resize_mode;
            public float cfg_scale, denoising_strength;
            public string[] init_images;
            public bool do_not_save_samples = true, do_not_save_grid = true;
            public A1111OverrideSettings override_settings;
        }
        [Serializable] class A1111Img2ImgResponse { public string[] images; }

        async Task<byte[]> A1111Txt2ImgAsync(string prompt, string negative, int seed, int w, int h)
        {
            string url = _baseUrl.TrimEnd('/') + "/sdapi/v1/txt2img";
            var body = new A1111Txt2ImgRequest
            {
                prompt = prompt,
                negative_prompt = negative,
                width = w,
                height = h,
                steps = _steps,
                cfg_scale = _cfg,
                seed = seed,
                sampler_name = _sampler,
                sampler_index = _sampler,
                override_settings = string.IsNullOrWhiteSpace(_modelHint) ? null
                    : new A1111OverrideSettings { sd_model_checkpoint = _modelHint }
            };
            string json = JsonUtility.ToJson(body, false);
            return await PostJsonGetPngAsync<A1111Txt2ImgResponse>(url, json);
        }

        async Task<byte[]> A1111Img2ImgAsync(string prompt, string negative, int seed, int w, int h)
        {
            byte[] refPng = null;
            if (_refTex != null)
            {
                string ap = AssetDatabase.GetAssetPath(_refTex);
                if (!string.IsNullOrEmpty(ap) && File.Exists(ap)) refPng = File.ReadAllBytes(ap);
                else refPng = _refTex.EncodeToPNG();
            }
            if (refPng == null && !string.IsNullOrWhiteSpace(_refFile) && File.Exists(_refFile))
                refPng = File.ReadAllBytes(_refFile);

            if (refPng == null)
            {
                Log("❌ No reference image set for img2img.");
                return null;
            }

            string url = _baseUrl.TrimEnd('/') + "/sdapi/v1/img2img";
            var body = new A1111Img2ImgRequest
            {
                prompt = prompt,
                negative_prompt = negative,
                steps = _steps,
                cfg_scale = _cfg,
                seed = seed,
                sampler_name = _sampler,
                sampler_index = _sampler,
                width = w,
                height = h,
                denoising_strength = _denoise,
                resize_mode = _resizeMode,
                init_images = new[] { Convert.ToBase64String(refPng) },
                override_settings = string.IsNullOrWhiteSpace(_modelHint) ? null
                    : new A1111OverrideSettings { sd_model_checkpoint = _modelHint }
            };
            string json = JsonUtility.ToJson(body, false);
            return await PostJsonGetPngAsync<A1111Img2ImgResponse>(url, json);
        }

        async Task<byte[]> PostJsonGetPngAsync<TResp>(string url, string json) where TResp : class
        {
            using (var req = new UnityWebRequest(url, "POST"))
            {
                var payload = Encoding.UTF8.GetBytes(json);
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
                    var text = req.downloadHandler.text;
                    if (typeof(TResp) == typeof(A1111Txt2ImgResponse))
                    {
                        var resp = JsonUtility.FromJson<A1111Txt2ImgResponse>(text);
                        if (resp?.images != null && resp.images.Length > 0)
                            return Convert.FromBase64String(resp.images[0]);
                    }
                    else
                    {
                        var resp = JsonUtility.FromJson<A1111Img2ImgResponse>(text);
                        if (resp?.images != null && resp.images.Length > 0)
                            return Convert.FromBase64String(resp.images[0]);
                    }
                }
                catch (Exception ex) { Log("Parse error: " + ex.Message); }
                return null;
            }
        }

        // --- Custom REST (raw PNG expected) ---
        async Task<byte[]> CustomAsync(string prompt, int seed, int w, int h)
        {
            string json = _customJson
                .Replace("{prompt}", ModuleUtil.EscapeJson(prompt))
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
                return req.downloadHandler.data;
            }
        }

        void Log(string msg) { _log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {msg}"); }
    }
}
#endif
