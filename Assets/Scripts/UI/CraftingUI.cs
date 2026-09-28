using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// OSRS-style crafting selection window for furnaces / cooking fires / benches.
///
/// Fully self-building: it constructs its own Canvas and every widget at runtime,
/// so there is NOTHING to wire up in the Unity inspector. It also auto-spawns
/// itself after each scene load, so you don't even need to drop it into a scene.
/// </summary>
public class CraftingUI : MonoBehaviour
{
    public static CraftingUI Instance { get; private set; }

    // ── palette ──────────────────────────────────────────────────────────
    static readonly Color C_Backdrop   = new Color(0f, 0f, 0f, 0.45f);
    static readonly Color C_Panel      = new Color(0.13f, 0.12f, 0.10f, 0.98f);
    static readonly Color C_TitleBar   = new Color(0.20f, 0.17f, 0.12f, 1f);
    static readonly Color C_Slot       = new Color(0.08f, 0.08f, 0.07f, 1f);
    static readonly Color C_Row        = new Color(0.18f, 0.17f, 0.14f, 1f);
    static readonly Color C_RowSel     = new Color(0.42f, 0.33f, 0.14f, 1f);
    static readonly Color C_Btn        = new Color(0.25f, 0.22f, 0.16f, 1f);
    static readonly Color C_BtnSel     = new Color(0.55f, 0.42f, 0.16f, 1f);
    static readonly Color C_Gold       = new Color(1f, 0.85f, 0.45f, 1f);
    static readonly Color C_Good       = new Color(0.55f, 0.95f, 0.55f, 1f);
    static readonly Color C_Bad        = new Color(0.95f, 0.45f, 0.45f, 1f);

    // ── runtime widget refs ──────────────────────────────────────────────
    GameObject _backdrop;
    GameObject _panel;
    TMP_Text   _titleText;
    RectTransform _listContent;

    Image    _detailIcon;
    TMP_Text _detailName;
    TMP_Text _detailReqs;
    TMP_Text _detailMeta;
    TMP_Text _craftBtnLabel;
    readonly List<(Button btn, Image bg, int qty)> _qtyButtons = new();

    readonly List<RecipeRow> _rows = new();

    CraftingStation _station;
    CraftingRecipe  _selected;
    int _quantity = 1;          // -1 means "All"

    class RecipeRow
    {
        public CraftingRecipe recipe;
        public Image bg;
        public TMP_Text label;
    }

    // Spawns the UI automatically after every scene load — zero editor setup.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("CraftingUI (auto)");
        go.AddComponent<CraftingUI>();
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
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    public bool IsOpen => _backdrop != null && _backdrop.activeSelf;

    // ── public API ───────────────────────────────────────────────────────
    public void Open(CraftingStation station)
    {
        _station = station;
        if (_titleText) _titleText.text = station != null ? station.DisplayName.ToUpper() : "CRAFTING";

        BuildRecipeRows();
        _quantity = 1;
        _selected = (_station != null && _station.recipes.Count > 0) ? _station.recipes[0] : null;
        UpdateQtyButtons();
        RefreshSelection();

        if (_backdrop) _backdrop.SetActive(true);
        _panel.transform.SetAsLastSibling();
    }

    public void Close()
    {
        if (_backdrop) _backdrop.SetActive(false);
        // The batch keeps running in the background after the window closes — you can
        // open your inventory and watch each item finish. Only walking away (Interactor3D)
        // or the job completing/failing stops it.
    }

    /// <summary>Called by CraftingManager after a craft so counts/colors update live.</summary>
    public void RefreshAfterCraft()
    {
        if (!IsOpen) return;
        UpdateRowAvailability();
        RefreshDetail();
    }

    // ── selection / refresh ──────────────────────────────────────────────
    void RefreshSelection()
    {
        foreach (var row in _rows)
            row.bg.color = (row.recipe == _selected) ? C_RowSel : C_Row;
        RefreshDetail();
    }

    void RefreshDetail()
    {
        var player = PlayerEntity.Instance;

        if (_selected == null)
        {
            if (_detailIcon) { _detailIcon.enabled = false; }
            if (_detailName) _detailName.text = "Select an item to make";
            if (_detailReqs) _detailReqs.text = "";
            if (_detailMeta) _detailMeta.text = "";
            if (_craftBtnLabel) _craftBtnLabel.text = "Make";
            return;
        }

        var outItem = ItemRegistry.Get(_selected.outputItemId);
        if (_detailIcon)
        {
            _detailIcon.enabled = outItem != null && outItem.icon != null;
            _detailIcon.sprite = outItem != null ? outItem.icon : null;
        }
        if (_detailName)
        {
            string outName = outItem != null ? outItem.name : _selected.name;
            string qtySuffix = _selected.outputQty > 1 ? $" x{_selected.outputQty}" : "";
            _detailName.text = outName + qtySuffix;
        }

        // Ingredient lines, green if you have enough, red if not.
        if (_detailReqs)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ing in _selected.inputs)
            {
                var ingItem = ItemRegistry.Get(ing.itemId);
                string ingName = ingItem != null ? ingItem.name : ("Item " + ing.itemId);
                int have = player != null ? player.Inventory.CountOf(ing.itemId) : 0;
                bool ok = have >= ing.quantity;
                string hex = ColorUtility.ToHtmlStringRGB(ok ? C_Good : C_Bad);
                sb.AppendLine($"<color=#{hex}>{ingName}: {have}/{ing.quantity}</color>");
            }
            _detailReqs.text = sb.ToString().TrimEnd();
        }

        // Level + XP + max craftable.
        if (_detailMeta)
        {
            int lvl = player != null ? player.Stats.GetLevel(_selected.skill) : 1;
            bool lvlOk = lvl >= _selected.levelRequired;
            string lvlHex = ColorUtility.ToHtmlStringRGB(lvlOk ? C_Good : C_Bad);
            int max = CraftingManager.Instance != null ? CraftingManager.Instance.MaxCraftable(_selected) : 0;
            _detailMeta.text =
                $"Skill: {_selected.skill}\n" +
                $"<color=#{lvlHex}>Level required: {_selected.levelRequired}</color>\n" +
                $"XP each: {_selected.xpGranted}\n" +
                $"You can make: {max}";
        }

        if (_craftBtnLabel)
        {
            if (_quantity == -1)
            {
                int max = CraftingManager.Instance != null ? CraftingManager.Instance.MaxCraftable(_selected) : 0;
                _craftBtnLabel.text = $"Make All ({max})";
            }
            else _craftBtnLabel.text = $"Make x{_quantity}";
        }
    }

    void UpdateRowAvailability()
    {
        var player = PlayerEntity.Instance;
        foreach (var row in _rows)
        {
            bool canMake = player != null
                && player.Stats.GetLevel(row.recipe.skill) >= row.recipe.levelRequired
                && row.recipe.CanCraft(player.Inventory);
            row.label.color = canMake ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
        }
    }

    void OnCraftPressed()
    {
        if (_selected == null || CraftingManager.Instance == null) return;
        int count = _quantity == -1 ? CraftingManager.Instance.MaxCraftable(_selected) : _quantity;
        if (count <= 0) count = 1;
        CraftingManager.Instance.CraftMany(_selected, count);
    }

    void SetQuantity(int q)
    {
        _quantity = q;
        UpdateQtyButtons();
        RefreshDetail();
    }

    void UpdateQtyButtons()
    {
        foreach (var (_, bg, qty) in _qtyButtons)
            bg.color = (qty == _quantity) ? C_BtnSel : C_Btn;
    }

    // ── recipe row construction ──────────────────────────────────────────
    void BuildRecipeRows()
    {
        foreach (var row in _rows) Destroy(row.bg.gameObject);
        _rows.Clear();

        if (_station == null) return;

        foreach (var recipe in _station.recipes)
        {
            var rowGo = NewUI("Recipe_" + recipe.name, _listContent);
            var bg = rowGo.gameObject.AddComponent<Image>();
            bg.color = C_Row;
            var le = rowGo.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 42; le.preferredHeight = 42;

            // icon
            var outItem = ItemRegistry.Get(recipe.outputItemId);
            var iconGo = NewUI("Icon", rowGo);
            Place(iconGo, 4, 3, 36, 36);
            var icon = iconGo.gameObject.AddComponent<Image>();
            icon.color = C_Slot;
            if (outItem != null && outItem.icon != null) { icon.sprite = outItem.icon; icon.color = Color.white; }
            icon.raycastTarget = false;

            // label
            var labelGo = NewUI("Label", rowGo);
            labelGo.anchorMin = new Vector2(0, 0); labelGo.anchorMax = new Vector2(1, 1);
            labelGo.offsetMin = new Vector2(46, 0); labelGo.offsetMax = new Vector2(-6, 0);
            var label = labelGo.gameObject.AddComponent<TextMeshProUGUI>();
            string outName = outItem != null ? outItem.name : recipe.name;
            label.text = recipe.outputQty > 1 ? $"{outName} x{recipe.outputQty}" : outName;
            label.fontSize = 15;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;

            var btn = rowGo.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            var captured = recipe;
            btn.onClick.AddListener(() => { _selected = captured; RefreshSelection(); });

            _rows.Add(new RecipeRow { recipe = recipe, bg = bg, label = label });
        }

        UpdateRowAvailability();
    }

    // ── UI construction ──────────────────────────────────────────────────
    void BuildUI()
    {
        // Canvas (screen-space overlay, drawn above the HUD)
        var canvasGo = new GameObject("CraftingCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        UITheme.ApplyScaler(scaler, new Vector2(1920, 1080));

        // Container only: inventory and HUD tabs remain usable while crafting.
        var backdropRt = NewUI("Backdrop", canvasGo.transform);
        Stretch(backdropRt);
        _backdrop = backdropRt.gameObject;
        var backdropImg = _backdrop.AddComponent<Image>();
        backdropImg.color = Color.clear;
        backdropImg.raycastTarget = false;

        // Main panel
        const float PW = 580, PH = 460;
        var panelRt = NewUI("Panel", backdropRt);
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0f, 0.5f);
        panelRt.pivot = new Vector2(0f, 0.5f);
        panelRt.sizeDelta = new Vector2(PW, PH);
        panelRt.anchoredPosition = new Vector2(20f, 0f);
        _panel = panelRt.gameObject;
        var panelImg = _panel.AddComponent<Image>();
        panelImg.color = C_Panel;
        // swallow clicks so they don't reach the backdrop / world
        _panel.AddComponent<Button>().transition = Selectable.Transition.None;

        // Title bar
        var titleBar = NewUI("TitleBar", panelRt);
        Place(titleBar, 0, 0, PW, 38);
        titleBar.gameObject.AddComponent<Image>().color = C_TitleBar;

        var titleTxtRt = NewUI("Title", titleBar);
        Stretch(titleTxtRt);
        _titleText = titleTxtRt.gameObject.AddComponent<TextMeshProUGUI>();
        _titleText.text = "CRAFTING";
        _titleText.fontSize = 18;
        _titleText.color = C_Gold;
        _titleText.alignment = TextAlignmentOptions.Center;
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.raycastTarget = false;

        // Close button
        var closeRt = NewUI("Close", titleBar);
        Place(closeRt, PW - 34, 5, 28, 28);
        var closeImg = closeRt.gameObject.AddComponent<Image>();
        closeImg.color = new Color(0.5f, 0.15f, 0.12f, 1f);
        var closeBtn = closeRt.gameObject.AddComponent<Button>();
        closeBtn.targetGraphic = closeImg;
        closeBtn.onClick.AddListener(Close);
        var closeLbl = NewUI("X", closeRt);
        Stretch(closeLbl);
        var closeTxt = closeLbl.gameObject.AddComponent<TextMeshProUGUI>();
        closeTxt.text = "X"; closeTxt.fontSize = 16; closeTxt.alignment = TextAlignmentOptions.Center;
        closeTxt.color = Color.white; closeTxt.raycastTarget = false;

        // ── left: scrollable recipe list ─────────────────────────────────
        const float listX = 12, listY = 48, listW = 250, listH = PH - listY - 12;
        var viewport = NewUI("Viewport", panelRt);
        Place(viewport, listX, listY, listW, listH);
        viewport.gameObject.AddComponent<Image>().color = C_Slot;
        viewport.gameObject.AddComponent<RectMask2D>();

        var contentRt = NewUI("Content", viewport);
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0, 0);
        _listContent = contentRt;
        var vlg = contentRt.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 3; vlg.padding = new RectOffset(3, 3, 3, 3);
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fitter = contentRt.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = contentRt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24;

        // ── right: detail pane ────────────────────────────────────────────
        float dX = listX + listW + 14;
        float dW = PW - dX - 12;

        // output icon
        var iconRt = NewUI("DetailIcon", panelRt);
        Place(iconRt, dX + (dW - 72) / 2f, listY + 4, 72, 72);
        var iconBg = NewUI("IconBg", iconRt); Stretch(iconBg);
        iconBg.gameObject.AddComponent<Image>().color = C_Slot;
        var iconImgRt = NewUI("Img", iconRt); Stretch(iconImgRt);
        _detailIcon = iconImgRt.gameObject.AddComponent<Image>();
        _detailIcon.raycastTarget = false;
        _detailIcon.preserveAspect = true;

        // output name
        var nameRt = NewUI("DetailName", panelRt);
        Place(nameRt, dX, listY + 84, dW, 26);
        _detailName = nameRt.gameObject.AddComponent<TextMeshProUGUI>();
        _detailName.fontSize = 17; _detailName.color = C_Gold;
        _detailName.alignment = TextAlignmentOptions.Center;
        _detailName.fontStyle = FontStyles.Bold;
        _detailName.raycastTarget = false;

        // "Materials" header
        var matsHdrRt = NewUI("MatsHeader", panelRt);
        Place(matsHdrRt, dX, listY + 116, dW, 20);
        var matsHdr = matsHdrRt.gameObject.AddComponent<TextMeshProUGUI>();
        matsHdr.text = "MATERIALS"; matsHdr.fontSize = 12;
        matsHdr.color = new Color(0.7f, 0.7f, 0.7f, 1f);
        matsHdr.alignment = TextAlignmentOptions.Left; matsHdr.raycastTarget = false;

        // ingredient lines
        var reqsRt = NewUI("DetailReqs", panelRt);
        Place(reqsRt, dX, listY + 138, dW, 78);
        _detailReqs = reqsRt.gameObject.AddComponent<TextMeshProUGUI>();
        _detailReqs.fontSize = 14; _detailReqs.alignment = TextAlignmentOptions.TopLeft;
        _detailReqs.raycastTarget = false;

        // meta (skill/level/xp/max)
        var metaRt = NewUI("DetailMeta", panelRt);
        Place(metaRt, dX, listY + 218, dW, 88);
        _detailMeta = metaRt.gameObject.AddComponent<TextMeshProUGUI>();
        _detailMeta.fontSize = 13; _detailMeta.alignment = TextAlignmentOptions.TopLeft;
        _detailMeta.color = new Color(0.85f, 0.85f, 0.85f, 1f);
        _detailMeta.raycastTarget = false;

        // quantity buttons row
        float qY = PH - 92;
        int[] qtys = { 1, 5, 10, -1 };
        string[] qLabels = { "1", "5", "10", "All" };
        float qW = (dW - 9) / 4f;
        for (int i = 0; i < qtys.Length; i++)
        {
            var qRt = NewUI("Qty_" + qLabels[i], panelRt);
            Place(qRt, dX + i * (qW + 3), qY, qW, 28);
            var qImg = qRt.gameObject.AddComponent<Image>();
            qImg.color = C_Btn;
            var qBtn = qRt.gameObject.AddComponent<Button>();
            qBtn.targetGraphic = qImg;
            int captured = qtys[i];
            qBtn.onClick.AddListener(() => SetQuantity(captured));
            var qLblRt = NewUI("Lbl", qRt); Stretch(qLblRt);
            var qLbl = qLblRt.gameObject.AddComponent<TextMeshProUGUI>();
            qLbl.text = qLabels[i]; qLbl.fontSize = 14;
            qLbl.alignment = TextAlignmentOptions.Center; qLbl.raycastTarget = false;
            _qtyButtons.Add((qBtn, qImg, qtys[i]));
        }

        // big Make button
        var makeRt = NewUI("MakeButton", panelRt);
        Place(makeRt, dX, PH - 56, dW, 40);
        var makeImg = makeRt.gameObject.AddComponent<Image>();
        makeImg.color = new Color(0.30f, 0.45f, 0.20f, 1f);
        var makeBtn = makeRt.gameObject.AddComponent<Button>();
        makeBtn.targetGraphic = makeImg;
        makeBtn.onClick.AddListener(OnCraftPressed);
        var makeLblRt = NewUI("Lbl", makeRt); Stretch(makeLblRt);
        _craftBtnLabel = makeLblRt.gameObject.AddComponent<TextMeshProUGUI>();
        _craftBtnLabel.text = "Make"; _craftBtnLabel.fontSize = 18;
        _craftBtnLabel.fontStyle = FontStyles.Bold;
        _craftBtnLabel.alignment = TextAlignmentOptions.Center;
        _craftBtnLabel.color = Color.white; _craftBtnLabel.raycastTarget = false;
    }

    // ── tiny helpers ─────────────────────────────────────────────────────
    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    /// <summary>Anchor top-left and place by (x, y-from-top) with size (w, h).</summary>
    static void Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
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
