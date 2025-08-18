using Mirror;
using TMPro;
using UnityEngine;

namespace MMO.Economy.UI
{
    public class CurrencyHUDAmount : MonoBehaviour
    {
        public CurrencyDef currency;
        public TMP_Text label;              // e.g., "Honor:"
        public TMP_Text amountText;
        public bool formatted = false;      // if true, uses CurrencyMath.Format()

        PlayerWallet _wallet;

        void OnEnable() { TryBind(); UpdateNow(); }
        void OnDisable() { if (_wallet) _wallet.CurrencyChanged -= OnChanged; }

        void TryBind()
        {
            if (_wallet) return;
            var id = NetworkClient.localPlayer;
            if (!id) return;
            _wallet = id.GetComponent<PlayerWallet>();
            if (_wallet) _wallet.CurrencyChanged += OnChanged;
        }

        void OnChanged(string id, long _) { if (currency && id == currency.currencyId) UpdateNow(); }

        void UpdateNow()
        {
            if (!currency || !_wallet || amountText == null) return;
            long v = _wallet.Get(currency.currencyId);
            amountText.text = formatted ? CurrencyMath.Format(currency, v) : v.ToString();
            if (label) label.text = currency.displayName + ":";
        }
    }
}
