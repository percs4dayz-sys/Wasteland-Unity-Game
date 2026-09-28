using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Imports a heightmap image (BMP, PNG, JPG, RAW) and applies it to the active
/// Unity Terrain. BMP/PNG are strongly preferred.
///
/// CRITICAL: Unity terrain heightmapResolution MUST be 2^n + 1 (33, 65, 129,
/// 257, 513, 1025, 2049, 4097).  This tool automatically resamples ANY source
/// image to the nearest valid terrain resolution, so the "pin needles" spike
/// bug never happens.
///
/// Menu:  Wasteland ▸ Terrain ▸ Import Heightmap from Image
/// </summary>
public class RawHeightmapImporter : EditorWindow
{
    // ── Source image (BMP / PNG / JPG — preferred) ────────────────
    private Texture2D _sourceImage;
    private bool _readabilityEnsured; // prevent reimport-loop in OnGUI

    // ── RAW fallback ───────────────────────────────────────────────
    private string _rawPath = "";
    private enum BitDepth { Auto, EightBit, SixteenBitLE, SixteenBitBE }
    private BitDepth _bitDepth = BitDepth.Auto;
    private int _rawWidth  = 1025;
    private int _rawHeight = 1025;

    // ── Terrain resolution (always valid 2^n+1) ───────────────────
    private int _terrainRes = 1025;

    // ── Transform ──────────────────────────────────────────────────
    private bool _flipVertically   = false;
    private bool _flipHorizontally = false;
    private bool _invert           = false;

    // ── Smoothing ─────────────────────────────────────────────────
    private int _smoothPasses = 3;      // blur iterations to kill AI texture noise
    private int _smoothRadius = 2;      // box-blur radius in pixels

    // ── Height remap ───────────────────────────────────────────────
    private float _heightMultiplier = 60f;

    // ── Preview ────────────────────────────────────────────────────
    private Texture2D _previewTex;
    private string _fileInfo = "";

    // ═══════════════════════════════════════════════════════════════
    [MenuItem("Wasteland/Archived/Terrain/Import Heightmap from Image", false, 9000)]
    public static void Open() =>
        GetWindow<RawHeightmapImporter>("Heightmap Importer").minSize = new Vector2(420, 490);

    void OnGUI()
    {
        GUILayout.Label("Heightmap Image \u2192 Unity Terrain", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        EditorGUILayout.HelpBox(
            "Drag a BMP, PNG, or JPG heightmap here.  The tool resamples\n" +
            "it to the correct power-of-two+1 resolution automatically \u2014\n" +
            "no more pin-needle spikes!",
            MessageType.Info);

        EditorGUILayout.Space(8);

        // ── Image source ──────────────────────────────────────────
        GUILayout.Label("Source Image  (BMP / PNG / JPG)", EditorStyles.boldLabel);
        var prevImg = _sourceImage;
        _sourceImage = (Texture2D)EditorGUILayout.ObjectField(
            "Heightmap", _sourceImage, typeof(Texture2D), false);

        if (_sourceImage != prevImg)
            OnSourceImageChanged();

        if (_sourceImage != null)
        {
            if (!_readabilityEnsured)
                EnsureReadable(_sourceImage);
            EditorGUILayout.LabelField(
                $"  Source: {_sourceImage.width}\u00d7{_sourceImage.height}" +
                $"  \u2192  Terrain: {_terrainRes}\u00d7{_terrainRes}" +
                $"  ({_sourceImage.format})");
        }

        EditorGUILayout.Space(8);

        // ── RAW fallback ──────────────────────────────────────────
        GUILayout.Label("RAW file  (fallback \u2014 use BMP if you have it)", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            _rawPath = EditorGUILayout.TextField(_rawPath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string sel = EditorUtility.OpenFilePanel("Select RAW heightmap", "Assets",
                    "raw,data,bin,r16,r8");
                if (!string.IsNullOrEmpty(sel))
                    _rawPath = AbsToAssetPath(sel);
            }
        }

        if (_sourceImage == null && !string.IsNullOrEmpty(_rawPath))
        {
            _bitDepth = (BitDepth)EditorGUILayout.EnumPopup("Bit Depth", _bitDepth);
            using (new EditorGUILayout.HorizontalScope())
            {
                _rawWidth  = EditorGUILayout.IntField("Width",  _rawWidth);
                _rawHeight = EditorGUILayout.IntField("Height", _rawHeight);
            }
            if (GUILayout.Button("Analyse RAW File", GUILayout.Height(22)))
                AnalyseRawFile();
        }

        if (!string.IsNullOrEmpty(_fileInfo))
            EditorGUILayout.HelpBox(_fileInfo, MessageType.None);

        EditorGUILayout.Space(8);

        // ── Terrain resolution ────────────────────────────────────
        GUILayout.Label("Terrain Settings", EditorStyles.boldLabel);
        _terrainRes = EditorGUILayout.IntSlider("Heightmap Resolution", _terrainRes, 33, 4097);
        // snap to nearest valid
        _terrainRes = SnapToValidRes(_terrainRes);
        EditorGUILayout.LabelField(
            $"  (valid resolutions: 33, 65, 129, 257, 513, 1025, 2049, 4097)",
            EditorStyles.miniLabel);

        _heightMultiplier = EditorGUILayout.FloatField("Max Height (m)", _heightMultiplier);

        EditorGUILayout.Space(8);

        // ── Smoothing (kill AI texture noise / pin needles) ──────
        GUILayout.Label("Smoothing  (blur away AI texture noise)", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Passes", GUILayout.Width(48));
            _smoothPasses = EditorGUILayout.IntSlider(_smoothPasses, 0, 20);
            EditorGUILayout.LabelField("Radius", GUILayout.Width(40));
            _smoothRadius = EditorGUILayout.IntSlider(_smoothRadius, 1, 8);
        }
        if (GUILayout.Button("Refresh Preview with Smoothing", GUILayout.Height(22)))
            BuildPreview();

        EditorGUILayout.Space(4);

        // ── Transform ─────────────────────────────────────────────
        _flipVertically   = EditorGUILayout.Toggle("Flip Vertically",   _flipVertically);
        _flipHorizontally = EditorGUILayout.Toggle("Flip Horizontally", _flipHorizontally);
        _invert           = EditorGUILayout.Toggle("Invert (swap black/white)", _invert);

        EditorGUILayout.Space(8);

        // ── Preview ───────────────────────────────────────────────
        if (_previewTex != null)
        {
            GUILayout.Label("Preview  (at terrain resolution)", EditorStyles.boldLabel);
            Rect r = GUILayoutUtility.GetRect(256, 256);
            EditorGUI.DrawPreviewTexture(r, _previewTex, null, ScaleMode.ScaleToFit);
        }

        EditorGUILayout.Space(12);

        // ── Apply ─────────────────────────────────────────────────
        bool hasSource = _sourceImage != null ||
                         (!string.IsNullOrEmpty(_rawPath) && File.Exists(AssetPathToAbs(_rawPath)));
        GUI.enabled = hasSource;
        if (GUILayout.Button("APPLY HEIGHTMAP TO TERRAIN", GUILayout.Height(40)))
            ApplyHeightmap();
        GUI.enabled = true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  SOURCE IMAGE CHANGED
    // ═══════════════════════════════════════════════════════════════
    void OnSourceImageChanged()
    {
        _fileInfo = "";
        _readabilityEnsured = false;
        DestroyPreview();

        if (_sourceImage == null) return;

        EnsureReadable(_sourceImage);

        // Auto-pick the closest valid terrain resolution
        int srcW = _sourceImage.width;
        _terrainRes = SnapToValidRes(srcW);

        _fileInfo = $"{_sourceImage.name}: {srcW}\u00d7{_sourceImage.height}" +
                    $" \u2192 terrain res {_terrainRes}\u00d7{_terrainRes}";

        BuildPreview();
    }

    // ═══════════════════════════════════════════════════════════════
    //  PREVIEW
    // ═══════════════════════════════════════════════════════════════
    void BuildPreview()
    {
        DestroyPreview();
        int r = _terrainRes;
        if (r <= 0 || r > 4096) return;

        _previewTex = new Texture2D(r, r, TextureFormat.RGBA32, false);
        float[] data = ResampleSourceToTerrain(r);

        // Apply smoothing before preview
        if (_smoothPasses > 0)
            data = BoxBlur(data, r, r, _smoothRadius, _smoothPasses);

        for (int y = 0; y < r; y++)
            for (int x = 0; x < r; x++)
            {
                float v = Mathf.Clamp01(data[y * r + x]);
                _previewTex.SetPixel(x, y, new Color(v, v, v, 1f));
            }
        _previewTex.Apply();
        
        Repaint();
    }

    // ═══════════════════════════════════════════════════════════════
    //  RESAMPLE source image \u2192 float[] at target terrain resolution
    //  Uses bilinear sampling so ANY source resolution works.
    // ═══════════════════════════════════════════════════════════════
    float[] ResampleSourceToTerrain(int terrainRes)
    {
        var result = new float[terrainRes * terrainRes];

        if (_sourceImage != null)
        {
            // Sample the Unity Texture2D with bilinear filtering at
            // each terrain heightmap sample point.
            // u,v in 0..1 across the source texture.
            for (int y = 0; y < terrainRes; y++)
            {
                float v = y / (float)(terrainRes - 1);
                for (int x = 0; x < terrainRes; x++)
                {
                    float u = x / (float)(terrainRes - 1);
                    Color c = _sourceImage.GetPixelBilinear(u, v);
                    result[y * terrainRes + x] = c.grayscale;
                }
            }
        }
        else if (!string.IsNullOrEmpty(_rawPath))
        {
            // RAW fallback: read the whole file, then nearest-neighbour resample
            string absPath = AssetPathToAbs(_rawPath);
            var rawBytes = File.ReadAllBytes(absPath);

            // Read raw at native resolution
            float[] rawData = ReadHeightData(rawBytes, _rawWidth, _rawHeight);

            // Nearest-neighbour resample to terrainRes
            for (int y = 0; y < terrainRes; y++)
            {
                int srcY = Mathf.Clamp((int)(y * (float)_rawHeight / terrainRes), 0, _rawHeight - 1);
                for (int x = 0; x < terrainRes; x++)
                {
                    int srcX = Mathf.Clamp((int)(x * (float)_rawWidth / terrainRes), 0, _rawWidth - 1);
                    result[y * terrainRes + x] = rawData[srcY * _rawWidth + srcX];
                }
            }
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    //  APPLY
    // ═══════════════════════════════════════════════════════════════
    void ApplyHeightmap()
    {
        int res = _terrainRes;
        res = SnapToValidRes(res);
        _terrainRes = res;

        // ── Resample source to terrain resolution ─────────────────
        float[] samples = ResampleSourceToTerrain(res);

        // ── Smooth to kill AI texture noise ────────────────────────
        if (_smoothPasses > 0)
        {
            EditorUtility.DisplayProgressBar("Heightmap Importer",
                $"Smoothing ({_smoothPasses} passes, radius {_smoothRadius})...", 0.5f);
            samples = BoxBlur(samples, res, res, _smoothRadius, _smoothPasses);
        }

        // ── Get or create terrain ─────────────────────────────────
        Terrain terrain = Selection.activeGameObject?.GetComponent<Terrain>();
        if (terrain == null) terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Art/Generated3D"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Art"))
                    AssetDatabase.CreateFolder("Assets", "Art");
                AssetDatabase.CreateFolder("Assets/Art", "Generated3D");
            }

            const string dataPath = "Assets/Art/Generated3D/ImportedHeightmap.asset";
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath) != null)
                AssetDatabase.DeleteAsset(dataPath);

            var data = new TerrainData
            {
                heightmapResolution = res,
                size = new Vector3(600f, _heightMultiplier, 600f)
            };
            AssetDatabase.CreateAsset(data, dataPath);
            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Ground";
            go.transform.position = new Vector3(-300f, 0f, -300f);
            terrain = go.GetComponent<Terrain>();
        }

        // ── Configure terrain ─────────────────────────────────────
        terrain.terrainData.heightmapResolution = res;
        terrain.terrainData.size = new Vector3(
            terrain.terrainData.size.x,
            _heightMultiplier,
            terrain.terrainData.size.z);

        // ── Build float[,] and apply ──────────────────────────────
        var heights = new float[res, res];

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                int srcY = _flipVertically   ? (res - 1 - y) : y;
                int srcX = _flipHorizontally ? (res - 1 - x) : x;
                int idx  = srcY * res + srcX;

                float val = samples[idx];
                if (_invert) val = 1f - val;
                heights[y, x] = Mathf.Clamp01(val);
            }
        }

        terrain.terrainData.SetHeights(0, 0, heights);
        terrain.Flush();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = terrain.gameObject;

        EditorUtility.DisplayDialog("Heightmap Applied",
            $"Heightmap applied to terrain.\n\n" +
            $"Source: {(_sourceImage != null ? _sourceImage.name : Path.GetFileName(_rawPath))}\n" +
            $"Terrain resolution: {res}\u00d7{res}\n" +
            $"Max height: {_heightMultiplier} m\n\n" +
            "SAVE the scene (Ctrl+S).", "OK");
    }

    // ═══════════════════════════════════════════════════════════════
    //  RAW FILE ANALYSIS
    // ═══════════════════════════════════════════════════════════════
    void AnalyseRawFile()
    {
        string absPath = AssetPathToAbs(_rawPath);
        if (!File.Exists(absPath))
        {
            _fileInfo = "File not found.";
            return;
        }

        long len = new FileInfo(absPath).Length;
        var bytes = File.ReadAllBytes(absPath);

        int equal = 0, diff = 0;
        byte bMin = 255, bMax = 0;
        int pairs = (int)(len / 2);
        for (int i = 0; i < len - 1; i += 2)
        {
            if (bytes[i] == bytes[i + 1]) equal++; else diff++;
            if (bytes[i]     < bMin) bMin = bytes[i];
            if (bytes[i]     > bMax) bMax = bytes[i];
            if (bytes[i + 1] < bMin) bMin = bytes[i + 1];
            if (bytes[i + 1] > bMax) bMax = bytes[i + 1];
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Size: {len:N0} bytes    Byte range: {bMin}\u2013{bMax}");
        sb.AppendLine($"Equal 16-bit pairs: {equal}/{pairs}  (diff: {diff})");

        if (diff == 0)
        {
            int guess = (int)Mathf.Sqrt(len / 2f);
            if (guess * guess == len / 2)
            {
                _rawWidth = guess; _rawHeight = guess;
                _bitDepth = BitDepth.EightBit;
                sb.AppendLine($"\u2192 8-bit data duplicated into 16-bit.  {guess}\u00d7{guess}");
            }
            else
            {
                sb.AppendLine($"\u2192 8-bit-in-16bit but non-square ({len / 2f} pixels)");
            }
        }
        else
        {
            int guess = (int)Mathf.Sqrt(len / 2f);
            if (guess * guess == len / 2)
            {
                _rawWidth = guess; _rawHeight = guess;
                sb.AppendLine($"\u2192 True 16-bit RAW, {guess}\u00d7{guess}");
            }
            else
            {
                sb.AppendLine($"\u2192 16-bit RAW, non-square ({len / 2f} pixels)");
            }
        }

        _fileInfo = sb.ToString().TrimEnd();
        _terrainRes = SnapToValidRes(_rawWidth);
    }

    // ═══════════════════════════════════════════════════════════════
    //  RAW byte reading (unchanged)
    // ═══════════════════════════════════════════════════════════════
    float[] ReadHeightData(byte[] raw, int w, int h)
    {
        int totalPixels = w * h;
        var result = new float[totalPixels];

        bool isEightBit;
        if (_bitDepth == BitDepth.Auto)
        {
            int equal = 0;
            long maxCheck = System.Math.Min(raw.Length - 1, 20000L);
            int checkPairs = (int)(maxCheck / 2);
            for (int i = 0; i < maxCheck - 1; i += 2)
                if (raw[i] == raw[i + 1]) equal++;
            isEightBit = (equal == checkPairs);
        }
        else
        {
            isEightBit = (_bitDepth == BitDepth.EightBit);
        }

        if (isEightBit)
        {
            for (int i = 0; i < totalPixels && i * 2 < raw.Length; i++)
                result[i] = raw[i * 2] / 255f;
        }
        else
        {
            bool bigEndian = (_bitDepth == BitDepth.SixteenBitBE);
            for (int i = 0; i < totalPixels && i * 2 + 1 < raw.Length; i++)
            {
                int lo = raw[i * 2], hi = raw[i * 2 + 1];
                int val = bigEndian ? (hi << 8) | lo : (lo | (hi << 8));
                result[i] = val / 65535f;
            }
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    //  SEPARABLE BOX BLUR  — kills AI texture noise / pin-needle spikes
    //  Horizontal pass then vertical pass = O(r) instead of O(r²)
    // ═══════════════════════════════════════════════════════════════
    static float[] BoxBlur(float[] src, int w, int h, int radius, int passes)
    {
        float[] buf1 = new float[w * h];
        float[] buf2 = new float[w * h];
        System.Array.Copy(src, buf1, src.Length);

        for (int pass = 0; pass < passes; pass++)
        {
            float pct = (float)pass / passes;
            // --- Horizontal pass ---
            for (int y = 0; y < h; y++)
            {
                if (y % 128 == 0 && passes > 1)
                    EditorUtility.DisplayProgressBar("Heightmap Importer",
                        $"Smoothing pass {pass+1}/{passes} (horizontal)...", pct + 0.4f / passes);
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f;
                    int count = 0;
                    int xMin = Mathf.Max(0, x - radius);
                    int xMax = Mathf.Min(w - 1, x + radius);
                    for (int sx = xMin; sx <= xMax; sx++)
                    {
                        sum += buf1[y * w + sx];
                        count++;
                    }
                    buf2[y * w + x] = sum / count;
                }
            }

            // --- Vertical pass ---
            for (int y = 0; y < h; y++)
            {
                if (y % 128 == 0 && passes > 1)
                    EditorUtility.DisplayProgressBar("Heightmap Importer",
                        $"Smoothing pass {pass+1}/{passes} (vertical)...", pct + 0.8f / passes);
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f;
                    int count = 0;
                    int yMin = Mathf.Max(0, y - radius);
                    int yMax = Mathf.Min(h - 1, y + radius);
                    int rowStride = w;
                    for (int sy = yMin; sy <= yMax; sy++)
                    {
                        sum += buf2[sy * rowStride + x];
                        count++;
                    }
                    buf1[y * w + x] = sum / count;
                }
            }
        }

        return buf1;
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Snap to the nearest valid Unity terrain resolution (2^n + 1).</summary>
    static int SnapToValidRes(int desired)
    {
        int[] valid = { 33, 65, 129, 257, 513, 1025, 2049, 4097 };
        int best = valid[0];
        foreach (int v in valid)
            if (System.Math.Abs(v - desired) < System.Math.Abs(best - desired))
                best = v;
        return best;
    }

    void EnsureReadable(Texture2D tex)
    {
        if (tex == null) return;
        string path = AssetDatabase.GetAssetPath(tex);
        if (string.IsNullOrEmpty(path)) return;

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        // Only reimport if actually needed, and only ONCE per image change
        if (!importer.isReadable || importer.textureCompression != TextureImporterCompression.Uncompressed)
        {
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        _readabilityEnsured = true; // prevents reimport-loop in OnGUI
    }

    string AssetPathToAbs(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return "";
        if (Path.IsPathRooted(assetPath)) return assetPath;
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
    }

    string AbsToAssetPath(string absPath)
    {
        string dataPath = Path.GetFullPath(Application.dataPath);
        string full = Path.GetFullPath(absPath);
        if (full.StartsWith(dataPath, System.StringComparison.OrdinalIgnoreCase))
            return "Assets" + full.Substring(dataPath.Length).Replace('\\', '/');
        return absPath;
    }

    void DestroyPreview()
    {
        if (_previewTex != null)
        {
            DestroyImmediate(_previewTex);
            _previewTex = null;
        }
    }

    void OnDisable() => DestroyPreview();
}
