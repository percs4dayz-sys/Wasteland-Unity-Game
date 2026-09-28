using UnityEditor;
using UnityEngine;

/// <summary>
/// Phone texture budget. The asset packs ship PC-sized textures (many 2K–4K, and the Hivemind packs mark
/// hundreds as "Compression: None"), which put the Android build at 11 GB: far past the 4 GB an APK can
/// hold, and more than a phone can load. On Android only, every texture is capped at 1024 and uncompressed
/// colour / normal / sprite textures get compressed. A texture with its own Android override keeps its
/// format and only gets the size cap. Read/Write textures are left alone: scripts read or paint their
/// pixels (Sidekick's colour maps are exact 32×32 swatches it edits at runtime), and compressed formats
/// break that. PC imports are untouched. Unity saves the Android override it sets here into each
/// texture's .meta (Android section only), so the choices show up in git and stay put if this is removed.
/// </summary>
class AndroidTexturePolicy : AssetPostprocessor
{
    const int MaxSize = 1024;

    public override uint GetVersion() => 1;

    void OnPreprocessTexture()
    {
        var importer = (TextureImporter)assetImporter;
        if (importer.isReadable) return;
        var android = importer.GetPlatformTextureSettings("Android");
        if (android.overridden)
        {
            if (android.maxTextureSize <= MaxSize) return;
            android.maxTextureSize = MaxSize;
            importer.SetPlatformTextureSettings(android);
            return;
        }

        var baseline = importer.GetDefaultPlatformTextureSettings();
        bool compress = baseline.textureCompression == TextureImporterCompression.Uncompressed && Compressible(importer.textureType);
        if (baseline.maxTextureSize <= MaxSize && !compress) return;

        android.overridden = true;
        android.maxTextureSize = Mathf.Min(baseline.maxTextureSize, MaxSize);
        android.format = TextureImporterFormat.Automatic;
        android.textureCompression = compress ? TextureImporterCompression.Compressed : baseline.textureCompression;
        android.compressionQuality = baseline.compressionQuality;
        android.crunchedCompression = baseline.crunchedCompression;
        android.resizeAlgorithm = baseline.resizeAlgorithm;
        importer.SetPlatformTextureSettings(android);
    }

    static bool Compressible(TextureImporterType type) =>
        type == TextureImporterType.Default || type == TextureImporterType.NormalMap || type == TextureImporterType.Sprite;
}
