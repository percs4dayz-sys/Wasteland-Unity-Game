using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The "where do I go next" layer. One objective at a time, shown as
///   • a line at the top of the screen — "OBJECTIVE  Talk to Roxy · 31 m",
///   • a bobbing arrow over the target while it's on screen,
///   • an arrow on the screen edge pointing the way while it isn't,
///   • a marker on the minimap (MinimapHUD reads Target), pinned to the rim however far away it is.
/// Anything can drive it: QuestGuide.Show("Talk to Roxy", roxy.transform) / QuestGuide.Clear(). The target
/// may be null for objectives with no place (the arrows hide, the line stays). Banner() puts up a big
/// fading title for moments like arriving in the world. Self-building and auto-spawned.
/// </summary>
public class QuestGuide : MonoBehaviour
{
    public static QuestGuide Instance { get; private set; }

    /// <summary>What the current objective points at (null = no place, or no objective).</summary>
    public static Transform Target { get; private set; }
    /// <summary>The current objective's text (null = no objective).</summary>
    public static string Objective { get; private set; }

    public static void Show(string objective, Transform target) { Objective = objective; Target = target; }
    public static void Clear() { Objective = null; Target = null; }

    /// <summary>Big centred title + subtitle that fades in, holds, and fades out.</summary>
    public static void Banner(string title, string subtitle, float seconds = 4.5f)
    {
        if (Instance != null) Instance.ShowBanner(title, subtitle, seconds);
    }

    static readonly Color Amber = new Color(1f, 0.78f, 0.25f, 1f);
    const float EdgeInset = 70f;   // screen px kept between the edge arrow and the screen border

    RectTransform _panel, _marker, _edge;
    TextMeshProUGUI _text;
    CanvasGroup _banner;
    TextMeshProUGUI _bannerTitle, _bannerSub;
    Canvas _canvas;
    Coroutine _bannerRoutine;
    Transform _measured;
    float _headHeight, _nextMeasure;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("QuestGuide (auto)");
        go.AddComponent<QuestGuide>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    void LateUpdate()
    {
        var player = PlayerEntity.Instance;
        var cam = Camera.main;
        bool show = Objective != null && player != null && cam != null;
        if (_panel.gameObject.activeSelf != show) _panel.gameObject.SetActive(show);
        bool pointAt = show && Target != null;
        if (!pointAt)
        {
            if (_marker.gameObject.activeSelf) _marker.gameObject.SetActive(false);
            if (_edge.gameObject.activeSelf) _edge.gameObject.SetActive(false);
            if (show) _text.text = Objective;
            return;
        }

        Vector3 to = Target.position - player.transform.position; to.y = 0f;
        _text.text = $"{Objective}  <color=#B8A98A>·  {Mathf.RoundToInt(to.magnitude)} m</color>";

        Vector3 head = Target.position + Vector3.up * HeadHeight(Target);
        Vector3 sp = cam.WorldToScreenPoint(head);
        float inset = EdgeInset * _canvas.scaleFactor / 1.5f;
        bool onScreen = sp.z > 0f && sp.x > inset && sp.x < Screen.width - inset && sp.y > inset && sp.y < Screen.height - inset;
        _marker.gameObject.SetActive(onScreen);
        _edge.gameObject.SetActive(!onScreen);

        float bob = Mathf.Sin(Time.unscaledTime * 4f) * 8f * _canvas.scaleFactor;
        if (onScreen)
        {
            _marker.position = new Vector3(sp.x, sp.y + 26f * _canvas.scaleFactor + bob, 0f);
            return;
        }

        // Off screen: an arrow on the border, on the line from the screen centre toward the target.
        var centre = new Vector2(Screen.width, Screen.height) * 0.5f;
        Vector2 dir = (Vector2)sp - centre;
        if (sp.z < 0f) dir = -dir;                                  // behind the camera: the projection is mirrored
        if (dir.sqrMagnitude < 1f) dir = Vector2.down;
        dir.Normalize();
        float halfW = centre.x - inset, halfH = centre.y - inset;
        float k = Mathf.Min(halfW / Mathf.Max(Mathf.Abs(dir.x), 1e-4f), halfH / Mathf.Max(Mathf.Abs(dir.y), 1e-4f));
        _edge.position = centre + dir * (k - Mathf.Abs(bob));
        _edge.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f);   // the sprite points down
    }

    /// <summary>How far above its pivot the target's top is — measured from its renderers, re-checked now and
    /// then (models load in and change), so the arrow sits over the head of any size of thing.</summary>
    float HeadHeight(Transform t)
    {
        if (t == _measured && Time.unscaledTime < _nextMeasure) return _headHeight;
        _measured = t; _nextMeasure = Time.unscaledTime + 2f;
        float top = float.MinValue;
        foreach (var r in t.GetComponentsInChildren<Renderer>())
            if (r.enabled && !(r is ParticleSystemRenderer)) top = Mathf.Max(top, r.bounds.max.y);
        _headHeight = top > float.MinValue ? Mathf.Clamp(top - t.position.y, 0.5f, 12f) + 0.3f : 2.2f;
        return _headHeight;
    }

    void ShowBanner(string title, string subtitle, float seconds)
    {
        _bannerTitle.text = title;
        _bannerSub.text = subtitle;
        if (_bannerRoutine != null) StopCoroutine(_bannerRoutine);
        _bannerRoutine = StartCoroutine(BannerRoutine(seconds));
    }

    IEnumerator BannerRoutine(float seconds)
    {
        _banner.gameObject.SetActive(true);
        for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime) { _banner.alpha = t / 0.6f; yield return null; }
        _banner.alpha = 1f;
        yield return new WaitForSecondsRealtime(seconds);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime) { _banner.alpha = 1f - t; yield return null; }
        _banner.gameObject.SetActive(false);
        _bannerRoutine = null;
    }

    // ── build ────────────────────────────────────────────────────────────
    void BuildUI()
    {
        var canvasGo = new GameObject("QuestGuideCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 450;   // over the HUD, under every menu (520+) and the right-click menu (700)
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Objective line, top centre (the player frame is top-left, the minimap top-right).
        _panel = Rect("Objective", canvasGo.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        _panel.pivot = new Vector2(0.5f, 1f);
        _panel.anchoredPosition = new Vector2(0f, -46f);   // clear of the DEV tab that sits top-centre in dev builds
        _panel.sizeDelta = new Vector2(600f, 62f);
        var bg = _panel.gameObject.AddComponent<Image>();
        UITheme.Panel(bg, 0.9f);
        bg.raycastTarget = false;

        var head = Rect("Head", _panel, new Vector2(0f, 1f), new Vector2(1f, 1f));
        head.pivot = new Vector2(0.5f, 1f);
        head.sizeDelta = new Vector2(0f, 24f);
        head.anchoredPosition = new Vector2(0f, -4f);
        var headTxt = head.gameObject.AddComponent<TextMeshProUGUI>();
        UITheme.Label(headTxt, 15f, Amber);
        headTxt.text = "Objective";
        headTxt.alignment = TextAlignmentOptions.Center;

        var line = Rect("Text", _panel, new Vector2(0f, 0f), new Vector2(1f, 0f));
        line.pivot = new Vector2(0.5f, 0f);
        line.sizeDelta = new Vector2(-24f, 32f);
        line.anchoredPosition = new Vector2(0f, 5f);
        _text = line.gameObject.AddComponent<TextMeshProUGUI>();
        UITheme.Label(_text, 24f, UITheme.Text, upper: false);
        _text.alignment = TextAlignmentOptions.Center;
        _text.textWrappingMode = TextWrappingModes.NoWrap;
        // Not Ellipsis: the heading font's tall line box doesn't fit the line exactly, and an ellipsised
        // line that doesn't fit is hidden outright — the objective showed as a blank bar.
        _text.overflowMode = TextOverflowModes.Overflow;
        _text.raycastTarget = false;
        _panel.gameObject.SetActive(false);

        var arrow = ArrowSprite();
        _marker = Arrow("Marker", canvasGo.transform, arrow, 46f);
        _edge = Arrow("EdgeArrow", canvasGo.transform, arrow, 54f);

        // Welcome / milestone banner, centred a little above the middle.
        var bannerRt = Rect("Banner", canvasGo.transform, new Vector2(0f, 0.62f), new Vector2(1f, 0.62f));
        bannerRt.sizeDelta = new Vector2(0f, 150f);
        _banner = bannerRt.gameObject.AddComponent<CanvasGroup>();
        _banner.blocksRaycasts = false; _banner.interactable = false;
        var titleRt = Rect("Title", bannerRt, new Vector2(0f, 0.45f), new Vector2(1f, 1f));
        _bannerTitle = titleRt.gameObject.AddComponent<TextMeshProUGUI>();
        UITheme.Label(_bannerTitle, 52f, Amber, shadow: true);
        _bannerTitle.alignment = TextAlignmentOptions.Center;
        var subRt = Rect("Sub", bannerRt, new Vector2(0f, 0f), new Vector2(1f, 0.45f));
        _bannerSub = subRt.gameObject.AddComponent<TextMeshProUGUI>();
        if (UITheme.BodyFont != null) _bannerSub.font = UITheme.BodyFont;
        var subShadow = UITheme.ShadowMaterial(_bannerSub.font);
        if (subShadow != null) _bannerSub.fontSharedMaterial = subShadow;
        _bannerSub.fontSize = 30f; _bannerSub.color = UITheme.Text;
        _bannerSub.alignment = TextAlignmentOptions.Center;
        _bannerSub.raycastTarget = false;
        _banner.gameObject.SetActive(false);
    }

    static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    static RectTransform Arrow(string name, Transform parent, Sprite sprite, float size)
    {
        var rt = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        rt.sizeDelta = new Vector2(size, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite; img.color = Amber; img.raycastTarget = false;
        rt.gameObject.SetActive(false);
        return rt;
    }

    /// <summary>A downward arrowhead: white fill (tinted by the Image) inside a dark outline, so it reads over
    /// sky, sand and snow alike. Drawn once, 4× supersampled for smooth edges.</summary>
    static Sprite ArrowSprite()
    {
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Vector2 a = new Vector2(6f, 58f), b = new Vector2(58f, 58f), c = new Vector2(32f, 8f);   // outer
        Vector2 ia = new Vector2(15f, 53f), ib = new Vector2(49f, 53f), ic = new Vector2(32f, 19f); // inner fill
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float outer = 0f, inner = 0f;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                    {
                        var p = new Vector2(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f);
                        if (Inside(p, a, b, c)) outer++;
                        if (Inside(p, ia, ib, ic)) inner++;
                    }
                outer /= 16f; inner /= 16f;
                var fill = Color.Lerp(new Color(0.08f, 0.05f, 0.02f, outer), Color.white, inner);
                fill.a = Mathf.Max(outer, inner);
                px[y * N + x] = fill;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }

    static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
        float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
        float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }
}
