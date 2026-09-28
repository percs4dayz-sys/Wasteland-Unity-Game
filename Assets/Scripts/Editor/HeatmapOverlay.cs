using UnityEngine;
using UnityEditor;

/// <summary>
/// Creates a semi-transparent overlay plane in the scene with your heatmap
/// as a visual reference for manual terrain building.
///
/// Usage: Wasteland → Terrain → Create Heatmap Overlay
///
/// The plane sits at Y=0.01 (just above the ground), uses the heatmap as
/// its texture, and is visible in both Scene and Game view at 50% opacity.
/// Toggle visibility via the "Overlay" layer or disable the GameObject.
/// </summary>
public static class HeatmapOverlay
{
    [MenuItem("Wasteland/Archived/Terrain/Create Heatmap Overlay", false, 9000)]
    public static void Create()
    {
        // Let user pick the image
        string imagePath = EditorUtility.OpenFilePanel("Select Heatmap Image", "Assets", "png,tif,tga,jpg");
        if (string.IsNullOrEmpty(imagePath)) return;

        // Convert absolute path to Assets-relative, or copy into project
        string assetPath = AbsoluteToAssetPath(imagePath);
        if (string.IsNullOrEmpty(assetPath))
        {
            // Copy into Assets/Art/
            string fileName = System.IO.Path.GetFileName(imagePath);
            string destDir = "Assets/Art";
            if (!AssetDatabase.IsValidFolder(destDir))
                destDir = "Assets";
            string dest = destDir + "/" + fileName;
            System.IO.File.Copy(imagePath, dest, true);
            AssetDatabase.Refresh();
            assetPath = dest;

            // Ensure readable
            TextureImporter imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (imp != null)
            {
                imp.isReadable = true;
                imp.textureType = TextureImporterType.Default;
                imp.alphaIsTransparency = true;
                imp.SaveAndReimport();
            }
        }

        EditorUtility.DisplayProgressBar("Heatmap Overlay", "Loading texture...", 0f);
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        EditorUtility.ClearProgressBar();

        if (tex == null)
        {
            Debug.LogError("[HeatmapOverlay] Could not load texture from: " + assetPath);
            return;
        }

        // Create material with the heatmap
        Material mat = new Material(Shader.Find("Unlit/Transparent"));
        mat.mainTexture = tex;
        mat.color = new Color(1, 1, 1, 0.5f);
        mat.name = "HeatmapOverlay_Mat";
        mat.renderQueue = 3000; // Transparent queue

        // Save material
        string matDir = "Assets/Art";
        if (!AssetDatabase.IsValidFolder(matDir))
            AssetDatabase.CreateFolder("Assets", "Art");
        string matPath = matDir + "/HeatmapOverlay_Mat.mat";
        AssetDatabase.CreateAsset(mat, matPath);

        // Remove any existing overlay
        var existing = GameObject.Find("HeatmapOverlay");
        if (existing != null)
            Object.DestroyImmediate(existing);

        // Create plane
        GameObject overlayGO = GameObject.CreatePrimitive(PrimitiveType.Plane);
        overlayGO.name = "HeatmapOverlay";
        overlayGO.transform.position = new Vector3(0, 0.01f, 0);

        // Scale plane to match texture aspect ratio
        float aspect = (float)tex.width / tex.height;
        float baseSize = Mathf.Max(tex.width, tex.height);
        float w = baseSize * aspect / 10f;
        float h = baseSize / 10f;
        overlayGO.transform.localScale = new Vector3(w, 1f, h);

        // Apply material
        var renderer = overlayGO.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        // Remove collider (we don't need it)
        Object.DestroyImmediate(overlayGO.GetComponent<Collider>());

        // ── Make it CLICK-THROUGH ──────────────────────────────────────
        // Layer 2 = IgnoreRaycast — prevents physics & scene-view picking
        overlayGO.layer = 2;
        Transform[] children = overlayGO.GetComponentsInChildren<Transform>();
        foreach (var child in children) child.gameObject.layer = 2;

        // Also tell the scene view to ignore this object for picking
        SceneVisibilityManager.instance.DisablePicking(overlayGO, false);

        // Tag it so it's easy to find
        overlayGO.tag = "EditorOnly";

        Undo.RegisterCreatedObjectUndo(overlayGO, "Create Heatmap Overlay");
        Selection.activeGameObject = overlayGO;

        Debug.Log($"[HeatmapOverlay] Created! Plane size: {tex.width}×{tex.height}px → {w:F1}×{h:F1} world units. " +
                  "It's on the IgnoreRaycast layer — you can click/trace right through it. " +
                  "Toggle visibility to hide, adjust opacity in the material.");
    }

    /// <summary>Converts an absolute path to an Assets-relative path if it's inside the project.</summary>
    private static string AbsoluteToAssetPath(string absolute)
    {
        string dataPath = Application.dataPath.Replace("/", "\\").TrimEnd('\\');
        absolute = absolute.Replace("/", "\\");
        if (absolute.StartsWith(dataPath))
        {
            return "Assets" + absolute.Substring(dataPath.Length);
        }
        return null;
    }
}
