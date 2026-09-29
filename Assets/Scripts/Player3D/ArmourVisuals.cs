using System.Collections.Generic;
using UnityEngine;
using Synty.SidekickCharacters.Enums;

/// <summary>
/// Makes worn armour show on the player, by swapping meshes on the character's existing skeleton.
///
/// Companion to EquipmentVisuals, which handles items held in the HANDS; this handles what's worn
/// on the BODY.
///
/// NO SIDEKICK RUNTIME. Every Sidekick part — base body and armour alike — is skinned to the same
/// skeleton with the same bone names, verified against the assets. So changing armour is just:
/// assign a different sharedMesh to a SkinnedMeshRenderer and re-point its bone array at our rig.
/// Sidekick's involvement ends at authoring; from here the parts are ordinary meshes.
///
/// The alternative — Synty's runtime API — opens a database, loads all 158 parts, and rebuilds the
/// entire character on every change. That is what ran the editor out of memory. This approach loads
/// only the meshes actually worn and never rebuilds anything.
///
/// One renderer per part slot is created under the player and reused forever; swapping gear just
/// reassigns its mesh, or hides it when nothing occupies that slot.
///
/// Put this on the Player root, alongside PlayerEntity.
/// </summary>
[RequireComponent(typeof(PlayerEntity))]
public class ArmourVisuals : MonoBehaviour
{
    [Tooltip("Enable modular armour mesh swapping. Turn OFF if using a custom single-mesh model.")]
    public bool useModularArmour = true;

    [Tooltip("Log each part swap. Noisy — bring-up only.")]
    public bool verbose;

    [Tooltip("Resources path of Sidekick's shared character material. Every part uses this; without " +
             "it parts render untextured grey, because the part FBXs carry no material themselves.")]
    public string BaseMaterialResource = "Materials/M_BaseMaterial";

    /// <summary>The player's own body — what armour covers, and what's restored when it comes off.</summary>
    readonly Dictionary<CharacterPartType, string> _base = new();

    /// <summary>What each slot is currently showing, so unchanged slots are left alone.</summary>
    readonly Dictionary<CharacterPartType, string> _shown = new();

    /// <summary>One reusable renderer per part type, all bound to the shared skeleton.</summary>
    readonly Dictionary<CharacterPartType, SkinnedMeshRenderer> _renderers = new();

    /// <summary>Bones of the player's rig by name, for remapping each part's bone array.</summary>
    readonly Dictionary<string, Transform> _bones = new();

    /// <summary>Proven-correct bind pose per bone, harvested from the base body's OWN renderers before
    /// we disable them. Every Sidekick part is authored in the same mesh space against the same
    /// skeleton, so a bone's bind pose is identical across parts — reusing the base body's bind poses
    /// (which demonstrably render correctly) rebinds any part without the reference-rig mismatch that
    /// exploded the mesh into spikes.</summary>
    readonly Dictionary<string, Matrix4x4> _bindByBone = new();

    Equipment _equipment;
    Transform _rootBone;
    bool _ready;

    /// <summary>One material instance carrying the baked colour map, shared by every part.</summary>
    Material _skinMaterial;

    void Start()
    {
        Initialize(GetComponent<PlayerEntity>()?.Equipment);
    }

    // Also used by the editor's isolated equipment validation scene.
    public void Initialize(Equipment equipment)
    {
        if (!useModularArmour)
        {
            Debug.Log("[ArmourVisuals] useModularArmour is OFF — leaving original character meshes alone.");
            enabled = false;
            return;
        }

        if (_equipment != null) _equipment.OnChanged -= Refresh;
        _equipment = equipment;
        if (_equipment == null)
        {
            Debug.LogError("[ArmourVisuals] No Equipment on the player.");
            enabled = false;
            return;
        }

        if (!MapSkeleton())
        {
            Debug.LogError("[ArmourVisuals] No skinned skeleton found under the player. Swap the " +
                           "player model to a Sidekick character first " +
                           "(Wasteland > Player > Swap Player Model To Selected Prefab).");
            enabled = false;
            return;
        }

        LoadBaseBody();

        _ready = true;
        _equipment.OnChanged += Refresh;
        Refresh();
    }

    void OnDestroy()
    {
        if (_equipment != null) _equipment.OnChanged -= Refresh;
        foreach (var r in _renderers.Values)
            if (r != null && r.sharedMesh != null) Release(r.sharedMesh);
        if (_skinMaterial != null) Release(_skinMaterial);
    }

    static void Release(Object value)
    {
        if (Application.isPlaying) Object.Destroy(value);
        else Object.DestroyImmediate(value);
    }

    // ── skeleton ─────────────────────────────────────────────────────────

    /// <summary>Index every bone under the player by name. Parts reference bones by name, so this
    /// is what lets an arbitrary part bind to our rig.</summary>
    bool MapSkeleton()
    {
        var existing = GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (existing == null || existing.rootBone == null) return false;

        _rootBone = existing.rootBone;
        _bones.Clear();
        _bindByBone.Clear();
        foreach (var t in _rootBone.GetComponentsInChildren<Transform>(true))
            _bones[t.name] = t;

        // Take over the body. The imported character prefab ships its own skinned meshes, and we
        // add ours on top — two complete overlapping characters, which z-fights into a shimmering
        // mess of flickering colours. We own the body now, so switch the originals off. They're
        // only disabled, not destroyed, so the skeleton and the prefab link stay intact.
        //
        // Before disabling each original renderer, harvest its EXACT bone instances (so we bind to
        // the transforms that actually animate, not a stray same-named node) and its proven bind
        // poses (so re-bound parts skin correctly — this is the fix for the exploding mesh).
        int silenced = 0;
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null || smr.gameObject.name.StartsWith("Part_")) continue;   // ours

            var bones = smr.bones;
            var mesh = smr.sharedMesh;
            var binds = mesh != null ? mesh.bindposes : null;
            if (bones != null)
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == null) continue;
                    _bones[bones[i].name] = bones[i];                                   // exact instance
                    if (binds != null && i < binds.Length && !_bindByBone.ContainsKey(bones[i].name))
                        _bindByBone[bones[i].name] = binds[i];                          // proven bind pose
                }

            if (!smr.enabled) continue;
            smr.enabled = false;
            silenced++;
        }
        if (silenced > 0)
            Debug.Log($"[ArmourVisuals] Disabled {silenced} original body renderer(s) — this " +
                      "component supplies the body now (prevents z-fighting with the imported model).");

        // The root itself may sit outside its own child list depending on the rig.
        _bones[_rootBone.name] = _rootBone;

        // Disabling the base renderers left the character's Animator with no ENABLED renderer of its
        // own, so under its default "Cull Update Transforms" mode Unity stops updating the skeleton —
        // freezing everything in bind pose. That is the T-pose that appears the moment this component
        // is added. Force the Animator to keep evaluating; our Part_ renderers are what's visible now.
        foreach (var anim in GetComponentsInChildren<Animator>(true))
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        if (verbose) Debug.Log($"[ArmourVisuals] Mapped {_bones.Count} bones from '{_rootBone.name}'.");
        return _bones.Count > 0;
    }

    // ── base body ────────────────────────────────────────────────────────

    void LoadBaseBody()
    {
        _base.Clear();

        var saved = CharacterAppearance.Load();
        foreach (var kv in saved) _base[kv.Key] = kv.Value;

        // Fixed starter clothing over the bare body — not selectable, not a drop.
        string starter = $"{CharacterCreatorConfig.StarterOutfitPrefix}_{CharacterCreatorConfig.StarterOutfitSet:00}";
        foreach (var slot in CharacterCreatorConfig.ClothingSlots)
        {
            string part = GuessPartName(starter, slot);
            if (part != null) _base[slot] = part;
        }

        if (verbose) Debug.Log($"[ArmourVisuals] Base body: {_base.Count} parts " +
                               $"({saved.Count} from the character creator).");
    }

    /// <summary>Install the creator's choices directly (used when creating a character in-session).</summary>
    public void SetBaseLoadout(IDictionary<CharacterPartType, string> parts)
    {
        _base.Clear();
        if (parts != null) foreach (var kv in parts) _base[kv.Key] = kv.Value;
        if (_ready) Refresh();
    }

    // ── the swap ─────────────────────────────────────────────────────────

    void Refresh()
    {
        if (!_ready) return;

        var wanted = new Dictionary<CharacterPartType, string>(_base);

        // Lay each equipped armour item over the slots it covers.
        foreach (string slot in Equipment.Slots)
        {
            var coverage = CharacterCreatorConfig.CoverageFor(slot);
            if (coverage.Length == 0) continue;              // held items aren't body parts

            var worn = _equipment.GetItem(slot);
            string family = worn != null ? worn.armourMesh : null;
            if (string.IsNullOrEmpty(family)) continue;      // nothing worn → base shows through

            foreach (var type in coverage)
            {
                string part = GuessPartName(family, type);
                // Attachments are optional: a set may have no cape, knee pad, etc.
                // Remove the previous set's attachment instead of leaving stale armour visible.
                if (part != null && Resources.Load<GameObject>(SidekickPartLibrary.ResourcePath(part)) != null)
                    wanted[type] = part;
                else if (!System.Array.Exists(CharacterCreatorConfig.ClothingSlots, t => t == type))
                    wanted.Remove(type);
            }
        }

        // Apply only what actually changed — most equipment events touch one or two slots.
        foreach (var kv in wanted)
        {
            if (_shown.TryGetValue(kv.Key, out var current) && current == kv.Value) continue;
            if (ApplyPart(kv.Key, kv.Value)) _shown[kv.Key] = kv.Value;
        }

        // Slots that used to show something but no longer should.
        var stale = new List<CharacterPartType>();
        foreach (var kv in _shown)
            if (!wanted.ContainsKey(kv.Key)) stale.Add(kv.Key);
        foreach (var type in stale)
        {
            if (_renderers.TryGetValue(type, out var r) && r != null) r.enabled = false;
            _shown.Remove(type);
        }
    }

    /// <summary>Put a part's mesh on this slot's renderer, bound to our skeleton.</summary>
    bool ApplyPart(CharacterPartType type, string partName)
    {
        var src = SidekickPartLibrary.LoadRenderer(partName);
        if (src == null || src.sharedMesh == null) return false;

        var dst = GetOrCreateRenderer(type);

        // Re-point the part's bones at OUR rig, by name. A part may reference bones this rig does NOT
        // have — most often hair/cloth JIGGLE bones (hair_dyn_*). Left null, every vertex weighted to
        // such a bone collapses to the origin and streaks out as a spike (the leftover hair spike). So
        // for a missing bone we substitute its nearest ANCESTOR that DOES exist (walking up the part's
        // own hierarchy), rigidly attaching those verts to a real bone — hair rides the head instead.
        // Bind each of the part's bones to a bone on OUR rig for which the base body PROVED a bind pose
        // (harvested in MapSkeleton). If the exact bone wasn't skinned by the base body — hair/cloth
        // bones (hair_dyn_*) never are, even the ones that DO exist on the rig — walk up the part's own
        // hierarchy to the nearest bone that WAS proven, so those verts attach rigidly to a real bone
        // (hair → head) instead of shattering. Every vertex thus gets a proven, pose-independent bind.
        var srcBones = src.bones;
        var mapped = new Transform[srcBones.Length];
        int substituted = 0;
        string firstSub = null;
        for (int i = 0; i < srcBones.Length; i++)
        {
            var sb = srcBones[i];
            Transform target = null;
            bool exact = false;
            for (var t = sb; t != null; t = t.parent)
                if (!t.name.Contains("_dyn_") && _bindByBone.ContainsKey(t.name) && _bones.TryGetValue(t.name, out var b))
                { target = b; exact = t == sb; break; }
            if (target == null && sb != null)          // no proven ancestor — any existing bone, else root
                for (var t = sb; t != null; t = t.parent)
                    if (!t.name.Contains("_dyn_") && _bones.TryGetValue(t.name, out var b)) { target = b; break; }
            mapped[i] = target != null ? target : _rootBone;
            if (!exact) { substituted++; if (firstSub == null) firstSub = sb != null ? sb.name : "<null>"; }
        }
        if (substituted > 0 && verbose)
            Debug.Log($"[ArmourVisuals] '{partName}': {substituted}/{srcBones.Length} bones re-anchored " +
                      $"to a proven parent (first '{firstSub}').");

        // THE DEFORM FIX. The part mesh ships bind poses authored against Sidekick's REFERENCE rig,
        // whose rest pose differs from this scene's SK_BaseModel rig — binding those original bind
        // poses to our bones is what exploded the mesh into spikes. So we give the renderer a per-part
        // mesh copy whose bind poses come from OUR skeleton: exact name-matched bones reuse the PROVEN
        // bind poses harvested from the base body's own renderers; substituted (missing) bones get a
        // bind pose computed directly against our rig at rest, so those verts sit correctly and ride
        // the ancestor bone rather than collapsing.
        var rebound = Object.Instantiate(src.sharedMesh);
        rebound.name = src.sharedMesh.name + " (rebound)";
        var rootLTW = dst.transform.localToWorldMatrix;
        var binds = new Matrix4x4[mapped.Length];
        for (int i = 0; i < mapped.Length; i++)
        {
            // Prefer the PROVEN bind pose of whatever real bone we ended up on — the exact name match
            // OR the substituted ancestor (e.g. hair_dyn_* → head). It's pose-independent and correct,
            // so hair settles as a rigid cap on the head instead of shattering. Direct computation is
            // only a last resort for a bone the base body never skinned.
            if (mapped[i] != null && _bindByBone.TryGetValue(mapped[i].name, out var bp)) binds[i] = bp;
            else if (mapped[i] != null) binds[i] = mapped[i].worldToLocalMatrix * rootLTW;
            else binds[i] = Matrix4x4.identity;
        }
        rebound.bindposes = binds;

        var prev = dst.sharedMesh;                 // a previous rebound copy, or null on first apply
        dst.sharedMesh = rebound;

        // Sidekick part FBXs carry no usable material of their own — the runtime API assigns the
        // shared M_BaseMaterial to every character it builds. We bypass that API, so we do the same
        // thing ourselves; without it every part renders untextured grey.
        //
        // ONE MATERIAL PER SUB-MESH. This used to be `dst.sharedMaterial = ...` (singular), which
        // sets a ONE-ELEMENT materials array — and Unity silently does not draw any sub-mesh that
        // has no material opposite it. These parts have 2-3 sub-meshes each (the civilian torso has
        // 3), so everything past sub-mesh 0 vanished. That is the invisible chest and arms: not a
        // rig or bind-pose problem at all, just a materials array one entry long.
        // Assigned AFTER the mesh, because the count comes from the mesh.
        var skin = SkinMaterial(src);
        var mats = new Material[Mathf.Max(1, rebound.subMeshCount)];
        for (int i = 0; i < mats.Length; i++) mats[i] = skin;
        dst.sharedMaterials = mats;

        dst.bones = mapped;
        dst.rootBone = src.rootBone != null && _bones.TryGetValue(src.rootBone.name, out var rb) ? rb : _rootBone;
        if (prev != null) Release(prev);    // free the old copy; never the shared source asset

        // Body build is blend shape weights, baked at creation. Replay them on every part so the
        // physique stays consistent when armour swaps a limb in or out.
        CharacterBake.ApplyShapes(dst);

        dst.enabled = true;
        dst.updateWhenOffscreen = true;

        if (verbose) Debug.Log($"[ArmourVisuals] {type} → {partName}");
        return true;
    }

    /// <summary>The one material every part shares. Starts from Sidekick's M_BaseMaterial (which
    /// carries the colour/metallic/smoothness atlases), and swaps in the player's baked colour map
    /// if a character was created. Built once and reused, so the whole body stays consistent.</summary>
    Material SkinMaterial(SkinnedMeshRenderer src)
    {
        if (_skinMaterial != null) return _skinMaterial;

        var basis = Resources.Load<Material>(BaseMaterialResource);
        if (basis == null)
        {
            // Fall back to whatever the part shipped with rather than rendering nothing.
            Debug.LogWarning($"[ArmourVisuals] Resources/{BaseMaterialResource} not found — " +
                             "the character will render untextured.");
            return src.sharedMaterial;
        }

        _skinMaterial = new Material(basis);

        var baked = CharacterBake.ColorMap();
        if (baked != null)   // the creator's skin/hair choices
        {
            _skinMaterial.mainTexture = baked;
            if (_skinMaterial.HasProperty("_ColorMap")) _skinMaterial.SetTexture("_ColorMap", baked);
        }

        return _skinMaterial;
    }

    SkinnedMeshRenderer GetOrCreateRenderer(CharacterPartType type)
    {
        if (_renderers.TryGetValue(type, out var existing) && existing != null) return existing;

        var go = new GameObject($"Part_{type}");
        go.transform.SetParent(transform, false);
        var smr = go.AddComponent<SkinnedMeshRenderer>();
        _renderers[type] = smr;
        return smr;
    }

    /// <summary>The conventional part name for a family/set and slot, e.g.
    /// ("SK_FANT_KNGT_17", Torso) → "SK_FANT_KNGT_17_10TORS_HU01". Sidekick's naming is strict
    /// enough that this is derivable, so no catalogue lookup is needed.</summary>
    internal static string GuessPartName(string family, CharacterPartType type)
    {
        string code = SlotCode(type);
        if (code == null || string.IsNullOrEmpty(family)) return null;
        return $"{family}_{(int)type:00}{code}_HU01";
    }

    /// <summary>The four-letter slot code Sidekick uses in part filenames.</summary>
    static string SlotCode(CharacterPartType type) => type switch
    {
        CharacterPartType.Head => "HEAD",
        CharacterPartType.Hair => "HAIR",
        CharacterPartType.EyebrowLeft => "EBRL",
        CharacterPartType.EyebrowRight => "EBRR",
        CharacterPartType.EyeLeft => "EYEL",
        CharacterPartType.EyeRight => "EYER",
        CharacterPartType.EarLeft => "EARL",
        CharacterPartType.EarRight => "EARR",
        CharacterPartType.FacialHair => "FCHR",
        CharacterPartType.Torso => "TORS",
        CharacterPartType.ArmUpperLeft => "AUPL",
        CharacterPartType.ArmUpperRight => "AUPR",
        CharacterPartType.ArmLowerLeft => "ALWL",
        CharacterPartType.ArmLowerRight => "ALWR",
        CharacterPartType.HandLeft => "HNDL",
        CharacterPartType.HandRight => "HNDR",
        CharacterPartType.Hips => "HIPS",
        CharacterPartType.LegLeft => "LEGL",
        CharacterPartType.LegRight => "LEGR",
        CharacterPartType.FootLeft => "FOTL",
        CharacterPartType.FootRight => "FOTR",
        CharacterPartType.AttachmentHead => "AHED",
        CharacterPartType.AttachmentFace => "AFAC",
        CharacterPartType.AttachmentBack => "ABAC",
        CharacterPartType.AttachmentHipsFront => "AHPF",
        CharacterPartType.AttachmentHipsBack => "AHPB",
        CharacterPartType.AttachmentHipsLeft => "AHPL",
        CharacterPartType.AttachmentHipsRight => "AHPR",
        CharacterPartType.AttachmentShoulderLeft => "ASHL",
        CharacterPartType.AttachmentShoulderRight => "ASHR",
        CharacterPartType.AttachmentElbowLeft => "AEBL",
        CharacterPartType.AttachmentElbowRight => "AEBR",
        CharacterPartType.AttachmentKneeLeft => "AKNL",
        CharacterPartType.AttachmentKneeRight => "AKNR",
        // These sit after the attachments in the enum and are easy to miss — leaving them out made
        // 23 real part files unreachable, including every nose and every set of teeth.
        CharacterPartType.Nose => "NOSE",
        CharacterPartType.Teeth => "TETH",
        CharacterPartType.Tongue => "TONG",
        CharacterPartType.Wrap => "WRAP",
        _ => null,
    };
}
