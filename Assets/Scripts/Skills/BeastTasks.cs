using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Beastmastery works like RuneScape's Slayer: once you own a beast you are always on a BEAST TASK —
/// "kill N of one kind of creature" — and those kills are how the skill trains.
///
///   • The task's tier follows your Beastmastery level, in the same 20-level brackets every skill's
///     tiers use: 1-19 → tier 1, 20-39 → 2, 40-59 → 3, 60-79 → 4, 80+ → 5. The creature is picked
///     from what actually lives in the loaded world at that tier (nearest lower tier if none), so a
///     task is never impossible where you are.
///   • A kill only counts while your beast is OUT with you (B to summon). Kills without it are
///     ignored, with a reminder.
///   • Each counted kill gives Beastmastery XP equal to the creature's max HP (Slayer's rule).
///     Finishing adds a 10% bonus, and the next task is handed out automatically a moment later.
///   • Travelled somewhere your target doesn't live? Any creature of the task's tier counts there.
///
/// Progress is stored in a single save flag ("bmtask|…"), so it survives save/load and scene
/// changes with no SaveData change. Self-bootstrapping — no scene setup.
/// </summary>
public class BeastTasks : MonoBehaviour
{
    public static BeastTasks Instance { get; private set; }

    /// <summary>Raised when a whole task's count is finished (not on each kill). Roxy's hunting lesson
    /// ends here, so one kill can't mark it done while the rest of the count is still outstanding.</summary>
    public static event System.Action TaskCompleted;

    const string FlagPrefix    = "bmtask|";
    const float  KillRange     = 80f;    // a kill further than this from you isn't yours
    const float  NextTaskDelay = 4f;     // pause after finishing before the next assignment
    const float  BonusFraction = 0.10f;  // completion bonus, as a share of the task's kill XP
    const float  RescanSeconds = 30f;    // spawners can add creatures after load

    // Kills per task, by tier (inclusive). Index 0 unused.
    static readonly int[] MinKills = { 0, 10, 12, 15, 18, 20 };
    static readonly int[] MaxKills = { 0, 15, 20, 25, 30, 35 };

    /// <summary>The live task, parsed from the save flag.</summary>
    public class Task
    {
        public string creature;   // normalised creature name — the match key
        public int tier, done, required, xpPerKill;
    }

    class Kind { public string name; public int tier, maxHP, count; }
    readonly Dictionary<string, Kind> _kinds = new();   // huntable creatures in the loaded world

    float _nextTick, _nextScanAt, _nextAssignAt, _nextHintAt;
    bool  _remindAfterScan = true;
    string _lastCreature;   // avoid handing out the same creature twice in a row

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("BeastTasks (auto)").AddComponent<BeastTasks>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);   // one task tracker across scene changes
    }

    void OnEnable()
    {
        CombatTarget.AnyKilled += OnKilled;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        CombatTarget.AnyKilled -= OnKilled;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _nextScanAt = 0f;          // re-read what lives in the new area
        _remindAfterScan = true;   // and tell the player where their task stands
    }

    // ── public: rules, HUD, queries ──────────────────────────────────────

    /// <summary>Beastmastery level → task tier (1-5).</summary>
    public static int TierForLevel(int level) => Mathf.Clamp(1 + level / 20, 1, 5);

    public static bool OwnsBeast => PlayerEntity.Instance != null && PlayerEntity.Instance.HasFlag("pup_hatched");
    static bool BeastOut => CompanionManager.Instance != null && CompanionManager.Instance.IsActive;

    /// <summary>The current task, or null when there isn't one.</summary>
    public static Task Current => Read(PlayerEntity.Instance);

    public static bool MatchesCurrentTask(CombatTarget target)
    {
        var task = Current;
        if (task == null || target == null) return false;
        return CreatureName(target) == task.creature ||
            (Instance != null && !Instance._kinds.ContainsKey(task.creature) &&
             Mathf.Clamp(target.LootTier, 1, 5) == task.tier);
    }

    /// <summary>HUD row for the task: `label` = what to hunt (or a status line), `value` = progress
    /// ("3/12", empty when there's no task). False = hide the row (no beast yet). `alert` = the beast
    /// isn't out, so kills won't count right now.</summary>
    public static bool HudInfo(out string label, out string value, out float fill, out bool alert)
    {
        label = ""; value = ""; fill = 0f; alert = false;
        if (!OwnsBeast) return false;

        var t = Current;
        if (t == null)
        {
            bool nothingHere = Instance != null && Instance._kinds.Count == 0;
            label = nothingHere ? "Nothing to hunt here" : "Finding a hunt…";
            return true;
        }
        fill  = t.required > 0 ? Mathf.Clamp01((float)t.done / t.required) : 0f;
        alert = !BeastOut;
        label = t.creature;
        value = $"{t.done}/{t.required}";
        return true;
    }

    /// <summary>A creature's task name: its object name without clone/instance-number noise, so
    /// "Frog Marauder", "Frog Marauder (2)" and "Frog Marauder(Clone)" are the same kind.</summary>
    public static string CreatureName(CombatTarget ct)
    {
        string n = ct != null ? ct.gameObject.name : "";
        n = n.Replace("(Clone)", "").Trim();
        while (true)
        {
            string before = n;
            if (n.EndsWith(")"))
            {
                int open = n.LastIndexOf('(');
                if (open >= 0 && IsDigits(n.Substring(open + 1, n.Length - open - 2)))
                    n = n.Substring(0, open).Trim();
            }
            int space = n.LastIndexOf(' ');
            if (space > 0 && IsDigits(n.Substring(space + 1))) n = n.Substring(0, space).Trim();
            if (n == before) break;
        }
        n = n.Replace('|', '/');   // '|' is the save-flag separator
        return string.IsNullOrEmpty(n) ? "Creature" : n;
    }

    /// <summary>Ordinary creatures only: no training dummies, bosses, minibosses, the God-Hunter or
    /// the Elemental Golems (those are the Fission loop's quarry).</summary>
    public static bool Huntable(CombatTarget ct) =>
        ct != null && !ct.isDummy && !ct.isBoss && !ct.isMiniBoss && !ct.isGodHunter
        && ct.GetComponent<FissionGolem>() == null;

    // ── loop ─────────────────────────────────────────────────────────────

    void Update()
    {
        if (Time.unscaledTime < _nextTick) return;
        _nextTick = Time.unscaledTime + 1f;

        var p = PlayerEntity.Instance;
        if (p == null) return;

        if (Time.unscaledTime >= _nextScanAt)
        {
            Scan();
            _nextScanAt = Time.unscaledTime + RescanSeconds;
            if (_remindAfterScan) { _remindAfterScan = false; Remind(p); }
        }

        if (!OwnsBeast) return;
        if (Read(p) != null) return;

        // Anything left behind by a bad/old flag is cleared so a fresh task can be handed out.
        ClearTaskFlags(p);
        if (Time.unscaledTime >= _nextAssignAt && !Assign(p))
            _nextAssignAt = Time.unscaledTime + 10f;   // nothing huntable here yet — try again soon
    }

    void OnKilled(CombatTarget ct)
    {
        var p = PlayerEntity.Instance;
        if (p == null || !OwnsBeast || !Huntable(ct)) return;
        var t = Read(p);
        if (t == null) return;

        Vector3 d = ct.transform.position - p.transform.position;
        if (d.sqrMagnitude > KillRange * KillRange) return;

        if (!MatchesCurrentTask(ct)) return;

        if (!BeastOut)
        {
            if (Time.unscaledTime >= _nextHintAt)
            {
                _nextHintAt = Time.unscaledTime + 30f;
                Say("That kill didn't count — your beast has to be out with you. Press <b>B</b> to summon it.");
            }
            return;
        }

        int xp = Mathf.Max(1, ct.maxHP);
        t.done++;
        p.Stats.AddXP(Skill.Beastmastery, xp);

        if (t.done >= t.required)
        {
            int bonus = Mathf.Max(1, Mathf.RoundToInt(t.required * t.xpPerKill * BonusFraction));
            p.Stats.AddXP(Skill.Beastmastery, bonus);
            ClearTaskFlags(p);
            _lastCreature = t.creature;
            _nextAssignAt = Time.unscaledTime + NextTaskDelay;
            Say($"<color=#80FF80>Task complete!</color> {t.required} × {t.creature} hunted down with your beast. " +
                $"Bonus <b>+{bonus}</b> Beastmastery XP — your next task is on its way.");
            TaskCompleted?.Invoke();
        }
        else
        {
            Write(p, t);
            Say($"{t.done}/{t.required} {t.creature}  (+{xp} Beastmastery XP)");
        }
    }

    // ── assignment ───────────────────────────────────────────────────────

    void Scan()
    {
        _kinds.Clear();
        foreach (var ct in Object.FindObjectsByType<CombatTarget>(FindObjectsInactive.Include))
        {
            if (!Huntable(ct)) continue;
            string n = CreatureName(ct);
            if (!_kinds.TryGetValue(n, out var k))
                _kinds[n] = k = new Kind { name = n, tier = Mathf.Clamp(ct.LootTier, 1, 5), maxHP = Mathf.Max(1, ct.maxHP) };
            k.count++;
        }
    }

    bool Assign(PlayerEntity p)
    {
        if (_kinds.Count == 0) return false;

        int want = TierForLevel(p.Stats.GetLevel(Skill.Beastmastery));
        int tier = PickTier(want);

        var pool = new List<Kind>();
        foreach (var k in _kinds.Values) if (k.tier == tier) pool.Add(k);
        if (pool.Count > 1) pool.RemoveAll(k => k.name == _lastCreature);   // variety when there's a choice

        int total = 0;
        foreach (var k in pool) total += k.count;
        int roll = Random.Range(0, total);
        var pick = pool[0];
        foreach (var k in pool) { if (roll < k.count) { pick = k; break; } roll -= k.count; }

        var t = new Task
        {
            creature  = pick.name,
            tier      = tier,
            done      = 0,
            required  = Random.Range(MinKills[tier], MaxKills[tier] + 1),
            xpPerKill = pick.maxHP,
        };
        Write(p, t);

        string note = tier < want ? $" (nothing of tier {want} lives around here, so it's a tier {tier} hunt)" : "";
        Say($"New beast task: kill <b>{t.required} × {t.creature}</b> with your beast at your side — " +
            $"+{t.xpPerKill} Beastmastery XP per kill{note}.");
        if (!BeastOut) Say("Summon your beast with <b>B</b> — kills only count while it's out with you.");
        return true;
    }

    /// <summary>The wanted tier if anything of it lives here, else the nearest lower one, else the
    /// nearest higher one.</summary>
    int PickTier(int want)
    {
        bool Has(int tier) { foreach (var k in _kinds.Values) if (k.tier == tier) return true; return false; }
        if (Has(want)) return want;
        for (int t = want - 1; t >= 1; t--) if (Has(t)) return t;
        for (int t = want + 1; t <= 5; t++) if (Has(t)) return t;
        return want;
    }

    void Remind(PlayerEntity p)
    {
        if (!OwnsBeast) return;
        var t = Read(p);
        if (t == null) return;
        string where = _kinds.ContainsKey(t.creature) ? ""
            : $" They don't live around here — any tier {t.tier} creature counts while you're in this area.";
        Say($"Current beast task: {t.done}/{t.required} {t.creature}.{where}");
    }

    // ── save flag ────────────────────────────────────────────────────────

    static Task Read(PlayerEntity p)
    {
        if (p == null) return null;
        foreach (var f in p.GetAllFlags())
        {
            if (!f.StartsWith(FlagPrefix)) continue;
            var parts = f.Split('|');
            if (parts.Length < 6) return null;
            if (!int.TryParse(parts[2], out int tier) || !int.TryParse(parts[3], out int done) ||
                !int.TryParse(parts[4], out int req)  || !int.TryParse(parts[5], out int xp)) return null;
            if (string.IsNullOrEmpty(parts[1]) || req <= 0 || done >= req) return null;
            return new Task { creature = parts[1], tier = Mathf.Clamp(tier, 1, 5), done = Mathf.Max(0, done),
                              required = req, xpPerKill = Mathf.Max(1, xp) };
        }
        return null;
    }

    static void Write(PlayerEntity p, Task t)
    {
        ClearTaskFlags(p);
        p.SetFlag($"{FlagPrefix}{t.creature}|{t.tier}|{t.done}|{t.required}|{t.xpPerKill}");
    }

    static void ClearTaskFlags(PlayerEntity p)
    {
        var stale = new List<string>();
        foreach (var f in p.GetAllFlags()) if (f.StartsWith(FlagPrefix)) stale.Add(f);
        foreach (var f in stale) p.RemoveFlag(f);
    }

    static bool IsDigits(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s) if (!char.IsDigit(c)) return false;
        return true;
    }

    static void Say(string m) => HUDController.Emit("<color=#E0A040>[BEAST TASK]:</color> " + m);
}
