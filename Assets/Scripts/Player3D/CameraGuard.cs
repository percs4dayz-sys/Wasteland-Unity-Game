using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Guarantees a working main camera exists on Play, and rebuilds one if it doesn't.
///
/// Losing the camera isn't only a black screen — a surprising amount of the game reads
/// <c>Camera.main</c> and quietly stops working when it's null: click-to-move raycasts
/// (ClickToMove3D), hover tooltips (HoverInspector), floating combat numbers (CombatFeedbackUI)
/// and the first-person arms. So this runs before any of them and heals the scene.
///
/// It handles the three ways a camera goes "missing", in preference order, and only ever CREATES a
/// camera as a last resort — adopting the existing one keeps whatever framing/post settings the
/// scene author set:
///   1. A camera exists but isn't tagged MainCamera → Camera.main is null even though you can see
///      the world. Retag it.
///   2. A camera exists but its GameObject or Camera component is disabled → re-enable it.
///   3. No camera at all → build one (Camera + AudioListener + OrbitCamera3D) targeting the player.
///
/// Also collapses duplicate AudioListeners, which is the usual side effect of a camera being
/// replaced by hand (imported FBX props frequently drag one in).
///
/// Self-installing, like the project's other bootstrap helpers — no scene wiring.
/// </summary>
public static class CameraGuard
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        Ensure();
        // Re-check on every scene load, not just the first.
        SceneManager.sceneLoaded += (_, __) => Ensure();
    }

    /// <summary>Make sure Camera.main resolves to something usable. Safe to call repeatedly.</summary>
    public static Camera Ensure()
    {
        // Fast path — a tagged, enabled camera is already live.
        var main = Camera.main;
        if (main != null)
        {
            PrepareForPlay(main, created: false);
            return main;
        }

        // Adopt any camera already in the scene, including disabled ones. Prefer one that already
        // has our orbit rig, then any enabled camera, then anything at all.
        Camera best = null;
        int bestScore = -1;
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
        {
            if (cam == null) continue;
            int score = 0;
            if (cam.GetComponent<OrbitCamera3D>() != null) score += 4;
            if (cam.enabled) score += 2;
            if (cam.gameObject.activeInHierarchy) score += 1;
            if (score > bestScore) { bestScore = score; best = cam; }
        }

        if (best != null)
        {
            if (!best.gameObject.activeSelf) best.gameObject.SetActive(true);
            if (!best.enabled) best.enabled = true;
            if (!best.CompareTag("MainCamera")) best.tag = "MainCamera";

            Debug.LogWarning($"[CameraGuard] Camera.main was null — recovered '{best.name}' " +
                             "(re-enabled and/or retagged MainCamera).");
            PrepareForPlay(best, created: false);
            return best;
        }

        // Nothing to adopt: build one.
        var go = new GameObject("Main Camera") { tag = "MainCamera" };
        var built = go.AddComponent<Camera>();
        go.AddComponent<AudioListener>();

        Debug.LogWarning("[CameraGuard] No camera in the scene — built a replacement Main Camera.");
        PrepareForPlay(built, created: true);
        return built;
    }

    /// <summary>Give the camera the pieces the game expects, and clear duplicate audio listeners.</summary>
    static void PrepareForPlay(Camera cam, bool created)
    {
        // In 3D the camera needs the orbit rig; it self-heals its own target in Start().
        // Only attach it when this is genuinely a 3D session so 2D scenes aren't given an orbit rig.
        bool is3D = GameMode.Is3D || Object.FindAnyObjectByType<Player3DController>() != null;
        if (is3D && cam.GetComponent<OrbitCamera3D>() == null)
        {
            cam.gameObject.AddComponent<OrbitCamera3D>();
            Debug.LogWarning($"[CameraGuard] Added a missing OrbitCamera3D to '{cam.name}'.");
        }

        if (created)
        {
            // Sensible starting framing for a fresh camera; OrbitCamera3D takes over from here.
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 3000f;
            var player = PlayerEntity.Instance != null
                ? PlayerEntity.Instance
                : Object.FindAnyObjectByType<PlayerEntity>();
            if (player != null)
                cam.transform.position = player.transform.position + new Vector3(0f, 12f, -12f);
            cam.transform.LookAt(player != null ? player.transform.position + Vector3.up : Vector3.zero);
        }

        EnsureSingleAudioListener(cam);
    }

    /// <summary>Exactly one AudioListener, preferring the one on the main camera. More than one makes
    /// Unity spam warnings and picks arbitrarily.</summary>
    static void EnsureSingleAudioListener(Camera cam)
    {
        var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include);
        if (listeners.Length == 0)
        {
            cam.gameObject.AddComponent<AudioListener>();
            return;
        }

        AudioListener keep = null;
        foreach (var l in listeners)
            if (l != null && l.gameObject == cam.gameObject) { keep = l; break; }
        if (keep == null) keep = listeners[0];

        keep.enabled = true;
        foreach (var l in listeners)
            if (l != null && l != keep && l.enabled) l.enabled = false;
    }
}
