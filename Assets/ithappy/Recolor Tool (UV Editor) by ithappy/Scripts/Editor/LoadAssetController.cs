using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace ithappy.RecolorToolWindow
{
    public static class LoadAssetController
    {
        public static VisualTreeAsset GetLayout()
        {
            return LoadAsset<VisualTreeAsset>("VisualTreeAsset", "RecolorToolWindowLayout");
        }
        
        public static Texture2D GetGridTexture()
        {
            return LoadAsset<Texture2D>("Texture", "RecolorToolAxisTexture");
        }

        public static Texture2D GetCheckerTexture()
        {
            return LoadAsset<Texture2D>("Texture", "RecolorToolGridTexture");
        }
        
        public static Texture2D GetMainTexture()
        {
            return LoadAsset<Texture2D>("Texture2D", "Textures_4");
        }
        
        public static T LoadAsset<T>(string type, string fileName) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(fileName))
                return null;
            
            string filter = string.IsNullOrEmpty(type) ? fileName : $"t:{type} {fileName}";
            string[] guids = AssetDatabase.FindAssets(filter);
    
            if (guids.Length == 0)
                return null;
            
            if (guids.Length == 1)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<T>(path);
            }
            
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string nameWithoutExtension = Path.GetFileNameWithoutExtension(path);
        
                if (nameWithoutExtension.Equals(fileName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return AssetDatabase.LoadAssetAtPath<T>(path);
                }
            }
            
            string firstPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<T>(firstPath);
        }
        
        public static Material GetOrCreateMasterMaterial(Texture2D texture)
        {
            if (texture == null) return null;

            string texturePath = AssetDatabase.GetAssetPath(texture);
            string dir = string.IsNullOrEmpty(texturePath) ? "Assets" : System.IO.Path.GetDirectoryName(texturePath);
            string materialPath = System.IO.Path.Combine(dir, "RecolorTool_MasterMaterial.mat").Replace("\\", "/");

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (existing != null)
            {
                existing.mainTexture = texture;
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }

            Shader shader = GraphicsSettings.currentRenderPipeline != null
                ? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Standard")
                : Shader.Find("Standard");

            if (shader == null) return null;

            Material mat = new Material(shader);
            mat.mainTexture = texture;
            mat.name = "MasterMaterial";

            AssetDatabase.CreateAsset(mat, materialPath);
            AssetDatabase.SaveAssets();

            return AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        }
    }
}

