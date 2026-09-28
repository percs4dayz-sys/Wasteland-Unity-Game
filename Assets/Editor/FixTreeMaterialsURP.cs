using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click: converts the Ambient-Occlusion-Trees materials from the legacy Built-in
/// "Nature/Tree Soft Occlusion Bark/Leaves" (and old Standard) shaders — which render
/// magenta in URP — over to "Universal Render Pipeline/Lit".
///
/// The legacy shaders are missing in URP, so Unity swaps in the error shader and hides
/// their properties. We therefore read the original _MainTex / _Color / _Cutoff straight
/// from the material's SERIALIZED data (m_SavedProperties) before re-binding them onto
/// _BaseMap / _BaseColor. Leaves (name contains "leaves"/"leaf") get alpha-clip + two-sided
/// rendering so the cutout TGAs read correctly; bark/trunk/stump stay opaque.
///
/// Idempotent — already-URP materials are skipped, so re-running after importing more trees
/// is safe.  Menu:  Wasteland ▸ World ▸ Fix Tree Materials (URP)
/// </summary>
public static class FixTreeMaterialsURP
{
    const string Folder = "Assets/FantasyEnvironments/Environments/Ambient-Occlusion-Trees/Materials";
    const string TexFolder = "Assets/FantasyEnvironments/Environments/Ambient-Occlusion-Trees/Textures";

    [MenuItem("Wasteland/World/Fix Tree Materials (URP)")]
    public static void Fix()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            EditorUtility.DisplayDialog("Fix Tree Materials (URP)",
                "Couldn't find the 'Universal Render Pipeline/Lit' shader — is URP installed/active?", "OK");
            return;
        }

        var guids = AssetDatabase.FindAssets("t:Material", new[] { Folder });
        int converted = 0, leafCount = 0, skipped = 0, noTex = 0;
        var log = new StringBuilder();

        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            // Skip ones already on URP/Lit with a base map bound.
            if (mat.shader == lit && mat.GetTexture("_BaseMap") != null) { skipped++; continue; }

            // Pull the originals out of serialized data BEFORE swapping shader.
            // Try _MainTex (Built-in) then _BaseMap (already-URP mats); finally fall back to a
            // texture in the sibling Textures/ folder whose name matches the material — handles
            // packs where a material was shipped with no texture assigned (e.g. Willow_bark).
            Texture baseTex = ReadTexEnv(mat, "_MainTex") ?? ReadTexEnv(mat, "_BaseMap");
            bool fromFolder = false;
            if (baseTex == null) { baseTex = FindTextureByName(mat.name); fromFolder = baseTex != null; }
            Color baseCol = ReadColor(mat, "_Color", Color.white);
            float cutoff = ReadFloat(mat, "_Cutoff", 0.5f);

            string lower = mat.name.ToLower();
            bool isLeaves = lower.Contains("leaves") || lower.Contains("leaf");

            mat.shader = lit;
            if (baseTex != null) mat.SetTexture("_BaseMap", baseTex); else noTex++;
            mat.SetColor("_BaseColor", baseCol);
            mat.SetFloat("_Smoothness", 0f);   // bark/leaves are matte
            mat.SetFloat("_Metallic", 0f);

            if (isLeaves)
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", cutoff <= 0f ? 0.5f : cutoff);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cull", 0f);              // Render Face = Both (two-sided leaves)
                mat.doubleSidedGI = true;
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                leafCount++;
            }
            else
            {
                mat.SetFloat("_AlphaClip", 0f);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cull", 2f);              // back-face cull
                mat.renderQueue = -1;                   // from shader (opaque)
            }

            EditorUtility.SetDirty(mat);
            log.AppendLine((isLeaves ? "[leaf] " : "[bark] ") + mat.name +
                (baseTex == null ? "   <-- NO base texture found!" : fromFolder ? "   (texture matched from Textures/)" : ""));
            converted++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Fix Tree Materials (URP)",
            $"Converted {converted} material(s) to URP/Lit  ({leafCount} leaf mats with alpha-clip + two-sided).\n" +
            $"{skipped} already URP, {noTex} had no base texture.\n\n{log}", "OK");
    }

    /// <summary>Finds a texture in the Textures/ folder whose file name matches the material name
    /// (exact, case-insensitive — e.g. material "Willow_bark" → "Willow_bark.png").</summary>
    static Texture FindTextureByName(string matName)
    {
        var guids = AssetDatabase.FindAssets(matName + " t:Texture", new[] { TexFolder });
        foreach (var g in guids)
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            if (System.IO.Path.GetFileNameWithoutExtension(p).Equals(matName, System.StringComparison.OrdinalIgnoreCase))
                return AssetDatabase.LoadAssetAtPath<Texture>(p);
        }
        return null;
    }

    // ── helpers: read values from m_SavedProperties even when the shader is the error shader ──

    static SerializedProperty FindPair(SerializedObject so, string arrayPath, string key)
    {
        var arr = so.FindProperty(arrayPath);
        if (arr == null) return null;
        for (int i = 0; i < arr.arraySize; i++)
        {
            var el = arr.GetArrayElementAtIndex(i);
            var name = el.FindPropertyRelative("first");
            if (name != null && name.stringValue == key) return el.FindPropertyRelative("second");
        }
        return null;
    }

    static Texture ReadTexEnv(Material mat, string key)
    {
        var second = FindPair(new SerializedObject(mat), "m_SavedProperties.m_TexEnvs", key);
        var tex = second?.FindPropertyRelative("m_Texture");
        return tex != null ? tex.objectReferenceValue as Texture : null;
    }

    static Color ReadColor(Material mat, string key, Color def)
    {
        var second = FindPair(new SerializedObject(mat), "m_SavedProperties.m_Colors", key);
        return second != null ? second.colorValue : def;
    }

    static float ReadFloat(Material mat, string key, float def)
    {
        var second = FindPair(new SerializedObject(mat), "m_SavedProperties.m_Floats", key);
        return second != null ? second.floatValue : def;
    }
}
