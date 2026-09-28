using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click fixer for dontfuckindelete.unity — the only gameplay scene.
/// Menu:  Wasteland ▸ Fix Scene (dontfuckindelete)
///
/// What it does (all idempotent — safe to run again):
///   1. PLAYER VISUAL   — attaches Crimson-Circuit FBX as the Visual child if missing,
///                         assigns avatar + PlayerAnimator.controller, root motion off.
///                         Extracts + wires the FBX's 4 textures into proper materials.
///   2. ROXY            — places Eve.fbx with RoxyNPC. CapsuleCollider for Interactor3D.
///   3. CRAFTING STATIONS — swaps primitive-box placeholders for real FBX models,
///                         preserving the CraftingStation component + all serialized recipes.
///   4. WORLD SCATTER   — drops GLB props (cars, ruins, trees) around the spawn area.
///   5. SAVE            — marks the scene dirty and saves.
///
/// HUD: all UI auto-spawns via [RuntimeInitializeOnLoadMethod]. Zero scene wiring needed.
/// </summary>
public static class FixDontFuckInDelete
{
    // ── asset paths ──────────────────────────────────────────────────────────
    const string PlayerFbxA   = "Assets/Art/newshit/finalofficialplayercharacter/Crimson-Circuit-019f196d.fbx";
    const string PlayerFbxB   = "Assets/Art/newshit/finalofficialplayercharacter/Crimson-Circuit-019f196d 1.fbx";
    const string ControllerP  = "Assets/Resources/PlayerAnimator.controller";

    // Roxy: prefer the "Eve" girl model, fall back to generic NPC pack
    const string RoxyFbxA     = "Assets/Art/newshit/GirlModels/uploads_files_6689266_Cute+Girl/Eve cute/Eve .fbx";
    const string RoxyFbxB     = "Assets/_todaysassets/NPCs/NPC_01.fbx";

    const string ForgeFbx     = "Assets/_todaysassets/SplitModels/Rusted-Armory-Workshop/forge.fbx";
    const string CookFbx      = "Assets/_todaysassets/SplitModels/Rusted-Armory-Workshop/CookingRange.fbx";
    const string BenchFbx     = "Assets/_todaysassets/SplitModels/Rusted-Armory-Workshop/GunsmithingBench.fbx";

    const string MatFolder    = "Assets/Art/Generated3D";

    // ── world scatter asset paths (actual filenames confirmed on disk) ────────
    static readonly string[] PropGlbs = {
        "Assets/Art/new assets nodes etc/abandoned car.glb",
        "Assets/Art/new assets nodes etc/abandoned robotics.glb",
        "Assets/Art/new assets nodes etc/damaged building.glb",
        "Assets/Art/new assets nodes etc/damagedbuilding2.glb",
        "Assets/Art/new assets nodes etc/anotherdamagedbuilding.glb",
        "Assets/Art/new assets nodes etc/Junk Pile.glb",
        "Assets/Art/new assets nodes etc/tech dumpster.glb",
        "Assets/Art/new assets nodes etc/downed drones pile.glb",
    };
    static readonly string[] TreeGlbs = {
        "Assets/Art/new assets nodes etc/big mutant tree.glb",
        "Assets/Art/new assets nodes etc/gnarled tree.glb",
        "Assets/Art/new assets nodes etc/petrified tree.glb",
        "Assets/Art/new assets nodes etc/ancient hardened flora.glb",
        "Assets/Art/new assets nodes etc/fossilized deadwood.glb",
        "Assets/Art/new assets nodes etc/brush scrub.glb",
    };

    public static void FixHeadless()
    {
        var log = new System.Text.StringBuilder();
        log.AppendLine("=== Fix Scene Headless ===\n");
        int changes = 0;

        var playerGO = GameObject.Find("Player");
        if (playerGO == null)
        {
            Debug.LogError("[FixSceneHeadless] No 'Player' GameObject found.");
            return;
        }

        changes += FixPlayerVisual(playerGO, log);

        changes += PlaceNPC<RoxyNPC>("Roxy",
            playerGO.transform.position + new Vector3(-8f, 0f, 6f),
            RoxyFbxA, RoxyFbxB, new Color(0.85f, 0.5f, 0.55f), log);

        changes += SwapStation(StationType.Furnace,     ForgeFbx, "Furnace",      log);
        changes += SwapStation(StationType.CookingFire, CookFbx,  "Cooking Fire", log);
        changes += SwapStation(StationType.Workbench,   BenchFbx, "Workbench",    log);

        changes += ScatterWorld(playerGO.transform.position, log);

        if (changes > 0)
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            log.AppendLine($"\n✅  Saved. {changes} change(s) applied.");
        }
        else
        {
            log.AppendLine("\nℹ  Nothing to change.");
        }

        Debug.Log(log.ToString());
    }

    // ── entry point ──────────────────────────────────────────────────────────
    [MenuItem("Wasteland/Fix Scene (dontfuckindelete)")]
    public static void Fix()
    {
        var log = new System.Text.StringBuilder();
        log.AppendLine("=== Fix Scene (dontfuckindelete) ===\n");
        int changes = 0;

        var playerGO = GameObject.Find("Player");
        if (playerGO == null)
        {
            Debug.LogError("[FixScene] No 'Player' GameObject found — is dontfuckindelete open?");
            return;
        }

        // ── 1. Player visual ─────────────────────────────────────────────────
        changes += FixPlayerVisual(playerGO, log);

        // ── 2. Roxy (sole tutorial NPC) ───────────────────────────────────────
        changes += PlaceNPC<RoxyNPC>("Roxy",
            playerGO.transform.position + new Vector3(-8f, 0f, 6f),
            RoxyFbxA, RoxyFbxB, new Color(0.85f, 0.5f, 0.55f), log);

        // ── 3. Crafting stations ──────────────────────────────────────────────
        changes += SwapStation(StationType.Furnace,     ForgeFbx, "Furnace",      log);
        changes += SwapStation(StationType.CookingFire, CookFbx,  "Cooking Fire", log);
        changes += SwapStation(StationType.Workbench,   BenchFbx, "Workbench",    log);

        // ── 4. World scatter ──────────────────────────────────────────────────
        changes += ScatterWorld(playerGO.transform.position, log);

        // ── 5. Save ───────────────────────────────────────────────────────────
        if (changes > 0)
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            log.AppendLine($"\n✅  Saved. {changes} change(s) applied.");
        }
        else
        {
            log.AppendLine("\nℹ  Nothing to change — scene already set up.");
        }

        log.AppendLine("\nNext step: Wasteland ▸ Player ▸ 1. Set Up Rigs + Animator (Mixamo)");
        log.AppendLine("then:       Wasteland ▸ Player ▸ 2. Rebuild Animator (fast)");
        log.AppendLine("then:       Press Play — walk to Roxy and press E.");
        log.AppendLine("tip:        Run again at any time — it's fully idempotent.");
        log.AppendLine("world:      Wasteland ▸ Build World Layout  — stamps the 5-tier terrain.");

        Debug.Log(log.ToString());
        EditorUtility.DisplayDialog("Fix Scene — Done",
            $"{changes} change(s) applied.\nSee Console for full report.", "OK");
    }

    // ── player visual ────────────────────────────────────────────────────────
    static int FixPlayerVisual(GameObject playerGO, System.Text.StringBuilder log)
    {
        int changes = 0;

        // Fix root scale — a scaled root inflates the CharacterController capsule
        if (playerGO.transform.localScale != Vector3.one)
        {
            var vis0 = playerGO.transform.Find("Visual");
            if (vis0 != null)
                vis0.localScale = Vector3.Scale(vis0.localScale, playerGO.transform.localScale);
            playerGO.transform.localScale = Vector3.one;
            log.AppendLine("✅  Player root scale reset to 1.");
            changes++;
        }

        var existingVis = playerGO.transform.Find("Visual");

        if (existingVis == null)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerFbxA)
                     ?? AssetDatabase.LoadAssetAtPath<GameObject>(PlayerFbxB);

            GameObject vis;
            if (model != null)
            {
                vis = (GameObject)PrefabUtility.InstantiatePrefab(model);
                Undo.RegisterCreatedObjectUndo(vis, "Add player visual");
                StripJunkComponents(vis);
                NormalizeHeight(vis, 1.8f);
                // Wire textures from the FBX folder onto the skinned meshes
                ApplyPlayerTextures(vis, model);
                foreach (var smr in vis.GetComponentsInChildren<SkinnedMeshRenderer>())
                    smr.updateWhenOffscreen = true;
                log.AppendLine($"✅  Player Visual: attached {model.name}.");
            }
            else
            {
                vis = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.DestroyImmediate(vis.GetComponent<Collider>());
                vis.GetComponent<Renderer>().sharedMaterial =
                    MakeMat("PlayerBody", new Color(0.35f, 0.42f, 0.3f));
                Undo.RegisterCreatedObjectUndo(vis, "Add player capsule visual");
                log.AppendLine("⚠  Crimson-Circuit FBX not found — used capsule placeholder.");
            }

            vis.name = "Visual";
            vis.transform.SetParent(playerGO.transform, false);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localRotation = Quaternion.identity;
            WireAnimator(vis, model);
            changes++;
        }
        else
        {
            // Already there — refresh controller, ensure textures are wired
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerFbxA)
                     ?? AssetDatabase.LoadAssetAtPath<GameObject>(PlayerFbxB);
            ApplyPlayerTextures(existingVis.gameObject, model);
            WireAnimator(existingVis.gameObject, null);
            log.AppendLine("✅  Player Visual already present — controller + textures refreshed.");
        }

        return changes;
    }

    /// <summary>Wire the 4 textures sitting next to the FBX onto all SkinnedMeshRenderers on the
    /// Visual. The FBX uses embedded materials (materialLocation=1) which come out grey at runtime
    /// because Unity never auto-links the sibling JPGs. We load each jpg, build a proper URP/Lit
    /// material, and assign it to every sub-mesh so the character has actual colour.</summary>
    static void ApplyPlayerTextures(GameObject vis, GameObject modelAsset)
    {
        string fbxPath = modelAsset != null
            ? AssetDatabase.GetAssetPath(modelAsset)
            : PlayerFbxA;
        string folder = System.IO.Path.GetDirectoryName(fbxPath).Replace('\\', '/');

        // image0..image3.jpg — grab all of them
        var textures = new List<Texture2D>();
        for (int i = 0; i <= 3; i++)
        {
            var jpg  = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/image{i}.jpg");
            var png  = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/image{i}.png");
            var tex  = jpg ?? png;
            if (tex != null) textures.Add(tex);
        }
        if (textures.Count == 0)
        {
            // Nothing in the FBX folder — try Resources/PlayerTextures
            foreach (var t in Resources.LoadAll<Texture2D>("PlayerTextures"))
                textures.Add(t);
        }
        if (textures.Count == 0) return;

        if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
        if (!AssetDatabase.IsValidFolder(MatFolder))    AssetDatabase.CreateFolder("Assets/Art", "Generated3D");

        var smrs = vis.GetComponentsInChildren<SkinnedMeshRenderer>();
        for (int i = 0; i < smrs.Length; i++)
        {
            int ti = i % textures.Count;
            string matPath = $"{MatFolder}/PlayerMat_{i}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader) { name = $"PlayerMat_{i}" };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.mainTexture = textures[ti];
            if (mat.HasProperty("_BaseMap"))  mat.SetTexture("_BaseMap",  textures[ti]);
            if (mat.HasProperty("_MainTex"))  mat.SetTexture("_MainTex",  textures[ti]);
            smrs[i].sharedMaterial = mat;
        }
        AssetDatabase.SaveAssets();
    }

    static void WireAnimator(GameObject visGO, GameObject modelAsset)
    {
        var anim = visGO.GetComponentInChildren<Animator>();
        if (anim == null) anim = visGO.AddComponent<Animator>();
        anim.applyRootMotion = false;

        if (anim.avatar == null && modelAsset != null)
        {
            string path = AssetDatabase.GetAssetPath(modelAsset);
            var av = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (av != null) anim.avatar = av;
        }

        var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerP);
        if (ctrl != null) anim.runtimeAnimatorController = ctrl;
    }

    // ── NPC placement ────────────────────────────────────────────────────────
    static int PlaceNPC<T>(string goName, Vector3 pos,
                           string primaryModel, string fallbackModel,
                           Color capsuleColor,
                           System.Text.StringBuilder log) where T : Component
    {
        if (Object.FindAnyObjectByType<T>() != null)
        {
            EnsureInteractionCollider(Object.FindAnyObjectByType<T>().gameObject);
            log.AppendLine($"✅  {goName} already in scene.");
            return 0;
        }

        SnapToTerrain(ref pos);

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(primaryModel)
                 ?? AssetDatabase.LoadAssetAtPath<GameObject>(fallbackModel);

        GameObject go;
        if (model != null)
        {
            go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            Undo.RegisterCreatedObjectUndo(go, $"Place {goName}");
            DisableStrayComponents(go);
            NormalizeHeight(go, 1.8f);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                smr.updateWhenOffscreen = true;
            log.AppendLine($"✅  {goName} placed using {model.name}.");
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.DestroyImmediate(go.GetComponent<CapsuleCollider>());
            go.GetComponent<Renderer>().sharedMaterial = MakeMat(goName.Replace(" ", ""), capsuleColor);
            Undo.RegisterCreatedObjectUndo(go, $"Place {goName} capsule");
            log.AppendLine($"⚠  {goName}: no model found — placed capsule placeholder.");
        }

        go.name = goName;
        go.transform.position = pos + Vector3.up * 0.05f;
        go.transform.rotation = Quaternion.identity;
        go.AddComponent<T>();
        EnsureInteractionCollider(go);
        return 1;
    }

    // ── station swap ──────────────────────────────────────────────────────────
    static int SwapStation(StationType type, string modelPath, string displayName,
                            System.Text.StringBuilder log)
    {
        CraftingStation target = null;
        foreach (var cs in Object.FindObjectsByType<CraftingStation>())
            if (cs.stationType == type) { target = cs; break; }

        if (target == null)
        {
            log.AppendLine($"⚠  No {displayName} found in scene — skipping.");
            return 0;
        }

        // Skip if already a non-primitive mesh
        var mf = target.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            string mn = mf.sharedMesh.name;
            if (mn != "Cube" && mn != "Cylinder" && mn != "Sphere" && mn != "Capsule")
            {
                log.AppendLine($"✅  {displayName}: already using a real model.");
                return 0;
            }
        }

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
        {
            log.AppendLine($"⚠  {displayName}: model not found at {modelPath} — skipping.");
            return 0;
        }

        Vector3    savedPos     = target.transform.position;
        Quaternion savedRot     = target.transform.rotation;
        var        savedRecipes = target.recipes;
        var        savedType    = target.stationType;

        Undo.DestroyObjectImmediate(target.gameObject);

        var newGO = (GameObject)PrefabUtility.InstantiatePrefab(model);
        Undo.RegisterCreatedObjectUndo(newGO, $"Swap {displayName}");
        newGO.name = displayName;
        newGO.transform.position = savedPos;
        newGO.transform.rotation = savedRot;
        // Crafting stations are worktop-height objects — ~1.1 m, not human-height 1.8 m
        NormalizeHeight(newGO, 1.1f);
        DisableStrayComponents(newGO);

        // Solid collider for Interactor3D proximity detection
        if (newGO.GetComponentInChildren<Collider>() == null)
        {
            var bc = newGO.AddComponent<BoxCollider>();
            bc.size   = new Vector3(1.4f, 1.6f, 1.4f);
            bc.center = new Vector3(0f, 0.8f, 0f);
        }
        foreach (var c in newGO.GetComponentsInChildren<Collider>())
            c.isTrigger = false;

        var cs2 = newGO.AddComponent<CraftingStation>();
        cs2.stationType = savedType;
        cs2.recipes     = savedRecipes;

        log.AppendLine($"✅  {displayName}: swapped to {model.name}.");
        return 1;
    }

    // ── world scatter ─────────────────────────────────────────────────────────
    static int ScatterWorld(Vector3 origin, System.Text.StringBuilder log)
    {
        if (GameObject.Find("ScatterPropsGenerated") != null)
        {
            log.AppendLine("✅  World scatter already present.");
            return 0;
        }

        var root = new GameObject("ScatterPropsGenerated");
        Undo.RegisterCreatedObjectUndo(root, "Scatter world props");
        int placed = 0;

        // Props: rings of ruined buildings and junk around the spawn point
        var propSlots = new List<(string path, Vector3 offset, float yRot)>
        {
            // damaged buildings forming a ruined suburb backdrop to the north
            (PropGlbs[2], new Vector3(  0f, 0f,  28f),   0f),
            (PropGlbs[3], new Vector3(-22f, 0f,  20f),  45f),
            (PropGlbs[4], new Vector3( 18f, 0f,  24f), -20f),
            (PropGlbs[4], new Vector3(-14f, 0f,  32f),  90f),
            // junk/debris scatter south of spawn — the "starting area" floor dressing
            (PropGlbs[5], new Vector3( 10f, 0f,  -6f),  15f),
            (PropGlbs[5], new Vector3(-12f, 0f,  -8f), 200f),
            (PropGlbs[6], new Vector3(  6f, 0f, -14f),   0f),
            (PropGlbs[6], new Vector3(-16f, 0f, -16f), 120f),
            (PropGlbs[7], new Vector3( 20f, 0f,   2f), -40f),
            // abandoned robotics east — wasteland flavor
            (PropGlbs[1], new Vector3( 24f, 0f,  -4f),  70f),
            (PropGlbs[1], new Vector3(-20f, 0f,  10f), 180f),
            // abandoned car wreck
            (PropGlbs[0], new Vector3( 14f, 0f, -10f),  20f),
            (PropGlbs[0], new Vector3(-18f, 0f, -12f), 170f),
        };

        foreach (var (path, offset, yRot) in propSlots)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) continue;
            var pos = origin + offset;
            SnapToTerrain(ref pos);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            go.transform.position = pos;
            go.transform.eulerAngles = new Vector3(0f, yRot, 0f);
            DisableStrayComponents(go);
            placed++;
        }

        // Mutant trees at mid-ground distance
        var treeSlots = new List<(string path, Vector3 offset, float yRot)>
        {
            (TreeGlbs[0], new Vector3( 26f, 0f,   2f),   0f),
            (TreeGlbs[1], new Vector3(-26f, 0f,   6f),  90f),
            (TreeGlbs[2], new Vector3( 12f, 0f,  22f), 180f),
            (TreeGlbs[3], new Vector3(-12f, 0f,  24f),  45f),
            (TreeGlbs[4], new Vector3(  4f, 0f, -22f), 270f),
            (TreeGlbs[5], new Vector3(-20f, 0f, -16f),  60f),
            (TreeGlbs[0], new Vector3( 32f, 0f,  14f), 120f),
            (TreeGlbs[1], new Vector3(-30f, 0f, -10f), 210f),
        };

        foreach (var (path, offset, yRot) in treeSlots)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) continue;
            var pos = origin + offset;
            SnapToTerrain(ref pos);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            go.transform.position = pos;
            go.transform.eulerAngles = new Vector3(0f, yRot, 0f);
            NormalizeHeight(go, 5.0f);
            DisableStrayComponents(go);
            placed++;
        }

        if (placed > 0)
            log.AppendLine($"✅  World scatter: placed {placed} props/trees.");
        else
            log.AppendLine("⚠  World scatter: no GLB models found at expected paths.");

        return placed > 0 ? 1 : 0;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    static void EnsureInteractionCollider(GameObject go)
    {
        if (go.GetComponentsInChildren<Collider>(true).Any(c => !c.isTrigger)) return;
        var cc    = go.AddComponent<CapsuleCollider>();
        cc.height = 1.8f;
        cc.radius = 0.4f;
        cc.center = new Vector3(0f, 0.9f, 0f);
    }

    static void NormalizeHeight(GameObject go, float wantHeight)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        var b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        if (b.size.y < 1e-5f) return;
        go.transform.localScale *= wantHeight / b.size.y;
    }

    static void SnapToTerrain(ref Vector3 pos)
    {
        var t = Terrain.activeTerrain;
        if (t != null) { pos.y = t.SampleHeight(pos); return; }
        if (Physics.Raycast(new Vector3(pos.x, pos.y + 200f, pos.z), Vector3.down,
                            out var hit, 400f, ~0, QueryTriggerInteraction.Ignore))
            pos.y = hit.point.y;
    }

    static void StripJunkComponents(GameObject go)
    {
        foreach (var c in go.GetComponentsInChildren<Camera>(true))
            Object.DestroyImmediate(c.gameObject);
        foreach (var l in go.GetComponentsInChildren<Light>(true))
            Object.DestroyImmediate(l.gameObject);
        foreach (var a in go.GetComponentsInChildren<AudioListener>(true))
            Object.DestroyImmediate(a);
    }

    static void DisableStrayComponents(GameObject go)
    {
        StripJunkComponents(go);
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            var type = mb.GetType();
            if (type == typeof(RoxyNPC)           || type == typeof(TutorialNPC)  ||
                type == typeof(CraftingStation)    || type == typeof(BankingCrate) ||
                type == typeof(CombatTarget)) continue;
            mb.enabled = false;
        }
    }

    static Material MakeMat(string name, Color color)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Art"))   AssetDatabase.CreateFolder("Assets", "Art");
        if (!AssetDatabase.IsValidFolder(MatFolder))      AssetDatabase.CreateFolder("Assets/Art", "Generated3D");

        string path     = $"{MatFolder}/{name}.mat";
        var    existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) { existing.color = color; return existing; }

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat    = new Material(shader) { color = color };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
