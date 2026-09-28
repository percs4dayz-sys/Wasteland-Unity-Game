using System;
using UnityEngine;

public class CraftingManager : MonoBehaviour
{
    public static CraftingManager Instance { get; private set; }

    public event Action<string> OnMessage;

    // Everything in the game is tick-based (OSRS-style). Crafting produces one
    // output every CRAFT_INTERVAL ticks — 4 ticks * 0.6s = 2.4s per bar — even
    // when "Make All" was chosen.
    const int CRAFT_INTERVAL = 4;

    CraftingRecipe _job;        // recipe currently being batch-crafted
    int _jobRemaining;          // how many outputs are left to make
    int _ticksSinceCraft;
    int _jobMade;               // produced so far this job (for the summary message)

    public bool IsCrafting => _job != null;

    void Awake() => Instance = this;

    void Start() => GameTick.OnTick += Tick;
    void OnDestroy() => GameTick.OnTick -= Tick;

    /// <summary>
    /// Called when the player reaches a crafting station. Opens the selection UI
    /// instead of auto-crafting. Falls back to legacy auto-craft if the UI is missing.
    /// </summary>
    public void Interact(CraftingStation station)
    {
        var player = PlayerEntity.Instance;
        if (player == null) return;

        if (CraftingUI.Instance != null)
        {
            CraftingUI.Instance.Open(station);
            return;
        }

        // Fallback: no UI present — behave like before.
        var (_, msg) = station.TryCraft(player);
        OnMessage?.Invoke(msg);
    }

    /// <summary>How many of this recipe can be made right now (materials only).</summary>
    public int MaxCraftable(CraftingRecipe recipe)
    {
        var player = PlayerEntity.Instance;
        if (player == null || recipe == null || recipe.inputs.Count == 0) return 0;

        int max = int.MaxValue;
        foreach (var ing in recipe.inputs)
        {
            if (ing.quantity <= 0) continue;
            int have = player.Inventory.CountOf(ing.itemId);
            max = Mathf.Min(max, have / ing.quantity);
        }
        return max == int.MaxValue ? 0 : max;
    }

    /// <summary>
    /// Non-consuming feasibility check. Returns whether a single craft could succeed
    /// right now plus a player-facing reason when it can't.
    /// </summary>
    public (bool ok, string message) CanCraft(CraftingRecipe recipe) => CanCraft(recipe, PlayerEntity.Instance);

    public static (bool ok, string message) CanCraft(CraftingRecipe recipe, PlayerEntity player)
    {
        if (player == null) return (false, "No player.");
        if (recipe == null) return (false, "Nothing selected.");
        if (!string.IsNullOrEmpty(recipe.requiredFlag) && !player.HasFlag(recipe.requiredFlag))
            return (false, "Complete Roxy's Fission lesson to unlock this replacement recipe.");

        if (player.Stats.GetLevel(recipe.skill) < recipe.levelRequired)
            return (false, $"You need {recipe.skill} level {recipe.levelRequired} to make that.");

        // Name exactly what's short so the player knows what to go gather,
        // e.g. "You need 1 more Wood Scrap to make that."
        var missing = new System.Collections.Generic.List<string>();
        foreach (var ing in recipe.inputs)
        {
            int have = player.Inventory.CountOf(ing.itemId);
            if (have < ing.quantity)
            {
                string nm = ItemRegistry.Get(ing.itemId)?.name ?? ("item " + ing.itemId);
                missing.Add($"{ing.quantity - have} more {nm}");
            }
        }
        if (missing.Count > 0)
            return (false, $"You need {string.Join(" and ", missing)} to make that.");

        return (true, null);
    }

    /// <summary>Attempts a single craft. Returns success + a player-facing message.</summary>
    public (bool success, string message) CraftOne(CraftingRecipe recipe) => CraftOne(recipe, PlayerEntity.Instance);

    public static (bool success, string message) CraftOne(CraftingRecipe recipe, PlayerEntity player)
    {
        var (ok, why) = CanCraft(recipe, player);
        if (!ok) return (false, why);

        // Consume first so freed slots are available for the output (prevents false "full").
        var before = player.Inventory.Snapshot();
        recipe.Consume(player.Inventory);

        // Cooking can burn: produce Burnt Food (no XP) instead of the meal.
        bool burned = recipe.burnItemId != 0 && RollBurn(recipe, player);
        int outId = burned ? recipe.burnItemId : recipe.outputItemId;
        int outQty = burned ? 1 : recipe.outputQty;

        if (!player.Inventory.Add(outId, outQty))
        {
            // Inventory was genuinely full — give the materials back so nothing is lost.
            player.Inventory.Restore(before);
            return (false, "Your inventory is full.");
        }

        if (burned)
            return (true, "You accidentally burn the food.");

        player.Stats.AddXP(recipe.skill, recipe.xpGranted);
        string outName = ItemRegistry.Get(recipe.outputItemId)?.name ?? recipe.name;
        return (true, $"You make {outName}. ({recipe.xpGranted} {recipe.skill} XP)");
    }

    // Burn chance falls linearly from BASE_BURN at the recipe's required level to 0 at
    // noBurnLevel; at/above noBurnLevel food never burns (OSRS-style mastery).
    const float BASE_BURN = 0.45f;

    static bool RollBurn(CraftingRecipe recipe, PlayerEntity player)
    {
        if (recipe.noBurnLevel <= recipe.levelRequired) return false;
        int lvl = player.Stats.GetLevel(recipe.skill);
        if (lvl >= recipe.noBurnLevel) return false;
        float t = Mathf.InverseLerp(recipe.levelRequired, recipe.noBurnLevel, lvl);
        float chance = Mathf.Lerp(BASE_BURN, 0f, t);
        return UnityEngine.Random.value < chance;
    }

    /// <summary>
    /// Begins a tick-based batch craft of up to <paramref name="count"/> outputs.
    /// One output is produced every CRAFT_INTERVAL ticks; the job stops at the
    /// first failure (out of materials, full inventory, etc.).
    /// </summary>
    public void CraftMany(CraftingRecipe recipe, int count)
    {
        if (recipe == null) { OnMessage?.Invoke("Nothing selected."); return; }
        if (count <= 0) count = 1;

        // Validate up-front (without consuming) so impossible jobs give instant
        // feedback. The first bar itself is still produced after a full interval,
        // so every output takes the same 2.4s.
        var (ok, why) = CanCraft(recipe);
        if (!ok) { StopCrafting(); OnMessage?.Invoke(why); return; }

        _job = recipe;
        _jobMade = 0;
        _jobRemaining = count;
        _ticksSinceCraft = 0;
        OnMessage?.Invoke("You begin crafting...");
    }

    /// <summary>Cancels any in-progress batch craft (e.g. when the window closes).</summary>
    public void StopCrafting()
    {
        _job = null;
        _jobRemaining = 0;
        _jobMade = 0;
        _ticksSinceCraft = 0;
    }

    void FinishCrafting()
    {
        if (_job != null && _jobMade > 0)
        {
            string outName = ItemRegistry.Get(_job.outputItemId)?.name ?? _job.name;
            OnMessage?.Invoke($"You finish crafting {_jobMade} x {outName}.");
        }
        StopCrafting();
    }

    void Tick(long tickCount)
    {
        if (_job == null) return;

        _ticksSinceCraft++;
        if (_ticksSinceCraft < CRAFT_INTERVAL) return;
        _ticksSinceCraft = 0;

        var (ok, msg) = CraftOne(_job);
        if (!ok) { OnMessage?.Invoke(msg); StopCrafting(); return; }

        OnMessage?.Invoke(msg);
        _jobMade++;
        _jobRemaining--;
        CraftingUI.Instance?.RefreshAfterCraft();

        if (_jobRemaining <= 0) FinishCrafting();
    }
}
