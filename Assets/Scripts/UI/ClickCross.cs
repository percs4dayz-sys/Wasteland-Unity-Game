using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// OSRS click-crosses: press on the world and a little X flashes where you pressed, yellow when you're only
/// walking, red when you're doing something to something (attack, talk, take, chop …). It shrinks away in
/// about half a second. ClickCross.Show(screenPos, red). Self-building and auto-spawned.
/// </summary>
public class ClickCross : MonoBehaviour
{
    static ClickCross _instance;

    static readonly Color Yellow = new Color(1f, 1f, 0f, 1f), Red = new Color(1f, 0.1f, 0.05f, 1f);
    const float Life = 0.45f;

    Canvas _canvas;
    Image[] _pool;
    float[] _born;
    int _next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("ClickCross (auto)");
        _instance = go.AddComponent<ClickCross>();
        DontDestroyOnLoad(go);
    }

    public static void Show(Vector2 screenPos, bool red)
    {
        if (_instance != null) _instance.Spawn(screenPos, red);
    }

    void Awake()
    {
        var canvasGo = new GameObject("ClickCrossCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 455;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var sprite = CrossSprite();
        _pool = new Image[4];
        _born = new float[4];
        for (int i = 0; i < _pool.Length; i++)
        {
            var rt = new GameObject("Cross", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(canvasGo.transform, false);
            rt.sizeDelta = new Vector2(30f, 30f);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite; img.raycastTarget = false;
            rt.gameObject.SetActive(false);
            _pool[i] = img;
        }
    }

    void Spawn(Vector2 screenPos, bool red)
    {
        var img = _pool[_next];
        _born[_next] = Time.unscaledTime;
        _next = (_next + 1) % _pool.Length;
        img.color = red ? Red : Yellow;
        img.rectTransform.position = screenPos;
        img.rectTransform.localScale = Vector3.one;
        img.gameObject.SetActive(true);
    }

    void Update()
    {
        for (int i = 0; i < _pool.Length; i++)
        {
            var img = _pool[i];
            if (!img.gameObject.activeSelf) continue;
            float k = (Time.unscaledTime - _born[i]) / Life;
            if (k >= 1f) { img.gameObject.SetActive(false); continue; }
            // OSRS steps the cross down through a few sizes rather than easing: four frames.
            float step = Mathf.Floor(k * 4f) / 4f;
            img.rectTransform.localScale = Vector3.one * (1f - step * 0.55f);
        }
    }

    /// <summary>A thick X with a dark edge, white so the Image tints it yellow or red.</summary>
    static Sprite CrossSprite()
    {
        const int N = 32;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x + 0.5f, v = y + 0.5f;
                // distance to the two diagonals
                float d = Mathf.Min(Mathf.Abs(u - v), Mathf.Abs(u + v - N)) / 1.4142f;
                bool inBox = u > 4f && u < N - 4f && v > 4f && v < N - 4f;
                if (!inBox) { px[y * N + x] = Color.clear; continue; }
                float fill = Mathf.Clamp01(3.2f - d);          // ~3 px half-width stroke
                float edge = Mathf.Clamp01(4.6f - d);          // dark outline around it
                var c = Color.Lerp(new Color(0f, 0f, 0f, edge), Color.white, fill);
                c.a = Mathf.Max(edge, fill);
                px[y * N + x] = c;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
    }
}
