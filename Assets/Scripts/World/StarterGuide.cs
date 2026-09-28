using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A new player's first steps, so nobody is left standing in the square wondering what to do:
///   1. Arriving in the world: a welcome banner, then the camera glides over to Roxy and back.
///   2. "Talk to Roxy" becomes the objective (QuestGuide: the top line, an arrow over her head, an arrow on
///      the screen edge when she's off screen, and the minimap marker).
///   3. Each lesson she gives points at where it's done — the nearest dead tree, fishing spot, furnace … —
///      with a short camera glide over to it, then the objective marker stays on that same spot until you
///      earn XP in that skill (Beastmastery requires a task kill with your beast). Then it's "Return to
///      Roxy", and round again.
/// Meeting her sets the player flag "roxy_met", so returning players aren't welcomed all over again, and
/// the lesson in progress (or "return to Roxy") is kept in the saved flags too, so it's still pointing the
/// right way after you close the game and come back. Self-bootstrapping; does nothing without a RoxyNPC.
/// </summary>
public class StarterGuide : MonoBehaviour
{
    public const string MetFlag = "roxy_met";
    const string LessonFlag = "guide_lesson:";   // + (int)skill + ":" + giveItemId while a lesson's under way
    const string ReturnFlag = "guide_return";    // lesson done: back to Roxy
    const int ScrapBarId = 11;   // Roxy hands one over for the forging lesson (the other Smithing lesson smelts)

    static StarterGuide _instance;

    RoxyNPC _roxy;
    float _nextFind;
    bool _welcomed;          // shown once per visit to the scene
    Skill? _lesson;          // the lesson in progress, if any
    int _lessonItem;
    string _combatStatus;
    bool _returnToRoxy;
    bool _restored;          // picked the saved lesson back up this visit
    PlayerStats _stats;
    float _nextBeastGuideAt;

    public static bool TryActiveLesson(PlayerEntity player, out Skill skill, out int item)
    {
        skill = default; item = 0;
        if (player == null || player.HasFlag(ReturnFlag)) return false;
        foreach (var flag in player.GetAllFlags())
        {
            if (!flag.StartsWith(LessonFlag)) continue;
            var parts = flag.Substring(LessonFlag.Length).Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out int savedSkill) && int.TryParse(parts[1], out item))
            {
                skill = (Skill)savedSkill;
                return true;
            }
        }
        return false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("StarterGuide (auto)");
        _instance = go.AddComponent<StarterGuide>();
        DontDestroyOnLoad(go);
        SceneManager.sceneLoaded += (_, __) => { if (_instance != null) _instance.ResetForScene(); };
    }

    /// <summary>RoxyNPC calls this each time she hands out a lesson: the camera glides over to where it's
    /// done, and the objective marks that same spot.</summary>
    public static void LessonGiven(Skill skill, int giveItemId)
    {
        if (_instance != null) _instance.StartLesson(skill, giveItemId, peek: true);
    }

    public static void AwaitCombatTraining()
    {
        Remember(RoxyNPC.CombatTrainingFlag);
        if (_instance == null) return;
        _instance._lesson = null;
        _instance._returnToRoxy = false;
        _instance._combatStatus = null;
    }

    void ResetForScene()
    {
        _roxy = null; _welcomed = false; _lesson = null; _returnToRoxy = false; _restored = false;
        _combatStatus = null;
        Unhook();
        QuestGuide.Clear();
    }

    void Update()
    {
        var player = PlayerEntity.Instance;
        if (player == null) return;
        if (_roxy == null)
        {
            if (Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + 2f;
            _roxy = FindAnyObjectByType<RoxyNPC>(FindObjectsInactive.Include);
            if (_roxy == null) return;
        }

        if (!player.HasFlag(MetFlag))
        {
            if (!_welcomed && Time.timeSinceLevelLoad > 2f) { _welcomed = true; StartCoroutine(Welcome()); }
            if (QuestGuide.Target != _roxy.transform) QuestGuide.Show("Talk to Roxy", _roxy.transform);
            return;
        }

        // Back in a saved game: pick up the lesson (or "return to Roxy") where you left it. Waits a moment
        // for the save to land on the player, like the welcome does.
        if (!_restored && Time.timeSinceLevelLoad > 2f) { _restored = true; Restore(player); }

        if (player.HasFlag(RoxyNPC.CombatTrainingFlag))
        {
            bool ready = RoxyNPC.ReadyForFission(player.Stats);
            string text = ready ? "Return to Roxy for your Geiger Counter and Fission lesson"
                : "Train all four combat skills: " + RoxyNPC.CombatProgress(player.Stats);
            if (_combatStatus != text)
            {
                _combatStatus = text;
                QuestGuide.Show(text, ready ? _roxy.transform : null);
            }
            return;
        }

        if (_lesson == Skill.Beastmastery && Time.unscaledTime >= _nextBeastGuideAt)
        {
            _nextBeastGuideAt = Time.unscaledTime + 1f;
            UpdateBeastObjective(player);
        }
        if (_returnToRoxy && QuestGuide.Target != _roxy.transform) QuestGuide.Show("Return to Roxy for your next lesson", _roxy.transform);
    }

    IEnumerator Welcome()
    {
        bool touch = Application.isMobilePlatform;
        QuestGuide.Banner("Welcome to the Broken Crescent",
            $"Roxy has work for you — she's just up the road. {(touch ? "Tap" : "Click")} her to talk.");
        yield return new WaitForSeconds(3f);
        if (_roxy != null && QuestCam.Instance != null)
            QuestCam.Instance.Peek(_roxy.transform.position + Vector3.up * 1.2f, touch
                ? "That's Roxy. Tap her to talk — and hold your finger on anything to see everything you can do with it."
                : "That's Roxy. Click her to talk — and right-click anything to see everything you can do with it.");
    }

    void UpdateBeastObjective(PlayerEntity player)
    {
        if (!player.HasFlag("egg_chosen"))
        {
            QuestGuide.Show("Talk to Roxy to choose your beast companion", _roxy.transform);
            return;
        }
        if (CompanionManager.Instance == null || !CompanionManager.Instance.IsActive)
        {
            QuestGuide.Show("Summon your beast, then hunt a creature from your beast task", null);
            return;
        }
        var task = BeastTasks.Current;
        if (task == null) { QuestGuide.Show("Your beast is finding a hunt", null); return; }
        Transform nearest = null;
        float distance = float.PositiveInfinity;
        foreach (var target in Object.FindObjectsByType<CombatTarget>(FindObjectsSortMode.None))
        {
            if (!BeastTasks.Huntable(target) || target.IsDead || !BeastTasks.MatchesCurrentTask(target)) continue;
            float d = (target.transform.position - player.transform.position).sqrMagnitude;
            if (d < distance) { distance = d; nearest = target.transform; }
        }
        QuestGuide.Show($"Hunt {task.creature} with your beast to earn Beastmastery XP", nearest);
    }

    void StartLesson(Skill skill, int giveItemId, bool peek)
    {
        _restored = true;
        _returnToRoxy = false;
        _lesson = skill;
        _lessonItem = giveItemId;
        Hook();
        var from = PlayerEntity.Instance != null ? PlayerEntity.Instance.transform.position : Vector3.zero;
        Transform target = skill switch
        {
            Skill.Smithing when giveItemId == ScrapBarId => QuestCam.NearestStation(StationType.Workbench, from),
            Skill.Scrapping when giveItemId == FissionItems.GeigerCounter => null,   // the golems roam; the Geiger counter leads
            Skill.Refinement => QuestCam.NearestStation(StationType.Furnace, from),
            Skill.Fission => null,
            Skill.Beastmastery => null,
            _ => QuestCam.TargetForSkill(skill, giveItemId),
        };
        QuestGuide.Show(LessonText(skill, giveItemId), target);
        if (peek && target != null && QuestCam.Instance != null)
            QuestCam.Instance.Peek(target.position, skill == Skill.Smithing && giveItemId == ScrapBarId
                ? "Take the bar to the workbench and forge your armour."
                : QuestCam.DefaultCaption(skill));
        Remember(LessonFlag + (int)skill + ":" + giveItemId);
    }

    void Restore(PlayerEntity p)
    {
        if (p.HasFlag(RoxyNPC.CombatTrainingFlag)) { _combatStatus = null; return; }
        if (p.HasFlag(ReturnFlag)) { _returnToRoxy = true; return; }
        string saved = null;
        foreach (var f in p.GetAllFlags()) if (f.StartsWith(LessonFlag)) { saved = f; break; }
        if (saved == null) return;
        var parts = saved.Substring(LessonFlag.Length).Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out int skill) && int.TryParse(parts[1], out int item))
            StartLesson((Skill)skill, item, peek: false);
    }

    /// <summary>Keeps where the guide is pointing in the player's saved flags (one at a time).</summary>
    static void Remember(string flag)
    {
        var p = PlayerEntity.Instance;
        if (p == null) return;
        var old = new List<string>();
        foreach (var f in p.GetAllFlags()) if (f.StartsWith(LessonFlag) || f == ReturnFlag) old.Add(f);
        foreach (var f in old) p.RemoveFlag(f);
        p.SetFlag(flag);
    }

    static string LessonText(Skill skill, int giveItemId) => skill switch
    {
        Skill.Fishing      => "Catch a fish at the fishing spot",
        Skill.Woodcutting  => "Chop down a dead tree",
        Skill.Scrapping when giveItemId == FissionItems.GeigerCounter => "Use the Geiger Counter, defeat an Earth Golem, then scrap its body for raw cores",
        Skill.Refinement => "Refine your raw fission cores at a furnace",
        Skill.Fission => "Equip power gauntlets and refined cores as ammo; deal Fission damage",
        Skill.Scrapping    => "Smash the rubble pile for scrap",
        Skill.Cooking      => "Cook your catch on the cooking fire",
        Skill.Smithing when giveItemId == ScrapBarId => "Forge scrap armour at the workbench",
        Skill.Smithing     => "Smelt your scrap into a bar at the furnace",
        Skill.Beastmastery => "Choose your beast companion",
        _                  => "Do Roxy's lesson",
    };

    void FinishLesson()
    {
        _lesson = null;
        _returnToRoxy = true;
        Remember(ReturnFlag);
        QuestGuide.Banner("Lesson complete", "Head back to Roxy for the next one.", 3f);
        if (_roxy != null) QuestGuide.Show("Return to Roxy for your next lesson", _roxy.transform);
    }

    void OnXp(Skill skill, int amount)
    {
        if (_lesson != skill) return;
        if (_lessonItem == FissionItems.GeigerCounter)
        {
            var p = PlayerEntity.Instance;
            if (skill == Skill.Scrapping)
            {
                for (int tier = 1; tier <= 5; tier++)
                    if (p.Inventory.Contains(FissionItems.RawCore(tier)))
                    { StartLesson(Skill.Refinement, _lessonItem, false); return; }
                return; // Ordinary junk does not complete the golem lesson.
            }
            if (skill == Skill.Refinement) { StartLesson(Skill.Fission, _lessonItem, false); return; }
        }
        FinishLesson();
    }

    void Hook()
    {
        var p = PlayerEntity.Instance;
        if (p == null || p.Stats == null || p.Stats == _stats) return;
        Unhook();
        _stats = p.Stats;
        _stats.OnXPGained += OnXp;
    }

    void Unhook()
    {
        if (_stats != null) _stats.OnXPGained -= OnXp;
        _stats = null;
    }
}
