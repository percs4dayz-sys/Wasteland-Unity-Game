using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Steps each tier up to its own elevation, so height and tier become the same thing.
///
/// The problem this solves: on the authored heightmap every ring contains BOTH a low basin
/// and a high wall, so elevation ranges overlap between tiers and height-banding paints
/// walls the same colour everywhere. After this runs, tier 1's basin is the lowest ground
/// and tier 5's is the highest, exactly as in the concept art — so painting by height Just
/// Works, and so does gating by height later.
///
/// It does NOT flatten anything. Original detail (ridges, arcs, the tutorial island) is
/// compressed into DetailFraction of the range and rides on top of the tier ramp, so the
/// landforms survive. The flat surround outside the landmass is left untouched.
///
/// Menu:  Wasteland ▸ World Tiers ▸ 2 · Step Tier Elevations (MODIFIES HEIGHTS)
/// </summary>
public static class WorldTierElevationStepper
{
    /// <summary>How much of the vertical range stays as original detail vs the tier ramp.
    /// 0.35 keeps ridges clearly readable while still separating the tiers cleanly.</summary>
    const float DetailFraction = 0.35f;

    /// <summary>Tiers to create. Matches the five biome bands.</summary>
    const int TierCount = 5;

    /// <summary>Fraction of each band's width used to ramp up to the next tier.</summary>
    const float StepBlend = 0.35f;

    [MenuItem("Wasteland/World Tiers/2 · Step Tier Elevations (MODIFIES HEIGHTS)", false, 2)]
    public static void Step()
    {
        var terrain = FindTerrain();
        if (terrain == null) { EditorUtility.DisplayDialog("No Terrain", "No Terrain in the open scene.", "OK"); return; }

        var td = terrain.terrainData;
        var tp = terrain.transform.position;
        int res = td.heightmapResolution;
        float[,] h = td.GetHeights(0, 0, res, res);

        float lo = 1f, hi = 0f;
        foreach (float v in h) { if (v < lo) lo = v; if (v > hi) hi = v; }
        float range = hi - lo;
        if (range < 0.0001f) { EditorUtility.DisplayDialog("Flat Terrain", "No height variation to work with.", "OK"); return; }

        float landT = lo + range * 0.06f;
        Vector3 c = DetectCentre(h, res, td, tp, landT);
        float landR = DetectLandRadius(h, res, td, tp, c, landT);

        if (!EditorUtility.DisplayDialog("Step Tier Elevations",
            $"THIS MODIFIES THE HEIGHTMAP.\n\n" +
            $"Ring centre: ({c.x:0.0}, {c.z:0.0})\n" +
            $"Landmass radius: {landR:0} m\n" +
            $"Current range: {lo * td.size.y:0.0} – {hi * td.size.y:0.0} m\n\n" +
            $"{TierCount} tiers will be stepped upward from centre to rim, keeping " +
            $"{DetailFraction * 100:0}% of the range as original ridge detail.\n" +
            "The flat surround is left alone. A backup is written first.\n\nContinue?",
            "Step Elevations", "Cancel")) return;

        var log = new StringBuilder("=== Step Tier Elevations ===\n");
        string bk = WorldTierBiomePainter.BackupPublic(td);
        if (bk != null) log.AppendLine($"Backup: {bk}");
        log.AppendLine($"Centre ({c.x:0.0}, {c.z:0.0})   land radius {landR:0} m   " +
                       $"range {lo:0.000}–{hi:0.000}");

        float sx = td.size.x / (res - 1), sz = td.size.z / (res - 1);
        float bandW = landR / TierCount;
        var tierArea = new int[TierCount];

        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float orig = h[z, x];
                if (orig <= landT) continue;            // leave the flat surround exactly as-is

                float dx = tp.x + x * sx - c.x, dz = tp.z + z * sz - c.z;
                float r = Mathf.Sqrt(dx * dx + dz * dz);

                // continuous tier position: 0 at centre, TierCount at the rim
                float t = Mathf.Clamp(r / bandW, 0f, TierCount - 0.0001f);
                int tier = Mathf.FloorToInt(t);
                float frac = t - tier;

                // plateau across most of the band, ramp over the last StepBlend of it
                float within = frac <= 1f - StepBlend
                    ? 0f
                    : Mathf.SmoothStep(0f, 1f, (frac - (1f - StepBlend)) / StepBlend);
                float ramp = (tier + within) / (TierCount - 1f);
                ramp = Mathf.Clamp01(ramp);

                float detail = (orig - lo) / range;     // 0..1 original shape
                float mixed = detail * DetailFraction + ramp * (1f - DetailFraction);

                h[z, x] = Mathf.Clamp01(lo + mixed * range);
                tierArea[Mathf.Min(tier, TierCount - 1)]++;
            }
        }

        td.SetHeights(0, 0, h);
        terrain.Flush();
        EditorUtility.SetDirty(td);
        AssetDatabase.SaveAssets();

        // report the resulting per-tier elevation so the painter's bands are predictable
        log.AppendLine("\nresulting tier elevations (world metres):");
        for (int i = 0; i < TierCount; i++)
        {
            float rampLo = i / (float)(TierCount - 1);
            float basin = lo + (rampLo * (1f - DetailFraction)) * range;
            log.AppendLine($"      Tier {i + 1}  basin ≈ {basin * td.size.y,6:0.0} m   " +
                           $"r {i * bandW,5:0} – {(i + 1) * bandW,5:0} m   " +
                           $"{tierArea[i]} texels");
        }
        log.AppendLine("\nHeight now tracks tier. Run '1 · Paint Biomes By Height' next.");

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("Step Tier Elevations — Done",
            "Tiers stepped. Height now tracks tier.\n\n" +
            "Next: run '1 · Paint Biomes By Height'.", "OK");
    }

    // ── shared detection (same logic the painter uses) ───────────────────────

    static Vector3 DetectCentre(float[,] h, int res, TerrainData td, Vector3 tp, float landT)
    {
        float sx = td.size.x / (res - 1), sz = td.size.z / (res - 1);
        Vector3 guess = new Vector3(tp.x + td.size.x * 0.5f, 0f, tp.z + td.size.z * 0.5f);
        Vector2 best = new Vector2(guess.x, guess.z);
        float bestScore = float.MaxValue;
        float span = Mathf.Min(td.size.x, td.size.z) * 0.18f;

        for (int gz = 0; gz <= 10; gz++)
            for (int gx = 0; gx <= 10; gx++)
            {
                float cx = guess.x + Mathf.Lerp(-span, span, gx / 10f);
                float cz = guess.z + Mathf.Lerp(-span, span, gz / 10f);

                const int B = 48;
                var s = new float[B]; var s2 = new float[B]; var n = new int[B];
                float maxR = Mathf.Min(td.size.x, td.size.z) * 0.5f;

                for (int z = 0; z < res; z += 4)
                    for (int x = 0; x < res; x += 4)
                    {
                        float v = h[z, x];
                        if (v <= landT) continue;
                        float dx = tp.x + x * sx - cx, dz = tp.z + z * sz - cz;
                        float r = Mathf.Sqrt(dx * dx + dz * dz);
                        if (r >= maxR) continue;
                        int b = (int)(r / maxR * B);
                        if (b >= B) continue;
                        s[b] += v; s2[b] += v * v; n[b]++;
                    }

                float sc = 0f; int used = 0;
                for (int b = 0; b < B; b++)
                {
                    if (n[b] < 30) continue;
                    float m = s[b] / n[b];
                    sc += Mathf.Max(0f, s2[b] / n[b] - m * m); used++;
                }
                if (used < 10) continue;
                sc /= used;
                if (sc < bestScore) { bestScore = sc; best = new Vector2(cx, cz); }
            }
        return new Vector3(best.x, 0f, best.y);
    }

    static float DetectLandRadius(float[,] h, int res, TerrainData td, Vector3 tp, Vector3 c, float landT)
    {
        float sx = td.size.x / (res - 1), sz = td.size.z / (res - 1);
        const int B = 120;
        float maxR = Mathf.Min(td.size.x, td.size.z) * 0.75f;
        var landHits = new int[B]; var allHits = new int[B];

        for (int z = 0; z < res; z += 2)
            for (int x = 0; x < res; x += 2)
            {
                float dx = tp.x + x * sx - c.x, dz = tp.z + z * sz - c.z;
                float r = Mathf.Sqrt(dx * dx + dz * dz);
                if (r >= maxR) continue;
                int b = (int)(r / maxR * B);
                allHits[b]++;
                if (h[z, x] > landT) landHits[b]++;
            }

        float found = maxR * 0.5f;
        for (int b = B - 1; b >= 0; b--)
        {
            if (allHits[b] < 20) continue;
            if (landHits[b] / (float)allHits[b] >= 0.33f) { found = (b + 1) / (float)B * maxR; break; }
        }
        return found;
    }

    static Terrain FindTerrain()
    {
        var g = GameObject.Find("Ground");
        var t = g != null ? g.GetComponent<Terrain>() : null;
        return t != null ? t : Terrain.activeTerrain;
    }
}
