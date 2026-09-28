using System.Collections.Generic;
using UnityEngine;

/// <summary>Actual water footprint, including sloping rivers. Water is visual, never a walkable collider.</summary>
[RequireComponent(typeof(MeshFilter))]
public class WaterSurface : MonoBehaviour
{
    static readonly HashSet<WaterSurface> Active = new();
    Vector3[] vertices;
    int[] triangles;
    Bounds bounds;
    readonly Dictionary<Vector2Int, List<int>> cells = new();
    const float CellSize = 16f;
    static Vector2Int Cell(Vector3 p) => new(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));

    void OnEnable() => Refresh();
    void OnDisable() => Active.Remove(this);

    /// <summary>Rebuild after editing a water mesh or moving its transform.</summary>
    public void Refresh() { CacheMesh(); Active.Add(this); }

    void CacheMesh()
    {
        var mesh = GetComponent<MeshFilter>().sharedMesh;
        if (mesh == null) return;
        vertices = mesh.vertices;
        triangles = mesh.triangles;
        cells.Clear();
        bounds = new Bounds(transform.TransformPoint(vertices[0]), Vector3.zero);
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = transform.TransformPoint(vertices[i]);
            bounds.Encapsulate(vertices[i]);
        }
        for (int i = 0; i < triangles.Length; i += 3)
        {
            var a = vertices[triangles[i]]; var b = vertices[triangles[i + 1]]; var c = vertices[triangles[i + 2]];
            var min = Cell(Vector3.Min(a, Vector3.Min(b, c)));
            var max = Cell(Vector3.Max(a, Vector3.Max(b, c)));
            for (int x = min.x; x <= max.x; x++) for (int z = min.y; z <= max.y; z++)
            {
                var key = new Vector2Int(x, z);
                if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
                list.Add(i);
            }
        }
    }

    public bool TryHeight(Vector3 point, out float height)
    {
        height = float.NegativeInfinity;
        if (vertices == null) CacheMesh();
        if (vertices == null || point.x < bounds.min.x || point.x > bounds.max.x ||
            point.z < bounds.min.z || point.z > bounds.max.z) return false;
        bool found = false;
        if (!cells.TryGetValue(Cell(point), out var localTriangles)) return false;
        foreach (int i in localTriangles)
        {
            var a = vertices[triangles[i]]; var b = vertices[triangles[i + 1]]; var c = vertices[triangles[i + 2]];
            float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(det) < 0.00001f) continue;
            float u = ((b.z - c.z) * (point.x - c.x) + (c.x - b.x) * (point.z - c.z)) / det;
            float v = ((c.z - a.z) * (point.x - c.x) + (a.x - c.x) * (point.z - c.z)) / det;
            if (u < -0.0001f || v < -0.0001f || u + v > 1.0001f) continue;
            height = Mathf.Max(height, u * a.y + v * b.y + (1 - u - v) * c.y);
            found = true;
        }
        return found;
    }

    public static float HeightAt(Vector3 p, float seaLevel)
    {
        float height = seaLevel;
        foreach (var water in Active)
            if (water != null && water.TryHeight(p, out float y)) height = Mathf.Max(height, y);
        return height;
    }

    public static bool Blocks(Vector3 feet, float seaLevel, Transform player)
    {
        float water = HeightAt(feet, seaLevel);
        foreach (var terrain in Terrain.activeTerrains)
        {
            var o = terrain.transform.position; var size = terrain.terrainData.size;
            if (feet.x < o.x || feet.x > o.x + size.x || feet.z < o.z || feet.z > o.z + size.z) continue;
            if (terrain.SampleHeight(feet) + o.y >= water - 0.04f) return false;
            // A bridge or platform is safe only if the player can step onto its top.
            foreach (var hit in Physics.RaycastAll(feet + Vector3.up * 0.5f, Vector3.down,
                         Mathf.Max(0.6f, feet.y + 0.5f - water), ~0, QueryTriggerInteraction.Ignore))
            {
                if (player != null && hit.transform.IsChildOf(player)) continue;
                if (hit.collider is TerrainCollider || hit.collider is CharacterController ||
                    hit.collider.GetComponentInParent<WaterSurface>() != null ||
                    hit.collider.GetComponentInParent<ResourceNode>() != null) continue;
                if (hit.normal.y >= 0.5f && hit.point.y >= water + 0.04f) return false;
            }
            return true;
        }
        return false;
    }
}
