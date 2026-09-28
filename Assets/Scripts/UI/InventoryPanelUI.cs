using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// OSRS-style inventory panel: a grid of 28 slots.
/// Self-building & auto-spawned. Toggle from the HUD tab bar or with I.
/// Replaces the scene-wired inventory panel to match the newer "zero-setup" UI style.
/// </summary>
public class InventoryPanelUI : MonoBehaviour
{
    public static InventoryPanelUI Instance { get; private set; }

    static readonly Color C_Backdrop = new Color(0f, 0f, 0f, 0.40f);
    static readonly Color C_Panel    = new Color(0.13f, 0.12f, 0.10f, 0.99f);
    static readonly Color C_PanelDrop= new Color(0.42f, 0.07f, 0.05f, 0.99f);   // Tap-to-drop is on (OSRS reds the inventory)
    static readonly Color C_Bar      = new Color(0.20f, 0.17f, 0.12f, 1f);
    static readonly Color C_Slot     = new Color(0.18f, 0.17f, 0.14f, 1f);
    static readonly Color C_SlotEmpty= new Color(0.08f, 0.08f, 0.07f, 1f);
    static readonly Color C_SlotLocked = new Color(0.05f, 0.05f, 0.05f, 0.55f);   // saddlebag slots without a beast out
    static readonly Color C_Gold     = new Color(1f, 0.85f, 0.45f, 1f);

    GameObject _backdrop;
    GameObject _panel;
    RectTransform _grid;

    class Slot { public int index; public Image icon; public TMP_Text qty; public TMP_Text name; public Image bg; }
    readonly List<Slot> _slots = new();

    PlayerEntity _player;
    bool _bound;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("InventoryPanelUI (auto)").AddComponent<InventoryPanelUI>();
    }

    /// <summary>The phone's function button in Tap-to-drop mode turns the inventory red, like OSRS.</summary>
    public void SetDropMode(bool on)
    {
        var img = _panel != null ? _panel.GetComponent<Image>() : null;
        if (img != null) img.color = on ? C_PanelDrop : C_Panel;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);   // survive scene changes (warp to 3D world etc.)
        EnsureEventSystem();
        BuildUI();
        if (_backdrop) _backdrop.SetActive(false);
    }

    void Update()
    {
        if (!_bound || _player == null) { _bound = false; TryBind(); }   // re-bind after scene swaps
        if (Input.GetKeyDown(KeyCode.I) && !IsTyping()) Toggle();
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    public bool IsOpen => _backdrop != null && _backdrop.activeSelf;
    public void Toggle() { if (IsOpen) Close(); else Open(); }

    public void Open()
    {
        TryBind();
        Refresh();
        if (_backdrop) _backdrop.SetActive(true);
        _panel.transform.SetAsLastSibling();
    }

    public void Close() { if (_backdrop) _backdrop.SetActive(false); }

    void TryBind()
    {
        if (_bound) return;
        _player = PlayerEntity.Instance;
        if (_player == null) return;
        _player.Inventory.OnChanged += Refresh;
        _bound = true;
        Refresh();
    }

    void OnDestroy()
    {
        if (_player != null && _player.Inventory != null) _player.Inventory.OnChanged -= Refresh;
    }

    public void Refresh()
    {
        if (_player == null) return;
        int capacity = _player.Inventory.Capacity;
        for (int i = 0; i < _slots.Count; i++)
        {
            var stack = _player.Inventory.GetSlot(i);
            var s = _slots[i];
            if (stack != null)
            {
                var item = ItemRegistry.Get(stack.itemId);
                bool hasIcon = item != null && item.icon != null;
                s.bg.color = C_Slot;
                s.icon.enabled = hasIcon;
                s.icon.sprite = hasIcon ? item.icon : null;
                // Show the name only when there's no icon, so you can still tell what it is.
                s.name.text = hasIcon ? "" : (item != null ? item.name : ("#" + stack.itemId));
                s.qty.text = stack.quantity > 1 ? stack.quantity.ToString() : "";
            }
            else
            {
                bool locked = i >= capacity;   // saddlebag slot with no beast carrying it
                s.bg.color = locked ? C_SlotLocked : C_SlotEmpty;
                s.icon.enabled = false;
                s.name.text = "";
                s.qty.text = "";
            }
        }
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("InventoryCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 540;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        UITheme.ApplyScaler(scaler, new Vector2(1920, 1080));

        var backdropRt = NewUI("Backdrop", canvasGo.transform); Stretch(backdropRt);
        _backdrop = backdropRt.gameObject;
        // Only the visible panel captures input, so crafting and the tab dock stay usable.
        var bd = _backdrop.AddComponent<Image>();
        bd.color = new Color(0f, 0f, 0f, 0f);
        bd.raycastTarget = false;

        // Grid sized so the 4 columns exactly fill the panel width (no dead space on the right)
        // and the slots are big enough that the fallback name text is readable.
        const int   COLS    = 4;
        const int   SLOTS   = 28;   // standard OSRS inventory (companion saddlebag overflow shown separately)
        const float CELL    = 74f;
        const float SPACING = 8f;
        const float PAD     = 6f;
        const float MARGIN  = 12f;
        const float BAR     = 36f;
        const float HINT    = 26f;
        const float GAP     = 8f;
        int   rows  = Mathf.CeilToInt(SLOTS / (float)COLS);
        float gridW = COLS * CELL + (COLS - 1) * SPACING + PAD * 2f;
        float gridH = rows * CELL + (rows - 1) * SPACING + PAD * 2f;
        float PW    = gridW + MARGIN * 2f;
        float PH    = BAR + GAP + gridH + GAP + HINT + GAP;

        var panelRt = NewUI("Panel", backdropRt);
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(1f, 0.5f);
        panelRt.pivot = new Vector2(1f, 0.5f);
        panelRt.sizeDelta = new Vector2(PW, PH);
        panelRt.anchoredPosition = new Vector2(-20f, 0f);
        UITheme.DockSidePanel(panelRt);   // bottom-right above the tab dock, clear of the minimap
        _panel = panelRt.gameObject;
        _panel.AddComponent<Image>().color = C_Panel;
        _panel.AddComponent<Button>().transition = Selectable.Transition.None;

        var bar = NewUI("Bar", panelRt); Place(bar, 0, 0, PW, BAR);
        bar.gameObject.AddComponent<Image>().color = C_Bar;

        // Drag handle on the title bar moves the whole panel.
        var drag = bar.gameObject.AddComponent<PanelDragHandler>();
        drag.Target = panelRt;

        var titleRt = NewUI("Title", bar); Stretch(titleRt);
        var title = titleRt.gameObject.AddComponent<TextMeshProUGUI>();
        title.text = "INVENTORY"; title.fontSize = 20; title.color = C_Gold;
        title.fontStyle = FontStyles.Bold; title.alignment = TextAlignmentOptions.Center;
        title.raycastTarget = false;

        var closeRt = NewUI("Close", bar); Place(closeRt, PW - 32, 5, 26, 26);
        var closeImg = closeRt.gameObject.AddComponent<Image>();
        closeImg.color = new Color(0.55f, 0.18f, 0.15f, 1f);
        var cbtn = closeRt.gameObject.AddComponent<Button>(); cbtn.targetGraphic = closeImg;
        cbtn.onClick.AddListener(Close);
        var cxRt = NewUI("X", closeRt); Stretch(cxRt);
        var cx = cxRt.gameObject.AddComponent<TextMeshProUGUI>();
        cx.text = "X"; cx.fontSize = 16; cx.alignment = TextAlignmentOptions.Center;
        cx.color = Color.white; cx.raycastTarget = false;

        // Grid container — cells computed above so the columns fill the panel.
        var gridRt = NewUI("Grid", panelRt);
        Place(gridRt, MARGIN, BAR + GAP, gridW, gridH);
        _grid = gridRt;
        var glg = gridRt.gameObject.AddComponent<GridLayoutGroup>();
        glg.cellSize = new Vector2(CELL, CELL);
        glg.spacing = new Vector2(SPACING, SPACING);
        glg.padding = new RectOffset((int)PAD, (int)PAD, (int)PAD, (int)PAD);
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = COLS;

        for (int i = 0; i < SLOTS; i++)
        {
            var slotRt = NewUI("Slot_" + i, gridRt);
            var bg = slotRt.gameObject.AddComponent<Image>();
            bg.color = C_Slot;

            var slotScript = slotRt.gameObject.AddComponent<InventorySlotUI>();
            slotScript.SlotIndex = i;

            var iconRt = NewUI("Icon", slotRt); Stretch(iconRt);
            iconRt.offsetMin = new Vector2(5, 5); iconRt.offsetMax = new Vector2(-5, -5);
            var icon = iconRt.gameObject.AddComponent<Image>();
            icon.raycastTarget = false; icon.preserveAspect = true; icon.enabled = false;

            var qtyRt = NewUI("Qty", slotRt);
            qtyRt.anchorMin = Vector2.zero; qtyRt.anchorMax = Vector2.one;
            qtyRt.offsetMin = new Vector2(4, 3); qtyRt.offsetMax = new Vector2(-4, -3);
            var qty = qtyRt.gameObject.AddComponent<TextMeshProUGUI>();
            qty.fontSize = 18; qty.color = C_Gold; qty.fontStyle = FontStyles.Bold;
            qty.alignment = TextAlignmentOptions.TopLeft; qty.raycastTarget = false;

            // Name fallback (shown only for icon-less items) — auto-sizes so it always fits the slot.
            var nameRt = NewUI("Name", slotRt); Stretch(nameRt);
            nameRt.offsetMin = new Vector2(3, 3); nameRt.offsetMax = new Vector2(-3, -3);
            var nm = nameRt.gameObject.AddComponent<TextMeshProUGUI>();
            nm.color = Color.white;
            nm.enableAutoSizing = true; nm.fontSizeMin = 9; nm.fontSizeMax = 18;
            nm.alignment = TextAlignmentOptions.Center; nm.raycastTarget = false;

            _slots.Add(new Slot { index = i, icon = icon, qty = qty, name = nm, bg = bg });
        }

        var hintRt = NewUI("Hint", panelRt);
        Place(hintRt, MARGIN, BAR + GAP + gridH + GAP, gridW, HINT);
        var hint = hintRt.gameObject.AddComponent<TextMeshProUGUI>();
        hint.text = "Drag items to rearrange. (I toggles this panel)";
        hint.fontSize = 13; hint.color = UITheme.TextDim;
        hint.alignment = TextAlignmentOptions.Center; hint.raycastTarget = false;
    }

    // ── helpers ──────────────────────────────────────────────────────────
    static bool IsTyping() => ChatInput.IsTyping;

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

/// <summary>
/// Simple helper to allow dragging a UI panel via a handle (like a title bar).
/// </summary>
public class PanelDragHandler : MonoBehaviour, IDragHandler
{
    public RectTransform Target;

    public void OnDrag(PointerEventData eventData)
    {
        if (Target == null) return;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            Target.anchoredPosition += eventData.delta / canvas.scaleFactor;
        }
    }
}
