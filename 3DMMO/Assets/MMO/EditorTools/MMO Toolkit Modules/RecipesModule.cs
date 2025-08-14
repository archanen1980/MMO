#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using MMO.Shared.Crafting;
using MMO.Shared.Item;

namespace MMO.EditorTools
{
    [ToolkitModule("recipes", "Crafting Recipes", order: 20, icon: "d_Prefab Icon")]
    public class RecipesModule : MMOToolkitModuleBase
    {
        CraftingRecipeDef[] _recipes = Array.Empty<CraftingRecipeDef>();
        int _index = -1;
        CraftingRecipeDef _selected;
        ReorderableList _inputsList;
        Vector2 _left;

        public override void OnEnable() { Refresh(); }
        public override void OnDisable() { }

        public override void OnGUI()
        {
            EditorGUILayout.LabelField("Recipes (Resources/Recipes)", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();

            // Left list
            EditorGUILayout.BeginVertical(GUILayout.Width(320));
            if (GUILayout.Button("New Recipe", GUILayout.Height(24))) CreateNew();

            using (var sv = new EditorGUILayout.ScrollViewScope(_left))
            {
                _left = sv.scrollPosition;
                for (int i = 0; i < _recipes.Length; i++)
                {
                    var r = _recipes[i];
                    if (!r) continue;
                    using (new EditorGUILayout.HorizontalScope("box"))
                    {
                        if (GUILayout.Toggle(_index == i, $"{r.recipeId}", "Button"))
                        {
                            _index = i;
                            Select(r);
                        }
                    }
                }
            }

            if (_selected)
            {
                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Duplicate", GUILayout.Height(22))) Duplicate(_selected);
                    if (GUILayout.Button("Reveal", GUILayout.Height(22))) EditorGUIUtility.PingObject(_selected);
                }
                if (GUILayout.Button("Delete", GUILayout.Height(22)))
                {
                    if (EditorUtility.DisplayDialog("Delete Recipe",
                        $"Delete recipe '{_selected.recipeId}'?", "Delete", "Cancel"))
                    {
                        AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(_selected));
                        _selected = null; _index = -1;
                        Refresh();
                    }
                }
            }

            EditorGUILayout.EndVertical();

            // Right: inspector
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Recipe Inspector", EditorStyles.boldLabel);

            if (_selected == null)
            {
                EditorGUILayout.HelpBox("Select a recipe on the left, or click 'New Recipe'.", MessageType.Info);
            }
            else
            {
                EditorGUI.BeginChangeCheck();

                using (new EditorGUILayout.HorizontalScope())
                {
                    _selected.recipeId = EditorGUILayout.TextField("Recipe ID", _selected.recipeId);
                    if (GUILayout.Button("Auto", GUILayout.Width(60)))
                    {
                        string baseId = _selected.output?.item ? _selected.output.item.itemId : "recipe";
                        _selected.recipeId = "craft_" + ModuleUtil.Slugify(baseId, "recipe");
                    }
                }

                _selected.stationTag = EditorGUILayout.TextField("Station Tag", _selected.stationTag);
                _selected.craftSeconds = Mathf.Max(0f, EditorGUILayout.FloatField("Craft Seconds", _selected.craftSeconds));

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Inputs", EditorStyles.boldLabel);
                EnsureInputsList();
                _inputsList.DoLayoutList();

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
                if (_selected.output == null) _selected.output = new CraftingRecipeDef.ItemAmount();
                _selected.output.item = (ItemDef)EditorGUILayout.ObjectField("Item", _selected.output.item, typeof(ItemDef), false);
                _selected.output.amount = Mathf.Max(1, EditorGUILayout.IntField("Amount", _selected.output.amount));

                var err = ValidateUniqueId(_selected);
                if (!string.IsNullOrEmpty(err))
                    EditorGUILayout.HelpBox(err, MessageType.Error);
                if (_selected.output == null || _selected.output.item == null)
                    EditorGUILayout.HelpBox("Output item is required.", MessageType.Warning);

                if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(_selected);

                GUILayout.Space(6);
                if (GUILayout.Button("Save", GUILayout.Height(26)))
                {
                    AssetDatabase.SaveAssets();
                    Refresh();
                }
            }

            EditorGUILayout.EndVertical(); // inspector
            EditorGUILayout.EndHorizontal();
        }

        void EnsureInputsList()
        {
            if (_selected == null) return;
            if (_inputsList != null) return;

            SerializedObject so = new SerializedObject(_selected);
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

        void Refresh()
        {
            ModuleUtil.EnsureFolder(ModuleUtil.RecipesFolder);
            string[] guids = AssetDatabase.FindAssets("t:CraftingRecipeDef", new[] { ModuleUtil.RecipesFolder });
            _recipes = guids.Select(g => AssetDatabase.LoadAssetAtPath<CraftingRecipeDef>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            Array.Sort(_recipes, (a, b) => string.Compare(a?.recipeId, b?.recipeId, StringComparison.OrdinalIgnoreCase));
            _inputsList = null;
        }

        void CreateNew()
        {
            ModuleUtil.EnsureFolder(ModuleUtil.RecipesFolder);
            var asset = ScriptableObject.CreateInstance<CraftingRecipeDef>();
            asset.recipeId = MakeUniqueId("new_recipe");
            asset.craftSeconds = 1f;
            asset.inputs = new CraftingRecipeDef.ItemAmount[0];
            asset.output = new CraftingRecipeDef.ItemAmount { amount = 1 };

            string path = AssetDatabase.GenerateUniqueAssetPath($"{ModuleUtil.RecipesFolder}/{asset.recipeId}.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Refresh();

            Select(asset);
            EditorGUIUtility.PingObject(asset);
        }

        void Duplicate(CraftingRecipeDef src)
        {
            if (!src) return;
            ModuleUtil.EnsureFolder(ModuleUtil.RecipesFolder);
            var clone = UnityEngine.Object.Instantiate(src);
            clone.recipeId = MakeUniqueId(src.recipeId);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{ModuleUtil.RecipesFolder}/{clone.recipeId}.asset");
            AssetDatabase.CreateAsset(clone, path);
            AssetDatabase.SaveAssets();
            Refresh();
            Select(clone);
            EditorGUIUtility.PingObject(clone);
        }

        void Select(CraftingRecipeDef r)
        {
            _selected = r;
            _index = Array.IndexOf(_recipes, r);
            _inputsList = null;
        }

        string ValidateUniqueId(CraftingRecipeDef target)
        {
            if (!target) return null;
            string id = (target.recipeId ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(id)) return "Recipe ID cannot be empty.";
            foreach (var r in _recipes)
            {
                if (!r || r == target) continue;
                if (string.Equals(r.recipeId?.Trim(), target.recipeId?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return $"Duplicate recipeId found in asset '{r.name}'.";
            }
            return null;
        }

        string MakeUniqueId(string baseId)
        {
            string id = (baseId ?? "recipe").Trim().ToLowerInvariant();
            var taken = _recipes.Where(a => a).Select(a => (a.recipeId ?? "").Trim().ToLowerInvariant()).ToHashSet();
            if (!taken.Contains(id)) return id;
            int n = 2; while (taken.Contains(id + "_" + n)) n++;
            return id + "_" + n;
        }
    }
}
#endif
