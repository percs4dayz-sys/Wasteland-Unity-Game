using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

namespace ithappy.RecolorToolWindow
{
    public static class MeshCloneService
    {
        private const string FINAL_SUFFIX = "_UVEdited";
        
        public static Mesh SaveMeshAsAsset(Mesh workingMesh, Mesh originalMesh)
        {
            if (workingMesh == null || originalMesh == null) return null;
    
            string originalAssetPath = AssetDatabase.GetAssetPath(originalMesh);

            string folder = "Assets";

            if (!string.IsNullOrEmpty(originalAssetPath))
            {
                folder = Path.GetDirectoryName(originalAssetPath).Replace("\\", "/");
        
                if (originalAssetPath.Contains("unity_builtin_extra") || originalAssetPath.Contains("Library/"))
                {
                    folder = "Assets";
                }
            }
            
            string baseName = originalMesh.name;
            while (baseName.EndsWith(FINAL_SUFFIX))
            {
                baseName = baseName.Substring(0, baseName.Length - FINAL_SUFFIX.Length);
            }
    
            string desiredName = baseName + FINAL_SUFFIX + ".asset";
            string fullPath = Path.Combine(folder, desiredName).Replace("\\", "/");
            string finalPath = AssetDatabase.GenerateUniqueAssetPath(fullPath);
    
            Mesh finalMesh = Object.Instantiate(workingMesh);
            finalMesh.name = Path.GetFileNameWithoutExtension(finalPath);

            AssetDatabase.CreateAsset(finalMesh, finalPath);
            AssetDatabase.SaveAssets();
    
            Debug.Log($"Saved mesh asset: {finalPath}");
    
            return finalMesh;
        }

        public static GameObject CreateNewPrefab(GameObject originalPrefab, Dictionary<int, Mesh> newMeshes,
            List<MeshResolver.MeshComponentInfo> meshComponents, Material[] previewMaterials = null)
        {
            if (originalPrefab == null) return null;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string originalPath = AssetDatabase.GetAssetPath(originalPrefab);
            string directory = string.IsNullOrEmpty(originalPath) ? "Assets" : Path.GetDirectoryName(originalPath);
            string baseName = originalPrefab.name;
            while (baseName.EndsWith(FINAL_SUFFIX))
                baseName = baseName.Substring(0, baseName.Length - FINAL_SUFFIX.Length);
            string prefabName = baseName + FINAL_SUFFIX + ".prefab";
            string newPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(directory, prefabName));

            GameObject tempInstance = Object.Instantiate(originalPrefab);
            tempInstance.name = originalPrefab.name;

            foreach (var kvp in newMeshes)
            {
                int index = kvp.Key;
                Mesh newMesh = kvp.Value;

                if (index < meshComponents.Count)
                {
                    var comp = meshComponents[index];

                    if (comp.MeshFilter != null)
                    {
                        var allMF = tempInstance.GetComponentsInChildren<MeshFilter>(true);
                        foreach (var mf in allMF)
                        {
                            if (mf.name == comp.MeshFilter.name)
                            {
                                mf.sharedMesh = newMesh;
                                break;
                            }
                        }
                    }
                    else if (comp.SkinnedRenderer != null)
                    {
                        var allSMR = tempInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                        foreach (var smr in allSMR)
                        {
                            if (smr.name == comp.SkinnedRenderer.name)
                            {
                                smr.sharedMesh = newMesh;
                                break;
                            }
                        }
                    }
                }
            }
            
            if (previewMaterials != null && previewMaterials.Length > 0 && previewMaterials[0] != null)
            {
                var allRenderers = tempInstance.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in allRenderers)
                {
                    var materials = new Material[renderer.sharedMaterials.Length];
                    for (int i = 0; i < materials.Length; i++)
                    {
                        materials[i] = previewMaterials[0];
                    }

                    renderer.sharedMaterials = materials;
                }
            }

            GameObject newPrefab = PrefabUtility.SaveAsPrefabAsset(tempInstance, newPath);
            Object.DestroyImmediate(tempInstance);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return newPrefab;
        }
    }
}