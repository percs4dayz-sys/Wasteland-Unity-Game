using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Raycasts straight down through the Player's position and logs every collider
/// it passes through, with the surface height and full hierarchy path. Use it to
/// find a stray collider the player is standing ON when it should be lower — e.g.
/// the "floating at y=0.94" bug where an invisible surface sits above the visible
/// pavement.
///
/// Run it in EDIT mode with the Player roughly where it spawns.
/// Menu: Tools ▸ Wasteland ▸ Probe Ground Under Player
/// </summary>
public static class GroundProbe
{
    [MenuItem("Tools/Wasteland/Probe Ground Under Player")]
    public static void Probe()
    {
        var player = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include)
            .FirstOrDefault(m => m != null && (m.GetType().Name == "PlayerEntity" || m.GetType().Name == "Player3DController"))
            ?.transform;

        if (player == null)
        {
            EditorUtility.DisplayDialog("Probe Ground", "No Player found in this scene.", "OK");
            return;
        }

        Vector3 p = player.position;
        var hits = Physics.RaycastAll(new Ray(new Vector3(p.x, p.y + 50f, p.z), Vector3.down),
                                      200f, ~0, QueryTriggerInteraction.Ignore)
                   .OrderByDescending(h => h.point.y)   // highest surface first (what ClampToGround grabs)
                   .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"════ Ground under Player (player.y = {p.y:F3}) ════");
        if (hits.Count == 0) sb.AppendLine("  (nothing — no collider beneath the player at all)");
        foreach (var h in hits)
            sb.AppendLine($"  surface y={h.point.y:F3}  normalY={h.normal.y:F2}  →  {Path(h.collider.gameObject)}   ({h.collider.GetType().Name})");
        sb.AppendLine("The TOPMOST walkable surface is the one the player snaps onto.");

        Debug.Log(sb.ToString());
    }

    static string Path(GameObject go)
    {
        var t = go.transform; string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
