using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// OSRS-style bank window: bank contents on the left, your inventory on the right.
/// Click a bank item to withdraw 1, click an inventory item to deposit 1, or Deposit All.
///
/// Self-building: if no panel is wired in the inspector it constructs its own canvas
/// and widgets at runtime, and it auto-spawns after scene load. Zero editor setup.
/// </summary>
public class BankUI : MonoBehaviour
{
    public static BankUI Instance { get; private set; }

    [SerializeField] private GameObject panel;
    [SerializeField] private Transform grid;        // optional legacy reference
    [SerializeField] private Button closeBtn;

    static readonly Color C_Backdrop = new Color(0f, 0f, 0f, 0.45f);
    static readonly Color C_Panel    = new Color(0.13f, 0.12f, 0.10f, 0.99f);
    static readonly Color C_Bar      = new Color(0.20f, 0.17f, 0.12f, 1f);
    static readonly Color C_Slot     = new Color(0.18f, 0.17f, 0.14f, 1f);
    static readonly Color C_SlotEmpty= new Color(0.08f, 0.08f, 0.07f, 1f);
    static readonly Color C_Gold     = new Color(1f, 0.85f, 0.45f, 1f);

    // Auto-close the bank once the player steps ~1 tile away from where they opened it
    // (a failsafe on top of Escape / clicking outside). tileMeters in the 3D world ≈ 2m.
    const float AutoCloseDistance = 2f;
    private Vector3 _openPos;

    private PlayerEntity _player;
    private bool _bound;
    private GameObject _backdrop;
    private RectTransform _bankContent;
    private RectTransform _invContent;

    public bool IsOpen => panel != null && panel.activeSelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("BankUI (auto)").AddComponent<BankUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);   // survive scene changes (warp to 3D world etc.)
        EnsureEventSystem();

        // Always build our own reliable UI (ignores any half-wired scene panel,
        // which is exactly what was preventing the bank from opening).
        BuildUI();
        if (panel) panel.SetActive(false);
        // The backdrop is a full-screen click-catcher: left on, it swallowed every tap and click on the world
        // (Roxy, nodes, walking — on a phone, everything) even with the bank shut.
        if (_backdrop) _backdrop.SetActive(false);
    }

    void Start() => RefreshCrateSubscriptions();

    void OnEnable()  => UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
        => RefreshCrateSubscriptions();   // new scene = new crates

    public void RefreshCrateSubscriptions()
    {
        var crates = Object.FindObjectsByType<BankingCrate>(FindObjectsInactive.Include);
        foreach (var crate in crates)
        {
            crate.OnOpened -= Open;
            crate.OnOpened += Open;
        }
    }

    public void Open(PlayerEntity player)
    {
        _player = player;
        if (player != null) _openPos = player.transform.position;   // anchor for the walk-away auto-close
        BindPlayer();
        if (panel) panel.SetActive(true);
        if (_backdrop) _backdrop.SetActive(true);
        panel.transform.SetAsLastSibling();
        Refresh();
    }

    public void Close()
    {
        if (panel) panel.SetActive(false);
        if (_backdrop) _backdrop.SetActive(false);
    }

    void Update()
    {
        // Self-heal: the backdrop only ever belongs up while the bank is.
        if (!IsOpen && _backdrop != null && _backdrop.activeSelf) _backdrop.SetActive(false);
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
        // Walked away from the crate → close the bank (failsafe).
        if (_player != null &&
            (_player.transform.position - _openPos).sqrMagnitude > AutoCloseDistance * AutoCloseDistance)
            Close();
    }

    void BindPlayer()
    {
        // Re-bind if the player object changed (scene swap creates a new PlayerEntity).
        if (_player == null) return;
        if (_bound && _player == _lastBound) return;
        _player.Inventory.OnChanged += Refresh;
        _player.Bank.OnChanged += Refresh;
        _lastBound = _player;
        _bound = true;
    }
    PlayerEntity _lastBound;

    // ── refresh ──────────────────────────────────────────────────────────
    public void Refresh()
    {
        if (_player == null || !IsOpen) return;
        if (_bankContent != null) RefreshBank();
        if (_invContent != null) RefreshInventory();
        else if (grid != null) RefreshLegacyGrid();   // fall back to old wired grid
    }

    void RefreshBank()
    {
        ClearChildren(_bankContent);
        for (int i = 0; i < Bank.SIZE; i++)
        {
            var stack = _player.Bank.GetSlot(i);
            if (stack == null || stack.itemId <= 0) continue;
            int idx = i;
            MakeSlot(_bankContent, stack.itemId, stack.quantity, () =>
            {
                var s = _player.Bank.GetSlot(idx);
                if (s == null) return;
                if (_player.Inventory.Add(s.Copy(1)))
                {
                    _player.Bank.WithdrawAt(idx, 1);
                    HUDController.Instance?.AddChatLine($"<color=#80FF80>[BANK]:</color> Withdrew {ItemRegistry.Get(s.itemId)?.name}.");
                }
                else HUDController.Instance?.AddChatLine("<color=#FF8080>[BANK]:</color> Inventory full.");
            });
        }
    }

    void RefreshInventory()
    {
        ClearChildren(_invContent);
        for (int i = 0; i < Inventory.MAX; i++)   // include saddlebag slots
        {
            var stack = _player.Inventory.GetSlot(i);
            if (stack == null || stack.itemId <= 0) { MakeSlot(_invContent, 0, 0, null); continue; }
            int idx = i;
            MakeSlot(_invContent, stack.itemId, stack.quantity, () =>
            {
                var s = _player.Inventory.GetSlot(idx);
                if (s == null) return;
                int id = s.itemId;
                if (_player.Bank.Deposit(s))
                {
                    _player.Inventory.RemoveAt(idx, s.quantity);
                    HUDController.Instance?.AddChatLine($"<color=#80FF80>[BANK]:</color> Deposited {ItemRegistry.Get(id)?.name}.");
                }
                else HUDController.Instance?.AddChatLine("<color=#FF8080>[BANK]:</color> Bank full.");
            });
        }
    }

    void RefreshLegacyGrid()
    {
        // Minimal support for an inspector-wired grid (older scenes).
        for (int i = 0; i < Bank.SIZE && i < grid.childCount; i++)
        {
            var ui = grid.GetChild(i).GetComponent<BankSlotUI>();
            if (ui == null) continue;
            ui.SlotIndex = i;
            ui.SetItem(_player.Bank.GetSlot(i));
        }
    }

    void DepositAll()
    {
        if (_player == null) return;
        for (int i = 0; i < Inventory.MAX; i++)   // include saddlebag slots
        {
            var s = _player.Inventory.GetSlot(i);
            if (s == null || s.itemId <= 0) continue;
            int id = s.itemId, qty = s.quantity;
            if (_player.Bank.Deposit(s)) _player.Inventory.RemoveAt(i, qty);
        }
        HUDController.Instance?.AddChatLine("<color=#80FF80>[BANK]:</color> Deposited everything.");
    }

    // ── widget builders ──────────────────────────────────────────────────
    void MakeSlot(RectTransform parent, int itemId, int qty, System.Action onClick)
    {
        var cell = NewUI("Slot", parent);
        var bg = cell.gameObject.AddComponent<Image>();
        bg.color = itemId > 0 ? C_Slot : C_SlotEmpty;

        if (itemId > 0)
        {
            var item = ItemRegistry.Get(itemId);
            var iconRt = NewUI("Icon", cell);
            iconRt.anchorMin = new Vector2(0, 0); iconRt.anchorMax = new Vector2(1, 1);
            iconRt.offsetMin = new Vector2(3, 3); iconRt.offsetMax = new Vector2(-3, -3);
            var icon = iconRt.gameObject.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            if (item != null && item.icon != null) icon.sprite = item.icon;
            else { icon.color = new Color(0.5f, 0.5f, 0.5f, 1f); }

            // qty (top-left) — only show if > 1
            if (qty > 1)
            {
                var qRt = NewUI("Qty", cell); Stretch(qRt);
                var q = qRt.gameObject.AddComponent<TextMeshProUGUI>();
                q.text = qty.ToString(); q.fontSize = 12; q.color = C_Gold;
                q.alignment = TextAlignmentOptions.TopLeft; q.raycastTarget = false;
                q.margin = new Vector4(3, 2, 0, 0);
            }
            // name (bottom) for clarity since icons may be missing
            var nRt = NewUI("Name", cell); Stretch(nRt);
            var n = nRt.gameObject.AddComponent<TextMeshProUGUI>();
            n.text = item != null ? item.name : ("#" + itemId);
            n.fontSize = 9; n.color = Color.white;
            n.alignment = TextAlignmentOptions.Bottom; n.raycastTarget = false;

            if (onClick != null)
            {
                var btn = cell.gameObject.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.onClick.AddListener(() => onClick());
            }
        }
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("BankCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 520;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var backdropRt = NewUI("Backdrop", canvasGo.transform); Stretch(backdropRt);
        _backdrop = backdropRt.gameObject;
        _backdrop.AddComponent<Image>().color = C_Backdrop;
        var bBtn = _backdrop.AddComponent<Button>();
        bBtn.transition = Selectable.Transition.None;
        bBtn.onClick.AddListener(Close);

        const float PW = 780, PH = 500;
        var panelRt = NewUI("Panel", backdropRt);
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(1f, 0.5f);   // right-center, matching the Inventory panel
        panelRt.pivot = new Vector2(1f, 0.5f);
        panelRt.sizeDelta = new Vector2(PW, PH);
        panelRt.anchoredPosition = new Vector2(-20f, 0f);
        UITheme.DockSidePanel(panelRt);   // bottom-right above the tab dock, clear of the minimap
        panel = panelRt.gameObject;
        panel.AddComponent<Image>().color = C_Panel;
        panel.AddComponent<Button>().transition = Selectable.Transition.None;

        // title bar
        var bar = NewUI("Bar", panelRt); Place(bar, 0, 0, PW, 38);
        bar.gameObject.AddComponent<Image>().color = C_Bar;
        var titleRt = NewUI("Title", bar); Stretch(titleRt);
        var title = titleRt.gameObject.AddComponent<TextMeshProUGUI>();
        title.text = "BANK"; title.fontSize = 18; title.color = C_Gold;
        title.fontStyle = FontStyles.Bold; title.alignment = TextAlignmentOptions.Center;
        title.raycastTarget = false;

        var closeRt = NewUI("Close", bar); Place(closeRt, PW - 34, 5, 28, 28);
        var closeImg = closeRt.gameObject.AddComponent<Image>();
        closeImg.color = new Color(0.55f, 0.18f, 0.15f, 1f);
        var cbtn = closeRt.gameObject.AddComponent<Button>(); cbtn.targetGraphic = closeImg;
        cbtn.onClick.AddListener(Close);
        var cxRt = NewUI("X", closeRt); Stretch(cxRt);
        var cx = cxRt.gameObject.AddComponent<TextMeshProUGUI>();
        cx.text = "X"; cx.fontSize = 15; cx.alignment = TextAlignmentOptions.Center;
        cx.color = Color.white; cx.raycastTarget = false;

        // ── left: bank grid (scroll) ──────────────────────────────────────
        float colY = 48, colH = PH - colY - 12;
        float bankW = 470;
        _bankContent = BuildScrollGrid(panelRt, 12, colY, bankW, colH, 8);

        // header over bank
        // ── right: inventory grid ─────────────────────────────────────────
        float invX = 12 + bankW + 12;
        float invW = PW - invX - 12;
        var invLblRt = NewUI("InvLbl", panelRt); Place(invLblRt, invX, colY, invW, 18);
        var invLbl = invLblRt.gameObject.AddComponent<TextMeshProUGUI>();
        invLbl.text = "INVENTORY"; invLbl.fontSize = 12; invLbl.color = new Color(0.7f,0.7f,0.7f,1f);
        invLbl.alignment = TextAlignmentOptions.Center; invLbl.raycastTarget = false;

        _invContent = BuildScrollGrid(panelRt, invX, colY + 22, invW, colH - 60, 4);

        // deposit all button (bottom-right)
        var daRt = NewUI("DepositAll", panelRt);
        Place(daRt, invX, PH - 44, invW, 32);
        var daImg = daRt.gameObject.AddComponent<Image>();
        daImg.color = new Color(0.30f, 0.45f, 0.20f, 1f);
        var daBtn = daRt.gameObject.AddComponent<Button>(); daBtn.targetGraphic = daImg;
        daBtn.onClick.AddListener(DepositAll);
        var daLblRt = NewUI("Lbl", daRt); Stretch(daLblRt);
        var daLbl = daLblRt.gameObject.AddComponent<TextMeshProUGUI>();
        daLbl.text = "Deposit All"; daLbl.fontSize = 15; daLbl.color = Color.white;
        daLbl.alignment = TextAlignmentOptions.Center; daLbl.raycastTarget = false;
    }

    /// <summary>Builds a scrollable grid and returns its content RectTransform.</summary>
    RectTransform BuildScrollGrid(RectTransform parent, float x, float y, float w, float h, int columns)
    {
        var viewport = NewUI("Viewport", parent);
        Place(viewport, x, y, w, h);
        viewport.gameObject.AddComponent<Image>().color = C_SlotEmpty;
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = NewUI("Content", viewport);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1f); content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0, 0);

        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        float cell = (w - 6 - (columns - 1) * 4) / columns;
        grid.cellSize = new Vector2(cell, cell);
        grid.spacing = new Vector2(4, 4);
        grid.padding = new RectOffset(3, 3, 3, 3);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;

        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = content;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24;
        return content;
    }

    // ── helpers ──────────────────────────────────────────────────────────
    static void ClearChildren(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
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
