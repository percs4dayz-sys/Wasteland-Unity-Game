using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ithappy.RecolorToolWindow
{
    public static class MaterialResolver
    {
        private static readonly HashSet<string> MainTextureNames = new HashSet<string>
        {
            "_BaseMap", "_MainTex", "_AlbedoMap", "_AlbedoTex",
            "_Main", "_Albedo", "_BaseTexture", "_BaseTex", "_Base_Texture"
        };
        
        private static readonly string[] TexturePropertyKeywords = { "albedo", "main", "base", "color" };
        
        public static bool HasTextureProperty(this Material material, string propertyName)
        {
            if (material?.shader == null)
                return false;

            var shader = material.shader;
            int propertyCount = shader.GetPropertyCount();

            for (int i = 0; i < propertyCount; i++)
            {
                if (shader.GetPropertyName(i) == propertyName &&
                    shader.GetPropertyType(i) == ShaderPropertyType.Texture)
                {
                    return true;
                }
            }

            return false;
        }
        
        public static Texture GetMainTexture(this Material material)
        {
            if (material?.shader == null)
                return null;
            
            var mainTex = TryGetStandardMainTexture(material);
            if (mainTex != null)
                return mainTex;
            
            string propertyName = FindMainTexturePropertyName(material);
            return propertyName != null ? material.GetTexture(propertyName) : null;
        }
        
        public static Vector2 GetMainTextureOffset(this Material material)
        {
            if (material?.shader == null)
                return Vector2.zero;
            
            if (TryGetStandardMainTexture(material) != null)
                return material.mainTextureOffset;
            
            string propertyName = FindMainTexturePropertyName(material);
            return propertyName != null ? material.GetTextureOffset(propertyName) : Vector2.zero;
        }
        
        public static Vector2 GetMainTextureScale(this Material material)
        {
            if (material?.shader == null)
                return Vector2.one;
            
            if (TryGetStandardMainTexture(material) != null)
                return material.mainTextureScale;
            
            string propertyName = FindMainTexturePropertyName(material);
            return propertyName != null ? material.GetTextureScale(propertyName) : Vector2.one;
        }
        
        private static Texture TryGetStandardMainTexture(Material material)
        {
            if (material.HasTextureProperty("_MainTex"))
                return material.mainTexture;

            return null;
        }
        
        private static string FindMainTexturePropertyName(Material material)
        {
            foreach (string name in MainTextureNames)
            {
                if (material.HasTextureProperty(name))
                    return name;
            }
            
            var texturePropertyNames = material.GetTexturePropertyNames();

            foreach (string name in texturePropertyNames)
            {
                string lowerName = name.ToLowerInvariant();
                foreach (string keyword in TexturePropertyKeywords)
                {
                    if (lowerName.Contains(keyword))
                        return name;
                }
            }

            return null;
        }
    }
}
