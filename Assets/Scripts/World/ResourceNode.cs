using System;
using UnityEngine;

// NOTE: stored as an integer index on scene nodes/prefabs — only APPEND new members.
public enum ResourceNodeType { RubblePile, WoodenDebris, WaterBarrel, RatNest, SulphurVent }

/// <summary>Outcome of one gather cycle (a batch of attempts on a single tick boundary).</summary>
public enum GatherCycleResult { Stop, NoCatch, Caught }

public class ResourceNode : MonoBehaviour, ITickable, IExaminable
{
    [Header("Node Config")]
    [SerializeField] public ResourceNodeType nodeType;
    [SerializeField] public Skill skill;
    [SerializeField] public int levelRequired = 1;
    [SerializeField] public int requiredToolId;
    [SerializeField] public int dropItemId;
    [Tooltip("How many of dropItemId a single successful gather yields. 1 for everything that " +
             "doesn't stack (one item = one slot); bulk resources like Sulphur set this higher.")]
    [SerializeField] public int dropQuantity = 1;
    [SerializeField] public int xpPerAction = 25;
    [SerializeField] public int respawnTicks = 10;
    [Tooltip("Legacy/unused — yield is now the random minYield..maxYield roll below. Kept so old tools/prefabs still serialize.")]
    [SerializeField] public int maxHits = 3;

    [Header("Yield — each node (and each respawn) rolls a fresh random amount in this range")]
    [SerializeField] public int minYield = 1;
    [SerializeField] public int maxYield = 8;

    [Header("Optional per-tier flavor (overrides the ResourceNodeType defaults when set)")]
    [SerializeField] public string displayNameOverride;
    [SerializeField] public string examineOverride;
    [Tooltip("Optional reachable place to stand (for example the bank beside a fishing ripple).")]
    public Transform interactionPoint;
    public Vector3 InteractionPosition => interactionPoint != null ? interactionPoint.position : transform.position;

    /// <summary>The point the player faces while working this node: the middle of the visible model.
    /// A model's pivot is often off to one side or at a corner, so aiming at transform.position could
    /// turn the player away from the thing being chopped or mined. Fishing spots face the water spot.</summary>
    public Vector3 FacingPosition
    {
        get
        {
            if (skill == Skill.Fishing || _renderers == null) return transform.position;
            Bounds visual = default;
            bool any = false;
            foreach (var r in _renderers)
            {
                if (r == null || !r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                if (!any) { visual = r.bounds; any = true; }
                else visual.Encapsulate(r.bounds);
            }
            return any ? visual.center : transform.position;
        }
    }

    [Tooltip("Shown instead of the node while it's depleted (a tree's stump). Leave empty and the node just vanishes until it respawns.")]
    [SerializeField] public GameObject depletedVisual;

    [Header("Tick-based rolls (OSRS-style). Leave 0 to use the safe defaults below.")]
    [Tooltip("Game ticks between each roll cycle (0.6s each). Woodcutting=4, Fishing=5, mining/scrapping basic tool=8. 0 → 4.")]
    [SerializeField] public int ticksPerCycle = 4;
    [Tooltip("Success rolls made each cycle. The FIRST success yields one resource — more attempts = faster effective gathering (better pickaxes). Mining: bronze=7, steel=5, mithril/rune=4. 0 → 1.")]
    [SerializeField] public int attemptsPerCycle = 1;
    [Tooltip("FLAT per-attempt success chance out of 256 (256 = always). Used only when the level-scaled chance below is left at 0. 0 → 256 (guaranteed).")]
    [Range(0, 256)] [SerializeField] public int successChance = 256;

    [Header("Level-scaled chance (OSRS-style: interpolates linearly, lvl 1 → lvl 99). Set the lvl-99 value > 0 to enable; otherwise the flat chance above is used.")]
    [Tooltip("Per-attempt success chance (out of 256) at skill level 1.")]
    [Range(0, 256)] [SerializeField] public int chanceAtLevel1;
    [Tooltip("Per-attempt success chance (out of 256) at skill level 99. Leave 0 to disable level scaling and use the flat chance.")]
    [Range(0, 256)] [SerializeField] public int chanceAtLevel99;

    [Header("Optional better-tool bonus (sacred-clay harpoon / higher-tier pickaxe)")]
    [Tooltip("Item id of an upgraded tool. When held or equipped, the values below override the base ones. Any field left ≤0 keeps the base value.")]
    [SerializeField] public int betterToolId;
    [SerializeField] public int betterToolTicksPerCycle;
    [SerializeField] public int betterToolAttemptsPerCycle;
    [Range(0, 256)] [SerializeField] public int betterToolSuccessChance;

    private int _hitsRemaining;
    private int _respawnCountdown;

    public bool IsDepleted => _hitsRemaining <= 0;

    // ── IExaminable ──────────────────────────────────────────────────────
    public string DisplayName => !string.IsNullOrEmpty(displayNameOverride) ? displayNameOverride : nodeType switch
    {
        ResourceNodeType.RubblePile   => "Rubble Pile",
        ResourceNodeType.WoodenDebris => "Wooden Debris",
        ResourceNodeType.WaterBarrel  => "Fishing Spot",
        ResourceNodeType.RatNest      => "Rat Nest",
        ResourceNodeType.SulphurVent  => "Sulphur Vent",
        _                     => "Resource Node"
    };

    public string ExamineText
    {
        get
        {
            if (IsDepleted) return $"A {DisplayName.ToLower()} — picked clean for now.";
            if (!string.IsNullOrEmpty(examineOverride)) return examineOverride;
            return nodeType switch
            {
                ResourceNodeType.RubblePile   => "A pile of broken concrete. Worth breaking apart for scrap.",
                ResourceNodeType.WoodenDebris => "Splintered planks and pallets. Good for salvaging wood.",
                ResourceNodeType.WaterBarrel  => "A murky pool. Cast a line to catch some shrimp.",
                ResourceNodeType.RatNest      => "A filthy nest. Something stirs inside...",
                ResourceNodeType.SulphurVent  => "A crust of yellow crystal around a hissing vent. Ammo starts here.",
                _                     => "A scavengeable resource."
            };
        }
    }

    public event Action OnDepleted;
    public event Action OnRespawned;

    private Renderer[] _renderers;
    private Collider[] _colliders3D;

    /// <summary>Roll a fresh random yield (1–8 by default) so no two nodes — and no
    /// two respawns — give the same amount.</summary>
    private void RollYield()
    {
        int lo = Mathf.Max(1, minYield);
        int hi = Mathf.Max(lo, maxYield);
        _hitsRemaining = UnityEngine.Random.Range(lo, hi + 1);   // inclusive
    }

    public void ResetYield() => RollYield();

    void Awake()
    {
        RollYield();
        // Fishing spots are bubbles on the water — built before the renderers are gathered, so they hide
        // with the spot while it's fished out.
        if (skill == Skill.Fishing) FishingBubbles.Apply(transform);
        // The stump (if any) isn't part of the node's own look — it swaps in while depleted.
        _renderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(true), r => !UnderStump(r.transform));
        _colliders3D = System.Array.FindAll(GetComponentsInChildren<Collider>(true), c => !UnderStump(c.transform));
        if (depletedVisual != null) depletedVisual.SetActive(false);
    }

    bool UnderStump(Transform t) => depletedVisual != null && t.IsChildOf(depletedVisual.transform);

    /// <summary>Hide the node (renderer + collider) while depleted — showing its stump instead, if it
    /// has one — and bring it back on respawn.</summary>
    private void SetVisible(bool visible)
    {
        if (_renderers != null)
            foreach (var r in _renderers) if (r != null) r.enabled = visible;
        if (_colliders3D != null)
            foreach (var c in _colliders3D) if (c != null) c.enabled = visible;
        if (depletedVisual != null) depletedVisual.SetActive(!visible);
    }

    void Start() => GameTick.Instance.Register(this);
    void OnDestroy() => GameTick.Instance?.Unregister(this);

    public void OnTick(long tickCount)
    {
        if (!IsDepleted) return;
        _respawnCountdown--;
        if (_respawnCountdown <= 0)
        {
            RollYield();
            SetVisible(true);
            OnRespawned?.Invoke();
        }
    }

    // ── tick cadence / tool bonus ─────────────────────────────────────────
    bool HasBetterTool(PlayerEntity player) =>
        betterToolId > 0
        && player != null
        && (player.Inventory.Contains(betterToolId) || player.Equipment.GetItemId("Tool") == betterToolId);

    /// <summary>Ticks between roll cycles, accounting for an equipped better tool. (≤0 fields fall back to base/defaults.)</summary>
    public int CurrentTicksPerCycle(PlayerEntity player)
    {
        if (HasBetterTool(player) && betterToolTicksPerCycle > 0) return betterToolTicksPerCycle;
        return ticksPerCycle > 0 ? ticksPerCycle : 4;
    }

    int CurrentAttempts(PlayerEntity player)
    {
        if (HasBetterTool(player) && betterToolAttemptsPerCycle > 0) return betterToolAttemptsPerCycle;
        return attemptsPerCycle > 0 ? attemptsPerCycle : 1;
    }

    int CurrentChance(PlayerEntity player)
    {
        // A held better tool (e.g. sacred-clay harpoon) overrides everything when set.
        if (HasBetterTool(player) && betterToolSuccessChance > 0) return betterToolSuccessChance;

        // OSRS-style: interpolate the per-attempt chance linearly from level 1 → 99.
        // Enabled only when chanceAtLevel99 is configured; otherwise fall back to the flat chance.
        if (chanceAtLevel99 > 0)
        {
            int lvl = player != null ? player.Stats.GetLevel(skill) : 1;
            float t = Mathf.Clamp01((lvl - 1) / 98f);
            int c = Mathf.RoundToInt(Mathf.Lerp(chanceAtLevel1, chanceAtLevel99, t));
            return Mathf.Clamp(c, 1, 256);
        }

        return successChance > 0 ? successChance : 256;
    }

    /// <summary>
    /// Runs one roll cycle: up to <c>attempts</c> success rolls, stopping at the first hit,
    /// which yields a single resource (OSRS-style). Returns Stop if gathering can't continue
    /// (wrong tool, level too low, pack full, depleted), NoCatch if all rolls missed, or
    /// Caught on a successful gather.
    /// </summary>
    public (GatherCycleResult result, string message) TryGatherCycle(PlayerEntity player)
    {
        if (IsDepleted) return (GatherCycleResult.Stop, "Nothing left to gather here.");

        if (!GatheringTools.CanUse(player, requiredToolId))
        {
            string toolName = ItemRegistry.Get(requiredToolId)?.name ?? "the right tool";
            return (GatherCycleResult.Stop, $"You need a {toolName} or a usable upgrade to do that.");
        }

        if (player.Stats.GetLevel(skill) < levelRequired)
            return (GatherCycleResult.Stop, $"Requires {skill} level {levelRequired}.");

        if (player.Inventory.IsFull())
            return (GatherCycleResult.Stop, "Your inventory is full.");

        int attempts = CurrentAttempts(player);
        int chance   = CurrentChance(player);
        bool caught  = false;
        for (int i = 0; i < attempts; i++)
        {
            if (UnityEngine.Random.Range(0, 256) < chance) { caught = true; break; }
        }
        if (!caught) return (GatherCycleResult.NoCatch, null);   // missed this cycle — keep trying, no chat spam

        // Clamped so a prefab serialized before dropQuantity existed (or zeroed by hand) still
        // yields something instead of silently gathering nothing.
        int qty = Mathf.Max(1, dropQuantity);
        if ((ItemRegistry.Get(dropItemId).stackable || player.Inventory.FreeSlots() > qty) &&
            UnityEngine.Random.value < 0.10f * ModuleCatalog.Power(player.Equipment, ModuleCatalog.Effect.Gather)) qty++;
        if (!player.Inventory.Add(dropItemId, qty))
            return (GatherCycleResult.Stop, "There is no room for this resource.");
        player.Stats.AddXP(skill, xpPerAction);

        _hitsRemaining--;
        if (IsDepleted)
        {
            _respawnCountdown = respawnTicks;
            SetVisible(false);   // node vanishes until it respawns
            OnDepleted?.Invoke();
        }

        string dropName = ItemRegistry.Get(dropItemId)?.name ?? "item";
        string amount = qty > 1 ? $"{qty} x {dropName}" : $"some {dropName}";
        return (GatherCycleResult.Caught, $"You gather {amount}. ({xpPerAction} {skill} XP)");
    }
}
