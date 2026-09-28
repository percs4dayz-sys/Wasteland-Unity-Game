using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Spawns a ready-to-place FOG WALL curtain into the open scene — a thin vertical screen of fog
/// that hides the next zone until the player walks through it (see FogWall.cs). Drop one at each
/// boundary you circled (bridge mouths, river crossings), then move/rotate/size it to fit.
///
/// It builds a reusable soft-fog texture + URP particle material in _Wasteland (once), so every
/// wall shares them. Each spawned wall gets a unique id so their "crossed" save-flags don't clash.
///
/// Menu:  Wasteland ▸ World ▸ Create Fog Wall (at Scene view)
/// </summary>
public static class FogWallTool
{
    const string TexPath = WastelandPaths.Root + "/Textures/FogSoft.png";
    const string MatPath = WastelandPaths.Root + "/Materials/FogWall.mat";

    // Default curtain: WIDE + TALL, but THIN front-to-back so it blocks the sightline across the
    // seam without fogging the ground/water on your side.
    static readonly Vector3 DefaultSize = new(14f, 9f, 1.5f);

    [MenuItem("Wasteland/World/Create Fog Wall (at Scene view)")]
    public static void CreateFogWall()
    {
        var mat = EnsureFogMaterial();

        var go = new GameObject("FogWall");
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = DefaultSize;
        box.center = new Vector3(0, DefaultSize.y * 0.5f, 0);   // sit on the ground

        // Child particle system = the visible fog.
        var fogGo = new GameObject("Fog");
        fogGo.transform.SetParent(go.transform, false);
        var ps = fogGo.AddComponent<ParticleSystem>();
        ConfigureFog(ps, mat);

        var wall = go.AddComponent<FogWall>();
        wall.id = UniqueId();
        wall.ApplyShape();

        // Place it where the user is looking in the Scene view (drop to ground if something's below).
        var sv = SceneView.lastActiveSceneView;
        Vector3 pos = sv != null ? sv.pivot : Vector3.zero;
        if (Physics.Raycast(pos + Vector3.up * 50f, Vector3.down, out var hit, 200f)) pos = hit.point;
        go.transform.position = pos;

        Undo.RegisterCreatedObjectUndo(go, "Create Fog Wall");
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);

        Debug.Log($"[FogWallTool] Spawned '{go.name}' (id='{wall.id}'). Move/rotate it onto the boundary, " +
                  "set Width/Height via the BoxCollider Size, and give it a memorable id.");
    }

    static string UniqueId()
    {
        int n = Object.FindObjectsByType<FogWall>().Length + 1;
        return "wall_" + n;
    }

    // ── fog particle setup ────────────────────────────────────────────────
    static void ConfigureFog(ParticleSystem ps, Material mat)
    {
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 4f;
        main.startSpeed = 0.08f;
        main.startSize = 6f;                       // big soft puffs that overlap into a solid screen
        main.startColor = new Color(0.74f, 0.74f, 0.78f, 0.45f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 400;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        var emission = ps.emission;
        emission.rateOverTime = 60f;               // dense enough to occlude; lower if it tanks fps

        var shape = ps.shape;                      // FogWall.ApplyShape() resizes this to the collider
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = DefaultSize;

        // Soft fade in/out so puffs don't pop at spawn/death.
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f),
                    new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.Distance;
    }

    // ── shared assets (built once) ────────────────────────────────────────
    static Material EnsureFogMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (existing != null) return existing;

        WastelandPaths.EnsureFolder(WastelandPaths.Root + "/Textures");
        WastelandPaths.EnsureFolder(WastelandPaths.Root + "/Materials");

        var tex = EnsureFogTexture();

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
        var mat = new Material(shader) { name = "FogWall" };
        if (shader.name.Contains("Universal"))
        {
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1f);                 // transparent
            mat.SetFloat("_Blend", 0f);                   // alpha blend
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
        else
        {
            mat.mainTexture = tex;
        }

        AssetDatabase.CreateAsset(mat, MatPath);
        AssetDatabase.SaveAssets();
        return mat;
    }

    static Texture2D EnsureFogTexture()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
        if (existing != null) return existing;

        const int s = 64;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        float c = (s - 1) * 0.5f;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - d);
                a *= a;                                   // soft, round falloff
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();

        File.WriteAllBytes(TexPath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(TexPath);
        var imp = (TextureImporter)AssetImporter.GetAtPath(TexPath);
        imp.alphaIsTransparency = true;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
    }
}
