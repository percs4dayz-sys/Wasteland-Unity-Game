using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Self-building chat/log box for the 3D world (the rich 2D HUD with its chat is scene-wired and
/// absent in 3D). Shows the newest messages bottom-left. Listens to HUDController.Emit (routed
/// combat/use/beast/system lines) plus the skilling/combat/crafting managers so gather, XP and
/// craft feedback all surface. Only visible while in a 3D scene; the 2D HUD handles chat there.
/// Auto-spawned, zero setup.
///
/// Now also has a working input line: press Enter to type, Enter again to send. Lines starting with
/// '/' are parsed as commands (see ChatCommands — e.g. /stuck returns you to spawn). While the input
/// is focused, ChatInput.IsTyping is true so movement/hotkeys/clicks ignore the keystrokes.
///
/// Enter is ALSO Unity's UI "Submit" key (so is Space), and that used to leak: the Enter that opened
/// the chat also clicked whatever button was selected (in the editor, the DEV button), and a line left
/// selected after sending could be re-sent/re-opened by the next Enter or Space. So this runs before
/// the EventSystem, clears the UI selection before opening, ignores the opening Enter echoing into
/// the field, and lets go of the field completely once a line is sent or cancelled.
/// </summary>
[DefaultExecutionOrder(-200)]   // before the EventSystem, so the opening Enter can't also "Submit" a button
public class Hud3DChat : MonoBehaviour
{
    public static Hud3DChat Instance { get; private set; }

    const int MAX_LINES = 8;
    readonly List<string> _lines = new();

    GameObject _panel;
    RectTransform _panelRt;
    Image _panelBg, _inputBg;
    TMP_Text _text;
    TMP_InputField _input;
    int _submitFrame = -1;   // guards against the submit-Enter immediately re-opening the input
    int _openFrame = -10;    // the frame Enter opened the input (its own Enter mustn't submit an empty line)

    // The log box is see-through while you play (just shadowed text over the world) and fades its panel
    // in while you're typing or have the mouse over it.
    const float IdleAlpha = 0f, ActiveAlpha = 0.9f;
    static readonly Color InputIdle  = new Color(0.05f, 0.05f, 0.045f, 0.55f);
    static readonly Color InputFocus = new Color(0.07f, 0.065f, 0.06f, 0.95f);

    // Re-pointed each scene so we always listen to the live managers.
    SkillingManager _sm;
    CraftingManager _craft;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("Hud3DChat (auto)").AddComponent<Hud3DChat>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();
        HUDController.OnLine += Add;
    }

    void OnDestroy()
    {
        HUDController.OnLine -= Add;
        if (_sm != null)    _sm.OnMessage -= Add;
        if (_craft != null) _craft.OnMessage -= Add;
    }

    /// <summary>True while the player is typing in the 3D chat input (so other systems ignore keys).</summary>
    public bool IsInputFocused() => _input != null && _input.isFocused;

    /// <summary>True on the frame Enter opened the chat line (it takes focus at the end of that frame),
    /// so nothing else treats that same Enter as its own (e.g. ControllerUI's auto-select + Submit).</summary>
    public static bool OpeningThisFrame => Instance != null && Instance._openFrame == Time.frameCount;

    void Update()
    {
        // Only show in the 3D world with a player in it — not on the title / character-creation
        // screens; the 2D scenes have their own chat panel.
        bool show = GameMode.Is3D && PlayerEntity.Instance != null;
        // An NPC conversation takes the chat box's place (OSRS-style) — hide the log and the typing line under it.
        bool talking = DialogueUI.IsShowing;
        if (_panel != null && _panel.activeSelf != (show && !talking)) _panel.SetActive(show && !talking);
        if (_inputBg != null && _inputBg.gameObject.activeSelf == talking) _inputBg.gameObject.SetActive(!talking);

        // Fade the log panel in while typing / hovering, out while playing.
        if (_panelBg != null && _panel.activeSelf)
        {
            bool focused = _input != null && _input.isFocused;
            bool hover = RectTransformUtility.RectangleContainsScreenPoint(_panelRt, Input.mousePosition, null);
            float target = focused || hover ? ActiveAlpha : IdleAlpha;
            var c = _panelBg.color;
            if (!Mathf.Approximately(c.a, target))
            {
                c.a = Mathf.MoveTowards(c.a, target, Time.unscaledDeltaTime * 5f);
                _panelBg.color = c;
            }
            if (_inputBg != null) _inputBg.color = focused ? InputFocus : InputIdle;
        }

        // The input field needs an EventSystem to receive focus/typing. Most scenes ship one; create
        // a scene-local fallback only when the 3D world has none (not DontDestroyOnLoad, so it can't
        // collide with a 2D scene's own EventSystem after a scene change).
        if (GameMode.Is3D && EventSystem.current == null)
            new GameObject("EventSystem (auto)", typeof(EventSystem), typeof(StandaloneInputModule));

        // Enter opens the input line (unless we just sent on this same Enter press, which would
        // otherwise re-open it instantly).
        if (show && !talking && _input != null && !_input.isFocused && Time.frameCount != _submitFrame &&
            (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) &&
            !OtherTextFieldFocused())
        {
            OpenInput();
        }

        // Once the line is closed, don't leave it as the UI's selected object: a selected-but-closed
        // field treats Enter/Space ("Submit") as "send the old text again and re-open".
        var es = EventSystem.current;
        if (_input != null && es != null && !_input.isFocused && Time.frameCount != _openFrame &&
            es.currentSelectedGameObject == _input.gameObject)
            es.SetSelectedGameObject(null);

        // Keep our subscriptions pointed at the current (per-scene) managers.
        if (SkillingManager.Instance != _sm)
        {
            if (_sm != null) _sm.OnMessage -= Add;
            _sm = SkillingManager.Instance;
            if (_sm != null) _sm.OnMessage += Add;
        }
        if (CraftingManager.Instance != _craft)
        {
            if (_craft != null) _craft.OnMessage -= Add;
            _craft = CraftingManager.Instance;
            if (_craft != null) _craft.OnMessage += Add;
        }
    }

    /// <summary>Open the line for typing. Runs before the EventSystem, so clearing the selection here
    /// means this same Enter keypress can't also click the selected button. The phone's function button
    /// (Keyboard mode) calls it too.</summary>
    public void OpenInput()
    {
        var es = EventSystem.current;
        if (es != null) es.SetSelectedGameObject(null);
        _input.text = "";
        _input.ActivateInputField();     // takes focus in the field's LateUpdate, this frame
        _openFrame = Time.frameCount;
    }

    // Enter shouldn't steal focus from another text box (e.g. a name field) the player is typing in.
    bool OtherTextFieldFocused()
    {
        var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (sel == null || sel == _input.gameObject) return false;
        var other = sel.GetComponent<TMP_InputField>();
        return other != null && other.isFocused;
    }

    void OnSubmit(string text)
    {
        // The Enter that OPENED the line can reach the field right after it wakes up (its key event is
        // still queued) and "submit" an empty line — ignore that and stay open for typing.
        if (string.IsNullOrWhiteSpace(text) && Time.frameCount - _openFrame <= 2)
        {
            _input.ActivateInputField();
            return;
        }

        _submitFrame = Time.frameCount;
        if (!string.IsNullOrWhiteSpace(text) && !ChatCommands.TryHandle(text))
            Add($"{PlayerEntity.ChatLabel()}: {text}");
        _input.text = "";
        _input.DeactivateInputField();   // hand control back to movement; Enter re-opens it
    }

    void Add(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        _lines.Add(line);
        if (_lines.Count > MAX_LINES) _lines.RemoveAt(0);
        if (_text != null) _text.text = string.Join("\n", _lines);
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("Hud3DChatCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 455;
        UITheme.ApplyScaler(canvasGo.GetComponent<CanvasScaler>(), new Vector2(1920, 1080));

        var rt = new GameObject("ChatPanel", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(canvasGo.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);   // bottom-left
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(16, 56);           // sits just above the input bar
        rt.sizeDelta = new Vector2(600, 226);
        var panelImg = rt.gameObject.AddComponent<Image>();
        UITheme.Panel(panelImg, IdleAlpha);                  // faded in on hover / while typing (Update)
        panelImg.raycastTarget = true;                       // click the box to advance dialogue
        rt.gameObject.AddComponent<RectMask2D>();             // clip text to the box (no more spilling up-screen)
        rt.gameObject.AddComponent<DialogueClickAdvance>();   // left-click the box = "Continue" through NPC lines
        _panel = rt.gameObject;
        _panelRt = rt;
        _panelBg = panelImg;

        var txtRt = new GameObject("Log", typeof(RectTransform)).GetComponent<RectTransform>();
        txtRt.SetParent(rt, false);
        txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = new Vector2(12, 8); txtRt.offsetMax = new Vector2(-12, -8);
        _text = txtRt.gameObject.AddComponent<TextMeshProUGUI>();
        _text.font = UITheme.BodyFont;
        var shadow = UITheme.ShadowMaterial(_text.font);     // readable straight over the world
        if (shadow != null) _text.fontSharedMaterial = shadow;
        _text.fontSize = 19; _text.color = UITheme.Text;
        _text.lineSpacing = 6f;
        _text.alignment = TextAlignmentOptions.BottomLeft;   // newest at the bottom
        _text.raycastTarget = false;

        BuildInput(canvasGo.transform);

        _panel.SetActive(false);
    }

    /// <summary>Builds the bottom chat input line (background + placeholder hint + text + the
    /// TMP_InputField wiring). The placeholder doubles as a discoverability hint for the command.</summary>
    void BuildInput(Transform canvas)
    {
        var barRt = new GameObject("ChatInput", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        barRt.SetParent(canvas, false);
        barRt.anchorMin = barRt.anchorMax = new Vector2(0f, 0f);
        barRt.pivot = new Vector2(0f, 0f);
        barRt.anchoredPosition = new Vector2(16, 14);
        barRt.sizeDelta = new Vector2(600, 36);
        _inputBg = barRt.GetComponent<Image>();
        UITheme.Tile(_inputBg);
        _inputBg.color = InputIdle;

        // Viewport that clips the text to the bar.
        var areaRt = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
        areaRt.SetParent(barRt, false);
        areaRt.anchorMin = Vector2.zero; areaRt.anchorMax = Vector2.one;
        areaRt.offsetMin = new Vector2(12, 4); areaRt.offsetMax = new Vector2(-12, -4);

        var placeholder = new GameObject("Placeholder", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        placeholder.rectTransform.SetParent(areaRt, false);
        placeholder.rectTransform.anchorMin = Vector2.zero; placeholder.rectTransform.anchorMax = Vector2.one;
        placeholder.rectTransform.offsetMin = Vector2.zero; placeholder.rectTransform.offsetMax = Vector2.zero;
        placeholder.font = UITheme.BodyFont;
        placeholder.fontSize = 17; placeholder.color = UITheme.TextDim;
        placeholder.fontStyle = FontStyles.Italic;
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        placeholder.text = "Press Enter to chat  ·  /stuck if you're stuck";

        var textComp = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        textComp.rectTransform.SetParent(areaRt, false);
        textComp.rectTransform.anchorMin = Vector2.zero; textComp.rectTransform.anchorMax = Vector2.one;
        textComp.rectTransform.offsetMin = Vector2.zero; textComp.rectTransform.offsetMax = Vector2.zero;
        textComp.font = UITheme.BodyFont;
        textComp.fontSize = 18; textComp.color = UITheme.Text;
        textComp.alignment = TextAlignmentOptions.MidlineLeft;

        _input = barRt.gameObject.AddComponent<TMP_InputField>();
        _input.textViewport = areaRt;
        _input.textComponent = textComp;
        _input.placeholder = placeholder;
        _input.lineType = TMP_InputField.LineType.SingleLine;
        _input.caretColor = UITheme.Amber;
        _input.customCaretColor = true;
        _input.caretWidth = 2;
        _input.selectionColor = new Color(0.97f, 0.72f, 0.32f, 0.35f);
        _input.onSubmit.AddListener(OnSubmit);
    }
}
