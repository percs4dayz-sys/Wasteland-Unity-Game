using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// OSRS mobile's function button (the fingerprint on the left of the screen). Tap it to switch its mode on
/// or off; press and hold (or right-click) to pick the mode:
///   • Tap-to-drop — the inventory turns red and tapping an item drops it (desktop: shift-click drop).
///   • Single-tap  — a tap opens the minimenu instead of doing the default action.
///   • Keyboard    — tapping the button opens the chat keyboard.
/// Phones only, and hidden when Settings ▸ Controls ▸ Show the function button is off. Self-building.
/// </summary>
public class FunctionButton : MonoBehaviour
{
    public enum Mode { TapToDrop, SingleTap, Keyboard }

    const string ModeKey = "touch.functionMode";

    /// <summary>The mode the button switches (kept between sessions).</summary>
    public static Mode Current
    {
        get => (Mode)Mathf.Clamp(PlayerPrefs.GetInt(ModeKey, 0), 0, 2);
        private set { PlayerPrefs.SetInt(ModeKey, (int)value); PlayerPrefs.Save(); }
    }
    /// <summary>Is the mode switched on right now (this session only, like OSRS).</summary>
    public static bool Active { get; private set; }

    public static bool TapToDrop => Active && Current == Mode.TapToDrop;
    public static bool SingleTap => Active && Current == Mode.SingleTap;

    static FunctionButton _instance;

    static readonly Color Idle = new Color(0.09f, 0.08f, 0.07f, 0.85f);
    static readonly Color DropOn = new Color(0.75f, 0.12f, 0.08f, 0.95f);
    static readonly Color TapOn = new Color(0.85f, 0.62f, 0.12f, 0.95f);

    GameObject _root;
    Image _disc;
    TextMeshProUGUI _label;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("FunctionButton (auto)");
        _instance = go.AddComponent<FunctionButton>();
        DontDestroyOnLoad(go);
    }

    void Awake() => Build();

    void Update()
    {
        bool show = Application.isMobilePlatform && TouchSettings.ShowFunctionButton && PlayerEntity.Instance != null;
        if (_root.activeSelf != show)
        {
            _root.SetActive(show);
            if (!show && Active) SetActive(false);   // hidden (setting off / left the world): drop the mode too
        }
    }

    // The button itself lives on a child; clicks bubble here through ClickRelay.
    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Right) { OpenModeMenu(e.position); return; }
        if (TouchInput.SuppressClick) return;   // the tail of the long-press that opened the mode menu
        if (Current == Mode.Keyboard) { Hud3DChat.Instance?.OpenInput(); return; }
        SetActive(!Active);
    }

    void OpenModeMenu(Vector2 at)
    {
        if (ContextMenuUI.Instance == null) return;
        var options = new List<(string, System.Action)>();
        foreach (Mode m in System.Enum.GetValues(typeof(Mode)))
        {
            var mode = m;
            string mark = mode == Current ? "<color=#FFB000>▸ </color>" : "";
            options.Add(($"{mark}{Label(mode)}", () => { SetActive(false); Current = mode; Refresh(); }));
        }
        options.Add(("Cancel", null));
        ContextMenuUI.Instance.Open(at, "Function mode", options);
    }

    void SetActive(bool on)
    {
        Active = on;
        InventoryPanelUI.Instance?.SetDropMode(TapToDrop);
        Refresh();
    }

    void Refresh()
    {
        _disc.color = !Active ? Idle : Current == Mode.TapToDrop ? DropOn : TapOn;
        _label.text = Current switch { Mode.TapToDrop => "DROP", Mode.SingleTap => "1-TAP", _ => "KEYS" };
    }

    static string Label(Mode m) => m switch
    {
        Mode.TapToDrop => "Tap-to-drop",
        Mode.SingleTap => "Single-tap",
        _ => "Keyboard",
    };

    // ── build ────────────────────────────────────────────────────────────
    void Build()
    {
        var canvasGo = new GameObject("FunctionButtonCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 460;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Left edge, above the chat box — where OSRS keeps it.
        var rt = new GameObject("FunctionButton", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(canvasGo.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.36f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(22f, 0f);
        rt.sizeDelta = new Vector2(96f, 96f);
        _root = rt.gameObject;

        _disc = _root.AddComponent<Image>();
        _disc.sprite = DiscSprite();
        _root.AddComponent<ClickRelay>().target = this;

        var ridgesRt = new GameObject("Print", typeof(RectTransform)).GetComponent<RectTransform>();
        ridgesRt.SetParent(rt, false);
        ridgesRt.anchorMin = Vector2.zero; ridgesRt.anchorMax = Vector2.one;
        ridgesRt.offsetMin = ridgesRt.offsetMax = Vector2.zero;
        var ridges = ridgesRt.gameObject.AddComponent<Image>();
        ridges.sprite = RidgesSprite();
        ridges.color = new Color(0.96f, 0.9f, 0.78f, 1f);
        ridges.raycastTarget = false;

        var lbl = new GameObject("Label", typeof(RectTransform)).GetComponent<RectTransform>();
        lbl.SetParent(rt, false);
        lbl.anchorMin = new Vector2(0f, 0f); lbl.anchorMax = new Vector2(1f, 0f);
        lbl.pivot = new Vector2(0.5f, 1f);
        lbl.anchoredPosition = new Vector2(0f, -2f);
        lbl.sizeDelta = new Vector2(20f, 26f);
        _label = lbl.gameObject.AddComponent<TextMeshProUGUI>();
        UITheme.Label(_label, 18f, UITheme.Text, shadow: true);
        _label.alignment = TextAlignmentOptions.Center;

        Refresh();
        _root.SetActive(false);
    }

    /// <summary>Passes the button's clicks up to the FunctionButton (which isn't on the canvas).</summary>
    class ClickRelay : MonoBehaviour, IPointerClickHandler
    {
        public FunctionButton target;
        public void OnPointerClick(PointerEventData e) => target.OnPointerClick(e);
    }

    const int IconSize = 96;

    /// <summary>The round badge: plain white (the Image tints it idle / red / amber), soft edge.</summary>
    static Sprite DiscSprite() => Paint((p, r) => Mathf.Clamp01(IconSize / 2f - 1f - r));

    /// <summary>Fingerprint ridges and a thin rim, drawn over the disc in a fixed light colour. The ridges are
    /// squashed ellipses broken into arcs, so they read as a print rather than a target.</summary>
    static Sprite RidgesSprite() => Paint((p, r) =>
    {
        var q = new Vector2(p.x * 1.25f, p.y + 6f);
        float rr = q.magnitude;
        float ring = Mathf.Abs(Mathf.Repeat(rr, 7f) - 3.5f);
        float gap = Mathf.Repeat(Mathf.Atan2(q.y, q.x) * 3f + rr * 0.35f, Mathf.PI * 2f);
        float ridge = rr < 30f && gap > 0.7f ? Mathf.Clamp01(1.6f - ring) : 0f;
        float rim = Mathf.Clamp01(1.5f - Mathf.Abs(r - (IconSize / 2f - 4f)));
        return Mathf.Max(ridge, rim);
    });

    /// <summary>White texture whose alpha is alpha(offset from centre, distance from centre).</summary>
    static Sprite Paint(System.Func<Vector2, float, float> alpha)
    {
        const int N = IconSize;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[N * N];
        var c = new Vector2(N / 2f, N / 2f);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f) - c;
                px[y * N + x] = new Color(1f, 1f, 1f, alpha(p, p.magnitude));
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }
}
