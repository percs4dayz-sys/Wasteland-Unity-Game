using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// A small always-available system menu (toggle with the corner button or F10).
/// Lets you set a player name and wipe the save to start fresh — handy for testing.
/// Fully self-building & auto-spawned; no inspector setup required.
/// </summary>
public class WastelandMenuUI : MonoBehaviour
{
    public static WastelandMenuUI Instance { get; private set; }

    static readonly Color C_Panel  = new Color(0.13f, 0.12f, 0.10f, 0.99f);
    static readonly Color C_Bar    = new Color(0.20f, 0.17f, 0.12f, 1f);
    static readonly Color C_Field  = new Color(0.08f, 0.08f, 0.07f, 1f);
    static readonly Color C_Gold   = new Color(1f, 0.85f, 0.45f, 1f);
    static readonly Color C_Danger = new Color(0.55f, 0.18f, 0.15f, 1f);

    GameObject _panel;
    TMP_InputField _nameField;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("WastelandMenuUI (auto)").AddComponent<WastelandMenuUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);   // survive scene changes (warp to 3D world etc.)
        EnsureEventSystem();
        BuildUI();
        if (_panel) _panel.SetActive(false);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F10)) Toggle();
        if (_panel != null && _panel.activeSelf && Input.GetKeyDown(KeyCode.Escape)) _panel.SetActive(false);
    }

    public void Toggle()
    {
        if (_panel == null) return;
        bool show = !_panel.activeSelf;
        _panel.SetActive(show);
        if (show)
        {
            var p = PlayerEntity.Instance;
            _nameField.text = p != null ? p.PlayerName : PlayerPrefs.GetString("PlayerName", "Survivor");
            _panel.transform.SetAsLastSibling();
        }
    }

    void WipeAndRestart()
    {
        string name = string.IsNullOrWhiteSpace(_nameField.text) ? "Survivor" : _nameField.text.Trim();
        PlayerPrefs.SetString("PlayerName", name);
        PlayerPrefs.Save();
        SaveManager.Instance?.DeleteSave();
        // Reload the current scene from scratch.
        var active = SceneManager.GetActiveScene();
        Time.timeScale = 1f;
        SceneManager.LoadScene(active.buildIndex >= 0 ? active.buildIndex : 0);
    }

    void ApplyName()
    {
        string name = string.IsNullOrWhiteSpace(_nameField.text) ? "Survivor" : _nameField.text.Trim();
        PlayerPrefs.SetString("PlayerName", name);
        PlayerPrefs.Save();
        var p = PlayerEntity.Instance;
        if (p != null) p.PlayerName = name;
        HUDController.Instance?.AddChatLine($"<color=#FFD966>[SYS]:</color> Name set to {name}.");
    }

    // ── build ────────────────────────────────────────────────────────────
    void BuildUI()
    {
        var canvasGo = new GameObject("WastelandMenuCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 550;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // (No always-on corner button — it cluttered the top-left and overlapped the hover readout.
        //  Open this menu with F10 instead. See Update().)

        // Panel (centered).
        const float PW = 440, PH = 250;
        var panelRt = NewUI("Panel", canvasGo.transform);
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(PW, PH);
        panelRt.anchoredPosition = Vector2.zero;
        _panel = panelRt.gameObject;
        _panel.AddComponent<Image>().color = C_Panel;

        // Title bar
        var bar = NewUI("Bar", panelRt); Place(bar, 0, 0, PW, 36);
        bar.gameObject.AddComponent<Image>().color = C_Bar;
        var titleRt = NewUI("Title", bar); Stretch(titleRt);
        var title = titleRt.gameObject.AddComponent<TextMeshProUGUI>();
        title.text = "WASTELAND MENU"; title.fontSize = 17; title.color = C_Gold;
        title.fontStyle = FontStyles.Bold; title.alignment = TextAlignmentOptions.Center;
        title.raycastTarget = false;

        var closeRt = NewUI("Close", bar); Place(closeRt, PW - 32, 4, 28, 28);
        var closeImg = closeRt.gameObject.AddComponent<Image>(); closeImg.color = C_Danger;
        var closeBtn = closeRt.gameObject.AddComponent<Button>(); closeBtn.targetGraphic = closeImg;
        closeBtn.onClick.AddListener(() => _panel.SetActive(false));
        var cxRt = NewUI("X", closeRt); Stretch(cxRt);
        var cx = cxRt.gameObject.AddComponent<TextMeshProUGUI>();
        cx.text = "X"; cx.fontSize = 15; cx.alignment = TextAlignmentOptions.Center;
        cx.color = Color.white; cx.raycastTarget = false;

        // "Player name" label
        var nameLblRt = NewUI("NameLbl", panelRt); Place(nameLblRt, 24, 52, PW - 48, 22);
        var nameLbl = nameLblRt.gameObject.AddComponent<TextMeshProUGUI>();
        nameLbl.text = "Player name"; nameLbl.fontSize = 14;
        nameLbl.color = new Color(0.8f, 0.8f, 0.8f, 1f);
        nameLbl.alignment = TextAlignmentOptions.Left; nameLbl.raycastTarget = false;

        // Name input field
        var fieldRt = NewUI("NameField", panelRt); Place(fieldRt, 24, 76, PW - 48, 36);
        var fieldImg = fieldRt.gameObject.AddComponent<Image>(); fieldImg.color = C_Field;
        _nameField = fieldRt.gameObject.AddComponent<TMP_InputField>();
        var textAreaRt = NewUI("TextArea", fieldRt); Stretch(textAreaRt);
        textAreaRt.offsetMin = new Vector2(8, 4); textAreaRt.offsetMax = new Vector2(-8, -4);
        textAreaRt.gameObject.AddComponent<RectMask2D>();
        var inputTextRt = NewUI("Text", textAreaRt); Stretch(inputTextRt);
        var inputText = inputTextRt.gameObject.AddComponent<TextMeshProUGUI>();
        inputText.fontSize = 16; inputText.color = Color.white;
        inputText.alignment = TextAlignmentOptions.Left;
        _nameField.textViewport = textAreaRt;
        _nameField.textComponent = inputText;
        _nameField.text = "Survivor";
        _nameField.onEndEdit.AddListener(_ => ApplyName());

        // Apply-name button
        var applyRt = NewUI("ApplyBtn", panelRt); Place(applyRt, 24, 122, (PW - 56) / 2f, 38);
        var applyImg = applyRt.gameObject.AddComponent<Image>();
        applyImg.color = new Color(0.30f, 0.45f, 0.20f, 1f);
        var applyBtn = applyRt.gameObject.AddComponent<Button>(); applyBtn.targetGraphic = applyImg;
        applyBtn.onClick.AddListener(ApplyName);
        AddLabel(applyRt, "Set Name", 15);

        // Wipe button
        var wipeRt = NewUI("WipeBtn", panelRt);
        Place(wipeRt, 24 + (PW - 56) / 2f + 8, 122, (PW - 56) / 2f, 38);
        var wipeImg = wipeRt.gameObject.AddComponent<Image>(); wipeImg.color = C_Danger;
        var wipeBtn = wipeRt.gameObject.AddComponent<Button>(); wipeBtn.targetGraphic = wipeImg;
        wipeBtn.onClick.AddListener(WipeAndRestart);
        AddLabel(wipeRt, "Wipe Save & Restart", 14);

        // hint
        var hintRt = NewUI("Hint", panelRt); Place(hintRt, 24, 176, PW - 48, 60);
        var hint = hintRt.gameObject.AddComponent<TextMeshProUGUI>();
        hint.text = "Wipe deletes your save and reloads the world from scratch with the name above.\nToggle this menu any time with F10.";
        hint.fontSize = 12; hint.color = new Color(0.65f, 0.65f, 0.65f, 1f);
        hint.alignment = TextAlignmentOptions.TopLeft; hint.raycastTarget = false;
    }

    static void AddLabel(RectTransform parent, string text, float size)
    {
        var rt = NewUI("Lbl", parent); Stretch(rt);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = Color.white;
        t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
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
