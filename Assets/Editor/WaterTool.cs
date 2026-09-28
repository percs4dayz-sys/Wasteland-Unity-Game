using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Drops a ready-to-use water plane into the open scene — a transparent blue URP surface with NO
/// collider, so you can still click-to-move and fish from its edge. Non-destructive: each run just
/// adds one more "Water" plane (make as many ponds/lakes as you like). Move it (W) to your spot, set
/// its HEIGHT (Y) to the waterline, and scale it (R) to size the pool. Save the scene to keep it.
///
/// Menu:  Wasteland ▸ Add Water Plane
/// </summary>
public static class WaterTool
{
    const string MatPath = "Assets/Art/Generated3D/WastelandWater.mat";

    [MenuItem("Wasteland/Add Water Plane")]
    public static void AddWater()
    {
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = "Water";

        // Drop it where you're looking in the Scene view, on the ground plane.
        var sv = SceneView.lastActiveSceneView;
        Vector3 pos = sv != null ? sv.pivot : Vector3.zero;
        pos.y = 0.05f;                                  // just above ground; raise/lower to taste
        plane.transform.position = pos;
        plane.transform.localScale = new Vector3(2f, 1f, 2f);   // Plane is 10m → 20×20m pool

        // No collider — clicks pass through to the terrain/ground so movement & fishing still work.
        var col = plane.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);

        plane.GetComponent<Renderer>().sharedMaterial = GetWaterMaterial();

        Undo.RegisterCreatedObjectUndo(plane, "Add Water Plane");
        Selection.activeGameObject = plane;
        EditorGUIUtility.PingObject(plane);
        EditorSceneManager.MarkSceneDirty(plane.scene);

        EditorUtility.DisplayDialog("Water added",
            "Dropped a water plane where the Scene view is focused.\n\n" +
            "• Move it (W) to the fishing spot; set its HEIGHT (Y) to the waterline.\n" +
            "• Scale it (R) to size the pool.\n" +
            "• No collider, so you can still click-move and fish at its edge.\n" +
            "• Put a 'NODE: Fishing' marker / fishing node next to it for an obvious fishing spot.\n\n" +
            "Run again to add more pools. SAVE the scene (Ctrl+S) to keep them.",
            "Got it");
    }

    static Material GetWaterMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (existing != null) return existing;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader) { name = "WastelandWater" };

        var water = new Color(0.16f, 0.42f, 0.70f, 0.62f);
        mat.color = water;                 // Standard fallback
        mat.SetColor("_BaseColor", water); // URP Lit

        // URP Lit → Transparent surface setup (done by hand since we're scripting the material).
        mat.SetFloat("_Surface", 1f);      // 0 = Opaque, 1 = Transparent
        mat.SetFloat("_Blend", 0f);        // Alpha blend
        mat.SetFloat("_Smoothness", 0.9f);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;

        if (!AssetDatabase.IsValidFolder("Assets/Art/Generated3D"))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
            AssetDatabase.CreateFolder("Assets/Art", "Generated3D");
        }
        AssetDatabase.CreateAsset(mat, MatPath);
        AssetDatabase.SaveAssets();
        return mat;
    }
}
