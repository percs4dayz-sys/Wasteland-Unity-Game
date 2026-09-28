using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click foliage: scatters every model in Resources/Foliage densely across the walkable "Ground"
/// and bakes the result into the scene. Companion to the paint-by-hand World Scatter Brush — this is
/// the "just fill the whole map with grass/flowers/bushes" button.
///
/// It raycasts a jittered grid down onto the ground (same placement approach as WorldScatterBrush's
/// Auto-Fill), giving even coverage with random yaw + scale so it never looks tiled. Yaw is added on top
/// of the model's import rotation (don't overwrite it — Blender FBX roots carry a −90° X correction, and
/// clobbering it lays the plants flat). Each plant is re-skinned with its SET's shared toon material
/// (Bloom / Clockwork / Foliage each own an atlas, since embedded FBX textures import white), and flagged
/// Static so Unity batches them — a few thousand plants render cheaply. Everything lands under a single
/// "Foliage" object you can move, hide, or delete — or run "Clear World Foliage" to re-fill.
///
/// Foliage source = Resources/Foliage, named "&lt;Set&gt;_NN.fbx" with a matching "&lt;Set&gt;_Atlas.png"
/// beside it. Drop in a new set (plants + its atlas) to expand the palette. Open: Wasteland ▸ World ▸ Fill World With Foliage
/// </summary>
public static class FoliageFiller
{
    const string GroupName   = "Foliage";
    const int    MaxInstances = 12000;  // performance cap; grid spacing auto-widens to respect it
    const float  MinSpacing   = 0.75f;  // closest two plants ever get (world units) — dense carpet

    [MenuItem("Wasteland/World/Fill World With Foliage")]
    public static void Fill()
    {
        var models = Resources.LoadAll<GameObject>(GroupName);
        if (models == null || models.Length == 0)
        {
            EditorUtility.DisplayDialog("Fill Foliage",
                "No foliage found in Assets/Resources/Foliage.\nPut some plant models there first.", "OK");
            return;
        }

        if (!TryGetGroundBounds(out Bounds b))
        {
            EditorUtility.DisplayDialog("Fill Foliage",
                "Couldn't find the ground. Make sure there's a 'Ground' object (or active Terrain) in the scene.",
                "OK");
            return;
        }

        // Auto-widen the grid step so a huge map doesn't blow past the instance cap.
        float area = Mathf.Max(1f, b.size.x * b.size.z);
        float step = Mathf.Max(MinSpacing, Mathf.Sqrt(area / MaxInstances));
        int approx = Mathf.RoundToInt((b.size.x / step) * (b.size.z / step));

        if (!EditorUtility.DisplayDialog("Fill World With Foliage",
                $"Scatter up to ~{approx} plants ({models.Length} types) across the ground?\n\n" +
                "Tip: run 'Clear World Foliage' first if you want to start fresh.", "Fill", "Cancel"))
            return;

        var parent = GetOrMakeGroup();
        Undo.RegisterFullObjectHierarchyUndo(parent.gameObject, "Fill Foliage");
        var matCache = new Dictionary<string, Material>();   // set name -> shared material

        int placed = 0;
        // Jittered grid march for even, gap-respecting coverage.
        for (float x = b.min.x; x < b.max.x && placed < MaxInstances; x += step)
        for (float z = b.min.z; z < b.max.z && placed < MaxInstances; z += step)
        {
            float jx = x + Random.Range(-step * 0.4f, step * 0.4f);
            float jz = z + Random.Range(-step * 0.4f, step * 0.4f);
            var from = new Vector3(jx, b.max.y + 500f, jz);
            if (!Physics.Raycast(from, Vector3.down, out var hit, 10000f)) continue;

            var model = models[Random.Range(0, models.Length)];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.transform.position = hit.point;
            // Keep the model's import orientation (Blender→Unity bakes a −90° X onto the FBX root) and
            // only add yaw around world-up. Overwriting rotation outright is what made them lie flat.
            go.transform.Rotate(0f, Random.Range(0f, 360f), 0f, Space.World);
            go.transform.localScale *= Random.Range(0.7f, 1.4f);
            go.transform.SetParent(parent, true);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);

            // Give the plant its SET's shared texture (Bloom / Clockwork / Foliage each have their own
            // atlas). FBX-embedded textures don't import reliably → white, so we re-skin from the atlas.
            var mat = MaterialForModel(model.name, matCache);
            if (mat != null)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var slots = new Material[r.sharedMaterials.Length == 0 ? 1 : r.sharedMaterials.Length];
                    for (int k = 0; k < slots.Length; k++) slots[k] = mat;
                    r.sharedMaterials = slots;
                }

            Undo.RegisterCreatedObjectUndo(go, "Fill Foliage");
            placed++;
        }

        AssetDatabase.SaveAssets();
        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        EditorUtility.SetDirty(parent.gameObject);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);

        EditorUtility.DisplayDialog("Fill Foliage",
            $"Scattered {placed} plants under \"{GroupName}\".\nSave the scene to keep them.", "Nice");
        Debug.Log($"[FoliageFiller] Placed {placed} foliage instances across the ground.");
    }

    [MenuItem("Wasteland/World/Clear World Foliage")]
    public static void Clear()
    {
        var g = GameObject.Find(GroupName);
        if (g == null) { EditorUtility.DisplayDialog("Clear Foliage", "No \"Foliage\" group in the scene.", "OK"); return; }
        Undo.DestroyObjectImmediate(g);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }

    /// <summary>Bounds of the walkable ground: prefer a "Ground" object's renderers, else active Terrain.</summary>
    static bool TryGetGroundBounds(out Bounds b)
    {
        b = new Bounds();
        var ground = GameObject.Find("Ground");
        if (ground != null)
        {
            var rends = ground.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                return true;
            }
        }
        if (Terrain.activeTerrain != null)
        {
            var t = Terrain.activeTerrain;
            var d = t.terrainData;
            b = new Bounds(t.transform.position + d.size * 0.5f, d.size);
            return true;
        }
        return false;
    }

    static Transform GetOrMakeGroup()
    {
        var g = GameObject.Find(GroupName);
        if (g == null)
        {
            g = new GameObject(GroupName);
            Undo.RegisterCreatedObjectUndo(g, "Create Foliage Group");
        }
        return g.transform;
    }

    // Foliage naming convention: "<Set>_<NN>.fbx" (e.g. Bloom_03, Clockwork_07, Foliage_11) with a shared
    // atlas "<Set>_Atlas.png" sitting beside them in Resources/Foliage. Each set gets ONE shared toon
    // material → every plant shows its own set's texture, and it's still one instancing batch per set.
    static string SetOf(string modelName)
    {
        int u = modelName.LastIndexOf('_');
        return u > 0 ? modelName.Substring(0, u) : modelName;
    }

    static Material MaterialForModel(string modelName, Dictionary<string, Material> cache)
    {
        string set = SetOf(modelName);
        if (cache.TryGetValue(set, out var cached)) return cached;

        string matPath = $"Assets/Resources/Foliage/{set}_Mat.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            var atlas  = Resources.Load<Texture2D>($"Foliage/{set}_Atlas");
            var shader = Shader.Find("Wasteland/Toon (URP)")
                      ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { name = set + "_Mat", enableInstancing = true };
            if (atlas != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", atlas);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", atlas);
            }
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        cache[set] = mat;
        return mat;
    }
}
