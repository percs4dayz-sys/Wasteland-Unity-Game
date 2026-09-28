using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// A tiny right-click context menu (OSRS-style). Built entirely from script and
/// self-creates at scene start — no scene wiring needed.
///
/// Usage: ContextMenuUI.Instance.Open(screenPos, "Title", options);
/// where options is a list of (label, action) pairs.
/// </summary>
public class ContextMenuUI : MonoBehaviour
{
    public static ContextMenuUI Instance { get; private set; }

    /// <summary>True while the menu is up, or on the frame a click dismissed it — world clicks check
    /// this so the click that closes the menu doesn't also walk you somewhere.</summary>
    public static bool Blocking => Instance != null && (Instance._open || Instance._hiddenFrame == Time.frameCount);

    /// <summary>Phones get finger-sized rows. At desktop size a row is ~2 mm tall on a phone screen.</summary>
    static bool Touch => Application.isMobilePlatform;

    static readonly Color HoverTint = new Color(0.62f, 0.46f, 0.16f, 1f);   // entry under a sliding finger

    private int           _hiddenFrame = -1;
    private Canvas        _canvas;
    private RectTransform _panel;
    private TMP_FontAsset _font;
    private bool          _open;
    private readonly List<(RectTransform rt, Image img, Button btn)> _entries = new();
    private int           _hover = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance == null)
            new GameObject("ContextMenuUI").AddComponent<ContextMenuUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);   // persist across scene loads like the other self-built UIs
        _font = UIUtil.FindFont();
        BuildCanvas();
        BuildPanel();
        Hide();
    }

    /// <summary>Own Screen-Space-Overlay canvas with a sorting order above every panel, so the
    /// right-click menu always draws ON TOP of the Inventory/Bank/etc. windows (cross-canvas draw
    /// order is decided by Canvas.sortingOrder, not sibling order). Inventory is 540, the highest
    /// modal (EggChoice) is 600 — 700 clears them all.</summary>
    void BuildCanvas()
    {
        var canvasGo = new GameObject("ContextMenuCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 700;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
    }

    void BuildPanel()
    {
        if (_canvas == null) return;

        var go = new GameObject("ContextMenu",
            typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        go.transform.SetParent(_canvas.transform, false);
        _panel = go.GetComponent<RectTransform>();

        var img = go.GetComponent<Image>();
        UITheme.Panel(img, 0.98f);
        img.raycastTarget = true;

        var vlg = go.GetComponent<VerticalLayoutGroup>();
        vlg.padding              = Touch ? new RectOffset(10, 10, 10, 10) : new RectOffset(6, 6, 6, 6);
        vlg.spacing              = Touch ? 4 : 2;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;

        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // Anchor to canvas center; cursor position is given relative to center.
        _panel.anchorMin = new Vector2(0.5f, 0.5f);
        _panel.anchorMax = new Vector2(0.5f, 0.5f);
        _panel.pivot     = new Vector2(0f, 1f);   // grows down-right from the cursor
    }

    public void Open(Vector2 screenPos, string title, List<(string label, Action action)> options)
    {
        if (_panel == null) { BuildPanel(); if (_panel == null) return; }

        // Clear previous entries.
        for (int i = _panel.childCount - 1; i >= 0; i--)
            Destroy(_panel.GetChild(i).gameObject);
        _entries.Clear();
        _hover = -1;

        if (!string.IsNullOrEmpty(title)) AddTitle(title);
        foreach (var (label, action) in options) AddButton(label, action);

        // Convert the screen position to canvas-local space.
        var canvasRect = _canvas.transform as RectTransform;
        Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, cam, out var local))
            _panel.anchoredPosition = local;

        _panel.gameObject.SetActive(true);
        _panel.SetAsLastSibling();
        _open = true;

        LayoutRebuilder.ForceRebuildLayoutImmediate(_panel);
        if (Touch && _entries.Count > 0)
        {
            // OSRS mobile: the minimenu opens with its first entry under your finger, so a slide down picks.
            var first = _entries[0].rt;
            Vector2 firstCentre = _panel.InverseTransformPoint(first.TransformPoint(first.rect.center));
            _panel.anchoredPosition = local - firstCentre;
            if (TouchSettings.VibrateOnMenuOpen) TouchSettings.Haptic();
        }

        // Keep the whole menu on screen: flip it left/up when it would spill off the right/bottom edge,
        // and never let it hang off the left/top either.
        Vector2 half = canvasRect.rect.size * 0.5f, size = _panel.rect.size, pos = _panel.anchoredPosition;
        if (pos.x + size.x > half.x) pos.x = Mathf.Max(-half.x, pos.x - size.x);
        if (pos.y - size.y < -half.y) pos.y = Mathf.Min(half.y, pos.y + size.y);
        pos.x = Mathf.Max(pos.x, -half.x);
        pos.y = Mathf.Min(pos.y, half.y);
        _panel.anchoredPosition = pos;
    }

    /// <summary>Touch: the finger that opened the menu is sliding over it — light up the entry underneath
    /// (a tick of vibration each time it changes, if that's on).</summary>
    public void TouchHover(Vector2 screenPos)
    {
        if (!_open) return;
        int i = EntryAt(screenPos);
        if (i == _hover) return;
        if (_hover >= 0 && _hover < _entries.Count) _entries[_hover].img.color = UITheme.TileTint;
        _hover = i;
        if (i < 0) return;
        _entries[i].img.color = HoverTint;
        if (TouchSettings.VibrateOnMenuHover) TouchSettings.Haptic(8);
    }

    /// <summary>Touch: the finger that opened the menu lifted after sliding — choose the entry under it.
    /// Lifted anywhere else, the menu stays open for a tap.</summary>
    public void TouchRelease(Vector2 screenPos)
    {
        if (!_open) return;
        int i = EntryAt(screenPos);
        if (i >= 0) _entries[i].btn.onClick.Invoke();
    }

    int EntryAt(Vector2 screenPos)
    {
        for (int i = 0; i < _entries.Count; i++)
            if (_entries[i].rt != null && RectTransformUtility.RectangleContainsScreenPoint(_entries[i].rt, screenPos, null))
                return i;
        return -1;
    }

    public void Hide()
    {
        if (_open) _hiddenFrame = Time.frameCount;
        _open = false;
        if (_panel != null) _panel.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!_open || _panel == null) return;

        // Close when clicking anywhere outside the menu.
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
        {
            Camera cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera : null;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_panel, Input.mousePosition, cam))
                Hide();
        }
    }

    void AddTitle(string text)
    {
        var go = new GameObject("Title", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(_panel, false);
        go.GetComponent<LayoutElement>().minHeight = Touch ? 48 : 24;

        var txt = go.AddComponent<TextMeshProUGUI>();
        txt.font          = _font;
        txt.fontSize      = Touch ? 30 : 20;
        txt.fontStyle     = FontStyles.Bold;
        txt.color         = UITheme.Amber;
        txt.alignment     = TextAlignmentOptions.Left;
        txt.margin        = new Vector4(8, 0, 8, 0);
        txt.text          = text;
        txt.raycastTarget = false;
    }

    void AddButton(string label, Action action)
    {
        var go = new GameObject(label + "Btn",
            typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(_panel, false);

        var le = go.GetComponent<LayoutElement>();
        le.minHeight = Touch ? 68 : 28;
        le.minWidth  = Touch ? 280 : 130;

        var img = go.GetComponent<Image>();
        UITheme.Tile(img);
        img.color = UITheme.TileTint;

        var btn = go.GetComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.6f, 1.45f, 1.25f, 1f);   // warm lift over the tile tint
        colors.pressedColor     = new Color(2.2f, 1.9f, 1.4f, 1f);
        colors.selectedColor    = Color.white;
        btn.colors = colors;
        btn.onClick.AddListener(() => { action?.Invoke(); Hide(); });
        _entries.Add((go.GetComponent<RectTransform>(), img, btn));

        var txtGo = new GameObject("Text", typeof(RectTransform));
        txtGo.transform.SetParent(go.transform, false);
        var txt = txtGo.AddComponent<TextMeshProUGUI>();
        txt.font          = _font;
        txt.fontSize      = Touch ? 28 : 18;
        txt.color         = UITheme.Text;
        txt.alignment     = TextAlignmentOptions.Left;
        txt.text          = label;
        txt.raycastTarget = false;

        var rt = txtGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(8f, 0f);
        rt.offsetMax = new Vector2(-8f, 0f);
    }
}
