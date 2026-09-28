using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Self-healing scene lighting — so the world never renders as flat grey "gym light", in the editor
/// OR in a build, with zero per-scene setup. On every scene load it:
///   • ensures a directional "sun" exists and isn't near-black (rescues intensity only — won't clobber
///     a light you've deliberately tuned),
///   • upgrades FLAT solid-colour ambient to a warm sky→ground GRADIENT (dimensional fill light),
///   • drops in a procedural skybox if the scene has none (guarded: skipped if the shader was stripped
///     from the build).
/// Pairs with OrbitCamera3D forcing post-processing on. Matches the project's self-building pattern.
/// </summary>
public static class SceneLightingHealer
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded += (scene, mode) => Heal();
        Heal();
    }

    static void Heal()
    {
        // ── 1) the sun ──────────────────────────────────────────────────────
        var sun = BrightestDirectional();
        if (sun == null)
        {
            var go = new GameObject("Sun (auto)");
            sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);           // faintly warm
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(50f, -35f, 0f);  // nice raking angle for shadows
        }
        else if (sun.intensity < 0.4f)
        {
            sun.intensity = 1.15f;                              // only RESCUE a near-black light
            if (sun.shadows == LightShadows.None) sun.shadows = LightShadows.Soft;
        }

        // ── 2) ambient — turn flat grey into a warm dimensional gradient ─────
        if (RenderSettings.ambientMode == AmbientMode.Flat)
        {
            RenderSettings.ambientMode         = AmbientMode.Trilight;      // sky / horizon / ground
            RenderSettings.ambientSkyColor     = new Color(0.52f, 0.56f, 0.62f);  // cool sky fill
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.42f, 0.36f);  // warm horizon
            RenderSettings.ambientGroundColor  = new Color(0.16f, 0.14f, 0.12f);  // dark bounce
            RenderSettings.ambientIntensity    = 1f;
        }

        // ── 3) skybox — give the world a sky if it has none ─────────────────
        if (RenderSettings.skybox == null)
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader != null)   // may be stripped from a build; the gradient ambient still carries it
            {
                var sky = new Material(shader) { name = "AutoSky" };
                if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", 1.05f);
                if (sky.HasProperty("_SkyTint"))     sky.SetColor("_SkyTint", new Color(0.6f, 0.56f, 0.5f));
                if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", new Color(0.3f, 0.27f, 0.24f));
                if (sky.HasProperty("_Exposure"))    sky.SetFloat("_Exposure", 1.15f);
                RenderSettings.skybox = sky;
                if (sun != null) RenderSettings.sun = sun;
            }
        }

        DynamicGI.UpdateEnvironment();
    }

    static Light BrightestDirectional()
    {
        Light best = null;
        foreach (var l in Object.FindObjectsByType<Light>())
            if (l.isActiveAndEnabled && l.type == LightType.Directional &&
                (best == null || l.intensity > best.intensity))
                best = l;
        return best;
    }
}
