#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// EDITOR-ONLY test panel — never compiled into a real build (#if UNITY_EDITOR), so a friend playing
/// a build never sees it. Auto-spawned and self-building. A small "DEV" button (top-left) toggles a
/// panel with:
///   • New Game        — wipe the save, reload fresh, and fire the splash (the easy "start over" button).
///   • Max Combat Lv   — 99 in every combat skill so no tier gear is level-gated.
///   • Clear Bag       — strip all weapons/armor/ammo from the inventory to reset a test.
///   • A scroll list of EVERY tier weapon, armor piece and ammo — one click drops it in your bag
///     (or the bank if the bag is full), so you can equip and test any tier instantly.
/// </summary>
public class DevPanel : MonoBehaviour
{
    public static DevPanel Instance { get; private set; }

    const string TitleScene = "CharacterSelect";

    GameObject _panel;
    TMP_FontAsset _font;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded += (scene, mode) => MaybeSpawn(scene.name);
        MaybeSpawn(SceneManager.GetActiveScene().name);
    }

    static void MaybeSpawn(string sceneName)
    {
        if (sceneName == TitleScene) return;          // gameplay scenes only
        if (Instance != null) return;
        new GameObject("DevPanel (auto)").AddComponent<DevPanel>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        _font = UIUtil.FindFont();
        EnsureEventSystem();
        BuildToggle();
    }

    // ── top-level toggle button ───────────────────────────────────────────
    void BuildToggle()
    {
        var canvasGo = new GameObject("DevCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;   // above the HUD, below the splash
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;   // balance W/H so odd aspect ratios don't blow the panel up

        // Small, quiet pill at the top-centre — the top-left belongs to the player frame, and this is an
        // editor-only tool, so it shouldn't shout.
        var btn = Button(canvasGo.transform, "DEV", Vector2.zero, new Vector2(64, 26),
                         new Color(0.16f, 0.15f, 0.13f, 0.85f), TogglePanel);
        var rt = (RectTransform)btn.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1);   // pin top-centre
        rt.pivot = new Vector2(0.5f, 1);
        rt.anchoredPosition = new Vector2(0, -8);
        var img = btn.GetComponent<Image>();
        UITheme.Tile(img);
        var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (lbl != null) UITheme.Label(lbl, 14f, new Color(0.75f, 0.60f, 0.95f, 1f));   // dev-violet text only

        BuildPanel(canvasGo.transform);
        _panel.SetActive(false);
    }

    void TogglePanel() { if (_panel != null) _panel.SetActive(!_panel.activeSelf); }

    // ── the panel itself ──────────────────────────────────────────────────
    void BuildPanel(Transform parent)
    {
        // Left-side panel that STRETCHES to the screen height (top/bottom margins) so the gear list
        // is always fully on-screen and scrolls within whatever space it has — never runs off the bottom.
        const float width = 360f;
        var rt = Rect("DevPanel", parent);
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.offsetMin = new Vector2(10, 12);            // left = 10, bottom margin = 12
        rt.offsetMax = new Vector2(10 + width, -54);   // right edge, top = 54 below the screen top
        rt.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.07f, 0.09f, 0.96f);
        _panel = rt.gameObject;

        // Drag bar across the top — grab it to reposition the whole panel (handy if the Game view is zoomed).
        DragBar(rt, "DEV MENU  ·  drag to move");

        // Action buttons — 2x2 grid, sized to the 360-wide panel.
        Vector2 bsize = new Vector2(170, 38);
        Button(rt, "New Game", Vector2.zero, bsize,
               new Color(0.22f, 0.45f, 0.25f), NewGameTutorial).SetTopLeft(rt, new Vector2(8, -30));
        Button(rt, "Reset Here", Vector2.zero, bsize,
               new Color(0.20f, 0.45f, 0.45f), ResetHere).SetTopLeft(rt, new Vector2(182, -30));
        Button(rt, "Max Lv", Vector2.zero, bsize,
               new Color(0.20f, 0.35f, 0.55f), MaxCombatLevels).SetTopLeft(rt, new Vector2(8, -72));
        Button(rt, "Clear Bag", Vector2.zero, bsize,
               new Color(0.5f, 0.3f, 0.18f), ClearBag).SetTopLeft(rt, new Vector2(182, -72));

        Header(rt, "Tier weapons / armor / ammo — click to add", new Vector2(0, -116));

        BuildGearList(rt);
    }

    void BuildGearList(Transform parent)
    {
        // Scroll view filling the panel below the header.
        var viewport = Rect("Viewport", parent);
        viewport.anchorMin = new Vector2(0, 0); viewport.anchorMax = new Vector2(1, 1);
        viewport.offsetMin = new Vector2(8, 8); viewport.offsetMax = new Vector2(-8, -140);
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.25f);
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        var scroll = parent.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.scrollSensitivity = 24;

        var content = Rect("Content", viewport);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 3; vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childControlWidth = true; vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;

        foreach (var item in ItemRegistry.All
                     .Where(i => i.IsWeapon || i.IsArmor || i.IsAmmo)
                     .OrderBy(i => i.id))
        {
            int id = item.id;
            string lvl = item.requirements != null && item.requirements.Count > 0
                ? "  (" + string.Join(", ", item.requirements.Select(r => $"{r.Key} {r.Value}")) + ")"
                : "";
            ListButton(content, $"{item.name}{lvl}", () => GiveItem(id));
        }
    }

    // ── actions ───────────────────────────────────────────────────────────
    void GiveItem(int id)
    {
        var player = PlayerEntity.Instance;
        var item = ItemRegistry.Get(id);
        if (player == null || item == null) return;

        int qty = item.IsAmmo ? 1000 : 1;
        if (player.Inventory.Add(id, qty))
            HUDController.Emit($"<color=#C080FF>[DEV]</color> +{qty} {item.name} → bag.");
        else if (player.Bank.Deposit(id, qty))
            HUDController.Emit($"<color=#C080FF>[DEV]</color> Bag full — {item.name} sent to your bank.");
        else
            HUDController.Emit($"<color=#FF8080>[DEV]</color> No room for {item.name}.");
    }

    void MaxCombatLevels()
    {
        var player = PlayerEntity.Instance;
        if (player == null) return;

        // SetXP bypasses the tutorial level cap on purpose — this is a test tool.
        // Skill-aware max: Endurance goes to 220, Fission is a combat skill too now.
        Skill[] combat = { Skill.Attack, Skill.Strength, Skill.Defence,
                           Skill.Marksmanship, Skill.Fission, Skill.Endurance };
        foreach (var s in combat)
            player.Stats.SetXP(s, XPTable.XPForLevel(s, XPTable.MaxLevelFor(s)));
        player.Stats.Heal(9999);   // top off HP now that Endurance is maxed

        HUDController.Emit($"<color=#C080FF>[DEV]</color> Combat skills maxed (Endurance {XPTable.EnduranceMax}, " +
                           $"rest {XPTable.MaxLevel}) — combat level {player.Stats.GetCombatLevel()}. " +
                           "(Reopen the Skills tab to refresh the display.)");
    }

    void ClearBag()
    {
        var player = PlayerEntity.Instance;
        if (player == null) return;
        foreach (var item in ItemRegistry.All.Where(i => i.IsWeapon || i.IsArmor || i.IsAmmo))
        {
            int have = player.Inventory.CountOf(item.id);
            if (have > 0) player.Inventory.Remove(item.id, have);
        }
        HUDController.Emit("<color=#C080FF>[DEV]</color> Cleared all gear from your bag.");
    }

    /// <summary>True "new character + new game": wipe the save and drop a fresh player onto the
    /// mainland — the same starting point a real new game uses (the tutorial island is retired).</summary>
    void NewGameTutorial()
    {
        SplashScreen.PendingNewGame = true;
        SaveManager.Instance?.DeleteSave();
        Time.timeScale = 1f;
        SceneManager.LoadScene(CharacterSelectUI.StartScene);   // Broken Crescent — the only world in the build
    }

    /// <summary>Fast in-place reset: wipe the save and reload the CURRENT scene fresh — handy for
    /// iterating in MainWorld3D without sitting through the tutorial.</summary>
    void ResetHere()
    {
        SplashScreen.PendingNewGame = true;
        SaveManager.Instance?.DeleteSave();
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    // ── builders / helpers ────────────────────────────────────────────────
    GameObject Button(Transform parent, string label, Vector2 pos, Vector2 size, Color color,
                      UnityEngine.Events.UnityAction onClick)
    {
        var rt = Rect(label + "Btn", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;

        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.highlightedColor = color * 1.3f;
        colors.pressedColor = color * 0.8f;
        colors.fadeDuration = 0.05f;
        btn.colors = colors;
        btn.onClick.AddListener(onClick);

        var txt = Label(rt, label, 22, FontStyles.Bold, Color.white);
        Stretch(txt.rectTransform);
        return rt.gameObject;
    }

    void ListButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        var rt = Rect("Row", parent);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.minHeight = 30; le.preferredHeight = 30;

        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0.18f, 0.16f, 0.2f, 1f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.3f, 0.26f, 0.34f, 1f);
        colors.pressedColor = new Color(0.4f, 0.2f, 0.4f, 1f);
        colors.fadeDuration = 0.05f;
        btn.colors = colors;
        btn.onClick.AddListener(onClick);

        var txt = Label(rt, label, 17, FontStyles.Normal, new Color(0.92f, 0.92f, 0.95f));
        var trt = txt.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(8, 0); trt.offsetMax = new Vector2(-8, 0);
        txt.alignment = TextAlignmentOptions.MidlineLeft;
        // Long gear names (e.g. "Tactical Ballistic Greaves (Hardening 60)") used to overflow the row
        // and clip. Auto-size shrinks the text just enough to always show the WHOLE name on one line.
        txt.enableAutoSizing = true;
        txt.fontSizeMin = 9;
        txt.fontSizeMax = 17;
    }

    // A grab strip at the top of the panel that drags the whole panel around.
    void DragBar(RectTransform panel, string text)
    {
        var rt = Rect("DragBar", panel);
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(0, 24);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0.16f, 0.13f, 0.18f, 1f);   // raycastTarget on so it receives drags
        rt.gameObject.AddComponent<DevDrag>().panel = panel;

        var t = Label(rt, text, 13, FontStyles.Bold, new Color(0.65f, 0.6f, 0.7f));
        Stretch(t.rectTransform);
        t.alignment = TextAlignmentOptions.Center;
    }

    void Header(Transform parent, string text, Vector2 pos)
    {
        var rt = Rect("Header", parent);
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(-16, 22);
        var t = Label(rt, text, 15, FontStyles.Italic, new Color(0.7f, 0.7f, 0.75f));
        Stretch(t.rectTransform);
        t.alignment = TextAlignmentOptions.Center;
    }

    TMP_Text Label(Transform parent, string content, float size, FontStyles style, Color color)
    {
        var rt = Rect("Label", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(200, size * 1.6f);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = _font; t.fontSize = size; t.fontStyle = style; t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.text = content; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }
}

/// <summary>Drag handle: translates the whole DevPanel with the pointer. Overlay canvas, so the
/// RectTransform's world position is in screen pixels and pointer delta maps 1:1.</summary>
class DevDrag : MonoBehaviour, IDragHandler
{
    public RectTransform panel;
    public void OnDrag(PointerEventData e)
    {
        if (panel != null) panel.position += (Vector3)e.delta;
    }
}

/// <summary>Small extension so the action-row buttons can be re-anchored to the panel's top-left
/// after the generic centered Button() builder creates them.</summary>
static class DevPanelRectExt
{
    public static void SetTopLeft(this GameObject go, RectTransform _, Vector2 pos)
    {
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
    }
}
#endif
