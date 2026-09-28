using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Places one TierGate per tier boundary, at a single consistent bearing — the "giant gate
/// in the middle of the mountain range" layout from the concept art. Explore a tier, beat
/// its boss, level up, unlock the gate, move outward.
///
/// Boundaries are read from the PAINTED ALPHAMAPS, not from assumed radii. Whatever you
/// painted by hand is the source of truth, so the gates land exactly on your tier edges
/// even though those edges follow organic arcs rather than circles.
///
/// Writes nothing to the terrain — no heights, no alphamaps. It only creates GameObjects,
/// so it is fully undoable and safe to re-run (existing gates are skipped).
///
/// Menu:  Wasteland ▸ World Tiers ▸ 3 · Place Tier Gates At Painted Boundaries
/// </summary>
public static class WorldTierGatePlacer
{
    /// <summary>Compass bearing the gates sit on. 0 = due north, matching the concept art.</summary>
    const float GateBearingDeg = 0f;

    /// <summary>Tier layers occupy alphamap indices 0..TierLayerCount-1; anything above is cliff/detail.</summary>
    const int TierLayerCount = 5;

    const float SampleStepM = 1.5f;    // radial march resolution
    const int   SmoothSpan  = 4;       // samples either side used to reject speckle

    [MenuItem("Wasteland/World Tiers/3 · Place Tier Gates At Painted Boundaries", false, 3)]
    public static void Place()
    {
        var terrain = FindTerrain();
        if (terrain == null) { EditorUtility.DisplayDialog("No Terrain", "No Terrain in the open scene.", "OK"); return; }

        var td = terrain.terrainData;
        var tp = terrain.transform.position;
        int aRes = td.alphamapResolution;
        int layers = td.terrainLayers.Length;

        if (layers < TierLayerCount)
        {
            EditorUtility.DisplayDialog("Not Enough Layers",
                $"Expected at least {TierLayerCount} tier layers, found {layers}.\n\n" +
                "Paint the biomes first.", "OK");
            return;
        }

        float[,,] maps = td.GetAlphamaps(0, 0, aRes, aRes);

        // Start from the centroid of the PAINTED tier-1 band, not the terrain centre. The
        // hand-painted rings are centred well south of the terrain middle, so marching from
        // the middle starts already inside tier 3 and misses the inner boundaries entirely.
        Vector3 c = PaintedTierCentroid(maps, aRes, td, tp, 0)
                    ?? new Vector3(tp.x + td.size.x * 0.5f, 0f, tp.z + td.size.z * 0.5f);

        // ── march outward, recording where the dominant tier layer changes ──
        float rad = GateBearingDeg * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        float maxR = Mathf.Min(td.size.x, td.size.z) * 0.5f - 2f;

        var samples = new List<int>();
        var radii = new List<float>();
        for (float r = 0f; r < maxR; r += SampleStepM)
        {
            Vector3 p = c + dir * r;
            int t = DominantTier(maps, aRes, td, tp, p, layers);
            samples.Add(t);
            radii.Add(r);
        }

        var log = new StringBuilder("=== Place Tier Gates At Painted Boundaries ===\n");
        log.AppendLine($"Start point ({c.x:0.0}, {c.z:0.0})   " +
                       $"[centroid of the painted Tier 1 band; terrain centre is " +
                       $"({tp.x + td.size.x * 0.5f:0.0}, {tp.z + td.size.z * 0.5f:0.0})]");
        log.AppendLine($"bearing {GateBearingDeg:0}°   {samples.Count} samples to r={maxR:0} m");
        log.AppendLine($"tier under start point: {DominantTier(maps, aRes, td, tp, c, layers) + 1}");

        // A boundary counts when the tier is stable either side and steps UP. Bands can pinch
        // out along a given bearing (gold straight to white), so any increase counts, not just
        // +1 — otherwise those transitions are silently discarded. Deduped by target tier,
        // keeping the innermost crossing.
        var firstCrossing = new Dictionary<int, (float radius, int from)>();
        for (int i = SmoothSpan; i < samples.Count - SmoothSpan; i++)
        {
            int before = Majority(samples, i - SmoothSpan, i);
            int after = Majority(samples, i + 1, i + 1 + SmoothSpan);
            if (before < 0 || after < 0 || after <= before) continue;

            // a jump of more than one band means the ones between pinched out here;
            // register a crossing for each so every tier still gets its gate
            for (int t = before + 1; t <= after; t++)
                if (!firstCrossing.ContainsKey(t))
                    firstCrossing[t] = (radii[i], t - 1);
        }

        var boundaries = new List<(float radius, int from, int to)>();
        var targets = new List<int>(firstCrossing.Keys);
        targets.Sort();
        foreach (int t in targets)
            boundaries.Add((firstCrossing[t].radius, firstCrossing[t].from, t));

        log.AppendLine($"\ndetected {boundaries.Count} tier boundary/boundaries along that bearing:");
        foreach (var b in boundaries)
            log.AppendLine($"      r = {b.radius,6:0} m   tier {b.from + 1} → {b.to + 1}");

        if (boundaries.Count == 0)
        {
            log.AppendLine("\n!! No clean boundaries found. Either the bearing crosses unpainted ground,");
            log.AppendLine("   or the bands aren't ordered outward along it. Try a different GateBearingDeg.");
            Debug.LogWarning(log.ToString());
            EditorUtility.DisplayDialog("No Boundaries Found",
                "Couldn't find tier transitions along that bearing.\n\nSee Console.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Place Tier Gates",
            $"{boundaries.Count} gate(s) will be placed at the painted tier boundaries, " +
            $"on bearing {GateBearingDeg:0}°.\n\n" +
            "Nothing is written to the terrain — GameObjects only, fully undoable.\n\nContinue?",
            "Place Gates", "Cancel")) return;

        var root = GameObject.Find("WorldTierGates");
        if (root == null)
        {
            root = new GameObject("WorldTierGates");
            Undo.RegisterCreatedObjectUndo(root, "Create WorldTierGates");
        }

        int placed = 0, skipped = 0;
        foreach (var b in boundaries)
        {
            int targetTier = b.to + 1;                              // tier being entered, 1-based
            if (targetTier < 2) continue;                           // no gate into tier 1

            string name = $"TierGate_T{targetTier}";
            if (root.transform.Find(name) != null) { skipped++; continue; }

            Vector3 pos = c + dir * b.radius;
            pos.y = terrain.SampleHeight(pos) + tp.y;

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Place TierGate");
            go.transform.SetParent(root.transform, true);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(30f, 18f, 4f);                    // wide: it's a mountain pass
            bc.center = new Vector3(0f, 9f, 0f);

            // Boss kill is the only gate — assign Required Boss on the gate to arm it.
            var gate = go.AddComponent<TierGate>();
            gate.targetTier = targetTier;
            gate.gateId = $"t{targetTier}_main";
            gate.bossAliveMessage = LockedText(targetTier);
            gate.unlockMessage = UnlockText(targetTier);
            gate.fogVisual = BuildMarker(go, targetTier);

            placed++;
            log.AppendLine($"      {name,-16} r={b.radius,6:0} m  at {pos}  " +
                           "— assign this tier's boss to arm it");
        }

        log.AppendLine($"\n✅  {placed} gate(s) placed" + (skipped > 0 ? $", {skipped} already existed." : "."));
        log.AppendLine("      Drop your gate model as a child of each TierGate object and clear the placeholder.");

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("Tier Gates — Done",
            $"{placed} gate(s) placed at your painted boundaries.\n\n" +
            "Each is a trigger with a placeholder arch. Parent a real gate model under it " +
            "and delete the placeholder.\n\nCtrl+Z undoes everything. Ctrl+S keeps it.", "OK");
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HEIGHT-STEP MODE
    //  Each tier is a plateau at its own elevation with a slope between them, so the slope
    //  IS the tier boundary. This walks outward, finds each run of rising ground, and puts
    //  the gate at the middle of that ramp — the chokepoint between two plateaus.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Minimum rise, in metres, for a run to count as a tier step rather than noise.</summary>
    const float MinStepRise = 4f;

    [MenuItem("Wasteland/World Tiers/3b · Place Tier Gates At Height Steps", false, 4)]
    public static void PlaceAtSteps()
    {
        var terrain = FindTerrain();
        if (terrain == null) { EditorUtility.DisplayDialog("No Terrain", "No Terrain in the open scene.", "OK"); return; }

        var td = terrain.terrainData;
        var tp = terrain.transform.position;
        int aRes = td.alphamapResolution;
        float[,,] maps = td.GetAlphamaps(0, 0, aRes, aRes);

        Vector3 c = PaintedTierCentroid(maps, aRes, td, tp, 0)
                    ?? new Vector3(tp.x + td.size.x * 0.5f, 0f, tp.z + td.size.z * 0.5f);

        float rad = GateBearingDeg * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        float maxR = Mathf.Min(td.size.x, td.size.z) * 0.5f - 2f;

        // sample the surface along the bearing
        var hs = new List<float>();
        var rs = new List<float>();
        for (float r = 0f; r < maxR; r += SampleStepM)
        {
            Vector3 p = c + dir * r;
            hs.Add(terrain.SampleHeight(p) + tp.y);
            rs.Add(r);
        }

        var log = new StringBuilder("=== Place Tier Gates At Height Steps ===\n");
        log.AppendLine($"Start ({c.x:0.0}, {c.z:0.0})  bearing {GateBearingDeg:0}°  " +
                       $"{hs.Count} samples to r={maxR:0} m");

        // group contiguous rising ground into ramps
        var ramps = new List<(float rMid, float rise, float from, float to)>();
        int i2 = 1;
        while (i2 < hs.Count)
        {
            if (hs[i2] - hs[i2 - 1] <= 0.02f) { i2++; continue; }
            int start = i2 - 1;
            while (i2 < hs.Count && hs[i2] - hs[i2 - 1] > -0.02f) i2++;
            int end = i2 - 1;
            float rise = hs[end] - hs[start];
            if (rise >= MinStepRise)
                ramps.Add(((rs[start] + rs[end]) * 0.5f, rise, hs[start], hs[end]));
        }

        log.AppendLine($"\n{ramps.Count} rising step(s) of ≥{MinStepRise:0} m found:");
        foreach (var r in ramps)
            log.AppendLine($"      r ≈ {r.rMid,6:0} m   rise {r.rise,5:0.0} m   " +
                           $"({r.from:0.0} → {r.to:0.0} m)");

        if (ramps.Count == 0)
        {
            log.AppendLine("\n!! No steps found. Lower MinStepRise, or check the bearing crosses the plateaus.");
            Debug.LogWarning(log.ToString());
            EditorUtility.DisplayDialog("No Steps Found", "No tier steps along that bearing.\nSee Console.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Place Tier Gates At Height Steps",
            $"{ramps.Count} step(s) detected on bearing {GateBearingDeg:0}°.\n" +
            "A gate will sit at the middle of each ramp.\n\n" +
            "Nothing is written to the terrain — GameObjects only, undoable.\n\nContinue?",
            "Place Gates", "Cancel")) return;

        var root = GameObject.Find("WorldTierGates");
        if (root == null)
        {
            root = new GameObject("WorldTierGates");
            Undo.RegisterCreatedObjectUndo(root, "Create WorldTierGates");
        }

        int placed = 0, skipped = 0;
        for (int k = 0; k < ramps.Count; k++)
        {
            int targetTier = k + 2;                     // first step leads into tier 2
            if (targetTier > 5) break;

            string name = $"TierGate_T{targetTier}";
            if (root.transform.Find(name) != null) { skipped++; continue; }

            Vector3 pos = c + dir * ramps[k].rMid;
            pos.y = terrain.SampleHeight(pos) + tp.y;

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Place TierGate");
            go.transform.SetParent(root.transform, true);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(30f, 18f, 4f);
            bc.center = new Vector3(0f, 9f, 0f);

            // Boss kill is the only gate — assign Required Boss on the gate to arm it.
            var gate = go.AddComponent<TierGate>();
            gate.targetTier = targetTier;
            gate.gateId = $"t{targetTier}_main";
            gate.bossAliveMessage = LockedText(targetTier);
            gate.unlockMessage = UnlockText(targetTier);
            gate.fogVisual = BuildMarker(go, targetTier);

            placed++;
            log.AppendLine($"      {name,-16} r={ramps[k].rMid,6:0} m  at {pos}  " +
                           "— assign this tier's boss to arm it");
        }

        log.AppendLine($"\n✅  {placed} gate(s) placed" + (skipped > 0 ? $", {skipped} already existed." : "."));
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("Tier Gates — Done",
            $"{placed} gate(s) placed on the ramps between plateaus.\n\n" +
            "Delete any old gates first if the count looks wrong.\n\nCtrl+Z undoes it.", "OK");
    }

    /// <summary>
    /// World-space centroid of every alphamap texel where the given tier layer dominates.
    /// This is where the hand-painted rings are actually centred. Null if that band was
    /// never painted.
    /// </summary>
    static Vector3? PaintedTierCentroid(float[,,] maps, int aRes, TerrainData td, Vector3 tp, int tierLayer)
    {
        int layers = td.terrainLayers.Length;
        if (tierLayer >= layers) return null;

        double sx = 0, sz = 0;
        long n = 0;
        int tierMax = Mathf.Min(TierLayerCount, layers);

        for (int y = 0; y < aRes; y++)
            for (int x = 0; x < aRes; x++)
            {
                int best = -1; float bw = 0.0001f;
                for (int l = 0; l < tierMax; l++)
                    if (maps[y, x, l] > bw) { bw = maps[y, x, l]; best = l; }
                if (best != tierLayer) continue;

                sx += tp.x + (x / (float)(aRes - 1)) * td.size.x;
                sz += tp.z + (y / (float)(aRes - 1)) * td.size.z;
                n++;
            }

        if (n < 50) return null;
        return new Vector3((float)(sx / n), 0f, (float)(sz / n));
    }

    /// <summary>Strongest TIER layer at a world position, ignoring cliff/detail layers.</summary>
    static int DominantTier(float[,,] maps, int aRes, TerrainData td, Vector3 tp, Vector3 p, int layers)
    {
        float u = (p.x - tp.x) / td.size.x;
        float v = (p.z - tp.z) / td.size.z;
        if (u < 0f || u > 1f || v < 0f || v > 1f) return -1;

        int x = Mathf.Clamp(Mathf.RoundToInt(u * (aRes - 1)), 0, aRes - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(v * (aRes - 1)), 0, aRes - 1);

        int best = -1; float bw = 0.0001f;
        int n = Mathf.Min(TierLayerCount, layers);
        for (int l = 0; l < n; l++)
            if (maps[y, x, l] > bw) { bw = maps[y, x, l]; best = l; }
        return best;
    }

    static int Majority(List<int> s, int from, int to)
    {
        var count = new Dictionary<int, int>();
        for (int i = from; i < to && i < s.Count; i++)
        {
            if (s[i] < 0) continue;
            count.TryGetValue(s[i], out int c);
            count[s[i]] = c + 1;
        }
        int best = -1, bc = 0;
        foreach (var kv in count) if (kv.Value > bc) { bc = kv.Value; best = kv.Key; }
        return bc >= (to - from) / 2 ? best : -1;
    }

    /// <summary>Placeholder arch so the gate is visible in-scene until a real model is parented.</summary>
    static GameObject BuildMarker(GameObject parent, int tier)
    {
        var marker = new GameObject("PlaceholderArch");
        Undo.RegisterCreatedObjectUndo(marker, "Gate placeholder");
        marker.transform.SetParent(parent.transform, false);

        Color col = TierColor(tier);
        MakeBar(marker.transform, new Vector3(-12f, 6f, 0f), new Vector3(2f, 12f, 2f), col);
        MakeBar(marker.transform, new Vector3(12f, 6f, 0f), new Vector3(2f, 12f, 2f), col);
        MakeBar(marker.transform, new Vector3(0f, 13f, 0f), new Vector3(26f, 2f, 2f), col);
        return marker;
    }

    static void MakeBar(Transform parent, Vector3 pos, Vector3 scale, Color col)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = "Post";
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        Object.DestroyImmediate(g.GetComponent<Collider>());

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader) { name = "GatePlaceholder" };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
        g.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static Color TierColor(int tier) => tier switch
    {
        2 => new Color(0.92f, 0.72f, 0.28f),
        3 => new Color(0.55f, 0.32f, 0.20f),
        4 => new Color(0.70f, 0.88f, 0.98f),
        _ => new Color(0.80f, 0.25f, 0.16f),
    };

    static string LockedText(int tier) => tier switch
    {
        2 => "The gate holds. Beyond it the Greenbelt gives way to the Barren Plains — dust, heat and worse. Train harder.",
        3 => "Smoke rises past the gate. The Scorched Highlands take the unprepared first.",
        4 => "Ice rimes the gate. The Frozen Wastes bury their dead standing. Not yet.",
        5 => "The gate is warm to the touch. The Wasteland is what broke the world, and it is still hungry.",
        _ => "You feel unprepared for what lies ahead.",
    };

    static string UnlockText(int tier) => tier switch
    {
        2 => "The gate grinds open. The Barren Plains stretch out ahead — stay sharp, survivor.",
        3 => "The gate yields. The Scorched Highlands accept those hard enough to climb.",
        4 => "The gate cracks apart. The Frozen Wastes lie open, and the old world's secrets with them.",
        5 => "The gate falls. The Wasteland has been waiting for you.",
        _ => "The way is clear.",
    };

    static Terrain FindTerrain()
    {
        var g = GameObject.Find("Ground");
        var t = g != null ? g.GetComponent<Terrain>() : null;
        return t != null ? t : Terrain.activeTerrain;
    }
}
