using UnityEngine;
using MMO.Visuals; // OutlineController

namespace MMO.Targeting
{
    [RequireComponent(typeof(Collider))]
    public class Targetable : MonoBehaviour
    {
        [Header("Optional Wiring")]
        public OutlineController outline; // auto-found if not assigned

        void Awake()
        {
            if (!outline) outline = GetComponent<OutlineController>() ?? GetComponentInChildren<OutlineController>(true);
        }

        public void SetHighlighted(bool on, Color color, float width = -1f)
        {
            if (!outline) return;
            outline.SetColor(color, applyNow: false);
            if (width >= 0f) outline.SetWidth(width, applyNow: false);
            outline.SetEnabled(on, force: true);
        }
    }
}
