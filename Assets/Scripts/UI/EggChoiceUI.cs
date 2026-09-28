using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One-time Beastmastery choice: Roxy offers three young beasts, you keep ONE.
/// You get the live baby immediately — it joins you and grows with your Beastmastery
/// (stages at level 30 and 60). No egg, no incubation.
///   Mutant Pup          → wolf line   (savage melee fighter)
///   Shellback Hatchling → turtle line (tank that carries extra inventory)
///   Rad-Chick           → bird line   (spotter: buffs YOUR ranged damage)
/// Self-building modal, opened from Roxy's Beastmastery quest. Closing without picking is
/// fine — talk to her again and she re-offers it.
/// </summary>
public class EggChoiceUI : MonoBehaviour
{
    public static EggChoiceUI Instance { get; private set; }

    static readonly Color C_Backdrop = new Color(0f, 0f, 0f, 0.55f);
    static readonly Color C_Panel    = new Color(0.13f, 0.12f, 0.10f, 0.99f);
    static readonly Color C_Bar      = new Color(0.20f, 0.17f, 0.12f, 1f);
    static readonly Color C_Card     = new Color(0.18f, 0.17f, 0.14f, 1f);
    static readonly Color C_Gold     = new Color(1f, 0.85f, 0.45f, 1f);

    struct CompanionOption
    {
        public string key, title, blurb;
        public CompanionOption(string k, string t, string b) { key = k; title = t; blurb = b; }
    }

    static readonly CompanionOption[] Options =
    {
        new CompanionOption("wolf",   "Mutant Pup",
            "A snarling scrap of teeth and fur. Grows into a savage hunter at your side — and one day, an ALPHA."),
        new CompanionOption("turtle", "Shellback Hatchling",
            "A plodding little tank. Grows into a great shellback that hauls extra gear for you (+2/+4/+6 pack slots)."),
        new CompanionOption("bird",   "Rad-Chick",
            "A scrappy ball of feathers and attitude. Grows into a skyhunter whose strikes hit far harder than they ought to."),
    };

    GameObject _backdrop;

    public static void Show()
    {
        if (Instance == null)
            new GameObject("EggChoiceUI (auto)").AddComponent<EggChoiceUI>();
        Instance.Open();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUI();
        _backdrop.SetActive(false);
    }

    public void Open()
    {
        var player = PlayerEntity.Instance;
        if (player == null || player.HasFlag("egg_chosen")) return;
        _backdrop.SetActive(true);
    }

    public void Close() { if (_backdrop != null) _backdrop.SetActive(false); }

    void Pick(CompanionOption opt)
    {
        var player = PlayerEntity.Instance;
        if (player == null) return;
        if (player.HasFlag("egg_chosen")) { Close(); return; }

        // Live baby, right now — no egg, no incubation.
        player.SetFlag("egg_chosen");        // marks the choice as made (gates re-offering)
        player.SetFlag("pup_hatched");       // the companion now exists
        player.SetFlag("species_" + opt.key);

        CompanionManager.Instance?.Summon();
        Chat($"<color=#80FF80>A {opt.title.ToLower()} bounds to your side!</color>");
        Close();
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("EggChoiceCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 600;   // above inventory/bank
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var backdropRt = NewUI("Backdrop", canvasGo.transform);
        backdropRt.anchorMin = Vector2.zero; backdropRt.anchorMax = Vector2.one;
        backdropRt.offsetMin = Vector2.zero; backdropRt.offsetMax = Vector2.zero;
        _backdrop = backdropRt.gameObject;
        _backdrop.AddComponent<Image>().color = C_Backdrop;
        var bBtn = _backdrop.AddComponent<Button>();
        bBtn.transition = Selectable.Transition.None;
        bBtn.onClick.AddListener(Close);

        const float PW = 560, PH = 430;
        var panelRt = NewUI("Panel", backdropRt);
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(PW, PH);
        var panel = panelRt.gameObject;
        panel.AddComponent<Image>().color = C_Panel;
        panel.AddComponent<Button>().transition = Selectable.Transition.None; // swallow clicks

        var bar = NewUI("Bar", panelRt); Place(bar, 0, 0, PW, 36);
        bar.gameObject.AddComponent<Image>().color = C_Bar;
        var titleRt = NewUI("Title", bar);
        titleRt.anchorMin = Vector2.zero; titleRt.anchorMax = Vector2.one;
        titleRt.offsetMin = Vector2.zero; titleRt.offsetMax = Vector2.zero;
        var title = titleRt.gameObject.AddComponent<TextMeshProUGUI>();
        title.text = "CHOOSE YOUR COMPANION  —  ONLY ONE";
        title.fontSize = 17; title.color = C_Gold; title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center; title.raycastTarget = false;

        float y = 46;
        foreach (var opt in Options)
        {
            var o = opt;   // capture per-iteration copy for the listener
            var card = NewUI("Card_" + o.key, panelRt); Place(card, 14, y, PW - 28, 108);
            var cardImg = card.gameObject.AddComponent<Image>();
            cardImg.color = C_Card;
            var btn = card.gameObject.AddComponent<Button>();
            btn.targetGraphic = cardImg;
            btn.onClick.AddListener(() => Pick(o));

            var nameRt = NewUI("Name", card); Place(nameRt, 12, 8, PW - 52, 22);
            var nm = nameRt.gameObject.AddComponent<TextMeshProUGUI>();
            nm.text = o.title; nm.fontSize = 16; nm.color = C_Gold;
            nm.fontStyle = FontStyles.Bold; nm.alignment = TextAlignmentOptions.Left;
            nm.raycastTarget = false;

            var blurbRt = NewUI("Blurb", card); Place(blurbRt, 12, 32, PW - 52, 70);
            var bl = blurbRt.gameObject.AddComponent<TextMeshProUGUI>();
            bl.text = o.blurb; bl.fontSize = 13; bl.color = new Color(0.85f, 0.85f, 0.8f, 1f);
            bl.alignment = TextAlignmentOptions.TopLeft; bl.raycastTarget = false;   // TMP wraps by default

            y += 116;
        }

        var hintRt = NewUI("Hint", panelRt); Place(hintRt, 14, PH - 32, PW - 28, 20);
        var hint = hintRt.gameObject.AddComponent<TextMeshProUGUI>();
        hint.text = "Pick one — it joins you right away and grows with your Beastmastery. Not ready? Click outside; Roxy will offer them again.";
        hint.fontSize = 13; hint.color = UITheme.TextDim;
        hint.alignment = TextAlignmentOptions.Center; hint.raycastTarget = false;
    }

    // ── helpers ──────────────────────────────────────────────────────────
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

    static void Chat(string m) => HUDController.Emit("<color=#C9A227>[BEAST]:</color> " + m);
}
