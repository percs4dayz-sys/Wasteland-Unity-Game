using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Click-to-move route planner. Finds a path over whatever navmesh is loaded (its own baked data, or
/// a scene's WorldNavMesh) and hands Player3DController one corner at a time, so a click walks round
/// walls, fences and buildings instead of grinding into them. With no navmesh here, or no route to
/// the spot, it falls back to walking straight at it, so a click always does something.
/// Player3DController adds one to the player automatically.
/// </summary>
public class BlackwaterNavigator : MonoBehaviour
{
    public NavMeshData data;   // optional: a world that ships its own navmesh (Blackwater) sets this
    NavMeshDataInstance instance;
    NavMeshPath path;
    Vector3[] corners;
    int corner;
    Vector3 lastTarget;
    float nextPlan;

    void OnEnable() { path = new NavMeshPath(); if (data != null) instance = NavMesh.AddNavMeshData(data); }
    void OnDisable() { if (instance.valid) instance.Remove(); }

    public void Plan(Vector3 target, float stoppingDistance)
    {
        if (corners != null && (target - lastTarget).sqrMagnitude < .5f && Time.time < nextPlan) return;
        lastTarget = target; nextPlan = Time.time + .3f; corner = 0;
        if (path == null) path = new NavMeshPath();

        bool routed = NavMesh.SamplePosition(transform.position, out var start, 4f, NavMesh.AllAreas)
                   && NavMesh.SamplePosition(target, out var end, Mathf.Max(4f, stoppingDistance), NavMesh.AllAreas)
                   && NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path)
                   && path.status != NavMeshPathStatus.PathInvalid && path.corners.Length > 0;
        if (routed)
        {
            corners = path.corners;
            // A partial route stops short at the closest reachable point; finish on the click itself so
            // the walk doesn't end early for no visible reason.
            if (path.status == NavMeshPathStatus.PathPartial)
            {
                System.Array.Resize(ref corners, corners.Length + 1);
                corners[corners.Length - 1] = target;
            }
            corner = corners.Length > 1 ? 1 : 0;
        }
        else corners = new[] { target };   // no navmesh here, or nowhere near one: walk straight at it
    }

    public Vector3 Direction(float stop, out bool arrived)
    {
        arrived = corners == null || corners.Length == 0;
        if (arrived) return Vector3.zero;
        while (corner < corners.Length)
        {
            var delta = corners[corner] - transform.position; delta.y = 0;
            float reach = corner == corners.Length - 1 ? Mathf.Max(.3f, stop) : .65f;
            if (delta.magnitude > reach) return delta.normalized;
            corner++;
        }
        arrived = true; return Vector3.zero;
    }
}
