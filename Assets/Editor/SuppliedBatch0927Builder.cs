using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using Gait = CreatureMotion.Gait;

/// <summary>
/// Turns the owner's Sept 27 Meshy delivery into game content. Each Art/SuppliedBatch0927/&lt;id&gt; folder holds the
/// Meshy original, its PBR maps and GameModel.fbx (the same model decimated in Blender for phones: ~3k tris for
/// tools, ~6k for gauntlets, ~12k for creatures).
///   • Nine gathering-tool upgrades → Resources/HeldModels/Tools/&lt;SteelPickaxe…&gt;, icons ItemIcons/700–708.
///     Each model is turned to match its starter tool's measured frame (shaft, head side, reel), and its grip is
///     measured at the same point along the shaft, so the starter's hand placement carries over
///     (written to Scripts/Player3D/HeldModelPlacement.Generated.cs).
///   • Three reactor gauntlets above the starter pair → Resources/HeldModels/Weapons/&lt;Neon|Surge|Inferno&gt;Gauntlet
///     &lt;Right|Left&gt; (left = mirrored), icons ItemIcons/403, 404, 415.
///   • Five creatures take over the five enemies whose models were lost, under new names (the prefab keeps its
///     GUID), and three of each join their tier's hunting grounds with a neighbour's stats and drop table.
///   • The Wastelander → a talkable townsperson (WandererNPC) in the tier-1 settlement.
/// Existing icons are never overwritten — only the new items' missing icons are rendered.
/// Idempotent: re-running rebuilds everything in place. Menu: Wasteland ▸ Supplied Batch 0927 ▸ Build All.
/// </summary>
public static class SuppliedBatch0927Builder
{
    const string Root = "Assets/Art/SuppliedBatch0927";
    const string GeneratedPlacement = "Assets/Scripts/Player3D/HeldModelPlacement.Generated.cs";

    enum Kind { Pick, Hatchet, Rod }

    // invert: the silver rod's drooping line sits further off the rod than its reel, which fools the reel test.
    static readonly HashSet<string> Inverted = new() { "0927024552" };

    static readonly (string id, string prefab, int item, string baseModel, Kind kind, Color tint)[] Tools =
    {
        ("0927023247", "SteelPickaxe",       700, "HeldModels/pickaxe", Kind.Pick,    Color.white),
        ("0927023247", "AlloyPickaxe",       701, "HeldModels/pickaxe", Kind.Pick,    new Color(0.58f, 0.62f, 0.7f)),   // same head, gunmetal finish
        ("0927023230", "TitaniumPickaxe",    702, "HeldModels/pickaxe", Kind.Pick,    Color.white),
        ("0927023331", "SteelHatchet",       703, "HeldModels/hatchet", Kind.Hatchet, Color.white),
        ("0927023325", "AlloyHatchet",       704, "HeldModels/hatchet", Kind.Hatchet, Color.white),
        ("0927023321", "TitaniumHatchet",    705, "HeldModels/hatchet", Kind.Hatchet, Color.white),
        ("0927024425", "SteelFishingRod",    706, "HeldModels/fishing_rod/source/FishingRod", Kind.Rod, Color.white),
        ("0927024552", "AlloyFishingRod",    707, "HeldModels/fishing_rod/source/FishingRod", Kind.Rod, Color.white),
        ("0927025226", "TitaniumFishingRod", 708, "HeldModels/fishing_rod/source/FishingRod", Kind.Rod, Color.white),
    };

    // The Meshy gloves stand fingers-up (+Y) with the back of the hand to +Z; turned to the starter
    // gauntlets' frame (fingers +Z, back of hand +Y). Length = the longest side once in the hand.
    static readonly (string id, string model, int item, float length)[] Gauntlets =
    {
        ("0927025428", "NeonGauntlet",    FissionItems.NeonGauntlets,    0.30f),
        ("0927025436", "SurgeGauntlet",   FissionItems.SurgeGauntlets,   0.38f),
        ("0927025456", "InfernoGauntlet", FissionItems.InfernoGauntlets, 0.34f),
    };

    static readonly (string id, string oldPrefab, string prefab, string display, float height, Gait gait, int tier)[] Monsters =
    {
        ("0927024848", "FrogMarauder",   "ScrapGremlin",  "Scrap Gremlin",  1.1f, Gait.Hopper,    1),
        ("0927025148", "BigBoy",         "RustRover",     "Rust Rover",     1.3f, Gait.Quadruped, 2),
        ("0927025236", "Paperman",       "Shardback",     "Shardback",      1.5f, Gait.Quadruped, 3),
        ("0927024943", "Robert",         "ChromeStalker", "Chrome Stalker", 1.5f, Gait.Quadruped, 4),
        ("0927024938", "AndeanColossus", "RubbleTitan",   "Rubble Titan",   3.0f, Gait.Heavy,     5),
    };

    const string WastelanderId = "0927025111";

    [MenuItem("Wasteland/Supplied Batch 0927/Build All")]
    public static void BuildAllMenu() => Debug.Log(BuildHeldItems() + BuildCreatures());

    /// <summary>Tools + gauntlets, their icons and the generated hand placements.</summary>
    public static string BuildHeldItems()
    {
        var log = new StringBuilder();
        var profiles = new StringBuilder();
        foreach (var t in Tools) log.AppendLine(BuildTool(t, profiles));
        foreach (var g in Gauntlets) log.AppendLine(BuildGauntlet(g, profiles));
        WritePlacement(profiles);
        AssetDatabase.SaveAssets();
        HeldModels.ClearCache();
        AssetDatabase.Refresh();   // picks up the regenerated placement file
        return log.ToString();
    }

    /// <summary>The five creatures, their places in the hunting grounds, and the Wastelander (saves the world scene).</summary>
    public static string BuildCreatures()
    {
        var log = new StringBuilder();
        foreach (var m in Monsters) log.AppendLine(BuildMonster(m));
        AssetDatabase.SaveAssets();
        log.AppendLine(PlaceInWorld());
        return log.ToString();
    }

    // ── tools ────────────────────────────────────────────────────────────
    static string BuildTool((string id, string prefab, int item, string baseModel, Kind kind, Color tint) t, StringBuilder profiles)
    {
        string baseName = Path.GetFileName(t.baseModel);
        if (!HeldModelPlacement.TryGet(baseName, out var bp)) return t.prefab + ": no placement for " + baseName;
        var baseGo = Object.Instantiate(HeldModels.Load(t.baseModel));
        GameObject root = null;
        try
        {
            baseGo.transform.position = Vector3.zero;
            var bv = Verts(baseGo);
            var bFrame = Measure(bv, t.kind, bp.forward);
            var bb = BoundsOf(bv);
            float gripFrac = (Vector3.Dot(bb.center + Vector3.Scale(bb.size, bp.handle), bFrame.axis) - bFrame.lo) / bFrame.len;

            var mat = Surface(t.id, 1024);
            if (t.tint != Color.white) mat = Variant(mat, t.id, t.prefab, t.tint);
            root = new GameObject(t.prefab);
            var model = AddModel(root, t.id, mat);
            var nFrame = Measure(Verts(root), t.kind, null);
            if (Inverted.Contains(t.id)) nFrame = Measure(Verts(root), t.kind, -nFrame.axis);   // re-measure the reel side from the right end
            // On top of the import's own rotation (the FBX root carries one), which the measurement included.
            model.transform.localRotation = Quaternion.LookRotation(bFrame.axis, bFrame.side) * Quaternion.Inverse(Quaternion.LookRotation(nFrame.axis, nFrame.side))
                                          * model.transform.localRotation;
            model.transform.localPosition = -BoundsOf(Verts(root)).center;

            var rv = Verts(root);
            var rb = BoundsOf(rv);
            var f = Measure(rv, t.kind, bFrame.axis);
            Vector3 grip = ShaftPoint(rv, f, f.lo + gripFrac * f.len);
            Vector3 handle = new(Norm(grip.x - rb.center.x, rb.size.x), Norm(grip.y - rb.center.y, rb.size.y), Norm(grip.z - rb.center.z, rb.size.z));
            profiles.AppendLine($"            \"{t.prefab}\" => new Profile({V(handle)},{V(bp.forward)},{V(bp.up)},{F(bp.length)}f),");

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"Assets/Resources/HeldModels/Tools/{t.prefab}.prefab");
            string icon = Icon(prefab, t.item, Vector3.Cross(bFrame.axis, bFrame.side), bFrame.axis, 28f);
            return $"{t.prefab}: {Tris(root)} tris, grip at {gripFrac:0.00} of the shaft{icon}";
        }
        finally
        {
            Object.DestroyImmediate(baseGo);
            if (root != null) Object.DestroyImmediate(root);
        }
    }

    // ── gauntlets ────────────────────────────────────────────────────────
    static string BuildGauntlet((string id, string model, int item, float length) g, StringBuilder profiles)
    {
        var mat = Surface(g.id, 1024);
        Quaternion turn = Quaternion.Inverse(Quaternion.LookRotation(Vector3.up, Vector3.forward));   // fingers → +Z, back of hand → +Y
        GameObject right = null;
        string result = "";
        foreach (bool left in new[] { false, true })
        {
            var root = new GameObject(g.model + (left ? "Left" : "Right"));
            try
            {
                var holder = new GameObject("Mirror");
                holder.transform.SetParent(root.transform, false);
                var model = AddModel(holder, g.id, mat);
                model.transform.localRotation = turn * model.transform.localRotation;   // on top of the import's rotation
                model.transform.localPosition = -BoundsOf(Verts(holder)).center;
                if (left) holder.transform.localScale = new Vector3(-1f, 1f, 1f);   // the left glove: thumb side flips
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"Assets/Resources/HeldModels/Weapons/{root.name}.prefab");
                profiles.AppendLine($"            \"{root.name}\" => new Profile(new Vector3(0f,0f,-.08f),z,y,{F(g.length)}f),");
                if (!left) { right = prefab; result = $"{g.model}: {Tris(root)} tris"; }
            }
            finally { Object.DestroyImmediate(root); }
        }
        return result + Icon(right, g.item, Vector3.up, Vector3.forward, 12f);
    }

    static void WritePlacement(StringBuilder profiles)
    {
        string code =
"using UnityEngine;\n\n" +
"// GENERATED by SuppliedBatch0927Builder (Wasteland ▸ Supplied Batch 0927 ▸ Build All) — do not edit by hand.\n" +
"// Handle points for the Sept 27 tools and gauntlets, measured from each model's geometry after it was\n" +
"// turned to match its starter tool's frame.\n" +
"public static partial class HeldModelPlacement\n{\n" +
"    static Profile Generated(string modelName)\n    {\n" +
"        Vector3 x = Vector3.right, y = Vector3.up, z = Vector3.forward;\n" +
"        return modelName switch\n        {\n" +
profiles +
"            _ => default\n        };\n    }\n}\n";
        File.WriteAllText(GeneratedPlacement, code.Replace("\n", "\r\n"));
    }

    // ── creatures ────────────────────────────────────────────────────────
    static string BuildMonster((string id, string oldPrefab, string prefab, string display, float height, Gait gait, int tier) m)
    {
        string oldPath = $"Assets/Resources/Enemies/{m.oldPrefab}.prefab", newPath = $"Assets/Resources/Enemies/{m.prefab}.prefab";
        string path = File.Exists(newPath) ? newPath : oldPath;
        if (!File.Exists(path)) return m.prefab + ": no prefab to take over";
        var root = PrefabUtility.LoadPrefabContents(path);
        string size;
        try
        {
            for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            foreach (var a in root.GetComponents<Animator>()) Object.DestroyImmediate(a);   // no skeleton: CreatureMotion animates it
            var b = FitModel(root, m.id, Surface(m.id, 2048), m.height);
            var cc = root.GetComponent<CapsuleCollider>() ?? root.AddComponent<CapsuleCollider>();
            if (b.size.z > b.size.y && b.size.z > b.size.x)
            {   // long body: lie the capsule along it
                cc.direction = 2;
                cc.radius = Mathf.Min(b.size.x, b.size.y) * 0.45f;
                cc.height = Mathf.Max(b.size.z * 0.95f, cc.radius * 2f);
                cc.center = new Vector3(0f, b.size.y * 0.5f, b.center.z);
            }
            else
            {
                cc.direction = 1;
                cc.radius = Mathf.Max(b.size.x, b.size.z) * 0.35f;
                cc.height = Mathf.Max(b.size.y, cc.radius * 2f);
                cc.center = new Vector3(0f, b.size.y * 0.5f, 0f);
            }
            if (!root.GetComponent<CombatTarget>()) root.AddComponent<CombatTarget>();
            if (!root.GetComponent<Enemy3D>()) root.AddComponent<Enemy3D>();
            (root.GetComponent<CreatureMotion>() ?? root.AddComponent<CreatureMotion>()).gait = m.gait;
            if (!root.GetComponent<SpawnedEnemy>()) root.AddComponent<SpawnedEnemy>();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            size = b.size.ToString("F1");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        string renamed = path == newPath ? "" : AssetDatabase.RenameAsset(path, m.prefab);
        return $"{m.display}: took over {m.oldPrefab}, size {size}{(string.IsNullOrEmpty(renamed) ? "" : " RENAME FAILED " + renamed)}";
    }

    // ── placement in the world scene ─────────────────────────────────────
    static string PlaceInWorld()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var log = new StringBuilder();
        foreach (var m in Monsters)
        {
            var grounds = SceneObject($"Tier {m.tier} hunting grounds");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Resources/Enemies/{m.prefab}.prefab");
            if (grounds == null || prefab == null) { log.AppendLine($"{m.display}: no tier {m.tier} hunting grounds / prefab"); continue; }
            for (int i = grounds.childCount - 1; i >= 0; i--)
                if (grounds.GetChild(i).name == m.display) Object.DestroyImmediate(grounds.GetChild(i).gameObject);
            var anchors = Enumerable.Range(0, grounds.childCount).Select(grounds.GetChild)
                .Where(c => c.GetComponent<CombatTarget>() != null && c.GetComponent<Enemy3D>() != null).ToList();
            if (anchors.Count == 0) { log.AppendLine($"{m.display}: tier {m.tier} has no enemies to stand beside"); continue; }
            int placed = 0;
            for (int k = 0; k < 3; k++)
            {
                var a = anchors[(k * anchors.Count) / 3];
                Vector3 p = a.position + Quaternion.Euler(0f, 137f * (k + 1) + 31f * m.tier, 0f) * Vector3.forward * 7f;
                if (NavMesh.SamplePosition(p, out var hit, 6f, NavMesh.AllAreas)) p = hit.position;
                else if (NavMesh.SamplePosition(a.position, out hit, 3f, NavMesh.AllAreas)) p = hit.position;
                else p = a.position + Vector3.right * 2f;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, grounds);
                go.name = m.display;   // CombatTarget shows the object's name
                go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, (k * 97 + m.tier * 53) % 360, 0f));
                CopyStats(a.gameObject, go);
                placed++;
            }
            log.AppendLine($"{m.display}: {placed} placed in tier {m.tier} hunting grounds");
        }
        log.AppendLine(PlaceWastelander());
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return log.ToString();
    }

    /// <summary>The tier's own balance and loot: hit points, levels, damage, drop table and timings from the
    /// enemy it stands beside. Reach grows with the body so a big creature can still land a swing.</summary>
    static void CopyStats(GameObject from, GameObject to)
    {
        var a = from.GetComponent<CombatTarget>(); var b = to.GetComponent<CombatTarget>();
        b.maxHP = a.maxHP; b.attackLevel = a.attackLevel; b.defenceLevel = a.defenceLevel; b.maxDamage = a.maxDamage;
        b.isAggressive = a.isAggressive; b.dropTable = a.dropTable;
        var ea = from.GetComponent<Enemy3D>(); var eb = to.GetComponent<Enemy3D>();
        eb.aggroRange = ea.aggroRange; eb.moveSpeed = ea.moveSpeed; eb.attackCooldown = ea.attackCooldown;
        eb.respawnSeconds = ea.respawnSeconds; eb.windupSeconds = ea.windupSeconds; eb.deathLingerSeconds = ea.deathLingerSeconds;
        var cc = to.GetComponent<CapsuleCollider>();
        eb.attackRange = Mathf.Max(ea.attackRange, (cc != null ? cc.radius : 0.5f) + 1.2f);
        EditorUtility.SetDirty(b); EditorUtility.SetDirty(eb);
    }

    static string PlaceWastelander()
    {
        var root = new GameObject("Wastelander");
        GameObject prefab;
        try
        {
            var b = FitModel(root, WastelanderId, Surface(WastelanderId, 2048), 1.8f);
            var cc = root.AddComponent<CapsuleCollider>();
            cc.radius = 0.35f; cc.height = b.size.y; cc.center = new Vector3(0f, b.size.y * 0.5f, 0f);
            root.AddComponent<WandererNPC>();
            prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Wastelander.prefab");
        }
        finally { Object.DestroyImmediate(root); }

        var roxy = Object.FindAnyObjectByType<RoxyNPC>(FindObjectsInactive.Include);
        var town = SceneObject("BC_T1_Settlement");
        if (roxy == null || town == null) return "Wastelander: prefab built, but no Roxy / settlement to stand in";
        for (int i = town.childCount - 1; i >= 0; i--)
            if (town.GetChild(i).name == "Wastelander") Object.DestroyImmediate(town.GetChild(i).gameObject);
        var r = roxy.transform;
        Vector3 p = r.position + r.right * 5f + r.forward * 2f;
        if (NavMesh.SamplePosition(p, out var hit, 4f, NavMesh.AllAreas)) p = hit.position;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, town);
        Vector3 look = r.position - p; look.y = 0f;
        go.transform.SetPositionAndRotation(p, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity);
        return $"Wastelander: standing in the settlement at {p:F0}, {Vector3.Distance(p, r.position):0.0} m from Roxy";
    }

    static Transform SceneObject(string name)
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            if (t.name == name && t.gameObject.scene.IsValid()) return t;
        return null;
    }

    // ── model / material helpers ─────────────────────────────────────────
    static GameObject AddModel(GameObject parent, string id, Material mat)
    {
        string path = $"{Root}/{id}/GameModel.fbx";
        var imp = (ModelImporter)AssetImporter.GetAtPath(path);
        if (imp == null) throw new System.Exception("missing " + path);
        if (imp.importCameras || imp.importLights || imp.materialImportMode != ModelImporterMaterialImportMode.None ||
            imp.animationType != ModelImporterAnimationType.None || !imp.isReadable)
        {
            imp.importCameras = false; imp.importLights = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.animationType = ModelImporterAnimationType.None; imp.importAnimation = false;
            imp.isReadable = true;   // measured here; small meshes
            imp.SaveAndReimport();
        }
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        model.name = "Model";
        model.transform.SetParent(parent.transform, false);
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            r.sharedMaterials = Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
        return model;
    }

    /// <summary>Scales the model to a height, feet on the root's origin, centred — it keeps Meshy's facing (+Z).</summary>
    static Bounds FitModel(GameObject root, string id, Material mat, float height)
    {
        var model = AddModel(root, id, mat);
        var b = BoundsOf(Verts(root));
        model.transform.localScale *= height / Mathf.Max(0.001f, b.size.y);   // the import's own unit scale stays in
        b = BoundsOf(Verts(root));
        model.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
        return BoundsOf(Verts(root));
    }

    static Material Surface(string id, int maxSize)
    {
        string dir = $"{Root}/{id}";
        string stem = Directory.GetFiles(dir, "*_texture.png").Select(p => p.Replace('\\', '/')).First();
        stem = stem.Substring(0, stem.Length - 4);
        string matPath = dir + "/Surface.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }

        mat.SetTexture("_BaseMap", Texture(stem + ".png", maxSize, TextureImporterType.Default, true));
        mat.SetColor("_BaseColor", Color.white);
        var normal = Texture(stem + "_normal.png", maxSize, TextureImporterType.NormalMap, false);
        mat.SetTexture("_BumpMap", normal);
        if (normal != null) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");

        string packed = dir + "/MetallicSmoothness.png";
        if (!File.Exists(packed) && File.Exists(stem + "_metallic.png") && File.Exists(stem + "_roughness.png"))
        {
            var metal = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            var rough = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                metal.LoadImage(File.ReadAllBytes(stem + "_metallic.png"));
                rough.LoadImage(File.ReadAllBytes(stem + "_roughness.png"));
                if (metal.width == rough.width && metal.height == rough.height)
                {
                    var mp = metal.GetPixels32(); var rp = rough.GetPixels32();
                    for (int i = 0; i < mp.Length; i++) mp[i] = new Color32(mp[i].r, 0, 0, (byte)(255 - rp[i].r));
                    metal.SetPixels32(mp); metal.Apply();
                    File.WriteAllBytes(packed, metal.EncodeToPNG());
                    AssetDatabase.ImportAsset(packed);
                }
            }
            finally { Object.DestroyImmediate(metal); Object.DestroyImmediate(rough); }
        }
        var ms = File.Exists(packed) ? Texture(packed, maxSize, TextureImporterType.Default, false) : null;
        mat.SetTexture("_MetallicGlossMap", ms);
        if (ms != null) { mat.EnableKeyword("_METALLICSPECGLOSSMAP"); mat.SetFloat("_Smoothness", 1f); }
        else { mat.DisableKeyword("_METALLICSPECGLOSSMAP"); mat.SetFloat("_Metallic", 0.15f); mat.SetFloat("_Smoothness", 0.3f); }

        var emission = Texture(stem + "_emission.png", maxSize, TextureImporterType.Default, true);
        mat.SetTexture("_EmissionMap", emission);
        if (emission != null) { mat.SetColor("_EmissionColor", Color.white); mat.EnableKeyword("_EMISSION"); mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        else { mat.SetColor("_EmissionColor", Color.black); mat.DisableKeyword("_EMISSION"); }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material Variant(Material source, string id, string name, Color tint)
    {
        string path = $"{Root}/{id}/Surface_{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(source); AssetDatabase.CreateAsset(mat, path); }
        else mat.CopyPropertiesFromMaterial(source);
        mat.SetColor("_BaseColor", tint);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Texture2D Texture(string path, int maxSize, TextureImporterType type, bool srgb)
    {
        if (!File.Exists(path)) return null;
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        if (imp == null) return null;
        if (imp.maxTextureSize != maxSize || imp.textureType != type || imp.sRGBTexture != srgb)
        {
            imp.maxTextureSize = maxSize; imp.textureType = type; imp.sRGBTexture = srgb;
            imp.textureCompression = TextureImporterCompression.Compressed;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ── icons ────────────────────────────────────────────────────────────
    /// <summary>Renders an icon for an item that has none. Never replaces existing icon art.</summary>
    static string Icon(GameObject prefab, int itemId, Vector3 viewDir, Vector3 up, float roll)
    {
        string path = $"Assets/Resources/ItemIcons/{itemId}.png";
        string ledger = Root + "/rendered_icons.txt";   // icons this builder made (and may re-render)
        var ours = File.Exists(ledger) ? File.ReadAllLines(ledger).ToList() : new List<string>();
        if (File.Exists(path) && !ours.Contains(itemId.ToString())) return ", kept existing icon";
        var pru = new PreviewRenderUtility();
        try
        {
            pru.camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            var go = (GameObject)Object.Instantiate(prefab);
            pru.AddSingleGO(go);
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float radius = b.extents.magnitude;
            var cam = pru.camera;
            cam.orthographic = true;
            cam.transform.position = b.center + viewDir.normalized * radius * 4f;
            cam.transform.LookAt(b.center, Quaternion.AngleAxis(roll, viewDir.normalized) * up);
            cam.nearClipPlane = 0.01f; cam.farClipPlane = radius * 10f;
            // Frame the model's actual outline as the camera sees it (no cropped heads), with a small margin.
            var pts = Verts(go);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in pts)
            {
                var q = cam.transform.InverseTransformPoint(p);
                minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x); minY = Mathf.Min(minY, q.y); maxY = Mathf.Max(maxY, q.y);
            }
            cam.transform.position += cam.transform.right * ((minX + maxX) * 0.5f) + cam.transform.up * ((minY + maxY) * 0.5f);
            cam.orthographicSize = Mathf.Max(maxX - minX, maxY - minY) * 0.5f * 1.08f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            var f = cam.transform.forward; var u = cam.transform.up; var ri = cam.transform.right;
            pru.lights[0].intensity = 1.35f; pru.lights[0].transform.rotation = Quaternion.LookRotation(f - u * 0.7f - ri * 0.5f);
            pru.lights[1].intensity = 0.75f; pru.lights[1].transform.rotation = Quaternion.LookRotation(f + u * 0.3f + ri * 0.8f);
            pru.ambientColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            // URP's preview drops the clear colour's alpha, so render over black and over white and take the
            // coverage from the difference: the background shows through exactly where the model doesn't.
            Color[] Shot(Color bg)
            {
                cam.backgroundColor = bg;
                pru.BeginStaticPreview(new Rect(0, 0, 512, 512));
                pru.Render(true);
                var shot = pru.EndStaticPreview();
                var px = shot.GetPixels();
                Object.DestroyImmediate(shot);
                return px;
            }
            var onBlack = Shot(Color.black); var onWhite = Shot(Color.white);
            var outPx = new Color[onBlack.Length];
            for (int i = 0; i < outPx.Length; i++)
            {
                Color k = onBlack[i], w = onWhite[i];
                float a = 1f - Mathf.Clamp01(Mathf.Max(w.r - k.r, Mathf.Max(w.g - k.g, w.b - k.b)));
                outPx[i] = a < 0.004f ? Color.clear : new Color(Mathf.Clamp01(k.r / a), Mathf.Clamp01(k.g / a), Mathf.Clamp01(k.b / a), a);
            }
            var tex = new Texture2D(512, 512, TextureFormat.RGBA32, false);
            tex.SetPixels(outPx); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            if (!ours.Contains(itemId.ToString())) { ours.Add(itemId.ToString()); File.WriteAllLines(ledger, ours); }
            Object.DestroyImmediate(tex);
        }
        finally { pru.Cleanup(); }
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.maxTextureSize = 512;
        imp.SaveAndReimport();
        return ", icon " + itemId;
    }

    // ── geometry ─────────────────────────────────────────────────────────
    struct Frame { public Vector3 axis, side, c; public float lo, len; }

    /// <summary>The shaft (principal axis, pointing at the head / rod tip), and the side the head's blade or the
    /// rod's reel sits on. A forced axis only fixes the direction (for the starter models, whose placements
    /// already say which way is forward).</summary>
    /// <summary>Positions along the shaft are absolute projections on the axis (lo..lo+len, butt to head/tip);
    /// c is a point on the shaft line, so offsets from it are offsets from the handle.</summary>
    static Frame Measure(List<Vector3> v, Kind kind, Vector3? forced)
    {
        Vector3 axis = forced.HasValue ? forced.Value.normalized : LongestDirection(v);
        (float lo, float hi) Range() { float a = float.MaxValue, b = float.MinValue; foreach (var p in v) { float t = Vector3.Dot(p, axis); if (t < a) a = t; if (t > b) b = t; } return (a, b); }
        Vector3 Centroid(float from, float to) { Vector3 s = Vector3.zero; int n = 0; foreach (var p in v) { float t = Vector3.Dot(p, axis); if (t >= from && t <= to) { s += p; n++; } } return n > 0 ? s / n : new Vector3(float.NaN, 0, 0); }
        float Spread(float from, float to) { var cc = Centroid(from, to); if (float.IsNaN(cc.x)) return 0f; double s = 0; int n = 0; foreach (var p in v) { float t = Vector3.Dot(p, axis); if (t >= from && t <= to) { s += (p - cc).magnitude; n++; } } return n > 0 ? (float)(s / n) : 0f; }
        List<Vector3> Centroids(float lo0, float len0, float a0, float a1) { var l = new List<Vector3>(); for (float fr = a0; fr <= a1 + 1e-4f; fr += 0.05f) { var cc = Centroid(lo0 + (fr - 0.03f) * len0, lo0 + (fr + 0.03f) * len0); if (!float.IsNaN(cc.x)) l.Add(cc); } return l; }

        var (lo, hi) = Range(); float len = hi - lo;
        if (!forced.HasValue && kind == Kind.Rod)
        {
            // A rod's own length is its longest line. The reel — the geometry furthest off that line — sits by
            // the handle, so the tip is the far end from it (the line guides make end-thickness unreliable).
            Vector3 g = Mean(v);
            var off = v.Select(p => { var d = p - g; return (r: (d - axis * Vector3.Dot(d, axis)).magnitude, t: Vector3.Dot(p, axis)); })
                       .OrderByDescending(x => x.r).Take(Mathf.Max(8, v.Count / 30)).ToList();
            float reelAt = off.Average(x => x.t);
            if (reelAt - lo > hi - reelAt) { axis = -axis; (lo, hi) = Range(); }
        }
        else if (!forced.HasValue)
        {
            // The longest line through a pick or hatchet runs butt → far corner of the head, so: find which end is
            // the butt, then fit the shaft through slice centres along the handle (rods: along the thin upper rod).
            float top = Spread(hi - 0.25f * len, hi), bottom = Spread(lo, lo + 0.25f * len);
            if (top < bottom) { axis = -axis; (lo, hi) = Range(); }   // point at the heavy head
            var cents = Centroids(lo, len, 0.05f, 0.55f);
            if (cents.Count >= 3)
            {
                var m = Mean(cents);
                var dir = Principal(cents.Select(p => p - m));
                if (Vector3.Dot(dir, axis) < 0f) dir = -dir;
                axis = dir; (lo, hi) = Range(); len = hi - lo;
            }
        }
        var line = Centroids(lo, len, kind == Kind.Rod ? 0.45f : 0.1f, kind == Kind.Rod ? 0.9f : 0.45f);
        Vector3 c = line.Count > 0 ? Mean(line) : Mean(v);
        IEnumerable<Vector3> Perp(float from, float to) { foreach (var p in v) { float t = Vector3.Dot(p, axis); if (t >= from && t <= to) { var d = p - c; yield return d - axis * Vector3.Dot(d, axis); } } }
        Vector3 side = kind switch
        {
            Kind.Pick => Principal(Perp(hi - 0.3f * len, hi)),                    // the head's spread
            Kind.Hatchet => Mean(Perp(hi - 0.3f * len, hi)),                      // toward the blade
            _ => Mean(Perp(lo + 0.08f * len, lo + 0.45f * len)),                 // toward the reel
        };
        side = Vector3.ProjectOnPlane(side, axis);
        if (side.sqrMagnitude < 1e-8f) side = Vector3.ProjectOnPlane(Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right, axis);
        return new Frame { axis = axis, side = side.normalized, c = c, lo = lo, len = len };
    }

    /// <summary>The direction of the object's greatest extent (sampled over a hemisphere) — unlike a
    /// principal axis, it isn't pulled toward wherever the mesh happens to be most detailed.</summary>
    static Vector3 LongestDirection(List<Vector3> v)
    {
        Vector3 best = Vector3.up; float bestExtent = -1f;
        const int N = 2000;
        for (int i = 0; i < N; i++)
        {
            float y = 1f - (i + 0.5f) / N, r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y)), phi = i * 2.3999632f;
            var d = new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in v) { float t = Vector3.Dot(p, d); if (t < lo) lo = t; if (t > hi) hi = t; }
            if (hi - lo > bestExtent) { bestExtent = hi - lo; best = d; }
        }
        return best;
    }

    /// <summary>Centre of the shaft at a distance along it: the mean of the slab's points nearest the shaft line
    /// (so a reel or blade in the slab doesn't drag the grip off the handle).</summary>
    static Vector3 ShaftPoint(List<Vector3> v, Frame f, float at)
    {
        var slab = new List<(float r, Vector3 p)>();
        foreach (var p in v)
        {
            if (Mathf.Abs(Vector3.Dot(p, f.axis) - at) >= 0.04f * f.len) continue;
            var d = p - f.c;
            slab.Add(((d - f.axis * Vector3.Dot(d, f.axis)).magnitude, p));
        }
        if (slab.Count == 0) return f.c + f.axis * (at - Vector3.Dot(f.c, f.axis));
        slab.Sort((a, b) => a.r.CompareTo(b.r));
        var near = slab.Take(Mathf.Max(1, slab.Count / 2)).ToList();
        Vector3 s = Vector3.zero; foreach (var n in near) s += n.p;
        return s / near.Count;
    }

    static Vector3 Principal(IEnumerable<Vector3> pts)
    {
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var p in pts) { xx += p.x * p.x; xy += p.x * p.y; xz += p.x * p.z; yy += p.y * p.y; yz += p.y * p.z; zz += p.z * p.z; }
        var v = new Vector3(0.577f, 0.577f, 0.577f);
        for (int i = 0; i < 80; i++)
        {
            var n = new Vector3((float)(xx * v.x + xy * v.y + xz * v.z), (float)(xy * v.x + yy * v.y + yz * v.z), (float)(xz * v.x + yz * v.y + zz * v.z));
            if (n.sqrMagnitude < 1e-20f) break;
            v = n.normalized;
        }
        return v;
    }

    static Vector3 Mean(IEnumerable<Vector3> pts) { Vector3 s = Vector3.zero; int n = 0; foreach (var p in pts) { s += p; n++; } return n > 0 ? s / n : Vector3.zero; }

    /// <summary>Every mesh vertex in world space (the object is measured at the origin, like the held-item wrapper).</summary>
    static List<Vector3> Verts(GameObject go)
    {
        var list = new List<Vector3>();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) Add(list, mf.sharedMesh, mf.transform.localToWorldMatrix);
        foreach (var sr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (sr.sharedMesh != null) Add(list, sr.sharedMesh, sr.transform.localToWorldMatrix);
        return list;
    }

    // Read-only mesh data works on any imported mesh in the editor, Read/Write enabled or not.
    static void Add(List<Vector3> list, Mesh mesh, Matrix4x4 m)
    {
        using var data = Mesh.AcquireReadOnlyMeshData(mesh);
        var verts = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp);
        data[0].GetVertices(verts);
        foreach (var p in verts) list.Add(m.MultiplyPoint3x4(p));
        verts.Dispose();
    }

    static Bounds BoundsOf(List<Vector3> v) { var b = new Bounds(v[0], Vector3.zero); foreach (var p in v) b.Encapsulate(p); return b; }
    static int Tris(GameObject go) => go.GetComponentsInChildren<MeshFilter>(true).Sum(m => m.sharedMesh != null ? (int)m.sharedMesh.GetIndexCount(0) / 3 : 0);
    static float Norm(float d, float size) => size > 1e-6f ? Mathf.Clamp(d / size, -0.5f, 0.5f) : 0f;
    static string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"new Vector3({F(v.x)}f,{F(v.y)}f,{F(v.z)}f)";
}
