// Assets/MMO/Gameplay/Camera/ThirdPersonCameraLocal.cs
using Mirror;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// Third-person camera for the *local* player that always stays behind the player:
    /// - Player rotates with Mouse X (handled in PlayerInputClient).
    /// - Camera yaw locks to the player's yaw (smoothly) so it stays behind.
    /// - Mouse Y = pitch, Wheel = zoom, with spherecast collision.
    ///
    /// Tip: Uses PlayerInputClient.LastSentIntent.yaw for snappy alignment,
    /// falling back to transform.eulerAngles.y.
    /// </summary>
    [DisallowMultipleComponent]
    public class ThirdPersonCameraLocal : NetworkBehaviour
    {
        [Header("Target & Offsets")]
        public Vector3 pivotLocalPosition = new Vector3(0f, 1.6f, 0f);

        [Header("Pitch")]
        [Range(-89f, 0f)] public float minPitch = -40f;
        [Range(0f, 89f)] public float maxPitch = 65f;
        public float pitchSensitivity = 1.5f;
        public bool invertY = false;
        float _pitch;

        [Header("Zoom")]
        public float distance = 4.5f;
        public float minDistance = 1.75f;
        public float maxDistance = 7.0f;
        public float zoomSensitivity = 2.0f;
        float _currentDistance;

        [Header("Yaw Follow (keeps camera behind player)")]
        [Tooltip("How quickly the camera aligns behind the player.")]
        public float followYawLerp = 18f;
        [Tooltip("Snap instantly if yaw error exceeds this (helps after teleports).")]
        public float snapIfErrorGreaterThan = 95f;
        float _orbitYaw; // camera yaw around player

        [Header("Collision")]
        public float collisionRadius = 0.25f;
        public LayerMask collisionMask = ~0; // exclude the Player layer if needed

        [Header("Smoothing")]
        public float distanceLerp = 12f;
        public float pivotLerp = 14f;

        // runtime
        Camera _cam;
        Transform _camT;
        Transform _pivotSmoothed;

        public override void OnStartLocalPlayer()
        {
            if (!isLocalPlayer) return;

            // Reuse main camera or create one
            _cam = Camera.main != null
                ? Camera.main
                : new GameObject("PlayerCamera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            _camT = _cam.transform;
            if (_cam.GetComponent<AudioListener>() == null) _cam.gameObject.AddComponent<AudioListener>();
            _cam.tag = "MainCamera";

            // Initialize from player's current yaw
            float startYaw = GetPlayerYawImmediate();
            _orbitYaw = startYaw;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            _currentDistance = Mathf.Clamp(distance, minDistance, maxDistance);

            // Smoothed world-space pivot to follow head height
            _pivotSmoothed = new GameObject("CameraPivotRuntime").transform;
            _pivotSmoothed.position = transform.TransformPoint(pivotLocalPosition);
            _pivotSmoothed.rotation = transform.rotation;

            ApplyCameraTransform(true);
        }

        void Update()
        {
            if (!isLocalPlayer || _camT == null) return;

            // Mouse Y → pitch
            float my = Input.GetAxis("Mouse Y");
            if (Mathf.Abs(my) > 0.0001f)
            {
                float dir = invertY ? 1f : -1f;
                _pitch = Mathf.Clamp(_pitch + my * pitchSensitivity * dir, minPitch, maxPitch);
            }

            // Wheel → zoom
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
                distance = Mathf.Clamp(distance - scroll * zoomSensitivity, minDistance, maxDistance);
            _currentDistance = Mathf.Lerp(_currentDistance, distance, Time.deltaTime * distanceLerp);

            // Yaw follow (lock behind player)
            float targetYaw = GetPlayerYawImmediate();
            float err = Mathf.Abs(Mathf.DeltaAngle(_orbitYaw, targetYaw));
            if (err > snapIfErrorGreaterThan)
                _orbitYaw = targetYaw;
            else
                _orbitYaw = Mathf.LerpAngle(_orbitYaw, targetYaw, Time.deltaTime * followYawLerp);

            ApplyCameraTransform(false);
        }

        float GetPlayerYawImmediate()
        {
            // Prefer last intent yaw for snappy follow (local-only); fallback to current transform yaw.
            float yawFromIntent = PlayerInputClient.LastSentIntent.yaw;
            if (!float.IsNaN(yawFromIntent) && !float.IsInfinity(yawFromIntent))
                return Mathf.Repeat(yawFromIntent, 360f);

            return transform.eulerAngles.y;
        }

        void ApplyCameraTransform(bool instant)
        {
            // Smooth pivot follow at head height
            Vector3 targetPivot = transform.TransformPoint(pivotLocalPosition);
            _pivotSmoothed.position = instant
                ? targetPivot
                : Vector3.Lerp(_pivotSmoothed.position, targetPivot, Time.deltaTime * pivotLerp);

            // Orbit orientation from pitch + locked yaw
            Quaternion orbitRot = Quaternion.Euler(_pitch, _orbitYaw, 0f);
            Vector3 desired = _pivotSmoothed.position + orbitRot * (Vector3.back * _currentDistance);

            // Collision (spherecast)
            Vector3 from = _pivotSmoothed.position;
            Vector3 dir = desired - from;
            float dist = dir.magnitude;
            if (dist > 1e-4f)
            {
                dir /= dist;
                if (Physics.SphereCast(from, collisionRadius, dir, out var hit, dist, collisionMask, QueryTriggerInteraction.Ignore))
                {
                    float safe = Mathf.Max(hit.distance - 0.05f, minDistance);
                    desired = from + dir * safe;
                }
            }

            _camT.position = instant ? desired : Vector3.Lerp(_camT.position, desired, Time.deltaTime * distanceLerp);
            _camT.rotation = Quaternion.LookRotation(_pivotSmoothed.position - _camT.position, Vector3.up);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.TransformPoint(pivotLocalPosition), 0.08f);
        }
#endif
    }
}
