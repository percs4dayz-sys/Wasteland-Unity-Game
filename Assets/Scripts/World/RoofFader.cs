using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;   // ShadowCastingMode

/// <summary>
/// RuneScape-style roof hiding. Walk into a building and its roof vanishes so the top-down
/// camera can see inside; walk out and it comes back.
///
/// Setup:
///   1. Put this on the building root.
///   2. Give it a trigger Collider covering the INTERIOR floor area (a Box Collider tall
///      enough that the player capsule always overlaps it).
///   3. Drag the roof object into "Roof". Everything renderable under it is hidden.
///
/// Hiding is instant by default, which is what RuneScape actually does and it reads fine.
/// Set fadeDuration above 0 for a cross-fade, but note that only works if the roof's
/// materials are transparent-capable — an opaque URP Lit material can't fade, so it will
/// fall back to popping.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RoofFader : MonoBehaviour
{
    [Header("What to hide")]
    [Tooltip("The roof object. Every Renderer under it (including children) is hidden.")]
    public Transform roof;

    [Tooltip("Also hide these — upper floors, ceiling beams, anything blocking the view.")]
    public List<Transform> alsoHide = new List<Transform>();

    [Header("Behaviour")]
    [Tooltip("0 = instant pop (RuneScape-style). Above 0 cross-fades, but needs transparent materials.")]
    [Range(0f, 1f)] public float fadeDuration = 0f;

    [Tooltip("Also stop the roof casting shadows while hidden — otherwise the interior stays dark.")]
    public bool suppressShadows = true;

    [Tooltip("Keep the roof visible in first-person view, where you'd want to see it above you.")]
    public bool keepInFirstPerson = true;

    readonly List<Renderer> _renderers = new List<Renderer>();
    readonly List<ShadowCastingMode> _shadowModes = new List<ShadowCastingMode>();
    // Hiding a Renderer leaves its Collider live, so a hidden roof would still swallow every click
    // aimed at the floor beneath it. The colliders go down with the renderers.
    readonly List<Collider> _colliders = new List<Collider>();
    bool _hidden;
    int _inside;                 // trigger enter/exit can pair up oddly; count instead of bool
    float _fade = 1f;

    void Awake()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        Collect();
    }

    void Collect()
    {
        _renderers.Clear();
        _shadowModes.Clear();
        _colliders.Clear();

        if (roof != null)
        {
            _renderers.AddRange(roof.GetComponentsInChildren<Renderer>(true));
            _colliders.AddRange(roof.GetComponentsInChildren<Collider>(true));
        }
        foreach (var t in alsoHide)
            if (t != null)
            {
                _renderers.AddRange(t.GetComponentsInChildren<Renderer>(true));
                _colliders.AddRange(t.GetComponentsInChildren<Collider>(true));
            }

        // This component's own trigger is the interior volume that detects the player — never
        // disable it, or walking in would hide the roof and immediately stop detecting the exit.
        var own = GetComponent<Collider>();
        if (own != null) _colliders.Remove(own);

        foreach (var r in _renderers)
            _shadowModes.Add(r.shadowCastingMode);

        if (_renderers.Count == 0)
            Debug.LogWarning($"[RoofFader] {name} has no roof renderers assigned — nothing to hide.", this);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<PlayerEntity>() == null) return;
        _inside++;
        Apply();
    }

    void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<PlayerEntity>() == null) return;
        _inside = Mathf.Max(0, _inside - 1);
        Apply();
    }

    void Apply()
    {
        bool wantHidden = _inside > 0;
        if (keepInFirstPerson && OrbitCamera3D.FirstPersonActive) wantHidden = false;
        if (wantHidden == _hidden) return;
        _hidden = wantHidden;

        if (fadeDuration <= 0f) SetVisible(!_hidden);
    }

    void Update()
    {
        // Switching to first person while standing inside should bring the roof back.
        if (keepInFirstPerson) Apply();

        if (fadeDuration <= 0f) return;

        float target = _hidden ? 0f : 1f;
        if (Mathf.Approximately(_fade, target)) return;

        _fade = Mathf.MoveTowards(_fade, target, Time.deltaTime / Mathf.Max(0.01f, fadeDuration));
        SetAlpha(_fade);
        if (_fade <= 0.01f) SetVisible(false);
        else SetVisible(true);
    }

    void SetVisible(bool visible)
    {
        for (int i = 0; i < _renderers.Count; i++)
        {
            var r = _renderers[i];
            if (r == null) continue;
            r.enabled = visible;
            if (suppressShadows)
                r.shadowCastingMode = visible ? _shadowModes[i] : ShadowCastingMode.Off;
        }

        // A hidden roof must also stop existing to raycasts, or clicks and hovers land on invisible
        // geometry. This is what makes "invisible" and "click-through" the same state.
        for (int i = 0; i < _colliders.Count; i++)
        {
            var c = _colliders[i];
            if (c != null) c.enabled = visible;
        }
    }

    void SetAlpha(float a)
    {
        foreach (var r in _renderers)
        {
            if (r == null) continue;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                if (m.HasProperty("_BaseColor"))
                {
                    Color c = m.GetColor("_BaseColor");
                    c.a = a;
                    m.SetColor("_BaseColor", c);
                }
            }
        }
    }

    /// <summary>Never leave a roof hidden because the building got disabled mid-visit.</summary>
    void OnDisable()
    {
        _inside = 0;
        _hidden = false;
        _fade = 1f;
        SetVisible(true);
    }

    void OnDrawGizmosSelected()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        if (col is BoxCollider b) Gizmos.DrawCube(b.center, b.size);
    }
}
