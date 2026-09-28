using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Magazine + reload layer for ranged (Marksmanship) weapons. Rounds still live in the Ammo
/// equipment slot and are consumed one per landed shot exactly as before — the ammo economy and
/// per-tier ammo strength are untouched. The magazine is a TIMING gate on top: each ranged weapon
/// holds ItemData.magazineSize shots (per-tier: pistol 6, rifles 10/14, sniper 5, energy 20), and
/// when it runs dry you reload for ItemData.reloadSeconds (R to reload early; empty mags reload
/// automatically and the queued shot fires when it finishes).
///
/// Magazine state is remembered PER WEAPON, so swapping guns isn't a free reload. Defaults for
/// anything without explicit data: 8 rounds / 1.5 s.
///
/// Self-building (no scene setup): ActionCombat3D adds this beside itself, and the ammo readout
/// (bottom-right: "MAG 6/6 • 240 rounds" + reload progress bar) builds its own canvas, shown only
/// while a ranged weapon is equipped.
/// </summary>
public class WeaponMagazine : MonoBehaviour
{
    public KeyCode reloadKey = KeyCode.R;

    /// <summary>Fires when a reload starts — PlayerAnimator3D plays a reload clip off this.</summary>
    public event System.Action OnReloadStarted;

    public bool IsReloading => _reloading;

    PlayerEntity _pe;
    readonly Dictionary<int, int> _loaded = new();   // weapon id → shots left in its magazine
    bool _reloading;
    int _reloadWeaponId;
    float _reloadStartedAt, _reloadEndsAt;

    // HUD (self-built)
    GameObject _panel;
    TMP_Text _text;
    Image _fill;

    void Awake()
    {
        _pe = GetComponent<PlayerEntity>();
        BuildHud();
    }

    ItemData RangedWeapon()
    {
        var w = _pe != null ? _pe.Equipment.GetItem("Weapon") : null;
        return w != null && w.weaponStyle == WeaponStyle.Ranged ? w : null;
    }

    public static int MagSize(ItemData w)     => w != null && w.magazineSize  > 0  ? w.magazineSize  : 8;
    public static float ReloadTime(ItemData w) => w != null && w.reloadSeconds > 0f ? w.reloadSeconds : 1.5f;

    /// <summary>Shots left in the current weapon's magazine (a never-fired weapon starts full).</summary>
    public int Loaded
    {
        get
        {
            var w = RangedWeapon();
            if (w == null) return 0;
            return _loaded.TryGetValue(w.id, out int n) ? n : MagSize(w);
        }
    }

    /// <summary>Called by ActionCombat3D on every trigger pull. Empty mag auto-reloads.</summary>
    public void NoteShotFired()
    {
        var w = RangedWeapon();
        if (w == null) return;
        _loaded[w.id] = Mathf.Max(0, Loaded - 1);
        if (_loaded[w.id] == 0) TryStartReload(auto: true);
    }

    public bool TryStartReload(bool auto = false)
    {
        var w = RangedWeapon();
        if (w == null || _reloading) return false;
        if (Loaded >= MagSize(w))
        {
            if (!auto) Msg("Magazine's already full.");
            return false;
        }
        if (_pe.Equipment.GetItemId("Ammo") == null || _pe.Equipment.AmmoQuantity <= 0)
        {
            if (!auto) Msg("No rounds to load — put ammo in your Ammo slot.");
            return false;
        }

        _reloading = true;
        _reloadWeaponId = w.id;
        _reloadStartedAt = Time.time;
        _reloadEndsAt = Time.time + ReloadTime(w);
        OnReloadStarted?.Invoke();
        return true;
    }

    void Update()
    {
        if (!ChatInput.IsTyping && Input.GetKeyDown(reloadKey)) TryStartReload();

        if (_reloading)
        {
            var w = RangedWeapon();
            // Swapped the gun away mid-reload → the reload is lost (no free background reloads).
            if (w == null || w.id != _reloadWeaponId) _reloading = false;
            else if (Time.time >= _reloadEndsAt)
            {
                _reloading = false;
                _loaded[w.id] = MagSize(w);
            }
        }

        UpdateHud();
    }

    // ── HUD ──────────────────────────────────────────────────────────────
    void BuildHud()
    {
        var canvasGo = new GameObject("AmmoHUD", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 455;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        _panel = new GameObject("AmmoPanel", typeof(RectTransform), typeof(Image));
        _panel.transform.SetParent(canvasGo.transform, false);
        var prt = _panel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = new Vector2(1f, 0f);
        prt.pivot = new Vector2(1f, 0f);
        prt.anchoredPosition = new Vector2(-18, 90);
        prt.sizeDelta = new Vector2(250, 44);
        var bg = _panel.GetComponent<Image>();
        bg.color = new Color(0.10f, 0.10f, 0.09f, 0.9f);
        bg.raycastTarget = false;

        var barBg = new GameObject("MagBg", typeof(RectTransform), typeof(Image));
        barBg.transform.SetParent(prt, false);
        var brt = barBg.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(1, 0);
        brt.pivot = new Vector2(0.5f, 0f);
        brt.anchoredPosition = new Vector2(0, 6);
        brt.sizeDelta = new Vector2(-16, 12);
        var barBgImg = barBg.GetComponent<Image>();
        barBgImg.color = new Color(0.05f, 0.05f, 0.05f, 1f);
        barBgImg.raycastTarget = false;

        var fillGo = new GameObject("MagFill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(brt, false);
        var frt = fillGo.GetComponent<RectTransform>();
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        _fill = fillGo.GetComponent<Image>();
        _fill.raycastTarget = false;
        _fill.type = Image.Type.Filled;
        _fill.fillMethod = Image.FillMethod.Horizontal;
        _fill.color = new Color(0.95f, 0.75f, 0.30f, 1f);

        var txtGo = new GameObject("Text", typeof(RectTransform));
        txtGo.transform.SetParent(prt, false);
        var trt = txtGo.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0, 0.4f); trt.anchorMax = new Vector2(1, 1);
        trt.offsetMin = new Vector2(8, 0); trt.offsetMax = new Vector2(-8, 0);
        _text = txtGo.AddComponent<TextMeshProUGUI>();
        _text.fontSize = 15;
        _text.color = UITheme.Text;
        _text.alignment = TextAlignmentOptions.Left;
        _text.raycastTarget = false;
        _text.textWrappingMode = TextWrappingModes.NoWrap;      // one line — never spills onto the bar
        _text.overflowMode = TextOverflowModes.Ellipsis;

        _panel.SetActive(false);
    }

    void UpdateHud()
    {
        if (_panel == null) return;
        var w = RangedWeapon();
        bool show = w != null;
        if (_panel.activeSelf != show) _panel.SetActive(show);
        if (!show) return;

        int ammo = _pe.Equipment.GetItemId("Ammo") != null ? _pe.Equipment.AmmoQuantity : 0;

        if (_reloading)
        {
            float t = Mathf.InverseLerp(_reloadStartedAt, _reloadEndsAt, Time.time);
            _fill.fillAmount = t;
            _fill.color = new Color(0.55f, 0.80f, 1f, 1f);
            _text.text = "<color=#9AD1FF>RELOADING…</color>";
        }
        else
        {
            int size = MagSize(w);
            _fill.fillAmount = size > 0 ? (float)Loaded / size : 0f;
            _fill.color = Loaded == 0 ? new Color(0.9f, 0.35f, 0.3f, 1f) : new Color(0.95f, 0.75f, 0.30f, 1f);
            string low = Loaded == 0 ? " — press R" : "";
            _text.text = $"MAG {Loaded}/{size}   <color=#B8B8A8>{ammo} rounds</color>{low}";
        }
    }

    static void Msg(string m) => HUDController.Emit("<color=#FFD24A>[COMBAT]:</color> " + m);
}
