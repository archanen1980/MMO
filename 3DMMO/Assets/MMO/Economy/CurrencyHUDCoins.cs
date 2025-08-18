using System.Linq;
using Mirror;
using TMPro;
using UnityEngine;

namespace MMO.Economy.UI
{
    /// Bind this to your inventory HUD. Assign the coins CurrencyDef and 4 TMP_Text fields.
    public class CurrencyHUDCoins : MonoBehaviour
    {
        public CurrencyDef coins; // denominated: copper/silver/gold/platinum
        public TMP_Text copperText, silverText, goldText, platinumText;

        PlayerWallet _wallet;

        void OnEnable()
        {
            TryBind();
            UpdateNow();
        }

        void OnDisable()
        {
            if (_wallet) _wallet.CurrencyChanged -= OnCurrencyChanged;
        }

        void TryBind()
        {
            if (_wallet) return;
            var id = NetworkClient.localPlayer;
            if (!id) return;
            _wallet = id.GetComponent<PlayerWallet>();
            if (_wallet) _wallet.CurrencyChanged += OnCurrencyChanged;
        }

        void OnCurrencyChanged(string id, long newValue)
        {
            if (coins && id == coins.currencyId) UpdateNow();
        }

        void UpdateNow()
        {
            if (!coins || !_wallet) return;
            long baseCopper = _wallet.Get(coins.currencyId);
            var denoms = CurrencyMath.ToDenoms(coins, baseCopper); // ascending (c,s,g,p)

            long c = denoms.ElementAtOrDefault(0);
            long s = denoms.ElementAtOrDefault(1);
            long g = denoms.ElementAtOrDefault(2);
            long p = denoms.ElementAtOrDefault(3);

            if (copperText) copperText.text = c.ToString();
            if (silverText) silverText.text = s.ToString();
            if (goldText) goldText.text = g.ToString();
            if (platinumText) platinumText.text = p.ToString();
        }
    }
}
