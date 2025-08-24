// Assets/MMO/Chat/UI/ChatItemLinkHoverAuto.cs
// Polling-based hover detector for TMP <link="item:..."> inside chat lines.
// - Works even if a parent (e.g., ScrollRect viewport) consumes pointer events.
// - Logs entire message on hover and logs when hovering item links.
// - Shows ItemTooltipCursor ONLY while the pointer is over the item link.

using System;
using System.Text;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MMO.Inventory.UI; // ItemTooltipCursor

namespace MMO.Chat.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class ChatItemLinkHoverAuto : MonoBehaviour
    {
        [Header("ItemDef Lookup (for tooltip payload)")]
        [Tooltip("Folder under Resources/ that contains ItemDef assets.")]
        [SerializeField] private string resourcesItemsFolder = "Items";

        [Header("Hover Logging")]
        [Tooltip("Log when hovering ANY chat line (once per enter by default).")]
        [SerializeField] private bool logOnHoverMessage = true;
        [Tooltip("If true, logs every frame while hovering (spammy).")]
        [SerializeField] private bool logEveryFrameWhileHover = false;
        [Tooltip("Include raw rich-text in hover logs.")]
        [SerializeField] private bool logRawRichText = true;
        [Tooltip("Include visible (tag-stripped) text in hover logs.")]
        [SerializeField] private bool logVisibleText = true;

        [Header("Link Logging")]
        [SerializeField] private bool logItemLinkHover = true;
        [SerializeField] private bool logNonItemLinks = false;

        [Header("Raycast / Canvas")]
        [Tooltip("If your chat canvas is not Overlay, assign its UI camera. (Auto-detected if omitted.)")]
        [SerializeField] private Camera explicitUiCamera;

        private TextMeshProUGUI _tmp;
        private RectTransform _rt;
        private Canvas _canvas;
        private Camera _uiCam;

        private ItemTooltipCursor _tooltip;

        // State (message)
        private bool _isHoveringMessage;
        private int _lastHoverLogFrame = -1;

        // State (link)
        private int _currentLinkIndex = -1;
        private string _currentItemId;
        private int _lastLoggedLinkIndex = -1;
        private string _lastLoggedItemId = null;
        private ItemTooltipCursor.Payload _currentPayload;

        private static readonly StringBuilder s_sb = new(512);

        void Awake()
        {
            _tmp = GetComponent<TextMeshProUGUI>();
            _rt = _tmp.rectTransform;
            _canvas = GetComponentInParent<Canvas>();
            _uiCam = explicitUiCamera
                   ? explicitUiCamera
                   : (_canvas && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null);

            _tmp.richText = true;
            _tmp.raycastTarget = true;

            if (_canvas && _canvas.isRootCanvas && !_canvas.GetComponent<GraphicRaycaster>())
                _canvas.gameObject.AddComponent<GraphicRaycaster>();

            _tooltip = ItemTooltipCursor.Instance ?? FindObjectOfType<ItemTooltipCursor>(true);
        }

        void OnEnable()
        {
            if (!_tooltip) _tooltip = ItemTooltipCursor.Instance ?? FindObjectOfType<ItemTooltipCursor>(true);
        }

        void Update()
        {
            Vector2 mouse = Input.mousePosition;
            bool inside = RectTransformUtility.RectangleContainsScreenPoint(_rt, mouse, _uiCam);

            if (inside)
            {
                if (!_isHoveringMessage)
                {
                    _isHoveringMessage = true;
                    if (logOnHoverMessage && !logEveryFrameWhileHover) LogWholeMessage(mouse, "enter");
                }
                else if (logOnHoverMessage && logEveryFrameWhileHover && Time.frameCount != _lastHoverLogFrame)
                {
                    _lastHoverLogFrame = Time.frameCount;
                    LogWholeMessage(mouse, "move");
                }

                DetectLinkUnderMouse(mouse);
            }
            else
            {
                if (_isHoveringMessage)
                {
                    _isHoveringMessage = false;
                    ClearItemLinkHover();
                    SafeHideTooltip();
                }
            }
        }

        // ---------- Link detection & tooltip ----------

        private void DetectLinkUnderMouse(Vector2 screenPos)
        {
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(_tmp, screenPos, _uiCam);

            if (linkIndex == -1)
            {
                // not over any link: clear state AND hide tooltip immediately
                if (_currentLinkIndex != -1 && logItemLinkHover)
                    Debug.Log("[ChatItemLinkHoverAuto] Exit link.");
                ClearItemLinkHover();
                SafeHideTooltip(); // <-- critical fix so tooltip doesn't persist while inside the message
                return;
            }

            var linkInfo = _tmp.textInfo.linkInfo[linkIndex];
            string linkId = linkInfo.GetLinkID();

            if (linkIndex != _currentLinkIndex)
            {
                _currentLinkIndex = linkIndex;

                if (linkId.StartsWith("item:", StringComparison.OrdinalIgnoreCase))
                {
                    _currentItemId = linkId.Substring("item:".Length);
                    _currentPayload = BuildPayload(_currentItemId);

                    if (logItemLinkHover && (_lastLoggedLinkIndex != linkIndex || _lastLoggedItemId != _currentItemId))
                    {
                        string label = ExtractVisibleLinkLabel(_tmp, linkInfo);
                        Debug.Log($"[ChatItemLinkHoverAuto] Hover ITEM link  id={_currentItemId}  label=\"{label}\"  linkIndex={linkIndex}  mouse={Fmt(screenPos)}");
                        _lastLoggedLinkIndex = linkIndex;
                        _lastLoggedItemId = _currentItemId;
                    }

                    SafeShowTooltipAtCursor();
                }
                else
                {
                    if (logNonItemLinks)
                        Debug.Log($"[ChatItemLinkHoverAuto] Hover NON-ITEM link id=\"{linkId}\"  linkIndex={linkIndex}  mouse={Fmt(screenPos)}");
                    ClearItemLinkHover();
                    SafeHideTooltip();
                }
            }
            // else: still over same link; ItemTooltipCursor follows cursor in LateUpdate
        }

        private void ClearItemLinkHover()
        {
            _currentLinkIndex = -1;
            _currentItemId = null;
        }

        private void SafeShowTooltipAtCursor()
        {
            if (_tooltip == null)
            {
                _tooltip = ItemTooltipCursor.Instance ?? FindObjectOfType<ItemTooltipCursor>(true);
                if (_tooltip == null) return;
            }
            try { _tooltip.ShowAtCursor(_currentPayload); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private void SafeHideTooltip()
        {
            try { _tooltip?.Hide(); } catch { }
        }

        // ---------- Message logging ----------

        private void LogWholeMessage(Vector2 mousePos, string reason)
        {
            s_sb.Length = 0;
            s_sb.Append("[ChatItemLinkHoverAuto] Hover message (").Append(reason).Append(")  mouse=").Append(Fmt(mousePos)).Append('\n');

            if (logRawRichText)
            {
                s_sb.Append(" RAW   : ");
                AppendTrimmed(s_sb, _tmp.text);
                s_sb.Append('\n');
            }

            if (logVisibleText)
            {
                string parsed = GetParsedTextSafe(_tmp);
                if (string.IsNullOrEmpty(parsed))
                    parsed = BuildVisibleFromChars(_tmp);

                s_sb.Append(" VISIBLE: ");
                AppendTrimmed(s_sb, parsed);
                s_sb.Append('\n');
            }

            Debug.Log(s_sb.ToString());
        }

        private static void AppendTrimmed(StringBuilder sb, string txt)
        {
            if (txt == null) { sb.Append("<null>"); return; }
            const int Max = 800;
            if (txt.Length <= Max) { sb.Append(txt); return; }
            sb.Append(txt, 0, Max).Append(" …[+").Append(txt.Length - Max).Append(" chars]");
        }

        private static string GetParsedTextSafe(TMP_Text tmp)
        {
            try
            {
                var m = typeof(TMP_Text).GetMethod("GetParsedText", BindingFlags.Instance | BindingFlags.Public);
                if (m != null) return m.Invoke(tmp, null) as string;
            }
            catch { }
            return null;
        }

        private static string BuildVisibleFromChars(TextMeshProUGUI tmp)
        {
            var ti = tmp.textInfo;
            if (ti == null || ti.characterInfo == null) return "";
            s_sb.Length = 0;
            var arr = ti.characterInfo;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i].isVisible) s_sb.Append(arr[i].character);
            }
            return s_sb.ToString();
        }

        // ---------- Tooltip payload building ----------

        private ItemTooltipCursor.Payload BuildPayload(string itemId)
        {
            var p = new ItemTooltipCursor.Payload { Icon = null, Title = itemId, Body = string.Empty };

            var def = ResolveItemDef(itemId);
            if (def == null) return p;

            p.Icon = GetSprite(def, "icon", "Icon", "sprite", "Sprite");
            p.Title = GetString(def, "displayName", "DisplayName", "title", "Title", "name") ?? itemId;
            p.Body = GetString(def, "description", "Description", "body", "Body", "tooltip", "Tooltip")
                      ?? TryInvokeString(def, "GetTooltip", "BuildTooltip", "GetDescription", "ToTooltip");

            return p;
        }

        private UnityEngine.Object ResolveItemDef(string itemId)
        {
            var itemDefType = Type.GetType("MMO.Shared.Item.ItemDef, Assembly-CSharp", throwOnError: false);
            if (itemDefType == null) return null;

            var direct = Resources.Load($"{resourcesItemsFolder}/{itemId}", itemDefType);
            if (direct) return direct;

            var all = Resources.LoadAll(resourcesItemsFolder, itemDefType);
            if (all != null)
            {
                foreach (var o in all)
                {
                    string id = GetString(o, "itemId", "ItemId", "id", "Id", "itemID", "ItemID") ?? o.name;
                    if (!string.IsNullOrEmpty(id) && string.Equals(id, itemId, StringComparison.OrdinalIgnoreCase))
                        return o;
                }
            }
            return null;
        }

        private static string GetString(object obj, params string[] names)
        {
            if (obj == null) return null;
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = obj.GetType();

            foreach (var n in names)
            {
                var p = t.GetProperty(n, BF);
                if (p != null && p.PropertyType == typeof(string))
                {
                    var v = p.GetValue(obj) as string;
                    if (!string.IsNullOrEmpty(v)) return v;
                }
                var f = t.GetField(n, BF);
                if (f != null && f.FieldType == typeof(string))
                {
                    var v = f.GetValue(obj) as string;
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
            if (obj is UnityEngine.Object uo) return uo.name;
            return null;
        }

        private static Sprite GetSprite(object obj, params string[] names)
        {
            if (obj == null) return null;
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = obj.GetType();

            foreach (var n in names)
            {
                var p = t.GetProperty(n, BF);
                if (p != null && typeof(Sprite).IsAssignableFrom(p.PropertyType))
                    return p.GetValue(obj) as Sprite;

                var f = t.GetField(n, BF);
                if (f != null && typeof(Sprite).IsAssignableFrom(f.FieldType))
                    return f.GetValue(obj) as Sprite;
            }
            return null;
        }

        private static string TryInvokeString(object obj, params string[] methodNames)
        {
            if (obj == null) return null;
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var t = obj.GetType();

            foreach (var n in methodNames)
            {
                var m = t.GetMethod(n, BF, null, Type.EmptyTypes, null);
                if (m != null && m.ReturnType == typeof(string))
                {
                    try
                    {
                        var v = m.Invoke(obj, null) as string;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                    catch { }
                }
            }
            return null;
        }

        private static string ExtractVisibleLinkLabel(TextMeshProUGUI tmp, TMP_LinkInfo linkInfo)
        {
            s_sb.Length = 0;
            int start = linkInfo.linkTextfirstCharacterIndex;
            int end = start + linkInfo.linkTextLength;
            var chars = tmp.textInfo.characterInfo;
            for (int i = start; i < end && i < chars.Length; i++)
                s_sb.Append(chars[i].character);
            return s_sb.ToString();
        }

        private static string Fmt(Vector2 v) => $"({v.x:0.##},{v.y:0.##})";
    }
}
