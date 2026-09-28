using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Settings panel — self-building, opened with O (keyboard), Start (controller) or the SETTINGS tab (the
/// only way on a phone). Volume sliders (Master + Music), Interface Size, and the OSRS-mobile Controls
/// (minimenu long-press time, the vibration toggles, the function button — see TouchSettings), all
/// persisted to PlayerPrefs. Controller-navigable via ControllerUI (auto-selects the first slider;
/// A adjusts, B closes).
///
/// Keybind rebinding is planned as a follow-up (needs a central input map) — see notes in chat.
/// </summary>
public class SettingsUI : MonoBehaviour
{
    public static SettingsUI Instance { get; private set; }

    const string MasterKey = "vol_master";
    const string MusicKey  = "vol_music";

    GameObject _backdrop, _panel;
    bool _musicApplied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("SettingsUI (auto)").AddComponent<SettingsUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        ApplySavedVolumes();
        Build();
        _backdrop.SetActive(false);
    }

    void Update()
    {
        // Apply saved music volume once the MusicManager exists (it bootstraps separately).
        if (!_musicApplied && MusicManager.Instance != null)
        {
            MusicManager.Instance.Volume = PlayerPrefs.GetFloat(MusicKey, 0.5f);
            _musicApplied = true;
        }

        if (ChatInput.IsTyping) return;
        if (Input.GetKeyDown(KeyCode.O) || Input.GetKeyDown(KeyCode.JoystickButton7)) Toggle();   // Start button
        if (IsOpen && (Input.GetKeyDown(KeyCode.Escape))) Close();
    }

    public bool IsOpen => _backdrop != null && _backdrop.activeSelf;
    public void Toggle() { if (IsOpen) Close(); else Open(); }
    public void Open()  { _backdrop.SetActive(true); _panel.transform.SetAsLastSibling(); }
    public void Close() { _backdrop.SetActive(false); }

    static void ApplySavedVolumes()
    {
        AudioListener.volume = PlayerPrefs.GetFloat(MasterKey, 1f);
        // Music applied in Update once MusicManager is alive.
    }

    // ── build ─────────────────────────────────────────────────────────────
    void Build()
    {
        var canvasGo = new GameObject("SettingsCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1100;   // above other panels, and above the title menu (1000) it opens from too
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        EnsureEventSystem();

        _backdrop = NewRect("Backdrop", canvasGo.transform, out var brt);
        Stretch(brt);
        var bd = _backdrop.AddComponent<Image>();
        bd.color = new Color(0f, 0f, 0f, 0.6f);   // modal dim — settings is a real pause-style panel

        _panel = NewRect("Panel", brt, out var prt);
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(500, 720);
        _panel.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.11f, 0.99f);

        var titleGo = NewRect("Title", prt, out var trt);
        trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0.5f, 1);
        trt.anchoredPosition = new Vector2(0, -14); trt.sizeDelta = new Vector2(0, 40);
        var title = titleGo.AddComponent<TextMeshProUGUI>();
        title.text = "SETTINGS"; title.fontSize = 26; title.alignment = TextAlignmentOptions.Center;
        title.color = new Color(1f, 0.85f, 0.45f); title.fontStyle = FontStyles.Bold; title.raycastTarget = false;

        MakeSlider(prt, "Master Volume", -90, PlayerPrefs.GetFloat(MasterKey, 1f), v =>
        {
            AudioListener.volume = v; PlayerPrefs.SetFloat(MasterKey, v);
        });
        MakeSlider(prt, "Music Volume", -160, PlayerPrefs.GetFloat(MusicKey, 0.5f), v =>
        {
            if (MusicManager.Instance != null) MusicManager.Instance.Volume = v;
            PlayerPrefs.SetFloat(MusicKey, v);
        });

        // Interface size: 80%–150% of the 1080p layout (UITheme applies it to every HUD canvas). Applied
        // when you let go of the slider, so the panel doesn't resize under the cursor mid-drag.
        float range = UITheme.MaxScale - UITheme.MinScale;
        TMP_Text sizeLbl = null;
        var sizeSlider = MakeSlider(prt, "Interface Size", -230, (UITheme.InterfaceScale - UITheme.MinScale) / range, v =>
        {
            if (sizeLbl != null) sizeLbl.text = $"Interface Size  {Mathf.RoundToInt((UITheme.MinScale + v * range) * 100)}%";
        }, out sizeLbl);
        sizeLbl.text = $"Interface Size  {Mathf.RoundToInt(UITheme.InterfaceScale * 100)}%";
        sizeSlider.gameObject.AddComponent<ApplyOnRelease>().onRelease = () =>
            UITheme.InterfaceScale = UITheme.MinScale + sizeSlider.value * range;

        // ── Controls: OSRS mobile's options (they only matter on a touch screen) ──
        MakeHeader(prt, "CONTROLS", -300);
        const int MinMs = TouchSettings.MinLongPressMs, MaxMs = TouchSettings.MaxLongPressMs;
        TMP_Text pressLbl = null;
        MakeSlider(prt, "Minimenu long-press time", -330, (TouchSettings.LongPressMs - MinMs) / (float)(MaxMs - MinMs), v =>
        {
            int ms = Mathf.RoundToInt((MinMs + v * (MaxMs - MinMs)) / 25f) * 25;
            TouchSettings.LongPressMs = ms;
            if (pressLbl != null) pressLbl.text = $"Minimenu long-press time  {ms} ms";
        }, out pressLbl);
        pressLbl.text = $"Minimenu long-press time  {TouchSettings.LongPressMs} ms";

        MakeToggle(prt, "Vibrate when the minimenu opens", -396, TouchSettings.VibrateOnMenuOpen, on => TouchSettings.VibrateOnMenuOpen = on);
        MakeToggle(prt, "Vibrate on hovering minimenu entries", -432, TouchSettings.VibrateOnMenuHover, on => TouchSettings.VibrateOnMenuHover = on);
        MakeToggle(prt, "Vibrate on drag", -468, TouchSettings.VibrateOnDrag, on => TouchSettings.VibrateOnDrag = on);
        MakeToggle(prt, "Vibrate on interaction", -504, TouchSettings.VibrateOnInteraction, on => TouchSettings.VibrateOnInteraction = on);
        MakeToggle(prt, "Show the function button", -540, TouchSettings.ShowFunctionButton, on => TouchSettings.ShowFunctionButton = on);

        // Close button
        var closeGo = NewRect("Close", prt, out var crt);
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0); crt.pivot = new Vector2(0.5f, 0);
        crt.anchoredPosition = new Vector2(0, 20); crt.sizeDelta = new Vector2(200, 46);
        var cimg = closeGo.AddComponent<Image>(); cimg.color = new Color(0.22f, 0.45f, 0.25f);
        var cbtn = closeGo.AddComponent<Button>(); cbtn.targetGraphic = cimg;
        cbtn.onClick.AddListener(Close);
        var cl = NewRect("Lbl", crt, out var clrt); Stretch(clrt);
        var clt = cl.AddComponent<TextMeshProUGUI>();
        clt.text = "Close (Esc)"; clt.fontSize = 18; clt.alignment = TextAlignmentOptions.Center;
        clt.color = Color.white; clt.raycastTarget = false;

        var hint = NewRect("Hint", prt, out var hrt);
        hrt.anchorMin = new Vector2(0, 0); hrt.anchorMax = new Vector2(1, 0); hrt.pivot = new Vector2(0.5f, 0);
        hrt.anchoredPosition = new Vector2(0, 74); hrt.sizeDelta = new Vector2(-24, 24);
        var ht = hint.AddComponent<TextMeshProUGUI>();
        ht.text = "Open with O, the Start button, or the SETTINGS tab"; ht.fontSize = 13;
        ht.alignment = TextAlignmentOptions.Center; ht.color = new Color(0.7f, 0.7f, 0.7f); ht.raycastTarget = false;
    }

    Slider MakeSlider(Transform parent, string label, float y, float value, UnityEngine.Events.UnityAction<float> onChange)
        => MakeSlider(parent, label, y, value, onChange, out _);

    Slider MakeSlider(Transform parent, string label, float y, float value, UnityEngine.Events.UnityAction<float> onChange,
                      out TMP_Text labelText)
    {
        var row = NewRect(label, parent, out var rrt);
        rrt.anchorMin = new Vector2(0, 1); rrt.anchorMax = new Vector2(1, 1); rrt.pivot = new Vector2(0.5f, 1);
        rrt.anchoredPosition = new Vector2(0, y); rrt.sizeDelta = new Vector2(-40, 54);

        var lblGo = NewRect("Label", rrt, out var lrt);
        lrt.anchorMin = new Vector2(0, 0.5f); lrt.anchorMax = new Vector2(1, 1);
        lrt.offsetMin = new Vector2(6, 0); lrt.offsetMax = new Vector2(-6, 0);
        var lt = lblGo.AddComponent<TextMeshProUGUI>();
        lt.text = label; lt.fontSize = 16; lt.color = Color.white; lt.raycastTarget = false;
        lt.alignment = TextAlignmentOptions.MidlineLeft;
        labelText = lt;

        // Slider track
        var sGo = NewRect("Slider", rrt, out var srt);
        srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 0.5f);
        srt.offsetMin = new Vector2(6, 4); srt.offsetMax = new Vector2(-6, 0);
        var slider = sGo.AddComponent<Slider>();

        var bg = NewRect("BG", srt, out var bgrt); Stretch(bgrt);
        var bgi = bg.AddComponent<Image>(); bgi.color = new Color(0.05f, 0.05f, 0.05f, 1f);

        var fillArea = NewRect("Fill Area", srt, out var fart); Stretch(fart);
        var fill = NewRect("Fill", fart, out var frt);
        frt.anchorMin = new Vector2(0, 0); frt.anchorMax = new Vector2(1, 1); frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        var fi = fill.AddComponent<Image>(); fi.color = new Color(0.95f, 0.75f, 0.3f, 1f);

        var handleArea = NewRect("Handle Area", srt, out var hart); Stretch(hart);
        var handle = NewRect("Handle", hart, out var hrt2);
        hrt2.sizeDelta = new Vector2(16, 22);
        var hi = handle.AddComponent<Image>(); hi.color = Color.white;

        slider.fillRect = frt;
        slider.handleRect = hrt2;
        slider.targetGraphic = hi;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f; slider.maxValue = 1f;
        slider.value = Mathf.Clamp01(value);
        slider.onValueChanged.AddListener(onChange);
        slider.onValueChanged.AddListener(_ => PlayerPrefs.Save());
        return slider;
    }

    void MakeHeader(Transform parent, string text, float y)
    {
        var go = NewRect(text, parent, out var rt);
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = new Vector2(0, y); rt.sizeDelta = new Vector2(-40, 26);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = 15; t.fontStyle = FontStyles.Bold; t.characterSpacing = 2f;
        t.color = new Color(1f, 0.85f, 0.45f); t.alignment = TextAlignmentOptions.MidlineLeft; t.raycastTarget = false;
    }

    /// <summary>A checkbox row: box on the left, label beside it; the whole row is tappable.</summary>
    void MakeToggle(Transform parent, string label, float y, bool value, System.Action<bool> onChange)
    {
        var row = NewRect(label, parent, out var rrt);
        rrt.anchorMin = new Vector2(0, 1); rrt.anchorMax = new Vector2(1, 1); rrt.pivot = new Vector2(0.5f, 1);
        rrt.anchoredPosition = new Vector2(0, y); rrt.sizeDelta = new Vector2(-40, 32);
        row.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);   // makes the whole row a hit target

        var box = NewRect("Box", rrt, out var brt);
        brt.anchorMin = brt.anchorMax = new Vector2(0, 0.5f); brt.pivot = new Vector2(0, 0.5f);
        brt.anchoredPosition = new Vector2(6, 0); brt.sizeDelta = new Vector2(26, 26);
        var boxImg = box.AddComponent<Image>(); boxImg.color = new Color(0.05f, 0.05f, 0.05f, 1f);
        var check = NewRect("Check", brt, out var crt);
        crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one; crt.offsetMin = new Vector2(5, 5); crt.offsetMax = new Vector2(-5, -5);
        var checkImg = check.AddComponent<Image>(); checkImg.color = new Color(0.95f, 0.75f, 0.3f, 1f);

        var lblGo = NewRect("Label", rrt, out var lrt);
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = new Vector2(42, 0); lrt.offsetMax = Vector2.zero;
        var lt = lblGo.AddComponent<TextMeshProUGUI>();
        lt.text = label; lt.fontSize = 16; lt.color = Color.white; lt.raycastTarget = false;
        lt.alignment = TextAlignmentOptions.MidlineLeft;

        var toggle = row.AddComponent<Toggle>();
        toggle.targetGraphic = boxImg;
        toggle.graphic = checkImg;
        toggle.isOn = value;
        toggle.onValueChanged.AddListener(on => { onChange(on); PlayerPrefs.Save(); });
    }

    // ── helpers ───────────────────────────────────────────────────────────
    static GameObject NewRect(string name, Transform parent, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        rt = go.GetComponent<RectTransform>();
        return go;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() == null)
            DontDestroyOnLoad(new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)));
    }
}

/// <summary>Runs an action once a slider settles — when the mouse is released, or ~0.3 s after the last
/// keyboard/controller step — so a setting that re-lays-out the UI (Interface Size) never fires mid-drag.</summary>
public class ApplyOnRelease : MonoBehaviour
{
    public System.Action onRelease;
    float _changedAt = -1f;

    void Awake() => GetComponent<Slider>().onValueChanged.AddListener(_ => _changedAt = Time.unscaledTime);

    void Update()
    {
        if (_changedAt < 0f || Input.GetMouseButton(0) || Time.unscaledTime - _changedAt < 0.3f) return;
        Flush();
    }

    void OnDisable() => Flush();   // closing Settings right after a change still applies it

    void Flush()
    {
        if (_changedAt < 0f) return;
        _changedAt = -1f;
        onRelease?.Invoke();
    }
}
