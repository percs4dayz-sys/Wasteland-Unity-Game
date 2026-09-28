using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Paper-doll equipment panel — slots are arranged where you'd actually wear the gear,
/// including the Ammo slot. Click a slot to unequip. Self-building & auto-spawned
/// (open with the "🛡 GEAR" button top-right, or F9). No inspector setup needed.
/// </summary>
public class EquipmentPanelUI : MonoBehaviour
{
    public static EquipmentPanelUI Instance { get; private set; }

    static readonly Color C_Backdrop = new Color(0f, 0f, 0f, 0.40f);
    static readonly Color C_Panel    = new Color(0.13f, 0.12f, 0.10f, 0.99f);
    static readonly Color C_Bar      = new Color(0.20f, 0.17f, 0.12f, 1f);
    static readonly Color C_Slot     = new Color(0.18f, 0.17f, 0.14f, 1f);
    static readonly Color C_SlotEmpty= new Color(0.08f, 0.08f, 0.07f, 1f);
    static readonly Color C_Gold     = new Color(1f, 0.85f, 0.45f, 1f);

    GameObject _backdrop;
    GameObject _panel;

    class Slot { public string name; public Image bg; public Image icon; public Image ghost; public TMP_Text label; }

    // Faint silhouette shown in an EMPTY slot so the paper-doll reads at a glance (UI/Icons, Synty set).
    static string GhostIcon(string slot) => slot switch
    {
        "Helmet" => "UI/Icons/helmet", "Weapon" => "UI/Icons/pistol", "Chest" => "UI/Icons/chest",
        "Shield" => "UI/Icons/shield", "Ammo"   => "UI/Icons/ammo",   "Tool"  => "UI/Icons/hammer",
        _ => null,
    };
    readonly List<Slot> _slots = new();

    PlayerEntity _player;
    bool _bound;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("EquipmentPanelUI (auto)").AddComponent<EquipmentPanelUI>();
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
        if (Input.GetKeyDown(KeyCode.F9)) Toggle();
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
        _player.Equipment.OnChanged += Refresh;
        _bound = true;
        Refresh();
    }

    void OnDestroy()
    {
        if (_player != null) _player.Equipment.OnChanged -= Refresh;
    }

    public void Refresh()
    {
        if (_player == null) return;
        foreach (var s in _slots)
        {
            var id = _player.Equipment.GetItemId(s.name);
            if (id.HasValue)
            {
                var item = ItemRegistry.Get(id.Value);
                s.bg.color = C_Slot;
                s.icon.enabled = item != null && item.icon != null;
                s.icon.sprite = item != null ? item.icon : null;
                string nm = item != null ? item.name : s.name;
                if (s.name == "Ammo") nm += " x" + _player.Equipment.AmmoQuantity;
                s.label.text = nm;
                s.ghost.enabled = false;
            }
            else
            {
                s.bg.color = C_SlotEmpty;
                s.icon.enabled = false;
                s.icon.sprite = null;
                s.label.text = s.name;
                s.ghost.enabled = s.ghost.sprite != null;
            }
        }
    }

    void Unequip(string slotName)
    {
        if (_player == null) return;
        string msg = _player.Unequip(slotName);
        if (!string.IsNullOrEmpty(msg))
            HUDController.Instance?.AddChatLine("<color=#88DDFF>[EQUIP]:</color> " + msg);
    }

    // ── build ────────────────────────────────────────────────────────────
    void BuildUI()
    {
        var canvasGo = new GameObject("EquipmentCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 535;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // (No floating button — the bottom HUD tab bar's "GEAR" tab opens this; F9 also toggles it.)

        var backdropRt = NewUI("Backdrop", canvasGo.transform); Stretch(backdropRt);
        _backdrop = backdropRt.gameObject;
        // Invisible, click-through backdrop — panel stays open while you move/click the world,
        // and clicking off it no longer closes it (OSRS-style persistent panel).
        var bd = _backdrop.AddComponent<Image>();
        bd.color = new Color(0f, 0f, 0f, 0f);
        bd.raycastTarget = false;

        const float PW = 380, PH = 420;
        var panelRt = NewUI("Panel", backdropRt);
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(1f, 0.5f);   // right-center, matching the Inventory panel
        panelRt.pivot = new Vector2(1f, 0.5f);
        panelRt.sizeDelta = new Vector2(PW, PH);
        panelRt.anchoredPosition = new Vector2(-20f, 0f);
        UITheme.DockSidePanel(panelRt);   // bottom-right above the tab dock, clear of the minimap
        _panel = panelRt.gameObject;
        _panel.AddComponent<Image>().color = C_Panel;
        _panel.AddComponent<Button>().transition = Selectable.Transition.None;

        var bar = NewUI("Bar", panelRt); Place(bar, 0, 0, PW, 34);
        bar.gameObject.AddComponent<Image>().color = C_Bar;
        var titleRt = NewUI("Title", bar); Stretch(titleRt);
        var title = titleRt.gameObject.AddComponent<TextMeshProUGUI>();
        title.text = "EQUIPMENT"; title.fontSize = 16; title.color = C_Gold;
        title.fontStyle = FontStyles.Bold; title.alignment = TextAlignmentOptions.Center;
        title.raycastTarget = false;

        var closeRt = NewUI("Close", bar); Place(closeRt, PW - 30, 4, 26, 26);
        var closeImg = closeRt.gameObject.AddComponent<Image>();
        closeImg.color = new Color(0.55f, 0.18f, 0.15f, 1f);
        var cbtn = closeRt.gameObject.AddComponent<Button>(); cbtn.targetGraphic = closeImg;
        cbtn.onClick.AddListener(Close);
        var cxRt = NewUI("X", closeRt); Stretch(cxRt);
        var cx = cxRt.gameObject.AddComponent<TextMeshProUGUI>();
        cx.text = "X"; cx.fontSize = 14; cx.alignment = TextAlignmentOptions.Center;
        cx.color = Color.white; cx.raycastTarget = false;

        // Paper-doll layout (where you'd wear each item).
        const float S = 78;       // slot size
        float cx0 = (PW - S) / 2f; // centre column x
        float colL = cx0 - S - 22; // left column
        float colR = cx0 + S + 22; // right column
        float row1 = 54, row2 = row1 + S + 26, row3 = row2 + S + 26, row4 = row3 + S + 26;

        MakeSlot("Helmet", cx0,  row1, S);   // head
        MakeSlot("Ammo",   colR, row1, S);   // quiver, top-right (OSRS-style)
        MakeSlot("Weapon", colL, row2, S);   // main hand
        MakeSlot("Chest",  cx0,  row2, S);   // body
        MakeSlot("Shield", colR, row2, S);   // off hand
        MakeSlot("Legs",   cx0,  row3, S);   // legs
        MakeSlot("Tool",   colL, row3, S);   // belt / tool

        var hintRt = NewUI("Hint", panelRt); Place(hintRt, 12, row4, PW - 24, 30);
        var hint = hintRt.gameObject.AddComponent<TextMeshProUGUI>();
        hint.text = "Click a slot to unequip. (F9 toggles this panel)";
        hint.fontSize = 13; hint.color = UITheme.TextDim;
        hint.alignment = TextAlignmentOptions.Center; hint.raycastTarget = false;
    }

    void MakeSlot(string slotName, float x, float y, float size)
    {
        var cellRt = NewUI("Slot_" + slotName, _panel.transform);
        Place(cellRt, x, y, size, size);
        var bg = cellRt.gameObject.AddComponent<Image>();
        bg.color = C_SlotEmpty;
        var btn = cellRt.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        string captured = slotName;
        btn.onClick.AddListener(() => Unequip(captured));

        var ghostRt = NewUI("GhostIcon", cellRt);
        ghostRt.anchorMin = Vector2.zero; ghostRt.anchorMax = Vector2.one;
        ghostRt.offsetMin = new Vector2(14, 14); ghostRt.offsetMax = new Vector2(-14, -14);
        var ghost = ghostRt.gameObject.AddComponent<Image>();
        string ghostPath = GhostIcon(slotName);
        ghost.sprite = ghostPath != null ? Resources.Load<Sprite>(ghostPath) : null;
        ghost.color = new Color(0.85f, 0.80f, 0.70f, 0.14f);
        ghost.raycastTarget = false; ghost.preserveAspect = true; ghost.enabled = ghost.sprite != null;

        var iconRt = NewUI("Icon", cellRt);
        iconRt.anchorMin = Vector2.zero; iconRt.anchorMax = Vector2.one;
        iconRt.offsetMin = new Vector2(6, 6); iconRt.offsetMax = new Vector2(-6, -6);
        var icon = iconRt.gameObject.AddComponent<Image>();
        icon.raycastTarget = false; icon.preserveAspect = true; icon.enabled = false;

        // label under the slot
        var labelRt = NewUI("Label", cellRt);
        labelRt.anchorMin = new Vector2(0, 0); labelRt.anchorMax = new Vector2(1, 0);
        labelRt.pivot = new Vector2(0.5f, 1f);
        labelRt.anchoredPosition = new Vector2(0, -2);
        labelRt.sizeDelta = new Vector2(size + 30, 18);
        var label = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = slotName; label.fontSize = 13; label.color = UITheme.TextDim;
        label.alignment = TextAlignmentOptions.Top; label.raycastTarget = false;

        _slots.Add(new Slot { name = slotName, bg = bg, icon = icon, ghost = ghost, label = label });
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
