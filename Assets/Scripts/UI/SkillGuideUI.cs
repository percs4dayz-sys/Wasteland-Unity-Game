using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// OSRS-style skill guide popup. Click a skill in the Skills panel to open it.
/// Shows every milestone unlock for that skill; unlocked tiers are highlighted,
/// locked tiers are dimmed. Modal — click the dimmed background or the X to close.
///
/// Self-creates at scene start — no scene wiring needed.
/// </summary>
public class SkillGuideUI : MonoBehaviour
{
    public static SkillGuideUI Instance { get; private set; }

    private Canvas        _canvas;
    private TMP_FontAsset _font;
    private GameObject    _root;
    private RectTransform _content;
    private TMP_Text      _title;
    private TMP_Text      _subtitle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance == null)
            new GameObject("SkillGuideUI").AddComponent<SkillGuideUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // It bootstraps in the first scene (the boot screen); without this it died on the way into the world.
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        if (_root != null && _root.activeSelf && Input.GetKeyDown(KeyCode.Escape))
            Close();
    }

    public void Open(Skill skill)
    {
        UnityEngine.Debug.Log("[SkillGuideUI] Opening guide for " + skill);
        if (_root == null)
        {
            _font = UIUtil.FindFont();
            Build();
            if (_root == null)
            {
                UnityEngine.Debug.LogError("[SkillGuideUI] Failed to build UI. Canvas: " + (_canvas != null));
                return;
            }
        }

        var player = PlayerEntity.Instance;
        var sd     = player?.Stats.GetSkillData(skill);
        int curLvl = sd?.Level ?? 1;

        _title.text    = sd != null ? $"{sd.displayName}  —  Level {curLvl}/99" : skill.ToString();
        _subtitle.text = SkillGuide.Subtitle(skill);

        // Rebuild milestone rows.
        for (int i = _content.childCount - 1; i >= 0; i--)
            Destroy(_content.GetChild(i).gameObject);

        foreach (var m in SkillGuide.For(skill))
            AddRow(m.level, m.unlock, curLvl >= m.level);

        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
    }

    public void Close()
    {
        if (_root != null) _root.SetActive(false);
    }

    // ── UI construction ──────────────────────────────────────────────────
    void Build()
    {
        // Own dedicated overlay canvas with a high sorting order so the guide always renders
        // (and receives clicks) above the Skills panel (sortingOrder 525). Sharing the HUD canvas
        // let the Skills panel draw over the guide's X / blocker, so it couldn't be closed.
        var canvasGo = new GameObject("SkillGuideCanvas",
            typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 600;
        var scaler = canvasGo.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // Root + dimmed full-screen blocker (click to close).
        _root = new GameObject("SkillGuideRoot", typeof(RectTransform));
        _root.transform.SetParent(_canvas.transform, false);
        Stretch(_root.GetComponent<RectTransform>());

        var blockerGo = new GameObject("Blocker", typeof(RectTransform), typeof(Image), typeof(Button));
        blockerGo.transform.SetParent(_root.transform, false);
        Stretch(blockerGo.GetComponent<RectTransform>());
        blockerGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        blockerGo.GetComponent<Button>().onClick.AddListener(Close);

        // Panel.
        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panelGo.transform.SetParent(_root.transform, false);
        var panel = panelGo.GetComponent<RectTransform>();
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot     = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(560f, 640f);
        panelGo.GetComponent<Image>().color = new Color(0.10f, 0.10f, 0.13f, 0.99f);

        // Title.
        _title = MakeText(panel, "Title", 26, FontStyles.Bold, new Color(1f, 0.85f, 0.4f));
        TopStrip(_title.rectTransform, 16, 52, 14, 34);   // wide right inset clears the X button
        _title.alignment = TextAlignmentOptions.TopLeft;

        // Close (X) button, top-right.
        var xGo = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
        xGo.transform.SetParent(panel, false);
        var xRt = xGo.GetComponent<RectTransform>();
        xRt.anchorMin = xRt.anchorMax = new Vector2(1, 1);
        xRt.pivot = new Vector2(1, 1);
        xRt.anchoredPosition = new Vector2(-10, -10);
        xRt.sizeDelta = new Vector2(34, 34);
        xGo.GetComponent<Image>().color = new Color(0.5f, 0.15f, 0.15f, 1f);
        xGo.GetComponent<Button>().onClick.AddListener(Close);
        var xTxt = MakeText(xRt, "CloseLabel", 22, FontStyles.Bold, Color.white);
        Stretch(xTxt.rectTransform);
        xTxt.alignment = TextAlignmentOptions.Center;
        xTxt.text = "X";

        // Subtitle.
        _subtitle = MakeText(panel, "Subtitle", 17, FontStyles.Italic, new Color(0.75f, 0.8f, 0.85f));
        TopStrip(_subtitle.rectTransform, 16, 16, 52, 44);
        _subtitle.alignment = TextAlignmentOptions.TopLeft;
        _subtitle.textWrappingMode = TextWrappingModes.Normal;

        // Scroll view (fills the rest of the panel below the subtitle).
var svGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
        svGo.transform.SetParent(panel, false);
        var svRt = svGo.GetComponent<RectTransform>();
        svRt.anchorMin = new Vector2(0, 0);
        svRt.anchorMax = new Vector2(1, 1);
        svRt.offsetMin = new Vector2(12, 12);
        svRt.offsetMax = new Vector2(-12, -100);
        svGo.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.07f, 1f);

        var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewportGo.transform.SetParent(svGo.transform, false);
        var viewport = viewportGo.GetComponent<RectTransform>();
        Stretch(viewport);
        viewportGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);

        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewportGo.transform, false);
        _content = contentGo.GetComponent<RectTransform>();
        _content.anchorMin = new Vector2(0, 1);
        _content.anchorMax = new Vector2(1, 1);
        _content.pivot     = new Vector2(0.5f, 1);
        _content.offsetMin = Vector2.zero;
        _content.offsetMax = Vector2.zero;

        var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
        vlg.padding              = new RectOffset(8, 8, 8, 8);
        vlg.spacing              = 4;
        vlg.childControlWidth     = true;
        vlg.childControlHeight    = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight= false;

        contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var sr = svGo.GetComponent<ScrollRect>();
        sr.horizontal       = false;
        sr.vertical         = true;
        sr.viewport         = viewport;
        sr.content          = _content;
        sr.scrollSensitivity = 24f;
        sr.movementType      = ScrollRect.MovementType.Clamped;
    }

    void AddRow(int level, string unlock, bool unlocked)
    {
        var rowGo = new GameObject($"Lv{level}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        rowGo.transform.SetParent(_content, false);
        rowGo.GetComponent<Image>().color = unlocked
            ? new Color(0.16f, 0.22f, 0.16f, 1f)    // greenish = unlocked
            : new Color(0.14f, 0.14f, 0.16f, 1f);   // grey = locked
        rowGo.GetComponent<LayoutElement>().minHeight = 46;

        var txt = MakeText(rowGo.GetComponent<RectTransform>(), "Text", 17, FontStyles.Normal, Color.white);
        Stretch(txt.rectTransform);
        txt.margin    = new Vector4(10, 4, 10, 4);
        txt.alignment = TextAlignmentOptions.Left;
        txt.textWrappingMode = TextWrappingModes.Normal;

        string lvlTag = unlocked
? $"<color=#7CFC7C><b>Lv {level}</b></color>"
            : $"<color=#888888><b>Lv {level}</b></color>";
        string body   = unlocked
            ? unlock
            : $"<color=#9a9a9a>{unlock}  (locked)</color>";
        txt.text = $"{lvlTag}   {body}";
    }

    // ── small helpers ────────────────────────────────────────────────────
    TMP_Text MakeText(Transform parent, string name, float size, FontStyles style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.font          = _font;
        t.fontSize      = size;
        t.fontStyle     = style;
        t.color         = color;
        t.raycastTarget = false;
        return t;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>Top-anchored, horizontally-stretched strip with a fixed height.</summary>
    static void TopStrip(RectTransform rt, float left, float right, float top, float height)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot     = new Vector2(0.5f, 1);
        rt.offsetMax = new Vector2(-right, -top);            // top edge below the anchor
        rt.offsetMin = new Vector2(left,  -top - height);    // bottom edge = top + height
    }
}
