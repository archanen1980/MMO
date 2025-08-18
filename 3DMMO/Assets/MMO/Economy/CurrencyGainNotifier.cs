using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace MMO.Economy.UI
{
    /// Attach to the Player prefab (client object). Shows currency gains as toasts
    /// and/or forwards a string to your chat system via UnityEvent.
    public class CurrencyGainNotifier : NetworkBehaviour
    {
        [Header("Toggle outputs")]
        public bool showToasts = true;
        public bool showSpends = false; // off = only gains
        public CurrencyToastUI toastUI;
        [Tooltip("If assigned, we'll look up CurrencyDef by id here (else try Resources/Registry).")]
        public MMO.Economy.CurrencyRegistry registryOverride;

        readonly Dictionary<string, long> _last = new();

        PlayerWallet _wallet;

        public override void OnStartClient()
        {
            if (!isLocalPlayer) return;

            _wallet = GetComponent<PlayerWallet>();
            if (!_wallet) return;

            // seed last map with current balances
            foreach (var kv in _wallet.balances)
                _last[kv.Key] = kv.Value;

            _wallet.CurrencyChanged += OnCurrencyChanged;
        }

        public override void OnStopClient()
        {
            if (_wallet) _wallet.CurrencyChanged -= OnCurrencyChanged;
        }

        void OnCurrencyChanged(string id, long newValue)
        {
            if (!isLocalPlayer) return;

            long old = _last.TryGetValue(id, out var v) ? v : 0;
            _last[id] = newValue;

            long delta = newValue - old;
            if (delta == 0) return;
            if (delta < 0 && !showSpends) return;

            var reg = registryOverride ? registryOverride : MMO.Economy.CurrencyRegistry.Instance;
            var def = reg ? reg.Find(id) : null;

            string text;
            long absDelta = System.Math.Abs(delta);

            if (def && def.isDenominated)
                text = (delta > 0 ? "+" : "-") + MMO.Economy.CurrencyMath.Format(def, absDelta);
            else if (def)
                text = (delta > 0 ? "+" : "-") + absDelta + (string.IsNullOrEmpty(def.shortCode) ? $" {def.displayName}" : $" {def.shortCode}");
            else
                text = (delta > 0 ? "+" : "-") + absDelta + $" {id}";

            if (showToasts && toastUI) toastUI.Show(text);
        }
    }
}
