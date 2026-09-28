using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// OSRS-style combat-style selector. The equipped weapon decides Melee vs Ranged
/// (handled in PlayerEntity.UpdateCombatStyle); this panel lets you pick the STANCE:
///   Melee : Aggressive / Accurate / Shared-Defensive (Controlled)
///   Ranged: Accurate (Distance) / Rapid / Longrange
///
/// Self-building & auto-spawned — open with the "⚔ STYLE" button (top-right) or F7.
/// </summary>
public class CombatStyleUI : MonoBehaviour
{
    public static CombatStyleUI Instance { get; private set; }

    static readonly Color C_Backdrop = new Color(0f, 0f, 0f, 0.40f);
    static readonly Color C_Panel    = new Color(0.13f, 0.12f, 0.10f, 0.99f);
    static readonly Color C_Bar      = new Color(0.20f, 0.17f, 0.12f, 1f);
    static readonly Color C_Gold     = new Color(1f, 0.85f, 0.45f, 1f);
    static readonly Color C_Active   = new Color(0.55f, 0.42f, 0.16f, 1f);
    static readonly Color C_Inactive = new Color(0.18f, 0.17f, 0.14f, 1f);

    // Special-attack bar: green for what you have, red for what you've spent — OSRS's colours.
    static readonly Color C_SpecFull  = new Color(0.28f, 0.75f, 0.28f, 1f);
    static readonly Color C_SpecEmpty = new Color(0.55f, 0.15f, 0.13f, 1f);
    static readonly Color C_SpecReady = new Color(0.22f, 0.40f, 0.22f, 1f);
    static readonly Color C_SpecCold  = new Color(0.20f, 0.19f, 0.16f, 1f);

    GameObject _backdrop;
    GameObject _panel;
    TMP_Text _styleText;
    RectTransform _btnParent;
    readonly List<(Button btn, Image bg, CombatStance stance)> _buttons = new();
    CombatStyle _builtStyle = (CombatStyle)(-1);

    // Special section
    GameObject _specSection;
    Image _specFill;
    Image _specButtonBg;
    TMP_Text _specText;

    CombatManager _combat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("CombatStyleUI (auto)").AddComponent<CombatStyleUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);   // survive scene changes (warp to 3D world etc.)
        EnsureEventSystem();
        BuildUI();
        if (_backdrop) _backdrop.SetActive(false);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F7)) Toggle();
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();

        // Energy refills continuously, so the bar has to follow it rather than wait for an event.
        if (IsOpen) RefreshSpecial();
    }

    public bool IsOpen => _backdrop != null && _backdrop.activeSelf;

    public void Toggle() { if (IsOpen) Close(); else Open(); }

    public void Open()
    {
        if (_combat == null) _combat = CombatManager.Instance;
        RebuildButtonsIfNeeded(true);
        Refresh();
        if (_backdrop) _backdrop.SetActive(true);
        _panel.transform.SetAsLastSibling();
    }

    public void Close() { if (_backdrop) _backdrop.SetActive(false); }

    /// <summary>Called by PlayerEntity when the equipped weapon changes the style.</summary>
    public void Refresh()
    {
        if (_combat == null) _combat = CombatManager.Instance;
        if (_combat == null) return;

        RebuildButtonsIfNeeded(false);

        if (_styleText != null)
            _styleText.text = _combat.Style switch
            {
                CombatStyle.Ranged  => "RANGED STANCES",
                CombatStyle.Fission => "FISSION STANCES",
                _                   => "MELEE STANCES",
            };

        foreach (var (_, bg, stance) in _buttons)
            bg.color = (stance == _combat.Stance) ? C_Active : C_Inactive;

        RefreshSpecial();
    }

    /// <summary>
    /// Drive the special bar from CombatManager's energy pool. Runs every frame while the panel is
    /// open (energy regenerates continuously, so an event-only refresh would look frozen between
    /// ticks). The whole section hides unless gauntlets are equipped — nothing else has a special.
    /// </summary>
    void RefreshSpecial()
    {
        if (_specSection == null || _combat == null) return;

        bool isFission = _combat.Style == CombatStyle.Fission;
        if (_specSection.activeSelf != isFission) _specSection.SetActive(isFission);
        if (!isFission) return;

        float pct = Mathf.Clamp01(_combat.SpecialEnergy / CombatManager.MaxSpecialEnergy);
        if (_specFill != null) _specFill.fillAmount = pct;

        var combat = PlayerEntity.Instance != null
            ? PlayerEntity.Instance.GetComponent<ActionCombat3D>() : null;
        bool ready = combat != null && combat.SpecialReady;

        if (_specButtonBg != null) _specButtonBg.color = ready ? C_SpecReady : C_SpecCold;
        if (_specText != null)
            _specText.text = $"SPECIAL ATTACK   {_combat.SpecialEnergy:F0}%";
    }

    void FireSpecial()
    {
        var combat = PlayerEntity.Instance != null
            ? PlayerEntity.Instance.GetComponent<ActionCombat3D>() : null;
        if (combat == null) return;
        combat.TrySpecial();
        RefreshSpecial();
    }

    void RebuildButtonsIfNeeded(bool force)
    {
        if (_combat == null) _combat = CombatManager.Instance;
        if (_combat == null || _btnParent == null) return;
        if (!force && _builtStyle == _combat.Style) return;
        _builtStyle = _combat.Style;

        foreach (Transform c in _btnParent) Destroy(c.gameObject);
        _buttons.Clear();

        foreach (var stance in CombatManager.StancesFor(_combat.Style))
        {
            var captured = stance;
            var rowGo = NewUI("Stance_" + stance, _btnParent);
            var bg = rowGo.gameObject.AddComponent<Image>();
            bg.color = C_Inactive;
            var le = rowGo.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 46; le.preferredHeight = 46;
            var btn = rowGo.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(() => SetStance(captured));
            _buttons.Add((btn, bg, captured));   // Refresh lights the active stance from this list

            var nameRt = NewUI("Name", rowGo);
            nameRt.anchorMin = new Vector2(0, 0.45f); nameRt.anchorMax = new Vector2(1, 1);
            nameRt.offsetMin = new Vector2(10, 0); nameRt.offsetMax = new Vector2(-10, 0);
            var nameTxt = nameRt.gameObject.AddComponent<TextMeshProUGUI>();
            nameTxt.text = CombatManager.StanceLabel(stance, _combat.Style);
            nameTxt.fontSize = 16; nameTxt.color = Color.white;
            nameTxt.alignment = TextAlignmentOptions.MidlineLeft;
            nameTxt.fontStyle = FontStyles.Bold; nameTxt.raycastTarget = false;

            var subRt = NewUI("Sub", rowGo);
            subRt.anchorMin = new Vector2(0, 0); subRt.anchorMax = new Vector2(1, 0.5f);
            subRt.offsetMin = new Vector2(10, 0); subRt.offsetMax = new Vector2(-10, 0);
            var subTxt = subRt.gameObject.AddComponent<TextMeshProUGUI>();
            subTxt.text = "Trains: " + CombatManager.StanceTrains(stance, _combat.Style);
            subTxt.fontSize = 12.5f; subTxt.color = UITheme.TextDim;
            subTxt.alignment = TextAlignmentOptions.MidlineLeft; subTxt.raycastTarget = false;
        }
    }

    void SetStance(CombatStance stance)
    {
        if (_combat == null) return;
        _combat.SetStance(stance);
        Refresh();
        HUDController.Instance?.AddChatLine(
            $"<color=#FFD24A>[COMBAT]:</color> {CombatManager.StanceLabel(stance, _combat.Style)} stance " +
            $"(trains {CombatManager.StanceTrains(stance, _combat.Style)}).");
    }

    // ── build ────────────────────────────────────────────────────────────
    void BuildUI()
    {
        var canvasGo = new GameObject("CombatStyleCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 530;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // (No floating button — the bottom HUD tab bar's "COMBAT" tab opens this; F7 also toggles it.)

        // backdrop
        var backdropRt = NewUI("Backdrop", canvasGo.transform); Stretch(backdropRt);
        _backdrop = backdropRt.gameObject;
        // Invisible, click-through backdrop — panel stays open while you move/click the world,
        // and clicking off it no longer closes it (OSRS-style persistent panel).
        var bd = _backdrop.AddComponent<Image>();
        bd.color = new Color(0f, 0f, 0f, 0f);
        bd.raycastTarget = false;

        // panel — taller than it was, to seat the special bar under the stance list.
        const float PW = 320, PH = 392;
        const float SpecH = 64;   // height reserved at the bottom for the special section
        var panelRt = NewUI("Panel", backdropRt);
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(1f, 0.5f);   // right-center, matching the Inventory panel
        panelRt.pivot = new Vector2(1f, 0.5f);
        panelRt.sizeDelta = new Vector2(PW, PH);
        panelRt.anchoredPosition = new Vector2(-20f, 0f);
        UITheme.DockSidePanel(panelRt);   // bottom-right above the tab dock, clear of the minimap
        _panel = panelRt.gameObject;
        _panel.AddComponent<Image>().color = C_Panel;
        _panel.AddComponent<Button>().transition = Selectable.Transition.None;

        var bar = NewUI("Bar", panelRt); Place(bar, 0, 0, PW, 34);
        bar.gameObject.AddComponent<Image>().color = C_Bar;
        var titleRt = NewUI("Title", bar); Stretch(titleRt);
        var title = titleRt.gameObject.AddComponent<TextMeshProUGUI>();
        title.text = "COMBAT STYLE"; title.fontSize = 16; title.color = C_Gold;
        title.fontStyle = FontStyles.Bold; title.alignment = TextAlignmentOptions.Center;
        title.raycastTarget = false;

        var closeRt = NewUI("Close", bar); Place(closeRt, PW - 30, 4, 26, 26);
        var closeImg = closeRt.gameObject.AddComponent<Image>();
        closeImg.color = new Color(0.55f, 0.18f, 0.15f, 1f);
        var cbtn = closeRt.gameObject.AddComponent<Button>(); cbtn.targetGraphic = closeImg;
        cbtn.onClick.AddListener(Close);
        var cxRt = NewUI("X", closeRt); Stretch(cxRt);
        var cx = cxRt.gameObject.AddComponent<TextMeshProUGUI>();
        cx.text = "X"; cx.fontSize = 14; cx.alignment = TextAlignmentOptions.Center;
        cx.color = Color.white; cx.raycastTarget = false;

        var styleRt = NewUI("StyleLbl", panelRt); Place(styleRt, 0, 38, PW, 22);
        _styleText = styleRt.gameObject.AddComponent<TextMeshProUGUI>();
        _styleText.text = "MELEE STANCES"; _styleText.fontSize = 13;
        _styleText.color = new Color(0.75f, 0.75f, 0.75f, 1f);
        _styleText.alignment = TextAlignmentOptions.Center; _styleText.raycastTarget = false;

        var listRt = NewUI("Buttons", panelRt);
        Place(listRt, 12, 64, PW - 24, PH - 76 - SpecH);
        _btnParent = listRt;
        var vlg = listRt.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6; vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        BuildSpecialSection(panelRt, PW, PH, SpecH);
    }

    /// <summary>
    /// The special-attack button and its energy bar, pinned under the stance list — RuneScape's
    /// layout, where the spec bar lives at the bottom of the combat tab.
    ///
    /// The bar is a RED background with a GREEN filled Image on top of it, so the red you see is
    /// simply the part the green no longer covers. That is why spending energy reads as the bar
    /// draining rather than as a colour change.
    /// </summary>
    void BuildSpecialSection(RectTransform panelRt, float PW, float PH, float SpecH)
    {
        var sectionRt = NewUI("Special", panelRt);
        Place(sectionRt, 12, PH - SpecH - 8, PW - 24, SpecH);
        _specSection = sectionRt.gameObject;

        // Clickable backing — the whole block is the button.
        _specButtonBg = sectionRt.gameObject.AddComponent<Image>();
        _specButtonBg.color = C_SpecCold;
        var btn = sectionRt.gameObject.AddComponent<Button>();
        btn.targetGraphic = _specButtonBg;
        btn.onClick.AddListener(FireSpecial);

        // Label
        var lblRt = NewUI("Label", sectionRt);
        lblRt.anchorMin = new Vector2(0, 0.48f); lblRt.anchorMax = new Vector2(1, 1);
        lblRt.offsetMin = new Vector2(10, 0); lblRt.offsetMax = new Vector2(-10, -4);
        _specText = lblRt.gameObject.AddComponent<TextMeshProUGUI>();
        _specText.text = "SPECIAL ATTACK   100%";
        _specText.fontSize = 14; _specText.color = Color.white;
        _specText.fontStyle = FontStyles.Bold;
        _specText.alignment = TextAlignmentOptions.Center;
        _specText.raycastTarget = false;

        // Bar background = the RED, always fully drawn.
        var barBgRt = NewUI("BarBg", sectionRt);
        barBgRt.anchorMin = new Vector2(0, 0); barBgRt.anchorMax = new Vector2(1, 0.44f);
        barBgRt.offsetMin = new Vector2(10, 8); barBgRt.offsetMax = new Vector2(-10, 0);
        var barBg = barBgRt.gameObject.AddComponent<Image>();
        barBg.color = C_SpecEmpty;
        barBg.raycastTarget = false;

        // Bar fill = the GREEN, horizontally filled to the current energy.
        var fillRt = NewUI("BarFill", barBgRt); Stretch(fillRt);
        _specFill = fillRt.gameObject.AddComponent<Image>();
        _specFill.color = C_SpecFull;
        _specFill.raycastTarget = false;
        _specFill.type = Image.Type.Filled;
        _specFill.fillMethod = Image.FillMethod.Horizontal;
        _specFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        _specFill.fillAmount = 1f;
        // Unity 6 no longer supplies the legacy UISprite.psd built-in resource.
        _specFill.sprite = Sprite.Create(Texture2D.whiteTexture,
            new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
            new Vector2(0.5f, 0.5f));

        _specSection.SetActive(false);   // only shown for the Fission style
    }

    // ── helpers ──────────────────────────────────────────────────────────
    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es);
        }
    }
}
