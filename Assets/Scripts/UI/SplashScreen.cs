using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Full-screen splash that pops up when a NEW game begins — auto-spawned and self-building, so no
/// scene wiring is needed. Shows on a gameplay scene load when either:
///   • a New Game was just started (DevPanel / future menu sets <see cref="PendingNewGame"/>), or
///   • there's no save file on disk yet (a fresh run — your first time hitting Play).
/// Continuing an existing save shows nothing. Art is Resources/UI/Splash (drop a dedicated image
/// there any time); falls back to the title art (UI/TitleScreen), then to a styled text card, so it
/// is never blank. Holds ~2s then fades, and any click / key skips it immediately.
/// </summary>
public class SplashScreen : MonoBehaviour
{
    /// <summary>Set right before loading into a fresh run to force the splash next scene
    /// (survives the scene load — statics persist within a play session).</summary>
    public static bool PendingNewGame;

    static bool _live;   // one splash at a time

    // Skip the title/menu scene — the splash is for dropping INTO the game world.
    const string TitleScene = "CharacterSelect";

    const float HoldSeconds = 2.0f;   // fully visible before it starts fading
    const float FadeSeconds = 0.6f;

    CanvasGroup _group;
    float _timer;
    bool _fading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded += (scene, mode) => MaybeShow(scene.name);
        MaybeShow(SceneManager.GetActiveScene().name);
    }

    static void MaybeShow(string sceneName)
    {
        if (sceneName == TitleScene || _live) return;

        // New game = explicitly requested, or no save on disk (a fresh start). A continued save is silent.
        bool newGame = PendingNewGame || !SaveManager.SaveExists();
        PendingNewGame = false;
        if (!newGame) return;

        _live = true;
        new GameObject("SplashScreen (auto)").AddComponent<SplashScreen>();
    }

    void Start() => Build();

    void OnDestroy() => _live = false;

    void Update()
    {
        _timer += Time.unscaledDeltaTime;

        // Any input skips straight to the fade.
        if (!_fading && _timer >= 0.25f && (Input.anyKeyDown || Input.GetMouseButtonDown(0)))
        {
            _fading = true;
            _timer = HoldSeconds;
        }

        if (_timer < HoldSeconds) return;
        if (!_fading) _fading = true;

        float t = (_timer - HoldSeconds) / FadeSeconds;
        if (_group != null) _group.alpha = Mathf.Clamp01(1f - t);
        if (t >= 1f) Destroy(gameObject);
    }

    void Build()
    {
        var canvasGo = new GameObject("SplashCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;   // above everything, including the dev panel
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        _group = canvasGo.GetComponent<CanvasGroup>();

        // Opaque black backdrop so the splash reads cleanly over whatever just loaded.
        var bg = Rect("Background", canvasGo.transform); Stretch(bg);
        bg.gameObject.AddComponent<Image>().color = Color.black;

        // Art: a dedicated splash if present, else the title art, else a text card.
        var sprite = LoadSprite("UI/Splash") ?? LoadSprite("UI/TitleScreen");
        if (sprite != null)
        {
            var art = Rect("Art", canvasGo.transform); Stretch(art);
            var img = art.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = Color.white;
            img.preserveAspect = false;  // fill the screen edge-to-edge (art is ~16:9)
        }
        else
        {
            MakeText(canvasGo.transform, "WASTELAND", 96, FontStyles.Bold,
                     new Color(1f, 0.85f, 0.4f), new Vector2(0, 40));
        }

        // "NEW GAME" banner + skip hint.
        MakeText(canvasGo.transform, "NEW GAME", 40, FontStyles.Bold,
                 new Color(0.9f, 0.95f, 1f), new Vector2(0, -300));
        MakeText(canvasGo.transform, "click to continue", 24, FontStyles.Italic,
                 new Color(0.75f, 0.75f, 0.78f), new Vector2(0, -360));
    }

    static Sprite LoadSprite(string path)
    {
        var s = Resources.Load<Sprite>(path);
        if (s != null) return s;
        var tex = Resources.Load<Texture2D>(path);   // imported as a plain texture? build a sprite anyway
        return tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f)) : null;
    }

    TMP_Text MakeText(Transform parent, string content, float size, FontStyles style, Color color, Vector2 pos)
    {
        var rt = Rect("Text", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(1400, size * 1.6f);

        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = UIUtil.FindFont();
        t.fontSize = size; t.fontStyle = style; t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.text = content; t.raycastTarget = false;
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
}
