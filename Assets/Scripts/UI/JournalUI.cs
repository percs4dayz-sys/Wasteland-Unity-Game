using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// A simple full-text reader for the survivor's journal pages (JournalLore). Self-building and
/// auto-spawned after each scene load — call JournalUI.Instance.Open(itemId) to read a page.
/// Esc or the backdrop closes it.
/// </summary>
public class JournalUI : MonoBehaviour
{
    public static JournalUI Instance { get; private set; }

    static readonly Color C_Backdrop = new(0f, 0f, 0f, 0.6f);
    static readonly Color C_Paper    = new(0.16f, 0.14f, 0.10f, 0.99f);
    static readonly Color C_Title    = new(1f, 0.85f, 0.45f, 1f);

    GameObject _backdrop;
    TMP_Text _titleText;
    TMP_Text _bodyText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("JournalUI (auto)").AddComponent<JournalUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();
        if (_backdrop) _backdrop.SetActive(false);
    }

    void Update()
    {
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    public bool IsOpen => _backdrop != null && _backdrop.activeSelf;

    public void Open(int itemId)
    {
        var entry = JournalLore.Get(itemId);
        if (entry == null) return;
        if (_titleText) _titleText.text = entry.title;
        if (_bodyText)  _bodyText.text  = entry.body;
        if (_backdrop)  _backdrop.SetActive(true);
    }

    public void Close() { if (_backdrop) _backdrop.SetActive(false); }

    void BuildUI()
    {
        var canvasGo = new GameObject("JournalCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 520;   // above the other panels
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var backdropRt = NewUI("Backdrop", canvasGo.transform);
        Stretch(backdropRt);
        _backdrop = backdropRt.gameObject;
        _backdrop.AddComponent<Image>().color = C_Backdrop;
        var backBtn = _backdrop.AddComponent<Button>();
        backBtn.transition = Selectable.Transition.None;
        backBtn.onClick.AddListener(Close);

        const float PW = 640, PH = 600;
        var panelRt = NewUI("Paper", backdropRt);
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(1f, 0.5f);   // right-center, matching the Inventory panel
        panelRt.pivot = new Vector2(1f, 0.5f);
        panelRt.sizeDelta = new Vector2(PW, PH);
        panelRt.anchoredPosition = new Vector2(-20f, 0f);
        UITheme.DockSidePanel(panelRt);   // bottom-right above the tab dock, clear of the minimap
        panelRt.gameObject.AddComponent<Image>().color = C_Paper;
        panelRt.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // swallow clicks

        var titleRt = NewUI("Title", panelRt);
        Place(titleRt, 28, 22, PW - 56, 34);
        _titleText = titleRt.gameObject.AddComponent<TextMeshProUGUI>();
        _titleText.fontSize = 22; _titleText.color = C_Title; _titleText.fontStyle = FontStyles.Bold;
        _titleText.alignment = TextAlignmentOptions.Left; _titleText.raycastTarget = false;

        // Scrollable body (entries are long).
        var viewport = NewUI("Viewport", panelRt);
        Place(viewport, 24, 66, PW - 48, PH - 122);
        viewport.gameObject.AddComponent<Image>().color = new Color(0.10f, 0.09f, 0.06f, 1f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var contentRt = NewUI("Content", viewport);
        contentRt.anchorMin = new Vector2(0, 1); contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero; contentRt.sizeDelta = Vector2.zero;
        var fitter = contentRt.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var vlg = contentRt.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(16, 16, 14, 14);
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        var bodyRt = NewUI("Body", contentRt);
        _bodyText = bodyRt.gameObject.AddComponent<TextMeshProUGUI>();
        _bodyText.fontSize = 17; _bodyText.color = new Color(0.88f, 0.85f, 0.78f, 1f);
        _bodyText.alignment = TextAlignmentOptions.TopLeft; _bodyText.raycastTarget = false;
        _bodyText.lineSpacing = 6f;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = contentRt;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28;

        // Close button
        var closeRt = NewUI("Close", panelRt);
        Place(closeRt, PW / 2f - 70, PH - 46, 140, 32);
        var closeImg = closeRt.gameObject.AddComponent<Image>();
        closeImg.color = new Color(0.30f, 0.26f, 0.18f, 1f);
        var closeBtn = closeRt.gameObject.AddComponent<Button>();
        closeBtn.targetGraphic = closeImg;
        closeBtn.onClick.AddListener(Close);
        var closeTxtRt = NewUI("Lbl", closeRt); Stretch(closeTxtRt);
        var closeTxt = closeTxtRt.gameObject.AddComponent<TextMeshProUGUI>();
        closeTxt.text = "Close  (Esc)"; closeTxt.fontSize = 16; closeTxt.color = Color.white;
        closeTxt.alignment = TextAlignmentOptions.Center; closeTxt.raycastTarget = false;
    }

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
}
