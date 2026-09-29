using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns an Elemental Golem into a fission node the geiger counter hunts. While ALIVE it is a normal
/// combat enemy (Enemy3D + CombatTarget) that the geiger points you toward. When you KILL it, its
/// corpse becomes a harvestable node — the raw fission cores are gathered from the dead body with the
/// existing ResourceNode gather loop.
///
/// Put this on each golem prefab (once the ElementalGolemPack is imported) and set the tier. It needs
/// nothing else wired — it registers itself for the geiger and converts on death automatically.
///
/// One loop per tier:
///   geiger → living golem → fight & kill → harvest corpse → corpse depletes → golem gone
/// Respawning a fresh golem elsewhere is the job of a spawner (added once the prefabs are in), so the
/// geiger always has a next target.
/// </summary>
[RequireComponent(typeof(CombatTarget))]
public class FissionGolem : MonoBehaviour
{
    [Tooltip("World tier 1-5. Sets which raw core drops and which golem this is meant to be.")]
    [Range(1, 5)] public int tier = 1;

    [Tooltip("Skill the corpse harvest trains and gates on. Stripping a corpse is MINING — it pays " +
             "Scrapping at the same rate as an ordinary node of that tier. Refining the cores " +
             "afterwards is what trains Refinement.")]
    public Skill harvestSkill = Skill.Scrapping;

    [Tooltip("Skill level needed to harvest this tier's corpse. Leave 0 to derive it from the tier " +
             "(1/20/40/60/80) — the spawner builds golems at runtime, so derivation is the norm.")]
    public int harvestLevelRequired;

    [Tooltip("Raw cores the corpse yields before it's stripped (rolled fresh per kill). Averages 9, " +
             "so the walk between golems is not the bottleneck — Refinement is meant to be a long " +
             "grind, but a travel-bound one would just be dead time.")]
    public int minYield = 3, maxYield = 15;

    [Tooltip("XP per core harvested. Leave 0 to derive it from the tier.")]
    public int xpPerCore;

    // ── per-tier progression, index 0 = tier 1 ───────────────────────────────
    // Gates and XP match the ORDINARY scrap nodes of the same tier exactly (see CreateTieredNodes:
    // Junk Pile 1/25 → Abandoned Robotics 80/170). Stripping a golem is just mining, so it pays
    // like mining; the reason to do it is the cores, not the Scrapping XP.
    static readonly int[] TierLevel  = { 1, 20, 40, 60, 80 };
    static readonly int[] TierXp     = { 25, 50, 80, 120, 170 };

    int TierIndex => Mathf.Clamp(tier, 1, 5) - 1;

    /// <summary>Level gate for this golem's corpse — the serialized override if set, else by tier.</summary>
    public int EffectiveHarvestLevel =>
        harvestLevelRequired > 0 ? harvestLevelRequired : TierLevel[TierIndex];

    /// <summary>XP per raw core — the serialized override if set, else by tier.</summary>
    public int EffectiveXpPerCore =>
        xpPerCore > 0 ? xpPerCore : TierXp[TierIndex];

    /// <summary>Every fission golem currently in the world — living or a still-harvestable corpse.
    /// The geiger reads this to find the nearest target.</summary>
    public static readonly List<FissionGolem> Active = new();

    public bool IsCorpse { get; private set; }
    public Vector3 Position => transform.position;

    CombatTarget _ct;
    ResourceNode _node;

    void OnEnable()  { if (!Active.Contains(this)) Active.Add(this); }
    void OnDisable() => Active.Remove(this);

    void Awake()
    {
        _ct = GetComponent<CombatTarget>();
        if (_ct != null) _ct.OnDied += BecomeCorpse;
    }

    void OnDestroy()
    {
        if (_ct != null) _ct.OnDied -= BecomeCorpse;
        Active.Remove(this);
    }

    /// <summary>Death → harvestable. Stop being an enemy, start being a resource node.</summary>
    void BecomeCorpse()
    {
        if (IsCorpse) return;
        IsCorpse = true;

        // Silence the combatant so the corpse doesn't chase or swing.
        var enemy = GetComponent<Enemy3D>();
        if (enemy != null) enemy.enabled = false;
        var motion = GetComponent<CreatureMotion>();
        if (motion != null) motion.enabled = false;
        var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null && agent.isActiveAndEnabled) agent.isStopped = true;

        // Drive the harvest through the existing ResourceNode loop — same gathering the player
        // already knows (click to gather, per-tick rolls, inventory add, XP, deplete → gone).
        _node = gameObject.GetComponent<ResourceNode>() ?? gameObject.AddComponent<ResourceNode>();
        _node.skill              = harvestSkill;
        _node.requiredToolId     = 3;
        _node.levelRequired      = EffectiveHarvestLevel;
        _node.dropItemId         = FissionItems.RawCore(tier);
        _node.dropQuantity       = 1;                      // one core per successful strip
        _node.xpPerAction        = EffectiveXpPerCore;
        // Design rule: higher gathering levels cut the grind, they don't just reroll it. Every 20
        // levels of the harvest skill raises the GUARANTEED floor by 2 cores (the ceiling stays put),
        // so a level-80+ stripper never walks away with the bottom of the roll.
        int lvl = PlayerEntity.Instance != null ? PlayerEntity.Instance.Stats.GetLevel(harvestSkill) : 1;
        _node.minYield           = Mathf.Min(maxYield, minYield + (lvl / 20) * 2);
        _node.maxYield           = maxYield;
        _node.ResetYield(); // AddComponent ran Awake before the corpse's yield was configured.
        _node.respawnTicks       = int.MaxValue;   // this corpse never respawns; a spawner makes a new golem
        _node.ticksPerCycle      = 4;
        _node.attemptsPerCycle   = 2;
        _node.successChance      = 256;
        _node.displayNameOverride = $"{name} Corpse";
        _node.examineOverride     = "The reactor at its core is exposed. Strip it for fission slag.";

        // When the corpse is stripped clean, remove it so the geiger stops pointing here.
        _node.OnDepleted += () => { Active.Remove(this); Destroy(gameObject, 1.5f); };
    }
}
