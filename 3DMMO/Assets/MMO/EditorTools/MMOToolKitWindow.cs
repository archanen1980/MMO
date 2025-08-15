#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MMO.EditorTools
{
    public class MMOToolkitWindow : EditorWindow
    {
        // Menus (also appears in Add Tab)
        [MenuItem("Tools/MMO Starter/MMO Toolkit…")]
        [MenuItem("Window/MMO/MMO Toolkit")]
        public static void Open()
        {
            var w = GetWindow<MMOToolkitWindow>();
            w.titleContent = new GUIContent("MMO Toolkit", EditorGUIUtility.IconContent("d_UnityEditor.InspectorWindow").image);
            w.minSize = new Vector2(980, 560);
            w.DiscoverModules();
            w.Show();
        }

        void OnEnable()
        {
            titleContent = new GUIContent("MMO Toolkit", EditorGUIUtility.IconContent("d_UnityEditor.InspectorWindow").image);
            DiscoverModules();
        }

        // ----- Module plumbing -----
        readonly List<IMMOToolkitModule> _allModules = new();
        readonly List<IMMOToolkitModule> _enabledModules = new();
        IMMOToolkitModule _activeModule;
        bool _showModuleManager = false;

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

        // ----- Persistent ordering -----
        string OrderKey(IMMOToolkitModule m) => $"MMO.Toolkit.ModuleOrder.{m.Id}";
        int LoadOrder(IMMOToolkitModule m) => EditorPrefs.GetInt(OrderKey(m), m.Order);
        void SaveOrderFromEnabled()
        {
            for (int i = 0; i < _enabledModules.Count; i++)
                EditorPrefs.SetInt(OrderKey(_enabledModules[i]), i);
        }

        void DiscoverModules()
        {
            _allModules.Clear();
            _enabledModules.Clear();

            var types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IMMOToolkitModule).IsAssignableFrom(t))
                .ToList();

            foreach (var t in types)
            {
                IMMOToolkitModule m = null;
                try { m = Activator.CreateInstance(t) as IMMOToolkitModule; } catch { }
                if (m == null) continue;

                var attr = t.GetCustomAttribute<ToolkitModuleAttribute>();
                if (attr != null) m.ApplyMeta(attr);

                _allModules.Add(m);
            }

            _allModules.Sort((a, b) => LoadOrder(a).CompareTo(LoadOrder(b)));

            foreach (var m in _allModules)
                if (m.Enabled) _enabledModules.Add(m);

            if (_enabledModules.Count > 0)
            {
                if (_activeModule == null || !_enabledModules.Contains(_activeModule))
                    _activeModule = _enabledModules[0];
            }
            else
            {
                _activeModule = null;
                _showModuleManager = true;
            }

            BuildLeftTabList();
            Repaint();
        }

        // ----- Left tabs via ReorderableList -----
        ReorderableList _tabList;

        void BuildLeftTabList()
        {
            _tabList = new ReorderableList(_enabledModules, typeof(IMMOToolkitModule), /*draggable*/ true, /*header*/ false, /*add*/ false, /*remove*/ false);
            _tabList.headerHeight = 0f;
            _tabList.footerHeight = 0f;
            _tabList.elementHeight = 30f;

            _tabList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (index < 0 || index >= _enabledModules.Count) return;
                var m = _enabledModules[index];

                rect = new Rect(rect.x, rect.y + 2, rect.width, rect.height - 4);

                // highlight active
                if (_activeModule == m && !_showModuleManager)
                    EditorGUI.DrawRect(rect, new Color(0.25f, 0.6f, 0.95f, 0.18f));

                var icon = string.IsNullOrEmpty(m.IconName) ? null : EditorGUIUtility.IconContent(m.IconName).image;
                var content = new GUIContent("  " + m.DisplayName, icon);
                var style = new GUIStyle(EditorStyles.miniButtonLeft)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontStyle = FontStyle.Bold
                };

                // Full-row clickable to select
                if (GUI.Button(rect, content, style))
                {
                    _showModuleManager = false;
                    if (_activeModule != m)
                    {
                        _activeModule?.OnDisable();
                        _activeModule = m;
                        _activeModule.OnEnable();
                    }
                    _tabList.index = index;
                }
            };

            // Keep selection synced
            _tabList.onSelectCallback = (list) =>
            {
                int idx = Mathf.Clamp(list.index, 0, _enabledModules.Count - 1);
                if (idx >= 0)
                {
                    var m = _enabledModules[idx];
                    _showModuleManager = false;
                    if (_activeModule != m)
                    {
                        _activeModule?.OnDisable();
                        _activeModule = m;
                        _activeModule.OnEnable();
                    }
                }
            };

            // Persist order when dragged
            _tabList.onReorderCallback = (list) =>
            {
                SaveOrderFromEnabled();
                Repaint();
            };

            // Ensure correct initial selection
            if (_activeModule != null)
                _tabList.index = _enabledModules.IndexOf(_activeModule);
            else
                _tabList.index = (_enabledModules.Count > 0) ? 0 : -1;
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLeftColumn();
                DrawRightPanel();
            }
        }

        void DrawLeftColumn()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(210)))
            {
                GUILayout.Space(8);
                GUILayout.Label("MMO Toolkit", Header);
                GUILayout.Space(6);

                // Tabs list (drag to reorder)
                if (_tabList == null) BuildLeftTabList();
                _tabList?.DoLayoutList();

                GUILayout.FlexibleSpace();
                EditorGUILayout.Space(4);

                // --- Single persistent Modules button in the original spot ---
                if (GUILayout.Button(new GUIContent("  Modules", EditorGUIUtility.IconContent("d_UnityEditor.InspectorWindow").image),
                                     new GUIStyle(EditorStyles.miniButtonLeft)
                                     { fixedHeight = 30, alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold }))
                {
                    _showModuleManager = true;
                    _activeModule = null;
                }
            }
        }

        void DrawRightPanel()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.Space(6);

                if (_showModuleManager)
                {
                    DrawModuleManager();
                    return;
                }

                if (_activeModule == null)
                {
                    EditorGUILayout.HelpBox("No modules enabled. Click Modules to enable features.", MessageType.Info);
                    return;
                }

                // Protect host GUI state from module side-effects
                var prevEnabled = GUI.enabled;
                var prevColor = GUI.color;
                var prevMatrix = GUI.matrix;
                try
                {
                    _activeModule.OnGUI();
                }
                finally
                {
                    GUI.enabled = prevEnabled;
                    GUI.color = prevColor;
                    GUI.matrix = prevMatrix;
                }
            }
        }

        void DrawModuleManager()
        {
            GUILayout.Label("Modules", Header);
            EditorGUILayout.HelpBox("Enable/disable modules. Drag tabs on the left to reorder. Order persists per user.", MessageType.Info);

            using (new EditorGUILayout.VerticalScope("box"))
            {
                foreach (var m in _allModules)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool was = m.Enabled;
                        bool now = EditorGUILayout.ToggleLeft(
                            new GUIContent(m.DisplayName,
                                string.IsNullOrEmpty(m.IconName) ? null : EditorGUIUtility.IconContent(m.IconName).image),
                            was, GUILayout.MaxWidth(320));

                        GUILayout.FlexibleSpace();
                        GUILayout.Label(m.Id, EditorStyles.miniLabel);

                        if (now != was)
                        {
                            m.Enabled = now;
                            _enabledModules.Clear();
                            foreach (var mod in _allModules) if (mod.Enabled) _enabledModules.Add(mod);

                            // Rebuild list and keep active module sane
                            if (!now && _activeModule == m) _activeModule = _enabledModules.FirstOrDefault();
                            SaveOrderFromEnabled();
                            BuildLeftTabList();
                            Repaint();
                        }
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Enable All", GUILayout.Width(110)))
                {
                    foreach (var m in _allModules) m.Enabled = true;
                    _enabledModules.Clear();
                    _enabledModules.AddRange(_allModules);
                    SaveOrderFromEnabled();
                    BuildLeftTabList();
                }
                if (GUILayout.Button("Disable All", GUILayout.Width(110)))
                {
                    foreach (var m in _allModules) m.Enabled = false;
                    _enabledModules.Clear();
                    SaveOrderFromEnabled();
                    BuildLeftTabList();
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh Modules", GUILayout.Width(140)))
                    DiscoverModules();
            }
        }
    }
}
#endif
