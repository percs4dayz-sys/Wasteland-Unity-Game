using UnityEngine;
using UnityEditor;

// Assigns the single shared atlas material to every POLYGON Apocalypse Wasteland
// model as it imports, and skips creating per-model material assets.
public class ApocWastelandModelPostprocessor : AssetPostprocessor
{
    const string TargetDir = "Assets/Synty/ApocalypseWasteland/Models";
    const string SharedMatPath = "Assets/Synty/ApocalypseWasteland/M_ApocalypseWasteland.mat";

    bool IsTarget => assetPath.Replace('\\', '/').StartsWith(TargetDir);

    void OnPreprocessModel()
    {
        if (!IsTarget) return;
        var importer = (ModelImporter)assetImporter;
        importer.materialImportMode = ModelImporterMaterialImportMode.None; // no per-model mats
        importer.importCameras = false;
        importer.importLights = false;
    }

    void OnPostprocessModel(GameObject g)
    {
        if (!IsTarget) return;
        var shared = AssetDatabase.LoadAssetAtPath<Material>(SharedMatPath);
        if (shared == null) return;
        foreach (var r in g.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = shared;
            r.sharedMaterials = mats;
        }
    }
}
