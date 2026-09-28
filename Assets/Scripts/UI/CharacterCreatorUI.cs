using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Synty.SidekickCharacters.API;
using Synty.SidekickCharacters.Database;
using Synty.SidekickCharacters.Database.DTO;
using Synty.SidekickCharacters.Enums;

/// <summary>
/// Face-and-body character creator. Builds a live Sidekick character you can actually see while you
/// cycle each feature, then saves the choice for the world to load.
///
/// DELIBERATELY NO GEAR. Every armour piece is a drop, so the creator only ever offers
/// SK_HUMN_BASE parts — enforced in CharacterCreatorConfig, not here, so a careless change to this
/// UI can't leak endgame armour into character creation. New characters wear the fixed starter
/// outfit and earn everything else.
///
/// Self-bootstrapping in the CharacterSelect scene, matching CharacterSelectUI. Attaches itself to
/// a panel you open from that screen; call <see cref="Open"/> to show it.
/// </summary>
public class CharacterCreatorUI : MonoBehaviour
{
    public static CharacterCreatorUI Instance { get; private set; }

    const string BaseModelResource = "Meshes/SK_BaseModel";
    const string BaseMaterialResource = "Materials/M_BaseMaterial";
    const string PreviewName = "CreatorPreview";

    // Feature rows shown to the player, in this order. Body build is one row rather than twelve —
    // nobody wants to pick a left forearm.
    static readonly (string label, CharacterPartType[] types)[] Rows =
    {
        ("Head",        new[] { CharacterPartType.Head }),
        ("Hair",        new[] { CharacterPartType.Hair }),
        ("Eyebrows",    new[] { CharacterPartType.EyebrowLeft, CharacterPartType.EyebrowRight }),
        ("Eyes",        new[] { CharacterPartType.EyeLeft, CharacterPartType.EyeRight }),
        ("Ears",        new[] { CharacterPartType.EarLeft, CharacterPartType.EarRight }),
        ("Facial hair", new[] { CharacterPartType.FacialHair }),
        ("Nose",        new[] { CharacterPartType.Nose }),
        ("Build",       new[]
        {
            CharacterPartType.Torso,
            CharacterPartType.ArmUpperLeft, CharacterPartType.ArmUpperRight,
            CharacterPartType.ArmLowerLeft, CharacterPartType.ArmLowerRight,
            CharacterPartType.HandLeft,     CharacterPartType.HandRight,
            CharacterPartType.Hips,
            CharacterPartType.LegLeft,      CharacterPartType.LegRight,
            CharacterPartType.FootLeft,     CharacterPartType.FootRight,
        }),
    };

    SidekickRuntime _runtime;
    DatabaseManager _db;
    Dictionary<CharacterPartType, Dictionary<string, SidekickPart>> _library;

    /// <summary>Options per part type, base parts only, in a stable order so indices mean something.</summary>
    readonly Dictionary<CharacterPartType, List<string>> _options = new();

    /// <summary>Current choice index per ROW (a row may drive several part types together).</summary>
    readonly int[] _rowIndex = new int[Rows.Length];

    /// <summary>Rows hidden from the UI but STILL SELECTED behind the scenes. Deleting a row from
    /// <see cref="Rows"/> would drop its part types out of CurrentSelection entirely — for "Head" that
    /// means a character built with no head mesh, not a character with a fixed head. Hide, don't delete.</summary>
    static readonly HashSet<string> HiddenRows = new() { "Head" };

    readonly GameObject[] _rowObjects = new GameObject[Rows.Length];
    GameObject[] _buttonBlock;
    float _rowStartY;

    // ── body build and colour ────────────────────────────────────────────
    //
    // These aren't part choices, so they can't ride in the Rows array: build is blend shapes and
    // colour is pixels in the shared material. They get their own rows, appended after the mesh ones.

    /// <summary>A row driven by something other than a part list.</summary>
    class ExtraRow
    {
        public string Label;
        public Func<int> Count;
        public Action<int> Step;
        public GameObject Go;
    }

    readonly List<ExtraRow> _extraRows = new();

    readonly CharacterAppearance.Look _look = new();
    List<SidekickBodyShapePreset> _bodyShapes = new();
    int _bodyIndex, _hairColorIndex, _facialHairColorIndex, _browColorIndex, _skinColorIndex;

    GameObject _preview;
    GameObject _panel;
    TextMeshProUGUI _status;
    TMP_InputField _nameField;
    Action<Dictionary<CharacterPartType, string>> _onConfirm;
    bool _ready;

    /// <summary>Show the creator. <paramref name="onConfirm"/> receives the chosen parts.</summary>
    public static void Open(Action<Dictionary<CharacterPartType, string>> onConfirm)
    {
        if (Instance == null)
            Instance = new GameObject("CharacterCreatorUI (auto)").AddComponent<CharacterCreatorUI>();
        Instance._onConfirm = onConfirm;
        Instance.gameObject.SetActive(true);
        if (Instance._panel != null) Instance._panel.SetActive(true);
    }

    async void Start()
    {
        BuildUI();
        SetStatus("Loading character parts…");

        var model = Resources.Load<GameObject>(BaseModelResource);
        var material = Resources.Load<Material>(BaseMaterialResource);
        if (model == null || material == null)
        {
            SetStatus("Character parts missing — check Sidekick is installed.");
            Debug.LogError($"[CharacterCreator] Missing '{BaseModelResource}' or '{BaseMaterialResource}'.");
            return;
        }

        _db = new DatabaseManager();
        _runtime = new SidekickRuntime(model, material, null, _db);

        try
        {
            await SidekickRuntime.PopulateToolData(_runtime);
        }
        catch (Exception ex)
        {
            SetStatus("Failed to load character parts. See the log.");
            Debug.LogError($"[CharacterCreator] {ex}");
            return;
        }

        _library = _runtime.MappedPartDictionary;
        _bodyShapes = SidekickLook.BodyShapes(_db);
        BuildOptions();

        _ready = true;
        SetStatus("");
        Randomise();      // start on a complete character rather than an empty rig
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ReleaseCameras();   // never leave the scene with every camera switched off
    }

    // ── options ──────────────────────────────────────────────────────────

    /// <summary>Collect the selectable parts per type. The gear filter lives in
    /// CharacterCreatorConfig so this can't drift from the rule.</summary>
    void BuildOptions()
    {
        _options.Clear();
        foreach (var type in CharacterCreatorConfig.AllSelectableSlots())
        {
            if (_library == null || !_library.TryGetValue(type, out var parts) || parts == null) continue;

            var usable = parts.Keys
                .Where(CharacterCreatorConfig.IsSelectable)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            if (usable.Count > 0) _options[type] = usable;
        }

        int total = _options.Values.Sum(v => v.Count);
        Debug.Log($"[CharacterCreator] {total} selectable parts across {_options.Count} slots " +
                  "(gear excluded).");

        LayoutRows();
    }

    /// <summary>Hide any row that can't actually change anything, and close the gap it leaves.
    ///
    /// A row's choice count is the SMALLEST count across the part types it drives, so a row is dead the
    /// moment one of its types ships a single mesh. In SK_HUMN_BASE that kills "Eyes" (EyeLeft and
    /// EyeRight have exactly one mesh each) and "Build" (all twelve body types have exactly one). Showing
    /// arrows that provably do nothing is worse than showing no row.</summary>
    void LayoutRows()
    {
        float y = _rowStartY;
        for (int r = 0; r < Rows.Length; r++)
        {
            var go = _rowObjects[r];
            if (go == null) continue;

            bool usable = OptionCount(r) > 1 && !HiddenRows.Contains(Rows[r].label);
            go.SetActive(usable);
            if (!usable) continue;

            var rt = (RectTransform) go.transform;
            rt.anchoredPosition = new Vector2(0f, y);
            y -= 44f;
        }

        foreach (var extra in _extraRows)
        {
            if (extra.Go == null) continue;

            bool usable = extra.Count() > 1;
            extra.Go.SetActive(usable);
            if (!usable) continue;

            ((RectTransform) extra.Go.transform).anchoredPosition = new Vector2(0f, y);
            y -= 44f;
        }

        // The buttons were laid out assuming every row was visible; drop them to the real end of the list.
        if (_buttonBlock != null)
        {
            float by = y - 16f;
            foreach (var b in _buttonBlock)
            {
                if (b == null) continue;
                ((RectTransform) b.transform).anchoredPosition = new Vector2(0f, by);
                by -= 48f;
            }
        }
    }

    /// <summary>How many choices a row offers — the smallest count across the types it drives, so
    /// stepping the row never runs off the end of one of them.</summary>
    int OptionCount(int row)
    {
        int min = int.MaxValue;
        foreach (var t in Rows[row].types)
            if (_options.TryGetValue(t, out var list)) min = Mathf.Min(min, list.Count);
        return min == int.MaxValue ? 0 : min;
    }

    void Step(int row, int delta)
    {
        int count = OptionCount(row);
        if (count <= 0) return;
        _rowIndex[row] = ((_rowIndex[row] + delta) % count + count) % count;
        _dirty = true;   // coalesced in Update — see RebuildInterval
    }

    // Rebuilding a character instantiates a whole skeleton and mesh set. Doing that synchronously on
    // every click means holding several full characters at once while Destroy() catches up, and it
    // ran the editor out of memory. Clicks now set a flag and at most one rebuild happens per
    // interval, however fast you scroll.
    const float RebuildInterval = 0.12f;
    bool _dirty;
    float _nextRebuild;
    int _rebuildsSinceUnload;

    void Update()
    {
        if (!_ready || !_dirty || Time.unscaledTime < _nextRebuild) return;
        _dirty = false;
        _nextRebuild = Time.unscaledTime + RebuildInterval;
        RefreshPreview();
    }

    void Randomise()
    {
        for (int r = 0; r < Rows.Length; r++)
        {
            int count = OptionCount(r);
            _rowIndex[r] = count > 0 ? UnityEngine.Random.Range(0, count) : 0;
        }

        if (_bodyShapes.Count > 0) _bodyIndex = UnityEngine.Random.Range(0, _bodyShapes.Count);
        _hairColorIndex       = UnityEngine.Random.Range(0, SidekickLook.HairPalette.Length);
        _facialHairColorIndex = _hairColorIndex;   // a beard that matches the hair reads as deliberate
        _browColorIndex       = _hairColorIndex;
        _skinColorIndex       = UnityEngine.Random.Range(0, SidekickLook.SkinPalette.Length);

        SyncLook();
        RefreshPreview();
    }

    /// <summary>Copy the current row indices into <see cref="_look"/>. Body build must be on the runtime
    /// before the next rebuild; colours apply straight to the material and need no rebuild.</summary>
    void SyncLook()
    {
        if (_bodyShapes.Count > 0)
            SidekickLook.SetFromPreset(_look, _bodyShapes[Mathf.Clamp(_bodyIndex, 0, _bodyShapes.Count - 1)]);

        _look.HairColor       = SidekickLook.HairPalette[Mathf.Clamp(_hairColorIndex, 0, SidekickLook.HairPalette.Length - 1)];
        _look.FacialHairColor = SidekickLook.HairPalette[Mathf.Clamp(_facialHairColorIndex, 0, SidekickLook.HairPalette.Length - 1)];
        _look.BrowColor       = SidekickLook.HairPalette[Mathf.Clamp(_browColorIndex, 0, SidekickLook.HairPalette.Length - 1)];
        _look.SkinColor       = SidekickLook.SkinPalette[Mathf.Clamp(_skinColorIndex, 0, SidekickLook.SkinPalette.Length - 1)];

        SidekickLook.ApplyBody(_runtime, _look);
        SidekickLook.ApplyColors(_runtime, _db, _look);
    }

    /// <summary>Build the non-part rows. Colour steps repaint the material directly — no rebuild, so
    /// clicking through shades is instant instead of regenerating the whole character each press.</summary>
    void BuildExtraRows()
    {
        _extraRows.Clear();

        _extraRows.Add(new ExtraRow
        {
            Label = "Build",
            Count = () => _bodyShapes.Count,
            Step  = d => { _bodyIndex = Wrap(_bodyIndex + d, _bodyShapes.Count); SyncLook(); RefreshPreview(); },
        });

        AddColorRow("Hair colour",   SidekickLook.HairPalette.Length, d => _hairColorIndex       = Wrap(_hairColorIndex + d,       SidekickLook.HairPalette.Length));
        AddColorRow("Beard colour",  SidekickLook.HairPalette.Length, d => _facialHairColorIndex = Wrap(_facialHairColorIndex + d, SidekickLook.HairPalette.Length));
        AddColorRow("Brow colour",   SidekickLook.HairPalette.Length, d => _browColorIndex       = Wrap(_browColorIndex + d,       SidekickLook.HairPalette.Length));
        AddColorRow("Skin tone",     SidekickLook.SkinPalette.Length, d => _skinColorIndex       = Wrap(_skinColorIndex + d,       SidekickLook.SkinPalette.Length));
    }

    void AddColorRow(string label, int count, Action<int> move)
    {
        _extraRows.Add(new ExtraRow
        {
            Label = label,
            Count = () => count,
            Step  = d => { move(d); SyncLook(); },   // material repaint only — no CreateCharacter
        });
    }

    static int Wrap(int i, int count) => count <= 0 ? 0 : ((i % count) + count) % count;

    /// <summary>The chosen part for every part type the creator controls.</summary>
    Dictionary<CharacterPartType, string> CurrentSelection()
    {
        var result = new Dictionary<CharacterPartType, string>();
        for (int r = 0; r < Rows.Length; r++)
        {
            foreach (var type in Rows[r].types)
            {
                if (!_options.TryGetValue(type, out var list) || list.Count == 0) continue;
                result[type] = list[Mathf.Clamp(_rowIndex[r], 0, list.Count - 1)];
            }
        }
        return result;
    }

    // ── preview ──────────────────────────────────────────────────────────

    void RefreshPreview()
    {
        if (!_ready) return;

        // Blend weights are read during CreateCharacter, so they have to be on the runtime first.
        SidekickLook.ApplyBody(_runtime, _look);

        var renderers = new List<SkinnedMeshRenderer>();
        foreach (var kv in CurrentSelection())
        {
            if (!_library.TryGetValue(kv.Key, out var parts)) continue;
            if (!parts.TryGetValue(kv.Value, out var part) || part == null) continue;
            var container = part.GetPartModel();
            var smr = container != null ? container.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (smr != null) renderers.Add(smr);
        }

        if (renderers.Count == 0) { SetStatus("No parts to preview."); return; }

        // Destroy the old preview BEFORE building the new one. Passing the old model as
        // `existingModel` does not reliably replace it — Synty's own demo destroys first, and
        // without this every arrow click leaves another character behind in the scene.
        //
        // DestroyImmediate, not Destroy: deferred destruction means the old character is still
        // resident while the new one is built, so fast scrolling stacks several full skeletons and
        // mesh sets in memory at once. That is what exhausted the editor's memory.
        //
        // NOTE: only the GameObject is destroyed. The meshes hang off Resources-loaded prefabs
        // shared with the project — destroying those would damage the actual assets on disk.
        // Freeing them is Resources.UnloadUnusedAssets's job, below.
        if (_preview != null) { DestroyImmediate(_preview); _preview = null; }
        for (var stale = GameObject.Find(PreviewName); stale != null; stale = GameObject.Find(PreviewName))
            DestroyImmediate(stale);   // sweep any left by an earlier run

        _preview = _runtime.CreateCharacter(PreviewName, renderers, false, true);
        if (_preview != null)
        {
            _preview.name = PreviewName;
            _preview.transform.position = PreviewSpot;
            // The rig looks down +Z and FrameStage puts the camera at +Z looking back at it, so the two
            // already face each other — identity is correct. The old 180° here spun it away from the
            // camera and showed you its back.
            _preview.transform.rotation = Quaternion.identity;
            foreach (var smr in _preview.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.updateWhenOffscreen = true;
        }

        FrameStage();

        // Every part ever previewed stays resident via Resources.Load, and each rebuild leaves
        // orphaned instances behind. Without an unload, browsing the part list is a slow memory
        // climb that ends in "Could not allocate memory". Not every frame — the call is expensive.
        if (++_rebuildsSinceUnload >= 12)
        {
            _rebuildsSinceUnload = 0;
            Resources.UnloadUnusedAssets();
        }
    }

    /// <summary>Fraction of the character's height the camera frames. ~0.35 is head and shoulders;
    /// raise toward 1.0 to pull back to the full figure.</summary>
    const float PortraitFraction = 0.35f;

    /// <summary>Where the preview character stands. Away from the origin so it can't end up inside
    /// whatever else the scene has parked there.</summary>
    static readonly Vector3 PreviewSpot = new Vector3(0f, 0f, 0f);

    /// <summary>Point a camera at the preview and make sure there's a light on it. The
    /// CharacterSelect scene is essentially empty, so without this the character is built correctly
    /// and you see nothing — or a black silhouette.</summary>
    void FrameStage()
    {
        // OWN the camera outright. Positioning whatever camera happened to be around kept producing a
        // correct distance on paper and a far-away view on screen, so the creator now renders through a
        // camera it created, parented to nothing, at a depth nothing else beats — and every other camera
        // is switched off for the duration. Restored in ReleaseCameras().
        if (_cam == null)
        {
            var camGo = new GameObject("CreatorCamera") { tag = "MainCamera" };
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.Skybox;
        }
        _cam.depth = 100f;            // draw on top of anything else that slips through
        _cam.enabled = true;
        Camera cam = _cam;

        foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
        {
            if (c == _cam || !c.enabled) continue;
            c.enabled = false;
            if (!_suppressedCameras.Contains(c)) _suppressedCameras.Add(c);
        }

        // An orbit rig would fight us for the transform every frame — it has no player to follow
        // on this screen anyway. Disable EVERY one in the scene, not just this camera's.
        foreach (var orbit in FindObjectsByType<OrbitCamera3D>(FindObjectsInactive.Exclude))
            orbit.enabled = false;

        cam.nearClipPlane = 0.05f;

        // Frame from the model's real bounds rather than assumed proportions — guessing an offset
        // is how you end up with a character floating far away or cropped at the neck.
        Bounds b = ModelBounds();
        float height = Mathf.Max(b.size.y, 0.5f);

        // Frame HEAD AND SHOULDERS, not the whole figure. Seven of the eight rows in this creator are
        // face features — head, hair, eyebrows, eyes, ears, facial hair, nose — and you cannot judge a
        // nose on a 1.8 m figure framed from four metres away. Only "Build" wants the full body.
        float framed = Mathf.Max(height * PortraitFraction, 0.25f);
        float dist = framed * 1.15f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);

        // Centre the shot just below the crown so the head sits in the upper third.
        Vector3 lookAt = new Vector3(b.center.x, b.max.y - framed * 0.5f, b.center.z);

        // The UI panel occupies the left ~28% of the screen, so nudge the subject right of centre.
        _camPos    = lookAt + new Vector3(framed * 0.18f, framed * 0.05f, dist);
        _camLookAt = lookAt;
        _camFramed = true;

        cam.transform.position = _camPos;
        cam.transform.LookAt(_camLookAt);

        if (_light == null && FindAnyObjectByType<Light>() == null)
        {
            var lightGo = new GameObject("CreatorLight");
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Directional;
            _light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(35f, 200f, 0f);
        }
    }

    Light _light;

    Camera _cam;
    Vector3 _camPos, _camLookAt;
    bool _camFramed;
    readonly List<Camera> _suppressedCameras = new();

    /// <summary>Hold the framing every frame. Something in this project re-parks the camera after
    /// FrameStage runs — the computed distance was correct while the view stayed wide — so re-assert
    /// it in LateUpdate, which runs after the Update-driven movers.</summary>
    void LateUpdate()
    {
        if (!_camFramed || _cam == null) return;
        if (_panel != null && !_panel.activeInHierarchy) return;

        _cam.transform.position = _camPos;
        _cam.transform.LookAt(_camLookAt);
    }

    /// <summary>Give the other cameras back when the creator closes, or the world loads into a black
    /// screen with every camera switched off.</summary>
    void ReleaseCameras()
    {
        _camFramed = false;
        foreach (var c in _suppressedCameras)
            if (c != null) c.enabled = true;
        _suppressedCameras.Clear();

        if (_cam != null) { Destroy(_cam.gameObject); _cam = null; }
    }

    /// <summary>World bounds of the preview's renderers, or a human-sized default if it isn't
    /// built yet. Skinned meshes need updateWhenOffscreen for these bounds to be trustworthy,
    /// which RefreshPreview sets.</summary>
    Bounds ModelBounds()
    {
        var fallback = new Bounds(PreviewSpot + Vector3.up * 0.9f, new Vector3(0.6f, 1.8f, 0.6f));
        if (_preview == null) return fallback;

        var rends = _preview.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return fallback;

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b.size.y < 0.2f ? fallback : b;   // degenerate → don't zoom into nothing
    }

    void Confirm()
    {
        string name = _nameField != null ? _nameField.text.Trim() : "";
        if (string.IsNullOrEmpty(name)) name = "Survivor";
        PlayerPrefs.SetString("PlayerName", name);
        PlayerPrefs.Save();

        var chosen = CurrentSelection();
        CharacterAppearance.Save(chosen);
        CharacterAppearance.SaveLook(_look);   // build + colours live outside the part list

        // Freeze the finished character while Sidekick is still loaded — this is the last moment it
        // exists. Blend shape weights and the painted colour map are captured as plain Unity data,
        // so the world can rebuild the character with no database, no part catalogue and no runtime
        // API. Sidekick is used to design the character, once, and then never again.
        // The colours live on the runtime's own material (Sidekick paints them there), not the asset.
        var painted = _runtime != null && _runtime.CurrentMaterial != null ? _runtime.CurrentMaterial
                                                                           : Resources.Load<Material>(BaseMaterialResource);
        CharacterBake.Capture(_preview, painted);

        // The preview is a creator-only object; the world builds its own from the saved appearance.
        if (_preview != null) Destroy(_preview);
        ReleaseCameras();   // hand the scene's cameras back before the world loads

        _onConfirm?.Invoke(chosen);
        if (_panel != null) _panel.SetActive(false);
    }

    // ── UI ───────────────────────────────────────────────────────────────

    void BuildUI()
    {
        var canvasGo = new GameObject("CreatorCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Must beat CharacterSelectUI's canvas (sortingOrder 1000, fullscreen opaque background),
        // or the creator builds correctly and renders completely behind it — looking, from the
        // outside, exactly like the Start button did nothing.
        canvas.sortingOrder = 2000;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        _panel = new GameObject("Panel");
        _panel.transform.SetParent(canvasGo.transform, false);
        var panelRt = _panel.AddComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0f, 0f);
        panelRt.anchorMax = new Vector2(0.28f, 1f);
        panelRt.offsetMin = panelRt.offsetMax = Vector2.zero;
        var bg = _panel.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.06f, 0.85f);

        float y = -30f;
        MakeLabel("CREATE YOUR SURVIVOR", 22, y, panelRt); y -= 42f;

        MakeLabel("Name", 15, y, panelRt); y -= 30f;
        _nameField = MakeNameField(y, panelRt); y -= 52f;

        for (int r = 0; r < Rows.Length; r++)
        {
            int row = r;   // capture
            _rowObjects[r] = MakeRow(Rows[r].label, y, panelRt, () => Step(row, -1), () => Step(row, +1));
            y -= 44f;
        }

        BuildExtraRows();
        foreach (var extra in _extraRows)
        {
            var e = extra;   // capture
            e.Go = MakeRow(e.Label, y, panelRt, () => e.Step(-1), () => e.Step(+1));
            y -= 44f;
        }

        _rowStartY = -30f - 42f - 30f - 52f;   // matches the y walk above, for LayoutRows()

        y -= 16f;
        // Kept so LayoutRows can slide them up once the dead rows are hidden.
        _buttonBlock = new[]
        {
            MakeButton("Randomise", y, panelRt, Randomise),
            MakeButton("Confirm", y - 48f, panelRt, Confirm),
        };
        y -= 96f;

        var statusGo = new GameObject("Status");
        statusGo.transform.SetParent(panelRt, false);
        var srt = statusGo.AddComponent<RectTransform>();
        srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(1f, 0f);
        srt.pivot = new Vector2(0.5f, 0f);
        srt.anchoredPosition = new Vector2(0f, 20f);
        srt.sizeDelta = new Vector2(-20f, 60f);
        _status = statusGo.AddComponent<TextMeshProUGUI>();
        _status.fontSize = 15;
        _status.alignment = TextAlignmentOptions.Center;
        _status.color = new Color(1f, 0.85f, 0.5f);
    }

    /// <summary>Name entry, here rather than on the welcome screen so you name the character while
    /// looking at it.</summary>
    TMP_InputField MakeNameField(float y, RectTransform parent)
    {
        var go = new GameObject("NameField");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(-40f, 42f);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.12f, 0.12f, 0.14f, 1f);

        var area = new GameObject("TextArea");
        area.transform.SetParent(rt, false);
        var art = area.AddComponent<RectTransform>();
        art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(10f, 4f); art.offsetMax = new Vector2(-10f, -4f);
        area.AddComponent<RectMask2D>();

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(art, false);
        var trt = textGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.fontSize = 17;
        text.alignment = TextAlignmentOptions.Left;

        var field = go.AddComponent<TMP_InputField>();
        field.targetGraphic = img;
        field.textViewport = art;
        field.textComponent = text;
        field.characterLimit = 16;
        field.text = PlayerPrefs.GetString("PlayerName", "Survivor");
        return field;
    }

    GameObject MakeRow(string label, float y, RectTransform parent, Action back, Action fwd)
    {
        var go = new GameObject(label);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(-24f, 38f);

        var txtGo = new GameObject("Label");
        txtGo.transform.SetParent(rt, false);
        var trt = txtGo.AddComponent<RectTransform>();
        trt.anchorMin = new Vector2(0f, 0f); trt.anchorMax = new Vector2(1f, 1f);
        trt.offsetMin = new Vector2(56f, 0f); trt.offsetMax = new Vector2(-56f, 0f);
        var txt = txtGo.AddComponent<TextMeshProUGUI>();
        txt.text = label;
        txt.fontSize = 17;
        txt.alignment = TextAlignmentOptions.Center;

        MakeArrow(rt, "<", true, back);
        MakeArrow(rt, ">", false, fwd);
        return go;
    }

    void MakeArrow(RectTransform parent, string glyph, bool left, Action onClick)
    {
        var go = new GameObject(left ? "Back" : "Fwd");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(left ? 0f : 1f, 0.5f);
        rt.anchorMax = rt.anchorMin;
        rt.pivot = new Vector2(left ? 0f : 1f, 0.5f);
        rt.sizeDelta = new Vector2(46f, 34f);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.22f, 0.24f, 0.28f, 1f);
        var btn = go.AddComponent<Button>();
        btn.onClick.AddListener(() => onClick());

        var t = new GameObject("T");
        t.transform.SetParent(rt, false);
        var trt = t.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        var txt = t.AddComponent<TextMeshProUGUI>();
        txt.text = glyph;
        txt.fontSize = 20;
        txt.alignment = TextAlignmentOptions.Center;
    }

    GameObject MakeButton(string label, float y, RectTransform parent, Action onClick)
    {
        var go = new GameObject(label);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(-40f, 40f);

        go.AddComponent<Image>().color = new Color(0.20f, 0.35f, 0.24f, 1f);
        go.AddComponent<Button>().onClick.AddListener(() => onClick());

        var t = new GameObject("T");
        t.transform.SetParent(rt, false);
        var trt = t.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        var txt = t.AddComponent<TextMeshProUGUI>();
        txt.text = label;
        txt.fontSize = 18;
        txt.alignment = TextAlignmentOptions.Center;
        return go;
    }

    void MakeLabel(string text, int size, float y, RectTransform parent)
    {
        var go = new GameObject("Title");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(-20f, 34f);
        var txt = go.AddComponent<TextMeshProUGUI>();
        txt.text = text;
        txt.fontSize = size;
        txt.alignment = TextAlignmentOptions.Center;
    }

    void SetStatus(string s)
    {
        if (_status != null) { _status.text = s; _status.enabled = !string.IsNullOrEmpty(s); }
    }
}
