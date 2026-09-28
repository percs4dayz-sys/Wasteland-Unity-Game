using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Applies UITheme to every code-built panel WITHOUT editing each panel. They were all built from the
/// same palette (panel 0.13/0.12/0.10, title bar 0.20/0.17/0.12, …), so each Image is recognised by its
/// colour and given its themed shape:
///   • panels → framed panel sprite + drop shadow      • title bars → header sprite, titles → display font
///   • slots / rows / buttons / tracks inside a panel → rounded bevelled tile (keeping their live colours)
///   • Filled bars with no sprite → shaded fill (fixes bars that always drew full)
/// It also puts every HUD canvas on the same scaling rules + the player's Interface Size.
///
/// Runs on a light interval so lazily-built panels get themed as they appear; each object is judged
/// once. Delete this script and the panels simply fall back to their original flat colours.
/// Self-bootstrapping — no scene wiring. (Replaces the old SyntyUISkin.)
/// </summary>
public class UIThemeSkin : MonoBehaviour
{
    // The palette the older panels were built with (see InventoryPanelUI / SkillsPanelUI / BankUI …).
    // Matched tightly: the slot colour (0.18/0.17/0.14) sits only 0.02 from the title-bar colour, and a
    // loose match is exactly how the old skin put title-bar art on the tab buttons.
    static readonly Color C_Panel   = new Color(0.13f, 0.12f, 0.10f);
    static readonly Color C_Card    = new Color(0.10f, 0.10f, 0.09f);   // small HUD cards (pet, ammo)
    static readonly Color C_Title   = new Color(0.20f, 0.17f, 0.12f);
    static readonly Color C_OldGold = new Color(1f, 0.85f, 0.45f);

    static bool Near(Color a, Color b, float t = 0.012f) =>
        Mathf.Abs(a.r - b.r) < t && Mathf.Abs(a.g - b.g) < t && Mathf.Abs(a.b - b.b) < t;

    readonly HashSet<Object> _seen = new();
    readonly Dictionary<CanvasScaler, Vector2> _design = new();
    readonly List<Image> _imgs = new();
    readonly List<TextMeshProUGUI> _txts = new();
    int _scaleVersion = -1;
    float _next, _nextPrune;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<UIThemeSkin>() != null) return;
        var go = new GameObject("UIThemeSkin (auto)");
        DontDestroyOnLoad(go);
        go.AddComponent<UIThemeSkin>();
    }

    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.2f;

        bool rescale = _scaleVersion != UITheme.ScaleVersion;
        _scaleVersion = UITheme.ScaleVersion;

        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
        {
            if (!canvas.isRootCanvas) continue;
            ScaleCanvas(canvas, rescale);

            canvas.GetComponentsInChildren(true, _imgs);
            foreach (var img in _imgs) SkinImage(img);   // images first: title detection looks at the themed parent

            canvas.GetComponentsInChildren(true, _txts);
            foreach (var t in _txts) SkinText(t);
        }

        if (Time.unscaledTime > _nextPrune)   // forget destroyed objects (XP drops, rebuilt rows …)
        {
            _nextPrune = Time.unscaledTime + 30f;
            _seen.RemoveWhere(o => o == null);
        }
    }

    // ── canvases ────────────────────────────────────────────────────────
    void ScaleCanvas(Canvas canvas, bool rescale)
    {
        var s = canvas.GetComponent<CanvasScaler>();
        if (s == null) return;
        if (!_design.TryGetValue(s, out var design))
        {
            // Only the HUD family (scale-with-screen, 1920×1080 design); anything custom is left alone.
            // (A canvas that already called UITheme.ApplyScaler has the design ÷ Interface Size.)
            if (s.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return;
            if (!IsDesign(s.referenceResolution) && !IsDesign(s.referenceResolution * UITheme.InterfaceScale)) return;
            design = new Vector2(1920f, 1080f);
            _design[s] = design;
        }
        else if (!rescale) return;
        UITheme.ApplyScaler(s, design);
    }

    static bool IsDesign(Vector2 r) => Mathf.Abs(r.x - 1920f) < 1f && Mathf.Abs(r.y - 1080f) < 1f;

    // ── images ──────────────────────────────────────────────────────────
    void SkinImage(Image img)
    {
        if (!_seen.Add(img)) return;
        if (img.sprite != null) return;                   // already has art: icons, themed, Synty …

        if (img.type == Image.Type.Filled)                // progress bar fill
        {
            UITheme.Fill(img);
            StyleTrack(img.transform.parent);
            return;
        }

        Color c = img.color;
        if (c.a < 0.05f) return;                          // invisible click-catchers

        if (Near(c, C_Panel) && c.a > 0.9f) { UITheme.Panel(img); UITheme.DropShadow(img); return; }
        if (Near(c, C_Card) && c.a > 0.5f)  { UITheme.Panel(img, c.a); return; }
        if (Near(c, C_Title))               { UITheme.Header(img); return; }

        if (InsideThemedPanel(img) && TileShaped(img)) UITheme.Tile(img);
    }

    // The dark track behind a bar fill gets the rounded tile too (keeps its colour).
    static void StyleTrack(Transform parent)
    {
        if (parent == null) return;
        var track = parent.GetComponent<Image>();
        if (track == null || track.sprite != null || track.color.a < 0.05f) return;
        if (Near(track.color, C_Panel) || Near(track.color, C_Card) || Near(track.color, C_Title)) return;
        UITheme.Tile(track);
    }

    // Anything that sits inside a themed panel: slots, rows, cells, buttons, fields.
    static bool InsideThemedPanel(Image img)
    {
        for (var t = img.transform.parent; t != null; t = t.parent)
        {
            var pi = t.GetComponent<Image>();
            if (pi == null) continue;
            if (pi.sprite == UITheme.PanelSprite) return true;
            if (pi.sprite == null && pi.color.a > 0.5f && (Near(pi.color, C_Panel) || Near(pi.color, C_Card))) return true;
        }
        return false;
    }

    static bool TileShaped(Image img)
    {
        if (img.preserveAspect) return false;                               // item/skill icon holders
        if (img.name.IndexOf("Icon", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
        var r = img.rectTransform.rect;
        return r.width >= 10f && r.height >= 6f;                            // skip hairline dividers
    }

    // ── text ────────────────────────────────────────────────────────────
    void SkinText(TextMeshProUGUI t)
    {
        if (!_seen.Add(t)) return;
        var parent = t.transform.parent;
        var pImg = parent != null ? parent.GetComponent<Image>() : null;
        if (pImg == null) return;

        if (pImg.sprite == UITheme.HeaderSprite)
        {
            UITheme.Title(t);                                   // panel titles
        }
        else if (pImg.sprite == UITheme.TileSprite && parent.GetComponent<Button>() != null && t.fontSize >= 14f)
        {
            // Button captions: display font, same size; the old gold becomes the theme amber. Small
            // secondary lines on a button (descriptions, "Trains: …") keep the plain body font.
            t.font = UITheme.HeadFont;
            if (Near(t.color, C_OldGold, 0.02f)) t.color = UITheme.Amber;
        }
    }
}
