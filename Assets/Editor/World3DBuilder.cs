using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click 3D world. Builds a playable "MainWorld3D" scene:
///   • ground, lighting, MMO camera
///   • 3D player (WASD/controller movement, dodge, action combat, E-to-interact)
///   • training dummy + a hostile enemy
///   • working resource nodes, furnace, bank crate (3D versions of the island systems)
///   • props auto-discovered from your imported asset packs and scattered around
///
/// Menu:  Wasteland ▸ Build 3D World (New Scene)
/// </summary>
public static class World3DBuilder
{
    const string ScenePath = "Assets/Scenes/MainWorld3D.unity";
    const string MatFolder = "Assets/Art/Generated3D";

    /// <summary>If a scene already exists at `path`, prompts: save a NEW copy (default — protects
    /// hand-placed work), Overwrite, or Cancel (returns null). Used by the scene builders.</summary>
    public static string SafeScenePath(string path)
    {
        if (!System.IO.File.Exists(path)) return path;
        int choice = EditorUtility.DisplayDialogComplex("Scene already exists",
            $"\"{path}\" already exists.\n\nSave a NEW copy so your placed work isn't lost, or overwrite it?",
            "Save NEW copy", "Cancel", "Overwrite");
        if (choice == 1) return null;     // Cancel
        if (choice == 2) return path;     // Overwrite
        string dir  = System.IO.Path.GetDirectoryName(path);
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        string ext  = System.IO.Path.GetExtension(path);
        int i = 1; string p;
        do { p = $"{dir}/{name}_{i++}{ext}"; } while (System.IO.File.Exists(p));
        return p;
    }

    [MenuItem("Wasteland/Archived/Build 3D World (New Scene)", false, 9000)]
    public static void Build()
    {
        // Don't silently wipe an existing MainWorld3D — prompt to save a new copy / cancel / overwrite,
        // the same guard the tutorial builder uses. (This menu regenerates the world from scratch.)
        string scenePath = SafeScenePath(ScenePath);
        if (scenePath == null) return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── light & ambience ─────────────────────────────────────────────
        var lightGo = new GameObject("Directional Light", typeof(Light));
        var light = lightGo.GetComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.15f;
        light.color = new Color(1f, 0.93f, 0.82f);   // dusty late-afternoon
        lightGo.transform.rotation = Quaternion.Euler(50f, -32f, 0f);
        RenderSettings.ambientLight = new Color(0.45f, 0.43f, 0.40f);

        // ── ground ───────────────────────────────────────────────────────
        string terrainDataPath = "Assets/Art/Generated3D/MainWorldData.asset";
        TerrainData terrainData = new TerrainData();
        terrainData.heightmapResolution = 513;
        terrainData.size = new Vector3(600, 100, 600); // 600m x 600m area (EDGE*2), 100m max height
        
        // Ensure the folder exists and save the data so it doesn't disappear
        if (AssetDatabase.LoadAssetAtPath<TerrainData>(terrainDataPath) != null) AssetDatabase.DeleteAsset(terrainDataPath);
        if (!AssetDatabase.IsValidFolder("Assets/Art/Generated3D")) AssetDatabase.CreateFolder("Assets/Art", "Generated3D");
        AssetDatabase.CreateAsset(terrainData, terrainDataPath);

        var groundGo = Terrain.CreateTerrainGameObject(terrainData);
        groundGo.name = "Ground";
        groundGo.transform.position = new Vector3(-300, 0, -300); // Center the 600x600 terrain

        // Auto-sculpt some initial mountains
        TerrainHeightSculptor.Generate(terrainData);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ── managers / player / camera ───────────────────────────────────
        BuildManagers();
        // Helper: get terrain surface Y at an XZ position
        float SY(float x, float z) => groundGo != null && groundGo.GetComponent<Terrain>() != null
            ? groundGo.GetComponent<Terrain>().SampleHeight(new Vector3(x, 0, z))
            : 0f;
        var player = BuildPlayerRig(new Vector3(0f, SY(0,0) + 0.1f, 0f));
        BuildCamera(player.transform);

        // ── training dummy + hostile ─────────────────────────────────────
        var dummy = Capsule("Training Dummy", new Vector3(4f, SY(4,5), 5f), new Color(0.75f, 0.62f, 0.4f));
        var dct = dummy.AddComponent<CombatTarget>();
        dct.isDummy = true; dct.maxHP = 30;
        dummy.AddComponent<RespawnOnDeath>();

        var hostile = Capsule("Wasteland Hostile", new Vector3(-12f, SY(-12,14), 14f), new Color(0.55f, 0.2f, 0.18f));
        var hct = hostile.AddComponent<CombatTarget>();
        hct.maxHP = 15; hct.attackLevel = 4; hct.defenceLevel = 2; hct.maxDamage = 2; hct.isAggressive = true;
        hostile.AddComponent<Enemy3D>();

        // ── resource nodes (3D versions of the island nodes) ─────────────
        MakeNode("Rubble Pile",  new Vector3(-6f, SY(-6,4), 4f), ResourceNodeType.RubblePile,   Skill.Scrapping, 3, 10,
                 PrimitiveType.Cube,     new Color(0.5f, 0.5f, 0.52f), new[] { "rock", "rubble", "debris" });
        MakeNode("Wood Debris",  new Vector3(7f, SY(7,-3), -3f), ResourceNodeType.WoodenDebris, Skill.Woodcutting,  4, 40,
                 PrimitiveType.Cylinder, new Color(0.45f, 0.3f, 0.15f), new[] { "stump", "log", "tree" });
        MakeNode("Fishing Spot", new Vector3(-3f, SY(-3,-8), -8f), ResourceNodeType.WaterBarrel, Skill.Fishing, 5, 22,
                 PrimitiveType.Cylinder, new Color(0.2f, 0.4f, 0.5f),  new[] { "barrel", "pond", "water" });

        // ── furnace + bank ───────────────────────────────────────────────
        var furnaceGo = Box("Furnace", new Vector3(10f, SY(10,6), 6f), new Vector3(1.4f, 1.6f, 1.4f),
                            new Color(0.35f, 0.22f, 0.18f), new[] { "furnace", "forge", "oven" });
        var st = furnaceGo.AddComponent<CraftingStation>();
        st.stationType = StationType.Furnace;
        st.recipes = CraftingRecipes.Furnace();

        // Workbench — assembles firearms, melee weapons, and armor (recipe mirrors the 2D island).
        var benchGo = Box("Workbench", new Vector3(8f, SY(8,8), 8f), new Vector3(1.6f, 1.1f, 0.9f),
                          new Color(0.30f, 0.30f, 0.34f), new[] { "bench", "workbench", "table", "anvil" });
        var gst = benchGo.AddComponent<CraftingStation>();
        gst.stationType = StationType.Workbench;
        gst.recipes = CraftingRecipes.Workbench();

        // Cooking fire — turns raw catches into food (trains Sustenance).
        var cookGo = Box("Cooking Fire", new Vector3(13f, SY(13,6), 6f), new Vector3(1.2f, 0.8f, 1.2f),
                         new Color(0.5f, 0.25f, 0.12f), new[] { "fire", "campfire", "firepit", "bonfire" });
        var cst = cookGo.AddComponent<CraftingStation>();
        cst.stationType = StationType.CookingFire;
        cst.recipes = CraftingRecipes.Cooking();

        var bankGo = Box("Bank Crate", new Vector3(12f, SY(12,2), 2f), new Vector3(1.2f, 1f, 1.2f),
                         new Color(0.3f, 0.34f, 0.2f), new[] { "crate", "chest", "container" });
        bankGo.AddComponent<BankingCrate>();

        // Tier 2-5 gather nodes use the Meshy-model prefabs from "Wasteland > World > Create Tiered
        // Node Prefabs" / "Create Fishing Spot Prefabs", hand-painted with the World Scatter Brush.

        // ── scatter props from your imported packs ───────────────────────
        int placed = ScatterProps();

        // ── save + register ──────────────────────────────────────────────
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings(ScenePath);
        AddToBuildSettings("Assets/Scenes/TutorialIsland.unity");

        EditorUtility.DisplayDialog("3D World Built",
            $"MainWorld3D created and saved ({placed} props scattered from your asset packs).\n\n" +
            "CONTROLS\n" +
            "  Move: WASD / left stick\n" +
            "  Camera: hold Right Mouse + drag, scroll to zoom (auto-follows on pad)\n" +
            "  Attack: Left Mouse / X button\n" +
            "  Dodge: Space / B button (i-frames!)\n" +
            "  Aim (ranged): hold Left Shift / LB\n" +
            "  Interact: E / A button near nodes, furnace, bank\n\n" +
            "Press Play here, or F12 from the island to warp in.",
            "Let's go");
    }

    // ── shared rig builders (used by TutorialIsland3DBuilder too) ────────
    internal static GameObject BuildManagers()
    {
        var managers = new GameObject("Managers");
        managers.AddComponent<GameTick>();
        managers.AddComponent<CombatManager>();
        managers.AddComponent<SkillingManager>();
        managers.AddComponent<CraftingManager>();
        return managers;
    }

    internal static GameObject BuildPlayerRig(Vector3 pos)
    {
        var player = new GameObject("Player");
        player.transform.position = pos;
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.center = new Vector3(0f, 0.95f, 0f); cc.radius = 0.35f;
        player.AddComponent<PlayerEntity>();                       // auto-adds PlayerStats + PlayerRespawn
        player.AddComponent<Player3DController>();
        player.AddComponent<ActionCombat3D>();
        player.AddComponent<ClickToMove3D>();
        player.AddComponent<Interactor3D>();
        player.AddComponent<World3DBootstrap>();
        AttachPlayerVisual(player.transform);
        return player;
    }

    internal static GameObject BuildCamera(Transform target)
    {
        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(OrbitCamera3D));
        camGo.tag = "MainCamera";
        camGo.GetComponent<OrbitCamera3D>().target = target;
        camGo.transform.position = target.position + new Vector3(0f, 6f, -8f);
        return camGo;
    }

    // ── helpers ──────────────────────────────────────────────────────────
    internal static GameObject Capsule(string name, Vector3 pos, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.transform.position = pos + Vector3.up * 1f;
        go.GetComponent<Renderer>().sharedMaterial = MakeMat(name.Replace(" ", ""), color);
        return go;
    }

    internal static GameObject Box(string name, Vector3 pos, Vector3 size, Color color, string[] prefabKeys)
    {
        var pretty = FindPackPrefab(prefabKeys);
        GameObject go;
        if (pretty != null)
        {
            go = (GameObject)PrefabUtility.InstantiatePrefab(pretty);
            go.name = name;
            go.transform.position = pos;
            Neutralize(go);
            NormalizeHeight(go, size.y + 0.4f);
            if (go.GetComponentInChildren<Collider>() == null)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.size = size; bc.center = Vector3.up * (size.y / 2f);
            }
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos + Vector3.up * (size.y / 2f);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = MakeMat(name.Replace(" ", ""), color);
        }
        foreach (var c in go.GetComponentsInChildren<Collider>()) c.isTrigger = false;
        return go;
    }

    internal static void MakeNode(string name, Vector3 pos, ResourceNodeType type, Skill skill, int toolId, int dropId,
                         PrimitiveType fallbackShape, Color color, string[] prefabKeys)
    {
        var pretty = FindPackPrefab(prefabKeys);
        GameObject go;
        if (pretty != null)
        {
            go = (GameObject)PrefabUtility.InstantiatePrefab(pretty);
            go.transform.position = pos;
            Neutralize(go);
            NormalizeHeight(go, 1.4f);
        }
        else
        {
            go = GameObject.CreatePrimitive(fallbackShape);
            go.transform.position = pos + Vector3.up * 0.5f;
            go.GetComponent<Renderer>().sharedMaterial = MakeMat(name.Replace(" ", ""), color);
        }
        go.name = name;
        if (go.GetComponentInChildren<Collider>() == null) go.AddComponent<SphereCollider>();

        var node = go.AddComponent<ResourceNode>();
        node.nodeType = type; node.skill = skill;
        node.requiredToolId = toolId; node.dropItemId = dropId;
        node.xpPerAction = 25; node.maxHits = 3; node.respawnTicks = 10;
    }

    internal static void AttachPlayerVisual(Transform parent)
    {
        // The animation driver reads movement/combat/skilling and feeds the Animator.
        if (parent.GetComponent<PlayerAnimator3D>() == null) parent.gameObject.AddComponent<PlayerAnimator3D>();
        // Shows equipped weapons/tools/shields in the soldier's hands.
        if (parent.GetComponent<EquipmentVisuals>() == null) parent.gameObject.AddComponent<EquipmentVisuals>();

        // Player character model — picked by gender from character select.
        // At editor-build time this reads the last-saved PlayerPrefs (defaults Male).
        // At runtime, the CharacterSelect scene sets this before loading MainWorld3D,
        // so the next cold-start will have the right model. For runtime gender changes,
        // PlayerEntity.Start() can trigger a visual rebuild.
        string gender = PlayerPrefs.GetString("PlayerGender", "Male");
        string charModelPath = gender == "Female"
            ? "Assets/Art/newshit/Superhero_Female_FullBody.fbx"
            : "Assets/Art/newshit/Superhero_Male_FullBody.fbx";

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(charModelPath)
                 ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PlayerModel/armored-survival-soldier.fbx")
                 ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/player.fbx");

        if (model == null)   // last resort: any humanoid in the imported packs
            model = FindPackPrefab(new[] { "soldier", "survivor", "character", "human", "male" }, requireSkinnedMesh: true);

        if (model != null)
        {
            var vis = (GameObject)PrefabUtility.InstantiatePrefab(model);
            vis.name = "Visual";
            vis.transform.SetParent(parent, false);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localRotation = Quaternion.identity;

            // The soldier FBX ships with a junk Camera + Light baked in — they hijack
            // rendering (the recurring "wrong camera") and add stray lights. Strip them.
            foreach (var cam in vis.GetComponentsInChildren<Camera>(true))
                if (cam != null) Object.DestroyImmediate(cam.gameObject);
            foreach (var lt in vis.GetComponentsInChildren<Light>(true))
                if (lt != null) Object.DestroyImmediate(lt.gameObject);
            foreach (var al in vis.GetComponentsInChildren<AudioListener>(true))
                if (al != null) Object.DestroyImmediate(al);

            // Normalize to ~1.8 m tall no matter the FBX's native units (Mixamo imports in cm,
            // i.e. ~100x — the shared NormalizeHeight's safety clamp would skip that and leave it
            // giant, so the camera ends up *inside* the mesh and it looks invisible).
            var rends = vis.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                if (b.size.y > 1e-4f) vis.transform.localScale *= 1.8f / b.size.y;
            }
            // Retargeted skinned meshes can collapse their runtime bounds and get frustum-culled;
            // force per-frame bounds so the character never vanishes.
            foreach (var smr in vis.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.updateWhenOffscreen = true;

            // Material override is a SAFETY NET only — the armored-survival-soldier.fbx
            // ships with working materials (as seen in MainWorld), so we keep those and
            // only force SoldierMaterial.mat if the model imported with null/missing
            // materials (e.g. the old .glb's "No Name"/"Stuck" mats). Forcing it
            // unconditionally was flattening the soldier to one untextured material.
            var renderers = vis.GetComponentsInChildren<Renderer>();
            bool materialsMissing = renderers.Any(r => r.sharedMaterial == null);
            var overrideMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/SoldierMaterial.mat");
            if (overrideMat != null && materialsMissing)
            {

                // If the material is pink (Standard shader in URP), force it to URP Lit
                if (overrideMat.shader.name.Contains("Standard") || overrideMat.shader.name.Contains("Error"))
                {
                    var urpShader = Shader.Find("Universal Render Pipeline/Lit");
                    if (urpShader != null) overrideMat.shader = urpShader;
                }

                foreach (var r in renderers)
                    r.sharedMaterial = overrideMat;
            }

            // Hook up the generated controller if it's been built (else PlayerAnimator3D loads it at runtime).
            var anim = vis.GetComponentInChildren<Animator>();
            if (anim == null) anim = vis.AddComponent<Animator>();
            anim.applyRootMotion = false;   // movement is code-driven

            // Assign the model's Humanoid avatar — without it the controller's clips can't
            // retarget and the character just stands in bind pose (the T-pose bug).
            if (anim.avatar == null)
            {
                var modelPath = AssetDatabase.GetAssetPath(model);
                var avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
                if (avatar != null) anim.avatar = avatar;
            }

            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Resources/PlayerAnimator.controller");
            if (ctrl != null) anim.runtimeAnimatorController = ctrl;
            return;
        }

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Visual";
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(parent, false);
        body.transform.localPosition = new Vector3(0f, 0.95f, 0f);
        body.GetComponent<Renderer>().sharedMaterial = MakeMat("PlayerBody", new Color(0.35f, 0.42f, 0.3f));

        var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);   // shows which way you face
        nose.name = "Facing";
        Object.DestroyImmediate(nose.GetComponent<Collider>());
        nose.transform.SetParent(parent, false);
        nose.transform.localPosition = new Vector3(0f, 1.5f, 0.35f);
        nose.transform.localScale = new Vector3(0.18f, 0.18f, 0.3f);
        nose.GetComponent<Renderer>().sharedMaterial = MakeMat("PlayerNose", new Color(0.9f, 0.8f, 0.5f));
    }

    internal static int ScatterProps(int count = 26, float minR = 14f, float spanR = 70f)
    {
        string[] keys = { "barrel", "crate", "rock", "ruin", "wall", "car", "tree", "fence", "container", "debris" };
        var prefabs = new List<GameObject>();
        foreach (var k in keys)
        {
            var p = FindPackPrefab(new[] { k });
            if (p != null && !prefabs.Contains(p)) prefabs.Add(p);
        }
        if (prefabs.Count == 0) return 0;

        var parent = new GameObject("ScatteredProps").transform;
        var rng = new System.Random(42);
        int placed = 0;
        for (int i = 0; i < count; i++)
        {
            var prefab = prefabs[i % prefabs.Count];
            float ang = (float)(rng.NextDouble() * Mathf.PI * 2);
            float r = minR + (float)rng.NextDouble() * spanR;
            Vector3 pos = new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
            // Snap to terrain surface
            var t = Terrain.activeTerrain ?? GameObject.Find("Ground")?.GetComponent<Terrain>();
            if (t != null) pos.y = t.SampleHeight(pos);

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            Neutralize(go);
            placed++;
        }
        return placed;
    }

    /// <summary>Find a prefab in the project whose name matches any keyword. 3D only (has a MeshRenderer).</summary>
    static GameObject FindPackPrefab(string[] keywords, bool requireSkinnedMesh = false)
    {
        foreach (var key in keywords)
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:Prefab {key}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Editor/") || path.Contains("Player/Directions")) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                if (go.GetComponentInChildren<Camera>() != null) continue;
                if (requireSkinnedMesh && go.GetComponentInChildren<SkinnedMeshRenderer>() == null) continue;
                if (!requireSkinnedMesh &&
                    go.GetComponentInChildren<MeshRenderer>() == null &&
                    go.GetComponentInChildren<SkinnedMeshRenderer>() == null) continue;
                return go;
            }
        }
        return null;
    }

    /// <summary>Disable foreign scripts/audio/lights on instantiated pack prefabs so they can't fight our systems.</summary>
    static void Neutralize(GameObject go)
    {
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && !(mb is ResourceNode) && !(mb is CraftingStation) &&
                !(mb is BankingCrate) && !(mb is CombatTarget)) mb.enabled = false;
        foreach (var al in go.GetComponentsInChildren<AudioListener>(true)) Object.DestroyImmediate(al);
        foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
    }

    /// <summary>Scale an object so its render bounds are roughly the wanted height — whatever
    /// the prefab's native unit scale is. (The old version clamped the scale factor to
    /// 0.01–100 and SKIPPED anything outside that, which left cm-scale pack assets giant and
    /// invisible — the same bug that hid the player.)</summary>
    static void NormalizeHeight(GameObject go, float wantHeight)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        var b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        float h = b.size.y;
        if (h < 1e-5f) return;                       // no visible height to measure
        go.transform.localScale *= wantHeight / h;   // normalize no matter how extreme
    }

    internal static Material MakeMat(string name, Color color)
    {
        if (!AssetDatabase.IsValidFolder(MatFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
            AssetDatabase.CreateFolder("Assets/Art", "Generated3D");
        }
        string path = $"{MatFolder}/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) { existing.color = color; return existing; }

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { color = color };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    internal static void AddToBuildSettings(string path)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == path)) return;
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
