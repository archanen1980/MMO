// Assets/MMO/Loot/LootInteractor.cs
using UnityEngine;
using Mirror;
using MMO.Targeting; // only used for optional camera auto-wiring

namespace MMO.Loot
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public class LootInteractor : NetworkBehaviour
    {
        [Header("Auto-Wiring (optional)")]
        [Tooltip("Camera used for aiming. If empty, tries AimTargetProvider.cam, then Camera.main, then any enabled camera.")]
        public Camera cam;

        [Header("Interaction")]
        public KeyCode interactKey = KeyCode.E;

        [Tooltip("Layers considered lootable / interactable.")]
        public LayerMask interactMask = ~0;

        [Tooltip("Sphere radius used for the cast (hits both colliders and triggers).")]
        public float sphereRadius = 0.22f;

        [Tooltip("Extra meters beyond camera->player distance. Keeps feel consistent when zooming.")]
        public float extraMeters = 3f;

        [Header("Server Guards")]
        [Tooltip("Max allowed distance (server) from player to target to prevent cheating.")]
        public float serverMaxDistance = 7f;

        [Header("Debug")]
        public bool drawDebugRayAlways = true;
        public bool drawDebugRayOnPress = true;
        public Color rayColorMiss = new(1f, 1f, 1f, 0.35f);
        public Color rayColorHit = Color.green;

        Transform _player;

        void Start()
        {
            _player = transform;

            // Try to auto-wire camera if not set
            if (!cam)
            {
                var atp = GetComponentInChildren<AimTargetProvider>(true) ?? FindObjectOfType<AimTargetProvider>(true);
                if (atp && atp.cam) cam = atp.cam;
            }
            if (!cam) cam = Camera.main;
            if (!cam)
            {
                var cams = FindObjectsOfType<Camera>();
                foreach (var c in cams) { if (c.isActiveAndEnabled) { cam = c; break; } }
            }

            if (!cam)
                Debug.LogWarning("[LootInteractor] No camera found. Aiming & interaction will fail.");
        }

        void Update()
        {
            if (!HasInputAuthority()) return;

            if (drawDebugRayAlways) DrawDebugRay(false);

            if (Input.GetKeyDown(interactKey))
            {
                if (drawDebugRayOnPress) DrawDebugRay(true);
                Debug.Log("[LootInteractor] E pressed (local=" + isLocalPlayer + ")");
                TryInteractOnce();
            }
        }

        // Mirror-safe input authority for player-controlled objects
        bool HasInputAuthority()
        {
            // For player input, Mirror guarantees isLocalPlayer on the local user's player object.
            return isLocalPlayer;
        }

        void DrawDebugRay(bool pressed)
        {
            if (!cam) return;

            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            float viewToPlayer = _player ? Vector3.Distance(cam.transform.position, _player.position) : 3f;
            float maxDistance = viewToPlayer + extraMeters;

            bool hitSomething = Physics.SphereCast(ray, sphereRadius, out _, maxDistance,
                                                   interactMask, QueryTriggerInteraction.Collide);

            Debug.DrawRay(ray.origin, ray.direction * maxDistance, hitSomething ? rayColorHit : rayColorMiss, pressed ? 0.5f : 0f);
        }

        void TryInteractOnce()
        {
            if (!cam) { Debug.LogWarning("[LootInteractor] No camera assigned/found."); return; }

            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            float viewToPlayer = _player ? Vector3.Distance(cam.transform.position, _player.position) : 3f;
            float maxDistance = viewToPlayer + extraMeters;

            if (!Physics.SphereCast(ray, sphereRadius, out RaycastHit hit, maxDistance,
                                    interactMask, QueryTriggerInteraction.Collide))
            {
                Debug.Log("[LootInteractor] No interactable in front (mask=" + interactMask.value + ").");
                return;
            }

            var loot = hit.collider ? hit.collider.GetComponentInParent<ILootable>() : null;
            if (loot == null)
            {
                Debug.Log("[LootInteractor] Hit " + hit.collider.name + " but no ILootable on it or its parents.");
                return;
            }

            Debug.Log("[LootInteractor] ILootable found on '" + loot + "'. IsAvailable=" + loot.IsAvailable);

            if (!loot.IsAvailable)
            {
                Debug.Log("[LootInteractor] Lootable exists but is not available.");
                return;
            }

            var lootComp = loot as Component;
            if (!lootComp)
            {
                Debug.LogWarning("[LootInteractor] ILootable is not a Component; cannot resolve NetworkIdentity.");
                return;
            }

            var id = lootComp.GetComponentInParent<NetworkIdentity>();
            if (!id)
            {
                Debug.LogWarning("[LootInteractor] Target has no NetworkIdentity. Add one on the same object as ILootable (or a parent).");
                return;
            }

            Debug.Log("[LootInteractor] Sending CmdTryLoot for '" + id.name + "' (netId=" + id.netId + ")");
            CmdTryLoot(id);
        }

        [Command]
        void CmdTryLoot(NetworkIdentity targetId)
        {
            if (!targetId)
            {
                Debug.LogWarning("[Server] CmdTryLoot with null targetId.");
                return;
            }

            var loot = targetId.GetComponentInChildren<ILootable>();
            if (loot == null)
            {
                Debug.LogWarning("[Server] Target has no ILootable anymore: " + targetId.name);
                return;
            }

            if (!loot.IsAvailable)
            {
                Debug.Log("[Server] ILootable found but not available: " + targetId.name);
                return;
            }

            var lootComp = loot as Component;
            if (!lootComp)
            {
                Debug.LogWarning("[Server] ILootable not a Component (unexpected).");
                return;
            }

            float sqr = (lootComp.transform.position - transform.position).sqrMagnitude;
            if (sqr > serverMaxDistance * serverMaxDistance)
            {
                Debug.LogWarning("[Server] Rejecting loot: too far. sqrDist=" + sqr);
                return;
            }

            bool ok = loot.ServerTryLoot(gameObject);
            Debug.Log("[Server] ServerTryLoot(" + targetId.name + ") returned " + ok);
        }
    }
}
