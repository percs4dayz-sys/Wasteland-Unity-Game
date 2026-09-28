using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Sculpts the main world into WALKABLE tiered terrain in the OPEN scene so you can't see the outer
/// edge from the centre: a low central hub that rises in distinct, RAMPED tiers out to the high
/// corners. Non-destructive — it swaps the flat "Ground" plane for a Terrain (or re-sculpts an
/// existing one) and snaps the player + WorldContent markers + enemies onto the new surface.
/// Slopes are gentle enough to walk (no impassable cliffs), so you can never get blocked.
///
/// Menu:  Wasteland ▸ Terrain ▸ Sculpt Main World (tiered)
/// </summary>
public static class MainWorldTerrain
{
    const float SIZE = 600f;       // 600×600 m — matches WorldContentPlanner.EDGE*2 / WorldBlockoutTool.HALF*2
    const float MAX_HEIGHT = 50f;  // metres from the low centre to the high outer edge
    const int   RES = 513;
    const string DataPath = "Assets/Art/Generated3D/MainWorldTerrain.asset";

    [MenuItem("Wasteland/Archived/Terrain/Sculpt Main World (tiered)", false, 9000)]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();

        // A flat Plane "Ground" gets replaced with a Terrain; an existing Terrain is reused.
        Terrain terrain = null;
        var ground = GameObject.Find("Ground");
        if (ground != null)
        {
            terrain = ground.GetComponent<Terrain>();
            if (terrain == null) Object.DestroyImmediate(ground);
        }

        if (terrain == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Art/Generated3D"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
                AssetDatabase.CreateFolder("Assets/Art", "Generated3D");
            }
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(DataPath) != null) AssetDatabase.DeleteAsset(DataPath);
            var data = new TerrainData { heightmapResolution = RES, size = new Vector3(SIZE, MAX_HEIGHT, SIZE) };
            AssetDatabase.CreateAsset(data, DataPath);
            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Ground";
            go.transform.position = new Vector3(-SIZE / 2f, 0f, -SIZE / 2f);   // centre the terrain on world origin
            terrain = go.GetComponent<Terrain>();
        }
        terrain.terrainData.size = new Vector3(SIZE, MAX_HEIGHT, SIZE);

        Sculpt(terrain.terrainData);
        terrain.Flush();
        SnapContent(terrain);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = terrain.gameObject;
        EditorUtility.DisplayDialog("Main World Terrain",
            "Sculpted walkable tiered terrain and snapped the player + markers + enemies onto it.\n\n" +
            "Centre is low; it rises in ramped tiers to the high corners (blocks the view to the edge), " +
            "and the slopes stay walkable. Re-run any time to reshape. Then run 'Block Out Main World' so " +
            "the edge walls are tall enough, and SAVE (Ctrl+S).", "OK");
    }

    static void Sculpt(TerrainData data)
    {
        int res = data.heightmapResolution;
        var h = new float[res, res];
        float seed = Random.value * 1000f;
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
                h[y, x] = Height((float)x / (res - 1), (float)y / (res - 1), seed);
        data.SetHeights(0, 0, h);
    }

    // Distinct tier plateaus joined by WALKABLE ramps, rising from the centre outward.
    static float Height(float u, float v, float seed)
    {
        float dist = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;   // 0 centre .. 1 edge

        // boundaries match WorldContentPlanner rings; plateau heights T1..T5
        float[] edge  = { 0.33f, 0.63f, 0.87f, 0.95f };
        float[] level = { 0.00f, 0.22f, 0.45f, 0.68f, 0.90f };
        float tier = level[0];
        for (int i = 0; i < edge.Length; i++)
            tier = Mathf.Lerp(tier, level[i + 1],
                              Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge[i] - 0.05f, edge[i] + 0.05f, dist)));

        float bumps = (Mathf.PerlinNoise(u * 7f + seed, v * 7f + seed) - 0.5f) * 0.06f;   // gentle natural variation
        return Mathf.Clamp01(tier + bumps + 0.01f);
    }

    static void SnapContent(Terrain terrain)
    {
        var pc = Object.FindAnyObjectByType<Player3DController>();
        if (pc != null) SnapTo(pc.transform, terrain, 0.4f);

        // WorldContent markers: snap each marker (its label child moves with it).
        var content = GameObject.Find("WorldContent");
        if (content != null)
            foreach (Transform tierGroup in content.transform)
                foreach (Transform marker in tierGroup)
                    SnapTo(marker, terrain, 0.6f);

        foreach (var ct in Object.FindObjectsByType<CombatTarget>())
            SnapTo(ct.transform, terrain, 1f);
    }

    static void SnapTo(Transform t, Terrain terrain, float lift)
    {
        Vector3 p = t.position;
        float surface = terrain.SampleHeight(p) + terrain.transform.position.y;
        t.position = new Vector3(p.x, surface + lift, p.z);
    }
}
