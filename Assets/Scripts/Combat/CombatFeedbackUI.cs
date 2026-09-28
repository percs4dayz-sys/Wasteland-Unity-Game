using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// OSRS-style combat feedback: floating hitsplats (damage numbers) on you and the enemy,
/// an enemy HP bar over your target, and your own HP bar over your head while fighting.
/// Driven entirely by existing combat events. Self-building & auto-spawned.
/// </summary>
public class CombatFeedbackUI : MonoBehaviour
{
    public static CombatFeedbackUI Instance { get; private set; }

    static readonly Color C_Hit  = new Color(0.85f, 0.15f, 0.12f, 1f); // red damage
    static readonly Color C_Miss = new Color(0.20f, 0.35f, 0.85f, 1f); // blue miss (0)
    static readonly Color C_EnemyHp = new Color(0.85f, 0.2f, 0.2f, 1f);
    static readonly Color C_PlayerHp = new Color(0.4f, 0.85f, 0.35f, 1f);

    const float SPLAT_LIFE = 1.0f;
    const float SPLAT_RISE = 55f;

    Canvas _canvas;
    RectTransform _canvasRect;
    Camera _cam;

    GameObject _enemyBar; Image _enemyFill; TMP_Text _enemyText;
    GameObject _playerBar; Image _playerFill; TMP_Text _playerText;

    CombatTarget _subbedTarget;

    /// <summary>Set by 3D action combat — the enemy whose HP bar/hitsplats we show.</summary>
    public CombatTarget FocusTarget;

    /// <summary>Show a damage/miss splat at a world position (used by 3D combat).</summary>
    public static void ShowWorldSplat(Vector3 worldPos, int dmg, bool hit)
        => Instance?.SpawnSplat(worldPos, hit ? dmg.ToString() : "0", hit ? C_Hit : C_Miss);

    class Splat { public RectTransform rt; public TMP_Text txt; public Image bg; public float age; }
    readonly List<Splat> _splats = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("CombatFeedbackUI (auto)").AddComponent<CombatFeedbackUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildCanvas();
        _enemyBar  = BuildBar(C_EnemyHp,  out _enemyFill,  out _enemyText);
        _playerBar = BuildBar(C_PlayerHp, out _playerFill, out _playerText);
        _enemyBar.SetActive(false);
        _playerBar.SetActive(false);
    }

    void OnDestroy()
    {
        if (_subbedTarget != null) _subbedTarget.OnDamaged -= OnEnemyDamaged;
    }

    void Update()
    {
        UpdateTargetSubscription();
        UpdateBars();
        AnimateSplats();
    }

    // ── target subscription (the 3D action-combat focus target) ───────────
    void UpdateTargetSubscription()
    {
        var cur = (FocusTarget != null && !FocusTarget.IsDead) ? FocusTarget : null;
        if (cur == _subbedTarget) return;
        if (_subbedTarget != null) _subbedTarget.OnDamaged -= OnEnemyDamaged;
        _subbedTarget = cur;
        if (_subbedTarget != null) _subbedTarget.OnDamaged += OnEnemyDamaged;
    }

    // damage to the enemy (covers BOTH your hits and your pack's hits, since both call TakeDamage).
    // Misses spawn their own "0" splat directly from ActionCombat3D via ShowWorldSplat.
    void OnEnemyDamaged(int amount)
    {
        if (_subbedTarget == null) return;
        SpawnSplat(_subbedTarget.transform.position, amount.ToString(), C_Hit);
    }

    // ── HP bars ──────────────────────────────────────────────────────────
    void UpdateBars()
    {
        if (_cam == null) _cam = Camera.main;
        float overhead = GameMode.Is3D ? 2.2f : 0.75f;

        // enemy bar — the 3D action-combat focus target
        var target = (FocusTarget != null && !FocusTarget.IsDead) ? FocusTarget : null;
        if (target != null && !target.IsDead)
        {
            PlaceOver(_enemyBar, target.transform.position, overhead);
            _enemyFill.fillAmount = Mathf.Clamp01(target.HPPercent);
            _enemyText.text = $"{Mathf.Max(0, target.CurrentHP)}/{target.maxHP}";
            _enemyBar.SetActive(true);
        }
        else _enemyBar.SetActive(false);

        // player bar — while you have a target locked, or whenever you're hurt
        var p = PlayerEntity.Instance;
        bool showPlayer = p != null &&
            (target != null || p.Stats.CurrentHP < p.Stats.MaxHP);
        if (showPlayer)
        {
            int cur = p.Stats.CurrentHP, max = p.Stats.MaxHP;
            PlaceOver(_playerBar, p.transform.position, overhead);
            _playerFill.fillAmount = max > 0 ? Mathf.Clamp01((float)cur / max) : 0f;
            _playerText.text = $"{cur}/{max}";
            _playerBar.SetActive(true);
        }
        else _playerBar.SetActive(false);
    }

    void PlaceOver(GameObject bar, Vector3 worldPos, float upOffset)
    {
        if (_cam == null) return;
        Vector3 sp = _cam.WorldToScreenPoint(worldPos + Vector3.up * upOffset);
        if (sp.z < 0f) { bar.SetActive(false); return; }
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, sp, null, out var local))
            ((RectTransform)bar.transform).anchoredPosition = local;
    }

    // ── hitsplats ────────────────────────────────────────────────────────
    void SpawnSplat(Vector3 worldPos, string text, Color color)
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        Vector3 sp = _cam.WorldToScreenPoint(worldPos + Vector3.up * (GameMode.Is3D ? 1.6f : 0.4f));
        if (sp.z < 0f) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, sp, null, out var local)) return;

        var go = new GameObject("Splat", typeof(RectTransform));
        go.transform.SetParent(_canvasRect, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = local + new Vector2(Random.Range(-10f, 10f), 0f);
        rt.sizeDelta = new Vector2(44, 30);

        var bg = go.AddComponent<Image>();
        bg.color = color;
        bg.raycastTarget = false;

        var txtGo = new GameObject("N", typeof(RectTransform));
        txtGo.transform.SetParent(rt, false);
        var trt = txtGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        var txt = txtGo.AddComponent<TextMeshProUGUI>();
        txt.text = text; txt.fontSize = 18; txt.fontStyle = FontStyles.Bold;
        txt.color = Color.white; txt.alignment = TextAlignmentOptions.Center;
        txt.raycastTarget = false;

        _splats.Add(new Splat { rt = rt, txt = txt, bg = bg, age = 0f });
    }

    void AnimateSplats()
    {
        for (int i = _splats.Count - 1; i >= 0; i--)
        {
            var s = _splats[i];
            s.age += Time.deltaTime;
            float t = s.age / SPLAT_LIFE;
            s.rt.anchoredPosition += new Vector2(0, SPLAT_RISE * Time.deltaTime);
            float a = t < 0.6f ? 1f : Mathf.Clamp01(1f - (t - 0.6f) / 0.4f);
            var bc = s.bg.color; bc.a = a; s.bg.color = bc;
            var tc = s.txt.color; tc.a = a; s.txt.color = tc;
            if (s.age >= SPLAT_LIFE) { Destroy(s.rt.gameObject); _splats.RemoveAt(i); }
        }
    }

    // ── build ────────────────────────────────────────────────────────────
    void BuildCanvas()
    {
        var canvasGo = new GameObject("CombatFeedbackCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 440;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        _canvasRect = canvasGo.GetComponent<RectTransform>();
    }

    GameObject BuildBar(Color fillColor, out Image fill, out TMP_Text text)
    {
        var bar = new GameObject("HPBar", typeof(RectTransform));
        bar.transform.SetParent(_canvasRect, false);
        var rt = bar.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(80, 12);

        var bgImg = bar.AddComponent<Image>();
        bgImg.color = new Color(0.05f, 0.05f, 0.05f, 0.9f);
        bgImg.raycastTarget = false;

        var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(rt, false);
        var frt = fillGo.GetComponent<RectTransform>();
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(1, 1); frt.offsetMax = new Vector2(-1, -1);
        fill = fillGo.GetComponent<Image>();
        fill.color = fillColor;
        fill.raycastTarget = false;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillAmount = 1f;

        var txtGo = new GameObject("Label", typeof(RectTransform));
        txtGo.transform.SetParent(rt, false);
        var trt = txtGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        text = txtGo.AddComponent<TextMeshProUGUI>();
        text.fontSize = 9; text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;

        return bar;
    }
}
