// Assets/MMO/Gameplay/Player/MmoPlayer.cs
using Mirror;
using UnityEngine;
using MMO.Shared;
using System.Collections.Generic;

namespace MMO.Gameplay
{
    /// <summary>
    /// Server-authoritative player with client prediction and interpolation.
    /// - Server simulates movement in FixedUpdate using last MoveIntent from the owner.
    /// - Local client predicts and gently corrects toward authoritative snapshots.
    /// - Remote clients render an interpolation buffer (~100ms back in time).
    /// - CharacterController is enabled only on the server to avoid client-side jitter.
    /// </summary>
    [DisallowMultipleComponent]
    public class MmoPlayer : NetworkBehaviour
    {
        [Header("Movement (server)")]
        public float moveSpeed = 6f;           // m/s
        public float sprintMultiplier = 1.5f;  // server authority
        public float turnSpeed = 360f;         // not used (we set yaw directly from intent)

        [Header("Prediction (local client)")]
        public float localCorrectionLerp = 10f;    // how fast local prediction blends toward server
        public float localSnapDistance = 2.5f;     // snap if error exceeds this (meters)

        [Header("Remote interpolation")]
        [Tooltip("How many seconds to render behind the newest server snapshot.")]
        public float interpBackTime = 0.10f; // 100ms buffer
        public int snapshotBufferSize = 32;

        [Header("Net")]
        [Tooltip("How often to sync state to clients (seconds). ~0.05 = 20Hz")]
        public float stateSendInterval = 0.05f;

        // Authoritative state replicated from server
        [SyncVar(hook = nameof(OnServerPosChanged))] private Vector3 serverPos;
        [SyncVar(hook = nameof(OnServerYawChanged))] private float serverYaw;

        // Last input received from owning client (server-only)
        private MoveIntent _lastIntent;

        // Components
        private CharacterController _cc;

        // --- Local prediction state (client owner) ---
        private Vector3 _predictedPos;
        private float _predictedYaw;

        // --- Interpolation buffer (other clients) ---
        struct Snapshot { public Vector3 pos; public float yaw; public double time; }
        private readonly Queue<Snapshot> _snapshots = new();

        // cache clock
        double Now => NetworkTime.time;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (_cc == null)
            {
                _cc = gameObject.AddComponent<CharacterController>();
                _cc.height = 1.8f; _cc.radius = 0.35f;
            }

            // Throttle SyncVar serialization for smoother net traffic
            syncInterval = stateSendInterval;
        }

        public override void OnStartServer()
        {
            // Initialize server state from current transform
            serverPos = transform.position;
            serverYaw = transform.eulerAngles.y;
            // Server uses the CharacterController
            if (_cc) _cc.enabled = true;
        }

        public override void OnStartClient()
        {
            // Clients do not simulate physics via CC
            if (!isServer && _cc) _cc.enabled = false;

            // Initialize client visual state
            _predictedPos = transform.position;
            _predictedYaw = transform.eulerAngles.y;

            // Seed interpolation buffer with a snapshot to avoid nulls
            PushSnapshot();
        }

        void FixedUpdate()
        {
            if (isServer)
                ServerTick();
        }

        void Update()
        {
            if (isClient)
                ClientTick();
        }

        // ---------------- Server simulation ----------------
        void ServerTick()
        {
            // Normalize yaw
            float yaw = Mathf.Repeat(_lastIntent.yaw, 360f);

            // Direction in world from yaw and WASD axes
            Vector3 fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            Vector3 rgt = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            Vector3 move = Vector3.ClampMagnitude(fwd * _lastIntent.vertical + rgt * _lastIntent.horizontal, 1f);

            float speed = moveSpeed * (_lastIntent.sprint ? sprintMultiplier : 1f);
            Vector3 delta = move * speed * Time.fixedDeltaTime;

            if (_cc && _cc.enabled)
                _cc.Move(delta);
            else
                transform.position += delta;

            transform.rotation = Quaternion.Euler(0, yaw, 0);

            // Write authoritative state (SyncVars)
            serverPos = transform.position;
            serverYaw = yaw;
        }

        // ---------------- Client rendering ----------------
        void ClientTick()
        {
            if (isLocalPlayer)
            {
                // --- Predict using the same rules as server (no gravity yet) ---
                var input = PlayerInputClient.LastSentIntent;
                float yaw = Mathf.Repeat(input.yaw, 360f);
                Vector3 fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                Vector3 rgt = Quaternion.Euler(0, yaw, 0) * Vector3.right;
                Vector3 move = Vector3.ClampMagnitude(fwd * input.vertical + rgt * input.horizontal, 1f);
                float speed = moveSpeed * (input.sprint ? sprintMultiplier : 1f);
                Vector3 delta = move * speed * Time.deltaTime;

                _predictedPos += delta;
                _predictedYaw = yaw;

                // Gentle correction toward latest server state
                float posErr = Vector3.Distance(_predictedPos, serverPos);
                if (posErr > localSnapDistance)
                    _predictedPos = serverPos;
                else
                    _predictedPos = Vector3.Lerp(_predictedPos, serverPos, Time.deltaTime * localCorrectionLerp);

                float yawErr = Mathf.Abs(Mathf.DeltaAngle(_predictedYaw, serverYaw));
                if (yawErr > 60f)
                    _predictedYaw = serverYaw;
                else
                    _predictedYaw = Mathf.LerpAngle(_predictedYaw, serverYaw, Time.deltaTime * (localCorrectionLerp * 0.5f));

                // Apply predicted visual state
                transform.SetPositionAndRotation(_predictedPos, Quaternion.Euler(0, _predictedYaw, 0));
            }
            else
            {
                // --- Remote players: interpolate using a small time buffer ---
                InterpolateRemote();
            }
        }

        void InterpolateRemote()
        {
            double renderTime = Now - interpBackTime;

            // Ensure we have at least two snapshots bracketing the render time
            while (_snapshots.Count >= 2 && _snapshots.Peek().time <= renderTime)
            {
                // drop outdated snapshots but keep one before renderTime
                var first = _snapshots.Dequeue();
                if (_snapshots.Count == 0) { _snapshots.Enqueue(first); break; }
            }

            if (_snapshots.Count == 0)
            {
                // Fallback to latest server state
                transform.SetPositionAndRotation(serverPos, Quaternion.Euler(0, serverYaw, 0));
                return;
            }

            // Find neighbors around renderTime
            Snapshot a = _snapshots.Peek(); // oldest kept
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

        // ---------------- SyncVar hooks (client) ----------------
        void OnServerPosChanged(Vector3 _, Vector3 newPos)
        {
            // Always keep latest serverPos; push a snapshot with the pair
            serverPos = newPos;
            PushSnapshot();
        }

        void OnServerYawChanged(float _, float newYaw)
        {
            serverYaw = newYaw;
            PushSnapshot();
        }

        void PushSnapshot()
        {
            // Combine current serverPos/yaw with a timestamp for interpolation
            _snapshots.Enqueue(new Snapshot { pos = serverPos, yaw = serverYaw, time = Now });
            while (_snapshots.Count > snapshotBufferSize)
                _snapshots.Dequeue();
        }

        // ---------------- Command from local client ----------------
        [Command]
        public void CmdSetMoveIntent(MoveIntent intent)
        {
            intent.horizontal = Mathf.Clamp(intent.horizontal, -1f, 1f);
            intent.vertical = Mathf.Clamp(intent.vertical, -1f, 1f);
            intent.yaw = Mathf.Repeat(intent.yaw, 360f);
            _lastIntent = intent;
        }
    }
}
