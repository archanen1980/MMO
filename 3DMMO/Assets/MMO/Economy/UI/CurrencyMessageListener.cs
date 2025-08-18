using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace MMO.Economy.UI
{
    /// Put this on the Player prefab (the local player).
    /// Posts deltas to CurrencyMessageBox when wallet balances change.
    public class CurrencyMessageListener : NetworkBehaviour
    {
        [Header("Filter")]
        [Tooltip("If set, only these currencies will post messages. Leave empty to allow all.")]
        public MMO.Economy.CurrencyDef[] onlyTheseCurrencies;

        [Tooltip("Show positive deltas (gains)")] public bool showGains = true;
        [Tooltip("Show negative deltas (spends/losses)")] public bool showSpends = true;
        [Tooltip("Ignore base deltas below this threshold.")] public long minAbsoluteAmount = 0;

        [Header("Lookup / Wiring")]
        [Tooltip("Optional registry for id→def lookup; defaults to Resources/CurrencyRegistry")]
        public MMO.Economy.CurrencyRegistry registryOverride;

        [Header("Reliability / Debug")]
        [Tooltip("If > 0, also poll the wallet at this interval and emit deltas (backup if OnChange doesn't fire).")]
        public float fallbackPollSeconds = 0.25f;
        [Tooltip("Log detailed debug info to the Console.")] public bool verbose = false;

        readonly Dictionary<string, long> _last = new();
        MMO.Economy.PlayerWallet _wallet;
        Coroutine _pollCo;

        public override void OnStartClient()
        {
            if (!isLocalPlayer) return;

            TryBindWallet();

            if (_wallet)
            {
                // seed snapshot (might be empty initially; that's OK)
                foreach (var kv in _wallet.balances)
                    _last[kv.Key] = kv.Value;

                _wallet.CurrencyChanged += OnWalletChanged;
                if (verbose) Debug.Log("[CurrencyMessageListener] Subscribed to wallet CurrencyChanged.");
            }

            if (fallbackPollSeconds > 0f)
                _pollCo = StartCoroutine(PollLoop());
        }

        public override void OnStopClient()
        {
            if (_wallet) _wallet.CurrencyChanged -= OnWalletChanged;
            if (_pollCo != null) StopCoroutine(_pollCo);
        }

        void TryBindWallet()
        {
            if (_wallet) return;

            // Try local hierarchy
            _wallet = GetComponent<MMO.Economy.PlayerWallet>()
                   ?? GetComponentInParent<MMO.Economy.PlayerWallet>()
                   ?? GetComponentInChildren<MMO.Economy.PlayerWallet>(true);

            if (!_wallet && verbose)
                Debug.LogWarning("[CurrencyMessageListener] PlayerWallet not found on local player.");
        }

        IEnumerator PollLoop()
        {
            var wait = new WaitForSecondsRealtime(Mathf.Max(0.05f, fallbackPollSeconds));
            while (true)
            {
                yield return wait;
                if (!_wallet)
                {
                    TryBindWallet();
                    continue;
                }

                // Compare snapshot → emit deltas
                foreach (var kv in _wallet.balances)
                    CompareAndEmit(kv.Key, kv.Value);

                // Also handle keys that might have been removed/reset (rare)
                // (No removal expected in this wallet; skip for simplicity)
            }
        }

        void OnWalletChanged(string id, long newValue)
        {
            if (!isLocalPlayer) return;
            CompareAndEmit(id, newValue);
        }

        void CompareAndEmit(string id, long newValue)
        {
            if (!PassesFilter(id)) return;

            long old = _last.TryGetValue(id, out var v) ? v : 0;
            if (newValue == old) return;

            _last[id] = newValue;

            long delta = newValue - old;
            if (System.Math.Abs(delta) < minAbsoluteAmount) return;
            if (delta > 0 && !showGains) return;
            if (delta < 0 && !showSpends) return;

            var reg = registryOverride ? registryOverride : MMO.Economy.CurrencyRegistry.Instance;
            var def = reg ? reg.Find(id) : null;

            if (CurrencyMessageBox.Instance == null)
            {
                if (verbose) Debug.LogWarning("[CurrencyMessageListener] CurrencyMessageBox.Instance is null. Is the UI in the scene?");
                return;
            }

            if (verbose)
                Debug.Log($"[CurrencyMessageListener] delta {id}: {old} -> {newValue} (Δ {delta}). Posting message.");

            CurrencyMessageBox.Instance.PostCurrency(def, delta);
        }

        bool PassesFilter(string id)
        {
            if (onlyTheseCurrencies == null || onlyTheseCurrencies.Length == 0) return true;
            foreach (var c in onlyTheseCurrencies)
                if (c && c.currencyId == id) return true;
            return false;
        }
    }
}
