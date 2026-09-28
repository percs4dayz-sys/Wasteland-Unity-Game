using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// OSRS-style mouseover text in the top-left: what the cursor is over and what a click will do,
/// e.g. "Chop down Dead Tree / 3 more options" (right-click lists them). Over the UI, inventory and
/// equipment slots push their item names via SetHoverName / ClearHover.
///
/// It sits just right of the player frame on its own canvas. Drawn on a shared canvas it ended up
/// underneath the frame, which is why it seemed to have stopped working.
///
/// Self-creates at scene start — no scene wiring needed.
/// </summary>
public class HoverInspector : MonoBehaviour
{
    public static HoverInspector Instance { get; private set; }

    private TMP_Text      _label;
    private RectTransform _bgRt;
    private CanvasScaler  _scaler;
    private int           _scaleVersion = -1;
    private Camera        _cam;
    private string        _uiHoverName;   // set by UI slots while hovered; null otherwise
    private string        _shown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance == null)
            new GameObject("HoverInspector").AddComponent<HoverInspector>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildLabel();
    }

    void BuildLabel()
    {
        var canvasGo = new GameObject("HoverLabelCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 470;                     // above the player frame (465), below menus
        _scaler = canvasGo.GetComponent<CanvasScaler>();

        var bgGo = new GameObject("HoverLabelBG", typeof(RectTransform), typeof(Image));
        bgGo.transform.SetParent(canvasGo.transform, false);
        var bg = bgGo.GetComponent<Image>();
        UITheme.Panel(bg, 0.85f);
        bg.raycastTarget = false;
        _bgRt = bgGo.GetComponent<RectTransform>();
        _bgRt.anchorMin = _bgRt.anchorMax = new Vector2(0f, 1f);
        _bgRt.pivot = new Vector2(0f, 1f);
        _bgRt.anchoredPosition = new Vector2(298f, -16f);   // just right of the player frame (16 + 272 wide)
        _bgRt.sizeDelta = new Vector2(200f, 34f);

        var go = new GameObject("HoverLabel", typeof(RectTransform));
        go.transform.SetParent(bgGo.transform, false);
        _label = go.AddComponent<TextMeshProUGUI>();
        _label.fontSize = 20;                          // set before styling (TMP defaults otherwise)
        UITheme.Label(_label, 20, UITheme.Text, shadow: true, upper: false);
        _label.raycastTarget = false;
        _label.alignment = TextAlignmentOptions.MidlineLeft;
        _label.margin = new Vector4(10f, 0f, 10f, 0f);
        _label.textWrappingMode = TextWrappingModes.NoWrap;
        _label.richText = true;
        _label.text = "";
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        bgGo.SetActive(false);
    }

    /// <summary>Called by UI slots on pointer-enter.</summary>
    public void SetHoverName(string name) => _uiHoverName = name;

    /// <summary>Called by UI slots on pointer-exit.</summary>
    public void ClearHover() => _uiHoverName = null;

    void Update()
    {
        if (_label == null) return;
        if (_scaler != null && _scaleVersion != UITheme.ScaleVersion)
        {
            UITheme.ApplyScaler(_scaler, new Vector2(1920, 1080));   // follows the Interface Size setting
            _scaleVersion = UITheme.ScaleVersion;
        }
        if (_cam == null) _cam = Camera.main;

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        string shown;
        if (overUI) shown = _uiHoverName;              // UI slots manage the name themselves
        else if (ContextMenuUI.Blocking) shown = null; // the open menu already says it all
        else shown = WorldText();

        // Right-click to examine → chat (2D only; in 3D right-click opens the Choose Option menu).
        if (!overUI && !GameMode.Is3D && Input.GetMouseButtonDown(1) && _cam != null &&
            Physics.Raycast(_cam.ScreenPointToRay(Input.mousePosition), out var hit, 500f))
        {
            var ex = hit.collider.GetComponentInParent<IExaminable>();
            if (ex != null) HUDController.Emit($"<color=#88DDFF>[EXAMINE]:</color> {ex.ExamineText}");
        }

        ApplyLabel(shown);
    }

    /// <summary>"Chop down Dead Tree / 3 more options" for whatever the cursor is on (nothing if it's
    /// just ground). Same list the right-click menu shows.</summary>
    string WorldText()
    {
        if (_cam == null) return null;
        var things = WorldInteractables.UnderCursor(_cam.ScreenPointToRay(Input.mousePosition), 0.35f, out _, out _);
        if (things.Count == 0) return null;
        var first = things[0];
        int more = things.Count * 2 + 1;               // other actions, Walk here, Examines, Cancel
        return $"{WorldInteractables.Verb(first)} {WorldInteractables.Coloured(first)}" +
               $"<color=#B8B2A6> / {more} more option{(more == 1 ? "" : "s")}</color>";
    }

    void ApplyLabel(string text)
    {
        bool has = !string.IsNullOrEmpty(text);
        if (_bgRt.gameObject.activeSelf != has) _bgRt.gameObject.SetActive(has);
        if (!has) { _shown = null; return; }
        if (text == _shown) return;
        _shown = text;
        _label.text = text;
        _bgRt.sizeDelta = new Vector2(_label.GetPreferredValues(text).x + 24f, 34f);   // hug the text
    }
}
