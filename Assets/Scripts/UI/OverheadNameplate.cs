using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Floating "Name  LvN" label above the player's head. Self-building screen-space label that tracks
/// the player each frame via the camera, so it needs no world-space canvas setup and always faces the
/// screen. Self-added to the player by ActionCombat3D; updates the combat level live as you train.
/// </summary>
public class OverheadNameplate : MonoBehaviour
{
    [Tooltip("Height above the player's root the label floats at (metres).")]
    public float height = 2.4f;

    PlayerEntity _pe;
    Canvas _canvas;
    RectTransform _rt;
    TMP_Text _text;
    int _lastLevel = -1;
    string _lastName = "";

    void Start()
    {
        _pe = GetComponent<PlayerEntity>();
        Build();
    }

    void Build()
    {
        var go = new GameObject("OverheadNameplateCanvas", typeof(Canvas), typeof(CanvasScaler));
        DontDestroyOnLoad(go);
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 400;   // under menus, over the world
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        _rt = labelGo.GetComponent<RectTransform>();
        _rt.sizeDelta = new Vector2(400, 40);
        _text = labelGo.AddComponent<TextMeshProUGUI>();
        _text.fontSize = 22;
        _text.alignment = TextAlignmentOptions.Center;
        _text.raycastTarget = false;
        _text.fontStyle = FontStyles.Bold;
        _text.color = Color.white;
        // Outline so it reads over any background.
        _text.outlineWidth = 0.25f;
        _text.outlineColor = new Color(0f, 0f, 0f, 0.9f);

        RefreshText(true);
    }

    void RefreshText(bool force)
    {
        if (_pe == null || _text == null) return;
        int lvl = _pe.Stats != null ? _pe.Stats.GetCombatLevel() : 1;
        string name = string.IsNullOrEmpty(_pe.PlayerName) ? "Survivor" : _pe.PlayerName;
        if (!force && lvl == _lastLevel && name == _lastName) return;
        _lastLevel = lvl; _lastName = name;
        _text.text = $"{name}  <color=#8FE0FF>Lv{lvl}</color>";
    }

    void LateUpdate()
    {
        if (_text == null) return;
        RefreshText(false);

        var cam = Camera.main;
        if (cam == null) { _text.enabled = false; return; }

        Vector3 world = transform.position + Vector3.up * height;
        Vector3 sp = cam.WorldToScreenPoint(world);
        bool visible = sp.z > 0f;                       // in front of the camera
        if (_text.enabled != visible) _text.enabled = visible;
        if (!visible) return;

        // Screen-overlay canvas uses screen pixels directly.
        _rt.position = sp;
    }

    void OnDestroy() { if (_canvas != null) Destroy(_canvas.gameObject); }
}
