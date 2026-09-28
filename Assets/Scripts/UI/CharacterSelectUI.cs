using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using TMPro;

/// <summary>
/// Title / character-select screen. SELF-BUILDING and auto-spawned — it creates its own canvas,
/// EventSystem, name field, gender buttons, and Start button at runtime, so the CharacterSelect
/// scene needs no wiring (it only has to exist and be build-index 0).
///
/// Flow: first time, Begin → the character creator → Broken Crescent (fresh run). Once you have a
/// character (a world save plus a saved look), Play goes straight back in with your save, and
/// "New character" (tap twice) starts over — the old run is archived, not deleted.
/// </summary>
public class CharacterSelectUI : MonoBehaviour
{
    public static CharacterSelectUI Instance { get; private set; }

    const string Scene = "CharacterSelect";
    public const string StartScene = "Overworld_BrokenCrescent";

    SaveManager   _saveManager;
    TMP_FontAsset _font;
    TMP_InputField _nameField;
    GameObject    _canvasGo;   // hidden while the character creator is open

    // Gender selection
    string _selectedGender;   // "Male" or "Female" (null until picked)
    Button _maleBtn, _femaleBtn;
    Image  _maleImg,  _femaleImg;

    static readonly Color C_Gold      = new Color(1f, 0.85f, 0.45f);
    static readonly Color C_Selected  = new Color(0.28f, 0.55f, 0.30f);
    static readonly Color C_Unselected = new Color(0.18f, 0.18f, 0.20f);
    static readonly Color C_StartDim  = new Color(0.22f, 0.45f, 0.25f);
    static readonly Color C_StartLit  = new Color(0.30f, 0.62f, 0.28f);
    static readonly Color C_StartOff  = new Color(0.12f, 0.12f, 0.13f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded += (scene, mode) =>
        {
            if (scene.name == Scene && Instance == null) Spawn();
        };
        if (SceneManager.GetActiveScene().name == Scene && Instance == null) Spawn();
    }

    static void Spawn() => new GameObject("CharacterSelectUI (auto)").AddComponent<CharacterSelectUI>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        _saveManager = SaveManager.Instance ?? FindAnyObjectByType<SaveManager>();
        _font = UIUtil.FindFont();
        EnsureEventSystem();
        Build();
    }

    // ── UI ────────────────────────────────────────────────────────────────
    void Build()
    {
        var canvasGo = new GameObject("CharacterSelectCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        _canvasGo = canvasGo;
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // Full-screen background: the animated title (Resources/UI/TitleVideo) when it's there, else the still.
        var bg = NewRect("Background", canvasGo.transform); Stretch(bg);
        var bgImg = bg.gameObject.AddComponent<Image>();
        var video = Resources.Load<VideoClip>("UI/TitleVideo");
        if (video != null)
        {
            bgImg.color = Color.black;
            BuildVideoBackground(bg, video);
        }
        else
        {
            var title = Resources.Load<Sprite>("UI/TitleScreen");
            if (title == null)
            {
                var tex = Resources.Load<Texture2D>("UI/TitleScreen");
                if (tex != null)
                    title = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            if (title != null) { bgImg.sprite = title; bgImg.color = Color.white; bgImg.preserveAspect = false; }
            else bgImg.color = new Color(0.10f, 0.09f, 0.10f, 1f);

            // ── Welcome text ── (the video brings its own title and mood)
            MakeText(canvasGo.transform, "Welcome", "A forgotten land stretches before you, lost traveler...",
                     28, FontStyles.Italic, new Color(0.82f, 0.78f, 0.70f), new Vector2(0, 280), 60);
        }

        // You make your character once. After that this screen is OSRS's "click here to play": Play goes
        // straight back into the world with your save, and starting over is the small button.
        bool returning = SaveManager.SaveExistsFor(StartScene) && CharacterAppearance.Exists;

        // No gender pick, no name field: this is only the title menu. You build (and name) your character
        // in the creator, once — see StartGame.
        BuildMenu(canvasGo.transform, returning);
    }

    VideoPlayer _video;
    RenderTexture _videoRt;

    /// <summary>The animated title screen: plays the clip on a loop into a RenderTexture. The whole frame is
    /// shown (the logo sits at the very top and the credit at the bottom, so cropping would cut them); where
    /// the screen is wider than the video, a darkened, enlarged copy of it fills the sides instead of bars.</summary>
    void BuildVideoBackground(RectTransform bg, VideoClip clip)
    {
        _videoRt = new RenderTexture((int)clip.width, (int)clip.height, 0) { name = "TitleVideoRT" };
        var prev = RenderTexture.active;                     // black until the first frame decodes, not garbage
        RenderTexture.active = _videoRt; GL.Clear(true, true, Color.black); RenderTexture.active = prev;

        _video = gameObject.AddComponent<VideoPlayer>();
        _video.clip = clip;
        _video.isLooping = true;
        _video.playOnAwake = false;
        _video.skipOnDrop = true;
        _video.renderMode = VideoRenderMode.RenderTexture;
        _video.targetTexture = _videoRt;
        var audio = gameObject.AddComponent<AudioSource>();   // through the mixer, so Master volume applies
        audio.playOnAwake = false;
        _video.audioOutputMode = VideoAudioOutputMode.AudioSource;
        _video.SetTargetAudioSource(0, audio);
        _video.prepareCompleted += vp => vp.Play();
        _video.Prepare();

        float aspect = (float)clip.width / clip.height;
        Layer("VideoFill", AspectRatioFitter.AspectMode.EnvelopeParent, new Color(0.18f, 0.18f, 0.18f, 1f));   // dim: a glow, not a second copy
        Layer("Video", AspectRatioFitter.AspectMode.FitInParent, Color.white);

        void Layer(string name, AspectRatioFitter.AspectMode mode, Color tint)
        {
            var rt = NewRect(name, bg);
            var raw = rt.gameObject.AddComponent<RawImage>();
            raw.texture = _videoRt; raw.color = tint; raw.raycastTarget = false;
            var fit = rt.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectRatio = aspect;
            fit.aspectMode = mode;
        }
    }

    void OnDestroy()
    {
        if (_videoRt != null) { _videoRt.Release(); Destroy(_videoRt); }
    }

    // ── Title menu ─────────────────────────────────────────────────────────
    // After the key-art mock-up: serif small caps straight on the scene, the chosen entry lit on a dark band
    // between two glowing gold rules, faint rules between entries, and a gold divider with the radiation mark
    // under the list. Hover (mouse) or press (finger) moves the light; a click / tap acts.

    /// <summary>The Credits screen. Edit freely.</summary>
    const string CreditsText =
        "<size=150%><color=#E8C48A>WASTELANDERS</color></size>\n" +
        "A game by Sbaum Productions\n\n" +
        "Made with Unity\n" +
        "Art from Synty Studios, Hivemind, Leartes Studios\n" +
        "and the other asset makers whose work fills the wasteland\n\n" +
        "<size=75%><color=#9A8F7E>Tap anywhere to close</color></size>";

    static readonly Color C_MenuText = new Color(0.86f, 0.80f, 0.70f, 1f);
    static readonly Color C_MenuLit  = new Color(1f, 0.96f, 0.88f, 1f);
    static readonly Color C_Glow     = new Color(1f, 0.62f, 0.22f, 1f);
    static readonly Color C_Warn     = new Color(1f, 0.55f, 0.35f, 1f);

    readonly List<(RectTransform row, TMP_Text label, string text)> _menu = new();
    RectTransform _lit;
    CanvasGroup _menuGroup;
    GameObject _credits;
    int _litIndex, _newGameRow = -1;
    float _confirmUntil;

    void BuildMenu(Transform canvas, bool returning)
    {
        var items = new List<(string text, UnityEngine.Events.UnityAction act)>();
        if (returning) items.Add(("Continue", ContinueGame));                 // straight back in with your save
        _newGameRow = items.Count;
        items.Add(("New Game", returning ? ConfirmNewGame : StartGame));      // over a save it archives you: tap twice
        items.Add(("Settings", () => SettingsUI.Instance?.Open()));
        items.Add(("Credits", ShowCredits));

        const float RowH = 56f, Width = 520f, Divider = 46f;
        float height = items.Count * RowH + Divider;

        var box = NewRect("Menu", canvas);
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0f);
        box.pivot = new Vector2(0.5f, 0f);
        box.anchoredPosition = new Vector2(0f, 40f);
        box.sizeDelta = new Vector2(Width, height);
        _menuGroup = box.gameObject.AddComponent<CanvasGroup>();

        // A soft pool of shadow behind the list, so it reads over the busy middle of the scene.
        var shade = NewRect("Shade", box);
        shade.anchorMin = shade.anchorMax = new Vector2(0.5f, 0.5f);
        shade.sizeDelta = new Vector2(Width * 1.7f, height * 1.5f);
        var shadeImg = shade.gameObject.AddComponent<Image>();
        shadeImg.sprite = GradientSprite(radial: true); shadeImg.color = new Color(0f, 0f, 0f, 0.7f); shadeImg.raycastTarget = false;

        // The light: a dark band with a glowing gold rule above and below it; it slides to the chosen entry.
        _lit = NewRect("Lit", box);
        _lit.anchorMin = _lit.anchorMax = new Vector2(0.5f, 0f);
        _lit.sizeDelta = new Vector2(Width * 0.62f, RowH - 6f);
        var band = _lit.gameObject.AddComponent<Image>();
        band.sprite = GradientSprite(radial: false); band.color = new Color(0f, 0f, 0f, 0.6f); band.raycastTarget = false;
        GlowRule(_lit, 1f); GlowRule(_lit, 0f);

        for (int i = 0; i < items.Count; i++)
        {
            float y = height - (i + 0.5f) * RowH;
            var row = NewRect(items[i].text, box);
            row.anchorMin = row.anchorMax = new Vector2(0.5f, 0f);
            row.anchoredPosition = new Vector2(0f, y);
            row.sizeDelta = new Vector2(Width * 0.62f, RowH);
            row.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);   // the tap target
            int index = i; var act = items[i].act;
            var btn = row.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => { Light(index); act(); });
            row.gameObject.AddComponent<MenuPointer>().onEnterOrPress = () => Light(index);

            var labelRt = NewRect("Label", row); Stretch(labelRt);
            var t = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = MenuFont(); t.fontSize = 34; t.fontStyle = FontStyles.SmallCaps; t.characterSpacing = 6f;
            t.alignment = TextAlignmentOptions.Center; t.textWrappingMode = TextWrappingModes.NoWrap; t.raycastTarget = false;
            t.text = items[i].text;
            _menu.Add((row, t, items[i].text));

            if (i < items.Count - 1)   // a faint rule between entries
            {
                var sep = NewRect("Rule", box);
                sep.anchorMin = sep.anchorMax = new Vector2(0.5f, 0f);
                sep.anchoredPosition = new Vector2(0f, y - RowH * 0.5f);
                sep.sizeDelta = new Vector2(Width * 0.5f, 1.5f);
                var si = sep.gameObject.AddComponent<Image>();
                si.sprite = GradientSprite(radial: false); si.color = new Color(1f, 0.93f, 0.8f, 0.22f); si.raycastTarget = false;
            }
        }

        // The divider under the list: two gold rules either side of the radiation mark.
        var div = NewRect("Divider", box);
        div.anchorMin = div.anchorMax = new Vector2(0.5f, 0f);
        div.anchoredPosition = new Vector2(0f, Divider * 0.5f);
        div.sizeDelta = new Vector2(Width * 0.56f, 24f);
        for (int s = -1; s <= 1; s += 2)
        {
            var r = NewRect("Rule", div);
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(s * Width * 0.155f, 0f);
            r.sizeDelta = new Vector2(Width * 0.24f, 2f);
            var ri = r.gameObject.AddComponent<Image>();
            ri.sprite = GradientSprite(radial: false); ri.color = new Color(0.85f, 0.62f, 0.3f, 0.9f); ri.raycastTarget = false;
        }
        var mark = NewRect("Mark", div);
        mark.anchorMin = mark.anchorMax = new Vector2(0.5f, 0.5f);
        mark.sizeDelta = new Vector2(22f, 22f);
        var mi = mark.gameObject.AddComponent<Image>();
        mi.sprite = TrefoilSprite(); mi.color = new Color(0.9f, 0.66f, 0.3f, 1f); mi.raycastTarget = false;

        Light(0);
    }

    /// <summary>A glowing gold rule along the top (edge 1) or bottom (edge 0) of the lit band.</summary>
    void GlowRule(RectTransform band, float edge)
    {
        var r = NewRect("Glow", band);
        r.anchorMin = new Vector2(0f, edge); r.anchorMax = new Vector2(1f, edge);
        r.anchoredPosition = Vector2.zero; r.sizeDelta = new Vector2(0f, 2.5f);
        var img = r.gameObject.AddComponent<Image>();
        img.sprite = GradientSprite(radial: false); img.color = C_Glow; img.raycastTarget = false;
    }

    void Light(int i)
    {
        if (_lit == null || i < 0 || i >= _menu.Count) return;
        _litIndex = i;
        _lit.anchoredPosition = _menu[i].row.anchoredPosition;
        for (int k = 0; k < _menu.Count; k++)
        {
            bool warn = k == _newGameRow && _confirmUntil > 0f;
            _menu[k].label.color = warn ? C_Warn : k == i ? C_MenuLit : C_MenuText;
            _menu[k].label.fontSize = k == i ? 36 : 34;
        }
    }

    /// <summary>The menu's serif: Resources/UI/Fonts/Cinzel SDF when it's there, else the UI font.</summary>
    TMP_FontAsset MenuFont() => Resources.Load<TMP_FontAsset>("UI/Fonts/Cinzel SDF") ?? _font;

    /// <summary>Straight back into the world as your saved character — no creator, nothing wiped.</summary>
    void ContinueGame()
    {
        _menuGroup.interactable = false;
        StartCoroutine(LoadWorld());
    }

    /// <summary>Starting over archives your current character, so it takes a second tap to mean it.</summary>
    void ConfirmNewGame()
    {
        if (Time.unscaledTime > _confirmUntil)
        {
            _confirmUntil = Time.unscaledTime + 3f;
            _menu[_newGameRow].label.text = "Start over? Tap again";
            Light(_newGameRow);
            return;
        }
        _confirmUntil = 0f;
        StartGame();
    }

    void ShowCredits()
    {
        if (_credits == null)
        {
            var rt = NewRect("Credits", _canvasGo.transform); Stretch(rt);
            rt.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.86f);
            rt.gameObject.AddComponent<Button>().onClick.AddListener(() => _credits.SetActive(false));
            var txt = NewRect("Text", rt); Stretch(txt);
            txt.offsetMin = new Vector2(120f, 80f); txt.offsetMax = new Vector2(-120f, -80f);
            var t = txt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = MenuFont(); t.fontSize = 32; t.color = C_MenuText; t.characterSpacing = 2f;
            t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
            t.text = CreditsText;
            _credits = rt.gameObject;
        }
        _credits.SetActive(true);
        _credits.transform.SetAsLastSibling();
    }

    void Update()
    {
        // The New Game confirm lapsed: put the label back.
        if (_confirmUntil > 0f && Time.unscaledTime > _confirmUntil)
        {
            _confirmUntil = 0f;
            if (_newGameRow >= 0 && _newGameRow < _menu.Count) _menu[_newGameRow].label.text = _menu[_newGameRow].text;
            Light(_litIndex);
        }
        if (_credits != null && _credits.activeSelf && Input.GetKeyDown(KeyCode.Escape)) _credits.SetActive(false);
    }

    /// <summary>Moves the menu's light: on hover with a mouse, on press with a finger.</summary>
    class MenuPointer : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler
    {
        public System.Action onEnterOrPress;
        public void OnPointerEnter(PointerEventData e) => onEnterOrPress?.Invoke();
        public void OnPointerDown(PointerEventData e) => onEnterOrPress?.Invoke();
    }

    static Sprite _hGrad, _rGrad, _trefoil;

    /// <summary>White with its alpha fading out from the middle: sideways (rules, the band) or all round (the shade).</summary>
    static Sprite GradientSprite(bool radial)
    {
        var cached = radial ? _rGrad : _hGrad;
        if (cached != null) return cached;
        int w = radial ? 128 : 256, h = radial ? 128 : 4;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                float a = Mathf.Clamp01(1f - (radial ? Mathf.Sqrt(u * u + v * v) : Mathf.Abs(u)));
                px[y * w + x] = new Color(1f, 1f, 1f, a * a * (3f - 2f * a));   // smoothstep
            }
        tex.SetPixels(px); tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        if (radial) _rGrad = sprite; else _hGrad = sprite;
        return sprite;
    }

    /// <summary>The radiation trefoil: three 60° blades round a hub, white (the Image tints it gold).</summary>
    static Sprite TrefoilSprite()
    {
        if (_trefoil != null) return _trefoil;
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x + 0.5f - N / 2f, dy = y + 0.5f - N / 2f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / (N / 2f);
                float ang = Mathf.Repeat(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg - 90f, 120f);   // blades point up, down-left, down-right
                bool blade = r > 0.3f && r < 0.95f && (ang < 30f || ang > 90f);
                px[y * N + x] = new Color(1f, 1f, 1f, blade || r < 0.2f ? 1f : 0f);
            }
        tex.SetPixels(px); tex.Apply();
        _trefoil = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        return _trefoil;
    }

    Button MakeGenderButton(Transform parent, string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        var rt = NewRect(label + "Btn", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(220, 72);

        var img = rt.gameObject.AddComponent<Image>();
        img.color = C_Unselected;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        MakeText(rt, "Label", label, 30, FontStyles.Bold, Color.white, Vector2.zero, 72);
        return btn;
    }

    // ── actions ─────────────────────────────────────────────────────────────
    void StartGame()
    {
        // Build your face and body before entering the world. Gear is deliberately NOT offered —
        // every armour piece is a drop, so the creator only exposes base body parts and everyone
        // starts in the same fixed clothing. Confirming continues into the world.
        //
        // Hide this screen first: its canvas is a fullscreen opaque background, which would sit
        // over the creator's 3D character preview even though the creator's UI draws on top.
        if (_canvasGo != null) _canvasGo.SetActive(false);
        if (_video != null) _video.Stop();   // hidden behind the creator: no point decoding it (or hearing it)
        CharacterCreatorUI.Open(_ => EnterWorld());
    }

    /// <summary>Wipe the old run and load the world. Split out of StartGame so the character
    /// creator can call it once the player has confirmed their appearance.</summary>
    void EnterWorld()
    {
        SaveManager.StartNewRun(StartScene);
        StartCoroutine(LoadWorld());
    }

    /// <summary>Load the world ASYNCHRONOUSLY, with a progress readout.
    ///
    /// SceneManager.LoadScene is fully blocking, and MainWorld3D pulls in ~320 MB across 149
    /// TerrainData assets — the process stops rendering and Windows paints it "Not Responding", which
    /// is indistinguishable from a crash. LoadSceneAsync keeps the frame loop alive so the player can
    /// see it's working. The load itself takes just as long; it just no longer looks like a hang.</summary>
    IEnumerator LoadWorld()
    {
        var overlay = BuildLoadingOverlay(out TMPro.TextMeshProUGUI label);

        var op = SceneManager.LoadSceneAsync(StartScene);
        if (op == null)
        {
            Debug.LogError($"[CharacterSelect] Could not load '{StartScene}'. Is it enabled in Build Settings?");
            if (overlay != null) Destroy(overlay);
            yield break;
        }

        // Progress stalls at 0.9 until activation; show it as the last stretch rather than a stuck bar.
        while (!op.isDone)
        {
            if (label != null)
                label.text = $"Entering the wasteland… {Mathf.RoundToInt(Mathf.Clamp01(op.progress / 0.9f) * 100f)}%";
            yield return null;
        }
    }

    /// <summary>A fullscreen panel that survives the load, so the last frame of the creator isn't left
    /// frozen on screen while the world streams in.</summary>
    GameObject BuildLoadingOverlay(out TMPro.TextMeshProUGUI label)
    {
        var go = new GameObject("LoadingOverlay");
        DontDestroyOnLoad(go);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;                       // above everything, including the HUD
        go.AddComponent<UnityEngine.UI.CanvasScaler>();

        var bgGo = new GameObject("BG");
        bgGo.transform.SetParent(go.transform, false);
        var bgRt = bgGo.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
        bgGo.AddComponent<UnityEngine.UI.Image>().color = new Color(0.03f, 0.03f, 0.04f, 1f);

        var txtGo = new GameObject("Label");
        txtGo.transform.SetParent(go.transform, false);
        var txtRt = txtGo.AddComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = txtRt.offsetMax = Vector2.zero;
        label = txtGo.AddComponent<TMPro.TextMeshProUGUI>();
        label.text = "Entering the wasteland…";
        label.fontSize = 28;
        label.alignment = TMPro.TextAlignmentOptions.Center;

        // Nothing in the world tore this down: it sat over a ready world until a 120 s timer ran out, which on
        // a phone read as a two-minute load. It fades away itself the moment your character is in the world.
        go.AddComponent<DismissWhenWorldReady>();
        return go;
    }

    /// <summary>Fades the loading overlay out once the world has loaded and the player exists (one beat
    /// later, so the first frame is drawn behind it). A 90 s cap covers a world with no player.</summary>
    class DismissWhenWorldReady : MonoBehaviour
    {
        const float Fade = 0.5f, Cap = 90f;
        float _born, _fadeAt = -1f;
        CanvasGroup _group;

        void Awake() { _born = Time.unscaledTime; _group = gameObject.AddComponent<CanvasGroup>(); }

        void Update()
        {
            bool ready = PlayerEntity.Instance != null && SceneManager.GetActiveScene().name != Scene;
            if (_fadeAt < 0f && (ready || Time.unscaledTime - _born > Cap)) _fadeAt = Time.unscaledTime + 0.25f;
            if (_fadeAt < 0f) return;
            float k = (Time.unscaledTime - _fadeAt) / Fade;
            if (k < 0f) return;
            _group.alpha = 1f - Mathf.Clamp01(k);
            if (k >= 1f) Destroy(gameObject);
        }
    }

    // ── builders ──────────────────────────────────────────────────────────
    Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size, Color color,
                      UnityEngine.Events.UnityAction onClick)
    {
        var rt = NewRect(label + "Button", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;

        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.highlightedColor = color * 1.25f;
        colors.pressedColor     = color * 0.8f;
        colors.fadeDuration     = 0.05f;
        btn.colors = colors;
        btn.onClick.AddListener(onClick);

        var txt = MakeText(rt, "Label", label, 38, FontStyles.Bold, Color.white, Vector2.zero, size.y);
        Stretch(txt.rectTransform);
        txt.alignment = TextAlignmentOptions.Center;
        return btn;
    }

    TMP_InputField MakeInputField(Transform parent, Vector2 pos, Vector2 size, string startText)
    {
        var rt = NewRect("NameField", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        rt.gameObject.AddComponent<Image>().color = new Color(0.16f, 0.16f, 0.18f, 1f);

        var input = rt.gameObject.AddComponent<TMP_InputField>();

        var area = NewRect("TextArea", rt);
        area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(16, 8); area.offsetMax = new Vector2(-16, -8);
        area.gameObject.AddComponent<RectMask2D>();

        var placeholder = MakeText(area, "Placeholder", "Enter name...", 30, FontStyles.Italic,
                                   new Color(0.6f, 0.6f, 0.62f), Vector2.zero, size.y);
        Stretch(placeholder.rectTransform);
        placeholder.alignment = TextAlignmentOptions.Left;

        var text = MakeText(area, "Text", "", 30, FontStyles.Normal, Color.white, Vector2.zero, size.y);
        Stretch(text.rectTransform);
        text.alignment = TextAlignmentOptions.Left;

        input.textViewport  = area;
        input.textComponent = text;
        input.placeholder   = placeholder;
        input.text          = startText;
        input.characterLimit = 16;
        input.lineType      = TMP_InputField.LineType.SingleLine;
        return input;
    }

    TMP_Text MakeText(Transform parent, string name, string content, float size, FontStyles style,
                      Color color, Vector2 pos, float height)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(900, height);

        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = _font; t.fontSize = size; t.fontStyle = style; t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.text = content;
        t.raycastTarget = false;
        return t;
    }

    // ── helpers ───────────────────────────────────────────────────────────
    static RectTransform NewRect(string name, Transform parent)
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
