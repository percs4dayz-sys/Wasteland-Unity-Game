using UnityEngine;

/// <summary>
/// A fishing spot is just bubbles on the water: small bubbles welling up and popping where the fish are,
/// gone while the spot is fished out and back when it respawns. Built at runtime over the spot's old flat
/// "Ripple" disc (which it hides), so every fishing spot — placed, prefab or future — looks the same with
/// no scene edits. The bubbles' renderer belongs to the node, so the node's own depletion hides them.
/// </summary>
public static class FishingBubbles
{
    static Material _material;

    /// <summary>Call before the node gathers its renderers. Does nothing for spots without a Ripple disc
    /// (e.g. the town's water barrels).</summary>
    public static void Apply(Transform spot)
    {
        var ripple = spot.Find("Ripple");
        if (ripple == null || spot.Find("Bubbles") != null) return;
        if (_material == null) _material = Resources.Load<Material>("FX/FishingBubbles");
        if (_material == null) return;   // keep the disc rather than show nothing

        float radius = Mathf.Max(0.25f, ripple.lossyScale.x * 0.45f);
        // Only the disc's look goes: a tap collider on the Ripple object must stay, or the spot can't be tapped.
        var disc = ripple.GetComponent<Renderer>();
        if (disc != null) { disc.enabled = false; Object.Destroy(disc); }

        var go = new GameObject("Bubbles");
        go.transform.SetParent(spot, false);
        go.transform.position = ripple.position + Vector3.up * 0.03f;
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);   // the emitter circle lies flat on the water

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.duration = 2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);   // a slow drift outward across the surface
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.24f);   // big enough to spot from the phone's camera
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.97f, 1f, 0.95f), new Color(0.7f, 0.92f, 1f, 0.8f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 48;
        main.scalingMode = ParticleSystemScalingMode.Shape;

        var emission = ps.emission;
        emission.rateOverTime = 20f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0.4f, 3, 6, 1, 0.9f) });   // the odd bigger "blub"

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        shape.radiusThickness = 1f;

        var velocity = ps.velocityOverLifetime;   // well up a little before popping
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.04f, 0.16f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        var size = ps.sizeOverLifetime;   // swell, then pop
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.75f, 1f), new Keyframe(1f, 1.3f)));

        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.9f, 0.8f), new GradientAlphaKey(0f, 1f) });
        colour.color = fade;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = _material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        go.AddComponent<Keeper>();
        ps.Play();
    }

    /// <summary>Keeps the bubbles going. The world streamer switches small scriptless pieces off by distance
    /// and its first pass left them off for good; it leaves anything with a script alone — and bubbles are
    /// cheap and pause off screen anyway. Anything else that stops them, this restarts.</summary>
    sealed class Keeper : MonoBehaviour
    {
        ParticleSystem _ps;
        void Awake() => _ps = GetComponent<ParticleSystem>();
        void OnEnable() { if (_ps != null && !_ps.isPlaying) _ps.Play(); }
        void Update() { if (_ps != null && !_ps.isPlaying) _ps.Play(); }
    }
}
