using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The game's one shared UI look — "wasteland field kit": dark gunmetal panels with a bronze rim,
/// amber title bars and accents, rounded bevelled tiles for slots/buttons, and readable text.
///
/// Every sprite here is DRAWN IN CODE the first time it's asked for (rounded 9-slice textures), so there
/// are no art files to lose or mis-import, and every panel looks like it belongs to the same game.
/// New UI can use the helpers directly (UITheme.Panel(img), UITheme.Tile(img) …); the older code-built
/// panels are themed automatically by UIThemeSkin, which recognises them by the shared palette colours
/// they were all built with.
///
/// Also owns the player's Interface Size setting (Settings ▸ Interface Size): every HUD canvas scales
/// with the screen from a 1920×1080 design, multiplied by that setting.
/// </summary>
public static class UITheme
{
    // ── palette ─────────────────────────────────────────────────────────
    public static readonly Color Amber     = new Color(0.97f, 0.72f, 0.32f, 1f);   // titles / highlights
    public static readonly Color Text      = new Color(0.93f, 0.91f, 0.86f, 1f);
    public static readonly Color TextDim   = new Color(0.64f, 0.62f, 0.58f, 1f);
    public static readonly Color Alert     = new Color(1f, 0.40f, 0.33f, 1f);

    public static readonly Color TileTint  = new Color(0.17f, 0.16f, 0.14f, 1f);    // buttons / rows
    public static readonly Color TrackTint = new Color(0.045f, 0.043f, 0.04f, 0.92f); // empty part of a bar

    // Stat-bar fills.
    public static readonly Color HpFill    = new Color(0.80f, 0.23f, 0.19f, 1f);
    public static readonly Color GoldFill  = new Color(0.88f, 0.62f, 0.22f, 1f);
    public static readonly Color XpFill    = new Color(0.33f, 0.60f, 0.93f, 1f);
    public static readonly Color BeastFill = new Color(0.30f, 0.72f, 0.52f, 1f);
    public static readonly Color TaskFill  = new Color(0.92f, 0.49f, 0.19f, 1f);

    // Panel / title-bar art colours (these sprites are full-colour, drawn untinted).
    static readonly Color Outer      = new Color(0.015f, 0.015f, 0.015f, 0.92f);  // 1-unit dark keyline
    static readonly Color Rim        = new Color(0.31f, 0.25f, 0.17f, 1f);         // bronze rim
    static readonly Color PanelTop   = new Color(0.120f, 0.111f, 0.098f, 1f);
    static readonly Color PanelBot   = new Color(0.074f, 0.070f, 0.064f, 1f);
    static readonly Color HeaderTop  = new Color(0.180f, 0.154f, 0.114f, 1f);
    static readonly Color HeaderBot  = new Color(0.122f, 0.105f, 0.080f, 1f);
    static readonly Color AccentLine = new Color(0.86f, 0.60f, 0.24f, 1f);
    static readonly Color AccentDark = new Color(0.30f, 0.19f, 0.07f, 1f);

    // ── fonts ───────────────────────────────────────────────────────────
    static TMP_FontAsset _head;
    /// <summary>Saira Condensed SemiBold — titles, tabs, bar labels (short, usually upper-case text).</summary>
    public static TMP_FontAsset HeadFont =>
        _head != null ? _head : (_head = Resources.Load<TMP_FontAsset>("UI/Synty/ApocFontBold")) ?? BodyFont;
    /// <summary>Plain, highly legible sans for body text and chat.</summary>
    public static TMP_FontAsset BodyFont => TMP_Settings.defaultFontAsset;

    static readonly Dictionary<TMP_FontAsset, Material> _shadowMats = new();

    /// <summary>A shared copy of the font's material with a soft drop shadow (TMP underlay), for text
    /// drawn straight over the 3D world (HUD labels, chat). Shared per font, so the text still batches.</summary>
    public static Material ShadowMaterial(TMP_FontAsset font)
    {
        if (font == null || font.material == null) return null;
        if (_shadowMats.TryGetValue(font, out var m) && m != null) return m;
        ShaderUtilities.GetShaderPropertyIDs();
        m = new Material(font.material) { name = font.name + " (UITheme shadow)", hideFlags = HideFlags.HideAndDontSave };
        m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        m.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.85f));
        m.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.35f);
        m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.45f);
        m.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.2f);
        m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.5f);
        _shadowMats[font] = m;
        return m;
    }

    // ── one-call styling helpers ────────────────────────────────────────
    /// <summary>Framed panel (bronze rim, dark gradient). Replaces the Image's colour.</summary>
    public static void Panel(Image img, float alpha = 1f)
    {
        img.sprite = PanelSprite; img.type = Image.Type.Sliced; img.fillCenter = true;
        img.pixelsPerUnitMultiplier = 1f;
        img.color = new Color(1f, 1f, 1f, alpha);
    }

    /// <summary>Title bar: rounded top to sit flush on a panel, amber underline. Replaces the colour.</summary>
    public static void Header(Image img)
    {
        img.sprite = HeaderSprite; img.type = Image.Type.Sliced; img.fillCenter = true;
        img.pixelsPerUnitMultiplier = 1f;
        img.color = Color.white;
    }

    /// <summary>Rounded, bevelled tile for slots, rows and buttons. KEEPS the Image's colour (it's a
    /// white-based shape, tinted by whatever colour the panel code sets — including live state changes).</summary>
    public static void Tile(Image img)
    {
        img.sprite = TileSprite; img.type = Image.Type.Sliced; img.fillCenter = true;
        img.pixelsPerUnitMultiplier = 1f;
    }

    /// <summary>Gives a Filled bar image its shading. (A Filled Image with no sprite ignores fillAmount
    /// and always draws full — this is what made every XP / magazine / pet-HP bar look maxed.)</summary>
    public static void Fill(Image img) { img.sprite = FillSprite; }

    /// <summary>Title text: condensed display font, upper-case, tracked out, amber.</summary>
    public static void Title(TMP_Text t)
    {
        t.font = HeadFont;
        t.fontStyle = (t.fontStyle & ~FontStyles.Bold) | FontStyles.UpperCase;
        t.characterSpacing = 5f;
        t.color = Amber;
    }

    /// <summary>Short label (tab, button, bar caption): condensed display font, optional drop shadow.</summary>
    public static void Label(TMP_Text t, float size, Color color, bool shadow = false, bool upper = true)
    {
        t.font = HeadFont;
        if (shadow) { var m = ShadowMaterial(t.font); if (m != null) t.fontSharedMaterial = m; }
        t.fontSize = size;
        t.color = color;
        t.fontStyle = upper ? FontStyles.UpperCase : FontStyles.Normal;
        t.characterSpacing = upper ? 2f : 0f;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
    }

    /// <summary>Where the side panels (inventory, skills, gear, bank …) open: bottom-right, just above the
    /// tab dock, so they never cover the minimap and line up with the tab you clicked.</summary>
    public static void DockSidePanel(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-16f, 88f);   // tab dock = 14 + 66 tall, plus a gap
    }

    /// <summary>Soft drop shadow under a floating panel.</summary>
    public static void DropShadow(Graphic g)
    {
        if (g.GetComponent<Shadow>() != null) return;
        var s = g.gameObject.AddComponent<Shadow>();
        s.effectColor = new Color(0f, 0f, 0f, 0.45f);
        s.effectDistance = new Vector2(3f, -4f);
    }

    // ── interface size (player setting) ─────────────────────────────────
    public const string ScaleKey = "ui_scale";
    public const float MinScale = 0.8f, MaxScale = 1.5f;
    /// <summary>Bumps whenever the player changes Interface Size, so canvases know to re-apply.</summary>
    public static int ScaleVersion { get; private set; }

    public static float InterfaceScale
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(ScaleKey, 1f), MinScale, MaxScale);
        set
        {
            PlayerPrefs.SetFloat(ScaleKey, Mathf.Clamp(value, MinScale, MaxScale));
            ScaleVersion++;
        }
    }

    /// <summary>Uniform scaling for every HUD canvas: designed at 1920×1080, balanced between width and
    /// height (so very wide or tall windows neither blow the UI up nor shrink it to nothing), times the
    /// player's Interface Size.</summary>
    public static void ApplyScaler(CanvasScaler s, Vector2 designResolution)
    {
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        s.matchWidthOrHeight = 0.5f;
        s.referenceResolution = designResolution / InterfaceScale;
    }

    // ── procedural sprites ──────────────────────────────────────────────
    // Drawn at 2 texels per UI unit (sprite PPU 200 against the canvas' 100), so rims stay crisp up to
    // 4K, with mipmaps so they don't shimmer when the interface is scaled down.
    const float TPU = 2f;

    static Sprite _panel, _header, _tile, _fill, _roundFill, _white, _ring;
    public static Sprite PanelSprite     => _panel     != null ? _panel     : (_panel     = MakePanel());
    public static Sprite HeaderSprite    => _header    != null ? _header    : (_header    = MakeHeader());
    public static Sprite TileSprite      => _tile      != null ? _tile      : (_tile      = MakeTile());
    public static Sprite FillSprite      => _fill      != null ? _fill      : (_fill      = MakeFill());
    public static Sprite RoundFillSprite => _roundFill != null ? _roundFill : (_roundFill = MakeRoundFill());
    public static Sprite WhiteSprite     => _white     != null ? _white     : (_white     = MakeWhite());
    /// <summary>Round bronze frame for the minimap (drawn at its 200-unit size, clear inside).</summary>
    public static Sprite RingSprite      => _ring      != null ? _ring      : (_ring      = MakeRing());

    /// <summary>True for any sprite this class drew (so the skin can tell themed images apart).</summary>
    public static bool IsThemeSprite(Sprite s) =>
        s != null && (s == _panel || s == _header || s == _tile || s == _fill || s == _roundFill || s == _white || s == _ring);

    // Signed distance (UI units) from p to the box [min,max] with corner radius rTop on the upper
    // corners and rBot on the lower ones. Negative inside.
    static float SdBox(Vector2 p, Vector2 min, Vector2 max, float rTop, float rBot)
    {
        Vector2 c = (min + max) * 0.5f, h = (max - min) * 0.5f;
        float r = p.y > c.y ? rTop : rBot;
        float qx = Mathf.Abs(p.x - c.x) - h.x + r, qy = Mathf.Abs(p.y - c.y) - h.y + r;
        float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        return Mathf.Min(Mathf.Max(qx, qy), 0f) + outside - r;
    }

    // Composites the standard edge onto a fill: a 1-unit keyline, a 1-unit rim, then the fill.
    // Anti-aliased at every boundary (distances converted to texels for the blend).
    static Color Edge(float d, Color fill, Color rim, Color outer)
    {
        float cover  = Mathf.Clamp01(0.5f - d * TPU);
        float toRim  = Mathf.Clamp01(0.5f - (d + 1f) * TPU);
        float toFill = Mathf.Clamp01(0.5f - (d + 2f) * TPU);
        Color c = Color.Lerp(Color.Lerp(outer, rim, toRim), fill, toFill);
        c.a *= cover;
        return c;
    }

    static Sprite Build(string name, float wU, float hU, Vector4 sliceU, System.Func<Vector2, Color> shade)
    {
        int w = Mathf.RoundToInt(wU * TPU), h = Mathf.RoundToInt(hU * TPU);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true)
        {
            name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = shade(new Vector2((x + 0.5f) / TPU, (y + 0.5f) / TPU));   // texel centre, in units
        tex.SetPixels32(px);
        tex.Apply(true, true);
        var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f * TPU, 0,
                              SpriteMeshType.FullRect, sliceU * TPU);
        s.name = name;
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    static Sprite MakePanel() => Build("UITheme.Panel", 24, 24, new Vector4(10, 10, 10, 10), p =>
    {
        float d = SdBox(p, Vector2.zero, new Vector2(24, 24), 7f, 7f);
        Color fill = Color.Lerp(PanelBot, PanelTop, p.y / 24f);
        return Edge(d, fill, Rim, Outer);
    });

    // Title bar: the box runs off the bottom of the texture so only the TOP corners round (matching the
    // panel it sits on); the bottom 2 units are the amber underline shared by every title bar.
    static Sprite MakeHeader() => Build("UITheme.Header", 24, 20, new Vector4(10, 2, 10, 10), p =>
    {
        float d = SdBox(p, new Vector2(0, -20), new Vector2(24, 20), 7f, 0f);
        Color fill = p.y < 1f ? AccentDark
                   : p.y < 2f ? AccentLine
                   : Color.Lerp(HeaderBot, HeaderTop, Mathf.InverseLerp(2f, 20f, p.y));
        return Edge(d, fill, Rim, Outer);
    });

    // White-based (tinted by the Image colour): dark keyline, bevel rim lit from above, soft gradient.
    static Sprite MakeTile() => Build("UITheme.Tile", 16, 16, new Vector4(6, 6, 6, 6), p =>
    {
        float d = SdBox(p, Vector2.zero, new Vector2(16, 16), 4f, 4f);
        float t = p.y / 16f;
        float g = Mathf.Lerp(0.80f, 0.93f, t);
        float r = Mathf.Lerp(0.60f, 1.00f, t);
        return Edge(d, new Color(g, g, g, 1f), new Color(r, r, r, 1f), new Color(0.28f, 0.28f, 0.28f, 1f));
    });

    // White-based bar fill with a glossy top; square ends (Filled images stretch the whole sprite).
    static Sprite MakeFill() => Build("UITheme.Fill", 4, 8, Vector4.zero, p =>
    {
        float t = p.y / 8f;
        float g = t > 0.85f ? 1f : Mathf.Lerp(0.70f, 0.94f, t);
        return new Color(g, g, g, 1f);
    });

    // Same shading with rounded ends, 9-sliced — for bars whose width is driven by anchors.
    static Sprite MakeRoundFill() => Build("UITheme.RoundFill", 12, 12, new Vector4(4, 4, 4, 4), p =>
    {
        float d = SdBox(p, Vector2.zero, new Vector2(12, 12), 3f, 3f);
        float t = p.y / 12f;
        float g = t > 0.85f ? 1f : Mathf.Lerp(0.70f, 0.94f, t);
        return new Color(g, g, g, Mathf.Clamp01(0.5f - d * TPU));
    });

    static Sprite MakeWhite() => Build("UITheme.White", 4, 4, Vector4.zero, _ => Color.white);

    // From the outside in: 1-unit keyline, 4-unit bronze rim (lit from above), 1-unit keyline, then clear
    // — the band covers the 6-unit gap between the minimap's frame and its masked map.
    static Sprite MakeRing() => Build("UITheme.Ring", 200, 200, Vector4.zero, p =>
    {
        float r = Vector2.Distance(p, new Vector2(100f, 100f));
        float d = r - 100f;                                          // neg inside the outer edge
        float cover  = Mathf.Clamp01(0.5f - d * TPU);
        float band   = Mathf.Clamp01(0.5f - (94f - r) * TPU);       // 0 inside radius 94
        float toRim  = Mathf.Clamp01(0.5f - (d + 1f) * TPU);
        float toKey  = Mathf.Clamp01(0.5f - (d + 5f) * TPU);
        Color rim = Color.Lerp(Rim, new Color(0.47f, 0.38f, 0.25f, 1f), p.y / 200f);
        Color c = Color.Lerp(Color.Lerp(Outer, rim, toRim), Outer, toKey);
        c.a *= cover * band;
        return c;
    });
}
