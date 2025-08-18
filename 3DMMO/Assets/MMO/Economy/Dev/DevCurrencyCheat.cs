using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace MMO.Economy.Dev
{
    /// Local-player developer cheat to add/subtract currencies at runtime.
    /// - Toggle with F10 (configurable)
    /// - Cursor is unlocked & visible while open
    /// - Works with PlayerWallet via server Commands
    /// - Coins: denominated (c/s/g/p) if CurrencyDef assigned; else base "coins" id fallback
    [AddComponentMenu("MMO/Dev/Dev Currency Cheat")]
    public class DevCurrencyCheat : NetworkBehaviour
    {
        [Header("Enable / Security")]
        [Tooltip("Leave OFF for release. In non-dev builds this component disables itself unless this is true.")]
        public bool enableInRelease = false;

        [Tooltip("Keyboard toggle for the cheat window.")]
        public KeyCode toggleKey = KeyCode.F10;

        [Tooltip("Unlock and show hardware cursor while the window is open.")]
        public bool unlockCursorWhenOpen = true;

        [Header("Currencies")]
        [Tooltip("Your denominated coin currency (c/s/g/p). If null, tool uses 'coins' as a base currency id.")]
        public MMO.Economy.CurrencyDef coins;

        [Tooltip("If Coins is null, this id is used as the base-coin key.")]
        public string coinsIdFallback = "coins";

        [Tooltip("Extra currencies (e.g., Honor). Displayed with ± buttons in base units.")]
        public MMO.Economy.CurrencyDef[] specials;

        [Header("Denominated coin step (+ row)")]
        public int copperStep = 10;
        public int silverStep = 1;
        public int goldStep = 1;
        public int platinumStep = 0;

        [Header("Specials step (base units per click)")]
        public long specialStep = 10;

        Rect _win = new Rect(20, 200, 460, 360);
        bool _show;

        MMO.Economy.PlayerWallet _wallet;

        // remember cursor state to restore when closing
        CursorLockMode _prevLock;
        bool _prevVisible;
        bool _cursorSaved;

        // denom indices (ascending list order)
        int _idxCopper = -1, _idxSilver = -1, _idxGold = -1, _idxPlatinum = -1;

        void Start()
        {
#if !UNITY_EDITOR
            if (!enableInRelease && !Debug.isDebugBuild)
            {
                enabled = false; // hard disable in release builds unless explicitly allowed
                return;
            }
#endif
        }

        public override void OnStartClient()
        {
            if (!isLocalPlayer) return;
            TryBindWallet();
            ReindexDenoms();
        }

        void Update()
        {
            if (!isLocalPlayer) return;

            if (Input.GetKeyDown(toggleKey))
            {
                _show = !_show;
                HandleCursorState(_show);
            }

            // If window is open and wallet is still null, keep trying to bind
            if (_show && _wallet == null)
                TryBindWallet();
        }

        void HandleCursorState(bool wantOpen)
        {
            if (!unlockCursorWhenOpen) return;

            if (wantOpen)
            {
                if (!_cursorSaved)
                {
                    _prevLock = Cursor.lockState;
                    _prevVisible = Cursor.visible;
                    _cursorSaved = true;
                }
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                if (_cursorSaved)
                {
                    Cursor.lockState = _prevLock;
                    Cursor.visible = _prevVisible;
                    _cursorSaved = false;
                }
            }
        }

        void OnDestroy()
        {
            // restore if destroyed while open
            HandleCursorState(false);
        }

        void TryBindWallet()
        {
            if (_wallet) return;

            // First try on this object
            _wallet = GetComponent<MMO.Economy.PlayerWallet>();
            if (_wallet) return;

            // Then try parent/children in case your wallet lives elsewhere on the player prefab
            _wallet = GetComponentInParent<MMO.Economy.PlayerWallet>();
            if (_wallet) return;

            _wallet = GetComponentInChildren<MMO.Economy.PlayerWallet>(true);
        }

        void ReindexDenoms()
        {
            _idxCopper = _idxSilver = _idxGold = _idxPlatinum = -1;
            if (!coins || !coins.isDenominated || coins.denominations == null) return;

            for (int i = 0; i < coins.denominations.Count; i++)
            {
                var n = coins.denominations[i].name.ToLowerInvariant();
                if (n.Contains("copper")) _idxCopper = i;
                else if (n.Contains("silver")) _idxSilver = i;
                else if (n.Contains("gold")) _idxGold = i;
                else if (n.Contains("platinum")) _idxPlatinum = i;
            }
        }

        void OnGUI()
        {
            if (!isLocalPlayer || !_show) return;
            _win = GUILayout.Window(GetInstanceID(), _win, DrawWindow, "Dev Currency Cheat");
        }

        void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            // Wallet status/help
            if (_wallet == null)
            {
                GUILayout.Label("<b>PlayerWallet not found on this player.</b>");
                GUILayout.Label("Add PlayerWallet to your player root, or a child object.");
                if (GUILayout.Button("Try Bind Again")) TryBindWallet();
                GUILayout.Space(6);
            }

            // ---- Coins section ----
            string coinsId = coins ? coins.currencyId : coinsIdFallback;
            GUILayout.Space(2);
            GUILayout.Label($"<b>Coins</b> (id: {coinsId})");

            long current = _wallet ? _wallet.Get(coinsId) : 0;

            if (coins && coins.isDenominated)
            {
                GUILayout.Label("Balance: " + MMO.Economy.CurrencyMath.Format(coins, current));
                GUILayout.Space(4);

                // Add row
                GUILayout.BeginHorizontal();
                GUILayout.Label("Add:", GUILayout.Width(40));
                copperStep = IntField("c", copperStep);
                silverStep = IntField("s", silverStep);
                goldStep = IntField("g", goldStep);
                platinumStep = IntField("p", platinumStep);
                GUI.enabled = _wallet != null;
                if (GUILayout.Button("+", GUILayout.Width(30)))
                {
                    long delta = BuildCoinsBaseDelta(copperStep, silverStep, goldStep, platinumStep);
                    if (delta != 0) CmdAdjustCoins(coinsId, delta);
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();

                // Sub row
                GUILayout.BeginHorizontal();
                GUILayout.Label("Sub:", GUILayout.Width(40));
                int sc = IntField("c", 0);
                int ss = IntField("s", 0);
                int sg = IntField("g", 0);
                int sp = IntField("p", 0);
                GUI.enabled = _wallet != null;
                if (GUILayout.Button("-", GUILayout.Width(30)))
                {
                    long delta = BuildCoinsBaseDelta(sc, ss, sg, sp);
                    if (delta != 0) CmdAdjustCoins(coinsId, -delta);
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            else
            {
                // Base-only fallback if no CurrencyDef assigned
                GUILayout.Label($"Balance (base): {current}");
                GUILayout.BeginHorizontal();
                long amt = LongField("±", 100, 100);
                GUI.enabled = _wallet != null;
                if (GUILayout.Button("Add", GUILayout.Width(50))) CmdAdjustCoins(coinsId, amt);
                if (GUILayout.Button("Sub", GUILayout.Width(50))) CmdAdjustCoins(coinsId, -amt);
                GUI.enabled = true;
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.Label("<i>(Tip: assign your Coins CurrencyDef for c/s/g/p steps)</i>");
            }

            // ---- Specials ----
            GUILayout.Space(8);
            GUILayout.Label("<b>Special Currencies</b>");
            if (specials != null && specials.Length > 0)
            {
                foreach (var c in specials)
                {
                    if (!c) continue;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{c.displayName} (id: {c.currencyId})", GUILayout.Width(220));

                    long bal = _wallet ? _wallet.Get(c.currencyId) : 0;
                    GUILayout.Label("Bal: " + bal, GUILayout.Width(120));

                    specialStep = LongField("±", specialStep, 80);

                    GUI.enabled = _wallet != null;
                    if (GUILayout.Button("+", GUILayout.Width(28))) CmdAdjustCurrency(c.currencyId, specialStep);
                    if (GUILayout.Button("-", GUILayout.Width(28))) CmdAdjustCurrency(c.currencyId, -specialStep);
                    GUI.enabled = true;

                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.Label("<i>No specials listed. Add some CurrencyDef assets to 'specials'.</i>");
            }

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Toggle: {toggleKey}", GUILayout.Width(140));
            if (GUILayout.Button("Close")) { _show = false; HandleCursorState(false); }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        // ---------- IMGUI fields ----------
        int IntField(string label, int value, int width = 60)
        {
            GUILayout.Label(label, GUILayout.Width(16));
            var s = GUILayout.TextField(value.ToString(), GUILayout.Width(width));
            return int.TryParse(s, out int v) ? v : value;
        }

        long LongField(string label, long value, int width = 80)
        {
            GUILayout.Label(label, GUILayout.Width(16));
            var s = GUILayout.TextField(value.ToString(), GUILayout.Width(width));
            return long.TryParse(s, out long v) ? v : value;
        }

        long BuildCoinsBaseDelta(int c, int s, int g, int p)
        {
            if (!coins || !coins.isDenominated || coins.denominations == null || coins.denominations.Count == 0)
                return c; // treat as base if not configured

            var units = new List<long>(coins.denominations.Count);
            for (int i = 0; i < coins.denominations.Count; i++) units.Add(0);

            if (_idxCopper >= 0) units[_idxCopper] = c;
            if (_idxSilver >= 0) units[_idxSilver] = s;
            if (_idxGold >= 0) units[_idxGold] = g;
            if (_idxPlatinum >= 0) units[_idxPlatinum] = p;

            return MMO.Economy.CurrencyMath.FromDenoms(coins, units);
        }

        // ---------- Server commands ----------
        [Command]
        void CmdAdjustCoins(string currencyId, long baseDelta)
        {
            if (string.IsNullOrWhiteSpace(currencyId) || baseDelta == 0) return;

            // Resolve wallet on the server from the player's identity (root or children)
            var root = connectionToClient?.identity;
            if (!root) return;

            var wallet = root.GetComponent<MMO.Economy.PlayerWallet>()
                      ?? root.GetComponentInChildren<MMO.Economy.PlayerWallet>(true);
            if (!wallet) return;

            if (baseDelta > 0) wallet.Give(currencyId, baseDelta);
            else wallet.TrySpend(currencyId, System.Math.Abs(baseDelta));
        }

        [Command]
        void CmdAdjustCurrency(string currencyId, long delta)
        {
            if (string.IsNullOrWhiteSpace(currencyId) || delta == 0) return;

            var root = connectionToClient?.identity;
            if (!root) return;

            var wallet = root.GetComponent<MMO.Economy.PlayerWallet>()
                      ?? root.GetComponentInChildren<MMO.Economy.PlayerWallet>(true);
            if (!wallet) return;

            if (delta > 0) wallet.Give(currencyId, delta);
            else wallet.TrySpend(currencyId, System.Math.Abs(delta));
        }
    }
}
