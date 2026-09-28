using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shows the currently-equipped item models in the soldier's hands.
///   • Right hand — the Weapon, or the Tool if no weapon is equipped.
///   • Left hand  — the Shield.
/// Models load from Resources via <see cref="ItemData.heldModel"/> and re-spawn whenever
/// <see cref="Equipment.OnChanged"/> fires. Per-item grip offsets align each model into the palm.
///
/// Put this on the Player root (the object with PlayerEntity). Hand bones are pulled from the
/// soldier's Humanoid Animator, so there's no manual bone wiring.
/// </summary>
public class EquipmentVisuals : MonoBehaviour
{
    [System.Serializable]
    public struct Grip
    {
        public int itemId;       // which item this offset is for (e.g. 3 = Pickaxe)
        public Vector3 position; // local position in the hand bone
        public Vector3 euler;    // local rotation (degrees)
        public float scale;      // uniform local scale (use this to size a too-big/too-small model)
    }

    [Header("Default grip (used for items without a specific override below)")]
    public Vector3 defaultPosition = Vector3.zero;
    public Vector3 defaultEuler = Vector3.zero;
    public float defaultScale = 1f;

    [Header("Per-item grip — add an entry per weapon/tool and dial it in")]
    public List<Grip> grips = new();

    Animator _anim;
    Transform _rightHand, _leftHand;
    Equipment _equip;
    GameObject _rightHeld, _leftHeld;
    int _rightId, _leftId;
    float _rightFit = 1f, _leftFit = 1f;   // cached auto-fit base scale (grip.scale multiplies it)
    int _retryAnchorsAt;                    // next frame to retry real bones while on fallback anchors

    void Start()
    {
        var pe = GetComponent<PlayerEntity>();
        if (pe == null || pe.Equipment == null) { enabled = false; return; }
        Initialize(pe.Equipment);
    }

    public void Initialize(Equipment equipment)
    {
        if (_equip != null) _equip.OnChanged -= Refresh;
        _equip = equipment;
        if (_equip == null) return;
        _equip.OnChanged += Refresh;
        Refresh();
    }

    void OnDestroy()
    {
        if (_equip != null) _equip.OnChanged -= Refresh;
        if (_fallbackMaterial != null) Release(_fallbackMaterial);
    }

    // The avatar may not be ready on the first frame — keep trying until the hand bones resolve,
    // then keep what's shown in sync with what's equipped.
    void Update()
    {
        if (_equip == null) return;

        // While items are parked on the fallback anchors, retry the REAL hand bones every couple of
        // seconds — a visual swap or an avatar fix can arrive at any time and should win immediately.
        if (_rightHand != null && _rightHand.name == "HeldAnchor_R" && Time.frameCount >= _retryAnchorsAt)
        {
            _retryAnchorsAt = Time.frameCount + 120;
            _rightHand = null; _leftHand = null; _anim = null;
            EnsureHands();
            if (_rightHand != null && _rightHand.name != "HeldAnchor_R") Refresh();
        }

        EnsureHands();
        if (_rightHand == null) return;

        // Self-heal: if what's equipped no longer matches what's shown, rebuild. Covers the load-order
        // race where a save restores equipment before the hand bones exist (so the OnChanged refresh
        // built nothing) — without this, held models only reappeared after a manual re-equip. Cheap:
        // two lookups + an int compare per frame.
        int gatheringTool = ActiveGatheringTool();
        int rightId = gatheringTool != 0 ? gatheringTool : ((_equip.GetItemId("Weapon") ?? _equip.GetItemId("Tool")) ?? 0);
        var weapon = _equip.GetItem("Weapon");
        int leftId = gatheringTool != 0 ? 0 : weapon != null && weapon.weaponStyle == WeaponStyle.Fission
            ? weapon.id : (_equip.GetItemId("Shield") ?? 0);
        // Rebuild when what's equipped changed, OR when something's equipped but its held model is
        // missing. The second case covers a load where equipment was restored before the hand bones
        // existed AND a runtime visual swap that destroyed the held props after they were built — the
        // id still "matched", so the plain id-compare never rebuilt, leaving empty hands until a
        // manual re-equip. (_rightHeld/_leftHeld read null once Unity destroys the held GameObject.)
        if (rightId != _rightId || leftId != _leftId
            || (rightId != 0 && _rightHeld == null)
            || (leftId  != 0 && _leftHeld  == null))
            Refresh();
    }

    void EnsureHands()
    {
        // A resolved hand that has drifted absurdly far from the body means the avatar/bone map is
        // wrong (the "held items float a giant distance away" bug) — drop it and re-resolve.
        if (_rightHand != null && !SaneHand(_rightHand)) _rightHand = null;
        if (_leftHand  != null && !SaneHand(_leftHand))  _leftHand  = null;
        if (_rightHand != null) return;

        if (_anim == null) _anim = GetComponentInChildren<Animator>();
        if (_anim == null) return;

        // Humanoid rigs: pull the mapped hand bones directly — but only trust bones that actually
        // sit on the body. A mis-assigned avatar (wrong mesh's avatar on the rig) reports "hand"
        // transforms parked at the origin or kilometres off; those get rejected here.
        if (_anim.avatar != null && _anim.isHuman)
        {
            _rightHand = Sane(_anim.GetBoneTransform(HumanBodyBones.RightHand));
            _leftHand  = Sane(_anim.GetBoneTransform(HumanBodyBones.LeftHand));
        }

        // Generic rigs (e.g. a Character-Creator model imported as Generic, or a humanoid whose hands
        // aren't mapped) return nothing above — find the bones by name so held items still appear.
        if (_rightHand == null) _rightHand = Sane(FindBoneByName(true));
        if (_leftHand  == null) _leftHand  = Sane(FindBoneByName(false));

        // Last resort: no trustworthy hand bone at all → anchor items at roughly hip height on the
        // player root, so gear is at least ON the character while the rig gets fixed.
        if (_rightHand == null) _rightHand = FallbackAnchor("HeldAnchor_R", new Vector3(0.35f, 1.05f, 0.15f));
        if (_leftHand  == null) _leftHand  = FallbackAnchor("HeldAnchor_L", new Vector3(-0.35f, 1.05f, 0.15f));
    }

    /// <summary>A hand bone is only believable within a few metres of the player root.</summary>
    bool SaneHand(Transform hand) => (hand.position - transform.position).sqrMagnitude <= 9f;   // 3 m
    Transform Sane(Transform hand) => hand != null && SaneHand(hand) ? hand : null;

    Transform FallbackAnchor(string name, Vector3 localPos)
    {
        var t = transform.Find(name);
        if (t == null)
        {
            t = new GameObject(name).transform;
            t.SetParent(transform, false);
            Debug.LogWarning("[EquipmentVisuals] No usable hand bone found (bad avatar/bone map?) — " +
                             "held items are anchored to the player root until the rig is fixed.");
        }
        t.localPosition = localPos;
        return t;
    }

    /// <summary>Find a hand bone by common naming conventions (Unity / Mixamo / Character Creator /
    /// Blender / Biped), so held items work even on non-Humanoid rigs.</summary>
    Transform FindBoneByName(bool right)
    {
        string[] candidates = right
            ? new[] { "RightHand", "mixamorig:RightHand", "CC_Base_R_Hand", "hand_r", "Hand_R", "R_Hand", "Bip01 R Hand" }
            : new[] { "LeftHand",  "mixamorig:LeftHand",  "CC_Base_L_Hand", "hand_l", "Hand_L", "L_Hand", "Bip01 L Hand" };

        var all = GetComponentsInChildren<Transform>(true);
        foreach (var name in candidates)
            foreach (var t in all)
                if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase))
                    return t;
        return null;
    }

    int ActiveGatheringTool()
    {
        var node = SkillingManager.Instance != null ? SkillingManager.Instance.ActiveNode : null;
        return node != null ? GatheringTools.BestFor(GetComponent<PlayerEntity>(), node.requiredToolId) : 0;
    }

    public void Refresh()
    {
        EnsureHands();
        if (_equip == null) return;

        int? weaponId = _equip.GetItemId("Weapon");
        var  weapon   = weaponId.HasValue ? ItemRegistry.Get(weaponId.Value) : null;

        // Power gauntlets (Fission) are worn on BOTH hands, so the same model shows left and right.
        // Every other weapon is right-hand only; the left hand carries a shield (or nothing). Since
        // gauntlets are two-handed the shield slot is already empty, so there's no conflict.
        bool bothHands = weapon != null && weapon.weaponStyle == WeaponStyle.Fission;

        int gatheringTool = ActiveGatheringTool();
        int? rightId = gatheringTool != 0 ? gatheringTool : weaponId ?? _equip.GetItemId("Tool");
        int? leftId  = gatheringTool != 0 ? (int?)null : bothHands ? weaponId : _equip.GetItemId("Shield");

        _rightHeld = Rebuild(_rightHeld, _rightHand, rightId, false, out _rightFit);
        _leftHeld  = Rebuild(_leftHeld,  _leftHand,  leftId,  true, out _leftFit);
        // Commit the shown id only when a held model was actually built (or nothing is equipped). If a
        // build produced nothing — hands not ready yet, OR the model was destroyed by a visual swap —
        // leave it -1 so Update() keeps retrying until a real model exists. (Genuinely model-less items
        // still fall back to a placeholder in Rebuild, so this won't spin forever.)
        _rightId = (rightId == null || _rightHeld != null) ? (rightId ?? 0) : -1;
        _leftId  = (leftId  == null || _leftHeld  != null) ? (leftId  ?? 0) : -1;
    }

    // Re-apply grips every frame so editing a Grip entry on this component DURING Play moves the held
    // model live — the practical way to dial placement in. Then "Copy Component" → Stop →
    // "Paste Component Values" to keep it (or just read the numbers off the Grip entry).
    void LateUpdate()
    {
        if (_rightHeld != null) ApplyGrip(_rightHeld, _rightId, _rightFit, FrameFor(false));
        if (_leftHeld  != null) ApplyGrip(_leftHeld,  _leftId,  _leftFit,  FrameFor(true));
    }

    void ApplyGrip(GameObject go, int itemId, float fit, Quaternion frame)
    {
        var g = GripFor(itemId);
        go.transform.localPosition = frame * g.position;
        go.transform.localRotation = frame * Quaternion.Euler(g.euler);
        go.transform.localScale    = Vector3.one * ((g.scale <= 0f ? 1f : g.scale) * fit);
    }

    Transform _rightFrameFor, _leftFrameFor;
    Quaternion _rightFrame = Quaternion.identity, _leftFrame = Quaternion.identity;

    Quaternion FrameFor(bool left)
    {
        var hand = left ? _leftHand : _rightHand;
        if (hand == null || hand.name.StartsWith("HeldAnchor_")) return Quaternion.identity;
        if (left)
        {
            if (_leftFrameFor != hand) { _leftFrameFor = hand; _leftFrame = HandFrame(_anim, hand, true); }
            return _leftFrame;
        }
        if (_rightFrameFor != hand) { _rightFrameFor = hand; _rightFrame = HandFrame(_anim, hand, false); }
        return _rightFrame;
    }

    /// <summary>
    /// Maps the hand frame the grips were authored in onto this rig's actual hand bone. The placements
    /// (HeldModelPlacement) and saved grips were made on the Synty hand, whose local −X runs down the
    /// fingers (right hand; mirrored on the left) and −Y exits through the thumb. Other rigs orient
    /// that bone differently — the Sidekick player's hand_r runs +X down the fingers and −Z out the
    /// thumb — which had every weapon and tool pointing backwards out of the fist. This measures the
    /// real finger and thumb directions from the skeleton, so grips hold on any humanoid rig and
    /// survive model swaps. Identity when the rig has no finger bones (the old behaviour).
    /// </summary>
    public static Quaternion HandFrame(Animator anim, Transform hand, bool left)
    {
        if (anim == null || hand == null || !anim.isHuman) return Quaternion.identity;
        var knuckle = anim.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal)
                   ?? anim.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
        var thumb = anim.GetBoneTransform(left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
        if (knuckle == null || thumb == null) return Quaternion.identity;
        // Knuckle and thumb-root positions are rigid in hand space, so finger curl doesn't skew this.
        Vector3 fingers = hand.InverseTransformDirection(knuckle.position - hand.position);
        Vector3 thumbSide = hand.InverseTransformDirection(thumb.position - hand.position);
        if (fingers.sqrMagnitude < 1e-8f) return Quaternion.identity;
        fingers.Normalize();
        thumbSide -= Vector3.Dot(thumbSide, fingers) * fingers;
        if (thumbSide.sqrMagnitude < 1e-8f) return Quaternion.identity;
        thumbSide.Normalize();
        Vector3 y = -thumbSide, x = (left ? 1f : -1f) * fingers, z = Vector3.Cross(x, y);
        return Quaternion.LookRotation(z, y);
    }

    GameObject Rebuild(GameObject current, Transform hand, int? itemId, bool leftHand, out float fit)
    {
        fit = 1f;
        if (current != null) Release(current);
        if (hand == null || itemId == null) return null;

        var item = ItemRegistry.Get(itemId.Value);
        if (item == null) return null;

        // Cosmetic weapon skin (right hand only): a chosen finish can override the model and/or tint,
        // so your favorite look rides on whatever tier weapon you actually wield. Stats are unaffected.
        var skin = WeaponSkins.AppliedFor(item);
        // The LEFT hand of a both-hands weapon (gauntlets) uses its own mesh when one is set, so the
        // pair is properly handed rather than one model mirrored. Falls back to the right model.
        string modelPath = (skin != null && !string.IsNullOrEmpty(skin.heldModel)) ? skin.heldModel
                         : (leftHand && !string.IsNullOrEmpty(item.heldModelLeft)) ? item.heldModelLeft
                         : item.heldModel;
        if (string.IsNullOrEmpty(modelPath)) return null;

        var prefab = HeldModels.Load(modelPath);   // by-name resolver: survives folder reorganizing
        if (prefab == null && GatheringTools.IsUpgrade(item.id))
            prefab = HeldModels.Load(GatheringTools.FallbackModel(item.id));
        if (prefab == null)
        {
            // Model not added yet → show a placeholder so the hand isn't empty. Fission (gauntlets)
            // reads as ranged here — a gun stand-in beats the melee machete it used to fall through to.
            bool rangedish = item.weaponStyle == WeaponStyle.Ranged || item.weaponStyle == WeaponStyle.Fission;
            string fallback = item.type == ItemType.Shield ? "HeldModels/shield_scrap"
                            : rangedish                     ? "HeldModels/Gun/pisol_pipe"
                            :                                 "HeldModels/machete";
            prefab = HeldModels.Load(fallback);
            if (prefab == null)
            {
                Debug.LogWarning($"[EquipmentVisuals] No model at Resources/{item.heldModel} (or fallback) for '{item.name}'.");
                return null;
            }
        }

        // A WRAPPER is what the grip actually drives. The raw model's pivot is usually NOT on its
        // geometry — generated/exported meshes are modelled off to one side of their origin — so
        // positioning/scaling/rotating the model directly makes the mesh float metres away ("10 feet
        // from the character"), come out a random size, or fling out when rotated. The wrapper sits
        // on the hand; the model is re-centred inside it so its geometry midpoint is at the wrapper's
        // origin. From then on grip position places the geometry, grip scale sizes it about its
        // centre, and grip rotation spins it in place. This is the fix for ALL those symptoms.
        var wrapper = new GameObject("Held_" + item.name).transform;
        wrapper.SetParent(hand, false);

        var model = Instantiate(prefab, wrapper).transform;
        model.localPosition = Vector3.zero;
        // Keep the import's rotation and scale: several FBXs use these to convert axes/units.

        // A held prop should never collide or fall — strip physics from the spawned model.
        foreach (var c in model.GetComponentsInChildren<Collider>(true)) Release(c);
        foreach (var rb in model.GetComponentsInChildren<Rigidbody>(true)) Release(rb);

        // Measure the geometry in WRAPPER space (accounts for any internal offsets/rotations the
        // prefab bakes in), re-centre the model on that midpoint, and size from the true extent —
        // NOT the pivot-to-mesh gap, which is what used to make the auto-fit scale go haywire.
        var heldRends = model.GetComponentsInChildren<Renderer>();
        Bounds gb = GeometryBounds(wrapper, heldRends);
        float longest = Mathf.Max(gb.size.x, Mathf.Max(gb.size.y, gb.size.z));
        if (HeldModelPlacement.TryGet(prefab.name, out var placement))
        {
            Vector3 handle=gb.center+Vector3.Scale(gb.size,placement.handle);
            Quaternion rotation=HeldModelPlacement.Rotation(placement,item,leftHand);
            model.localRotation=rotation*model.localRotation;
            model.localPosition=-(rotation*handle);
            if(longest>1e-6f)fit=placement.length/longest;
        }
        else
        {
            model.localPosition=-gb.center;
            if(longest>1e-6f)fit=.5f/longest;
        }

        // A missing material must not turn a perfectly usable weapon magenta — and an untextured import
        // (flat white, or a terrain shader on a gun) must not show as a white silhouette.
        foreach(var r in heldRends)
        {
            var materials=r.sharedMaterials;
            for(int i=0;i<materials.Length;i++)
                if(materials[i]==null)materials[i]=FallbackMaterial();
                else if(IsBlank(materials[i]))materials[i]=Retextured(materials[i]);
            r.sharedMaterials=materials;
        }

        // Skin tint: recolor the finish without swapping materials (works for tint-only skins that
        // ship no model). MaterialPropertyBlock, so the shared material asset is never mutated.
        if (skin != null && skin.tint.a > 0.001f)
        {
            var mpb = new MaterialPropertyBlock();
            foreach (var r in heldRends)
            {
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", skin.tint);   // URP Lit
                mpb.SetColor("_Color", skin.tint);       // legacy/other
                r.SetPropertyBlock(mpb);
            }
        }

        ApplyGrip(wrapper.gameObject, itemId.Value, fit, FrameFor(leftHand));
        return wrapper.gameObject;
    }

    /// <summary>Combined bounds of the renderers expressed in <paramref name="space"/>'s local
    /// coordinates, from each mesh's own bounds — so a pivot far from the geometry (common in
    /// generated models) can't skew the size or centre the way a world-space AABB would.</summary>
    static Bounds GeometryBounds(Transform space, Renderer[] rends)
    {
        var w2l = space.worldToLocalMatrix;
        bool first = true;
        Bounds b = new Bounds();
        foreach (var r in rends)
        {
            if (r == null) continue;
            Mesh m = (r as SkinnedMeshRenderer)?.sharedMesh
                   ?? r.GetComponent<MeshFilter>()?.sharedMesh;
            if (m == null) continue;
            var mb = m.bounds;
            var l2w = r.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3(
                    (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = w2l.MultiplyPoint3x4(l2w.MultiplyPoint3x4(corner));
                if (first) { b = new Bounds(local, Vector3.zero); first = false; }
                else b.Encapsulate(local);
            }
        }
        return first ? new Bounds(Vector3.zero, Vector3.one * 0.1f) : b;
    }

    Grip GripFor(int itemId)
    {
        foreach (var g in grips) if (g.itemId == itemId) return g;
        return new Grip { itemId = itemId, position = defaultPosition, euler = defaultEuler, scale = defaultScale };
    }

    Material _fallbackMaterial;
    Material FallbackMaterial()
    {
        if(_fallbackMaterial==null)
        {
            _fallbackMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _fallbackMaterial.SetColor("_BaseColor",new Color(.3f,.34f,.37f));
            _fallbackMaterial.SetFloat("_Metallic",.65f);
            _fallbackMaterial.SetFloat("_Smoothness",.35f);
        }
        return _fallbackMaterial;
    }
    readonly Dictionary<Material, Material> _retextured = new();   // per component: the gunmetal fallback is ours
    static readonly string[] TexFolders = { "HeldModels/Gun/", "HeldModels/Weapons/", "HeldModels/Tools/", "HeldModels/Shields/", "HeldModels/" };
    static readonly string[] AlbedoSuffixes = { "_albedo", "_Albedo", "_base", "_BaseColor", "_basecolor", "_diffuse", "_Diffuse" };

    static readonly string[] BaseColourProps = { "_BaseColor", "_Base_Color", "baseColorFactor", "_Color" };

    /// <summary>What an untextured FBX/OBJ import looks like in game: no maps at all and either a pale
    /// non-metal base colour, or a white emission glow. Also a terrain shader that ended up on a prop.
    /// Real metals (pale but metallic) are left alone.</summary>
    static bool IsBlank(Material m)
    {
        var sh = m.shader;
        if (sh == null) return false;
        if (sh.name.Contains("Terrain")) return true;
        for (int i = 0; i < sh.GetPropertyCount(); i++)
            if (sh.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture && m.GetTexture(sh.GetPropertyNameId(i)) != null)
                return false;
        if (m.HasProperty("_EmissionColor") && m.IsKeywordEnabled("_EMISSION") && m.GetColor("_EmissionColor").maxColorComponent > 0.5f)
            return true;
        float metal = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : m.HasProperty("metallicFactor") ? m.GetFloat("metallicFactor") : 0f;
        if (metal >= 0.5f) return false;
        foreach (var prop in BaseColourProps)
            if (m.HasProperty(prop))
            {
                Color c = m.GetColor(prop);
                return c.r > 0.7f && c.g > 0.7f && c.b > 0.7f;
            }
        return false;
    }

    /// <summary>The model's own maps when they sit loose beside it ("P88Mat" → P88_albedo), else worn gunmetal.</summary>
    Material Retextured(Material blank)
    {
        if (_retextured.TryGetValue(blank, out var done)) return done;
        string prefix = blank.name.Replace(" (Instance)", "");
        foreach (var cut in new[] { "Material", "Mat", "_mat" })
            if (prefix.Length > cut.Length && prefix.EndsWith(cut)) { prefix = prefix.Substring(0, prefix.Length - cut.Length); break; }
        Texture2D albedo = null;
        string found = null;
        foreach (var folder in TexFolders)
        {
            foreach (var suffix in AlbedoSuffixes)
                if ((albedo = Resources.Load<Texture2D>(folder + prefix + suffix)) != null) { found = folder; break; }
            if (albedo != null) break;
        }
        Material m;
        if (albedo != null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = prefix + " (auto)" };
            m.SetTexture("_BaseMap", albedo);
            m.SetColor("_BaseColor", Color.white);
            var normal = Resources.Load<Texture2D>(found + prefix + "_normal");
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Metallic", 0.5f);
            m.SetFloat("_Smoothness", 0.35f);
        }
        else m = FallbackMaterial();
        _retextured[blank] = m;
        return m;
    }

    static void Release(Object value)
    {
        if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);
    }

    /// <summary>Editor helper: re-spawn held models with current offsets (call from a button/inspector).</summary>
    [ContextMenu("Refresh Held Models")]
    void RefreshFromMenu() => Refresh();
}
