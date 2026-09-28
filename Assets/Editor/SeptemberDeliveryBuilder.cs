using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using G = CreatureMotion.Gait;

/// <summary>
/// Turns the September Meshy delivery into game-ready prefabs. Each folder under
/// Assets/Art/SeptemberDelivery holds a decimated GameModel.fbx (~10k tris, cut down from the 0.4–6M-tri
/// Meshy originals) plus Meshy's PBR maps.
///   • Walls & pillars (bone / car / concrete / garbage / scrap metal) → Worlds/BrokenCrescent/SeptemberDelivery/Walls:
///     scaled to wall height, pivot at the base centre with the wall running along local X, a BoxCollider
///     that blocks the player (holes in "damaged" pieces included — they're barriers), LOD cull at range.
///   • Props (cyber sofa, wreckage ridge) → .../Props.
///   • The horned creature → Resources/Enemies/HornedRavager, built like EnemyModelPrefabBuilder's enemies
///     (collider + CombatTarget + Enemy3D + SpawnedEnemy) so the Enemy Scatter Brush can place it.
/// The "Wasteland Monsters" lineup is several creatures welded into one mesh; it's split in Blender
/// first (Art/SeptemberDelivery/Monsters/) and built from there.
///
/// Materials are URP/Lit from Meshy's albedo + normal, with metallic/roughness packed into one
/// MetallicSmoothness map. Textures are clamped (1024 for walls/props, 2048 for creatures) — Meshy ships
/// 4–8K maps, far more than a wall seen from an overhead camera needs.
///
/// Idempotent: re-running rebuilds every prefab in place (same paths, so placed instances update).
/// Menu: Wasteland ▸ September Delivery ▸ Build Prefabs
/// </summary>
public static class SeptemberDeliveryBuilder
{
    public const string Source   = "Assets/Art/SeptemberDelivery";
    public const string Out      = "Assets/Worlds/BrokenCrescent/SeptemberDelivery";
    public const string WallsDir = Out + "/Walls";
    const string PropsDir = Out + "/Props";
    const string MatDir   = Out + "/Materials";
    const string EnemyDir = "Assets/Resources/Enemies";

    public enum Kind { Wall, Pillar, Prop, Enemy }

    struct Piece
    {
        public string folder, name, model, material; public Kind kind; public float height, yaw; public G gait;
        // folder = where the textures live; model = the mesh (defaults to folder/GameModel.fbx);
        // material = shared material name (defaults to the prefab name); yaw turns the art to face +Z;
        // gait = how a creature's CreatureMotion moves it (the models have no skeleton).
        public Piece(string folder, string name, Kind kind, float height, string model = null, string material = null, float yaw = 0f, G gait = G.Auto)
        { this.folder = folder; this.name = name; this.kind = kind; this.height = height; this.model = model; this.material = material; this.yaw = yaw; this.gait = gait; }
    }

    const string Monsters = "Assets/Art/SeptemberDelivery/Monsters/";   // split from the Meshy lineup in Blender
    static Piece Monster(int n, string name, float height, float yaw, G gait) =>
        new("Wasteland_Monsters_Mo", name, Kind.Enemy, height, $"{Monsters}Monster_{n:00}.fbx", "BC_WastelandMonsters", yaw, gait);

    // source folder prefix → prefab name, role, target height (m)
    static readonly Piece[] Pieces =
    {
        new("bone_wall_damaged",                "BC_Wall_Bone_Damaged",     Kind.Wall,   4.2f),
        new("bone_wall_module_2",               "BC_Wall_Bone_Module",      Kind.Wall,   4.8f),
        new("car_wall_corner",                  "BC_Wall_Car_Corner",       Kind.Wall,   3.6f),
        new("car_wall_damaged",                 "BC_Wall_Car_Damaged",      Kind.Wall,   3.0f),
        new("car_wall_module_1",                "BC_Wall_Car_ModuleA",      Kind.Wall,   3.6f),
        new("car_wall_module_2",                "BC_Wall_Car_ModuleB",      Kind.Wall,   3.6f),
        new("concrete_wall_corner",             "BC_Wall_Concrete_Corner",  Kind.Wall,   4.5f),
        new("concrete_wall_module__0925070049", "BC_Wall_Concrete_ModuleA", Kind.Wall,   4.5f),
        new("concrete_wall_module__0925070114", "BC_Wall_Concrete_ModuleB", Kind.Wall,   4.5f),
        new("concrete_wall_tall",               "BC_Wall_Concrete_Pillar",  Kind.Pillar, 8.0f),
        new("garbage_wall_corner",              "BC_Wall_Garbage_Corner",   Kind.Wall,   4.0f),
        new("garbage_wall_damaged",             "BC_Wall_Garbage_Damaged",  Kind.Wall,   4.0f),
        new("garbage_wall_module_1",            "BC_Wall_Garbage_Mound",    Kind.Pillar, 5.0f),   // a junk pyramid — accent, not a run piece
        new("garbage_wall_module_2",            "BC_Wall_Garbage_Module",   Kind.Wall,   3.5f),
        new("garbage_wall_tall",                "BC_Wall_Garbage_Spire",    Kind.Pillar, 8.0f),
        new("scrap_metal_wall_corn",            "BC_Wall_Scrap_Corner",     Kind.Wall,   4.5f),
        new("scrap_metal_wall_dama",            "BC_Wall_Scrap_Damaged",    Kind.Wall,   4.5f),
        new("scrap_metal_wall_tall",            "BC_Wall_Scrap_Tall",       Kind.Pillar, 6.5f),
        new("Cyber_Sofa",                       "BC_Prop_CyberSofa",        Kind.Prop,   1.1f),
        new("Wasteland_Wreckage",               "BC_Prop_WreckageRidge",    Kind.Prop,   2.5f),
        new("Generate_a_new_creatu",            "HornedRavager",            Kind.Enemy,  2.2f, gait: G.Quadruped),

        // The fifteen creatures from the "Wasteland Monsters" lineup (numbered as split). The yaw
        // turns each to face +Z — Meshy posed them every which way in the lineup.
        Monster( 1, "ThornTick",      1.2f, 45f, G.Crawler),
        Monster( 2, "RibcageHound",   1.4f, 90f, G.Quadruped),
        Monster( 3, "CarrionVulture", 1.5f, 90f, G.Hopper),
        Monster( 4, "ClubGhoul",      2.0f, 0f,  G.Biped),
        Monster( 5, "ScrapSentry",    2.0f, 90f, G.Tracked),
        Monster( 6, "AshWraith",      2.3f, 0f,  G.Floater),
        Monster( 7, "IronbackHound",  1.5f, 90f, G.Quadruped),
        Monster( 8, "Husk",           1.9f, 0f,  G.Biped),
        Monster( 9, "ScrapBrute",     2.6f, 0f,  G.Heavy),
        Monster(10, "BileToad",       2.2f, 90f, G.Blob),
        Monster(11, "ScrapMech",      3.4f, 0f,  G.Heavy),
        Monster(12, "MawCrawler",     1.8f, 50f, G.Crawler),
        Monster(13, "ScrapDevil",     3.0f, 0f,  G.Vortex),
        Monster(14, "BoneShambler",   1.9f, 0f,  G.Biped),
        Monster(15, "DuneSniper",     1.9f, 60f, G.Shooter),
    };

    [MenuItem("Wasteland/September Delivery/Build Prefabs")]
    public static void BuildMenu() => Debug.Log(BuildAll());

    /// <summary>Builds every piece, or just the named prefabs when any are given.</summary>
    public static string BuildAll(params string[] only)
    {
        foreach (var d in new[] { Out, WallsDir, PropsDir, MatDir, EnemyDir }) EnsureFolder(d);
        var log = new StringBuilder();
        foreach (var p in Pieces)
        {
            if (only.Length > 0 && !only.Contains(p.name)) continue;
            string folder = Directory.GetDirectories(Source)
                .Select(d => d.Replace('\\', '/'))
                .FirstOrDefault(d => Path.GetFileName(d).StartsWith(p.folder));
            if (folder == null) { log.AppendLine("MISSING folder " + p.folder); continue; }
            log.AppendLine(Build(p, folder));
        }
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    static string Build(Piece p, string folder)
    {
        string fbx = p.model ?? folder + "/GameModel.fbx";
        ConfigureImports(folder, p.kind == Kind.Enemy ? 2048 : 1024);
        if (p.model != null) ConfigureModel(p.model);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        if (model == null) return "NOT IMPORTED " + fbx;

        var mat = BuildMaterial(p.material ?? p.name, folder, p.kind == Kind.Enemy ? 2048 : 1024);

        var root = new GameObject(p.name);
        try
        {
            var vis = (GameObject)PrefabUtility.InstantiatePrefab(model);
            vis.name = "Visual";
            vis.transform.SetParent(root.transform, false);
            foreach (var c in vis.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in vis.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();

            // Walls run along their longest horizontal axis; make that local X so a placer can lay
            // them end to end by width. Creatures turn to face +Z (Enemy3D walks along forward).
            var b = Bounds(vis);
            if (p.kind == Kind.Wall && b.size.z > b.size.x * 1.15f)
            {
                vis.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                b = Bounds(vis);
            }
            if (p.yaw != 0f)
            {
                vis.transform.localRotation = Quaternion.Euler(0f, p.yaw, 0f) * vis.transform.localRotation;
                b = Bounds(vis);
            }

            // Scale to the target height, then put the pivot at the base centre.
            float s = p.height / Mathf.Max(0.001f, b.size.y);
            vis.transform.localScale *= s;
            b = Bounds(vis);
            vis.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
            b = Bounds(vis);

            string path;
            if (p.kind == Kind.Enemy)
            {
                var cc = root.AddComponent<CapsuleCollider>();
                float along = Mathf.Max(b.size.x, b.size.z);
                if (along > b.size.y * 1.1f)
                {
                    // Long, low body (hounds, crawlers): a capsule lying along it, as tall as the back.
                    cc.direction = b.size.z >= b.size.x ? 2 : 0;
                    cc.radius = Mathf.Clamp(b.size.y * 0.42f, 0.35f, 1.2f);
                    cc.height = Mathf.Max(along, cc.radius * 2f);
                    cc.center = new Vector3(0f, cc.radius, 0f);
                }
                else
                {
                    // Upright (ghouls, brutes, mechs): a standing capsule.
                    cc.direction = 1;
                    cc.radius = Mathf.Clamp(along * 0.3f, 0.3f, 1.1f);
                    cc.height = b.size.y;
                    cc.center = new Vector3(0f, b.size.y * 0.5f, 0f);
                }
                root.AddComponent<CombatTarget>();
                root.AddComponent<Enemy3D>();
                root.AddComponent<CreatureMotion>().gait = p.gait;   // the "fake rig": these models have no skeleton
                root.AddComponent<SpawnedEnemy>();
                path = $"{EnemyDir}/{p.name}.prefab";
            }
            else
            {
                var box = root.AddComponent<BoxCollider>();
                box.center = b.center; box.size = b.size;
                var lod = root.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(p.kind == Kind.Prop ? 0.02f : 0.012f, vis.GetComponentsInChildren<Renderer>()) });
                lod.RecalculateBounds();
                path = $"{(p.kind == Kind.Prop ? PropsDir : WallsDir)}/{p.name}.prefab";
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            long tris = vis.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null)
                           .Sum(f => (long)f.sharedMesh.triangles.Length / 3);
            return $"{p.name,-26} {p.kind,-6} {b.size.x:F1} x {b.size.y:F1} x {b.size.z:F1} m  {tris} tris  → {path}";
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ── imports ──────────────────────────────────────────────────────────
    static void ConfigureImports(string folder, int maxSize)
    {
        foreach (var file in Directory.GetFiles(folder).Select(f => f.Replace('\\', '/')))
        {
            var imp = AssetImporter.GetAtPath(file);
            if (imp is TextureImporter t)
            {
                bool normal = file.Contains("_normal");
                bool linear = file.Contains("_roughness") || file.Contains("_metallic");
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (t.maxTextureSize == maxSize && t.textureType == type && t.sRGBTexture == !linear && t.mipmapEnabled)
                    continue;
                t.maxTextureSize = maxSize; t.textureType = type; t.sRGBTexture = !linear;
                t.mipmapEnabled = true;
                t.textureCompression = TextureImporterCompression.Compressed;
                t.SaveAndReimport();
            }
            else if (imp is ModelImporter m)
            {
                if (!m.importAnimation && m.materialImportMode == ModelImporterMaterialImportMode.None) continue;
                m.importAnimation = false; m.importCameras = false; m.importLights = false;
                m.materialImportMode = ModelImporterMaterialImportMode.None;
                m.SaveAndReimport();
            }
        }
    }

    static void ConfigureModel(string path)
    {
        if (!(AssetImporter.GetAtPath(path) is ModelImporter m)) return;
        if (!m.importAnimation && m.materialImportMode == ModelImporterMaterialImportMode.None) return;
        m.importAnimation = false; m.importCameras = false; m.importLights = false;
        m.materialImportMode = ModelImporterMaterialImportMode.None;
        m.SaveAndReimport();
    }

    // ── material ─────────────────────────────────────────────────────────
    static Material BuildMaterial(string name, string folder, int size)
    {
        string path = $"{MatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(mat, path);
        }

        var pngs = Directory.GetFiles(folder, "*.png").Select(f => f.Replace('\\', '/')).ToArray();
        string albedo = pngs.FirstOrDefault(f => !f.Contains("_normal") && !f.Contains("_roughness") && !f.Contains("_metallic"));
        string normal = pngs.FirstOrDefault(f => f.Contains("_normal"));
        string metal  = pngs.FirstOrDefault(f => f.Contains("_metallic"));
        string rough  = pngs.FirstOrDefault(f => f.Contains("_roughness"));

        if (albedo != null) mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(albedo));
        mat.SetColor("_BaseColor", Color.white);
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
            mat.EnableKeyword("_NORMALMAP");
        }
        if (metal != null && rough != null)
        {
            string packed = $"{MatDir}/{name}_MetallicSmoothness.png";
            PackMetallicSmoothness(AssetDatabase.LoadAssetAtPath<Texture2D>(metal),
                                   AssetDatabase.LoadAssetAtPath<Texture2D>(rough), packed, size);
            mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(packed));
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Smoothness", 1f);      // scales the map's alpha
        }
        else
        {
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.25f);
        }
        mat.enableInstancing = true;              // hundreds of identical wall modules
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // URP wants metallic in R and smoothness (1 − roughness) in A.
    static void PackMetallicSmoothness(Texture2D metal, Texture2D rough, string path, int size)
    {
        int s = Mathf.Min(size, 1024);   // the detail in these maps is low-frequency
        var m = ReadAt(metal, s); var r = ReadAt(rough, s);
        var mp = m.GetPixels32(); var rp = r.GetPixels32();
        for (int i = 0; i < mp.Length; i++) mp[i] = new Color32(mp[i].r, 0, 0, (byte)(255 - rp[i].r));
        m.SetPixels32(mp); m.Apply();
        File.WriteAllBytes(path, m.EncodeToPNG());
        Object.DestroyImmediate(m); Object.DestroyImmediate(r);
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.sRGBTexture = false; ti.maxTextureSize = s; ti.mipmapEnabled = true;
        ti.SaveAndReimport();
    }

    static Texture2D ReadAt(Texture src, int size)
    {
        var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var prev = RenderTexture.active;
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        t.ReadPixels(new Rect(0, 0, size, size), 0, 0); t.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        return t;
    }

    // ── helpers ──────────────────────────────────────────────────────────
    public static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
