// Assets/MMO/Common/UI/UIRaycastDebugger.cs
// Always-on UI raycast HUD: shows the top hit under the mouse and (optionally) the full stack.
// Toggle with F9. Draws an overlay via OnGUI (does not intercept UI).
// Highlights top hit rect in screen space.

using System;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MMO.Common.UI
{
    public class UIRaycastDebugger : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] private bool startVisible = true;
        [SerializeField] private KeyCode toggleKey = KeyCode.F9;
        [SerializeField] private bool showStack = true;
        [SerializeField, Range(1, 32)] private int maxStack = 10;
        [SerializeField] private bool showWhenNotOverUI = true;
        [SerializeField] private bool highlightTopHit = true;

        public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }
        [SerializeField] private Corner anchor = Corner.TopRight;
        [SerializeField] private Vector2 pixelOffset = new Vector2(12, 12);
        [SerializeField] private float updateInterval = 0.05f; // seconds
        [SerializeField] private int fontSize = 12;

        [Header("Advanced")]
        [Tooltip("If true, forces a GraphicRaycaster on root canvases (useful in odd setups).")]
        [SerializeField] private bool ensureGraphicRaycasterOnRootCanvas = true;

        // --- runtime ---
        static readonly List<RaycastResult> s_results = new(32);
        static readonly StringBuilder s_sb = new(2048);
        PointerEventData _ped;

        bool _visible;
        float _nextSampleTime;
        string _hudText = "";
        RaycastResult? _topHit;
        static Texture2D s_lineTex;

        void Awake()
        {
            _visible = startVisible;

            if (ensureGraphicRaycasterOnRootCanvas)
            {
                // Be helpful: ensure root canvases can raycast.
                foreach (var c in FindObjectsOfType<Canvas>())
                    if (c.isRootCanvas && !c.GetComponent<GraphicRaycaster>())
                        c.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey))
                _visible = !_visible;

            if (!_visible) return;

            // Throttle sampling a bit to reduce GC/log spam.
            if (Time.unscaledTime >= _nextSampleTime)
            {
                _nextSampleTime = Time.unscaledTime + Mathf.Max(0.01f, updateInterval);
                Sample();
            }
        }

        void Sample()
        {
            var es = EventSystem.current;
            if (!es)
            {
                _hudText = "<no EventSystem>";
                _topHit = null;
                return;
            }

            // Prepare pointer
            _ped ??= new PointerEventData(es);
            _ped.Reset();
            _ped.position = Input.mousePosition;

            // Raycast
            s_results.Clear();
            es.RaycastAll(_ped, s_results);

            // Build HUD text
            s_sb.Length = 0;

            var sel = es.currentSelectedGameObject;
            if (sel)
                s_sb.Append("Selected: ").Append(Path(sel.transform)).Append('\n');

            if (s_results.Count == 0)
            {
                _topHit = null;
                if (showWhenNotOverUI)
                {
                    s_sb.Append("Over UI: <b>No</b>\n");
                    s_sb.Append("Mouse: ").Append(Vector2ToString(_ped.position)).Append('\n');
                }
                else
                {
                    _hudText = "";
                    return;
                }
            }
            else
            {
                var r = s_results[0];
                _topHit = r;

                s_sb.Append("Over UI: <b>Yes</b>\n");
                s_sb.Append("Mouse: ").Append(Vector2ToString(_ped.position)).Append('\n');

                s_sb.Append("\n<b>Top Hit</b>\n");
                s_sb.Append(" Object : ").Append(Path(r.gameObject.transform)).Append('\n');
                var rt = r.gameObject.transform as RectTransform;
                if (rt)
                {
                    var canvas = rt.GetComponentInParent<Canvas>();
                    if (canvas)
                    {
                        s_sb.Append(" Canvas : ").Append(canvas.name)
                           .Append(" [").Append(canvas.renderMode).Append(']');
                        if (canvas.overrideSorting)
                            s_sb.Append(" sort=").Append(canvas.sortingOrder);
                        s_sb.Append('\n');
                    }
                }
                s_sb.Append(" Module : ").Append(r.module != null ? r.module.GetType().Name : "<null>").Append('\n');
                s_sb.Append(" Distance: ").Append(r.distance.ToString("0.###"))
                   .Append("  Depth: ").Append(r.depth).Append('\n');

                if (showStack)
                {
                    s_sb.Append("\n<b>Stack (").Append(Mathf.Min(maxStack, s_results.Count)).Append('/').Append(s_results.Count).Append(")</b>\n");
                    for (int i = 0; i < s_results.Count && i < maxStack; i++)
                    {
                        var rr = s_results[i];
                        s_sb.Append(i == 0 ? "> " : "  ")
                            .Append(rr.gameObject.name)
                            .Append("  [").Append(rr.module?.GetType().Name ?? "null").Append(']')
                            .Append("  d=").Append(rr.distance.ToString("0.###"))
                            .Append("  depth=").Append(rr.depth)
                            .Append('\n');
                    }
                }
            }

            _hudText = s_sb.ToString();
        }

        void OnGUI()
        {
            if (!_visible) return;

            var style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = fontSize,
                richText = true,
                wordWrap = false,
                normal = { textColor = Color.white }
            };

            Vector2 size = style.CalcSize(new GUIContent(_hudText));
            // allow multi-line
            size.x = Mathf.Min(size.x, 600f);
            size.y = style.CalcHeight(new GUIContent(_hudText), size.x);

            Rect r = AnchorRect(size, anchor, pixelOffset);

            // Background
            var bg = new GUIStyle(GUI.skin.box);
            var oldColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.Box(r, GUIContent.none, bg);
            GUI.color = oldColor;

            // Text
            GUI.Label(new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 12), _hudText, style);

            // Highlight topmost rect
            if (highlightTopHit && _topHit.HasValue)
                DrawHighlightFor(_topHit.Value);
        }

        // --- drawing helpers ---

        void DrawHighlightFor(RaycastResult hit)
        {
            if (!hit.gameObject) return;
            var rt = hit.gameObject.GetComponent<RectTransform>();
            if (!rt) return;

            var canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = null;
            if (canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = canvas.worldCamera;

            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);

            // Convert to screen
            for (int i = 0; i < 4; i++)
                corners[i] = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);

            // Make a rect in screen coords (top-left origin for GUI)
            float xMin = Mathf.Min(corners[0].x, corners[2].x);
            float xMax = Mathf.Max(corners[0].x, corners[2].x);
            float yMin = Mathf.Min(corners[0].y, corners[2].y);
            float yMax = Mathf.Max(corners[0].y, corners[2].y);

            // GUI is Y-down; Screen is Y-up — convert
            float y = Screen.height - yMax;
            var rect = new Rect(xMin, y, xMax - xMin, yMax - yMin);

            DrawRect(rect, new Color(1f, 0.3f, 0.2f, 0.9f), 2f);
        }

        static void DrawRect(Rect r, Color color, float thickness)
        {
            if (s_lineTex == null)
            {
                s_lineTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                s_lineTex.SetPixel(0, 0, Color.white);
                s_lineTex.Apply();
            }

            var old = GUI.color;
            GUI.color = color;

            // top
            GUI.DrawTexture(new Rect(r.xMin, r.yMin, r.width, thickness), s_lineTex);
            // bottom
            GUI.DrawTexture(new Rect(r.xMin, r.yMax - thickness, r.width, thickness), s_lineTex);
            // left
            GUI.DrawTexture(new Rect(r.xMin, r.yMin, thickness, r.height), s_lineTex);
            // right
            GUI.DrawTexture(new Rect(r.xMax - thickness, r.yMin, thickness, r.height), s_lineTex);

            GUI.color = old;
        }

        // --- misc helpers ---

        static string Vector2ToString(Vector2 v) => $"({v.x:0.##}, {v.y:0.##})";

        static string Path(Transform t)
        {
            if (!t) return "<null>";
            var stack = new Stack<string>();
            while (t != null)
            {
                stack.Push(t.name);
                t = t.parent;
            }
            return string.Join("/", stack);
        }

        static Rect AnchorRect(Vector2 size, Corner c, Vector2 offset)
        {
            float x = 0, y = 0;
            switch (c)
            {
                case Corner.TopLeft:
                    x = offset.x;
                    y = offset.y;
                    break;
                case Corner.TopRight:
                    x = Screen.width - size.x - offset.x;
                    y = offset.y;
                    break;
                case Corner.BottomLeft:
                    x = offset.x;
                    y = Screen.height - size.y - offset.y;
                    break;
                case Corner.BottomRight:
                    x = Screen.width - size.x - offset.x;
                    y = Screen.height - size.y - offset.y;
                    break;
            }
            return new Rect(x, y, size.x, size.y);
        }
    }
}
