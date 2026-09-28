using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// A "coming soon" panel for the BUILD system, opened from the bottom tab bar. It exists so the
/// building feature — which is real and playable via the H key, but has no home on the tab bar and
/// is easy to forget — is at least DESCRIBED somewhere the player will look.
///
/// Deliberately not the build menu itself: BuildManager owns that (press H). This is a signpost.
///
/// Self-building & auto-spawned, matching the other tab panels (CombatStyleUI, SkillsPanelUI).
/// </summary>
public class BuildInfoPanelUI : MonoBehaviour
{
    public static BuildInfoPanelUI Instance { get; private set; }

    static readonly Color C_Panel = new Color(0.13f, 0.12f, 0.10f, 0.99f);
    static readonly Color C_Bar   = new Color(0.20f, 0.17f, 0.12f, 1f);
    static readonly Color C_Gold  = new Color(1f, 0.85f, 0.45f, 1f);

    GameObject _backdrop;
    GameObject _panel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("BuildInfoPanelUI (auto)").AddComponent<BuildInfoPanelUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureEventSystem();
        BuildUI();
        if (_backdrop) _backdrop.SetActive(false);
    }

    public bool IsOpen => _backdrop != null && _backdrop.activeSelf;
    public void Toggle() { if (IsOpen) Close(); else Open(); }
    public void Open()  { if (_backdrop) _backdrop.SetActive(true); _panel.transform.SetAsLastSibling(); }
    public void Close() { if (_backdrop) _backdrop.SetActive(false); }

    void Update()
    {
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("BuildInfoCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 530;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var backdropRt = NewUI("Backdrop", canvasGo.transform); Stretch(backdropRt);
        _backdrop = backdropRt.gameObject;
        var bd = _backdrop.AddComponent<Image>();
        bd.color = new Color(0f, 0f, 0f, 0f);
        bd.raycastTarget = false;

        const float PW = 360, PH = 300;
        var panelRt = NewUI("Panel", backdropRt);
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(1f, 0.5f);
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
        title.text = "BUILDING"; title.fontSize = 16; title.color = C_Gold;
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

        var bodyRt = NewUI("Body", panelRt);
        Place(bodyRt, 16, 46, PW - 32, PH - 60);
        var body = bodyRt.gameObject.AddComponent<TextMeshProUGUI>();
        body.text =
            "<color=#FFD24A><b>Coming soon.</b></color>\n\n" +
            "Building lets you place deployables in the world — cooking fires, furnaces, workbenches, " +
            "banks, auto-turrets and AFK resource stations.\n\n" +
            "<b>You can already do this:</b> press <color=#7FE7FF>H</color> to open the build menu, pick " +
            "a deployable, and click to place it. Rotate with <color=#7FE7FF>R</color>, cancel with " +
            "<color=#7FE7FF>Esc</color>.\n\n" +
            "It isn't its own skill yet — placing costs materials and is being reworked into a proper " +
            "progression. This tab will become the build browser when it lands.";
        body.fontSize = 15; body.color = new Color(0.86f, 0.86f, 0.82f, 1f);
        body.alignment = TextAlignmentOptions.TopLeft; body.raycastTarget = false;
        body.textWrappingMode = TextWrappingModes.Normal;
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

    static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es);
        }
    }
}
