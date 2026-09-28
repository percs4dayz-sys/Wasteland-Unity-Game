using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// READ-ONLY diagnostic. Changes nothing. Logs everything needed to understand why the
/// player isn't seating on the ground: player position, the CharacterController capsule
/// extents, where the visual mesh sits relative to that capsule, the identified ground,
/// and every collider directly beneath the player (with surface normals).
///
/// Menu:  Wasteland ▸ World ▸ DIAGNOSE Spawn (read-only)
/// </summary>
public static class DiagnoseSpawn
{
    [MenuItem("Wasteland/World/DIAGNOSE Spawn (read-only)")]
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("===== SPAWN DIAGNOSTIC =====");

        var pc = Object.FindAnyObjectByType<Player3DController>();
        if (pc == null) { Debug.LogWarning("[Diagnose] No Player3DController in scene."); return; }
        var player = pc.gameObject;
        Vector3 pp = player.transform.position;
        sb.AppendLine($"PLAYER '{player.name}' pos = {V(pp)}   active={player.activeInHierarchy}");

        var cc = player.GetComponent<CharacterController>();
        if (cc != null)
        {
            float bottom = pp.y + cc.center.y - cc.height * 0.5f;
            float top    = pp.y + cc.center.y + cc.height * 0.5f;
            sb.AppendLine($"  CharacterController: height={cc.height} center.y={cc.center.y} radius={cc.radius} enabled={cc.enabled}");
            sb.AppendLine($"  Capsule world Y: bottom={bottom:0.00}  top={top:0.00}");
        }
        else sb.AppendLine("  (no CharacterController)");

        // Visual mesh vs pivot — catches a model whose feet hang below the capsule.
        var rends = player.GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            Bounds vb = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) vb.Encapsulate(rends[i].bounds);
            sb.AppendLine($"  VISUAL bounds: min.y={vb.min.y:0.00} max.y={vb.max.y:0.00} center={V(vb.center)} size={V(vb.size)}");
            sb.AppendLine($"    -> visual bottom is {(vb.min.y - pp.y):0.00} relative to the player pivot");
            foreach (var r in rends)
                sb.AppendLine($"      renderer '{PathOf(r.transform, player.transform)}' bounds.min.y={r.bounds.min.y:0.00} max.y={r.bounds.max.y:0.00} lScale={V(r.transform.lossyScale)}");
        }
        else sb.AppendLine("  (no Renderer under player)");

        // Identify ground (biggest horizontal footprint, active, not the player).
        Collider ground = null; float bestArea = 0f;
        foreach (var col in Object.FindObjectsByType<Collider>())
        {
            if (!col.gameObject.activeInHierarchy) continue;
            if (col.transform.IsChildOf(player.transform)) continue;
            float area = col.bounds.size.x * col.bounds.size.z;
            if (area > bestArea) { bestArea = area; ground = col; }
        }
        if (ground != null)
            sb.AppendLine($"GROUND guess: '{ground.gameObject.name}' ({ground.GetType().Name}) bounds y {ground.bounds.min.y:0.00}..{ground.bounds.max.y:0.00}  xz size {ground.bounds.size.x:0}x{ground.bounds.size.z:0}");
        else
            sb.AppendLine("GROUND guess: (none found)");

        // Everything directly beneath the player's XZ, top-down.
        sb.AppendLine($"RAYCAST straight down through player XZ ({pp.x:0.0}, {pp.z:0.0}):");
        float top0 = (ground != null ? ground.bounds.max.y : pp.y) + 1000f;
        var hits = Physics.RaycastAll(new Ray(new Vector3(pp.x, top0, pp.z), Vector3.down), 5000f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        if (hits.Length == 0) sb.AppendLine("  (NOTHING hit — no collider under the player at all!)");
        foreach (var h in hits)
            sb.AppendLine($"  y={h.point.y,8:0.00}  normal.y={h.normal.y,5:0.00}  '{h.collider.gameObject.name}'{(h.collider.transform.IsChildOf(player.transform) ? "  <-- PLAYER's own" : "")}");

        sb.AppendLine("============================");
        Debug.Log(sb.ToString());
        EditorUtility.DisplayDialog("Spawn Diagnostic",
            "Logged full details to the Console as a single [Diagnose] block.\n\nCopy that whole block back to Claude.", "OK");
    }

    static string V(Vector3 v) => $"({v.x:0.00}, {v.y:0.00}, {v.z:0.00})";

    static string PathOf(Transform t, Transform stop)
    {
        var s = t.name;
        for (var p = t.parent; p != null && p != stop.parent; p = p.parent) s = p.name + "/" + s;
        return s;
    }
}
