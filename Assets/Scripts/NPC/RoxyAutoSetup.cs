using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif

/// <summary>
/// Auto-wires Roxy with zero scene setup: on scene load it finds her model and ensures she has the
/// <see cref="RoxyNPC"/> quest-giver component, an interaction collider, AND a looping idle animation,
/// so she's immediately click / press-E talkable and never stands frozen. Matches the project's
/// self-building pattern (DevPanel, ModulesPanelUI…).
///
/// It looks for a GameObject whose name contains "roxy" (case-insensitive). So: name her scene object
/// (or its root) <b>Roxy</b> and everything else is automatic. Idempotent — anything already set up
/// (by hand or a previous run) is left alone.
///
/// Idle animation: her glTF ships animation clips but no Animator Controller, so out of the box she
/// just freezes in a pose. In the editor, the first Play builds Resources/NPC/RoxyIdle (a looping copy
/// of her idle clip + a one-state controller) from the model and saves it, so every later Play — and
/// player builds — load it straight from Resources.
/// </summary>
public static class RoxyAutoSetup
{
    const string IdleResourcePath = "NPC/RoxyIdle";   // Resources/NPC/RoxyIdle.controller

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Wire()
    {
        // Prefer an already-wired Roxy (added manually or by a previous run) so we never duplicate,
        // but still run the collider/animation healing on her.
        var existing = Object.FindAnyObjectByType<RoxyNPC>();
        GameObject go = existing != null ? existing.gameObject : null;

        if (go == null)
        {
            foreach (var t in Object.FindObjectsByType<Transform>())
            {
                if (!t.name.ToLowerInvariant().Contains("roxy")) continue;
                go = t.gameObject;
                go.AddComponent<RoxyNPC>();
                Debug.Log($"[RoxyAutoSetup] Wired RoxyNPC onto '{go.name}'. Walk up and press E / click to talk.");
                break;
            }
        }

        if (go == null)
        {
            Debug.Log("[RoxyAutoSetup] No scene object named like 'Roxy' found. Rename her GameObject to 'Roxy' " +
                      "and she'll auto-wire on the next Play.");
            return;
        }

        EnsureInteractionCollider(go);
        EnsureIdleAnimation(go);
    }

    /// <summary>The Interactor3D detects NPCs via colliders (OverlapSphere → GetComponentInParent).
    /// Give Roxy a roughly human-sized capsule if she has none, so she's detectable.</summary>
    static void EnsureInteractionCollider(GameObject go)
    {
        if (go.GetComponentInChildren<Collider>() != null) return;
        var cc = go.AddComponent<CapsuleCollider>();
        cc.height = 1.8f;
        cc.radius = 0.4f;
        cc.center = new Vector3(0f, 0.9f, 0f);
    }

    /// <summary>Make sure Roxy actually animates: her Animator gets the RoxyIdle controller from
    /// Resources (built on first editor Play if missing). Root motion off so the clip can never walk
    /// her away from her spot.</summary>
    static void EnsureIdleAnimation(GameObject go)
    {
        // The final Meshy FBX is an unrigged mesh. The old model's bone animation cannot
        // animate it; don't add an Animator or bind an incompatible avatar/controller.
        if (go.GetComponentInChildren<SkinnedMeshRenderer>() == null) return;
        var anim = go.GetComponentInChildren<Animator>();
        if (anim == null) anim = go.AddComponent<Animator>();
        if (anim.runtimeAnimatorController != null) { anim.applyRootMotion = false; return; }   // already animating

        var rc = Resources.Load<RuntimeAnimatorController>(IdleResourcePath);
#if UNITY_EDITOR
        if (rc == null) rc = BuildIdleControllerAsset(go);
#endif
        if (rc == null)
        {
            Debug.LogWarning("[RoxyAutoSetup] No idle controller found at Resources/" + IdleResourcePath +
                             " and it couldn't be built — Roxy will stand frozen.");
            return;
        }
        anim.runtimeAnimatorController = rc;
        anim.applyRootMotion = false;
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            smr.updateWhenOffscreen = true;
        Debug.Log("[RoxyAutoSetup] Roxy is animating (Resources/" + IdleResourcePath + ").");
    }

#if UNITY_EDITOR
    /// <summary>Editor-only, runs at most once ever: pull the idle clip out of Roxy's model asset,
    /// save a looping copy + a one-state controller into Resources/NPC, and return the controller.
    /// After this the assets exist on disk, so runtime (and builds) just Resources.Load them.</summary>
    static RuntimeAnimatorController BuildIdleControllerAsset(GameObject go)
    {
        var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
        string path = (smr != null && smr.sharedMesh != null) ? AssetDatabase.GetAssetPath(smr.sharedMesh) : null;
        if (string.IsNullOrEmpty(path)) return null;

        var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                                 .Where(c => !c.name.StartsWith("__preview")).ToList();
        if (clips.Count == 0)
        {
            Debug.LogWarning($"[RoxyAutoSetup] No animation clips inside {path} — can't build an idle.");
            return null;
        }
        var source = clips.FirstOrDefault(c => c.name.ToLowerInvariant().Contains("idle")) ?? clips[0];

        // A looping COPY of the clip — loop-time on an imported sub-asset can't be edited directly.
        var copy = Object.Instantiate(source);
        copy.name = "RoxyIdle";
        var settings = AnimationUtility.GetAnimationClipSettings(copy);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(copy, settings);

        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/NPC")) AssetDatabase.CreateFolder("Assets/Resources", "NPC");
        AssetDatabase.CreateAsset(copy, "Assets/Resources/NPC/RoxyIdle.anim");

        var controller = AnimatorController.CreateAnimatorControllerAtPathWithClip(
            "Assets/Resources/NPC/RoxyIdle.controller", copy);
        AssetDatabase.SaveAssets();
        Debug.Log($"[RoxyAutoSetup] Built Resources/NPC/RoxyIdle from '{source.name}' in {path}.");
        return controller;
    }
#endif
}
