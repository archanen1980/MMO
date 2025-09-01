using System.Collections.Generic;
using UnityEngine;
using MMO.Loot; // ILootable
using MMO.Targeting; // AimTargetProvider
#if MIRROR
using Mirror;
#endif

namespace MMO.Targeting
{
    [DisallowMultipleComponent]
    public class TargetHighlighter : MonoBehaviour
    {
        [Header("Discovery / Optional Provider")]
        [Tooltip("If present, will use this provider for the aim ray. If null, falls back to cam/player below.")]
        public AimTargetProvider provider;

        [Tooltip("Fallback: camera to aim from if no provider.")]
        public Camera cam;

        [Tooltip("Fallback: player transform if no provider.")]
        public Transform player;

        [Header("Ray Settings (fallback only)")]
        [Tooltip("Add this much beyond the camera->player distance (fallback mode).")]
        public float extraDistance = 3f;

        [Tooltip("Layers to hit if no provider is set.")]
        public LayerMask hitMask = ~0;

        [Tooltip("How the fallback raycast should treat trigger colliders.")]
        public QueryTriggerInteraction triggerQuery = QueryTriggerInteraction.Collide;

        [Tooltip("Draw debug rays/colors.")]
        public bool debugRay = true;

        [Header("Outline")]
        [Tooltip("Outline material that draws a rim pass (e.g. your MMO/Outline shader).")]
        public Material outlineMaterial;
        [ColorUsage(true, true)] public Color lootableColor = new Color(0.2f, 1f, 0.6f, 1f);
        [ColorUsage(true, true)] public Color defaultColor = new Color(1f, 0.9f, 0.2f, 1f);
        [Range(0f, 0.05f)] public float outlineWidth = 0.015f;
        [Tooltip("If true, only highlight objects that implement ILootable.")]
        public bool onlyHighlightLootables = true;

        Transform currentTarget;
        readonly List<Renderer> appliedRenderers = new();
        MaterialPropertyBlock mpb;

        static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
        static readonly int WidthId = Shader.PropertyToID("_OutlineWidth");

        void Awake()
        {
            // Try auto-get provider
            if (!provider) provider = GetComponent<AimTargetProvider>();

            if (!cam) cam = Camera.main;

            if (!player)
            {
#if MIRROR
                if (NetworkClient.active && NetworkClient.localPlayer)
                    player = NetworkClient.localPlayer.transform;
#endif
                if (!player)
                {
                    var tagged = GameObject.FindGameObjectWithTag("Player");
                    if (tagged) player = tagged.transform;
                }
            }

            mpb = new MaterialPropertyBlock();
            if (!outlineMaterial)
                Debug.LogWarning("[TargetHighlighter] Please assign an Outline material (e.g., MMO/Outline).");
        }

        void OnDisable() => ClearCurrent();

        void Update()
        {
            // If we have a provider, use its exact ray/mask/trigger config
            if (provider)
            {
                if (provider.TryGetHit(out var hit))
                {
                    HandleHit(hit, provider.GetAimRay(), provider.GetAimDistance());
                }
                else
                {
                    DrawDebug(provider.GetAimRay(), provider.GetAimDistance(), Color.cyan);
                    if (currentTarget) ClearCurrent();
                }
                return;
            }

            // Fallback path (no provider present)
            if (!cam || !player) return;

            float baseDist = Vector3.Distance(cam.transform.position, player.position);
            float maxDist = baseDist + Mathf.Max(0f, extraDistance);
            var ray = new Ray(cam.transform.position, cam.transform.forward);

            if (Physics.Raycast(ray, out var fhit, maxDist, hitMask, triggerQuery))
                HandleHit(fhit, ray, maxDist);
            else
            {
                DrawDebug(ray, maxDist, Color.cyan);
                if (currentTarget) ClearCurrent();
            }
        }

        void HandleHit(RaycastHit hit, Ray rayUsed, float rayLen)
        {
            var hitTransform = hit.collider ? hit.collider.transform : null;
            if (!hitTransform) { DrawDebug(rayUsed, rayLen, Color.red); ClearCurrent(); return; }

            var loot = hitTransform.GetComponentInParent<ILootable>();
            bool isLootable = loot != null;

            // Debug ray colors: green = lootable, yellow = hit non-lootable
            DrawDebug(rayUsed, rayLen, isLootable ? Color.green : Color.yellow);

            if (onlyHighlightLootables && !isLootable)
            {
                if (currentTarget) ClearCurrent();
                return;
            }

            // Prefer the transform of the component that implements ILootable
            Transform outlineRoot = hitTransform.root;
            if (isLootable && loot is Component lootComp && lootComp)
                outlineRoot = lootComp.transform;

            if (outlineRoot != currentTarget)
            {
                ClearCurrent();
                SetCurrent(outlineRoot, isLootable);
            }
        }

        void DrawDebug(Ray ray, float length, Color c)
        {
            if (debugRay) Debug.DrawRay(ray.origin, ray.direction * length, c);
        }

        void SetCurrent(Transform targetRoot, bool isLootable)
        {
            currentTarget = targetRoot;
            if (!currentTarget || !outlineMaterial) return;

            if (isLootable)
                Debug.Log($"[TargetHighlighter] Aiming LOOTABLE: {currentTarget.name} (id {currentTarget.GetInstanceID()})");
            else
                Debug.Log($"[TargetHighlighter] Aiming: {currentTarget.name}");

            currentTarget.GetComponentsInChildren(true, appliedRenderers);
            foreach (var r in appliedRenderers)
                TryAppendOutline(r, isLootable ? lootableColor : defaultColor, outlineWidth);
        }

        void ClearCurrent()
        {
            if (!currentTarget) return;
            foreach (var r in appliedRenderers) TryRemoveOutline(r);
            appliedRenderers.Clear();
            currentTarget = null;
        }

        void TryAppendOutline(Renderer r, Color color, float width)
        {
            if (!r || !outlineMaterial) return;

            var mats = r.sharedMaterials;

            // If outline already present, just update properties
            int idx = System.Array.IndexOf(mats, outlineMaterial);
            if (idx >= 0)
            {
                mpb.Clear();
                r.GetPropertyBlock(mpb, idx);
                mpb.SetColor(ColorId, color);
                mpb.SetFloat(WidthId, width);
                r.SetPropertyBlock(mpb, idx);
                return;
            }

            // Otherwise append the outline material as an extra pass
            var newMats = new Material[mats.Length + 1];
            for (int i = 0; i < mats.Length; i++) newMats[i] = mats[i];
            newMats[^1] = outlineMaterial;
            r.sharedMaterials = newMats;

            mpb.Clear();
            r.GetPropertyBlock(mpb, newMats.Length - 1);
            mpb.SetColor(ColorId, color);
            mpb.SetFloat(WidthId, width);
            r.SetPropertyBlock(mpb, newMats.Length - 1);
        }

        void TryRemoveOutline(Renderer r)
        {
            if (!r || !outlineMaterial) return;

            var mats = r.sharedMaterials;
            int idx = System.Array.IndexOf(mats, outlineMaterial);
            if (idx < 0 || mats.Length <= 1) return;

            var newMats = new Material[mats.Length - 1];
            int k = 0;
            for (int i = 0; i < mats.Length; i++)
                if (i != idx) newMats[k++] = mats[i];
            r.sharedMaterials = newMats;
        }
    }
}
