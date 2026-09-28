using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using TMPro;

/// <summary>
/// Self-building circular minimap for the 3D world (top-right). A second orthographic camera looks
/// straight down and renders into a RenderTexture, clipped to a circle. Like OSRS, "up" on the map is
/// the way the camera is looking (it turns only when you turn the view, never with your character), the
/// centre arrow shows which way you face, and an N/E/S/W ring marks true north.
///
/// OSRS controls: tap / click the map to walk there (a red flag marks where you're heading); tap the
/// compass at its top-left to face north, hold / right-click it for Look North / East / South / West.
///
/// Markers (OSRS colours): red dots = items on the ground (ones just off the map sit on the rim so
/// you can see which way to go), yellow dots = NPCs and creatures, small squares = gathering spots
/// coloured by skill (green woodcutting, orange scrapping/mining, blue fishing), a pulsing amber dot =
/// the current objective (QuestGuide). 3D-only; auto-spawned, zero setup.
/// </summary>
public class MinimapHUD : MonoBehaviour
{
    public static MinimapHUD Instance { get; private set; }

    public float height    = 45f;   // how high the map camera floats above the player
    public float orthoSize = 24f;   // half the visible area (bigger = more zoomed out)
    public bool  rotateWithPlayer = true;   // true = view-up, like OSRS (turns with the camera); false = north-up

    Camera        _cam;
    RenderTexture _rt;
    GameObject    _panel;
    Transform     _target;
    RectTransform _arrowRt;
    RectTransform _compass;   // holds the N/E/S/W markers so they can rotate as a set
    RectTransform _markerLayer;
    readonly System.Collections.Generic.List<Image> _dots = new();
    ResourceNode[] _nodes = new ResourceNode[0];
    Enemy3D[] _creatures = new Enemy3D[0];
    readonly System.Collections.Generic.List<Transform> _npcs = new();
    float _nextScan;
    RectTransform _needle;                 // the compass button's north needle
    Vector3 _mapCentre; float _mapYaw;     // last frame's map framing, for turning a tap into a world point
    static Sprite _flagSprite;

    // Fixed compass markers (north-up map → static screen positions). bearing: N=0,E=90,S=180,W=270.
    static readonly (string label, float bearing)[] _dirs = { ("N", 0f), ("E", 90f), ("S", 180f), ("W", 270f) };
    const float CompassRadius = 84f;

    static readonly Color ItemRed = new(1f, 0.18f, 0.15f), NpcYellow = new(1f, 0.92f, 0.25f), ObjectiveAmber = new(1f, 0.62f, 0.1f);
    static Sprite _circleSprite, _arrowSprite, _dotSprite, _squareSprite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("MinimapHUD (auto)").AddComponent<MinimapHUD>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Build();
        RenderPipelineManager.beginCameraRendering += CoarseTerrainForMap;
        RenderPipelineManager.endCameraRendering += RestoreTerrain;
    }

    void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= CoarseTerrainForMap;
        RenderPipelineManager.endCameraRendering -= RestoreTerrain;
    }

    // Seen straight down by an orthographic camera, terrain picks its finest detail everywhere — about
    // 4M triangles for a 48 m map. The map is 188 px wide; coarse terrain looks identical at that size.
    readonly System.Collections.Generic.List<(Terrain t, float err)> _terrainErr = new();

    void CoarseTerrainForMap(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam != _cam) return;
        _terrainErr.Clear();
        foreach (var t in Terrain.activeTerrains)
        {
            _terrainErr.Add((t, t.heightmapPixelError));
            t.heightmapPixelError = 60f;
        }
    }

    void RestoreTerrain(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam != _cam) return;
        foreach (var (t, err) in _terrainErr) if (t != null) t.heightmapPixelError = err;
        _terrainErr.Clear();
    }

    void Build()
    {
        _rt = new RenderTexture(256, 256, 16) { name = "MinimapRT" };
        _rt.Create();

        var camGo = new GameObject("MinimapCamera");
        camGo.transform.SetParent(transform, false);
        _cam = camGo.AddComponent<Camera>();
        _cam.orthographic      = true;
        _cam.orthographicSize  = orthoSize;
        _cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // straight down, north-up (fixed)
        _cam.clearFlags        = CameraClearFlags.SolidColor;
        _cam.backgroundColor   = new Color(0.10f, 0.10f, 0.12f);
        _cam.targetTexture     = _rt;
        _cam.depth             = -10;
        _cam.allowMSAA         = false;
        _cam.allowHDR          = false;
        // Keep the map cheap. With the defaults this 48 m top-down view cost more than the main view
        // (~8.5M triangles and ~340 draw calls a frame): its own shadow pass, post-processing, and a
        // 1 km-deep view column. A map needs none of that: no shadows or effects, and only a slab
        // around the player's height.
        _cam.nearClipPlane     = 1f;
        _cam.farClipPlane      = height + 60f;
        var urpCam = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        urpCam.renderShadows = false;
        urpCam.renderPostProcessing = false;
        urpCam.requiresDepthTexture = false;
        urpCam.requiresColorTexture = false;
        urpCam.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;

        var canvasGo = new GameObject("MinimapCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 450;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        const float SIZE = 200f;
        var panelRt = NewRT("MinimapPanel", canvasGo.transform);
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(1f, 1f);   // top-right
        panelRt.pivot = new Vector2(1f, 1f);
        panelRt.anchoredPosition = new Vector2(-16f, -16f);
        panelRt.sizeDelta = new Vector2(SIZE, SIZE);
        _panel = panelRt.gameObject;

        var circle = CircleSprite();

        // Dark circular frame / border behind the map.
        var frameRt = NewRT("Frame", panelRt);
        frameRt.anchorMin = Vector2.zero; frameRt.anchorMax = Vector2.one;
        frameRt.offsetMin = Vector2.zero; frameRt.offsetMax = Vector2.zero;
        var frameImg = frameRt.gameObject.AddComponent<Image>();
        frameImg.sprite = circle; frameImg.type = Image.Type.Simple;
        frameImg.color = new Color(0f, 0f, 0f, 0.7f);
        frameImg.raycastTarget = false;

        // Circular mask: a white circle used purely as a stencil; the map (child) is clipped to it.
        var maskRt = NewRT("Mask", panelRt);
        maskRt.anchorMin = Vector2.zero; maskRt.anchorMax = Vector2.one;
        maskRt.offsetMin = new Vector2(6, 6); maskRt.offsetMax = new Vector2(-6, -6);
        var maskImg = maskRt.gameObject.AddComponent<Image>();
        maskImg.sprite = circle; maskImg.type = Image.Type.Simple; maskImg.raycastTarget = false;
        var mask = maskRt.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        var mapRt = NewRT("Map", maskRt);
        mapRt.anchorMin = Vector2.zero; mapRt.anchorMax = Vector2.one;
        mapRt.offsetMin = Vector2.zero; mapRt.offsetMax = Vector2.zero;
        var raw = mapRt.gameObject.AddComponent<RawImage>();
        raw.texture = _rt;
        raw.raycastTarget = true;                                  // tap / click the map to walk there
        mapRt.gameObject.AddComponent<Tap>().onClick = OnMapClick;

        // Dots ride on top of the map image, inside the circular mask so they clip with it.
        _markerLayer = NewRT("Markers", maskRt);
        _markerLayer.anchorMin = Vector2.zero; _markerLayer.anchorMax = Vector2.one;
        _markerLayer.offsetMin = Vector2.zero; _markerLayer.offsetMax = Vector2.zero;

        // Bronze rim over the map's edge, matching the panel frames (UITheme).
        var rimRt = NewRT("Rim", panelRt);
        rimRt.anchorMin = Vector2.zero; rimRt.anchorMax = Vector2.one;
        rimRt.offsetMin = Vector2.zero; rimRt.offsetMax = Vector2.zero;
        var rim = rimRt.gameObject.AddComponent<Image>();
        rim.sprite = UITheme.RingSprite; rim.type = Image.Type.Simple;
        rim.raycastTarget = false;

        // N/E/S/W markers around the rim, held in a centred container so the whole ring can rotate
        // as a set when the map is player-up (keeping each letter pointing at its true direction).
        _compass = NewRT("Compass", panelRt);
        _compass.anchorMin = _compass.anchorMax = new Vector2(0.5f, 0.5f);
        _compass.sizeDelta = Vector2.zero;
        for (int i = 0; i < _dirs.Length; i++)
        {
            var dRt = NewRT("Dir_" + _dirs[i].label, _compass);
            dRt.anchorMin = dRt.anchorMax = new Vector2(0.5f, 0.5f);
            dRt.sizeDelta = new Vector2(20f, 20f);
            float a = _dirs[i].bearing * Mathf.Deg2Rad;        // clockwise from "up"
            dRt.anchoredPosition = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * CompassRadius;
            var t = dRt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = _dirs[i].label; t.fontSize = 15; t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
            t.color = _dirs[i].label == "N" ? new Color(1f, 0.4f, 0.35f) : new Color(0.95f, 0.95f, 0.95f);
            t.outlineWidth = 0.2f; t.outlineColor = new Color32(0, 0, 0, 200);
        }

        BuildCompassButton(panelRt);

        // "You" arrow in the centre — rotates to your heading (the map itself stays put).
        _arrowRt = NewRT("PlayerArrow", panelRt);
        _arrowRt.anchorMin = _arrowRt.anchorMax = new Vector2(0.5f, 0.5f);
        _arrowRt.sizeDelta = new Vector2(18f, 18f);
        var arrow = _arrowRt.gameObject.AddComponent<Image>();
        arrow.sprite = ArrowSprite(); arrow.type = Image.Type.Simple;
        arrow.color = new Color(1f, 0.85f, 0.3f);
        arrow.raycastTarget = false;

        _panel.SetActive(false);
    }

    void LateUpdate()
    {
        bool show = GameMode.Is3D;
        if (_panel.activeSelf != show) _panel.SetActive(show);
        if (!show) { _cam.enabled = false; return; }

        if (_target == null && PlayerEntity.Instance != null)
            _target = PlayerEntity.Instance.transform;
        if (_target == null) return;

        Vector3 p = _target.position;
        float heading = _target.eulerAngles.y;
        // OSRS: the map turns with the camera (up = the way you're looking), never with your character.
        var mainCam = Camera.main;
        float view = mainCam != null ? mainCam.transform.eulerAngles.y : heading;

        // Follow the player from directly above.
        _cam.orthographicSize = orthoSize;
        _cam.transform.position = new Vector3(p.x, p.y + height, p.z);
        // View-up: yaw the top-down camera with the main camera (set rotateWithPlayer = false for a fixed
        // north-up map instead).
        _cam.transform.rotation = Quaternion.Euler(90f, rotateWithPlayer ? view : 0f, 0f);

        if (rotateWithPlayer)
        {
            // The arrow shows which way you face within the view; the N/E/S/W ring keeps pointing true-world.
            if (_arrowRt != null) _arrowRt.localRotation = Quaternion.Euler(0f, 0f, view - heading);
            if (_compass != null) _compass.localRotation = Quaternion.Euler(0f, 0f, view);
        }
        else
        {
            // North-up: ring fixed, arrow turns to show heading.
            if (_arrowRt != null) _arrowRt.localRotation = Quaternion.Euler(0f, 0f, -heading);
            if (_compass != null) _compass.localRotation = Quaternion.identity;
        }
        if (_needle != null) _needle.localRotation = Quaternion.Euler(0f, 0f, rotateWithPlayer ? view : 0f);

        // The actual fix: URP does NOT reliably auto-render an off-screen camera, so the map texture
        // stayed empty (looked like a frozen PNG). Drive it with an explicit per-frame render request
        // into MinimapRT instead of relying on the camera's automatic render.
        _cam.enabled = false;
        // The map doesn't need a fresh picture every frame: every 2nd frame on desktop, every 4th on
        // phones (the dots and arrow below still move every frame).
        if (Time.frameCount % (Application.isMobilePlatform ? 4 : 2) == 0)
        {
            var req = new RenderPipeline.StandardRequest { destination = _rt };
            if (RenderPipeline.SupportsRenderRequest(_cam, req))
                RenderPipeline.SubmitRenderRequest(_cam, req);
        }

        UpdateMarkers(p, rotateWithPlayer ? view : 0f);
    }

    // ── markers ───────────────────────────────────────────────────────────

    void UpdateMarkers(Vector3 centre, float yaw)
    {
        if (_markerLayer == null) return;
        if (Time.unscaledTime >= _nextScan) Rescan();
        _mapCentre = centre; _mapYaw = yaw;

        float radius = _markerLayer.rect.height * 0.5f;
        float pxPerMetre = radius / Mathf.Max(1f, orthoSize);
        float s = Mathf.Sin(yaw * Mathf.Deg2Rad), c = Mathf.Cos(yaw * Mathf.Deg2Rad);
        int used = 0;

        // Gathering spots first (underneath), then NPCs and creatures, then loot on top.
        foreach (var n in _nodes)
            if (n != null && n.isActiveAndEnabled && !n.IsDepleted)
                Place(ref used, n.transform.position, NodeColour(n.skill), 7f, false, false);
        foreach (var e in _creatures)
            if (e != null && e.isActiveAndEnabled && !e.GetComponent<CombatTarget>().IsDead)
                Place(ref used, e.transform.position, NpcYellow, 6f, true, false);
        foreach (var t in _npcs)
            if (t != null && t.gameObject.activeInHierarchy)
                Place(ref used, t.position, NpcYellow, 6f, true, false);
        foreach (var gi in GroundItem.All)
            if (gi != null) Place(ref used, gi.transform.position, ItemRed, 7f, true, true);
        // The current objective (QuestGuide) on top: bigger, pulsing amber, and on the rim however far it is.
        if (QuestGuide.Target != null)
            Place(ref used, QuestGuide.Target.position, ObjectiveAmber, 11f + Mathf.Sin(Time.unscaledTime * 5f) * 2f, true, true, true);
        // OSRS's red flag: where your walk is headed.
        var mover = _target != null ? _target.GetComponent<Player3DController>() : null;
        if (mover != null && mover.Destination.HasValue)
            Place(ref used, mover.Destination.Value, Color.white, 16f, true, true, true, FlagSprite());

        for (int i = used; i < _dots.Count; i++)
            if (_dots[i].gameObject.activeSelf) _dots[i].gameObject.SetActive(false);

        void Place(ref int index, Vector3 world, Color colour, float size, bool round, bool pinToRim, bool anyDistance = false, Sprite sprite = null)
        {
            Vector3 d = world - centre;
            var px = new Vector2(d.x * c - d.z * s, d.x * s + d.z * c) * pxPerMetre;   // map is player-up
            float r = px.magnitude, max = radius - 5f;
            if (r > max)
            {
                if (!pinToRim || (!anyDistance && r > max * 3f)) return;   // loot up to ~3 map-radii away sits on the rim
                px *= max / r;
            }
            var img = Dot(index++);
            img.sprite = sprite != null ? sprite : round ? DotSprite() : SquareSprite();
            img.color = colour;
            img.rectTransform.sizeDelta = new Vector2(size, size);
            img.rectTransform.anchoredPosition = px;
        }
    }

    // ── OSRS minimap controls ────────────────────────────────────────────

    /// <summary>Tap / click the map: walk to that spot (the red flag shows it).</summary>
    void OnMapClick(UnityEngine.EventSystems.PointerEventData e)
    {
        if (e.button != UnityEngine.EventSystems.PointerEventData.InputButton.Left || TouchInput.SuppressClick) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_markerLayer, e.position, null, out var local)) return;
        float radius = _markerLayer.rect.height * 0.5f;
        if (local.magnitude > radius) return;                 // the image is square; the map is only the circle
        float ppm = radius / Mathf.Max(1f, orthoSize);
        float s = Mathf.Sin(_mapYaw * Mathf.Deg2Rad), c = Mathf.Cos(_mapYaw * Mathf.Deg2Rad);
        // Undo the marker mapping (world offset → rotated map pixels).
        var world = _mapCentre + new Vector3(c * local.x + s * local.y, 0f, -s * local.x + c * local.y) / ppm;
        world.y = GroundY(world);
        var walker = _target != null ? _target.GetComponent<ClickToMove3D>() : null;
        if (walker != null) walker.WalkTo(world);
    }

    /// <summary>Ground height at a point: the terrain under it if there is one (so a tap over a roof or a tree
    /// still means the ground), else whatever a ray from above hits first.</summary>
    float GroundY(Vector3 p)
    {
        foreach (var t in Terrain.activeTerrains)
        {
            var o = t.transform.position; var size = t.terrainData.size;
            if (p.x >= o.x && p.x <= o.x + size.x && p.z >= o.z && p.z <= o.z + size.z) return t.SampleHeight(p) + o.y;
        }
        return Physics.Raycast(p + Vector3.up * 300f, Vector3.down, out var hit, 600f, ~0, QueryTriggerInteraction.Ignore)
            ? hit.point.y : _mapCentre.y;
    }

    /// <summary>OSRS's compass at the map's top-left: its red needle points north. Tap / click to face north;
    /// hold / right-click for Look North / East / South / West.</summary>
    void BuildCompassButton(RectTransform panelRt)
    {
        var rt = NewRT("CompassButton", panelRt);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(14f, -14f);
        rt.sizeDelta = new Vector2(42f, 42f);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = CircleSprite(); bg.color = new Color(0.08f, 0.07f, 0.06f, 0.92f);
        rt.gameObject.AddComponent<Tap>().onClick = OnCompassClick;

        var rimRt = NewRT("Rim", rt);
        rimRt.anchorMin = Vector2.zero; rimRt.anchorMax = Vector2.one;
        rimRt.offsetMin = rimRt.offsetMax = Vector2.zero;
        var rim = rimRt.gameObject.AddComponent<Image>();
        rim.sprite = UITheme.RingSprite; rim.raycastTarget = false;

        _needle = NewRT("Needle", rt);
        _needle.anchorMin = _needle.anchorMax = new Vector2(0.5f, 0.5f);
        _needle.sizeDelta = new Vector2(16f, 30f);
        var needle = _needle.gameObject.AddComponent<Image>();
        needle.sprite = ArrowSprite(); needle.color = new Color(1f, 0.28f, 0.22f);
        needle.raycastTarget = false;
    }

    void OnCompassClick(UnityEngine.EventSystems.PointerEventData e)
    {
        if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right)
        {
            if (ContextMenuUI.Instance == null) return;
            ContextMenuUI.Instance.Open(e.position, "Choose Option", new System.Collections.Generic.List<(string, System.Action)>
            {
                ("Look North", () => Face(0f)), ("Look East", () => Face(90f)),
                ("Look South", () => Face(180f)), ("Look West", () => Face(270f)), ("Cancel", null),
            });
            return;
        }
        if (!TouchInput.SuppressClick) Face(0f);
    }

    static void Face(float bearing)
    {
        var cam = Camera.main != null ? Camera.main.GetComponent<OrbitCamera3D>() : null;
        if (cam == null) cam = FindAnyObjectByType<OrbitCamera3D>();
        if (cam != null) cam.yaw = bearing;
    }

    /// <summary>OSRS's red destination flag: a light pole with a red pennant, outlined dark.</summary>
    static Sprite FlagSprite()
    {
        if (_flagSprite != null) return _flagSprite;
        const int N = 32;
        static bool Flag(int x, int y) => x >= 11 && y >= 16 && y <= 29 && x <= 11 + 15f * (1f - Mathf.Abs(y - 22.5f) / 6.5f);
        static bool Pole(int x, int y) => x >= 8 && x <= 10 && y >= 2 && y <= 29;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                Color col = Color.clear;
                if (Flag(x, y)) col = new Color(0.95f, 0.1f, 0.08f, 1f);
                else if (Pole(x, y)) col = new Color(0.88f, 0.86f, 0.8f, 1f);
                else
                    for (int dy = -1; dy <= 1 && col.a == 0f; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (Flag(x + dx, y + dy) || Pole(x + dx, y + dy)) { col = new Color(0f, 0f, 0f, 0.9f); break; }
                px[y * N + x] = col;
            }
        tex.SetPixels(px);
        tex.Apply();
        _flagSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.3f, 0.06f), 100f);   // pivot at the pole's foot
        return _flagSprite;
    }

    /// <summary>Forwards a UI click (a tap, a click, or a long-press's synthetic right-click) to a handler.</summary>
    class Tap : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
    {
        public System.Action<UnityEngine.EventSystems.PointerEventData> onClick;
        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e) => onClick?.Invoke(e);
    }

    Image Dot(int i)
    {
        while (_dots.Count <= i)
        {
            var rt = NewRT("Dot", _markerLayer);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            _dots.Add(img);
        }
        var dot = _dots[i];
        if (!dot.gameObject.activeSelf) dot.gameObject.SetActive(true);
        return dot;
    }

    /// <summary>The world's gathering spots, creatures and talkable NPCs — refreshed every couple of
    /// seconds rather than every frame (ground items keep their own live list).</summary>
    void Rescan()
    {
        _nextScan = Time.unscaledTime + 2f;
        _nodes = FindObjectsByType<ResourceNode>(FindObjectsSortMode.None);
        _creatures = FindObjectsByType<Enemy3D>(FindObjectsSortMode.None);
        _npcs.Clear();
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (mb is ITalkableNPC) _npcs.Add(mb.transform);
    }

    static Color NodeColour(Skill skill) => skill switch
    {
        Skill.Woodcutting => new Color(0.45f, 0.88f, 0.35f),
        Skill.Scrapping   => new Color(0.98f, 0.62f, 0.25f),
        Skill.Fishing     => new Color(0.35f, 0.80f, 1f),
        _                 => new Color(0.85f, 0.85f, 0.85f),
    };

    /// <summary>White fill with a black rim, so a tinted dot reads on any terrain.</summary>
    static Sprite DotSprite() => _dotSprite != null ? _dotSprite : (_dotSprite = MarkerSprite(true));
    static Sprite SquareSprite() => _squareSprite != null ? _squareSprite : (_squareSprite = MarkerSprite(false));

    static Sprite MarkerSprite(bool round)
    {
        const int S = 16;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var clear = new Color(0f, 0f, 0f, 0f);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                Color px;
                if (round)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S / 2f, S / 2f));
                    px = d > S / 2f - 0.5f ? clear : d > S / 2f - 3f ? Color.black : Color.white;
                }
                else px = (x < 2 || y < 2 || x > S - 3 || y > S - 3) ? Color.black : Color.white;
                tex.SetPixel(x, y, px);
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100);
    }

    // ── runtime sprite generators ─────────────────────────────────────────
    static Sprite CircleSprite()
    {
        if (_circleSprite != null) return _circleSprite;
        const int R = 128;
        var tex = new Texture2D(R, R, TextureFormat.RGBA32, false);
        Vector2 c = new(R / 2f, R / 2f);
        float rad = R / 2f - 1f;
        for (int y = 0; y < R; y++)
            for (int x = 0; x < R; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                float a = Mathf.Clamp01(rad - d);   // ~1px feathered edge
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        _circleSprite = Sprite.Create(tex, new Rect(0, 0, R, R), new Vector2(0.5f, 0.5f), 100);
        return _circleSprite;
    }

    static Sprite ArrowSprite()
    {
        if (_arrowSprite != null) return _arrowSprite;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        Vector2 apex = new(S * 0.5f, S * 0.92f);    // tip (points up)
        Vector2 bl   = new(S * 0.16f, S * 0.10f);   // base-left
        Vector2 br   = new(S * 0.84f, S * 0.10f);   // base-right
        var clear = new Color(0, 0, 0, 0);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var pt = new Vector2(x + 0.5f, y + 0.5f);
                tex.SetPixel(x, y, InTriangle(pt, apex, bl, br) ? Color.white : clear);
            }
        tex.Apply();
        _arrowSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100);
        return _arrowSprite;
    }

    static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Edge(p, a, b), d2 = Edge(p, b, c), d3 = Edge(p, c, a);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0;
        bool pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    static float Edge(Vector2 p, Vector2 a, Vector2 b) =>
        (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);

    static RectTransform NewRT(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }
}
