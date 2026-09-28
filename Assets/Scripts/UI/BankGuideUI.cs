using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Roxy's replayable guide uses photographs of the actual shared-world banks.</summary>
public class BankGuideUI : MonoBehaviour
{
    public const string LessonFlag = "roxy_bank_lesson";
    static BankGuideUI _instance;
    public static bool IsShowing => _instance != null && _instance._overlay.activeSelf;
    static BankingCrate[] TownBanks() => FindObjectsByType<BankingCrate>(FindObjectsInactive.Include, FindObjectsSortMode.None)
        .Where(b => b.gameObject.scene.IsValid() && !string.IsNullOrEmpty(b.townName))
        .OrderBy(b => b.guideOrder).ToArray();
    public static bool HasTownBanks => TownBanks().Length > 0;

    GameObject _overlay;
    TextMeshProUGUI _title, _town, _directions, _count;
    Image _photo;
    Button _previous, _next;
    BankingCrate[] _banks;
    PlayerEntity _owner;
    int _index, _scene;
    bool _lesson;

    public static void Show(string speaker, bool lesson = false)
    {
        var banks = TownBanks();
        if (banks.Length == 0 || PlayerEntity.Instance == null) return;
        if (_instance == null) new GameObject("BankGuideUI (auto)").AddComponent<BankGuideUI>();
        _instance._banks = banks;
        _instance._owner = PlayerEntity.Instance;
        _instance._owner.GetComponent<Player3DController>()?.ClearDestination();
        _instance._scene = SceneManager.GetActiveScene().handle;
        _instance._lesson = lesson;
        _instance._index = 0;
        _instance._title.text = speaker + "'s town banks";
        _instance._overlay.SetActive(true);
        _instance.Refresh();
    }

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        Build();
    }

    void Update()
    {
        if (!IsShowing) return;
        if (_owner == null || _owner != PlayerEntity.Instance || _scene != SceneManager.GetActiveScene().handle)
        { _overlay.SetActive(false); return; }
        if (Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    void Close()
    {
        if (_lesson && _owner != null && _owner == PlayerEntity.Instance) _owner.SetFlag(LessonFlag);
        _overlay.SetActive(false);
    }

    void Refresh()
    {
        var bank = _banks[_index];
        if (bank == null) { _overlay.SetActive(false); return; }
        _town.text = bank.townName;
        _directions.text = bank.directions + " Look for the BANK sign.";
        _photo.sprite = bank.guideImage;
        _photo.enabled = bank.guideImage != null;
        _count.text = (_index + 1) + " / " + _banks.Length;
        _previous.interactable = _index > 0;
        _next.interactable = _index < _banks.Length - 1;
    }

    void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 600;
        UITheme.ApplyScaler(gameObject.AddComponent<CanvasScaler>(), new Vector2(1920, 1080));
        gameObject.AddComponent<GraphicRaycaster>();
        var overlay = Rect("BankGuideOverlay", transform, Vector2.zero, Vector2.one);
        overlay.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .72f);
        _overlay = overlay.gameObject;
        var panel = Rect("Guide", overlay, new Vector2(.5f, .5f), new Vector2(.5f, .5f));
        panel.sizeDelta = new Vector2(940, 900);
        UITheme.Panel(panel.gameObject.AddComponent<Image>(), .99f);
        _title = Label(panel, "Title", 22, 56, 30, UITheme.Amber);
        var intro = Label(panel, "RoxyLesson", 85, 115, 25, UITheme.Text);
        intro.text = "Before you fill your pockets with half the wasteland, handsome: use the town bank. Every town opens the same stash. Leave supplies here and collect them from any other town.";
        _town = Label(panel, "Town", 208, 42, 28, UITheme.Amber);
        var photo = Rect("BankPhoto", panel, new Vector2(0, 1), Vector2.one);
        photo.pivot = new Vector2(.5f, 1);
        photo.anchoredPosition = new Vector2(0, -263);
        photo.sizeDelta = new Vector2(-48, 400);
        _photo = photo.gameObject.AddComponent<Image>();
        _photo.preserveAspect = true;
        _photo.raycastTarget = false;
        _directions = Label(panel, "Directions", 669, 62, 23, UITheme.Text);
        var how = Label(panel, "HowToBank", 731, 76, 22, UITheme.TextDim);
        how.text = "Interact with the crate. Use the inventory side to deposit and the bank side to withdraw. Deposit All stores everything in your bag. Reopen this guide with Roxy's Town banks button.";
        _previous = Button(panel, "Previous", -317, () => { _index--; Refresh(); });
        _next = Button(panel, "Next", 0, () => { _index++; Refresh(); });
        Button(panel, "Done", 317, Close);
        _count = Label(panel, "PageCount", 874, 22, 18, UITheme.TextDim);
        _overlay.SetActive(false);
    }

    static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false); rt.anchorMin = min; rt.anchorMax = max;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    static TextMeshProUGUI Label(Transform parent, string name, float top, float height, float size, Color color)
    {
        var rt = Rect(name, parent, new Vector2(0, 1), Vector2.one);
        rt.pivot = new Vector2(.5f, 1); rt.anchoredPosition = new Vector2(0, -top);
        rt.sizeDelta = new Vector2(-48, height);
        var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        UITheme.Label(text, size, color, false, false);
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    static Button Button(Transform parent, string title, float x, UnityEngine.Events.UnityAction action)
    {
        var rt = Rect(title, parent, new Vector2(.5f, 0), new Vector2(.5f, 0));
        rt.pivot = new Vector2(.5f, 0); rt.anchoredPosition = new Vector2(x, 28);
        rt.sizeDelta = new Vector2(276, 60);
        var image = rt.gameObject.AddComponent<Image>(); image.color = UITheme.TileTint; UITheme.Tile(image);
        var button = rt.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
        Label(rt, "Label", 0, 60, 26, UITheme.Amber).text = title;
        return button;
    }
}
