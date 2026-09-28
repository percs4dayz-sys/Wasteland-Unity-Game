using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// READ-ONLY. Logs every combat NPC's real size and how it's set up, so we can see why one
/// (the "Walker") is giant: its world height, root + visual scale, whether HeightNormalizer3D
/// is attached/enabled, and its colliders. Changes nothing.
///
/// Menu:  Wasteland ▸ World ▸ DIAGNOSE Enemies (read-only)
/// </summary>
public static class DiagnoseEnemies
{
    [MenuItem("Wasteland/World/DIAGNOSE Enemies (read-only)")]
    public static void Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("===== ENEMY DIAGNOSTIC =====");

        var enemies = Object.FindObjectsByType<CombatTarget>();
        if (enemies.Length == 0) { Debug.LogWarning("[DiagEnemies] No CombatTarget in scene."); return; }

        foreach (var ct in enemies)
        {
            var go = ct.gameObject;
            sb.AppendLine($"\n'{go.name}'  pos={V(go.transform.position)}  rootScale={V(go.transform.lossyScale)}");

            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                sb.AppendLine($"   WORLD height={b.size.y:0.00}m  size={V(b.size)}");
                foreach (var r in rends)
                    sb.AppendLine($"     renderer '{r.gameObject.name}' ({r.GetType().Name}) h={r.bounds.size.y:0.00} lScale={V(r.transform.lossyScale)}");
            }
            else sb.AppendLine("   (no renderers)");

            var vis = go.transform.Find("Visual");
            if (vis != null) sb.AppendLine($"   Visual localScale={V(vis.localScale)} lossyScale={V(vis.lossyScale)}");

            var norm = go.GetComponentInChildren<HeightNormalizer3D>(true);
            sb.AppendLine(norm != null
                ? $"   HeightNormalizer3D: present on '{norm.gameObject.name}' enabled={norm.enabled} target={norm.targetHeight}"
                : "   HeightNormalizer3D: MISSING");

            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                sb.AppendLine($"   collider '{c.gameObject.name}' {c.GetType().Name} enabled={c.enabled} trigger={c.isTrigger} sizeY={c.bounds.size.y:0.00}");
        }

        sb.AppendLine("\n============================");
        Debug.Log(sb.ToString());
        EditorUtility.DisplayDialog("Enemy Diagnostic", "Logged to the Console as one [ENEMY DIAGNOSTIC] block. Copy it back to Claude.", "OK");
    }

    static string V(Vector3 v) => $"({v.x:0.00}, {v.y:0.00}, {v.z:0.00})";
}
