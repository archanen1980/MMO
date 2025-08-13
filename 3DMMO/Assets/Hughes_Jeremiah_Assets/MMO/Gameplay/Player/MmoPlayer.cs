// Assets/MMO/Gameplay/Player/MmoPlayer.cs
using Mirror;
using UnityEngine;
using MMO.Shared;
using System.Collections.Generic;

namespace MMO.Gameplay
{
    /// <summary>
    /// Server-authoritative player with gravity + jump (coyote/buffer),
    /// client prediction, remote interpolation, auto-fit CharacterController,
    /// and anti-bounce landing (no upward corrections on ground).
    /// </summary>
    [DisallowMultipleComponent]
    public class MmoPlayer : NetworkBehaviour
    {
        [Header("Move (server)")]
        public float moveSpeed = 6f;
        public float sprintMultiplier = 1.5f;

        [Header("Gravity/Jump (server + prediction)")]
        public bool useGravity = true;
        public float gravity = -9.81f;
        public float groundStickForce = -0.2f;
        public float jumpHeight = 1.2f;
        public float coyoteTime = 0.10f;
        public float jumpBuffer = 0.10f;

        // NEW: small window to ignore ground-lock right after takeoff
        [Tooltip("Seconds after issuing jump where client ignores ground lock so takeoff isn't canceled.")]
        public float jumpTakeoffGrace = 0.08f;

        [Header("Prediction (local)")]
        public float localCorrectionLerp = 10f;
        public float localSnapDistance = 2.5f;
        public LayerMask groundMask = ~0;   // include your Ground/Default; exclude Player layer

        [Header("Remote interpolation")]
        public float interpBackTime = 0.10f;     // ~100 ms buffer
        public int snapshotBufferSize = 32;

        [Header("Networking")]
        public float stateSendInterval = 0.05f;  // ~20 Hz

        [Header("Controller Auto-Fit")]
        [Tooltip("Fit controller to child renderers at runtime (handles pivot-at-hips models).")]
        public bool autoFitController = true;
        [Tooltip("Min radius to avoid too-thin capsules.")]
        public float minRadius = 0.25f;
        [Tooltip("Extra lift above ground after nudge (meters).")]
        public float liftEpsilon = 0.02f;

        // Authoritative state (server → clients)
        [SyncVar(hook = nameof(OnServerPosChanged))] private Vector3 serverPos;
        [SyncVar(hook = nameof(OnServerYawChanged))] private float serverYaw;

        // Server-only input/state
        private MoveIntent _lastIntent;
        private float _serverVelY;
        private float _serverCoyoteTimer;
        private float _serverJumpBufferTimer;

        // Components
        private CharacterController _cc;

        // Local prediction
        private Vector3 _predictedPos;
        private float _predictedYaw;
        private float _clientVelY;
        private float _clientCoyoteTimer;
        private float _clientJumpBufferTimer;

        // NEW:
        private float _clientJumpSuppressTimer; // while >0, we treat as airborne on client


        // Interpolation buffer
        struct Snapshot { public Vector3 pos; public float yaw; public double time; }
        private readonly Queue<Snapshot> _snapshots = new();
        double Now => NetworkTime.time;

        void Awake()
        {
            // Only CharacterController should collide
            if (TryGetComponent<CapsuleCollider>(out var cap)) cap.enabled = false;
            if (TryGetComponent<Rigidbody>(out var rb)) { rb.isKinematic = true; rb.detectCollisions = false; }

            _cc = GetComponent<CharacterController>();
            if (_cc == null) _cc = gameObject.AddComponent<CharacterController>();

            // Safe defaults before we fit
            _cc.height = Mathf.Max(1.6f, _cc.height);
            _cc.radius = Mathf.Max(0.35f, _cc.radius);
            _cc.center = new Vector3(0f, _cc.height * 0.5f, 0f);
            _cc.skinWidth = 0.05f;
            _cc.minMoveDistance = 0f;
            _cc.stepOffset = Mathf.Min(0.4f, _cc.height * 0.3f);
            _cc.slopeLimit = 55f;

            if (autoFitController)
                FitControllerToRenderersByFeet(_cc, minRadius);

            syncInterval = stateSendInterval;
        }

        public override void OnStartServer()
        {
            // Place CC bottom onto ground regardless of pivot location
            transform.position = NudgeAboveGround(transform.position, 2.0f, out _);
            serverPos = transform.position;
            serverYaw = transform.eulerAngles.y;

            if (_cc) _cc.enabled = true; // server simulates CC
            _serverVelY = 0f;
            _serverCoyoteTimer = 0f;
            _serverJumpBufferTimer = 0f;
        }

        public override void OnStartClient()
        {
            if (!isServer && _cc) _cc.enabled = false; // clients don't use CC

            _predictedPos = transform.position;
            _predictedYaw = transform.eulerAngles.y;
            _clientVelY = 0f;
            _clientCoyoteTimer = 0f;
            _clientJumpBufferTimer = 0f;

            PushSnapshot();
        }

        void FixedUpdate()
        {
            if (isServer) ServerTick();
        }

        void Update()
        {
            if (isClient) ClientTick();
        }

        // =================== SERVER SIM ===================
        void ServerTick()
        {
            float yaw = Mathf.Repeat(_lastIntent.yaw, 360f);
            Vector3 fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            Vector3 rgt = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            Vector3 moveXZ = Vector3.ClampMagnitude(fwd * _lastIntent.vertical + rgt * _lastIntent.horizontal, 1f);
            float speed = moveSpeed * (_lastIntent.sprint ? sprintMultiplier : 1f);

            bool groundedNow = _cc && _cc.isGrounded;
            if (groundedNow) _serverCoyoteTimer = coyoteTime;
            else _serverCoyoteTimer = Mathf.Max(0f, _serverCoyoteTimer - Time.fixedDeltaTime);

            if (_lastIntent.jump) _serverJumpBufferTimer = jumpBuffer;
            else _serverJumpBufferTimer = Mathf.Max(0f, _serverJumpBufferTimer - Time.fixedDeltaTime);

            if (useGravity)
            {
                if (groundedNow && _serverVelY < 0f) _serverVelY = groundStickForce;
                _serverVelY += gravity * Time.fixedDeltaTime;
            }

            if (_serverJumpBufferTimer > 0f && _serverCoyoteTimer > 0f)
            {
                _serverVelY = Mathf.Sqrt(2f * Mathf.Abs(gravity) * jumpHeight);
                _serverJumpBufferTimer = 0f;
                _serverCoyoteTimer = 0f;
            }

            Vector3 delta = (moveXZ * speed + Vector3.up * _serverVelY) * Time.fixedDeltaTime;

            if (_cc && _cc.enabled) _cc.Move(delta);
            else transform.position += delta;

            if (_cc && _cc.isGrounded && _serverVelY < 0f) _serverVelY = groundStickForce;

            transform.rotation = Quaternion.Euler(0, yaw, 0);
            serverPos = transform.position;
            serverYaw = yaw;
        }

        // =================== CLIENT RENDER ===================
        void ClientTick()
        {
            if (isLocalPlayer)
            {
                var input = PlayerInputClient.LastSentIntent;
                float yaw = Mathf.Repeat(input.yaw, 360f);
                Vector3 fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                Vector3 rgt = Quaternion.Euler(0, yaw, 0) * Vector3.right;
                Vector3 moveXZ = Vector3.ClampMagnitude(fwd * input.vertical + rgt * input.horizontal, 1f);
                float speed = moveSpeed * (input.sprint ? sprintMultiplier : 1f);

                // --- Client ground probe near controller bottom ---
                float bottomLocal = _cc.center.y - _cc.height * 0.5f;
                Vector3 probeStart = _predictedPos + Vector3.up * (bottomLocal + 0.25f);
                bool probeGrounded = Physics.SphereCast(
                    probeStart, 0.22f, Vector3.down, out RaycastHit groundHit, 0.30f, groundMask, QueryTriggerInteraction.Ignore
                );

                // NEW: takeoff grace — ignore ground for a short window after jump
                if (_clientJumpSuppressTimer > 0f)
                    _clientJumpSuppressTimer = Mathf.Max(0f, _clientJumpSuppressTimer - Time.deltaTime);

                bool clientGrounded = (_clientJumpSuppressTimer > 0f) ? false : probeGrounded;

                // Coyote / buffer timers
                if (clientGrounded) _clientCoyoteTimer = coyoteTime;
                else _clientCoyoteTimer = Mathf.Max(0f, _clientCoyoteTimer - Time.deltaTime);

                if (input.jump) _clientJumpBufferTimer = jumpBuffer;
                else _clientJumpBufferTimer = Mathf.Max(0f, _clientJumpBufferTimer - Time.deltaTime);

                // Gravity + jump (prediction)
                if (useGravity)
                {
                    if (clientGrounded && _clientVelY < 0f) _clientVelY = groundStickForce;

                    // IMPORTANT: do NOT cancel upward velocity during takeoff grace
                    if (clientGrounded && _clientJumpSuppressTimer <= 0f && _clientVelY > 0f)
                        _clientVelY = 0f;

                    _clientVelY += gravity * Time.deltaTime;
                }

                // Resolve jump (prediction)
                if (_clientJumpBufferTimer > 0f && _clientCoyoteTimer > 0f)
                {
                    _clientVelY = Mathf.Sqrt(2f * Mathf.Abs(gravity) * jumpHeight);
                    _clientJumpBufferTimer = 0f;
                    _clientCoyoteTimer = 0f;

                    // NEW: start grace so the first upward frame isn't clamped by ground
                    _clientJumpSuppressTimer = jumpTakeoffGrace;
                    clientGrounded = false; // force airborne this frame
                }

                Vector3 delta = (moveXZ * speed + Vector3.up * _clientVelY) * Time.deltaTime;
                _predictedPos += delta;
                _predictedYaw = yaw;

                // --- Correction toward authoritative (with anti-bounce on ground) ---
                float posErr = Vector3.Distance(_predictedPos, serverPos);
                Vector3 blended = posErr > localSnapDistance
                    ? serverPos
                    : Vector3.Lerp(_predictedPos, serverPos, Time.deltaTime * localCorrectionLerp);

                if (clientGrounded)
                {
                    // Downward-only correction on Y when grounded
                    if (blended.y > _predictedPos.y) blended.y = _predictedPos.y;

                    // Snap feet to ground
                    float targetY = groundHit.point.y - bottomLocal + (_cc.skinWidth + liftEpsilon);
                    if (_predictedPos.y < targetY) blended.y = targetY; // pull up if under
                    else if (_predictedPos.y > targetY)                  // settle downward only
                        blended.y = Mathf.Lerp(_predictedPos.y, targetY, Time.deltaTime * 25f);
                }

                _predictedPos = blended;

                float yawErr = Mathf.Abs(Mathf.DeltaAngle(_predictedYaw, serverYaw));
                _predictedYaw = yawErr > 60f
                    ? serverYaw
                    : Mathf.LerpAngle(_predictedYaw, serverYaw, Time.deltaTime * (localCorrectionLerp * 0.5f));

                transform.SetPositionAndRotation(_predictedPos, Quaternion.Euler(0, _predictedYaw, 0));
            }
            else
            {
                InterpolateRemote();
            }
        }


        void InterpolateRemote()
        {
            double renderTime = Now - interpBackTime;

            while (_snapshots.Count >= 2 && _snapshots.Peek().time <= renderTime)
            {
                var first = _snapshots.Dequeue();
                if (_snapshots.Count == 0) { _snapshots.Enqueue(first); break; }
            }
            if (_snapshots.Count == 0)
            {
                transform.SetPositionAndRotation(serverPos, Quaternion.Euler(0, serverYaw, 0));
                return;
            }

            Snapshot a = _snapshots.Peek();
            Snapshot b = a;
            foreach (var s in _snapshots)
            {
                if (s.time < renderTime) { a = s; continue; }
                b = s; break;
            }

            float t = 0f;
            double dt = (b.time - a.time);
            if (dt > 0.0001) t = Mathf.Clamp01((float)((renderTime - a.time) / dt));

            Vector3 pos = Vector3.Lerp(a.pos, b.pos, t);
            float yaw = Mathf.LerpAngle(a.yaw, b.yaw, t);

            transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        }

        // ---- SyncVar hooks ----
        void OnServerPosChanged(Vector3 _, Vector3 newPos) { serverPos = newPos; PushSnapshot(); }
        void OnServerYawChanged(float _, float newYaw) { serverYaw = newYaw; PushSnapshot(); }

        void PushSnapshot()
        {
            _snapshots.Enqueue(new Snapshot { pos = serverPos, yaw = serverYaw, time = Now });
            while (_snapshots.Count > snapshotBufferSize) _snapshots.Dequeue();
        }

        // ---- Commands ----
        [Command]
        public void CmdSetMoveIntent(MoveIntent intent)
        {
            intent.horizontal = Mathf.Clamp(intent.horizontal, -1f, 1f);
            intent.vertical = Mathf.Clamp(intent.vertical, -1f, 1f);
            intent.yaw = Mathf.Repeat(intent.yaw, 360f);
            _lastIntent = intent; // jump edge handled via buffers/coyote
        }

        // --------- Auto-fit & Grounding helpers ---------

        // Fits CC so its bottom sits at the LOWEST point of the visible renderers.
        void FitControllerToRenderersByFeet(CharacterController cc, float minR)
        {
            var rends = GetComponentsInChildren<Renderer>(includeInactive: true);
            if (rends == null || rends.Length == 0) return;

            Bounds w = new Bounds(rends[0].bounds.center, Vector3.zero);
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] is ParticleSystemRenderer) continue;
                w.Encapsulate(rends[i].bounds);
            }

            // World → local
            float feetWorldY = w.min.y;
            float headWorldY = w.max.y;
            float modelHeight = Mathf.Max((headWorldY - feetWorldY), 1.4f);

            // Radius from XZ size
            float radiusFromXZ = 0.5f * Mathf.Min(w.size.x, w.size.z);
            float radius = Mathf.Max(minR, radiusFromXZ);

            // Ensure height >= 2*radius + epsilon
            float height = Mathf.Max(modelHeight, 2f * radius + 0.05f);

            // Local Y of feet relative to pivot
            float feetLocalY = transform.InverseTransformPoint(new Vector3(0f, feetWorldY, 0f)).y;

            // Center so that bottomLocal == feetLocalY
            float centerY = feetLocalY + height * 0.5f;

            cc.height = height;
            cc.radius = radius;
            cc.center = new Vector3(0f, centerY, 0f);

            cc.skinWidth = Mathf.Clamp(cc.skinWidth, 0.03f, 0.08f);
            cc.stepOffset = Mathf.Min(0.5f, cc.height * 0.3f);
            cc.slopeLimit = 55f;
        }

        // Casts down and positions so CC bottom rests on ground + epsilon
        Vector3 NudgeAboveGround(Vector3 start, float upDistance, out bool hitGround)
        {
            Vector3 from = start + Vector3.up * upDistance;
            if (Physics.Raycast(from, Vector3.down, out var hit, upDistance + 5f, groundMask, QueryTriggerInteraction.Ignore))
            {
                hitGround = true;
                float bottomLocal = _cc.center.y - _cc.height * 0.5f;
                float desiredY = hit.point.y - bottomLocal + (_cc.skinWidth + liftEpsilon);
                return new Vector3(start.x, desiredY, start.z);
            }
            hitGround = false;
            return start;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (_cc == null) return;
            Gizmos.color = Color.green;
            float bottomLocal = _cc.center.y - _cc.height * 0.5f;
            Vector3 bottomWorld = transform.position + new Vector3(0f, bottomLocal, 0f);
            Gizmos.DrawLine(bottomWorld + Vector3.left * 0.25f, bottomWorld + Vector3.right * 0.25f);
            Gizmos.DrawLine(bottomWorld + Vector3.forward * 0.25f, bottomWorld + Vector3.back * 0.25f);
        }
#endif
    }
}
