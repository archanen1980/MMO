#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;
using MMO.Shared.Item;
using MMO.Shared.Crafting;

namespace MMO.EditorTools
{
    /// <summary>
    /// One-stop authoring window for Items, Recipes, and simple UI generation.
    /// Open via: Tools -> MMO Starter -> Authoring...
    /// </summary>
    public class MmoAuthoringWindow : EditorWindow
    {
        const string ItemsFolder = "Assets/Resources/Items";
        const string RecipesFolder = "Assets/Resources/Recipes";
        const string UiPrefabFolder = "Assets/Prefabs/UI";

        Vector2 _leftScroll, _rightScroll;
        int _tab = 0; // 0=Items, 1=Recipes, 2=UI

        // Lazily-built header style (avoids EditorStyles access in OnEnable)
        GUIStyle _header;
        GUIStyle Header
        {
            get
            {
                if (_header == null)
                {
                    GUIStyle baseStyle;
                    try { baseStyle = EditorStyles.boldLabel; }
                    catch { baseStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold }; }
                    _header = new GUIStyle(baseStyle) { fontSize = 13 };
                }
                return _header;
            }
        }

        // Item state
        ItemDef[] _itemAssets = Array.Empty<ItemDef>();
        int _itemIndex = -1;
        ItemDef _selectedItem;

        // Recipe state
        CraftingRecipeDef[] _recipeAssets = Array.Empty<CraftingRecipeDef>();
        int _recipeIndex = -1;
        CraftingRecipeDef _selectedRecipe;
        ReorderableList _inputsList;

        [MenuItem("Tools/MMO Starter/Authoring...")]
        public static void Open()
        {
            var w = GetWindow<MmoAuthoringWindow>("MMO Authoring");
            w.minSize = new Vector2(850, 500);
            w.RefreshAll();
        }

        void OnEnable()
        {
            // Don't touch EditorStyles here; just refresh data.
            RefreshAll();
        }

        void OnGUI()
        {
            DrawToolbar();

            switch (_tab)
            {
                case 0: DrawItemsTab(); break;
                case 1: DrawRecipesTab(); break;
                case 2: DrawUiTab(); break;
            }
        }

        void DrawToolbar()
        {
            GUILayout.Space(6);
            _tab = GUILayout.Toolbar(_tab, new[] { "Items", "Recipes", "UI" }, GUILayout.Height(24));
            GUILayout.Space(6);
        }

        // ---------------- Items Tab ----------------
        void DrawItemsTab()
        {
            EditorGUILayout.BeginHorizontal();

            // Left: list
            EditorGUILayout.BeginVertical(GUILayout.Width(280));
            EditorGUILayout.LabelField("Items (Resources/Items)", Header);
            if (GUILayout.Button("New Item", GUILayout.Height(24)))
                CreateNewItem();

            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
            for (int i = 0; i < _itemAssets.Length; i++)
            {
                var it = _itemAssets[i];
                if (!it) continue;
                using (new EditorGUILayout.HorizontalScope("box"))
                {
                    if (GUILayout.Toggle(_itemIndex == i, $"{it.displayName}  [{it.itemId}]", "Button"))
                    {
                        _itemIndex = i;
                        _selectedItem = it;
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            if (_selectedItem)
            {
                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Duplicate", GUILayout.Height(22))) DuplicateItem(_selectedItem);
                    if (GUILayout.Button("Reveal", GUILayout.Height(22))) EditorGUIUtility.PingObject(_selectedItem);
                }
                if (GUILayout.Button("Delete", GUILayout.Height(22)))
                {
                    if (EditorUtility.DisplayDialog("Delete Item",
                        $"Delete '{_selectedItem.displayName}'?", "Delete", "Cancel"))
                    {
                        AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(_selectedItem));
                        _selectedItem = null; _itemIndex = -1;
                        RefreshItems();
                    }
                }
            }

            EditorGUILayout.EndVertical();

            // Right: inspector
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Item Inspector", Header);

            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
            if (_selectedItem == null)
            {
                EditorGUILayout.HelpBox("Select an item on the left, or click 'New Item' to create one.", MessageType.Info);
            }
            else
            {
                EditorGUI.BeginChangeCheck();

                _selectedItem.displayName = EditorGUILayout.TextField("Display Name", _selectedItem.displayName);

                // Auto-suggest itemId from displayName, but let user override
                using (new EditorGUILayout.HorizontalScope())
                {
                    _selectedItem.itemId = EditorGUILayout.TextField("Item ID", _selectedItem.itemId);
                    if (GUILayout.Button("Auto", GUILayout.Width(60)))
                        _selectedItem.itemId = Slugify(_selectedItem.displayName);
                }

                _selectedItem.maxStack = Mathf.Max(1, EditorGUILayout.IntField("Max Stack", _selectedItem.maxStack));
                _selectedItem.icon = (Sprite)EditorGUILayout.ObjectField("Icon", _selectedItem.icon, typeof(Sprite), false);

                EditorGUILayout.LabelField("Description");
                _selectedItem.description = EditorGUILayout.TextArea(_selectedItem.description, GUILayout.MinHeight(60));

                _selectedItem.weight = EditorGUILayout.FloatField("Weight", _selectedItem.weight);

                // Validation
                string idErr = ValidateUniqueItemId(_selectedItem);
                if (!string.IsNullOrEmpty(idErr))
                    EditorGUILayout.HelpBox(idErr, MessageType.Error);

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(_selectedItem);
                }

                GUILayout.Space(6);
                if (GUILayout.Button("Save", GUILayout.Height(26)))
                {
                    AssetDatabase.SaveAssets();
                    RefreshItems();
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical(); // inspector

            EditorGUILayout.EndHorizontal();
        }

        // ---------------- Recipes Tab ----------------
        void DrawRecipesTab()
        {
            EditorGUILayout.BeginHorizontal();

            // Left: list
            EditorGUILayout.BeginVertical(GUILayout.Width(280));
            EditorGUILayout.LabelField("Recipes (Resources/Recipes)", Header);
            if (GUILayout.Button("New Recipe", GUILayout.Height(24)))
                CreateNewRecipe();

            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
            for (int i = 0; i < _recipeAssets.Length; i++)
            {
                var r = _recipeAssets[i];
                if (!r) continue;
                using (new EditorGUILayout.HorizontalScope("box"))
                {
                    if (GUILayout.Toggle(_recipeIndex == i, $"{r.recipeId}", "Button"))
                    {
                        _recipeIndex = i;
                        SelectRecipe(r);
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            if (_selectedRecipe)
            {
                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Duplicate", GUILayout.Height(22))) DuplicateRecipe(_selectedRecipe);
                    if (GUILayout.Button("Reveal", GUILayout.Height(22))) EditorGUIUtility.PingObject(_selectedRecipe);
                }
                if (GUILayout.Button("Delete", GUILayout.Height(22)))
                {
                    if (EditorUtility.DisplayDialog("Delete Recipe",
                        $"Delete recipe '{_selectedRecipe.recipeId}'?", "Delete", "Cancel"))
                    {
                        AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(_selectedRecipe));
                        _selectedRecipe = null; _recipeIndex = -1;
                        RefreshRecipes();
                    }
                }
            }

            EditorGUILayout.EndVertical();

            // Right: inspector
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Recipe Inspector", Header);

            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
            if (_selectedRecipe == null)
            {
                EditorGUILayout.HelpBox("Select a recipe on the left, or click 'New Recipe'.", MessageType.Info);
            }
            else
            {
                EditorGUI.BeginChangeCheck();

                using (new EditorGUILayout.HorizontalScope())
                {
                    _selectedRecipe.recipeId = EditorGUILayout.TextField("Recipe ID", _selectedRecipe.recipeId);
                    if (GUILayout.Button("Auto", GUILayout.Width(60)))
                    {
                        string baseId = _selectedRecipe.output?.item ? _selectedRecipe.output.item.itemId : "recipe";
                        _selectedRecipe.recipeId = "craft_" + Slugify(baseId);
                    }
                }

                _selectedRecipe.stationTag = EditorGUILayout.TextField("Station Tag", _selectedRecipe.stationTag);
                _selectedRecipe.craftSeconds = Mathf.Max(0f, EditorGUILayout.FloatField("Craft Seconds", _selectedRecipe.craftSeconds));

                // Inputs list
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Inputs", EditorStyles.boldLabel);
                EnsureInputsList();
                _inputsList.DoLayoutList();

                // Output
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
                if (_selectedRecipe.output == null) _selectedRecipe.output = new CraftingRecipeDef.ItemAmount();
                _selectedRecipe.output.item = (ItemDef)EditorGUILayout.ObjectField("Item", _selectedRecipe.output.item, typeof(ItemDef), false);
                _selectedRecipe.output.amount = Mathf.Max(1, EditorGUILayout.IntField("Amount", _selectedRecipe.output.amount));

                // Validation
                string idErr = ValidateUniqueRecipeId(_selectedRecipe);
                if (!string.IsNullOrEmpty(idErr))
                    EditorGUILayout.HelpBox(idErr, MessageType.Error);
                if (_selectedRecipe.output == null || _selectedRecipe.output.item == null)
                    EditorGUILayout.HelpBox("Output item is required.", MessageType.Warning);
                if (_selectedRecipe.inputs == null || _selectedRecipe.inputs.Length == 0)
                    EditorGUILayout.HelpBox("Add at least one input.", MessageType.Info);

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(_selectedRecipe);
                }

                GUILayout.Space(6);
                if (GUILayout.Button("Save", GUILayout.Height(26)))
                {
                    AssetDatabase.SaveAssets();
                    RefreshRecipes();
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical(); // inspector

            EditorGUILayout.EndHorizontal();
        }

        // ---------------- UI Tab ----------------
        void DrawUiTab()
        {
            EditorGUILayout.LabelField("UI Generator", Header);
            EditorGUILayout.HelpBox("Generate a simple Inventory Panel prefab (Canvas + Panel + Grid + Slot labels). This is optional—your runtime UI already works. The prefab is handy if you want a designer-authored variant.", MessageType.Info);

            if (GUILayout.Button("Generate Inventory Panel Prefab...", GUILayout.Height(32)))
                GenerateInventoryPrefab();

            GUILayout.Space(8);
            if (GUILayout.Button("Open Export UnityPackage…", GUILayout.Height(24)))
                ExportUnityPackage();
        }

        // ===== Helpers =====
        void RefreshAll() { RefreshItems(); RefreshRecipes(); }

        void RefreshItems()
        {
            EnsureFolder(ItemsFolder);
            string[] guids = AssetDatabase.FindAssets("t:ItemDef", new[] { ItemsFolder });
            _itemAssets = guids.Select(g => AssetDatabase.LoadAssetAtPath<ItemDef>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            Array.Sort(_itemAssets, (a, b) => string.Compare(a?.displayName, b?.displayName, StringComparison.OrdinalIgnoreCase));
        }

        void RefreshRecipes()
        {
            EnsureFolder(RecipesFolder);
            string[] guids = AssetDatabase.FindAssets("t:CraftingRecipeDef", new[] { RecipesFolder });
            _recipeAssets = guids.Select(g => AssetDatabase.LoadAssetAtPath<CraftingRecipeDef>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            Array.Sort(_recipeAssets, (a, b) => string.Compare(a?.recipeId, b?.recipeId, StringComparison.OrdinalIgnoreCase));
            _inputsList = null; // rebuild next draw
        }

        void CreateNewItem()
        {
            EnsureFolder(ItemsFolder);
            string name = "New Item";
            string id = MakeUniqueItemId(Slugify(name));
            var asset = ScriptableObject.CreateInstance<ItemDef>();
            asset.displayName = name;
            asset.itemId = id;
            asset.maxStack = 1;

            string path = AssetDatabase.GenerateUniqueAssetPath($"{ItemsFolder}/{id}.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            RefreshItems();

            _selectedItem = asset;
            _itemIndex = Array.IndexOf(_itemAssets, asset);
            EditorGUIUtility.PingObject(asset);
        }

        void DuplicateItem(ItemDef src)
        {
            if (!src) return;
            EnsureFolder(ItemsFolder);
            var clone = Instantiate(src);
            clone.itemId = MakeUniqueItemId(src.itemId);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{ItemsFolder}/{clone.itemId}.asset");
            AssetDatabase.CreateAsset(clone, path);
            AssetDatabase.SaveAssets();
            RefreshItems();
            _selectedItem = clone;
            _itemIndex = Array.IndexOf(_itemAssets, clone);
            EditorGUIUtility.PingObject(clone);
        }

        void CreateNewRecipe()
        {
            EnsureFolder(RecipesFolder);
            var asset = ScriptableObject.CreateInstance<CraftingRecipeDef>();
            asset.recipeId = MakeUniqueRecipeId("new_recipe");
            asset.craftSeconds = 1f;
            asset.inputs = new CraftingRecipeDef.ItemAmount[0];
            asset.output = new CraftingRecipeDef.ItemAmount { amount = 1 };

            string path = AssetDatabase.GenerateUniqueAssetPath($"{RecipesFolder}/{asset.recipeId}.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            RefreshRecipes();

            SelectRecipe(asset);
            EditorGUIUtility.PingObject(asset);
        }

        void DuplicateRecipe(CraftingRecipeDef src)
        {
            if (!src) return;
            EnsureFolder(RecipesFolder);
            var clone = Instantiate(src);
            clone.recipeId = MakeUniqueRecipeId(src.recipeId);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{RecipesFolder}/{clone.recipeId}.asset");
            AssetDatabase.CreateAsset(clone, path);
            AssetDatabase.SaveAssets();
            RefreshRecipes();
            SelectRecipe(clone);
            EditorGUIUtility.PingObject(clone);
        }

        void SelectRecipe(CraftingRecipeDef r)
        {
            _selectedRecipe = r;
            _recipeIndex = Array.IndexOf(_recipeAssets, r);
            _inputsList = null;
        }

        void EnsureInputsList()
        {
            if (_selectedRecipe == null) return;
            if (_inputsList != null) return;

            SerializedObject so = new SerializedObject(_selectedRecipe);
            var prop = so.FindProperty("inputs");
            _inputsList = new ReorderableList(so, prop, true, true, true, true);
            _inputsList.drawHeaderCallback = (Rect r) => EditorGUI.LabelField(r, "Input Items");
            _inputsList.elementHeight = EditorGUIUtility.singleLineHeight + 6;

            _inputsList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var p = prop.GetArrayElementAtIndex(index);
                rect.y += 2;
                rect.height = EditorGUIUtility.singleLineHeight;

                var itemProp = p.FindPropertyRelative("item");
                var amtProp = p.FindPropertyRelative("amount");

                float w = rect.width;
                Rect rItem = new Rect(rect.x, rect.y, w - 80f, rect.height);
                Rect rAmt = new Rect(rect.x + w - 76f, rect.y, 70f, rect.height);

                EditorGUI.PropertyField(rItem, itemProp, GUIContent.none);
                EditorGUI.PropertyField(rAmt, amtProp, GUIContent.none);
                if (amtProp.intValue < 1) amtProp.intValue = 1;
            };

            _inputsList.onAddCallback = (ReorderableList list) =>
            {
                prop.arraySize++;
                so.ApplyModifiedProperties();
            };

            _inputsList.onRemoveCallback = (ReorderableList list) =>
            {
                if (EditorUtility.DisplayDialog("Remove Input", "Remove this input?", "Remove", "Cancel"))
                {
                    ReorderableList.defaultBehaviours.DoRemoveButton(list);
                    so.ApplyModifiedProperties();
                }
            };
        }

        // UI generation
        void GenerateInventoryPrefab()
        {
            EnsureFolder(UiPrefabFolder);

            // Root Canvas
            var canvasGO = new GameObject("InventoryCanvas_Generated", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            // Panel
            var panel = new GameObject("InventoryPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasGO.transform, false);
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(1, 0);
            prt.anchorMax = new Vector2(1, 0);
            prt.pivot = new Vector2(1, 0);
            prt.anchoredPosition = new Vector2(-10, 10);
            prt.sizeDelta = new Vector2(520, 360);
            panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.45f);

            // Grid
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

            // Slot template
            var slot = new GameObject("SlotTemplate", typeof(RectTransform), typeof(Image));
            slot.transform.SetParent(gridGO.transform, false);
            var srt = slot.GetComponent<RectTransform>();
            srt.sizeDelta = new Vector2(96, 64);
            var sbg = slot.GetComponent<Image>();
            sbg.color = new Color(0.1f, 0.1f, 0.1f, 0.65f);

            // Label
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
            text.font = ResolveFont();
            text.raycastTarget = false;

            // Save prefab
            string path = AssetDatabase.GenerateUniqueAssetPath($"{UiPrefabFolder}/InventoryPanel_Generated.prefab");
            var prefab = PrefabUtility.SaveAsPrefabAsset(canvasGO, path);
            DestroyImmediate(canvasGO);
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
            files = files.Where(ExistsAny).ToList();
            string save = EditorUtility.SaveFilePanel("Export MMO Authoring Package",
                Directory.GetCurrentDirectory(),
                $"MMO_Authoring_{DateTime.Now:yyyyMMdd_HHmm}.unitypackage", "unitypackage");
            if (string.IsNullOrEmpty(save)) return;
            AssetDatabase.ExportPackage(files.ToArray(), save,
                ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies);
            EditorUtility.DisplayDialog("Export Complete", $"Exported:\n{save}", "Great");
        }

        // --- validation & utils ---
        string ValidateUniqueItemId(ItemDef target)
        {
            if (!target) return null;
            string id = (target.itemId ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(id)) return "Item ID cannot be empty.";

            foreach (var it in _itemAssets)
            {
                if (!it || it == target) continue;
                if (string.Equals(it.itemId?.Trim(), target.itemId?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return $"Duplicate itemId found in asset '{it.name}'.";
            }
            return null;
        }

        string ValidateUniqueRecipeId(CraftingRecipeDef target)
        {
            if (!target) return null;
            string id = (target.recipeId ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(id)) return "Recipe ID cannot be empty.";

            foreach (var r in _recipeAssets)
            {
                if (!r || r == target) continue;
                if (string.Equals(r.recipeId?.Trim(), target.recipeId?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return $"Duplicate recipeId found in asset '{r.name}'.";
            }
            return null;
        }

        string MakeUniqueItemId(string baseId)
        {
            string id = (baseId ?? "item").Trim().ToLowerInvariant();
            HashSet<string> taken = _itemAssets.Where(a => a).Select(a => (a.itemId ?? "").Trim().ToLowerInvariant()).ToHashSet();
            if (!taken.Contains(id)) return id;
            int n = 2;
            while (taken.Contains(id + "_" + n)) n++;
            return id + "_" + n;
        }

        string MakeUniqueRecipeId(string baseId)
        {
            string id = (baseId ?? "recipe").Trim().ToLowerInvariant();
            HashSet<string> taken = _recipeAssets.Where(a => a).Select(a => (a.recipeId ?? "").Trim().ToLowerInvariant()).ToHashSet();
            if (!taken.Contains(id)) return id;
            int n = 2;
            while (taken.Contains(id + "_" + n)) n++;
            return id + "_" + n;
        }

        static string Slugify(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "new_item";
            s = s.ToLowerInvariant();
            var chars = s.Select(c =>
                char.IsLetterOrDigit(c) ? c :
                (c == ' ' || c == '-' || c == '_') ? '_' : '\0')
                .Where(c => c != '\0')
                .ToArray();
            var slug = new string(chars).Trim('_');
            return string.IsNullOrEmpty(slug) ? "new_item" : slug;
        }

        static void EnsureFolder(string path)
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

        static bool ExistsAny(string assetPath)
        {
            return Directory.Exists(assetPath) ||
                   File.Exists(assetPath) ||
                   AssetDatabase.FindAssets("", new[] { assetPath }).Length > 0;
        }

        static Font ResolveFont()
        {
            // Try OS font, then built-in legacy (Unity 6)
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
    }
}
#endif
