using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Drops the selected object(s) straight down onto the ground (the walkable collider —
/// e.g. Island Ground) so their base rests on the surface. Select a bunch of nodes /
/// props / NPCs and snap them all at once. Re-runnable; uses Undo.
///
/// It raycasts down from high above each object, ignores the object's own colliders, and
/// seats the object's renderer-bounds bottom on the highest surface it hits.
///
/// Menu: Tools ▸ Wasteland ▸ Snap Selected To Ground
/// </summary>
public static class GroundSnapTool
{
    [MenuItem("Tools/Wasteland/Snap Selected To Ground")]
    public static void Snap()
    {
        var sel = Selection.gameObjects;
        if (sel == null || sel.Length == 0)
        {
            EditorUtility.DisplayDialog("Snap To Ground",
                "Select the object(s) to drop onto the ground, then run this again.", "OK");
            return;
        }

        int snapped = 0, missed = 0;
        foreach (var go in sel)
        {
            Undo.RecordObject(go.transform, "Snap To Ground");

            var ownColliders = go.GetComponentsInChildren<Collider>(true);
            Vector3 p = go.transform.position;

            // Raycast down from well above and take the highest surface that isn't this object.
            var hits = Physics.RaycastAll(new Ray(new Vector3(p.x, p.y + 1000f, p.z), Vector3.down),
                                          5000f, ~0, QueryTriggerInteraction.Ignore)
                       .Where(h => !ownColliders.Contains(h.collider) && h.normal.y > 0.3f)
                       .OrderByDescending(h => h.point.y)
                       .ToList();

            if (hits.Count == 0) { missed++; continue; }
            float groundY = hits[0].point.y;

            // Seat the object's lowest rendered point on the ground (handles pivots that
            // aren't at the base). Falls back to the pivot if there are no renderers.
            var rends = go.GetComponentsInChildren<Renderer>();
            float bottom = p.y;
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                bottom = b.min.y;
            }

            float offset = p.y - bottom;                     // how far the pivot sits above the base
            go.transform.position = new Vector3(p.x, groundY + offset, p.z);
            snapped++;
        }

        if (snapped > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[GroundSnapTool] Snapped {snapped} object(s) to the ground" +
                  (missed > 0 ? $"; {missed} had no ground beneath them." : "."));
    }
}
