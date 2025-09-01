using UnityEngine;

namespace MMO.Visuals
{
    [DisallowMultipleComponent]
    public class OutlineController : MonoBehaviour
    {
        [ColorUsage(true, true)]
        public Color outlineColor = Color.cyan;

        [Range(0f, 0.1f)]
        public float outlineWidth = 0.02f;

        [Tooltip("Enable outline when the object spawns?")]
        public bool startEnabled = false;

        Renderer[] _renderers;
        MaterialPropertyBlock _mpb;
        bool _isEnabled;

        void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _mpb = new MaterialPropertyBlock();
        }

        void Start()
        {
            SetEnabled(startEnabled, force: true);
        }

        public void SetEnabled(bool on, bool force = false)
        {
            if (!force && _isEnabled == on) return;
            _isEnabled = on;
            Apply();
        }

        public void SetColor(Color c, bool applyNow = true)
        {
            outlineColor = c;
            if (applyNow && _isEnabled) Apply();
        }

        public void SetWidth(float w, bool applyNow = true)
        {
            outlineWidth = Mathf.Clamp(w, 0f, 0.1f);
            if (applyNow && _isEnabled) Apply();
        }

        void Apply()
        {
            foreach (var r in _renderers)
            {
                if (!r) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor("_OutlineColor", _isEnabled ? outlineColor : Color.clear);
                _mpb.SetFloat("_OutlineWidth", _isEnabled ? outlineWidth : 0f);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
