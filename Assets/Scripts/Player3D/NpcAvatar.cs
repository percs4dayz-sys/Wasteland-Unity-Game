using System.Collections.Generic;
using UnityEngine;
using Synty.SidekickCharacters.Enums;

/// <summary>
/// A Synty Sidekick character for an NPC, built with the same no-database technique the player uses
/// (see ArmourVisuals): instantiate the base rig, harvest its bones and proven bind poses, switch its own
/// body off, then hang one SkinnedMeshRenderer per part on that skeleton. No Sidekick runtime, no SQLite.
///
/// <see cref="Create"/> takes a part list; <see cref="RandomLook"/> makes a believable, repeatable one
/// from an NPC's name — different head, hair, brows, nose, ears, beard and accessories on the same civilian
/// clothing the player starts in. Colours come from the base material's stock palette (per-NPC colour needs
/// the Sidekick database, which the world deliberately avoids), so variety comes from the shapes.
/// </summary>
public class NpcAvatar : MonoBehaviour
{
    const string BaseModel = "Meshes/SK_BaseModel";
    const string BaseMaterial = "Materials/M_BaseMaterial";
    const string LocomotionController = "PlayerAnimator";   // Speed-driven idle/walk/run blend

    const string Base = "SK_HUMN_BASE";
    const string Civil09 = "SK_SCFI_CIVL_09", Civil10 = "SK_SCFI_CIVL_10", Villain01 = "SK_HORR_VILN_01";

    static Material _sharedMaterial;

    readonly Dictionary<string, Transform> _bones = new();
    readonly Dictionary<string, Matrix4x4> _bindByBone = new();
    readonly List<Object> _owned = new();
    Transform _rootBone;

    /// <summary>The rig with its parts applied, or null if the Sidekick base model isn't in the project.</summary>
    public static GameObject Create(string name, IDictionary<CharacterPartType, string> parts)
    {
        var prefab = Resources.Load<GameObject>(BaseModel);
        if (prefab == null) return null;

        var rig = Instantiate(prefab);
        rig.name = name + " Model";
        var avatar = rig.AddComponent<NpcAvatar>();
        if (!avatar.Build(parts)) { Destroy(rig); return null; }
        return rig;
    }

    void OnDestroy()
    {
        foreach (var o in _owned) if (o != null) Destroy(o);
    }

    bool Build(IDictionary<CharacterPartType, string> parts)
    {
        var existing = GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (existing == null || existing.rootBone == null) return false;

        _rootBone = existing.rootBone;
        foreach (var t in _rootBone.GetComponentsInChildren<Transform>(true)) _bones[t.name] = t;
        _bones[_rootBone.name] = _rootBone;

        // The imported rig ships its own body; harvest its exact bones + proven bind poses, then switch it off.
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var bones = smr.bones;
            var binds = smr.sharedMesh != null ? smr.sharedMesh.bindposes : null;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                _bones[bones[i].name] = bones[i];
                if (binds != null && i < binds.Length && !_bindByBone.ContainsKey(bones[i].name))
                    _bindByBone[bones[i].name] = binds[i];
            }
            smr.enabled = false;
        }

        var anim = GetComponentInChildren<Animator>(true);
        if (anim != null)
        {
            var controller = Resources.Load<RuntimeAnimatorController>(LocomotionController);
            if (controller != null) anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // base renderers are off, so don't cull the skeleton
        }

        int applied = 0;
        foreach (var kv in parts) if (Apply(kv.Key, kv.Value)) applied++;
        return applied > 0;
    }

    bool Apply(CharacterPartType type, string partName)
    {
        string path = SidekickPartLibrary.ResourcePath(partName);
        var prefab = path != null ? Resources.Load<GameObject>(path) : null;
        var src = prefab != null ? prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        if (src == null || src.sharedMesh == null) return false;   // a part this build doesn't have — just skip it

        var go = new GameObject("Part_" + type);
        go.transform.SetParent(transform, false);
        var dst = go.AddComponent<SkinnedMeshRenderer>();

        // Re-point the part's bones at THIS rig by name, walking up to the nearest bone with a proven bind
        // pose when the rig lacks one (hair jiggle bones etc.) — same rules as ArmourVisuals.ApplyPart.
        var srcBones = src.bones;
        var mapped = new Transform[srcBones.Length];
        for (int i = 0; i < srcBones.Length; i++)
        {
            Transform target = null;
            for (var t = srcBones[i]; t != null && target == null; t = t.parent)
                if (!t.name.Contains("_dyn_") && _bindByBone.ContainsKey(t.name) && _bones.TryGetValue(t.name, out var b)) target = b;
            for (var t = srcBones[i]; t != null && target == null; t = t.parent)
                if (!t.name.Contains("_dyn_") && _bones.TryGetValue(t.name, out var b)) target = b;
            mapped[i] = target != null ? target : _rootBone;
        }

        var rebound = Instantiate(src.sharedMesh);
        rebound.name = src.sharedMesh.name + " (npc)";
        _owned.Add(rebound);
        var rootLTW = dst.transform.localToWorldMatrix;
        var binds = new Matrix4x4[mapped.Length];
        for (int i = 0; i < mapped.Length; i++)
            binds[i] = _bindByBone.TryGetValue(mapped[i].name, out var bp) ? bp : mapped[i].worldToLocalMatrix * rootLTW;
        rebound.bindposes = binds;

        dst.sharedMesh = rebound;
        var mat = SharedMaterial(src);
        var mats = new Material[Mathf.Max(1, rebound.subMeshCount)];
        for (int i = 0; i < mats.Length; i++) mats[i] = mat;   // one material per sub-mesh or the extras vanish
        dst.sharedMaterials = mats;
        dst.bones = mapped;
        dst.rootBone = src.rootBone != null && _bones.TryGetValue(src.rootBone.name, out var rb) ? rb : _rootBone;
        dst.updateWhenOffscreen = true;
        return true;
    }

    static Material SharedMaterial(SkinnedMeshRenderer src)
    {
        if (_sharedMaterial != null) return _sharedMaterial;
        var basis = Resources.Load<Material>(BaseMaterial);
        return _sharedMaterial = basis != null ? new Material(basis) : src.sharedMaterial;
    }

    // ── looks ────────────────────────────────────────────────────────────────

    static bool Exists(string partName)
    {
        string path = SidekickPartLibrary.ResourcePath(partName);
        return path != null && Resources.Load<GameObject>(path) != null;
    }

    static void Put(Dictionary<CharacterPartType, string> d, string family, CharacterPartType type)
    {
        string name = ArmourVisuals.GuessPartName(family, type);
        if (name != null && Exists(name)) d[type] = name;
    }

    static int Hash(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) h = (h ^ c) * 16777619;
        return (int)(h & 0x7FFFFFFF);
    }

    /// <summary>A repeatable look for <paramref name="seed"/> (the NPC's name): face and hair from the base
    /// human set, civilian clothing, and a few accessories. <paramref name="guard"/> adds a helmet and shoulder
    /// pads; <paramref name="kid"/> skips facial hair.</summary>
    public static Dictionary<CharacterPartType, string> RandomLook(string seed, bool kid = false, bool guard = false)
    {
        var r = new System.Random(Hash(seed));
        var d = new Dictionary<CharacterPartType, string>();

        string head = $"{Base}_{r.Next(1, 3):00}";      // two head shapes exist
        string hair = $"{Base}_{r.Next(1, 11):00}";
        string brow = $"{Base}_{r.Next(1, 11):00}";
        string ear  = $"{Base}_{r.Next(1, 11):00}";
        string nose = $"{Base}_{r.Next(1, 12):00}";
        string eyes = $"{Base}_01";
        string body = $"{Base}_01";

        Put(d, head, CharacterPartType.Head);
        Put(d, hair, CharacterPartType.Hair);
        Put(d, brow, CharacterPartType.EyebrowLeft);
        Put(d, brow, CharacterPartType.EyebrowRight);
        Put(d, ear,  CharacterPartType.EarLeft);
        Put(d, ear,  CharacterPartType.EarRight);
        Put(d, nose, CharacterPartType.Nose);
        Put(d, eyes, CharacterPartType.EyeLeft);
        Put(d, eyes, CharacterPartType.EyeRight);
        if (!kid && r.NextDouble() < 0.4) Put(d, $"{Base}_{r.Next(1, 11):00}", CharacterPartType.FacialHair);

        // Bare body first (so a missing outfit piece never leaves a hole), then the civilian clothing over it.
        foreach (var slot in CharacterCreatorConfig.ClothingSlots) Put(d, body, slot);
        foreach (var slot in CharacterCreatorConfig.ClothingSlots) Put(d, Civil09, slot);

        // Accessories.
        if (guard)
        {
            Put(d, Civil10, CharacterPartType.AttachmentHead);
            Put(d, Civil10, CharacterPartType.AttachmentShoulderLeft);
            Put(d, Civil10, CharacterPartType.AttachmentShoulderRight);
            Put(d, Civil10, CharacterPartType.AttachmentHipsBack);
        }
        else
        {
            int hat = r.Next(0, 4);
            if (hat == 1) Put(d, Civil09, CharacterPartType.AttachmentHead);
            else if (hat == 2) Put(d, Civil10, CharacterPartType.AttachmentHead);
            else if (hat == 3 && !kid) Put(d, Villain01, CharacterPartType.AttachmentHead);
            if (r.NextDouble() < 0.25) Put(d, Civil09, CharacterPartType.AttachmentFace);
            if (r.NextDouble() < 0.40) Put(d, Civil09, CharacterPartType.AttachmentBack);
            if (r.NextDouble() < 0.35) Put(d, Civil09, CharacterPartType.AttachmentHipsFront);
        }
        return d;
    }
}
