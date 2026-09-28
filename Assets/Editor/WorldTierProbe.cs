using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// READ-ONLY analysis of whatever terrain is currently in the scene.
///
/// Nothing here writes to the TerrainData — no heights, no alphamaps, no assets. It samples
/// the live heightmap, searches for the centre the concentric rings are actually built
/// around, and reports where the mountain walls sit. Those measured numbers are what the
/// biome painter and gate placer should use, instead of constants derived from the source
/// PNG that may not match how the terrain was actually built.
///
/// Menu:  Wasteland ▸ World Tiers ▸ 0 · Probe Existing Terrain (read-only)
/// </summary>
public static class WorldTierProbe
{
    const int   CenterSearchSteps = 12;    // grid resolution per axis for the centre search
    const float CenterSearchRange = 70f;   // metres to search around the starting guess
    const int   RadialBins        = 120;

    [MenuItem("Wasteland/World Tiers/0 · Probe Existing Terrain (read-only)", false, -10)]
    public static void Probe()
    {
        var terrain = FindTerrain();
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("No Terrain",
                "No Terrain in the open scene. Open MainWorld3D or dontfuckindelete first.", "OK");
            return;
        }

        var td  = terrain.terrainData;
        int res = td.heightmapResolution;
        var tp  = terrain.transform.position;
        float[,] h = td.GetHeights(0, 0, res, res);

        var log = new StringBuilder("=== Probe Existing Terrain (read-only) ===\n");
        log.AppendLine($"Terrain {td.size.x}×{td.size.z} m, height {td.size.y} m, " +
                       $"heightmap {res}×{res}, origin {tp}");

        // ── height distribution, to separate the flat surround from the landmass ──
        float min = 1f, max = 0f;
        var hist = new int[64];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float v = h[z, x];
                if (v < min) min = v;
                if (v > max) max = v;
                hist[Mathf.Clamp((int)(v * 63.999f), 0, 63)]++;
            }
        log.AppendLine($"\nnormalised height range: {min:0.000} … {max:0.000}   " +
                       $"({min * td.size.y:0.0} … {max * td.size.y:0.0} m)");

        int total = res * res;
        log.AppendLine("height histogram (normalised → % of map):");
        for (int i = 0; i < 64; i += 2)
        {
            int c = hist[i] + hist[i + 1];
            if (c * 1000 / total == 0) continue;
            log.AppendLine($"   {i / 64f:0.00}-{(i + 2) / 64f:0.00}  {c * 100f / total,5:0.0}%  " +
                           new string('#', Mathf.Min(60, c * 300 / total)));
        }

        // Land threshold: a little above the flat surround, which is the dominant low bin.
        int lowBin = 0;
        for (int i = 1; i < 24; i++) if (hist[i] > hist[lowBin]) lowBin = i;
        float landT = (lowBin + 2.5f) / 64f;
        log.AppendLine($"\nflat-surround bin ≈ {lowBin / 64f:0.00}; treating height > {landT:0.000} " +
                       $"({landT * td.size.y:0.0} m) as land");

        // ── find the centre the rings are actually built around ──
        Vector3 guess = new Vector3(tp.x + td.size.x * 0.5072f, 0f, tp.z + td.size.z * 0.2488f);
        Vector2 best = new Vector2(guess.x, guess.z);
        float bestScore = float.MaxValue;

        for (int gz = 0; gz <= CenterSearchSteps; gz++)
            for (int gx = 0; gx <= CenterSearchSteps; gx++)
            {
                float cx = guess.x + Mathf.Lerp(-CenterSearchRange, CenterSearchRange, gx / (float)CenterSearchSteps);
                float cz = guess.z + Mathf.Lerp(-CenterSearchRange, CenterSearchRange, gz / (float)CenterSearchSteps);
                float s = RadialVariance(h, res, td, tp, new Vector2(cx, cz), landT);
                if (s < bestScore) { bestScore = s; best = new Vector2(cx, cz); }
            }

        log.AppendLine($"\nbest-fit ring centre: ({best.x:0.0}, {best.y:0.0})   " +
                       $"[my PNG-derived constant was (4.3, -150.7)]");
        log.AppendLine($"offset from that constant: " +
                       $"({best.x - 4.31f:+0.0;-0.0}, {best.y + 150.72f:+0.0;-0.0}) m");

        // ── radial profile at the best centre ──
        float maxR = Mathf.Min(td.size.x, td.size.z) * 0.95f;
        var mean = new float[RadialBins];
        var frac = new float[RadialBins];
        Profile(h, res, td, tp, best, landT, maxR, mean, frac);

        log.AppendLine("\nradial profile (land-only mean height):");
        log.AppendLine("   r(m)   mean    land%");
        for (int i = 0; i < RadialBins; i++)
        {
            float r = (i + 0.5f) / RadialBins * maxR;
            if (frac[i] <= 0.01f) continue;
            log.AppendLine($"  {r,5:0}  {mean[i]:0.000}  {frac[i] * 100,5:0}%  " +
                           new string('#', Mathf.Clamp((int)(mean[i] * 80f), 0, 70)));
        }

        // ── wall crests = local maxima of the land-only profile ──
        log.AppendLine("\ndetected mountain walls (local maxima):");
        int found = 0;
        for (int i = 3; i < RadialBins - 3; i++)
        {
            if (frac[i] < 0.15f) continue;
            float m = mean[i];
            if (m <= mean[i - 1] || m <= mean[i - 2] || m < mean[i + 1] || m < mean[i + 2]) continue;
            float r = (i + 0.5f) / RadialBins * maxR;
            log.AppendLine($"   wall at r ≈ {r,5:0} m   (mean {m:0.000} = {m * td.size.y:0.0} m)");
            found++;
        }
        if (found == 0) log.AppendLine("   none detected — the profile may be too smooth or the centre wrong");

        log.AppendLine("\nCompare these radii against the constants in WorldTierTerrainBuilder:");
        log.AppendLine("   WallR = 149 / 230 / 310 / 390 m");
        log.AppendLine("\nNothing was modified.");

        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("Terrain Probe",
            $"Best-fit ring centre: ({best.x:0.0}, {best.y:0.0})\n" +
            $"{found} wall(s) detected.\n\nFull profile is in the Console. Nothing was changed.", "OK");
    }

    /// <summary>
    /// Read-only: how much of the terrain each layer actually covers, and how height maps
    /// onto layers. Answers "are the alphamaps painted at all, or is everything on layer 0".
    /// </summary>
    [MenuItem("Wasteland/World Tiers/0b · Report Terrain Layer Coverage (read-only)", false, -9)]
    public static void LayerCoverage()
    {
        var terrain = FindTerrain();
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("No Terrain", "No Terrain in the open scene.", "OK");
            return;
        }

        var td = terrain.terrainData;
        int aRes = td.alphamapResolution;
        int hRes = td.heightmapResolution;
        int n = td.terrainLayers.Length;

        var log = new StringBuilder("=== Terrain Layer Coverage (read-only) ===\n");
        log.AppendLine($"alphamapResolution {aRes}, heightmapResolution {hRes}, layers {n}");

        if (n == 0) { log.AppendLine("\n!! NO TERRAIN LAYERS ASSIGNED."); Debug.Log(log.ToString()); return; }

        for (int i = 0; i < n; i++)
        {
            var l = td.terrainLayers[i];
            log.AppendLine($"   Layer[{i}] {(l == null ? "NULL" : l.name)}" +
                           (l != null && l.diffuseTexture == null ? "   !! NO DIFFUSE TEXTURE" : ""));
        }

        var maps = td.GetAlphamaps(0, 0, aRes, aRes);
        var heights = td.GetHeights(0, 0, hRes, hRes);

        var dominantCount = new int[n];
        var weightSum = new double[n];
        // per-layer min/max of the height it appears at, to see if bands track elevation
        var hMin = new float[n]; var hMax = new float[n];
        for (int i = 0; i < n; i++) { hMin[i] = 1f; hMax[i] = 0f; }

        for (int y = 0; y < aRes; y++)
            for (int x = 0; x < aRes; x++)
            {
                int best = 0; float bw = -1f;
                for (int l = 0; l < n; l++)
                {
                    float w = maps[y, x, l];
                    weightSum[l] += w;
                    if (w > bw) { bw = w; best = l; }
                }
                dominantCount[best]++;
                int hy = Mathf.RoundToInt((float)y / (aRes - 1) * (hRes - 1));
                int hx = Mathf.RoundToInt((float)x / (aRes - 1) * (hRes - 1));
                float h = heights[hy, hx];
                if (h < hMin[best]) hMin[best] = h;
                if (h > hMax[best]) hMax[best] = h;
            }

        int total = aRes * aRes;
        log.AppendLine("\nlayer coverage (dominant pixel share, and the height range it occupies):");
        for (int i = 0; i < n; i++)
        {
            float pct = dominantCount[i] * 100f / total;
            string name = td.terrainLayers[i] == null ? "NULL" : td.terrainLayers[i].name;
            string range = dominantCount[i] > 0 ? $"h {hMin[i]:0.000}–{hMax[i]:0.000}" : "—";
            log.AppendLine($"   [{i}] {name,-46} {pct,6:0.0}%  {range,-18} " +
                           new string('#', Mathf.Clamp((int)(pct / 2f), 0, 50)));
        }

        int painted = 0;
        for (int i = 0; i < n; i++) if (dominantCount[i] > 0) painted++;
        log.AppendLine($"\nlayers actually visible: {painted} of {n}");
        if (painted <= 1)
            log.AppendLine("=> The alphamaps are NOT painted — everything sits on one layer.");
        else
            log.AppendLine("=> The alphamaps ARE painted across multiple layers.");

        log.AppendLine("\nNothing was modified.");
        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("Layer Coverage",
            $"{painted} of {n} layer(s) visible.\n\nFull breakdown in the Console. Nothing changed.", "OK");
    }

    /// <summary>Lower is better: how much land height varies within each radial ring.</summary>
    static float RadialVariance(float[,] h, int res, TerrainData td, Vector3 tp,
                                Vector2 c, float landT)
    {
        const int BINS = 60;
        var sum = new float[BINS]; var sum2 = new float[BINS]; var n = new int[BINS];
        float maxR = Mathf.Min(td.size.x, td.size.z) * 0.95f;
        float sx = td.size.x / (res - 1), sz = td.size.z / (res - 1);

        for (int z = 0; z < res; z += 2)
            for (int x = 0; x < res; x += 2)
            {
                float v = h[z, x];
                if (v <= landT) continue;
                float dx = tp.x + x * sx - c.x;
                float dz = tp.z + z * sz - c.y;
                float r = Mathf.Sqrt(dx * dx + dz * dz);
                if (r >= maxR) continue;
                int b = (int)(r / maxR * BINS);
                if (b >= BINS) continue;
                sum[b] += v; sum2[b] += v * v; n[b]++;
            }

        float score = 0f; int used = 0;
        for (int b = 0; b < BINS; b++)
        {
            if (n[b] < 40) continue;
            float m = sum[b] / n[b];
            score += Mathf.Max(0f, sum2[b] / n[b] - m * m);
            used++;
        }
        return used < 12 ? float.MaxValue : score / used;
    }

    static void Profile(float[,] h, int res, TerrainData td, Vector3 tp, Vector2 c,
                        float landT, float maxR, float[] mean, float[] frac)
    {
        var sum = new float[RadialBins];
        var land = new int[RadialBins];
        var all = new int[RadialBins];
        float sx = td.size.x / (res - 1), sz = td.size.z / (res - 1);

        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float dx = tp.x + x * sx - c.x;
                float dz = tp.z + z * sz - c.y;
                float r = Mathf.Sqrt(dx * dx + dz * dz);
                if (r >= maxR) continue;
                int b = (int)(r / maxR * RadialBins);
                if (b >= RadialBins) continue;
                all[b]++;
                float v = h[z, x];
                if (v > landT) { sum[b] += v; land[b]++; }
            }

        for (int b = 0; b < RadialBins; b++)
        {
            mean[b] = land[b] > 0 ? sum[b] / land[b] : 0f;
            frac[b] = all[b] > 0 ? land[b] / (float)all[b] : 0f;
        }
    }

    static Terrain FindTerrain()
    {
        var ground = GameObject.Find("Ground");
        var t = ground != null ? ground.GetComponent<Terrain>() : null;
        return t != null ? t : Terrain.activeTerrain;
    }
}
