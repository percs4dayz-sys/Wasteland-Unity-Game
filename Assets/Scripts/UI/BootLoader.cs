using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// First scene in the build: a light screen that shows straight away and says how long start-up takes,
/// while the next scene (character select, with the Sidekick creator behind it) loads in the background.
/// On a phone that load runs a minute or more, and without this the screen just sat black the whole time.
/// Built entirely from code — the Boot scene only holds this component and a camera.
/// </summary>
public class BootLoader : MonoBehaviour
{
    [Tooltip("Key art shown while loading.")]
    public Texture2D art;

    RectTransform _fill;
    TextMeshProUGUI _status;

    IEnumerator Start()
    {
        BuildUI();
        yield return null;   // get the screen up before the heavy load starts competing for the frame

        float started = Time.realtimeSinceStartup;
        var op = SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().buildIndex + 1);
        if (op == null) yield break;
        while (!op.isDone)
        {
            // Progress parks at 0.9 while the scene wakes up; show that stretch as the last bit of the bar.
            float p = Mathf.Clamp01(op.progress / 0.9f);
            _fill.anchorMax = new Vector2(p, 1f);
            int secs = Mathf.FloorToInt(Time.realtimeSinceStartup - started);
            _status.text = $"{Mathf.RoundToInt(p * 100f)}%   ·   {secs / 60}:{secs % 60:00}";
            yield return null;
        }
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("BootCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;   // size by height, so wide phones just get more margin

        // Pure black, so the poster's black rounded corners blend in instead of showing as a box.
        var bg = Rect("Background", canvasGo.transform, Vector2.zero, Vector2.one);
        bg.gameObject.AddComponent<Image>().color = Color.black;

        // Poster down the left, full height.
        if (art != null)
        {
            var poster = Rect("Art", canvasGo.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            poster.pivot = new Vector2(0f, 0.5f);
            float h = 1000f;
            poster.sizeDelta = new Vector2(h * art.width / art.height, h);
            poster.anchoredPosition = new Vector2(60f, 0f);
            poster.gameObject.AddComponent<RawImage>().texture = art;
        }

        // Text column to the right of the art.
        var column = Rect("Column", canvasGo.transform, new Vector2(0f, 0f), new Vector2(1f, 1f));
        column.offsetMin = new Vector2(art != null ? 60f + 1000f * art.width / art.height + 80f : 120f, 0f);
        column.offsetMax = new Vector2(-100f, 0f);

        UITheme.Label(Label(column, "Loading", 64f, UITheme.Amber, 0.62f), 64f, UITheme.Amber);   // the HUD's heading style
        Label(column, "Starting up takes a minute or two on a phone while the world unpacks — hang tight.",
              36f, UITheme.Text, 0.47f);

        var track = Rect("Track", column, new Vector2(0f, 0.34f), new Vector2(1f, 0.34f));
        track.sizeDelta = new Vector2(0f, 18f);
        track.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
        _fill = Rect("Fill", track, Vector2.zero, new Vector2(0f, 1f));
        _fill.gameObject.AddComponent<Image>().color = UITheme.Amber;

        _status = Label(column, "0%", 30f, UITheme.TextDim, 0.27f);
    }

    static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    static TextMeshProUGUI Label(Transform parent, string text, float size, Color color, float y)
    {
        var rt = Rect("Label", parent, new Vector2(0f, y), new Vector2(1f, y));
        rt.sizeDelta = new Vector2(0f, size * 3.2f);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (UITheme.BodyFont != null) t.font = UITheme.BodyFont;
        t.fontSize = size; t.color = color;
        t.alignment = TextAlignmentOptions.Left;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.text = text;
        return t;
    }
}
