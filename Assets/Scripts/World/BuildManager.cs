using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using TMPro;

/// <summary>
/// Hearthcraft construction — the build menu + ghost placement, fully self-building (no inspector
/// setup, auto-spawns after every scene load like CraftingUI / CompanionManager).
///
///   • Press H            — open/close the build menu.
///   • Click a buildable  — enter placement; a translucent ghost follows the cursor.
///   • Left-click ground  — build it (consumes materials, gates on Hearthcraft level, grants XP).
///   • R                  — rotate the ghost.  Esc / H — cancel placement.
///
/// Placement spawns primitive placeholders carrying the real behaviour (CraftingStation / BankingCrate
/// / AFKStation / Turret / HousingPlot). Swap in proper models later — the components don't care.
/// NOTE: placed deployables are session-only; save/load persistence is a future pass.
/// </summary>
public class BuildManager : MonoBehaviour
{
    public static BuildManager Instance { get; private set; }
    public KeyCode menuKey = KeyCode.H;

    public bool IsPlacing => _placing != null;

    // ── palette ──
    static readonly Color C_Backdrop = new(0f, 0f, 0f, 0.45f);
    static readonly Color C_Panel    = new(0.13f, 0.12f, 0.10f, 0.98f);
    static readonly Color C_TitleBar = new(0.20f, 0.17f, 0.12f, 1f);
    static readonly Color C_Row      = new(0.18f, 0.17f, 0.14f, 1f);
    static readonly Color C_Gold     = new(1f, 0.85f, 0.45f, 1f);
    static readonly Color C_Good     = new(0.55f, 0.95f, 0.55f, 1f);
    static readonly Color C_Bad      = new(0.95f, 0.45f, 0.45f, 1f);

    GameObject _backdrop;
    RectTransform _listContent;

    Buildable _placing;
    GameObject _ghost;
    Renderer _ghostRend;
    Material _ghostMat;
    float _ghostYaw;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("BuildManager (auto)").AddComponent<BuildManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildMenuUI();
        if (_backdrop) _backdrop.SetActive(false);
    }

    void Update()
    {
        if (IsTyping()) return;

        if (IsPlacing) { UpdatePlacement(); return; }

        if (Input.GetKeyDown(menuKey)) ToggleMenu();
    }

    bool IsTyping() => ChatInput.IsTyping;

    // ── placement ─────────────────────────────────────────────────────────
    void StartPlacement(Buildable b)
    {
        _placing = b;
        _ghostYaw = 0f;
        if (_backdrop) _backdrop.SetActive(false);

        if (_ghost != null) Destroy(_ghost);
        _ghost = GameObject.CreatePrimitive(b.kind == BuildKind.Turret ? PrimitiveType.Cylinder : PrimitiveType.Cube);
        _ghost.name = "BuildGhost";
        var col = _ghost.GetComponent<Collider>();
        if (col != null) Destroy(col);            // ghost must not block its own ground raycast
        _ghost.transform.localScale = b.size;
        _ghostRend = _ghost.GetComponent<Renderer>();
        _ghostMat = MakeGhostMat();
        _ghostRend.sharedMaterial = _ghostMat;

        Msg($"Placing <color=#FFD24A>{b.name}</color> — left-click to build, R to rotate, Esc to cancel.");
    }

    void CancelPlacement()
    {
        _placing = null;
        if (_ghost != null) { Destroy(_ghost); _ghost = null; }
    }

    void UpdatePlacement()
    {
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(menuKey)) { CancelPlacement(); return; }
        if (Input.GetKeyDown(KeyCode.R)) _ghostYaw += 45f;

        var cam = Camera.main;
        if (cam == null) return;

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (overUI || !Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out var hit, 500f))
        {
            if (_ghost) _ghost.SetActive(false);
            return;
        }

        if (_ghost) _ghost.SetActive(true);
        _ghost.transform.position = hit.point + Vector3.up * (_placing.size.y * 0.5f);
        _ghost.transform.rotation = Quaternion.Euler(0f, _ghostYaw, 0f);

        bool ok = HasLevel(_placing) && CanAfford(_placing);
        SetGhostColor(ok);

        if (Input.GetMouseButtonDown(0))
        {
            if (!ok) { Msg(WhyCantBuild(_placing)); return; }
            PlaceNow(hit.point);
        }
    }

    void PlaceNow(Vector3 ground)
    {
        var p = PlayerEntity.Instance;
        if (p == null) return;

        foreach (var c in _placing.cost) p.Inventory.Remove(c.itemId, c.quantity);
        p.Stats.AddXP(Skill.Smithing, _placing.xp);
        SpawnDeployable(_placing, ground, Quaternion.Euler(0f, _ghostYaw, 0f));
        Msg($"You build a <color=#FFD24A>{_placing.name}</color>. ({_placing.xp} Hearthcraft XP)");

        // Keep placing more of the same while you can still afford it; otherwise drop out.
        if (!CanAfford(_placing)) { Msg("Out of materials for another one."); CancelPlacement(); }
    }

    public static GameObject SpawnDeployable(Buildable b, Vector3 ground, Quaternion rot)
    {
        var go = GameObject.CreatePrimitive(b.kind == BuildKind.Turret ? PrimitiveType.Cylinder : PrimitiveType.Cube);
        go.name = b.name;
        go.transform.position = ground + Vector3.up * (b.size.y * 0.5f);
        go.transform.rotation = rot;
        go.transform.localScale = b.size;
        Paint(go, b.color);
        foreach (var col in go.GetComponentsInChildren<Collider>()) col.isTrigger = false;
        go.AddComponent<InteractionFeedback>();

        switch (b.kind)
        {
            case BuildKind.CookingFire: AddStation(go, StationType.CookingFire, CraftingRecipes.Cooking());   break;
            case BuildKind.Furnace:     AddStation(go, StationType.Furnace,     CraftingRecipes.Furnace());   break;
            case BuildKind.Workbench:   AddStation(go, StationType.Workbench,   CraftingRecipes.Workbench()); break;
            case BuildKind.BankChest:   go.AddComponent<BankingCrate>();                                       break;
            case BuildKind.AFKStation:
                var afk = go.AddComponent<AFKStation>();
                afk.stationName = b.name; afk.skill = b.afkSkill;
                afk.produceItemId = b.afkProduceItemId; afk.consumeItemId = b.afkConsumeItemId;
                break;
            case BuildKind.Turret:
                var tu = go.AddComponent<Turret>();
                tu.turretName = b.name; tu.maxHit = b.turretMaxHit; tu.range = b.turretRange;
                break;
            case BuildKind.HousingPlot: go.AddComponent<HousingPlot>(); break;
        }
        return go;
    }

    static void AddStation(GameObject go, StationType type, List<CraftingRecipe> recipes)
    {
        var st = go.AddComponent<CraftingStation>();
        st.stationType = type; st.recipes = recipes;
    }

    // ── validity ──────────────────────────────────────────────────────────
    static bool HasLevel(Buildable b) =>
        PlayerEntity.Instance != null && PlayerEntity.Instance.Stats.GetLevel(Skill.Smithing) >= b.hearthLevel;

    static bool CanAfford(Buildable b)
    {
        var p = PlayerEntity.Instance;
        if (p == null) return false;
        foreach (var c in b.cost)
            if (!p.Inventory.Contains(c.itemId, c.quantity)) return false;
        return true;
    }

    static string WhyCantBuild(Buildable b)
    {
        if (!HasLevel(b)) return $"You need Hearthcraft level {b.hearthLevel} to build that.";
        return "You don't have the materials for that.";
    }

    static string CostText(Buildable b)
    {
        var parts = new List<string>();
        foreach (var c in b.cost)
        {
            var item = ItemRegistry.Get(c.itemId);
            parts.Add($"{c.quantity} {(item != null ? item.name : "item " + c.itemId)}");
        }
        return parts.Count > 0 ? string.Join(", ", parts) : "free";
    }

    // ── menu UI (self-built) ──────────────────────────────────────────────
    void ToggleMenu()
    {
        if (_backdrop == null) return;
        bool open = !_backdrop.activeSelf;
        if (open) RebuildList();
        _backdrop.SetActive(open);
    }

    void RebuildList()
    {
        for (int i = _listContent.childCount - 1; i >= 0; i--)
            Destroy(_listContent.GetChild(i).gameObject);

        var p = PlayerEntity.Instance;
        int hearth = p != null ? p.Stats.GetLevel(Skill.Smithing) : 1;

        foreach (var b in BuildCatalog.All())
        {
            var rowRt = NewUI("Row_" + b.name, _listContent);
            var bg = rowRt.gameObject.AddComponent<Image>();
            bg.color = C_Row;
            var le = rowRt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 48; le.preferredHeight = 48;

            bool unlocked = hearth >= b.hearthLevel;
            bool afford = CanAfford(b);

            var labelRt = NewUI("Label", rowRt);
            labelRt.anchorMin = new Vector2(0, 0); labelRt.anchorMax = new Vector2(1, 1);
            labelRt.offsetMin = new Vector2(8, 2); labelRt.offsetMax = new Vector2(-8, -2);
            var label = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
            string lvlHex = ColorUtility.ToHtmlStringRGB(unlocked ? C_Good : C_Bad);
            string costHex = ColorUtility.ToHtmlStringRGB(afford ? C_Good : C_Bad);
            label.text =
                $"<b>{b.name}</b>   <color=#{lvlHex}>Hearthcraft {b.hearthLevel}</color>\n" +
                $"<size=80%><color=#{costHex}>{CostText(b)}</color>   ·   {b.xp} XP   ·   {b.description}</size>";
            label.fontSize = 15;
            label.color = unlocked ? Color.white : new Color(0.6f, 0.6f, 0.6f, 1f);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;

            var btn = rowRt.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            var captured = b;
            btn.onClick.AddListener(() =>
            {
                if (!HasLevel(captured)) { Msg(WhyCantBuild(captured)); return; }
                StartPlacement(captured);
            });
        }
    }

    void BuildMenuUI()
    {
        var canvasGo = new GameObject("BuildCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var backdropRt = NewUI("Backdrop", canvasGo.transform);
        Stretch(backdropRt);
        _backdrop = backdropRt.gameObject;
        _backdrop.AddComponent<Image>().color = C_Backdrop;
        var backBtn = _backdrop.AddComponent<Button>();
        backBtn.transition = Selectable.Transition.None;
        backBtn.onClick.AddListener(() => { if (!IsPlacing) _backdrop.SetActive(false); });

        const float PW = 560, PH = 480;
        var panelRt = NewUI("Panel", backdropRt);
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(PW, PH);
        panelRt.anchoredPosition = Vector2.zero;
        panelRt.gameObject.AddComponent<Image>().color = C_Panel;
        panelRt.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // swallow clicks

        var titleBar = NewUI("TitleBar", panelRt);
        Place(titleBar, 0, 0, PW, 40);
        titleBar.gameObject.AddComponent<Image>().color = C_TitleBar;
        var titleRt = NewUI("Title", titleBar);
        Stretch(titleRt);
        var title = titleRt.gameObject.AddComponent<TextMeshProUGUI>();
        title.text = "HEARTHCRAFT — BUILD";
        title.fontSize = 18; title.color = C_Gold; title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center; title.raycastTarget = false;

        var closeRt = NewUI("Close", titleBar);
        Place(closeRt, PW - 34, 6, 28, 28);
        var closeImg = closeRt.gameObject.AddComponent<Image>();
        closeImg.color = new Color(0.5f, 0.15f, 0.12f, 1f);
        var closeBtn = closeRt.gameObject.AddComponent<Button>();
        closeBtn.targetGraphic = closeImg;
        closeBtn.onClick.AddListener(() => _backdrop.SetActive(false));
        var closeTxtRt = NewUI("X", closeRt); Stretch(closeTxtRt);
        var closeTxt = closeTxtRt.gameObject.AddComponent<TextMeshProUGUI>();
        closeTxt.text = "X"; closeTxt.fontSize = 16; closeTxt.color = Color.white;
        closeTxt.alignment = TextAlignmentOptions.Center; closeTxt.raycastTarget = false;

        // scrollable list
        var viewport = NewUI("Viewport", panelRt);
        Place(viewport, 12, 48, PW - 24, PH - 60);
        viewport.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.07f, 1f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var contentRt = NewUI("Content", viewport);
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;
        _listContent = contentRt;
        var vlg = contentRt.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 3; vlg.padding = new RectOffset(3, 3, 3, 3);
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fitter = contentRt.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = contentRt;
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24;
    }

    // ── visuals ───────────────────────────────────────────────────────────
    void SetGhostColor(bool ok)
    {
        if (_ghostMat == null) return;
        var c = ok ? new Color(0.35f, 1f, 0.45f, 0.4f) : new Color(1f, 0.35f, 0.35f, 0.4f);
        _ghostMat.color = c;
        if (_ghostMat.HasProperty("_BaseColor")) _ghostMat.SetColor("_BaseColor", c);
    }

    static Material MakeGhostMat()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader) { name = "BuildGhost" };
        var c = new Color(0.35f, 1f, 0.45f, 0.4f);
        mat.color = c;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        // URP transparent surface setup.
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_ZWrite"))  mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        return mat;
    }

    static void Paint(GameObject go, Color color)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
        { name = go.name.Replace(" ", "") };
        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        rend.sharedMaterial = mat;
    }

    // ── tiny UI helpers (mirrors CraftingUI) ──────────────────────────────
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

    static void Msg(string m) => HUDController.Emit("<color=#9FE0C0>[BUILD]:</color> " + m);
}
