using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A small multi-line tooltip box that follows the cursor while shown.
/// Self-creates at scene start — no scene wiring needed.
/// </summary>
public class TooltipUI : MonoBehaviour
{
    public static TooltipUI Instance { get; private set; }

    private Canvas        _canvas;
    private RectTransform _panel;
    private TMP_Text      _text;
    private TMP_FontAsset _font;
    private bool          _shown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance == null)
            new GameObject("TooltipUI").AddComponent<TooltipUI>();
        // The tooltip lives on the scene's overlay canvas, so it goes when the scene does: make a fresh one in
        // each new scene (it used to be built once, on the boot screen, and was gone by the time you played).
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, __) =>
        {
            if (Instance == null) new GameObject("TooltipUI").AddComponent<TooltipUI>();
        };
    }

    void Awake()
    {
        Instance = this;
        _canvas  = UIUtil.FindOverlayCanvas();
        _font    = UIUtil.FindFont();
        Build();
        Hide();
    }

    void Build()
    {
        if (_canvas == null) return;

        var go = new GameObject("Tooltip",
            typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        go.transform.SetParent(_canvas.transform, false);
        _panel = go.GetComponent<RectTransform>();

        var img = go.GetComponent<Image>();
        UITheme.Panel(img, 0.97f);                 // same framed look as every panel
        img.raycastTarget = false;                 // never steals clicks

        var vlg = go.GetComponent<VerticalLayoutGroup>();
        vlg.padding              = new RectOffset(12, 12, 8, 8);
        vlg.childControlWidth     = true;
        vlg.childControlHeight    = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight= false;

        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        _panel.anchorMin = new Vector2(0.5f, 0.5f);
        _panel.anchorMax = new Vector2(0.5f, 0.5f);
        _panel.pivot     = new Vector2(0f, 1f);   // grows down-right from cursor

        var txtGo = new GameObject("Text", typeof(RectTransform));
        txtGo.transform.SetParent(go.transform, false);
        _text = txtGo.AddComponent<TextMeshProUGUI>();
        _text.font               = _font;
        _text.fontSize           = 18;
        _text.color              = UITheme.Text;
        _text.alignment          = TextAlignmentOptions.TopLeft;
        _text.textWrappingMode   = TextWrappingModes.NoWrap;
        _text.raycastTarget      = false;
}

    public void Show(string richText)
    {
        if (_panel == null) { Build(); if (_panel == null) return; }
        _text.text = richText;
        _panel.gameObject.SetActive(true);
        _panel.SetAsLastSibling();
        _shown = true;
        Reposition();
    }

    public void Hide()
    {
        _shown = false;
        if (_panel != null) _panel.gameObject.SetActive(false);
    }

    void Update()
    {
        if (_shown) Reposition();
    }

    void Reposition()
    {
        if (_canvas == null || _panel == null) return;
        var canvasRect = _canvas.transform as RectTransform;
        Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Input.mousePosition, cam, out var local))
            _panel.anchoredPosition = local + new Vector2(18f, -18f);
    }
}
