using UnityEngine;

/// <summary>
/// Rescales a model to a fixed real-world height, measuring from the AUTHORED mesh bounds
/// (sharedMesh.bounds × the renderer transform) rather than Renderer.bounds.
///
/// Why authored bounds: a skinned mesh's Renderer.bounds collapses to near-zero on the frames
/// before/while the Animator first poses it. Measuring then made this normalizer compute a huge
/// target/height factor and BLOW THE MODEL UP — the "giant enemy" bug. Authored mesh bounds are
/// pose-independent and valid every frame (edit mode and runtime), so the factor is always
/// correct and the result is idempotent: a model already at the target height stays put.
///
/// Self-corrects regardless of the current scale (computes target / current world height), so it
/// fixes freshly-built AND hand-placed enemies. Runs once, then disables itself.
/// </summary>
[DisallowMultipleComponent]
public class HeightNormalizer3D : MonoBehaviour
{
    [Tooltip("Real-world height in metres the model is rescaled to.")]
    public float targetHeight = 1.8f;

    [Tooltip("Drop the model so its base sits at the parent collider's bottom (the ground).")]
    public bool footAlign = true;

    void LateUpdate()
    {
        if (!TryAuthoredWorldBounds(out Bounds b)) { enabled = false; return; }  // no mesh to size
        if (b.size.y <= 1e-3f) return;                                           // not measurable yet

        transform.localScale *= targetHeight / b.size.y;

        if (footAlign && TryAuthoredWorldBounds(out b))
            transform.position += Vector3.up * (GroundY() - b.min.y);

        enabled = false;   // one-shot
    }

    /// <summary>World bounds built from each mesh's AUTHORED bounds (sharedMesh.bounds transformed
    /// by its renderer). Pose-independent, so it never collapses the way Renderer.bounds can.</summary>
    bool TryAuthoredWorldBounds(out Bounds total)
    {
        total = default; bool has = false;
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>())
            AddMesh(smr.sharedMesh, smr.transform, ref total, ref has);
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
            AddMesh(mf.sharedMesh, mf.transform, ref total, ref has);
        return has;
    }

    static void AddMesh(Mesh mesh, Transform t, ref Bounds total, ref bool has)
    {
        if (mesh == null) return;
        Vector3 c = mesh.bounds.center, e = mesh.bounds.extents;
        for (int i = 0; i < 8; i++)
        {
            var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x,
                                         (i & 2) == 0 ? -e.y : e.y,
                                         (i & 4) == 0 ? -e.z : e.z);
            var w = t.localToWorldMatrix.MultiplyPoint3x4(corner);
            if (!has) { total = new Bounds(w, Vector3.zero); has = true; }
            else total.Encapsulate(w);
        }
    }

    /// <summary>Ground = the parent enemy's collider bottom, falling back to its position.</summary>
    float GroundY()
    {
        var parent = transform.parent;
        var col = parent != null ? parent.GetComponent<Collider>() : null;
        if (col != null) return col.bounds.min.y;
        return parent != null ? parent.position.y : transform.position.y;
    }
}
