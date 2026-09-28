using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Drops flowing water using the Simple Water Shader (IgniteCoders) asset:
///   • Add Pool      — a flat water surface for crater pools / ponds / lakes.
///   • Add Waterfall — a vertical water sheet to run down a cliff into a pool.
/// Both use the asset's animated Water_mat_01 (swap to _02/_03 in the Inspector for calmer/rougher
/// looks) and have NO collider, so you can walk into them. Move/scale to taste.
///
/// Menu:  Wasteland ▸ Water ▸ Add Pool / Add Waterfall
/// </summary>
public static class WaterFeatureTool
{
    const string MatPath = "Assets/IgniteCoders/Simple Water Shader/Resources/Water_mat_01.mat";

    [MenuItem("Wasteland/Archived/Water/Add Pool (flat)", false, 9000)]
    public static void AddPool()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Plane);   // 10 m at scale 1, lies flat
        go.name = "Water Pool";
        StripCollider(go);
        ApplyWater(go);
        Place(go, new Vector3(3f, 1f, 3f));   // 30×30 m default
        Finish(go,
            "FILLING A CRATER:\n" +
            "1. Move it (W) over your crater.\n" +
            "2. Set its HEIGHT (Y) to the waterline (a bit below the rim).\n" +
            "3. Scale it (R) so its edges tuck INTO the crater walls — then it reads as a filled pool, " +
            "not a floating sheet. Everything below the surface looks submerged.");
    }

    [MenuItem("Wasteland/Archived/Water/Add Waterfall (vertical)", false, 9000)]
    public static void AddWaterfall()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);    // 1×1 vertical sheet, faces +Z
        go.name = "Waterfall";
        StripCollider(go);
        ApplyWater(go);
        Place(go, new Vector3(4f, 8f, 1f));   // 4 m wide, 8 m tall
        Finish(go,
            "MAKING A WATERFALL:\n" +
            "1. Move it (W) onto the cliff face, top of the drop down into the pool.\n" +
            "2. Rotate (E) so the flat side faces out from the cliff.\n" +
            "3. Scale (R) to match the height/width of the fall.\n\n" +
            "If it vanishes when you look from the other side, tell me — I'll make it double-sided.");
    }

    static void ApplyWater(GameObject go)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        else EditorUtility.DisplayDialog("Water material not found",
            "Couldn't find:\n" + MatPath + "\n\nMake sure the Simple Water Shader asset imported there.", "OK");
    }

    static void StripCollider(GameObject go)
    {
        var col = go.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);
    }

    static void Place(GameObject go, Vector3 scale)
    {
        var sv = SceneView.lastActiveSceneView;
        go.transform.position = sv != null ? sv.pivot : Vector3.zero;
        go.transform.localScale = scale;
        Undo.RegisterCreatedObjectUndo(go, "Add " + go.name);
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
    }

    static void Finish(GameObject go, string tip)
    {
        EditorSceneManager.MarkSceneDirty(go.scene);
        EditorUtility.DisplayDialog(go.name, tip + "\n\nSAVE the scene (Ctrl+S) to keep it.", "OK");
    }
}
