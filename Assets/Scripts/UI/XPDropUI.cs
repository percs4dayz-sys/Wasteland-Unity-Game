using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// OSRS-style XP drops: a small "+N Skill" floats up and fades whenever you gain XP.
/// Fully self-building and auto-spawned — no inspector setup required.
/// </summary>
public class XPDropUI : MonoBehaviour
{
    public static XPDropUI Instance { get; private set; }

    const float LIFE = 1.6f;     // seconds a drop stays alive
    const float RISE = 60f;      // pixels it floats upward over its life

    RectTransform _anchor;       // top-centre stack point
    PlayerStats _stats;
    TMP_Text _congratulations;
    float _celebrationUntil;
    class Spark { public UnityEngine.UI.Image image; public Vector2 velocity; public float age; }
    readonly List<Spark> _sparks = new();

    AudioSource _sfx;            // plays the level-up jingle
    AudioClip _levelUpClip;
    bool _clipLoaded;

    class Drop { public RectTransform rt; public TMP_Text txt; public float age; }
    readonly List<Drop> _drops = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("XPDropUI (auto)").AddComponent<XPDropUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);   // survive scene changes (warp to 3D world etc.)
        BuildCanvas();

        // 2D UI sound — ignores the listener's 3D position so the jingle is always full volume.
        _sfx = gameObject.AddComponent<AudioSource>();
        _sfx.playOnAwake = false;
        _sfx.spatialBlend = 0f;
    }

    void BuildCanvas()
    {
        var canvasGo = new GameObject("XPDropCanvas",
            typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 450;
        var scaler = canvasGo.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var go = new GameObject("Anchor", typeof(RectTransform));
        go.transform.SetParent(canvasGo.transform, false);
        _anchor = go.GetComponent<RectTransform>();
        _anchor.anchorMin = _anchor.anchorMax = new Vector2(0.5f, 1f);
        _anchor.pivot = new Vector2(0.5f, 1f);
        _anchor.anchoredPosition = new Vector2(0, -70);
    }

    void Update()
    {
        TryBind(); // A persistent HUD must follow the current player, even while the old one still exists.
        if (_congratulations != null) _congratulations.gameObject.SetActive(Time.unscaledTime < _celebrationUntil);
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            var spark = _sparks[i];
            spark.age += Time.unscaledDeltaTime;
            spark.velocity += Vector2.down * (80f * Time.unscaledDeltaTime);
            spark.image.rectTransform.anchoredPosition += spark.velocity * Time.unscaledDeltaTime;
            var color = spark.image.color;
            color.a = Mathf.Clamp01(1f - spark.age / 1.5f);
            spark.image.color = color;
            if (spark.age >= 1.5f) { Destroy(spark.image.gameObject); _sparks.RemoveAt(i); }
        }

        for (int i = _drops.Count - 1; i >= 0; i--)
        {
            var d = _drops[i];
            d.age += Time.deltaTime;
            float t = d.age / LIFE;
            d.rt.anchoredPosition += new Vector2(0, RISE * Time.deltaTime);
            var c = d.txt.color; c.a = Mathf.Clamp01(1f - t); d.txt.color = c;
            if (d.age >= LIFE) { Destroy(d.rt.gameObject); _drops.RemoveAt(i); }
        }
    }

    void TryBind()
    {
        var player = PlayerEntity.Instance;
        var current = player != null ? player.Stats : null;
        if (current == _stats) return;
        Unbind();
        _stats = current;
        if (_stats == null) return;
        _stats.OnXPGained += OnXPGained;
        _stats.OnLevelUp  += OnLevelUp;
    }

    void OnDestroy() => Unbind();

    void Unbind()
    {
        if (_stats != null)
        {
            _stats.OnXPGained -= OnXPGained;
            _stats.OnLevelUp  -= OnLevelUp;
        }
        _stats = null;
    }

    // OSRS-style level-up: a chat shout, the level-up jingle, and a fist-pump on the player model
    // (the animation is driven separately by PlayerAnimator3D, which also listens to OnLevelUp).
    void OnLevelUp(Skill skill, int newLevel)
    {
        var sd = _stats != null ? _stats.GetSkillData(skill) : null;
        string name    = sd != null ? sd.displayName : skill.ToString();
        string flavour = sd != null && !string.IsNullOrEmpty(sd.levelUpFlavour) ? "  " + sd.levelUpFlavour : "";
        HUDController.Emit(
            $"<color=#FFD24A>Congratulations!</color> You've reached <color=#FFD24A>{name} level {newLevel}</color>.{flavour}");

        PlayLevelUpSound();
        ShowCelebration(name, newLevel);
    }

    void ShowCelebration(string skillName, int level)
    {
        if (_anchor == null) return;
        if (_congratulations == null)
        {
            var go = new GameObject("SkillLevelCongratulations", typeof(RectTransform));
            go.transform.SetParent(_anchor, false);
            _congratulations = go.AddComponent<TextMeshProUGUI>();
            UITheme.Label(_congratulations, 28f, UITheme.Amber, shadow: true, upper: false);
            _congratulations.alignment = TextAlignmentOptions.Center;
            _congratulations.raycastTarget = false;
            _congratulations.rectTransform.sizeDelta = new Vector2(640f, 90f);
            _congratulations.rectTransform.anchoredPosition = new Vector2(0f, -100f);
        }
        _congratulations.text = $"Congratulations!\n{skillName} level {level}";
        _celebrationUntil = Time.unscaledTime + 4f;
        _congratulations.gameObject.SetActive(true);
        // Small radial fireworks on either side of the announcement, with no input blocking.
        foreach (float x in new[] { -340f, 340f })
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI * 2f / 16f;
                var go = new GameObject("LevelSpark", typeof(RectTransform));
                go.transform.SetParent(_anchor, false);
                var spark = go.AddComponent<UnityEngine.UI.Image>();
                spark.sprite = UITheme.RoundFillSprite;
                spark.color = Color.HSVToRGB(i / 16f, 0.65f, 1f);
                spark.raycastTarget = false;
                spark.rectTransform.sizeDelta = new Vector2(6f, 6f);
                spark.rectTransform.anchoredPosition = new Vector2(x, -100f);
                _sparks.Add(new Spark { image = spark, velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 100f });
            }
    }

    void PlayLevelUpSound()
    {
        if (!_clipLoaded)   // load once, then cache (even a null result, so we don't retry every ding)
        {
            _levelUpClip = Resources.Load<AudioClip>("SFX/levelup");
            _clipLoaded = true;
            if (_levelUpClip == null)
                Debug.LogWarning("[XPDropUI] Level-up sound missing: Assets/Resources/SFX/levelup.mp3");
        }
        if (_sfx != null && _levelUpClip != null) _sfx.PlayOneShot(_levelUpClip);
    }

    void OnXPGained(Skill skill, int amount)
    {
        if (_anchor == null || amount <= 0) return;

        // push existing drops up a little so a fresh one doesn't overlap
        foreach (var d in _drops) d.rt.anchoredPosition += new Vector2(0, 22);

        var go = new GameObject("XP_" + skill, typeof(RectTransform));
        go.transform.SetParent(_anchor, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(300, 30);

        var txt = go.AddComponent<TextMeshProUGUI>();
        UITheme.Label(txt, 22f, UITheme.Amber, shadow: true, upper: false);   // amber amount, pale skill name
        txt.text = $"+{amount} <color=#EDE6D8>{skill}</color>";
        txt.alignment = TextAlignmentOptions.Center;

        _drops.Add(new Drop { rt = rt, txt = txt, age = 0f });
    }
}
