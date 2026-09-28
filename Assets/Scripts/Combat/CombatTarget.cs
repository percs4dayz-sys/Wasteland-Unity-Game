using System;
using UnityEngine;

public class CombatTarget : MonoBehaviour, IExaminable
{
    [SerializeField] public int maxHP = 10;
    [SerializeField] public int attackLevel = 1;
    [SerializeField] public int defenceLevel = 1;
    [SerializeField] public int maxDamage = 1;
    [SerializeField] public bool isDummy = false;
    [SerializeField] public bool isAggressive = false;
    [SerializeField] public bool isMiniBoss = false;   // better gear odds; never a beast-task target
    [SerializeField] public bool isBoss = false;       // the big boss: Brutality L99 bonus dmg; never a beast-task target

    [Tooltip("Which world tier this enemy belongs to (1-5). Drives its drop table. " +
             "Leave 0 to fall back to guessing from max HP.")]
    [Range(0, 5)] [SerializeField] public int tier = 0;

    [Tooltip("The God-Hunter on the island. Uses its own drop table — guaranteed resources every " +
             "kill plus a 1-in-50 roll on the God Tier gear table.")]
    [SerializeField] public bool isGodHunter = false;

    [Tooltip("Optional custom death drops. Empty keeps the standard tier / boss table.")]
    public MonsterDropTable dropTable;

    /// <summary>Tier for loot purposes. Uses the explicit field when set, else infers from HP
    /// using the enemy blueprint's mob values (40 / 90 / 160 / 280 / 450).</summary>
    public int LootTier =>
        tier > 0 ? tier
        : maxHP >= 450 ? 5
        : maxHP >= 280 ? 4
        : maxHP >= 160 ? 3
        : maxHP >= 90  ? 2
        : 1;

    public int CurrentHP { get; private set; }
    public float HPPercent => (float)CurrentHP / maxHP;
    public bool IsDead => CurrentHP <= 0;

    public string DisplayName => isDummy ? "Training Dummy" : WorldInteractables.Pretty(name);   // "HornedRavager(Clone)" → "Horned Ravager"
    public string ExamineText => isDummy
        ? "A stuffed practice dummy. Hit it to train your combat."
        : "A hostile creature. Looks like it wants a fight.";

    public event Action<int> OnDamaged;
    public event Action OnDied;

    /// <summary>Raised once for every non-dummy kill, before loot drops. Beast tasks (Beastmastery)
    /// count their kills through this.</summary>
    public static event Action<CombatTarget> AnyKilled;

    void Awake() => CurrentHP = maxHP;

    public void TakeDamage(int amount)
    {
        if (IsDead) return;
        CurrentHP = Mathf.Max(0, CurrentHP - amount);
        OnDamaged?.Invoke(amount);
        if (IsDead)
        {
            OnDied?.Invoke();
            if (!isDummy && PlayerEntity.Instance != null)
            {
                PlayerEntity.Instance.IncrementKills();
                AnyKilled?.Invoke(this);
                EnemyLoot.Drop(this, transform.position);
            }
        }
    }

    public void Reset()
    {
        CurrentHP = maxHP;
    }
}
