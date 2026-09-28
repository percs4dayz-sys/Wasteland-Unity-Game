using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Dresses the Broken Crescent's tier borders with the September wall kits.
///
/// WHERE the borders run was settled on 2026-09-24: BC_TierBoundary_Continuous is a staircase of plain
/// 5.8 m concrete pillars on a 4 m grid along every partition seam, walking-audited so nothing past T1
/// was reachable with the gates shut. It works, but it looks like a placeholder. This keeps those exact
/// seams and replaces the look:
///   1. reads each pillar back out of the combined blockade meshes (24 verts per pillar),
///   2. chains the pillars into lines, smooths them, and lays that border's wall family along each
///      (seated and leaned to the ground, 10% overlap, a pillar piece at every bend to plug the wedge),
///      leaving the gate openings clear and skipping faces too steep to stand on (>50°), where the
///      cliff is the wall,
///   3. hides the old pillars and turns their colliders off, so you stop at the wall you can see,
///   4. audits the seal: probes across every wall line for a gap a player could fit through, then
///      flood-fills the walkable ground from each tier (gates shut) and checks the next tiers' boss
///      arenas are unreachable. If anything leaks, the old pillar colliders are switched back on
///      (invisibly) so the border is never weaker than it was.
///
/// Everything new sits under BC_TierBorderWalls. "Remove" deletes it and restores the old pillars.
/// Never touches terrain.  Menu: Wasteland ▸ Broken Crescent ▸ Build / Remove Tier Border Walls
/// </summary>
public static class BCTierBorderWalls
{
    const string RootName     = "BC_TierBorderWalls";
    const string OldRootName  = "BC_TierBoundary_Continuous";
    const float  Grid         = 4f;      // the old pillar grid
    const float  StandDegrees = 50f;     // steeper ground is left to the cliff
    const float  BendDegrees  = 22f;     // a sharper turn gets a pillar piece to plug the wedge

    struct Style { public string family; public string[] run, accents; }

    // blockade mesh → the tier it keeps you out of → that border's wall family
    static readonly (string blockade, int into, Style style)[] Borders =
    {
        ("T1_ContinuousBlockade", 2, new Style { family = "car",      run = new[] { "BC_Wall_Car_ModuleA", "BC_Wall_Car_ModuleB", "BC_Wall_Car_Damaged" },
                                                                        accents = new[] { "BC_Wall_Car_Corner" } }),
        ("T2_ContinuousBlockade", 3, new Style { family = "junk",     run = new[] { "BC_Wall_Garbage_Module", "BC_Wall_Garbage_Damaged" },
                                                                        accents = new[] { "BC_Wall_Garbage_Spire", "BC_Wall_Garbage_Corner" } }),
        ("T3_ContinuousBlockade", 4, new Style { family = "concrete", run = new[] { "BC_Wall_Concrete_ModuleA", "BC_Wall_Concrete_ModuleB" },
                                                                        accents = new[] { "BC_Wall_Concrete_Pillar", "BC_Wall_Concrete_Corner" } }),
        ("T4_ContinuousBlockade", 5, new Style { family = "bone",     run = new[] { "BC_Wall_Bone_Module", "BC_Wall_Scrap_Damaged", "BC_Wall_Bone_Damaged" },
                                                                        accents = new[] { "BC_Wall_Scrap_Tall", "BC_Wall_Scrap_Corner" } }),
    };

    // ── menu ─────────────────────────────────────────────────────────────
    [MenuItem("Wasteland/Broken Crescent/Build Tier Border Walls")]
    public static void BuildMenu() => Debug.Log(Build());

    [MenuItem("Wasteland/Broken Crescent/Remove Tier Border Walls")]
    public static void RemoveMenu() => Debug.Log(Remove());

    public static string Remove()
    {
        var root = GameObject.Find(RootName);
        if (root != null) Undo.DestroyObjectImmediate(root);
        SetOldPillars(visible: true, solid: true);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        return "[BorderWalls] Removed; old pillar blockade restored.";
    }

    public static string Build()
    {
        var report = new StringBuilder();
        var oldRoot = GameObject.Find(OldRootName);
        if (oldRoot == null) return $"No {OldRootName} in the open scene — nothing to follow.";
        var existing = GameObject.Find(RootName);
        if (existing != null) Undo.DestroyObjectImmediate(existing);
        SetOldPillars(visible: true, solid: true);   // start from the proven state

        float sea = SeaLevel();
        var gates = FindGates(report);
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build tier border walls");
        _probes.Clear();
        var rng = new System.Random(20260925);

        foreach (var (blockade, into, style) in Borders)
        {
            var t = oldRoot.transform.Find(blockade);
            var mf = t != null ? t.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) { report.AppendLine($"{blockade}: missing"); continue; }

            var cells = PillarCells(mf);
            var chains = ChainCells(cells);
            var parent = new GameObject($"T{into - 1}|T{into} border ({style.family} walls)").transform;
            parent.SetParent(root.transform, false);

            int modules = 0, accents = 0; float walled = 0f;
            foreach (var chain in chains)
            {
                var line = Simplify(Smooth(chain, 3), 1.5f);
                var (m, a, len) = LayWall(line, style, parent, gates, sea, rng);
                modules += m; accents += a; walled += len;
            }
            report.AppendLine($"T{into - 1}|T{into} ({style.family}): {cells.Count} old pillars → {chains.Count} lines, " +
                              $"{walled:F0} m walled with {modules} modules + {accents} pillar pieces");
        }

        // Hide the placeholder pillars and let the new walls do the blocking. Wherever the new walls
        // don't reach (cliff faces, gate flanks, line ends), the old pillars stay as an invisible
        // collider-only filler — so there's no invisible wall in front of a visible one, and the seal
        // is never weaker than before. Then prove it; any leak pulls the nearby pillars into the filler.
        SetOldPillars(visible: false, solid: false);
        CollectSeam();
        Physics.SyncTransforms();
        var uncovered = UncoveredPillars(oldRoot, root);
        var filler = BuildFiller(root, uncovered);
        report.AppendLine($"Seam filler: {uncovered.Count} old pillars the new walls don't cover stay as invisible colliders");

        var (gapLine, gaps) = GapCheck();
        report.AppendLine(gapLine);
        string flood = ""; int leaks = 0;
        for (int pass = 0; pass < 5; pass++)
        {
            (flood, leaks) = FloodAudit(sea);
            if (leaks == 0) break;
            int added = 0;
            foreach (var p in _leakPoints)
                foreach (var pil in AllPillars(oldRoot))
                    if (Vector2.Distance(new Vector2(pil.center.x, pil.center.z), p) < 14f && uncovered.Add(pil)) added++;
            report.AppendLine($"  pass {pass + 1}: {flood}  → +{added} filler pillars at the leaks");
            if (added == 0) break;
            filler = BuildFiller(root, uncovered, filler);
            Physics.SyncTransforms();
        }
        report.AppendLine(flood);

        if (leaks > 0)
        {
            SetOldPillars(visible: false, solid: true);
            report.AppendLine("⚠ Still leaking — the full old pillar collider is back ON (invisible) as the seal.");
        }
        else report.AppendLine("✓ Sealed: new walls + seam filler; old pillars hidden and non-solid (Remove restores them).");

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        return report.ToString();
    }

    // ── seam filler ──────────────────────────────────────────────────────
    class Pillar { public Vector3 center; public Vector3[] verts; public int[] tris; }
    static readonly Dictionary<MeshFilter, List<Pillar>> _pillarCache = new();

    // Every old pillar as its own little box (24 verts, 36 indices), in world space.
    static IEnumerable<Pillar> AllPillars(GameObject oldRoot)
    {
        foreach (var (blockade, _, _) in Borders)
        {
            var mf = oldRoot.transform.Find(blockade)?.GetComponent<MeshFilter>();
            if (mf == null) continue;
            if (!_pillarCache.TryGetValue(mf, out var list))
            {
                list = new List<Pillar>();
                var v = mf.sharedMesh.vertices; var t = mf.sharedMesh.triangles;
                for (int i = 0, k = 0; i + 24 <= v.Length && k + 36 <= t.Length; i += 24, k += 36)
                {
                    var pv = new Vector3[24]; var sum = Vector3.zero;
                    for (int j = 0; j < 24; j++) { pv[j] = mf.transform.TransformPoint(v[i + j]); sum += pv[j]; }
                    var pt = new int[36];
                    for (int j = 0; j < 36; j++) pt[j] = t[k + j] - i;
                    list.Add(new Pillar { center = sum / 24f, verts = pv, tris = pt });
                }
                _pillarCache[mf] = list;
            }
            foreach (var p in list) yield return p;
        }
    }

    // A pillar is "covered" when a new wall's collider sits over its middle.
    static HashSet<Pillar> UncoveredPillars(GameObject oldRoot, GameObject root)
    {
        _pillarCache.Clear();
        var ours = new HashSet<Collider>(root.GetComponentsInChildren<Collider>());
        var set = new HashSet<Pillar>();
        var buf = new Collider[16];
        foreach (var p in AllPillars(oldRoot))
        {
            if (!Ground(p.center, out float gy)) continue;
            int n = Physics.OverlapBoxNonAlloc(new Vector3(p.center.x, gy + 1.2f, p.center.z), new Vector3(1.4f, 1f, 1.4f), buf,
                                               Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            bool covered = false;
            for (int i = 0; i < n; i++) if (ours.Contains(buf[i])) { covered = true; break; }
            if (!covered) set.Add(p);
        }
        return set;
    }

    // One collider-only mesh (no renderer) built from the uncovered pillars; saved as an asset so the
    // scene keeps it.
    const string FillerAsset = SeptemberDeliveryBuilder.Out + "/BorderSeamFiller.asset";
    static MeshCollider BuildFiller(GameObject root, HashSet<Pillar> pillars, MeshCollider existing = null)
    {
        var verts = new List<Vector3>(); var tris = new List<int>();
        foreach (var p in pillars)
        {
            int b = verts.Count; verts.AddRange(p.verts);
            foreach (int t in p.tris) tris.Add(b + t);
        }
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(FillerAsset);
        if (mesh == null) { mesh = new Mesh { name = "BorderSeamFiller" }; AssetDatabase.CreateAsset(mesh, FillerAsset); }
        mesh.Clear();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);

        var mc = existing;
        if (mc == null)
        {
            var go = new GameObject("Seam filler (invisible colliders where no wall stands)");
            go.transform.SetParent(root.transform, false);
            mc = go.AddComponent<MeshCollider>();
        }
        mc.sharedMesh = null; mc.sharedMesh = mesh;
        return mc;
    }

    // ── the old pillars ──────────────────────────────────────────────────
    static void SetOldPillars(bool visible, bool solid)
    {
        var oldRoot = GameObject.Find(OldRootName);
        if (oldRoot == null) return;
        foreach (var (blockade, _, _) in Borders)
        {
            var t = oldRoot.transform.Find(blockade);
            if (t == null) continue;
            var r = t.GetComponent<Renderer>(); if (r != null) { Undo.RecordObject(r, "Border walls"); r.enabled = visible; }
            var c = t.GetComponent<Collider>(); if (c != null) { Undo.RecordObject(c, "Border walls"); c.enabled = solid; }
        }
    }

    // Each pillar is a 24-vertex box in the combined mesh; its XZ centre sits on the 4 m grid.
    static List<Vector2Int> PillarCells(MeshFilter mf)
    {
        var v = mf.sharedMesh.vertices;
        var cells = new HashSet<Vector2Int>();
        for (int i = 0; i + 24 <= v.Length; i += 24)
        {
            Vector3 sum = Vector3.zero;
            for (int k = 0; k < 24; k++) sum += mf.transform.TransformPoint(v[i + k]);
            var c = sum / 24f;
            cells.Add(new Vector2Int(Mathf.RoundToInt(c.x / Grid), Mathf.RoundToInt(c.z / Grid)));
        }
        return cells.ToList();
    }

    // Greedy walk over the pillar grid: from a line end, keep stepping to the nearest unvisited
    // neighbouring pillar (straight on preferred), which follows the staircase one pillar at a time.
    static List<List<Vector3>> ChainCells(List<Vector2Int> cells)
    {
        var left = new HashSet<Vector2Int>(cells);
        var chains = new List<List<Vector3>>();
        int Degree(Vector2Int c) { int n = 0; for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) if ((dx != 0 || dz != 0) && left.Contains(c + new Vector2Int(dx, dz))) n++; return n; }

        while (left.Count > 0)
        {
            var start = left.OrderBy(Degree).ThenBy(c => c.x).ThenBy(c => c.y).First();
            var chain = new List<Vector3>();
            var cur = start; Vector2 heading = Vector2.zero;
            left.Remove(cur); chain.Add(World(cur));
            while (true)
            {
                Vector2Int? best = null; float bestScore = float.MaxValue;
                for (int dx = -2; dx <= 2; dx++)
                for (int dz = -2; dz <= 2; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    var n = cur + new Vector2Int(dx, dz);
                    if (!left.Contains(n)) continue;
                    var step = new Vector2(dx, dz);
                    if (heading != Vector2.zero && Vector2.Dot(heading, step.normalized) < -0.2f) continue;   // never double back
                    float score = step.magnitude;
                    if (score < bestScore) { bestScore = score; best = n; }
                }
                if (best == null) break;
                heading = ((Vector2)(best.Value - cur)).normalized;
                cur = best.Value; left.Remove(cur); chain.Add(World(cur));
            }
            // Stray 1–2 pillar fragments beside a line are its staircase corners — the line's wall covers
            // them (and the audits would catch a real hole).
            if (chain.Count >= 3) chains.Add(chain);
        }
        return chains;
    }

    static Vector3 World(Vector2Int c) => new(c.x * Grid, 0f, c.y * Grid);

    // ── line shaping ─────────────────────────────────────────────────────
    static List<Vector3> Smooth(List<Vector3> pts, int passes)
    {
        var p = pts;
        for (int pass = 0; pass < passes && p.Count > 2; pass++)
        {
            var n = new List<Vector3>(p.Count) { p[0] };
            for (int i = 1; i < p.Count - 1; i++) n.Add((p[i - 1] + p[i] * 2f + p[i + 1]) * 0.25f);
            n.Add(p[^1]);
            p = n;
        }
        return p;
    }

    static List<Vector3> Simplify(List<Vector3> pts, float tol)
    {
        if (pts.Count < 3) return pts;
        var keep = new bool[pts.Count]; keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>(); stack.Push((0, pts.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            float worst = 0f; int at = -1;
            for (int i = a + 1; i < b; i++)
            {
                float d = DistToSegment(pts[i], pts[a], pts[b]);
                if (d > worst) { worst = d; at = i; }
            }
            if (at >= 0 && worst > tol) { keep[at] = true; stack.Push((a, at)); stack.Push((at, b)); }
        }
        return pts.Where((p, i) => keep[i]).ToList();
    }

    static float DistToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = new Vector2(b.x - a.x, b.z - a.z); var ap = new Vector2(p.x - a.x, p.z - a.z);
        float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(ap, ab) / ab.sqrMagnitude);
        return (ap - ab * t).magnitude;
    }

    // ── wall laying ──────────────────────────────────────────────────────
    static (int modules, int accents, float metres) LayWall(List<Vector3> line, Style style, Transform parent,
        List<Bounds> gates, float sea, System.Random rng)
    {
        var run = style.run.Select(LoadWall).Where(p => p != null).ToArray();
        var acc = style.accents.Select(LoadWall).Where(p => p != null).ToArray();
        if (run.Length == 0 || line.Count < 2) return (0, 0, 0f);

        var cum = new List<float> { 0f };
        for (int i = 1; i < line.Count; i++) cum.Add(cum[i - 1] + Flat(line[i] - line[i - 1]).magnitude);
        float length = cum[^1];
        Vector3 At(float s)
        {
            s = Mathf.Clamp(s, 0f, length);
            int i = cum.FindIndex(c => c >= s); if (i <= 0) return line[0];
            float t = (s - cum[i - 1]) / Mathf.Max(1e-4f, cum[i] - cum[i - 1]);
            return Vector3.Lerp(line[i - 1], line[i], t);
        }

        // A single pillar-length line (an isolated stub) just gets a pillar piece.
        if (length < 3f)
        {
            if (acc.Length > 0 && !InGate(line[0], 1.5f, gates) && Standable(line[0], sea))
            { Place(acc[rng.Next(acc.Length)], line[0], Vector3.right, 1f, parent); return (0, 1, 0f); }
            return (0, 0, 0f);
        }

        int modules = 0, accents = 0; float walled = 0f;
        Vector3? lastDir = null;
        float s0 = 0f;
        while (s0 < length - 0.5f)
        {
            var prefab = run[rng.Next(run.Length)];
            float scale = 0.95f + (float)rng.NextDouble() * 0.12f;
            float w = Width(prefab) * scale;
            // Don't run a module far past the end of the line (that's where gate openings are).
            float step = Mathf.Min(w, length - s0 + w * 0.15f);
            if (step < w * 0.6f)
            {
                if (acc.Length > 0 && !InGate(At(length), 2f, gates) && Standable(At(length), sea))
                { Place(acc[rng.Next(acc.Length)], At(length), lastDir ?? Vector3.right, 1f, parent); accents++; }
                break;
            }
            var a = At(s0); var b = At(s0 + step);
            var dir = Flat(b - a); if (dir.sqrMagnitude < 1e-4f) { s0 += 1f; continue; }
            dir.Normalize();
            var mid = (a + b) * 0.5f;

            if (!InGate(mid, w * 0.5f, gates) && Standable(mid, sea))
            {
                if (lastDir.HasValue && acc.Length > 0 && Vector3.Angle(lastDir.Value, dir) > BendDegrees)
                { Place(acc[rng.Next(acc.Length)], a, dir, 1f, parent); accents++; }
                Place(prefab, mid, dir, scale, parent, rng.NextDouble() < 0.5);
                modules++; walled += step;
                lastDir = dir;
                var n = Vector3.Cross(dir, Vector3.up);
                for (float s = s0; s < s0 + step * 0.9f; s += 0.75f) _probes.Add((At(s), n));
            }
            else lastDir = null;
            s0 += step * 0.9f;   // 10% overlap: no light between modules
        }
        return (modules, accents, walled);
    }

    // Only the doorway itself (the shutters' footprint, plus a little) is kept clear — the walls run right up
    // to the gate towers so nothing slips past beside them.
    static bool InGate(Vector3 p, float reach, List<Bounds> gates) =>
        gates.Any(g => { var c = g.ClosestPoint(new Vector3(p.x, g.center.y, p.z)); return Flat(p - c).magnitude < reach + 0.75f; });

    // Ground a player could stand on: above the sea and gentler than StandDegrees.
    static bool Standable(Vector3 p, float sea)
    {
        var t = TerrainAt(p);
        if (t == null) return false;
        var o = t.transform.position; var s = t.terrainData.size;
        float y = t.SampleHeight(p) + o.y;
        if (y < sea + 0.2f) return false;
        float steep = t.terrainData.GetSteepness((p.x - o.x) / s.x, (p.z - o.z) / s.z);
        return steep < StandDegrees;
    }

    static void Place(GameObject prefab, Vector3 at, Vector3 dir, float scale, Transform parent, bool flip = false)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(go, "Place border wall");
        var box = prefab.GetComponent<BoxCollider>();
        float w = (box != null ? box.size.x : 6f) * scale, depth = (box != null ? box.size.z : 2f) * scale;
        var fwd = Vector3.Cross(dir, Vector3.up);
        if (flip) fwd = -fwd;
        var right = Vector3.Cross(Vector3.up, fwd);   // the wall's local +X after rotation

        // Lean along the slope (up to 25°), then sink just far enough that no corner of the footprint
        // floats: sample the ground under both ends, the middle, and the front and back faces.
        Ground(at - right * w * 0.45f, out float yl); Ground(at + right * w * 0.45f, out float yr);
        float tan = Mathf.Tan(Mathf.Clamp(Mathf.Atan2(yr - yl, w * 0.9f), -25f * Mathf.Deg2Rad, 25f * Mathf.Deg2Rad));
        float baseY = float.MaxValue;
        foreach (float s in new[] { -0.45f, 0f, 0.45f })
        foreach (float f in new[] { -0.4f, 0f, 0.4f })
        {
            var p = at + right * (s * w) + fwd * (f * depth);
            if (!Ground(p, out float g)) continue;
            baseY = Mathf.Min(baseY, g - s * w * tan);   // wall bottom at this point must not be above ground
        }
        if (baseY == float.MaxValue) Ground(at, out baseY);

        go.transform.SetPositionAndRotation(new Vector3(at.x, baseY - 0.2f, at.z),
            Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 0f, Mathf.Atan(tan) * Mathf.Rad2Deg));
        go.transform.localScale = Vector3.one * scale;
    }

    // ── audits ───────────────────────────────────────────────────────────
    // Every 0.75 m along each walled stretch, sweep a player-sized sphere ACROSS the line at knee and
    // chest height; one that gets through without touching a wall is a gap.
    static readonly List<(Vector3 p, Vector3 n)> _probes = new();

    static (string, int) GapCheck()
    {
        int gaps = 0; var at = new List<Vector3>();
        foreach (var (p, n) in _probes)
        {
            if (!Ground(p, out float gy)) continue;
            // A player-sized capsule (knee to head height) swept across the line: if it gets through
            // untouched, so could the player. (A knee-high stretch can't be stepped over, so it's not a gap.)
            var a = new Vector3(p.x, gy + 0.5f, p.z) - n * 4f; var b = a + Vector3.up * 1.1f;
            if (!Physics.CapsuleCast(a, b, 0.3f, n, 8f, ~0, QueryTriggerInteraction.Ignore))
            { gaps++; if (at.Count < 10) at.Add(p); }
        }
        return ($"Gap check: {_probes.Count} points along the walls, {gaps} a player could slip through" +
                (at.Count > 0 ? " — e.g. " + string.Join(", ", at.Select(v => $"({v.x:F0},{v.z:F0})")) : ""), gaps);
    }

    // Flood-fill the walkable ground (3 m cells, player-sized clearance, 45° climb limit, drops allowed)
    // from inside each tier with the gates shut, and check no later tier's boss arena is reachable. A
    // leak is traced back along the flood to where it crossed the old seam, so it can be fixed.
    static readonly List<Vector2> _leakPoints = new();

    static (string, int) FloodAudit(float sea)
    {
        _leakPoints.Clear();
        var bosses = BossSpots();
        if (bosses.Count < 2) return ("Flood audit: skipped (boss arenas not found)", 0);
        var sb = new StringBuilder("Flood audit (gates shut): ");
        int leaks = 0;
        var cache = new Dictionary<Vector2Int, (bool ok, float y)>();
        for (int tier = 1; tier <= 4; tier++)
        {
            if (!bosses.TryGetValue(tier, out var from)) continue;
            var parent = Flood(from, sea, cache);
            var leaked = new List<string>();
            foreach (var b in bosses.Where(b => b.Key > tier))
            {
                var hit = parent.Keys.Where(c => (new Vector2(c.x * Cell - b.Value.x, c.y * Cell - b.Value.z)).magnitude < 10f)
                                     .Select(c => (Vector2Int?)c).FirstOrDefault();
                if (hit == null) continue;
                var (label, at) = Crossing(hit.Value, parent);
                leaked.Add($"T{b.Key} via {label}");
                if (at.HasValue) _leakPoints.Add(at.Value);
            }
            sb.Append($"from T{tier}: {parent.Count} cells " + (leaked.Count > 0 ? "— LEAKS into " + string.Join(", ", leaked) : "sealed") + "; ");
            leaks += leaked.Count;
        }
        return (sb.ToString(), leaks);
    }

    // Walk the flood path back from a leaked cell; the first place it passes an old seam pillar is
    // where the border let it through.
    static (string label, Vector2? at) Crossing(Vector2Int end, Dictionary<Vector2Int, Vector2Int> parent)
    {
        var path = new List<Vector2Int>();
        for (var c = end; path.Count < 100000; c = parent[c]) { path.Add(c); if (parent[c] == c) break; }
        path.Reverse();
        foreach (var c in path)
        {
            var p = new Vector2(c.x * Cell, c.y * Cell);
            if (_seam.Any(s => (s - p).sqrMagnitude < 16f)) return ($"({p.x:F0},{p.y:F0})", p);
        }
        return ("(no seam crossing found — around a line end?)", null);
    }

    static readonly List<Vector2> _seam = new();   // old pillar centres, for leak tracing

    const float Cell = 3f;
    static Vector2Int Cell2(Vector3 p) => new(Mathf.RoundToInt(p.x / Cell), Mathf.RoundToInt(p.z / Cell));

    static Dictionary<Vector2Int, Vector2Int> Flood(Vector3 start, float sea, Dictionary<Vector2Int, (bool ok, float y)> cache)
    {
        var parent = new Dictionary<Vector2Int, Vector2Int>();
        var q = new Queue<Vector2Int>();
        var s = Cell2(start);
        // The boss may stand on a prop — start from the nearest open cell.
        for (int r = 0; r < 6 && !Open(s, sea, cache).ok; r++) s += new Vector2Int(1, 0);
        parent[s] = s; q.Enqueue(s);
        var hits = new RaycastHit[8];
        while (q.Count > 0 && parent.Count < 1_500_000)
        {
            var c = q.Dequeue(); var (_, cy) = Open(c, sea, cache);
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                var n = new Vector2Int(c.x + dx, c.y + dz);
                if (parent.ContainsKey(n)) continue;
                var (ok, ny) = Open(n, sea, cache);
                if (!ok) continue;
                float d = Cell * Mathf.Sqrt(dx * dx + dz * dz);
                if (ny - cy > d + 0.4f) continue;                                  // too steep to climb (45° + step)
                var a = new Vector3(c.x * Cell, 0f, c.y * Cell); var b = new Vector3(n.x * Cell, 0f, n.y * Cell);
                if (Blocked(a, cy, b, ny, hits)) continue;
                parent[n] = c; q.Enqueue(n);
            }
        }
        return parent;
    }

    /// <summary>Re-run only the flood audit on the current scene, either against the old pillars alone
    /// (new walls non-solid — checks the audit agrees with the 09-24 verification) or the new walls alone.</summary>
    public static string Audit(bool oldPillarsOnly)
    {
        var root = GameObject.Find(RootName);
        var newCols = root != null ? root.GetComponentsInChildren<Collider>() : new Collider[0];
        var oldRoot = GameObject.Find(OldRootName);
        var oldCols = Borders.Select(b => oldRoot != null ? oldRoot.transform.Find(b.blockade) : null)
                             .Where(t => t != null).Select(t => t.GetComponent<Collider>()).Where(c => c != null).ToArray();
        var saveNew = newCols.Select(c => c.enabled).ToArray(); var saveOld = oldCols.Select(c => c.enabled).ToArray();
        try
        {
            foreach (var c in newCols) c.enabled = !oldPillarsOnly;
            foreach (var c in oldCols) c.enabled = oldPillarsOnly;
            CollectSeam();
            Physics.SyncTransforms();
            return FloodAudit(SeaLevel()).Item1;
        }
        finally
        {
            for (int i = 0; i < newCols.Length; i++) newCols[i].enabled = saveNew[i];
            for (int i = 0; i < oldCols.Length; i++) oldCols[i].enabled = saveOld[i];
            Physics.SyncTransforms();
        }
    }

    static void CollectSeam()
    {
        _seam.Clear();
        var oldRoot = GameObject.Find(OldRootName);
        if (oldRoot == null) return;
        foreach (var (blockade, _, _) in Borders)
        {
            var mf = oldRoot.transform.Find(blockade)?.GetComponent<MeshFilter>();
            if (mf == null) continue;
            foreach (var c in PillarCells(mf)) _seam.Add(new Vector2(c.x * Grid, c.y * Grid));
        }
    }

    static readonly Collider[] _overlap = new Collider[16];
    static (bool ok, float y) Open(Vector2Int c, float sea, Dictionary<Vector2Int, (bool, float)> cache)
    {
        if (cache.TryGetValue(c, out var v)) return v;
        var p = new Vector3(c.x * Cell, 0f, c.y * Cell);
        bool ok = Ground(p, out float y) && y > sea + 0.2f;
        if (ok)
        {
            int n = Physics.OverlapCapsuleNonAlloc(new Vector3(p.x, y + 0.6f, p.z), new Vector3(p.x, y + 1.5f, p.z), 0.3f, _overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (!(_overlap[i] is TerrainCollider)) { ok = false; break; }
        }
        cache[c] = (ok, y);
        return (ok, y);
    }

    static bool Blocked(Vector3 a, float ay, Vector3 b, float by, RaycastHit[] hits)
    {
        foreach (float h in new[] { 0.7f, 1.5f })
        {
            var o = new Vector3(a.x, ay + h, a.z); var e = new Vector3(b.x, by + h, b.z);
            var d = e - o;
            int n = Physics.SphereCastNonAlloc(o, 0.25f, d.normalized, hits, d.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (!(hits[i].collider is TerrainCollider)) return true;
        }
        return false;
    }

    // Where each tier's gate boss stands (BC_ProgressionGateBosses), T1..T5.
    static Dictionary<int, Vector3> BossSpots()
    {
        var map = new Dictionary<int, Vector3>();
        foreach (var link in Object.FindObjectsByType<BCGateBossLink>(FindObjectsInactive.Include))
        {
            if (link.boss == null) continue;
            string n = link.name;   // "T{n}_BossGateLink"
            if (n.Length > 1 && n[0] == 'T' && char.IsDigit(n[1])) map[n[1] - '0'] = link.boss.transform.position;
        }
        return map;
    }

    // ── helpers ──────────────────────────────────────────────────────────
    static float SeaLevel()
    {
        var sea = GameObject.Find("Sea Level (Water)");
        return sea != null ? sea.transform.position.y : float.MinValue;
    }

    // Gate openings: the shutters of the T1–T4 boss links (T5's link drives the transit pads, not a border).
    static List<Bounds> FindGates(StringBuilder report)
    {
        var list = new List<Bounds>();
        foreach (var link in Object.FindObjectsByType<BCGateBossLink>(FindObjectsInactive.Include))
        {
            if (link.name.StartsWith("T5")) continue;
            var rs = link.shutters.Where(s => s != null).SelectMany(s => s.GetComponentsInChildren<Renderer>()).ToArray();
            if (rs.Length == 0) continue;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            list.Add(b);
            report.AppendLine($"Gate {link.name}: doorway at ({b.center.x:F0},{b.center.z:F0}), {b.size.x:F0} x {b.size.z:F0} m kept clear");
        }
        return list;
    }

    static Terrain TerrainAt(Vector3 p)
    {
        foreach (var t in Terrain.activeTerrains)
        {
            var o = t.transform.position; var s = t.terrainData.size;
            if (p.x >= o.x && p.x <= o.x + s.x && p.z >= o.z && p.z <= o.z + s.z) return t;
        }
        return null;
    }

    static bool Ground(Vector3 p, out float y)
    {
        var t = TerrainAt(p);
        if (t == null) { y = 0f; return false; }
        y = t.SampleHeight(p) + t.transform.position.y;
        return true;
    }

    static readonly Dictionary<string, GameObject> _prefabs = new();
    static GameObject LoadWall(string name)
    {
        if (_prefabs.TryGetValue(name, out var p) && p != null) return p;
        p = AssetDatabase.LoadAssetAtPath<GameObject>($"{SeptemberDeliveryBuilder.WallsDir}/{name}.prefab");
        if (p == null) Debug.LogWarning($"[BorderWalls] Missing wall prefab {name} — run Wasteland ▸ September Delivery ▸ Build Prefabs.");
        _prefabs[name] = p;
        return p;
    }

    static float Width(GameObject prefab)
    {
        var bc = prefab.GetComponent<BoxCollider>();
        return bc != null ? bc.size.x : 6f;
    }

    static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
}
