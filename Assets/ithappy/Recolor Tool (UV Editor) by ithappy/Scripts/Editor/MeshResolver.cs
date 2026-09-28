using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor.SceneManagement;

namespace ithappy.RecolorToolWindow
{
    public static class MeshResolver
    {
        public class MeshComponentInfo
        {
            public string DisplayName { get; set; }
            public Mesh Mesh { get; set; }
            public MeshFilter MeshFilter { get; set; }
            public SkinnedMeshRenderer SkinnedRenderer { get; set; }
            
            public override string ToString() => DisplayName;
        }
        
        public class ResolveResult
        {
            public Mesh Mesh { get; set; }
            public MeshFilter MeshFilter { get; set; }
            public SkinnedMeshRenderer SkinnedRenderer { get; set; }
            public GameObject GameObject { get; set; }
            public UnityEngine.Object OriginalObject { get; set; }
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public bool IsPrefab { get; set; }
            public List<MeshComponentInfo> AllMeshComponents { get; set; } = new List<MeshComponentInfo>();
            public int SelectedMeshIndex { get; set; }
        }

        public static ResolveResult Resolve(UnityEngine.Object obj, int selectedMeshIndex = 0)
        {
            var result = new ResolveResult
            {
                OriginalObject = obj,
                Success = false,
                SelectedMeshIndex = selectedMeshIndex
            };

            if (obj == null)
            {
                result.ErrorMessage = "Object is null";
                return result;
            }
            
            if (!(obj is GameObject gameObject))
            {
                result.ErrorMessage = "Please select a GameObject from the Project window.\nOnly saved prefabs are supported.";
                return result;
            }
            
            if (!IsPrefabAsset(gameObject))
            {
                result.ErrorMessage = "Please select a saved prefab from the Project window.\nScene objects are not supported.";
                return result;
            }
            
            result.IsPrefab = true;
            result.GameObject = gameObject;
            
            CollectMeshComponents(gameObject, result);
            
            if (result.AllMeshComponents.Count == 0)
            {
                result.ErrorMessage = "The selected prefab does not contain any mesh components.";
                return result;
            }
            
            if (selectedMeshIndex < 0)
            {
                result.Success = true;
                return result;
            }
            
            ApplySelectedMeshComponent(result, Mathf.Clamp(selectedMeshIndex, 0, result.AllMeshComponents.Count - 1));
            return result;
        }
        
        private static void CollectMeshComponents(GameObject gameObject, ResolveResult result)
        {
            var meshFilters = gameObject.GetComponentsInChildren<MeshFilter>(true);
            var skinnedRenderers = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
    
            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh != null)
                {
                    result.AllMeshComponents.Add(new MeshComponentInfo
                    {
                        DisplayName = $"{mf.name}",
                        Mesh = mf.sharedMesh,
                        MeshFilter = mf,
                        SkinnedRenderer = null
                    });
                }
            }
    
            foreach (var smr in skinnedRenderers)
            {
                if (smr.sharedMesh != null)
                {
                    result.AllMeshComponents.Add(new MeshComponentInfo
                    {
                        DisplayName = $"{smr.name}",
                        Mesh = smr.sharedMesh,
                        MeshFilter = null,
                        SkinnedRenderer = smr
                    });
                }
            }
        }
        
        private static void ApplySelectedMeshComponent(ResolveResult result, int index)
        {
            if (index < 0 || index >= result.AllMeshComponents.Count) return;
            
            var component = result.AllMeshComponents[index];
            result.Mesh = component.Mesh;
            result.MeshFilter = component.MeshFilter;
            result.SkinnedRenderer = component.SkinnedRenderer;
            result.SelectedMeshIndex = index;
            result.Success = result.Mesh != null;
        }
        
        private static string GetGameObjectPath(GameObject obj, GameObject root)
        {
            if (obj == root) return obj.name;
            
            string path = obj.name;
            Transform parent = obj.transform.parent;
            
            while (parent != null && parent.gameObject != root)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            
            return path;
        }

        private static bool IsPrefabAsset(GameObject gameObject)
        {
            if (PrefabUtility.IsPartOfPrefabAsset(gameObject))
                return true;
            
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                return false;
            
            if (gameObject.scene.IsValid())
                return false;
            
            string path = AssetDatabase.GetAssetPath(gameObject);
            return !string.IsNullOrEmpty(path) && !gameObject.scene.IsValid();
        }

        public static void ShowErrorDialog(string errorMessage)
        {
            EditorUtility.DisplayDialog("Invalid Selection", errorMessage, "OK");
        }
    }
}