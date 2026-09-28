using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// RuneScape-style music tab. Lists every track in Resources/Music; click a name to
/// play it now, click the ✕ to disable/enable it (disabled tracks are skipped in the
/// shuffle and remembered between runs), and Skip jumps to the next. The currently
/// playing track is highlighted. Self-building and auto-spawned — opened from HudTabBar.
/// </summary>
public class MusicPanelUI : MonoBehaviour
{
    public static MusicPanelUI Instance { get; private set; }

    static readonly Color C_Panel = new(0.13f, 0.12f, 0.10f, 0.99f);
    static readonly Color C_Bar   = new(0.20f, 0.17f, 0.12f, 1f);
    static readonly Color C_Gold  = new(1f, 0.85f, 0.45f, 1f);
    static readonly Color C_Row   = new(0.17f, 0.16f, 0.12f, 0.96f);
    static readonly Color C_RowOn = new(0.26f, 0.34f, 0.18f, 1f);   // currently playing
    static readonly Color C_Red   = new(0.55f, 0.18f, 0.15f, 1f);
    static readonly Color C_Dim   = new(0.45f, 0.43f, 0.40f, 1f);
    static readonly Color C_Text  = new(0.88f, 0.86f, 0.82f, 1f);

    GameObject _panel;
    RectTransform _content;
    TextMeshProUGUI _nowPlaying;
    bool _subscribed;

    struct Row { public int index; public Image bg; public TextMeshProUGUI label; public TextMeshProUGUI x; }
    readonly List<Row> _rows = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("MusicPanelUI (auto)").AddComponent<MusicPanelUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Build();
        _panel.SetActive(false);
    }

    public bool IsOpen => _panel != null && _panel.activeSelf;
    public void Close() { if (_panel) _panel.SetActive(false); }

    public void Toggle()
    {
        bool show = !_panel.activeSelf;
        _panel.SetActive(show);
        if (show)
        {
            _panel.transform.SetAsLastSibling();
            EnsureSubscribed();
            BuildRows();
            Refresh();
        }
    }

    void EnsureSubscribed()
    {
        if (_subscribed || MusicManager.Instance == null) return;
        MusicManager.Instance.OnTrackChanged += Refresh;
        _subscribed = true;
    }

    void OnDestroy()
    {
        if (_subscribed && MusicManager.Instance != null) MusicManager.Instance.OnTrackChanged -= Refresh;
    }

    // ── build the shell once ─────────────────────────────────────────────────
    void Build()
    {
        var canvasGo = new GameObject("MusicPanelCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 472;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var rt = NewUI("MusicPanel", canvasGo.transform);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(540, 640);
        rt.anchoredPosition = Vector2.zero;
        _panel = rt.gameObject;
        _panel.AddComponent<Image>().color = C_Panel;

        // Title bar + close.
        var bar = NewUI("Bar", rt); Place(bar, 0, 0, 540, 34);
        bar.gameObject.AddComponent<Image>().color = C_Bar;
        Label(bar, "MUSIC", 16, C_Gold, FontStyles.Bold, TextAlignmentOptions.Center, stretch: true);
        var close = Button(bar, 540 - 32, 4, 26, 26, C_Red, () => _panel.SetActive(false));
        Label(close, "X", 14, Color.white, FontStyles.Bold, TextAlignmentOptions.Center, stretch: true);

        // Now-playing line + Skip button.
        var npRt = NewUI("NowPlaying", rt); Place(npRt, 16, 42, 400, 26);
        _nowPlaying = npRt.gameObject.AddComponent<TextMeshProUGUI>();
        _nowPlaying.fontSize = 14; _nowPlaying.color = C_Gold;
        _nowPlaying.alignment = TextAlignmentOptions.MidlineLeft; _nowPlaying.raycastTarget = false;

        var skip = Button(rt, 540 - 96, 42, 80, 26, C_Bar, () => MusicManager.Instance?.Next());
        Label(skip, "Skip ▶", 13, C_Gold, FontStyles.Bold, TextAlignmentOptions.Center, stretch: true);

        // Scrolling list.
        var viewRt = NewUI("Viewport", rt); Place(viewRt, 12, 76, 516, 552);
        var viewImg = viewRt.gameObject.AddComponent<Image>(); viewImg.color = new Color(0.10f, 0.09f, 0.08f, 1f);
        viewRt.gameObject.AddComponent<RectMask2D>();
        var scroll = rt.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewRt; scroll.horizontal = false; scroll.scrollSensitivity = 24f;

        _content = NewUI("Content", viewRt);
        _content.anchorMin = new Vector2(0, 1); _content.anchorMax = new Vector2(1, 1); _content.pivot = new Vector2(0.5f, 1f);
        _content.anchoredPosition = Vector2.zero; _content.sizeDelta = new Vector2(0, 0);
        var vlg = _content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 4; vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.childControlWidth = true; vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = _content;
    }

    // ── one row per track ────────────────────────────────────────────────────
    void BuildRows()
    {
        if (_rows.Count > 0) return;   // build once
        var mm = MusicManager.Instance;
        if (mm == null) return;

        for (int i = 0; i < mm.Clips.Count; i++)
        {
            int idx = i;
            var rowRt = NewUI("Row" + i, _content);
            rowRt.gameObject.AddComponent<LayoutElement>().minHeight = 30;
            var bg = rowRt.gameObject.AddComponent<Image>(); bg.color = C_Row;

            // ✕ toggle on the left.
            var xBtn = Button(rowRt, 6, 4, 22, 22, new Color(0, 0, 0, 0.25f), () => {
                var m = MusicManager.Instance; if (m != null) m.SetEnabled(idx, !m.IsEnabled(idx));
            });
            var x = Label(xBtn, "✕", 13, C_Red, FontStyles.Bold, TextAlignmentOptions.Center, stretch: true);

            // Name button (click to play).
            var nameBtn = NewUI("Name", rowRt);
            nameBtn.anchorMin = new Vector2(0, 0); nameBtn.anchorMax = new Vector2(1, 1);
            nameBtn.offsetMin = new Vector2(34, 0); nameBtn.offsetMax = new Vector2(-6, 0);
            var nImg = nameBtn.gameObject.AddComponent<Image>(); nImg.color = new Color(0, 0, 0, 0);
            var nb = nameBtn.gameObject.AddComponent<Button>(); nb.targetGraphic = nImg;
            nb.onClick.AddListener(() => MusicManager.Instance?.PlayTrack(idx));
            var lbl = Label(nameBtn, Pretty(mm.Clips[idx].name), 13, C_Text, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, stretch: true);

            _rows.Add(new Row { index = idx, bg = bg, label = lbl, x = x });
        }
    }

    // ── update highlight / dim / now-playing ─────────────────────────────────
    void Refresh()
    {
        var mm = MusicManager.Instance;
        if (mm == null) return;
        if (_nowPlaying != null) _nowPlaying.text = "♪ " + Pretty(mm.CurrentTrackName);

        foreach (var r in _rows)
        {
            bool on = mm.IsEnabled(r.index);
            bool playing = r.index == mm.CurrentIndex;
            r.bg.color = playing ? C_RowOn : C_Row;
            r.label.color = !on ? C_Dim : (playing ? C_Gold : C_Text);
            r.label.fontStyle = (on ? FontStyles.Normal : FontStyles.Strikethrough);
            r.x.color = on ? C_Red : C_Dim;
        }
    }

    static string Pretty(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        return raw.Replace('_', ' ').Replace(" - Sonauto", "").Trim();
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static RectTransform Button(Transform parent, float x, float y, float w, float h, Color col, UnityEngine.Events.UnityAction onClick)
    {
        var rt = NewUI("Btn", parent); Place(rt, x, y, w, h);
        var img = rt.gameObject.AddComponent<Image>(); img.color = col;
        var btn = rt.gameObject.AddComponent<Button>(); btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        return rt;
    }

    static TextMeshProUGUI Label(Transform parent, string text, float size, Color col, FontStyles style, TextAlignmentOptions align, bool stretch)
    {
        var rt = NewUI("Lbl", parent);
        if (stretch) Stretch(rt);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = col; t.fontStyle = style;
        t.alignment = align; t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
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
}
