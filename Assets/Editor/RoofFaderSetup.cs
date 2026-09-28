using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds RuneScape-style roof hiding to selected buildings in one go: finds the roof by name,
/// adds the RoofFader component, and auto-sizes an interior trigger from the building's bounds.
///
/// Menu:  Wasteland ▸ World ▸ Add Roof Fade To Selected
/// </summary>
public static class RoofFaderSetup
{
    /// <summary>Child names containing any of these are treated as roof geometry.</summary>
    static readonly string[] RoofWords =
        { "roof", "ceiling", "thatch", "tile", "shingle", "top", "canopy", "attic" };

    [MenuItem("Wasteland/World/Add Roof Fade To Selected", false, 30)]
    public static void AddToSelected()
    {
        var picked = Selection.gameObjects;
        if (picked == null || picked.Length == 0)
        {
            EditorUtility.DisplayDialog("Nothing Selected",
                "Select one or more building root objects in the Hierarchy first.", "OK");
            return;
        }

        var log = new StringBuilder("=== Add Roof Fade ===\n");
        int done = 0, noRoof = 0;

        foreach (var go in picked)
        {
            if (go.GetComponent<RoofFader>() != null)
            {
                log.AppendLine($"   skip (already has one)  {go.name}");
                continue;
            }

            Transform roof = FindRoof(go.transform);
            if (roof == null)
            {
                log.AppendLine($"   !! no roof found in       {go.name}   " +
                               $"(looked for: {string.Join(", ", RoofWords)})");
                noRoof++;
                continue;
            }

            Undo.RegisterFullObjectHierarchyUndo(go, "Add roof fade");

            // Interior trigger sized from the building's renderers, kept low so the player
            // capsule overlaps it while standing on the floor rather than on the roof.
            var bounds = WorldBounds(go.transform);
            var box = go.GetComponent<BoxCollider>();
            if (box == null) box = Undo.AddComponent<BoxCollider>(go);
            box.isTrigger = true;

            Vector3 localCenter = go.transform.InverseTransformPoint(bounds.center);
            Vector3 size = bounds.size;
            box.center = new Vector3(localCenter.x, localCenter.y - size.y * 0.25f, localCenter.z);
            box.size = new Vector3(size.x * 0.85f, Mathf.Max(size.y * 0.5f, 3f), size.z * 0.85f);

            var rf = Undo.AddComponent<RoofFader>(go);
            rf.roof = roof;
            rf.fadeDuration = 0f;
            rf.suppressShadows = true;
            rf.keepInFirstPerson = true;

            done++;
            log.AppendLine($"   ok  {go.name,-32} roof = '{roof.name}'   " +
                           $"trigger {box.size.x:0.0} x {box.size.y:0.0} x {box.size.z:0.0}");
        }

        log.AppendLine($"\n{done} building(s) set up" + (noRoof > 0 ? $", {noRoof} had no identifiable roof." : "."));
        if (noRoof > 0)
            log.AppendLine("For those, add RoofFader by hand and drag the roof object into the Roof slot.");

        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("Roof Fade",
            $"{done} building(s) set up.\n" +
            (noRoof > 0 ? $"{noRoof} had no recognisable roof — see Console.\n\n" : "\n") +
            "Walk into one in Play mode and the roof should vanish.\n" +
            "Adjust the Box Collider if it triggers too early or late.", "OK");
    }

    static Transform FindRoof(Transform root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root) continue;
            string n = t.name.ToLowerInvariant();
            foreach (var w in RoofWords)
                if (n.Contains(w)) return t;
        }
        return null;
    }

    static Bounds WorldBounds(Transform root)
    {
        var rs = root.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(root.position, Vector3.one * 4f);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
