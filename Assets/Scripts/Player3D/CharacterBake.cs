using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Freezes the finished character so the WORLD never needs Sidekick.
///
/// Sidekick is used exactly once — on the character creation screen. After that the game must be
/// able to rebuild your character with plain Unity calls. Part choices already survive in
/// CharacterAppearance, but two things don't, because they only exist inside Sidekick's runtime:
///
///   BODY BUILD  — blend shape weights on the skinned meshes. Sidekick drives them through its
///                 runtime object, but the weights themselves are ordinary values you can set by
///                 name on any SkinnedMeshRenderer.
///   COLOUR      — pixels Sidekick writes into the shared material's colour map. Once written, the
///                 texture is just a texture; saved as a PNG it needs nothing to reload.
///
/// So at Confirm we snapshot both, and in the world we replay them. No database, no part catalogue,
/// no runtime API — which is the whole point of doing this at creation time.
/// </summary>
public static class CharacterBake
{
    const string ShapesKey = "PlayerBakedShapes";
    static string ColorMapPath => Path.Combine(Application.persistentDataPath, "character_colormap.png");

    static Texture2D _cachedMap;
    static Dictionary<string, float> _cachedShapes;

    public static bool Exists => File.Exists(ColorMapPath) || !string.IsNullOrEmpty(PlayerPrefs.GetString(ShapesKey, ""));

    // ── capture (creation screen) ────────────────────────────────────────

    /// <summary>Snapshot the finished character: every non-zero blend shape weight, plus the colour
    /// map Sidekick painted. Call once, when the player confirms.</summary>
    public static void Capture(GameObject model, Material sharedMaterial)
    {
        CaptureShapes(model);
        CaptureColorMap(sharedMaterial);
        _cachedMap = null;
        _cachedShapes = null;
    }

    static void CaptureShapes(GameObject model)
    {
        if (model == null) return;

        var weights = new Dictionary<string, float>();
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            if (mesh == null) continue;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                float w = smr.GetBlendShapeWeight(i);
                if (Mathf.Abs(w) < 0.01f) continue;          // only what was actually moved
                weights[mesh.GetBlendShapeName(i)] = w;
            }
        }

        var sb = new System.Text.StringBuilder();
        foreach (var kv in weights)
        {
            if (sb.Length > 0) sb.Append('|');
            sb.Append(kv.Key).Append('=')
              .Append(kv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        PlayerPrefs.SetString(ShapesKey, sb.ToString());
        PlayerPrefs.Save();
        Debug.Log($"[CharacterBake] Saved {weights.Count} blend shape weight(s).");
    }

    static void CaptureColorMap(Material sharedMaterial)
    {
        // Sidekick paints the creator's colour picks into "_ColorMap" (not the main texture slot).
        Texture2D tex = null;
        if (sharedMaterial != null)
        {
            if (sharedMaterial.HasProperty("_ColorMap")) tex = sharedMaterial.GetTexture("_ColorMap") as Texture2D;
            if (tex == null) tex = sharedMaterial.mainTexture as Texture2D;
        }
        if (tex == null)
        {
            Debug.LogWarning("[CharacterBake] No colour map on the material — colours won't persist.");
            return;
        }

        try
        {
            // Sidekick builds this texture at runtime via SetPixels, so it's normally readable.
            // If it isn't — or it's a compressed format, which EncodeToPNG can't write (the untouched
            // default map on a phone build) — copy it through a RenderTexture rather than failing outright.
            Texture2D readable = tex;
            if (!IsReadable(tex) || UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsCompressedFormat(tex.graphicsFormat))
                readable = MakeReadable(tex);

            File.WriteAllBytes(ColorMapPath, readable.EncodeToPNG());
            if (readable != tex) Object.Destroy(readable);
            Debug.Log($"[CharacterBake] Saved colour map to {ColorMapPath}.");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[CharacterBake] Couldn't save the colour map: {ex.Message}");
        }
    }

    static bool IsReadable(Texture2D tex)
    {
        try { tex.GetPixel(0, 0); return true; }
        catch { return false; }
    }

    static Texture2D MakeReadable(Texture2D src)
    {
        var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;

        var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        copy.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return copy;
    }

    // ── replay (in the world) ────────────────────────────────────────────

    /// <summary>The baked colour map, or null if none was saved. Point-filtered and un-mipped —
    /// this is a swatch atlas, and bilinear filtering bleeds neighbouring colours into each other.</summary>
    public static Texture2D ColorMap()
    {
        if (_cachedMap != null) return _cachedMap;
        if (!File.Exists(ColorMapPath)) return null;

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        if (!tex.LoadImage(File.ReadAllBytes(ColorMapPath))) return null;

        _cachedMap = tex;
        return _cachedMap;
    }

    static Dictionary<string, float> Shapes()
    {
        if (_cachedShapes != null) return _cachedShapes;

        _cachedShapes = new Dictionary<string, float>();
        string raw = PlayerPrefs.GetString(ShapesKey, "");
        foreach (string pair in raw.Split('|'))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            if (float.TryParse(pair.Substring(eq + 1), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float w))
                _cachedShapes[pair.Substring(0, eq)] = w;
        }
        return _cachedShapes;
    }

    /// <summary>Replay the baked build onto a freshly-swapped part. Blend shape names are shared
    /// across parts, so each renderer picks up the ones its own mesh actually has.</summary>
    public static void ApplyShapes(SkinnedMeshRenderer smr)
    {
        var shapes = Shapes();
        if (smr == null || smr.sharedMesh == null || shapes.Count == 0) return;

        var mesh = smr.sharedMesh;
        for (int i = 0; i < mesh.blendShapeCount; i++)
            if (shapes.TryGetValue(mesh.GetBlendShapeName(i), out float w))
                smr.SetBlendShapeWeight(i, w);
    }

    public static void Clear()
    {
        PlayerPrefs.DeleteKey(ShapesKey);
        PlayerPrefs.Save();
        if (File.Exists(ColorMapPath)) File.Delete(ColorMapPath);
        _cachedMap = null;
        _cachedShapes = null;
    }
}
