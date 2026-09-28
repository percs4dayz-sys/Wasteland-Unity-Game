using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// OSRS-style skills panel: a grid of every skill with its level and XP-to-next bar.
/// Click a skill to open its guide (per-level unlocks via SkillGuideUI).
/// Self-building & auto-spawned. Toggle from the HUD tab bar or with K.
/// </summary>
public class SkillsPanelUI : MonoBehaviour
{
    public static SkillsPanelUI Instance { get; private set; }

    // The skill the player has pinned to track on the HUD (right-click a skill to toggle).
    public static Skill TrackedSkill { get; private set; }
    public static bool HasTracked { get; private set; }

    static readonly Color C_Backdrop = new Color(0f, 0f, 0f, 0.45f);
    static readonly Color C_Panel    = new Color(0.13f, 0.12f, 0.10f, 0.99f);
    static readonly Color C_Bar      = new Color(0.20f, 0.17f, 0.12f, 1f);
    static readonly Color C_Cell     = new Color(0.17f, 0.16f, 0.13f, 1f);
    static readonly Color C_Gold     = new Color(1f, 0.85f, 0.45f, 1f);
    static readonly Color C_Xp       = new Color(0.55f, 0.85f, 0.4f, 1f);

    GameObject _backdrop, _panel;
    TMP_Text _totalText;
    RectTransform _grid, _hint;

    class Cell { public Skill skill; public TMP_Text lvl; public Image xpFill; public Image bg; }
    readonly List<Cell> _cells = new();
    bool _built;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("SkillsPanelUI (auto)").AddComponent<SkillsPanelUI>();
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
        if (Input.GetKeyDown(KeyCode.K) && !IsTyping()) Toggle();
        if (IsOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
            Refresh();   // live-update levels/XP bars while open — no need to close & reopen
        }
    }

    public bool IsOpen => _backdrop != null && _backdrop.activeSelf;
    public void Toggle() { if (IsOpen) Close(); else Open(); }

    public void Open()
    {
        BuildCellsIfNeeded();
        Refresh();
        if (_backdrop) _backdrop.SetActive(true);
        _panel.transform.SetAsLastSibling();
    }

    public void Close() { if (_backdrop) _backdrop.SetActive(false); }

    public void Refresh()
    {
        var player = PlayerEntity.Instance;
        if (player == null) return;

        int totalLevel = 0;
        long totalXp = 0;
        foreach (var c in _cells)
        {
            var sd = player.Stats.GetSkillData(c.skill);
            if (sd == null) continue;
            c.lvl.text = $"Lv {sd.Level}";
            c.xpFill.fillAmount = Mathf.Clamp01(sd.LevelProgress);
            totalLevel += sd.Level;
            totalXp += sd.xp;
        }
        if (_totalText) _totalText.text = $"Total Level: {totalLevel}     Total XP: {totalXp:N0}";
    }

    // ── build ────────────────────────────────────────────────────────────
    void BuildCellsIfNeeded()
    {
        if (_built) return;
        var player = PlayerEntity.Instance;
        if (player == null) return;
        _built = true;

        foreach (var sd in player.Stats.AllSkills())
        {
            if (Skills.IsRetiredMelee(sd.skill)) continue;   // melee/defence line retired in the shooter pivot
            var skill = sd.skill;
            var cellRt = NewUI("Skill_" + skill, _grid);
            var bg = cellRt.gameObject.AddComponent<Image>();
            bg.color = C_Cell;
            var le = cellRt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 54; le.preferredHeight = 54;

            // Left-click → guide; right-click → toggle HUD tracking.
            var click = cellRt.gameObject.AddComponent<SkillCellClick>();
            click.skill = skill;

            // Try loading skill icon
            Sprite skillIcon = Resources.Load<Sprite>($"SkillIcons/{skill.ToString().ToLower()}");
            float textLeftOffset = 8f;

            if (skillIcon != null)
            {
                var iconGo = NewUI("Icon", cellRt);
                iconGo.anchorMin = new Vector2(0, 0.5f);
                iconGo.anchorMax = new Vector2(0, 0.5f);
                iconGo.pivot = new Vector2(0, 0.5f);
                iconGo.anchoredPosition = new Vector2(8, 5); // Centered in the upper section above XP bar
                iconGo.sizeDelta = new Vector2(32, 32);
                var iconImg = iconGo.gameObject.AddComponent<Image>();
                iconImg.sprite = skillIcon;
                iconImg.raycastTarget = false;
                
                textLeftOffset = 46f;
            }

            // name
            var nameRt = NewUI("Name", cellRt);
            nameRt.anchorMin = new Vector2(0, 0.5f); nameRt.anchorMax = new Vector2(1, 1);
            nameRt.offsetMin = new Vector2(textLeftOffset, 0); nameRt.offsetMax = new Vector2(-44, -2);   // leave room for "Lv 99"
            var nt = nameRt.gameObject.AddComponent<TextMeshProUGUI>();
            nt.text = sd.displayName; nt.color = UITheme.Text;
            nt.fontSize = 15;   // set BEFORE activation, or TMP resets wrap/size defaults when the panel first opens
            nt.enableAutoSizing = true; nt.fontSizeMin = 11; nt.fontSizeMax = 15;   // long names shrink to fit
            nt.textWrappingMode = TextWrappingModes.NoWrap;
            nt.alignment = TextAlignmentOptions.BottomLeft; nt.raycastTarget = false;

            // level
            var lvlRt = NewUI("Lvl", cellRt);
            lvlRt.anchorMin = new Vector2(0, 0.5f); lvlRt.anchorMax = new Vector2(1, 1);
            lvlRt.offsetMin = new Vector2(textLeftOffset, 0); lvlRt.offsetMax = new Vector2(-8, -2);
            var lt = lvlRt.gameObject.AddComponent<TextMeshProUGUI>();
            lt.text = "Lv 1"; lt.fontSize = 15; lt.color = UITheme.Amber;
            lt.alignment = TextAlignmentOptions.BottomRight; lt.raycastTarget = false;

            // xp bar background
            var barBg = NewUI("XpBg", cellRt);
            barBg.anchorMin = new Vector2(0, 0); barBg.anchorMax = new Vector2(1, 0);
            barBg.pivot = new Vector2(0.5f, 0);
            barBg.anchoredPosition = new Vector2(0, 8);
            barBg.sizeDelta = new Vector2(-16, 8);
            barBg.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.05f, 1f);

            var fillRt = NewUI("XpFill", barBg);
            fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;
            var fill = fillRt.gameObject.AddComponent<Image>();
            fill.color = C_Xp; fill.raycastTarget = false;
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;

            _cells.Add(new Cell { skill = skill, lvl = lt, xpFill = fill, bg = bg });
        }
        RefreshHighlights();
        FitToCells();
    }

    // Shrink the panel to the rows it actually has (no dead space under the last skill).
    void FitToCells()
    {
        var panelRt = (RectTransform)_panel.transform;
        int rows = Mathf.Max(1, Mathf.CeilToInt(_cells.Count / 3f));
        float gridH = rows * 54f + (rows - 1) * 6f;
        float ph = 66f + gridH + 12f + 24f;
        panelRt.sizeDelta = new Vector2(panelRt.sizeDelta.x, ph);
        _grid.sizeDelta = new Vector2(_grid.sizeDelta.x, gridH);
        if (_hint != null) _hint.anchoredPosition = new Vector2(_hint.anchoredPosition.x, -(ph - 26f));
    }

    /// <summary>Right-click handler: pin/unpin a skill for the HUD tracked-XP bar.</summary>
    public void ToggleTrack(Skill s)
    {
        if (HasTracked && TrackedSkill == s) HasTracked = false;
        else { TrackedSkill = s; HasTracked = true; }
        RefreshHighlights();

        var name = PlayerEntity.Instance?.Stats.GetSkillData(s)?.displayName ?? s.ToString();
        HUDController.Instance?.AddChatLine(HasTracked
            ? $"<color=#FFD24A>[TRACK]:</color> Now tracking {name} on your HUD."
            : "<color=#FFD24A>[TRACK]:</color> Skill tracking off.");
    }

    void RefreshHighlights()
    {
        foreach (var c in _cells)
            c.bg.color = (HasTracked && c.skill == TrackedSkill)
                ? new Color(0.42f, 0.33f, 0.14f, 1f) : C_Cell;
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("SkillsCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 525;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var backdropRt = NewUI("Backdrop", canvasGo.transform); Stretch(backdropRt);
        _backdrop = backdropRt.gameObject;
        // Invisible, click-through backdrop — panel stays open while you move/click the world,
        // and clicking off it no longer closes it (OSRS-style persistent panel).
        var bd = _backdrop.AddComponent<Image>();
        bd.color = new Color(0f, 0f, 0f, 0f);
        bd.raycastTarget = false;

        const float PW = 580, PH = 470;
        var panelRt = NewUI("Panel", backdropRt);
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(1f, 0.5f);   // right-center, matching the Inventory panel
        panelRt.pivot = new Vector2(1f, 0.5f);
        panelRt.sizeDelta = new Vector2(PW, PH);
        panelRt.anchoredPosition = new Vector2(-20f, 0f);
        UITheme.DockSidePanel(panelRt);   // bottom-right above the tab dock, clear of the minimap
        _panel = panelRt.gameObject;
        _panel.AddComponent<Image>().color = C_Panel;
        _panel.AddComponent<Button>().transition = Selectable.Transition.None;

        var bar = NewUI("Bar", panelRt); Place(bar, 0, 0, PW, 36);
        bar.gameObject.AddComponent<Image>().color = C_Bar;
        var titleRt = NewUI("Title", bar); Stretch(titleRt);
        var title = titleRt.gameObject.AddComponent<TextMeshProUGUI>();
        title.text = "SKILLS"; title.fontSize = 18; title.color = C_Gold;
        title.fontStyle = FontStyles.Bold; title.alignment = TextAlignmentOptions.Center;
        title.raycastTarget = false;

        var closeRt = NewUI("Close", bar); Place(closeRt, PW - 32, 5, 26, 26);
        var closeImg = closeRt.gameObject.AddComponent<Image>();
        closeImg.color = new Color(0.55f, 0.18f, 0.15f, 1f);
        var cbtn = closeRt.gameObject.AddComponent<Button>(); cbtn.targetGraphic = closeImg;
        cbtn.onClick.AddListener(Close);
        var cxRt = NewUI("X", closeRt); Stretch(cxRt);
        var cx = cxRt.gameObject.AddComponent<TextMeshProUGUI>();
        cx.text = "X"; cx.fontSize = 14; cx.alignment = TextAlignmentOptions.Center;
        cx.color = Color.white; cx.raycastTarget = false;

        // total line
        var totRt = NewUI("Total", panelRt); Place(totRt, 12, 40, PW - 24, 22);
        _totalText = totRt.gameObject.AddComponent<TextMeshProUGUI>();
        _totalText.fontSize = 14; _totalText.color = UITheme.TextDim;
        _totalText.alignment = TextAlignmentOptions.Center; _totalText.raycastTarget = false;

        // grid (3 columns). Note: keep the top-left pivot/anchor that Place() sets — overriding the
        // pivot to centre-x here (without moving anchoredPosition) shoved the grid half its width off
        // the panel's left edge, which looked like a second empty window beside the skills.
        var gridRt = NewUI("Grid", panelRt);
        Place(gridRt, 12, 66, PW - 24, PH - 78);
        _grid = gridRt;
        var glg = gridRt.gameObject.AddComponent<GridLayoutGroup>();
        float cellW = (PW - 24 - 2 * 6) / 3f;
        glg.cellSize = new Vector2(cellW, 54);
        glg.spacing = new Vector2(6, 6);
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = 3;

        var hintRt = NewUI("Hint", panelRt); Place(hintRt, 12, PH - 24, PW - 24, 18);
        _hint = hintRt;
        var hint = hintRt.gameObject.AddComponent<TextMeshProUGUI>();
        hint.text = "Click a skill to see what it unlocks. (K toggles this panel)";
        hint.fontSize = 13; hint.color = UITheme.TextDim;
        hint.alignment = TextAlignmentOptions.Center; hint.raycastTarget = false;
    }

    // ── helpers ──────────────────────────────────────────────────────────
    static bool IsTyping() => ChatInput.IsTyping;

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

/// <summary>Per-skill cell click: left = open guide, right = toggle HUD tracking.</summary>
public class SkillCellClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public Skill skill;

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Right)
            SkillsPanelUI.Instance?.ToggleTrack(skill);          // hold-tap on mobile
        else if (!TouchInput.SuppressClick)                      // ignore the tap that tails a long-press
            SkillGuideUI.Instance?.Open(skill);
    }

    // Hover → tooltip with current XP and XP remaining to the next level.
    public void OnPointerEnter(PointerEventData e) => TooltipUI.Instance?.Show(BuildTooltip());
    public void OnPointerExit(PointerEventData e)  => TooltipUI.Instance?.Hide();

    string BuildTooltip()
    {
        var player = PlayerEntity.Instance;
        if (player == null) return "";
        var sd = player.Stats.GetSkillData(skill);
        if (sd == null) return "";

        int lvl = sd.Level;
        if (lvl >= 99)
            return $"<b>{sd.displayName}</b>  (Lv 99)\nXP: {sd.xp:N0}\n<color=#88FF88>MAX LEVEL</color>";

        return $"<b>{sd.displayName}</b>  (Lv {lvl})\n" +
               $"XP: {sd.xp:N0}\n" +
               $"Next level at: {XPTable.XPForLevel(lvl + 1):N0}\n" +
               $"Remaining: <color=#FFD24A>{sd.XPToNext:N0}</color>";
    }
}
