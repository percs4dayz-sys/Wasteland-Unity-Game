using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// NPC dialogue, OSRS-style: each line shows in its own box — who's talking, what they say, and
/// "Click here to continue" ("Tap …" on a phone). Clicking / tapping the box, pressing Space, or talking to
/// the NPC again moves to the next line; after the last one the box closes. Walking away ends it too.
/// Like OSRS, the box takes the chat box's place while it's up (Hud3DChat hides) and the lines aren't
/// copied into chat — one box, never two saying the same thing. Auto-spawns so Instance always exists.
/// </summary>
public class DialogueUI : MonoBehaviour
{
    public static DialogueUI Instance { get; private set; }

    const float WalkAwayDistance = 8f;
    const int MaxPageChars = 230;   // about four lines in the box — longer speeches page on "continue", like OSRS

    private string   _currentSpeaker;
    private string[] _lines;
    private int      _lineIndex;

    GameObject _box;
    TextMeshProUGUI _name, _text, _prompt;
    Vector3 _startedAt;
    GameObject _bankGuideButton;
    string _bankGuideSpeaker;
    public void EnableBankGuideFor(string speaker) => _bankGuideSpeaker = speaker;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("DialogueUI (auto)").AddComponent<DialogueUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildBox();
    }

    public void StartDialogue(string speaker, params string[] lines)
    {
        if (lines == null || lines.Length == 0) return;
        _currentSpeaker = speaker;
        _lines = Paginate(lines);
        _lineIndex = 0;
        _startedAt = PlayerEntity.Instance != null ? PlayerEntity.Instance.transform.position : Vector3.zero;
        ShowNextLine();
    }

    public void StartDialogue(string speaker, string line) => StartDialogue(speaker, new[] { line });

    /// <summary>Breaks long speeches into box-sized pages at sentence ends, so the text stays big enough to
    /// read on a phone instead of shrinking to fit. A single over-long sentence keeps its own page.</summary>
    static string[] Paginate(string[] lines)
    {
        var pages = new System.Collections.Generic.List<string>();
        var page = new System.Text.StringBuilder();
        void Add(string sentence)
        {
            if (page.Length > 0 && page.Length + 1 + sentence.Length > MaxPageChars) { pages.Add(page.ToString()); page.Clear(); }
            if (page.Length > 0) page.Append(' ');
            page.Append(sentence);
        }
        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line) || line.Length <= MaxPageChars) { pages.Add(line); continue; }
            int start = 0;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if ((c != '.' && c != '!' && c != '?') || (i + 1 < line.Length && line[i + 1] != ' ')) continue;
                string sentence = line.Substring(start, i + 1 - start).Trim();
                start = i + 1;
                if (sentence.Length > 0) Add(sentence);
            }
            string rest = line.Substring(start).Trim();
            if (rest.Length > 0) Add(rest);
            if (page.Length > 0) { pages.Add(page.ToString()); page.Clear(); }
        }
        return pages.ToArray();
    }

    /// <summary>Is the dialogue box on screen (the walk-up "[E] Talk to …" hint stays out of its way).</summary>
    public static bool IsShowing => Instance != null && Instance._box != null && Instance._box.activeSelf;

    public bool IsConversationWith(string speaker)
        => _lines != null && _currentSpeaker == speaker && _lineIndex < _lines.Length;

    public void ShowNextLine()
    {
        if (_lines == null || _lineIndex >= _lines.Length)
        {
            _lines = null;
            _currentSpeaker = null;
            Hide();
            return;
        }

        // Don't advance while the player is typing in chat.
        if (ChatInput.IsTyping) return;

        string line = _lines[_lineIndex];
        _name.text = _currentSpeaker;
        _text.text = line;
        _prompt.text = Application.isMobilePlatform ? "Tap here to continue" : "Click here to continue";
        _box.SetActive(true);
        _bankGuideButton.SetActive(_currentSpeaker == _bankGuideSpeaker);

        _lineIndex++;
    }

    void Hide()
    {
        if (_box != null) _box.SetActive(false);
    }

    /// <summary>The box was clicked / tapped: next line, or close after the last one.</summary>
    void Continue()
    {
        if (BankGuideUI.IsShowing) return;
        if (TouchInput.SuppressClick) return;   // the tail of a long-press
        if (_lines != null && _lineIndex < _lines.Length) ShowNextLine();
        else { _lines = null; _currentSpeaker = null; Hide(); }
    }

    void Update()
    {
        if (BankGuideUI.IsShowing) return;
        if (_box == null || !_box.activeSelf) return;
        if (!ChatInput.IsTyping && Input.GetKeyDown(KeyCode.Space)) Continue();
        // Walked off (or the world changed under us): the conversation's over.
        var p = PlayerEntity.Instance;
        if (p == null || (p.transform.position - _startedAt).sqrMagnitude > WalkAwayDistance * WalkAwayDistance)
        {
            _lines = null; _currentSpeaker = null; Hide();
        }
    }

    // ── build ────────────────────────────────────────────────────────────
    void BuildBox()
    {
        var canvasGo = new GameObject("DialogueCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 480;   // over the HUD, under the bank / panels (520+) and menus
        UITheme.ApplyScaler(canvasGo.GetComponent<CanvasScaler>(), new Vector2(1920, 1080));   // same as the chat

        // In the chat box's corner (bottom-left, which hides while this is up) — a little bigger than the
        // chat so a long line stays readable on a phone.
        var box = Rect("DialogueBox", canvasGo.transform, Vector2.zero, Vector2.zero);
        box.pivot = Vector2.zero;
        box.anchoredPosition = new Vector2(16f, 14f);
        box.sizeDelta = new Vector2(720f, 290f);
        var bg = box.gameObject.AddComponent<Image>();
        UITheme.Panel(bg, 0.96f);
        box.gameObject.AddComponent<ClickTarget>().onClick = Continue;   // the whole box is the button
        _box = box.gameObject;

        _name = Label(box, "Speaker", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -14f), 40f);
        UITheme.Label(_name, 26f, UITheme.Amber);
        _name.alignment = TextAlignmentOptions.Center;

        var bankTopic = Rect("BankGuide", box, new Vector2(1, 1), new Vector2(1, 1));
        bankTopic.pivot = Vector2.one;
        bankTopic.anchoredPosition = new Vector2(-16, -12);
        bankTopic.sizeDelta = new Vector2(130, 38);
        var bankImage = bankTopic.gameObject.AddComponent<Image>();
        bankImage.color = UITheme.TileTint; UITheme.Tile(bankImage);
        var bankButton = bankTopic.gameObject.AddComponent<Button>();
        bankButton.targetGraphic = bankImage;
        bankButton.onClick.AddListener(() => BankGuideUI.Show(_currentSpeaker));
        var bankLabel = Label(bankTopic, "Label", Vector2.zero, Vector2.one, Vector2.zero, 0);
        bankLabel.text = "Town banks"; bankLabel.fontSize = 21; bankLabel.alignment = TextAlignmentOptions.Center;
        bankLabel.color = UITheme.Amber;
        _bankGuideButton = bankTopic.gameObject;
        _bankGuideButton.SetActive(false);

        _text = Label(box, "Line", new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, 0f);
        _text.rectTransform.offsetMin = new Vector2(40f, 58f);
        _text.rectTransform.offsetMax = new Vector2(-40f, -58f);
        if (UITheme.BodyFont != null) _text.font = UITheme.BodyFont;
        _text.color = UITheme.Text;
        _text.alignment = TextAlignmentOptions.Center;
        _text.textWrappingMode = TextWrappingModes.Normal;
        _text.enableAutoSizing = true; _text.fontSizeMin = 16f; _text.fontSizeMax = 28f;

        _prompt = Label(box, "Continue", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 14f), 32f);
        _prompt.rectTransform.pivot = new Vector2(0.5f, 0f);
        if (UITheme.BodyFont != null) _prompt.font = UITheme.BodyFont;
        _prompt.fontSize = 22f;
        _prompt.color = new Color(0.55f, 0.78f, 1f, 1f);   // OSRS's blue "continue"
        _prompt.alignment = TextAlignmentOptions.Center;

        _box.SetActive(false);
    }

    static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    static TextMeshProUGUI Label(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, float height)
    {
        var rt = Rect(name, parent, anchorMin, anchorMax);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        if (height > 0f) rt.sizeDelta = new Vector2(0f, height);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.raycastTarget = false;
        return t;
    }

    /// <summary>Makes the dialogue box itself clickable / tappable.</summary>
    class ClickTarget : MonoBehaviour, IPointerClickHandler
    {
        public System.Action onClick;
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) onClick?.Invoke();
        }
    }
}
