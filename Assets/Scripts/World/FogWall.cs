using System.Collections;
using UnityEngine;

/// <summary>
/// A vertical CURTAIN of fog sitting on a zone boundary (a bridge mouth, a river crossing) so you
/// can't see what's in the next tier area until you push through. It is deliberately thin in its
/// crossing direction — it blocks the sightline ACROSS the seam, but does NOT fog the ground/water
/// on your side, so you can still walk up to the edge and fish.
///
/// First time the player walks through it, it dissolves and stays cleared forever — persisted via
/// the player flag "fogwall_&lt;id&gt;" (saved in SaveData.flags). On reload, an already-crossed wall
/// starts fully clear.
///
/// The fog is a child ParticleSystem (built by FogWallTool). The collider is the trigger AND the
/// shape: resize the BoxCollider and the fog refills it automatically (OnValidate / Awake).
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class FogWall : MonoBehaviour
{
    [Tooltip("Unique id for this wall. Persists as the save flag 'fogwall_<id>'. MUST be unique per wall.")]
    public string id = "boundary";

    [Tooltip("Optional one-line message shown in chat when the fog parts.")]
    public string revealMessage = "The fog parts as you push through...";

    [Tooltip("Extra seconds to wait after emission stops before fully disabling (lets the cloud thin out).")]
    public float dissipatePadding = 0.5f;

    BoxCollider _trigger;
    ParticleSystem[] _fx;
    bool _cleared;

    string Flag => "fogwall_" + id;

    void Awake()
    {
        _trigger = GetComponent<BoxCollider>();
        _trigger.isTrigger = true;
        _fx = GetComponentsInChildren<ParticleSystem>(true);
        ApplyShape();
    }

    void Start()
    {
        // Already crossed in a previous session? Start fully clear.
        if (PlayerEntity.Instance != null && PlayerEntity.Instance.HasFlag(Flag)) ClearInstant();
        else ShowInstant();
    }

    void OnTriggerEnter(Collider other)
    {
        if (_cleared) return;
        if (other.GetComponentInParent<PlayerEntity>() == null) return;
        Cross();
    }

    /// <summary>Reveal the boundary: set the persistent flag and dissolve the fog.</summary>
    public void Cross()
    {
        if (_cleared) return;
        _cleared = true;
        PlayerEntity.Instance?.SetFlag(Flag);
        if (!string.IsNullOrEmpty(revealMessage))
            HUDController.Emit("<color=#CCCCCC>" + revealMessage + "</color>");
        if (isActiveAndEnabled) StartCoroutine(Dissipate());
        else ClearInstant();
    }

    IEnumerator Dissipate()
    {
        float maxLife = 1f;
        foreach (var ps in _fx)
        {
            var em = ps.emission; em.enabled = false;            // stop spawning new fog
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            maxLife = Mathf.Max(maxLife, ps.main.startLifetime.constantMax);  // existing puffs age out
        }
        yield return new WaitForSeconds(maxLife + dissipatePadding);
        ClearInstant();
    }

    void ShowInstant()
    {
        foreach (var ps in _fx)
        {
            ps.gameObject.SetActive(true);
            var em = ps.emission; em.enabled = true;
            if (!ps.isPlaying) ps.Play();
        }
    }

    void ClearInstant()
    {
        _cleared = true;
        foreach (var ps in _fx) if (ps) ps.gameObject.SetActive(false);
        if (_trigger != null) _trigger.enabled = false;   // no further trigger cost once revealed
    }

    /// <summary>Resize the fog particle box to match the BoxCollider, so resizing the collider
    /// auto-refills the curtain (called from Awake and in-editor via OnValidate).</summary>
    public void ApplyShape()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null) return;
        var systems = (_fx != null && _fx.Length > 0) ? _fx : GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in systems)
        {
            if (ps == null) continue;
            ps.transform.localPosition = box.center;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = box.size;
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying) ApplyShape();
    }

    void OnDrawGizmosSelected()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null) return;
        Gizmos.color = new Color(0.7f, 0.7f, 0.8f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(box.center, box.size);
    }
#endif
}
