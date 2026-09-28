using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Controller-driven sandbox building mode — walk around while placing.
///
/// Toggle with View/Select (Xbox) or F5 (keyboard).
///
/// Controls while active:
///   Left stick          — walk (character moves normally)
///   Right stick         — aim camera (normal)
///   D-pad L/R           — cycle items within current category
///   D-pad U/D           — switch category
///   A                   — place the ghost at its current ground position
///   B / Esc             — exit sandbox mode
///   X                   — rotate ghost -45 deg
///   Y (hold) + RT/LT    — analog yaw (triggers control rotation speed)
///   RB / LB             — rotate ghost +/-15 deg
///   RT                  — raise ghost (hold for continuous lift)
///   LT                  — lower ghost (hold for continuous drop)
///   Q / E (keyboard)    — rotate +/-45 deg
///   R / F (keyboard)    — raise/lower ghost
///
/// Ghost follows 3m in front of wherever you're facing.  Walk around
/// to position it precisely, then tap A to drop the real asset.
///
/// Loads real prefabs from original project folders via AssetDatabase.
/// Hearthcraft deployables use BuildManager.SpawnDeployable.
/// </summary>
public class SandboxBuildManager : MonoBehaviour
{
    public static SandboxBuildManager Instance { get; private set; }
    public static bool IsActive => Instance != null && Instance._active;

    [Header("Toggle")]
    public KeyCode toggleKey = KeyCode.JoystickButton6;  // View/Select
    public KeyCode toggleKeyAlt = KeyCode.F5;

    [Header("Placement")]
    public float placeDistance = 3f;         // metres in front of player
    public float raiseLowerSpeed = 4f;       // metres/sec at full trigger
    public float yawAnalogSpeed = 120f;      // degrees/sec at full trigger
    public float rotationStep = 45f;         // degrees per X press
    public float fineRotationStep = 15f;     // degrees per LB/RB press

    // ── palette ──
    static readonly Color C_Panel = new(0.13f, 0.12f, 0.10f, 0.95f);
    static readonly Color C_Gold  = new(1f, 0.85f, 0.45f, 1f);
    static readonly Color C_Dim   = new(0.5f, 0.5f, 0.5f, 1f);

    // ── state ──
    bool _active;
    float _navTimer;
    const float NavInterval = 0.16f;

    List<CategoryBucket> _categories = new();
    int _catIdx, _itemIdx;

    SandboxAssetEntry _placing;
    GameObject _ghost;
    float _ghostYaw;
    float _ghostHeightOffset;       // accumulated from RT/LT

    Transform _player;
    Dictionary<string, Buildable> _hearthLookup = new();

    // ── UI ──
    GameObject _hudRoot;
    TextMeshProUGUI _catLabel, _itemLabel, _hintLabel;

    // ══════════════════════════════════════════════════════════════════════
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("SandboxBuildManager (auto)");
        go.AddComponent<SandboxBuildManager>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildHearthLookup();
        BuildHUD();
    }

    void Update()
    {
        if (ChatInput.IsTyping) return;

        if (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(toggleKeyAlt))
        {
            Toggle();
            return;
        }

        if (!_active) return;

        FindPlayer();
        if (_player == null) return;

        // ══════════════════════════════════════════════════════════════════
        // D-PAD NAVIGATION (flick-based to avoid overshoot)
        // ══════════════════════════════════════════════════════════════════
        Vector2 dpad = SandboxInputHelper.DPad;

        if (_navTimer <= 0f)
        {
            if (Mathf.Abs(dpad.x) > 0.5f)
            {
                // D-pad L/R: cycle items within current category.
                var cat = _categories[_catIdx];
                if (cat.entries.Count > 0)
                {
                    _itemIdx = (_itemIdx + (dpad.x > 0 ? 1 : -1) + cat.entries.Count) % cat.entries.Count;
                    SelectCurrent();
                }
                _navTimer = NavInterval;
            }
            else if (Mathf.Abs(dpad.y) > 0.5f)
            {
                // D-pad U/D: switch category.
                _catIdx = (_catIdx + (dpad.y > 0 ? 1 : -1) + _categories.Count) % _categories.Count;
                _itemIdx = 0;
                SelectCurrent();
                _navTimer = NavInterval;
            }
        }
        else
        {
            if (Mathf.Abs(dpad.x) < 0.3f && Mathf.Abs(dpad.y) < 0.3f)
                _navTimer -= Time.unscaledDeltaTime;
        }

        // ══════════════════════════════════════════════════════════════════
        // RAISE / LOWER (triggers)
        // ══════════════════════════════════════════════════════════════════
        float rt = SandboxInputHelper.RightTrigger;
        float lt = SandboxInputHelper.LeftTrigger;

        _ghostHeightOffset += (rt - lt) * raiseLowerSpeed * Time.unscaledDeltaTime;
        // Keyboard fallbacks.
        if (Input.GetKey(KeyCode.R)) _ghostHeightOffset += raiseLowerSpeed * Time.unscaledDeltaTime;
        if (Input.GetKey(KeyCode.F)) _ghostHeightOffset -= raiseLowerSpeed * Time.unscaledDeltaTime;

        // ══════════════════════════════════════════════════════════════════
        // ROTATION
        // ══════════════════════════════════════════════════════════════════
        if (_ghost != null)
        {
            // Snap rotation.
            if (Input.GetKeyDown(KeyCode.JoystickButton2)) _ghostYaw -= rotationStep;       // X
            if (Input.GetKeyDown(KeyCode.Q))               _ghostYaw -= rotationStep;
            if (Input.GetKeyDown(KeyCode.E))               _ghostYaw += rotationStep;

            // Fine snap rotation.
            if (Input.GetKeyDown(KeyCode.JoystickButton4)) _ghostYaw -= fineRotationStep;   // LB
            if (Input.GetKeyDown(KeyCode.JoystickButton5)) _ghostYaw += fineRotationStep;   // RB

            // Hold Y + triggers = analog yaw.
            if (Input.GetKey(KeyCode.JoystickButton3))     // Y button held
            {
                float yawInput = rt - lt;  // RT = yaw right, LT = yaw left
                _ghostYaw += yawInput * yawAnalogSpeed * Time.unscaledDeltaTime;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // PLACE / EXIT
        // ══════════════════════════════════════════════════════════════════
        if (Input.GetKeyDown(KeyCode.JoystickButton0))   // A
            PlaceCurrent();

        if (Input.GetKeyDown(KeyCode.JoystickButton1) || Input.GetKeyDown(KeyCode.Escape))   // B / Esc
            Toggle();

        UpdateGhost();
    }

    // ══════════════════════════════════════════════════════════════════════
    // TOGGLE
    // ══════════════════════════════════════════════════════════════════════
    void Toggle()
    {
        _active = !_active;
        if (_hudRoot) _hudRoot.SetActive(_active);

        if (_active)
        {
            SandboxAssetScanner.InvalidateCache();
            BuildCategories();
            FindPlayer();
            _catIdx = 0; _itemIdx = 0;
            _ghostHeightOffset = 0f;
            SelectCurrent();

            int total = 0;
            foreach (var c in _categories) total += c.entries.Count;
            HUDController.Emit(
                $"<color=#9FE0C0>[SANDBOX]:</color> ON — {total} assets in {_categories.Count} categories. " +
                "Walk around, D-pad to browse, A to place.");
        }
        else
        {
            CancelPlacement();
            HUDController.Emit("<color=#9FE0C0>[SANDBOX]:</color> OFF.");
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // SELECTION
    // ══════════════════════════════════════════════════════════════════════
    void SelectCurrent()
    {
        if (_categories.Count == 0) return;
        var cat = _categories[_catIdx];
        if (cat.entries.Count == 0) return;
        StartPlacement(cat.entries[_itemIdx]);
        RefreshHUD();
    }

    void StartPlacement(SandboxAssetEntry entry)
    {
        if (_placing == entry) return;

        _placing = entry;

        if (_ghost != null) Destroy(_ghost);

        if (entry.isHearthcraft && _hearthLookup.TryGetValue(entry.name, out var b))
        {
            var kind = b.kind == BuildKind.Turret ? PrimitiveType.Cylinder : PrimitiveType.Cube;
            _ghost = GameObject.CreatePrimitive(kind);
            _ghost.name = "SandboxGhost";
            _ghost.transform.localScale = b.size;
            Destroy(_ghost.GetComponent<Collider>());
            SetGhostMaterial(_ghost);
            return;
        }

        var prefab = entry.LoadPrefab();
        if (prefab == null)
        {
            Debug.LogWarning($"[Sandbox] Could not load '{entry.assetPath}'");
            return;
        }

        _ghost = Instantiate(prefab);
        _ghost.name = "SandboxGhost";
        foreach (var r in _ghost.GetComponentsInChildren<Renderer>())
            SetGhostMaterial(r.gameObject);
        foreach (var col in _ghost.GetComponentsInChildren<Collider>())
            Destroy(col);
    }

    static void SetGhostMaterial(GameObject go)
    {
        var r = go.GetComponent<Renderer>();
        if (r == null || r.sharedMaterial == null) return;
        var mat = new Material(r.sharedMaterial);
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        mat.shader = shader;
        mat.color = new Color(0.35f, 1f, 0.45f, 0.4f);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", new Color(0.35f, 1f, 0.45f, 0.4f));
        if (mat.HasProperty("_Surface"))   mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend"))     mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_ZWrite"))    mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_SrcBlend"))
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend"))
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        r.sharedMaterial = mat;
    }

    void CancelPlacement()
    {
        _placing = null;
        if (_ghost != null) { Destroy(_ghost); _ghost = null; }
    }

    // ══════════════════════════════════════════════════════════════════════
    // PLACEMENT
    // ══════════════════════════════════════════════════════════════════════
    void PlaceCurrent()
    {
        if (_placing == null || _ghost == null || _player == null) return;

        var pos = _ghost.transform.position;
        var rot = _ghost.transform.rotation;

        if (_placing.isHearthcraft &&
            _hearthLookup.TryGetValue(_placing.name, out var buildable))
        {
            BuildManager.SpawnDeployable(buildable, pos, rot);
        }
        else
        {
            var go = _placing.Instantiate(pos, rot);
            if (go != null)
            {
                go.transform.position = pos;
                go.transform.rotation = rot;
            }
        }

        HUDController.Emit(
            $"<color=#9FE0C0>[SANDBOX]:</color> Placed <color=#FFD24A>{_placing.name}</color>.");
    }

    void UpdateGhost()
    {
        if (_ghost == null || _player == null) return;

        // Ground point in front of the player + height offset.
        Vector3 origin = _player.position + _player.forward * placeDistance + Vector3.up * 2f;

        if (Physics.Raycast(origin, Vector3.down, out var hit, 10f))
            _ghost.transform.position = hit.point + Vector3.up * _ghostHeightOffset;
        else
        {
            var pos = _player.position + _player.forward * placeDistance;
            pos.y = _player.position.y + _ghostHeightOffset;
            _ghost.transform.position = pos;
        }

        _ghost.transform.rotation = Quaternion.Euler(0f, _ghostYaw, 0f);
        _ghost.SetActive(true);
    }

    // ══════════════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════════════
    void FindPlayer()
    {
        if (_player != null) return;
        var pe = PlayerEntity.Instance;
        if (pe != null) _player = pe.transform;
    }

    void BuildCategories()
    {
        _categories.Clear();
        foreach (var entry in SandboxAssetScanner.BuildCatalog())
        {
            var bucket = FindOrCreateBucket(entry.category);
            bucket.entries.Add(entry);
        }
    }

    CategoryBucket FindOrCreateBucket(string catName)
    {
        foreach (var b in _categories)
            if (b.name == catName) return b;
        var nb = new CategoryBucket { name = catName };
        _categories.Add(nb);
        return nb;
    }

    void BuildHearthLookup()
    {
        _hearthLookup.Clear();
        foreach (var b in BuildCatalog.All())
            _hearthLookup[b.name] = b;
    }

    // ══════════════════════════════════════════════════════════════════════
    // HUD
    // ══════════════════════════════════════════════════════════════════════
    void BuildHUD()
    {
        var canvasGo = new GameObject("SandboxHUDCanvas",
            typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.GetComponent<Canvas>().sortingOrder = 490;
        var scaler = canvasGo.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var barRt = NewUI("Bar", canvasGo.transform);
        barRt.anchorMin = new Vector2(0.5f, 0f);
        barRt.anchorMax = new Vector2(0.5f, 0f);
        barRt.pivot = new Vector2(0.5f, 0f);
        barRt.sizeDelta = new Vector2(940, 72);
        barRt.anchoredPosition = new Vector2(0, 10);
        barRt.gameObject.AddComponent<UnityEngine.UI.Image>().color = C_Panel;
        _hudRoot = barRt.gameObject;

        // Category (left)
        var catRt = NewUI("CatLabel", barRt);
        catRt.anchorMin = new Vector2(0.03f, 0.1f);
        catRt.anchorMax = new Vector2(0.28f, 0.9f);
        catRt.offsetMin = Vector2.zero; catRt.offsetMax = Vector2.zero;
        _catLabel = catRt.gameObject.AddComponent<TextMeshProUGUI>();
        _catLabel.fontSize = 15;
        _catLabel.color = C_Dim;
        _catLabel.alignment = TextAlignmentOptions.MidlineLeft;
        _catLabel.raycastTarget = false;

        // Item (center)
        var itemRt = NewUI("ItemLabel", barRt);
        itemRt.anchorMin = new Vector2(0.30f, 0.1f);
        itemRt.anchorMax = new Vector2(0.62f, 0.9f);
        itemRt.offsetMin = Vector2.zero; itemRt.offsetMax = Vector2.zero;
        _itemLabel = itemRt.gameObject.AddComponent<TextMeshProUGUI>();
        _itemLabel.fontSize = 19;
        _itemLabel.color = C_Gold;
        _itemLabel.fontStyle = FontStyles.Bold;
        _itemLabel.alignment = TextAlignmentOptions.Center;
        _itemLabel.raycastTarget = false;

        // Hints (right)
        var hintRt = NewUI("HintLabel", barRt);
        hintRt.anchorMin = new Vector2(0.64f, 0.1f);
        hintRt.anchorMax = new Vector2(0.97f, 0.9f);
        hintRt.offsetMin = Vector2.zero; hintRt.offsetMax = Vector2.zero;
        _hintLabel = hintRt.gameObject.AddComponent<TextMeshProUGUI>();
        _hintLabel.fontSize = 12;
        _hintLabel.color = C_Dim;
        _hintLabel.alignment = TextAlignmentOptions.MidlineRight;
        _hintLabel.raycastTarget = false;
        _hintLabel.text = "^v cat  <-> item\n[A] place  [B] exit\n[X] rot  [LB/RB] fine\n[Y+RT/LT] yaw  [RT/LT] up/dn";

        _hudRoot.SetActive(false);
    }

    void RefreshHUD()
    {
        if (_hudRoot == null || _categories.Count == 0) return;

        var cat = _categories[_catIdx];
        var entry = cat.entries.Count > 0 ? cat.entries[_itemIdx] : null;
        int totalCats = _categories.Count;
        int totalItems = cat.entries.Count;

        _catLabel.text =
            $"<color=#FFD24A>  {cat.name}  </color> ({_catIdx + 1}/{totalCats})";
        _itemLabel.text = entry != null
            ? $"  {entry.name}  ({_itemIdx + 1}/{totalItems})"
            : "(empty)";
    }

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    class CategoryBucket
    {
        public string name;
        public List<SandboxAssetEntry> entries = new();
    }
}
