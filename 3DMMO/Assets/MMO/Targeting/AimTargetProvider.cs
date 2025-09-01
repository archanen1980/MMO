using UnityEngine;

namespace MMO.Targeting
{
    /// <summary>
    /// Provides a consistent "aim ray" for interactions & targeting.
    /// Ray originates at the active camera and extends forward.
    /// Ray length = distance(camera -> player) + extraDistance, so zooming still works.
    /// </summary>
    [DisallowMultipleComponent]
    public class AimTargetProvider : MonoBehaviour
    {
        [Header("Auto-bind")]
        [Tooltip("Player/root transform. If omitted, will default to this component's transform.")]
        public Transform player;

        [Tooltip("Camera used for aiming. If omitted, will find Camera.main.")]
        public Camera cam;

        [Tooltip("Optional pivot if you need it for other systems; not required here.")]
        public Transform cameraPivot;

        [Header("Raycast")]
        [Tooltip("Physics layers considered valid for targeting/interaction.")]
        public LayerMask hitMask = ~0;

        [Tooltip("Extra meters added in front of the player beyond the camera distance.")]
        public float extraDistance = 3f;

        [Tooltip("How raycasts should treat trigger colliders.")]
        public QueryTriggerInteraction triggerQuery = QueryTriggerInteraction.Collide;

        [Tooltip("Draw the aim ray every frame for debugging.")]
        public bool debugDrawAlways = false;

        public enum QueryMode { Raycast, SphereCast }

        void Awake() => ForceRefreshRefs();

        /// <summary>Finds player & camera if not explicitly assigned.</summary>
        public void ForceRefreshRefs()
        {
            if (!player)
                player = transform; // assume this component lives on the player

            if (!cam)
                cam = Camera.main ?? FindObjectOfType<Camera>();

            if (!cameraPivot && cam)
                cameraPivot = cam.transform;
        }

        /// <summary>Returns a ray starting at the camera, pointing forward.</summary>
        public Ray GetAimRay()
        {
            var origin = cam ? cam.transform.position : transform.position;
            var dir = cam ? cam.transform.forward : transform.forward;
            return new Ray(origin, dir);
        }

        /// <summary>Distance = camera->player + extraDistance (never negative).</summary>
        public float GetAimDistance()
        {
            var camPos = cam ? cam.transform.position : transform.position;
            var playerPos = player ? player.position : transform.position;
            var baseDist = Vector3.Distance(camPos, playerPos);
            return Mathf.Max(0f, baseDist + Mathf.Max(0f, extraDistance));
        }

        /// <summary>Convenience: perform a physics query using the aim ray.</summary>
        public bool TryGetHit(out RaycastHit hit, float? overrideDistance = null, QueryMode mode = QueryMode.Raycast)
        {
            var ray = GetAimRay();
            float dist = overrideDistance ?? GetAimDistance();

            bool ok;
            if (mode == QueryMode.Raycast)
                ok = Physics.Raycast(ray, out hit, dist, hitMask, triggerQuery);
            else
                ok = Physics.SphereCast(ray, 0.1f, out hit, dist, hitMask, triggerQuery);

            if (debugDrawAlways)
                Debug.DrawRay(ray.origin, ray.direction * dist, ok ? Color.green : Color.red, 0f, false);

            return ok;
        }
    }
}
