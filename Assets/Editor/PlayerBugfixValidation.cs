using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Preview-scene checks: never enters Play Mode, edits the user's scene or loads/saves a character.
public static class PlayerBugfixValidation
{
    const string Output = "Docs/QA/PlayerBugfixes";
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    static readonly StringBuilder Report = new();
    static Scene scene;
    static object Call(object obj, string method, params object[] args) =>
        (obj as Type ?? obj.GetType()).GetMethod(method, Flags).Invoke(obj is Type ? null : obj, args);
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Flags).SetValue(obj, value);
    static T Get<T>(object obj, string field) => (T)obj.GetType().GetField(field, Flags).GetValue(obj);
    static GameObject Make(string name)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        return go;
    }
    static void Check(bool ok, string message) => Report.AppendLine((ok ? "PASS " : "FAIL ") + message);

    [InitializeOnLoadMethod]
    static void RequestedRun()
    {
        EditorApplication.delayCall += () =>
        {
            if (!File.Exists(Output + "/run.request") || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(Output + "/run.request");
            Run();
        };
    }

    [MenuItem("Wasteland/QA/Validate Player Bugfixes")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(Output);
        Report.Clear();
        scene = EditorSceneManager.NewPreviewScene();
        var previousPlayer = PlayerEntity.Instance;
        string objective = QuestGuide.Objective;
        Transform objectiveTarget = QuestGuide.Target;
        var instanceField = typeof(PlayerEntity).GetField("<Instance>k__BackingField", Flags);
        try
        {
            var player = Make("Test player").AddComponent<PlayerEntity>();
            var stats = player.GetComponent<PlayerStats>();
            Call(stats, "Awake");
            Set(player, "<Stats>k__BackingField", stats);
            instanceField.SetValue(null, player);
            player.SetFlag(StarterGuide.MetFlag);
            player.SetFlag("guide_lesson:" + (int)Skill.Beastmastery + ":0");
            player.SetFlag("egg_chosen");
            Check(StarterGuide.TryActiveLesson(player, out var skill, out _) && skill == Skill.Beastmastery,
                "Choosing a companion leaves Beastmastery training active");
            var guide = Make("Test guide").AddComponent<StarterGuide>();
            Set(guide, "_lesson", (Skill?)Skill.Beastmastery);
            Call(guide, "Hook");
            stats.AddXP(Skill.Woodcutting, 10);
            Check(!player.HasFlag("guide_return"), "Unrelated XP does not complete Beastmastery");
            stats.AddXP(Skill.Beastmastery, 25);
            Check(!player.HasFlag("guide_return") && StarterGuide.TryActiveLesson(player, out _, out _),
                "A single beast-task kill does not complete the hunting lesson");
            Call(guide, "OnBeastTaskCompleted");
            Check(player.HasFlag("guide_return") && !StarterGuide.TryActiveLesson(player, out _, out _),
                "Finishing the beast task completes the hunting lesson");
            Call(guide, "Unhook");

            var xp = Make("XP test").AddComponent<XPDropUI>();
            Call(xp, "BuildCanvas"); Call(xp, "TryBind");
            var replacement = Make("Replacement player").AddComponent<PlayerEntity>();
            var replacementStats = replacement.GetComponent<PlayerStats>();
            Call(replacementStats, "Awake"); Set(replacement, "<Stats>k__BackingField", replacementStats);
            instanceField.SetValue(null, replacement);
            Call(xp, "TryBind");
            Check(Get<PlayerStats>(xp, "_stats") == replacementStats, "XP feedback binds to replacement player");
            replacementStats.AddXP(Skill.Fishing, 100);
            Check(Get<TMPro.TMP_Text>(xp, "_congratulations") != null, "Skill level-up creates congratulations banner");
            Check(Get<System.Collections.ICollection>(xp, "_sparks").Count == 32, "Skill level-up creates two fireworks");
            Call(xp, "Unbind");

            var inventory = Make("Inventory test").AddComponent<InventoryPanelUI>();
            var crafting = Make("Crafting test").AddComponent<CraftingUI>();
            Call(inventory, "BuildUI"); Call(crafting, "BuildUI");
            Check(!Get<GameObject>(inventory, "_backdrop").GetComponent<UnityEngine.UI.Image>().raycastTarget &&
                !Get<GameObject>(crafting, "_backdrop").GetComponent<UnityEngine.UI.Image>().raycastTarget,
                "Inventory and crafting backdrops do not block each other or tabs");
            var craftRect = Get<GameObject>(crafting, "_panel").GetComponent<RectTransform>();
            var invRect = Get<GameObject>(inventory, "_panel").GetComponent<RectTransform>();
            Check(craftRect.anchorMin.x == 0 && invRect.anchorMin.x == 1, "Crafting and inventory occupy opposite sides");

            ItemIconLoader.InitializeIcons();
            int drops = 0;
            foreach (var item in ItemRegistry.All.Where(i => i.icon != null))
            {
                var drop = GroundItem.Spawn(item.id, 1, Vector3.zero);
                SceneManager.MoveGameObjectToScene(drop.gameObject, scene);
                var sprite = drop.GetComponentInChildren<SpriteRenderer>();
                float size = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
                if (Mathf.Abs(size - .7f) > .001f) Check(false, "Drop size " + item.name + " = " + size);
                drops++;
                Object.DestroyImmediate(drop.gameObject);
            }
            Check(drops > 0, "Sized " + drops + " item icons to 0.7 world metres");
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(target, scene);
            var station = target.AddComponent<CraftingStation>();
            target.GetComponent<BoxCollider>().size = Vector3.one * 20;
            Check(!WorldInteractables.RayHitsVisual(station, new Ray(new Vector3(5, 0, -10), Vector3.forward)),
                "Oversized collider cannot claim a ray missing the visible model");
            Check(WorldInteractables.RayHitsVisual(station, new Ray(new Vector3(0, 0, -10), Vector3.forward)),
                "Ray hitting visible model remains selectable");
            Object.DestroyImmediate(target);

            var wrapper = Make("Companion size");
            var wolf = Object.Instantiate(Resources.Load<GameObject>("wolf"), wrapper.transform);
            Call(typeof(CompanionManager), "StripStrayComponents", wolf);
            var animator = wolf.GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("WolfAnimator");
            animator.enabled = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false; animator.Rebind(); animator.Update(0f);
            Report.AppendLine("Wolf before size: " + BakedBounds(wrapper) + " scale=" + wrapper.transform.localScale);
            foreach (var renderer in wrapper.GetComponentsInChildren<Renderer>())
                Report.AppendLine("Wolf renderer " + renderer.name + " " + renderer.GetType().Name + " enabled=" + renderer.enabled + " bounds=" + renderer.bounds);
            Call(typeof(CompanionManager), "NormalizeModelHeight", wrapper, 1f);
            Report.AppendLine("Wolf normalized scale=" + wrapper.transform.localScale + " bounds=" + BakedBounds(wrapper));
            wrapper.transform.localScale *= .7f;
            animator.Update(0f);
            var bounds = BakedBounds(wrapper);
            Check(Mathf.Abs(bounds.size.y - .7f) < .02f, "Pup height = " + bounds.size.y.ToString("F3") + "m");
            animator.Update(.3f);
            bounds = BakedBounds(wrapper);
            Check(bounds.size.y < 1.1f, "Animated pup remains small: " + bounds.size.y.ToString("F3") + "m");
            Capture(bounds, "pup.png");
            Object.DestroyImmediate(wrapper);

            foreach (string clipName in new[] { "woodcutting", "Mining" })
            {
                string path = "Assets/Art/newshit/newplayershit/playeranimations/" + clipName + ".fbx";
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
                Check(clip.isHumanMotion && clip.isLooping, clipName + " imports as a looping Humanoid animation, duration=" + clip.length);
            }
            var scenePlayer = Object.FindObjectsByType<PlayerEntity>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.gameObject.scene != scene);
            var sourceAnimator = scenePlayer != null ? scenePlayer.GetComponentInChildren<Animator>(true) : null;
            if (sourceAnimator != null)
            {
                var sizeRoot = Make("Gather animation preview");
                var model = Object.Instantiate(sourceAnimator.gameObject, sizeRoot.transform);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                var body = model.GetComponent<Animator>();
                body.enabled = true; body.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Report.AppendLine("Player avatar=" + body.avatar + " valid=" + body.avatar.isValid + " human=" + body.isHuman);
                body.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("PlayerAnimator");
                body.applyRootMotion = false; body.Rebind(); body.Update(0f);
                Call(typeof(CompanionManager), "NormalizeModelHeight", sizeRoot, 1.8f);
                foreach (var state in new[] { "GatherWood", "GatherMine" })
                {
                    body.SetBool("Gathering", true); body.SetInteger("GatherType", state == "GatherWood" ? 0 : 2);
                    body.Play(state, 0, .25f); body.Update(.01f);
                    var info = body.GetCurrentAnimatorClipInfo(0);
                    Report.AppendLine(state + " clip=" + (info.Length > 0 ? AssetDatabase.GetAssetPath(info[0].clip) : "NONE"));
                    Capture(BakedBounds(sizeRoot), state + ".png");
                }
                var controller = (UnityEditor.Animations.AnimatorController)Resources.Load<RuntimeAnimatorController>("PlayerAnimator");
                foreach (var candidate in new[] { "playerwoodcut", "miningorwhatever" })
                {
                    var clip = AssetDatabase.LoadAllAssetsAtPath("Assets/Resources/otherneededassets/" + candidate + ".fbx")
                        .OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));
                    var state = controller.layers[0].stateMachine.states.First(s => s.state.name == "GatherWood").state;
                    var overrides = new AnimatorOverrideController(controller);
                    overrides[(AnimationClip)state.motion] = clip;
                    body.runtimeAnimatorController = overrides;
                    body.SetBool("Gathering", true); body.SetInteger("GatherType", 0);
                    body.Play("GatherWood", 0, .35f); body.Update(.01f);
                    Report.AppendLine("Candidate " + candidate + " humanoid=" + clip.isHumanMotion + " duration=" + clip.length);
                    Capture(BakedBounds(sizeRoot), candidate + ".png");
                    body.runtimeAnimatorController = controller;
                    Object.DestroyImmediate(overrides);
                }
                Object.DestroyImmediate(sizeRoot);
            }
        }
        catch (Exception ex) { Report.AppendLine("FAIL EXCEPTION " + ex); }
        finally
        {
            instanceField.SetValue(null, previousPlayer);
            QuestGuide.Show(objective, objectiveTarget);
            EditorSceneManager.ClosePreviewScene(scene);
            File.WriteAllText(Output + "/validation.txt", Report.ToString());
            Debug.Log("[PlayerBugfixValidation]\n" + Report);
        }
    }

    static Bounds BakedBounds(GameObject root)
    {
        Bounds result = default; bool any = false;
        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh(); renderer.BakeMesh(mesh, true);
            foreach (var vertex in mesh.vertices)
            {
                var point = renderer.transform.TransformPoint(vertex);
                if (!any) { result = new Bounds(point, Vector3.zero); any = true; }
                else result.Encapsulate(point);
            }
            Object.DestroyImmediate(mesh);
        }
        return result;
    }

    static void Capture(Bounds bounds, string filename)
    {
        var cameraGO = Make("QA camera"); var camera = cameraGO.AddComponent<Camera>(); camera.scene = scene;
        camera.orthographic = true; camera.orthographicSize = Mathf.Max(.6f, bounds.size.magnitude * .6f);
        camera.transform.position = bounds.center + new Vector3(3, 1, 3); camera.transform.LookAt(bounds.center);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.15f, .17f, .2f);
        var lightGO = Make("QA light"); var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 2; lightGO.transform.rotation = Quaternion.Euler(40, -20, 0);
        var rt = RenderTexture.GetTemporary(640, 640, 24); var previous = RenderTexture.active;
        camera.targetTexture = rt;
        try
        {
            camera.Render(); RenderTexture.active = rt;
            var image = new Texture2D(640, 640, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); image.Apply();
            File.WriteAllBytes(Output + "/" + filename, image.EncodeToPNG()); Object.DestroyImmediate(image);
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null; RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(cameraGO); Object.DestroyImmediate(lightGO);
        }
    }
}
