using System.Collections;
using UnityEngine;

/// <summary>
/// Short "quest intro" camera peek: when Roxy hands you a quest, the camera glides from the player
/// over to the place you need to go (the fishing spot, the rubble, the workbench…), holds a beat with
/// a caption, then glides back. Purely cosmetic and skippable — it never takes control of the player,
/// and clicking/moving during it doesn't break anything (movement still works; only the camera focus
/// is borrowed via OrbitCamera3D's cinematic hook).
///
/// It finds the target by the quest's Skill: gathering skills peek at the nearest matching
/// ResourceNode, the crafting skills at the matching station. Beastmastery (a companion skill with no
/// world spot) simply does nothing. Self-bootstrapping; no scene setup.
/// </summary>
public class QuestCam : MonoBehaviour
{
    public static QuestCam Instance { get; private set; }

    [Header("Timing (seconds)")]
    public float travelIn  = 1.0f;   // glide out to the target
    public float hold      = 1.4f;   // linger on it
    public float travelOut = 0.8f;   // glide back to the player

    Coroutine _running;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("QuestCam (auto)");
        go.AddComponent<QuestCam>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>Peek at where this quest's skill is trained. No-op if nothing matching is in the scene.</summary>
    public static void PeekAtSkill(Skill skill, string caption = null)
    {
        if (Instance == null) return;
        var target = FindTargetForSkill(skill);
        if (target == null) return;   // e.g. Beastmastery, or the spot isn't in this scene
        Instance.Peek(target.position, caption ?? DefaultCaption(skill));
    }

    public void Peek(Vector3 point, string caption)
    {
        if (_running != null) StopCoroutine(_running);
        _running = StartCoroutine(PeekRoutine(point, caption));
    }

    IEnumerator PeekRoutine(Vector3 point, string caption)
    {
        OrbitCamera3D.CinematicPoint = point;
        if (!string.IsNullOrEmpty(caption))
            HUDController.Emit($"<color=#9AD1FF>[GUIDE]:</color> {caption}");

        yield return Ramp(0f, 1f, travelIn);
        OrbitCamera3D.CinematicWeight = 1f;
        yield return new WaitForSeconds(hold);
        yield return Ramp(1f, 0f, travelOut);

        OrbitCamera3D.CinematicWeight = 0f;
        OrbitCamera3D.CinematicPoint = null;
        _running = null;
    }

    static IEnumerator Ramp(float from, float to, float seconds)
    {
        if (seconds <= 0f) { OrbitCamera3D.CinematicWeight = to; yield break; }
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            // Smoothstep so the glide eases in and out instead of jerking at the ends.
            float k = Mathf.SmoothStep(from, to, Mathf.Clamp01(t / seconds));
            OrbitCamera3D.CinematicWeight = k;
            yield return null;
        }
        OrbitCamera3D.CinematicWeight = to;
    }

    // ── target lookup ──────────────────────────────────────────────────────
    // Also used by StarterGuide to point the objective arrow at each of Roxy's lessons. Inactive objects
    // count: the world streamer switches far-off cells off, and the nearest dead tree may be in one.
    /// <param name="toolId">The tool the lesson hands over, if any: gathering spots that need it come first
    /// (the rod points at a real fishing spot, not a water barrel that happens to count as fishing).</param>
    public static Transform TargetForSkill(Skill skill, int toolId = 0) => FindTargetForSkill(skill, toolId);

    static Transform FindTargetForSkill(Skill skill, int toolId = 0)
    {
        var player = PlayerEntity.Instance;
        Vector3 from = player != null ? player.transform.position : Vector3.zero;

        switch (skill)
        {
            case Skill.Fishing:
            case Skill.Woodcutting:
            case Skill.Scrapping:
                return NearestNode(skill, from, toolId);

            case Skill.Cooking:  return NearestStation(StationType.CookingFire, from);
            // Smithing spans both surfaces (smelting at the Furnace, forging at the Workbench).
            // Point at the Furnace if one is nearer, else the Workbench, so either step guides.
            case Skill.Smithing:
                return NearestStation(StationType.Furnace, from)
                    ?? NearestStation(StationType.Workbench, from);

            default: return null;   // Beastmastery / combat skills — no fixed world spot
        }
    }

    // Nodes you can actually work at your level, and never a Sulphur Vent: they sit beside every mining
    // cluster and count as Scrapping too, but the lesson is "smash the rubble for scrap". Any node of the
    // skill is the fallback, so a scene without a beginner node still points somewhere.
    static Transform NearestNode(Skill skill, Vector3 from, int toolId = 0)
    {
        var stats = PlayerEntity.Instance != null ? PlayerEntity.Instance.Stats : null;
        int level = stats != null ? stats.GetLevel(skill) : 1;
        Transform best = null, usable = null, any = null;
        float bestSq = float.MaxValue, usableSq = float.MaxValue, anySq = float.MaxValue;
        foreach (var n in Object.FindObjectsByType<ResourceNode>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (n.skill != skill) continue;
            float d = (n.transform.position - from).sqrMagnitude;
            if (d < anySq) { anySq = d; any = n.transform; }
            if (n.levelRequired > level || n.nodeType == ResourceNodeType.SulphurVent) continue;
            if (d < usableSq) { usableSq = d; usable = n.transform; }
            if (toolId > 0 && n.requiredToolId != toolId) continue;
            if (d < bestSq) { bestSq = d; best = n.transform; }
        }
        return best != null ? best : usable != null ? usable : any;
    }

    public static Transform NearestStation(StationType type, Vector3 from)
    {
        Transform best = null; float bestSq = float.MaxValue;
        foreach (var s in Object.FindObjectsByType<CraftingStation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (s.stationType != type) continue;
            float d = (s.transform.position - from).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = s.transform; }
        }
        return best;
    }

    public static string DefaultCaption(Skill skill) => skill switch
    {
        Skill.Fishing     => "Head to the water — cast a line there.",
        Skill.Woodcutting => "See those dead trees? Chop one down for wood.",
        Skill.Scrapping   => "That rubble pile is where you'll dig up scrap.",
        Skill.Cooking  => "Take your catch to the cooking fire over there.",
        Skill.Smithing => "Smelt scrap into bars at the furnace, then forge gear at the workbench.",
        _                 => "There's your next stop."
    };
}
