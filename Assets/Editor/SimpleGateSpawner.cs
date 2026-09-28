using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Makes the four tier gates, fully configured, in a neat row in front of the Scene view.
/// You drag them where you want them. That's it.
///
/// No terrain reads, no heightmap writes, no boundary detection, nothing clever. Undoable.
///
/// Menu:  Wasteland ▸ World Tiers ▸ Make The Gates (place them yourself)
/// </summary>
public static class SimpleGateSpawner
{
    [MenuItem("Wasteland/World Tiers/Make The Gates (place them yourself)", false, -20)]
    public static void MakeGates()
    {
        var root = GameObject.Find("WorldTierGates");
        if (root == null)
        {
            root = new GameObject("WorldTierGates");
            Undo.RegisterCreatedObjectUndo(root, "Create WorldTierGates");
        }

        // Drop them in a row in front of wherever you're looking, so they're easy to grab.
        Vector3 origin = Vector3.zero;
        Vector3 right = Vector3.right;
        var sv = SceneView.lastActiveSceneView;
        if (sv != null && sv.camera != null)
        {
            origin = sv.pivot;
            right = sv.camera.transform.right;
            right.y = 0f;
            if (right.sqrMagnitude < 0.01f) right = Vector3.right;
            right.Normalize();
        }

        var made = new List<GameObject>();
        var existing = new List<string>();

        for (int tier = 2; tier <= 5; tier++)
        {
            string name = $"TierGate_T{tier}";
            if (root.transform.Find(name) != null) { existing.Add(name); continue; }

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(root.transform, true);
            go.transform.position = origin + right * ((tier - 3.5f) * 45f);

            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(30f, 18f, 4f);
            bc.center = new Vector3(0f, 9f, 0f);

            // Boss kill is the only gate — no level or kill requirements to bake in.
            var gate = go.AddComponent<TierGate>();
            gate.targetTier = tier;
            gate.gateId = $"t{tier}_main";
            gate.bossAliveMessage = Locked(tier);
            gate.unlockMessage = Unlocked(tier);
            gate.fogVisual = BuildArch(go, tier);

            made.Add(go);
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        if (made.Count > 0)
        {
            Selection.objects = made.ToArray();      // pre-selected so you can drag immediately
            if (sv != null) sv.FrameSelected();
        }

        string msg = made.Count > 0
            ? $"{made.Count} gate(s) created and selected:\n" +
              "  T2  needs combat 3, 2 kills\n" +
              "  T3  needs combat 10, 10 kills\n" +
              "  T4  needs combat 25, 30 kills\n" +
              "  T5  needs combat 50, 60 kills\n\n" +
              "Drag each one where you want it.\n" +
              "Hold Ctrl+Shift while dragging to snap onto the terrain surface."
            : "All four gates already exist in WorldTierGates.";
        if (existing.Count > 0) msg += $"\n\nSkipped (already there): {string.Join(", ", existing)}";

        Debug.Log("[Gates] " + msg.Replace("\n", " "));
        EditorUtility.DisplayDialog("Tier Gates", msg, "OK");
    }

    /// <summary>Simple visible arch so you can see what you're dragging.</summary>
    static GameObject BuildArch(GameObject parent, int tier)
    {
        var arch = new GameObject("PlaceholderArch");
        Undo.RegisterCreatedObjectUndo(arch, "Gate arch");
        arch.transform.SetParent(parent.transform, false);

        Color c = TierColor(tier);
        Bar(arch.transform, new Vector3(-13f, 6f, 0f), new Vector3(2.5f, 12f, 2.5f), c);
        Bar(arch.transform, new Vector3(13f, 6f, 0f), new Vector3(2.5f, 12f, 2.5f), c);
        Bar(arch.transform, new Vector3(0f, 13f, 0f), new Vector3(28f, 2.5f, 2.5f), c);
        return arch;
    }

    static void Bar(Transform parent, Vector3 pos, Vector3 scale, Color col)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = "Post";
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        Object.DestroyImmediate(g.GetComponent<Collider>());   // the gate's own trigger does the work

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader) { name = "GatePlaceholder" };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
        g.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static Color TierColor(int tier) => tier switch
    {
        2 => new Color(0.92f, 0.72f, 0.28f),   // Barren Plains gold
        3 => new Color(0.55f, 0.32f, 0.20f),   // Scorched brown
        4 => new Color(0.70f, 0.88f, 0.98f),   // Frozen blue-white
        _ => new Color(0.80f, 0.25f, 0.16f),   // Wasteland red
    };

    static string Locked(int tier) => tier switch
    {
        2 => "The gate holds fast. Beyond it the Greenbelt gives way to the Barren Plains — dust, heat and worse. Train harder.",
        3 => "Smoke rises past the gate. The Scorched Highlands take the unprepared first.",
        4 => "Ice rimes the gate. The Frozen Wastes bury their dead standing. Not yet.",
        5 => "The gate is warm to the touch. The Wasteland is what broke the world, and it is still hungry.",
        _ => "You feel unprepared for what lies ahead.",
    };

    static string Unlocked(int tier) => tier switch
    {
        2 => "The gate grinds open. The Barren Plains stretch out ahead — stay sharp, survivor.",
        3 => "The gate yields. The Scorched Highlands accept those hard enough to climb.",
        4 => "The gate cracks apart. The Frozen Wastes lie open, and the old world's secrets with them.",
        5 => "The gate falls. The Wasteland has been waiting for you.",
        _ => "The way is clear.",
    };
}
