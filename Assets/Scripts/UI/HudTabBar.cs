using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Self-contained bottom-right tab dock: Inventory, Combat style, Skills, Gear (paper-doll), Music,
/// Modules and Build. Each tab reliably opens/closes its self-built panel, only one panel is up at a
/// time, and the tab of the open panel stays lit. Replaces the flaky scene-wired tab buttons.
/// Auto-spawned, zero setup; styled by UITheme.
/// </summary>
public class HudTabBar : MonoBehaviour
{
    public static HudTabBar Instance { get; private set; }

    const float TabW = 76f, TabH = 54f, Gap = 4f, Pad = 6f;
    static readonly Color C_Icon = new Color(0.80f, 0.77f, 0.70f, 1f);

    class Tab
    {
        public string id;
        public System.Func<bool> isOpen;
        public Graphic icon;
        public TMP_Text label;
        public GameObject lit;     // amber underline shown while this tab's panel is open
        public bool shownOpen;
    }
    Tab[] _tabs;

    GameObject _questPanel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("HudTabBar (auto)").AddComponent<HudTabBar>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);   // survive scene changes (warp to 3D world etc.)
        EnsureEventSystem();
        BuildUI();
    }

    Canvas _dockCanvas;

    void Update()
    {
        // In-world HUD only: hidden on the title / character-creation screens (no player there).
        bool inWorld = PlayerEntity.Instance != null;
        if (_dockCanvas != null && _dockCanvas.enabled != inWorld) _dockCanvas.enabled = inWorld;
        if (_tabs == null || !inWorld) return;
        foreach (var t in _tabs)
        {
            bool open = t.isOpen();
            if (open == t.shownOpen) continue;
            t.shownOpen = open;
            t.lit.SetActive(open);
            t.icon.color  = open ? UITheme.Amber : C_Icon;
            t.label.color = open ? UITheme.Amber : UITheme.Text;
        }
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("HudTabBarCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        _dockCanvas = canvas;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 470;
        UITheme.ApplyScaler(canvasGo.GetComponent<CanvasScaler>(), new Vector2(1920, 1080));

        // Dock, bottom-RIGHT corner.
        var dock = NewUI("Tabs", canvasGo.transform);
        dock.anchorMin = dock.anchorMax = new Vector2(1f, 0f);
        dock.pivot = new Vector2(1f, 0f);
        dock.anchoredPosition = new Vector2(-16, 14);
        dock.sizeDelta = new Vector2(7 * TabW + 6 * Gap + 2 * Pad, TabH + 2 * Pad);
        UITheme.Panel(dock.gameObject.AddComponent<Image>(), 0.92f);
        var hlg = dock.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset((int)Pad, (int)Pad, (int)Pad, (int)Pad);
        hlg.spacing = Gap;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;

        _tabs = new[]
        {
            MakeTab(dock, "INV",     "Inventory (I)", "UI/Icons/backpack", () => InventoryPanelUI.Instance?.Toggle(), () => InventoryPanelUI.Instance != null && InventoryPanelUI.Instance.IsOpen),
            MakeTab(dock, "COMBAT",  "Combat style",  "UI/Icons/pistol",   () => CombatStyleUI.Instance?.Toggle(),    () => CombatStyleUI.Instance != null && CombatStyleUI.Instance.IsOpen),
            MakeTab(dock, "SKILLS",  "Skills (K)",    "UI/Icons/star",     () => SkillsPanelUI.Instance?.Toggle(),    () => SkillsPanelUI.Instance != null && SkillsPanelUI.Instance.IsOpen),
            MakeTab(dock, "GEAR",    "Equipment",     "UI/Icons/helmet",   () => EquipmentPanelUI.Instance?.Toggle(), () => EquipmentPanelUI.Instance != null && EquipmentPanelUI.Instance.IsOpen),
            MakeTab(dock, "MUSIC",   "Music",         null,                () => MusicPanelUI.Instance?.Toggle(),     () => MusicPanelUI.Instance != null && MusicPanelUI.Instance.IsOpen),
            MakeTab(dock, "MODULES", "Gear modules (M)", "UI/Icons/bolt",  () => ModulesPanelUI.Instance?.Toggle(),   () => ModulesPanelUI.Instance != null && ModulesPanelUI.Instance.IsOpen),
            MakeTab(dock, "SETTINGS", "Settings (O)", CogIcon,             () => SettingsUI.Instance?.Toggle(),       () => SettingsUI.Instance != null && SettingsUI.Instance.IsOpen),
        };

        BuildQuestPlaceholder(canvasGo.transform);
    }

    /// <summary>
    /// Toggles the target panel. No longer closes other panels by default,
    /// allowing for multiple windows (e.g. Inventory and Crafting) to stay open.
    /// </summary>
    void ExclusiveToggle(string id, System.Action toggleTarget)
    {
        toggleTarget();
    }

    /// <summary>Closes all tab-bar panels except the one named by <paramref name="except"/>.</summary>
    void CloseOtherPanels(string except)
    {
        if (except != "INV")     InventoryPanelUI.Instance?.Close();
        if (except != "COMBAT")  CombatStyleUI.Instance?.Close();
        if (except != "SKILLS")  SkillsPanelUI.Instance?.Close();
        if (except != "GEAR")    EquipmentPanelUI.Instance?.Close();
        if (except != "MUSIC")   MusicPanelUI.Instance?.Close();
        if (except != "MODULES") ModulesPanelUI.Instance?.Close();
        if (except != "SETTINGS") SettingsUI.Instance?.Close();
        BuildInfoPanelUI.Instance?.Close();   // no tab any more (SETTINGS replaced BUILD), but never leave it open under another
    }

    Tab MakeTab(Transform parent, string label, string tip, string iconPath,
                System.Action toggle, System.Func<bool> isOpen)
    {
        var tab = new Tab { id = label, isOpen = isOpen };

        var rt = NewUI("Tab_" + label, parent);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = TabW;
        var img = rt.gameObject.AddComponent<Image>();
        UITheme.Tile(img);
        img.color = UITheme.TileTint;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var cb = btn.colors;
        cb.normalColor      = Color.white;
        cb.highlightedColor = new Color(1.55f, 1.45f, 1.30f, 1f);   // warm lift on hover
        cb.pressedColor     = new Color(0.80f, 0.80f, 0.80f, 1f);
        cb.selectedColor    = Color.white;                          // don't stay "hovered" after a click
        cb.fadeDuration     = 0.08f;
        btn.colors = cb;
        btn.onClick.AddListener(() => ExclusiveToggle(label, toggle));
        rt.gameObject.AddComponent<TabTooltip>().text = tip;

        // Icon (top) — Synty white silhouette, tinted; Music has no silhouette in the pack, so it's a glyph.
        var iconRt = NewUI("Icon", rt);
        iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 1f);
        iconRt.pivot = new Vector2(0.5f, 1f);
        iconRt.anchoredPosition = new Vector2(0f, -7f);
        iconRt.sizeDelta = new Vector2(24f, 24f);
        var spr = iconPath == CogIcon ? CogSprite() : iconPath != null ? Resources.Load<Sprite>(iconPath) : null;
        if (spr != null)
        {
            var icon = iconRt.gameObject.AddComponent<Image>();
            icon.sprite = spr; icon.preserveAspect = true; icon.raycastTarget = false;
            tab.icon = icon;
        }
        else
        {
            var glyph = iconRt.gameObject.AddComponent<TextMeshProUGUI>();
            var font = UITheme.BodyFont;
            bool hasNote = font != null && font.HasCharacter('♫', true, true);
            glyph.font = font;
            glyph.text = hasNote ? "♫" : "~";
            glyph.fontSize = 24f; glyph.alignment = TextAlignmentOptions.Center;
            glyph.raycastTarget = false;
            glyph.textWrappingMode = TextWrappingModes.NoWrap;
            tab.icon = glyph;
        }
        tab.icon.color = C_Icon;

        // Caption (bottom).
        var lblRt = NewUI("Lbl", rt);
        lblRt.anchorMin = new Vector2(0f, 0f); lblRt.anchorMax = new Vector2(1f, 0f);
        lblRt.pivot = new Vector2(0.5f, 0f);
        lblRt.anchoredPosition = new Vector2(0f, 6f);
        lblRt.sizeDelta = new Vector2(-4f, 16f);
        var lbl = lblRt.gameObject.AddComponent<TextMeshProUGUI>();
        UITheme.Label(lbl, 13f, UITheme.Text);
        lbl.text = label;
        lbl.characterSpacing = 1.5f;
        lbl.alignment = TextAlignmentOptions.Center;
        tab.label = lbl;

        // Lit underline while this tab's panel is open.
        var litRt = NewUI("Lit", rt);
        litRt.anchorMin = new Vector2(0f, 0f); litRt.anchorMax = new Vector2(1f, 0f);
        litRt.pivot = new Vector2(0.5f, 0f);
        litRt.anchoredPosition = new Vector2(0f, 2f);
        litRt.sizeDelta = new Vector2(-16f, 3f);
        var litImg = litRt.gameObject.AddComponent<Image>();
        litImg.sprite = UITheme.RoundFillSprite; litImg.type = Image.Type.Sliced;
        litImg.color = UITheme.Amber; litImg.raycastTarget = false;
        tab.lit = litRt.gameObject;
        tab.lit.SetActive(false);

        return tab;
    }

    // The icon pack has no cog, so the SETTINGS tab draws one: eight teeth around a ring, white like the
    // pack's silhouettes (the tab tints it).
    const string CogIcon = "(cog)";

    static Sprite CogSprite()
    {
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x + 0.5f - N / 2f, dy = y + 0.5f - N / 2f;
                float r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
                float rim = Mathf.Cos(a * 8f) > 0.25f ? 29f : 22f;               // tooth or gap
                float alpha = Mathf.Min(Mathf.Clamp01(rim - r + 0.5f), Mathf.Clamp01(r - 9.5f));
                px[y * N + x] = new Color(1f, 1f, 1f, alpha);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }

    // ── quest/map placeholder ────────────────────────────────────────────
    void ToggleQuest()
    {
        if (_questPanel == null) return;
        bool show = !_questPanel.activeSelf;
        _questPanel.SetActive(show);
        if (show) _questPanel.transform.SetAsLastSibling();
    }

    void BuildQuestPlaceholder(Transform canvas)
    {
        var rt = NewUI("QuestPanel", canvas);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(420, 220);
        rt.anchoredPosition = Vector2.zero;
        _questPanel = rt.gameObject;
        UITheme.Panel(_questPanel.AddComponent<Image>());

        var bar = NewUI("Bar", rt); Place(bar, 0, 0, 420, 34);
        UITheme.Header(bar.gameObject.AddComponent<Image>());
        var titleRt = NewUI("Title", bar); Stretch(titleRt);
        var title = titleRt.gameObject.AddComponent<TextMeshProUGUI>();
        title.text = "QUESTS / MAP"; title.fontSize = 16;
        title.alignment = TextAlignmentOptions.Center;
        title.raycastTarget = false;
        UITheme.Title(title);

        var closeRt = NewUI("Close", bar); Place(closeRt, 420 - 32, 4, 26, 26);
        var closeImg = closeRt.gameObject.AddComponent<Image>();
        closeImg.color = new Color(0.55f, 0.18f, 0.15f, 1f);
        var cbtn = closeRt.gameObject.AddComponent<Button>(); cbtn.targetGraphic = closeImg;
        cbtn.onClick.AddListener(() => _questPanel.SetActive(false));
        var cxRt = NewUI("X", closeRt); Stretch(cxRt);
        var cx = cxRt.gameObject.AddComponent<TextMeshProUGUI>();
        cx.text = "X"; cx.fontSize = 14; cx.alignment = TextAlignmentOptions.Center;
        cx.color = Color.white; cx.raycastTarget = false;

        var bodyRt = NewUI("Body", rt); Place(bodyRt, 20, 50, 380, 150);
        var body = bodyRt.gameObject.AddComponent<TextMeshProUGUI>();
        body.text = "Quest log & full map coming soon.\n\nThis tab will track tutorial objectives " +
                    "(reach level 2 in each skill to open the portal) and later show a zoomed-out map.";
        body.fontSize = 14; body.color = new Color(0.8f, 0.8f, 0.8f, 1f);
        body.alignment = TextAlignmentOptions.TopLeft; body.raycastTarget = false;

        _questPanel.SetActive(false);
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

/// <summary>Hover a tab → its full name (and hotkey) in the shared tooltip.</summary>
public class TabTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public string text;
    public void OnPointerEnter(PointerEventData e) { if (!string.IsNullOrEmpty(text)) TooltipUI.Instance?.Show(text); }
    public void OnPointerExit(PointerEventData e)  => TooltipUI.Instance?.Hide();
}
