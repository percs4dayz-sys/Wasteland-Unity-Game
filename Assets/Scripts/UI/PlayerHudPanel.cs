using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Player frame, top-left: your name and combat level, then compact stat bars — HP, total level, the
/// skill you're tracking (right-click a skill in the Skills tab to pin one) and, once you own a beast,
/// its pack stamina and your current beast task (BeastTasks). Rows that don't apply hide themselves and
/// the frame shrinks to fit. Click-through. Self-building & auto-spawned; styled by UITheme.
/// </summary>
public class PlayerHudPanel : MonoBehaviour
{
    public static PlayerHudPanel Instance { get; private set; }

    const float Width = 272f, RowH = 24f;

    /// <summary>One stat bar: icon, dark track, coloured fill (width = progress), caption + value.</summary>
    class Row
    {
        public GameObject go;
        public RectTransform fill;
        public Image icon;
        public TMP_Text label, value;

        public void Set(float frac, string lbl, string val)
        {
            frac = Mathf.Clamp01(frac);
            if (!Mathf.Approximately(fill.anchorMax.x, frac)) fill.anchorMax = new Vector2(frac, 1f);
            if (label.text != lbl) label.text = lbl;
            if (value.text != val) value.text = val;
        }

        public void Show(bool on) { if (go.activeSelf != on) go.SetActive(on); }
    }

    TMP_Text _name, _combat;
    Row _hp, _total, _track, _beast, _task;
    Skill _trackIconSkill = (Skill)(-1);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("PlayerHudPanel (auto)").AddComponent<PlayerHudPanel>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);   // survive scene changes (warp to 3D world etc.)
        BuildUI();
    }

    Canvas _hudCanvas;

    void Update()
    {
        var p = PlayerEntity.Instance;
        // In-world HUD only: hidden on the title / character-creation screens (no player there).
        bool show = p != null && p.Stats != null;
        if (_hudCanvas != null && _hudCanvas.enabled != show) _hudCanvas.enabled = show;
        if (!show) return;

        // Name + combat level
        string nm = string.IsNullOrEmpty(p.PlayerName) ? "Survivor" : p.PlayerName;
        if (_name.text != nm) _name.text = nm;
        string cb = $"Combat {p.Stats.GetCombatLevel()}";
        if (_combat.text != cb) _combat.text = cb;

        // HP — the value turns red when you're badly hurt.
        int cur = p.Stats.CurrentHP, max = p.Stats.MaxHP;
        _hp.Set(max > 0 ? (float)cur / max : 0f, "HP", $"{cur} / {max}");
        _hp.value.color = (max > 0 && cur <= max * 0.3f) ? UITheme.Alert : UITheme.Text;

        // Total level. No fill: as a share of the ~1,500 max it's a sliver for most of the game, which
        // just reads as a glitch — the number is what matters.
        int totalLevel = 0;
        foreach (var sd in p.Stats.AllSkills())
            if (!Skills.IsRetiredMelee(sd.skill)) totalLevel += sd.Level;
        _total.Set(0f, "Total level", totalLevel.ToString());

        // Tracked skill — only while you're pinning one.
        bool tracking = SkillsPanelUI.HasTracked;
        _track.Show(tracking);
        if (tracking)
        {
            var sd = p.Stats.GetSkillData(SkillsPanelUI.TrackedSkill);
            if (sd != null)
            {
                _track.Set(sd.LevelProgress, sd.displayName, $"Lv {sd.Level}");
                if (_trackIconSkill != sd.skill)
                {
                    _trackIconSkill = sd.skill;
                    var spr = Resources.Load<Sprite>($"SkillIcons/{sd.skill.ToString().ToLower()}");
                    _track.icon.sprite = spr;
                    _track.icon.color = spr != null ? Color.white : UITheme.XpFill;
                    if (spr == null) _track.icon.sprite = Resources.Load<Sprite>("UI/Icons/star");
                }
            }
        }

        // Beast: pack stamina + your current beast task — only once you own one.
        bool ownsBeast = BeastTasks.OwnsBeast;
        _beast.Show(ownsBeast);
        if (ownsBeast)
        {
            float stam = CompanionManager.Instance != null ? CompanionManager.Instance.StaminaPercent : 1f;
            _beast.Set(stam, $"Beast Lv {p.Stats.GetLevel(Skill.Beastmastery)}", $"{Mathf.RoundToInt(stam * 100)}%");
        }

        // Red caption = your beast isn't out, so kills won't count yet.
        bool showTask = BeastTasks.HudInfo(out string taskLabel, out string taskValue, out float taskFill, out bool taskAlert);
        _task.Show(showTask);
        if (showTask)
        {
            _task.Set(taskFill, taskLabel, taskValue);
            _task.label.color = taskAlert ? UITheme.Alert : UITheme.Text;
        }
    }

    // ── build ────────────────────────────────────────────────────────────
    void BuildUI()
    {
        var canvasGo = new GameObject("PlayerHudCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        _hudCanvas = canvas;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 465;
        UITheme.ApplyScaler(canvasGo.GetComponent<CanvasScaler>(), new Vector2(1920, 1080));

        // The frame: top-left, click-through, grows/shrinks with its visible rows.
        var frame = NewUI("PlayerFrame", canvasGo.transform);
        frame.anchorMin = frame.anchorMax = new Vector2(0f, 1f);
        frame.pivot = new Vector2(0f, 1f);
        frame.anchoredPosition = new Vector2(16f, -16f);
        frame.sizeDelta = new Vector2(Width, 0f);
        var bg = frame.gameObject.AddComponent<Image>();
        UITheme.Panel(bg, 0.9f);
        bg.raycastTarget = false;

        var vlg = frame.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 8, 10);
        vlg.spacing = 5f;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fitter = frame.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Header: name (left) + combat level (right).
        var head = NewUI("Header", frame);
        var hle = head.gameObject.AddComponent<LayoutElement>();
        hle.minHeight = hle.preferredHeight = 24f;
        _name = Text(head, 19f, UITheme.Amber, TextAlignmentOptions.MidlineLeft);
        _name.characterSpacing = 3f;
        _combat = Text(head, 14f, UITheme.TextDim, TextAlignmentOptions.MidlineRight);

        _hp    = MakeRow(frame, "UI/Icons/heart",  UITheme.HpFill);
        _total = MakeRow(frame, "UI/Icons/star",   UITheme.GoldFill);
        _track = MakeRow(frame, "UI/Icons/star",   UITheme.XpFill);
        _beast = MakeRow(frame, "SkillIcons/beastmastery", UITheme.BeastFill, tintIcon: false);
        _task  = MakeRow(frame, "UI/Icons/target", UITheme.TaskFill);
        _task.label.overflowMode = TextOverflowModes.Ellipsis;   // long creature names clip neatly

        _track.Show(false); _beast.Show(false); _task.Show(false);
    }

    Row MakeRow(Transform parent, string iconPath, Color fillColor, bool tintIcon = true)
    {
        var row = new Row();
        var rt = NewUI("Row", parent);
        row.go = rt.gameObject;
        var le = row.go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = RowH;

        // Icon on the left, tinted to the bar's colour (Synty white silhouettes).
        var iconRt = NewUI("Icon", rt);
        iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 0.5f);
        iconRt.pivot = new Vector2(0f, 0.5f);
        iconRt.anchoredPosition = new Vector2(0f, 0f);
        iconRt.sizeDelta = new Vector2(20f, 20f);
        row.icon = iconRt.gameObject.AddComponent<Image>();
        row.icon.sprite = Resources.Load<Sprite>(iconPath);
        row.icon.preserveAspect = true;
        row.icon.raycastTarget = false;
        row.icon.color = tintIcon ? Color.Lerp(fillColor, Color.white, 0.25f) : Color.white;
        if (row.icon.sprite == null) row.icon.enabled = false;

        // Track + fill.
        var track = NewUI("Track", rt);
        track.anchorMin = Vector2.zero; track.anchorMax = Vector2.one;
        track.offsetMin = new Vector2(27f, 0f); track.offsetMax = Vector2.zero;
        var trackImg = track.gameObject.AddComponent<Image>();
        UITheme.Tile(trackImg);
        trackImg.color = UITheme.TrackTint;
        trackImg.raycastTarget = false;

        var area = NewUI("FillArea", track);
        area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(2f, 2f); area.offsetMax = new Vector2(-2f, -2f);
        row.fill = NewUI("Fill", area);
        row.fill.anchorMin = Vector2.zero; row.fill.anchorMax = new Vector2(1f, 1f);
        row.fill.offsetMin = row.fill.offsetMax = Vector2.zero;
        var fillImg = row.fill.gameObject.AddComponent<Image>();
        fillImg.sprite = UITheme.RoundFillSprite; fillImg.type = Image.Type.Sliced;
        fillImg.color = fillColor;
        fillImg.raycastTarget = false;

        // Caption (left) + value (right), drawn over the bar with a soft shadow.
        row.label = Text(track, 14f, UITheme.Text, TextAlignmentOptions.MidlineLeft);
        row.label.rectTransform.offsetMin = new Vector2(8f, 0f);
        row.label.rectTransform.offsetMax = new Vector2(-64f, 0f);   // leave room for the value
        row.value = Text(track, 14f, UITheme.Text, TextAlignmentOptions.MidlineRight);
        row.value.rectTransform.offsetMin = new Vector2(8f, 0f);
        row.value.rectTransform.offsetMax = new Vector2(-8f, 0f);
        return row;
    }

    static TMP_Text Text(Transform parent, float size, Color color, TextAlignmentOptions align)
    {
        var rt = NewUI("Text", parent);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        UITheme.Label(t, size, color, shadow: true);
        t.alignment = align;
        return t;
    }

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }
}
