using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>Contextual bottom-center controls for equipped weapon and owned beast specials.</summary>
public class SpecialAttackHUD : MonoBehaviour
{
    public static SpecialAttackHUD Instance { get; private set; }
    sealed class Control
    {
        public RectTransform rect;
        public Button button;
        public Image background, fill;
        public TMP_Text heading, title, status;
    }
    RectTransform _safeArea, _row;
    CanvasScaler _scaler;
    Control _weapon, _beast;
    const float Width = 264f, Gap = 12f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance == null) new GameObject("SpecialAttackHUD (auto)").AddComponent<SpecialAttackHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Build();
        Refresh();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }
    void Update() => Refresh();

    public void Refresh()
    {
        if (_row == null) return;
        UITheme.ApplyScaler(_scaler, new Vector2(1920, 1080));
        Rect safe = Screen.safeArea;
        _safeArea.anchorMin = new Vector2(safe.xMin / Mathf.Max(1, Screen.width), safe.yMin / Mathf.Max(1, Screen.height));
        _safeArea.anchorMax = new Vector2(safe.xMax / Mathf.Max(1, Screen.width), safe.yMax / Mathf.Max(1, Screen.height));

        var player = PlayerEntity.Instance;
        var combat = player != null ? player.GetComponent<ActionCombat3D>() : null;
        var beast = CompanionManager.Instance;
        bool showWeapon = combat != null && combat.HasWeaponSpecial;
        bool showBeast = player != null && beast != null && beast.HasSpecialAttack;
        _weapon.rect.gameObject.SetActive(showWeapon);
        _beast.rect.gameObject.SetActive(showBeast);
        _row.gameObject.SetActive(showWeapon || showBeast);
        float offset = showWeapon && showBeast ? (Width + Gap) * 0.5f : 0f;
        _weapon.rect.anchoredPosition = new Vector2(-offset, 0);
        _beast.rect.anchoredPosition = new Vector2(offset, 0);
        if (showWeapon)
        {
            float energy = CombatManager.Instance != null ? CombatManager.Instance.SpecialEnergy : 0f;
            string blocked = combat.SpecialBlockReason;
            Paint(_weapon, "WEAPON SPECIAL  [" + combat.specialKey + "]", combat.SpecialAttackName,
                blocked ?? $"Ready · {combat.specialEnergyCost:0}% energy",
                energy / CombatManager.MaxSpecialEnergy, blocked == null, UITheme.Amber);
        }
        if (showBeast)
        {
            string blocked = beast.SpecialBlockReason;
            Paint(_beast, "BEAST SPECIAL  [" + beast.frenzyKey + "]", beast.SpecialAttackName,
                blocked ?? "Ready",
                beast.IsActive ? 1f - beast.SpecialCooldownRemaining / Mathf.Max(0.01f, beast.frenzyCooldown) : 0f,
                blocked == null, UITheme.BeastFill);
        }
    }

    static void Paint(Control c, string heading, string title, string status, float fill, bool ready, Color accent)
    {
        c.heading.text = heading;
        c.title.text = title;
        c.status.text = status;
        c.button.interactable = ready;
        c.background.color = ready ? new Color(.28f, .26f, .19f, .98f) : new Color(.11f, .11f, .10f, .96f);
        c.title.color = ready ? accent : UITheme.Text;
        c.fill.color = accent;
        c.fill.fillAmount = Mathf.Clamp01(fill);
    }

    void FireWeapon()
    {
        PlayerEntity.Instance?.GetComponent<ActionCombat3D>()?.TrySpecial();
        Refresh();
    }

    void FireBeast()
    {
        CompanionManager.Instance?.TriggerFrenzy();
        Refresh();
    }

    void Build()
    {
        var canvasObject = new GameObject("SpecialAttackCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 465; // Above normal HUD; below menus/dialogues.
        _scaler = canvasObject.GetComponent<CanvasScaler>();
        _safeArea = Rect("SafeArea", canvasObject.transform);
        _safeArea.offsetMin = _safeArea.offsetMax = Vector2.zero;
        _row = Rect("SpecialActions", _safeArea);
        _row.anchorMin = _row.anchorMax = new Vector2(.5f, 0f);
        _row.pivot = new Vector2(.5f, 0f);
        _row.anchoredPosition = new Vector2(0, 12);
        _row.sizeDelta = new Vector2(Width * 2 + Gap, 70);
        _weapon = MakeControl("WeaponSpecial", FireWeapon);
        _beast = MakeControl("BeastSpecial", FireBeast);
        if (Object.FindAnyObjectByType<EventSystem>() == null)
            DontDestroyOnLoad(new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)));
    }

    Control MakeControl(string name, UnityEngine.Events.UnityAction action)
    {
        var c = new Control { rect = Rect(name, _row) };
        c.rect.anchorMin = c.rect.anchorMax = new Vector2(.5f, 0f);
        c.rect.pivot = new Vector2(.5f, 0f);
        c.rect.sizeDelta = new Vector2(Width, 70);
        c.background = c.rect.gameObject.AddComponent<Image>();
        UITheme.Tile(c.background);
        c.button = c.rect.gameObject.AddComponent<Button>();
        c.button.targetGraphic = c.background;
        c.button.navigation = new Navigation { mode = Navigation.Mode.None };
        c.button.onClick.AddListener(action);
        var colors = c.button.colors;
        colors.disabledColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
        c.button.colors = colors;
        c.heading = Label(c.rect, "Kind", 4, 15, 14, UITheme.TextDim);
        c.title = Label(c.rect, "Attack", 20, 24, 25, UITheme.Text);
        c.status = Label(c.rect, "Status", 44, 18, 17, UITheme.TextDim);
        var track = Rect("ChargeTrack", c.rect);
        Place(track, 12, 65, Width - 24, 3);
        track.gameObject.AddComponent<Image>().color = UITheme.TrackTint;
        var fill = Rect("Charge", track);
        fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        c.fill = fill.gameObject.AddComponent<Image>();
        c.fill.raycastTarget = false;
        UITheme.Fill(c.fill);
        c.fill.type = Image.Type.Filled;
        c.fill.fillMethod = Image.FillMethod.Horizontal;
        return c;
    }

    static TMP_Text Label(Transform parent, string name, float y, float height, float size, Color color)
    {
        var rt = Rect(name, parent);
        Place(rt, 10, y, Width - 20, height);
        var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        UITheme.Label(text, size, color, upper: false);
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.enableAutoSizing = true; text.fontSizeMin = size - 3; text.fontSizeMax = size;
        return text;
    }
    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }
    static void Place(RectTransform rt, float x, float y, float width, float height)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(width, height);
    }
}

