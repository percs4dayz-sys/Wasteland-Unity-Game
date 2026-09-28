using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Independent rolls; each successful roll picks one weighted outcome.</summary>
[CreateAssetMenu(menuName = "Wasteland/Monster Drop Table")]
public class MonsterDropTable : ScriptableObject
{
    [Serializable]
    public class Outcome
    {
        public int itemId = 10;
        [Min(1)] public int minQuantity = 1, maxQuantity = 1;
        [Min(0)] public float weight = 1;
    }

    [Serializable]
    public class Roll
    {
        public string label = "New drop";
        [Range(0, 1)] public float chance = 1;
        [Tooltip("Allow the player's Salvage module to grant an extra chance when this roll fails.")]
        public bool salvageBonus;
        public List<Outcome> outcomes = new();
    }

    public List<Roll> rolls = new();
    [Tooltip("-1 keeps the standard cosmetic skin chance: normal 2%, miniboss 10%, boss 25%.")]
    [Range(-1, 1)] public float cosmeticChance = -1;

    // Kept separate from world spawning so the actual drop logic can be validated without a kill.
    public void RollDrops(float salvagePower, Action<int, int> emit)
    {
        foreach (var roll in rolls)
        {
            if (roll == null || roll.outcomes == null) continue;
            float chance = Mathf.Clamp01(roll.chance);
            bool won = chance >= 1 || chance > 0 && UnityEngine.Random.value < chance;
            if (!won && roll.salvageBonus)
            {
                float bonus = Mathf.Clamp01(.10f * salvagePower);
                won = bonus >= 1 || bonus > 0 && UnityEngine.Random.value < bonus;
            }
            if (!won) continue;
            float total = 0;
            foreach (var entry in roll.outcomes)
                if (Valid(entry)) total += entry.weight;
            if (total <= 0) continue;
            float pick = UnityEngine.Random.value * total;
            Outcome selected = null;
            foreach (var entry in roll.outcomes)
            {
                if (!Valid(entry)) continue;
                selected = entry;
                pick -= entry.weight;
                if (pick <= 0) break;
            }
            if (selected != null)
            {
                int min = Mathf.Clamp(selected.minQuantity, 1, 1000000);
                int max = Mathf.Clamp(selected.maxQuantity, min, 1000000);
                emit(selected.itemId, UnityEngine.Random.Range(min, max + 1));
            }
        }
    }

    static bool Valid(Outcome entry) => entry != null && entry.weight > 0 &&
        !float.IsNaN(entry.weight) && !float.IsInfinity(entry.weight) && ItemRegistry.Get(entry.itemId) != null;
}
