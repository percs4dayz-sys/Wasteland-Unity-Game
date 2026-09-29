using UnityEngine;

public enum CombatStyle { Melee, Ranged, Fission }

// Attack options (OSRS-style), per weapon style:
//   Melee        : Accurate → Attack, Aggressive → Strength, Defensive → Defence
//   Marksmanship : Accurate (better aim), Rapid (faster fire), Distance/Longrange (more reach,
//                  better defence) — all train Marksmanship
//   Fission      : Accurate only → Fission
// Endurance (HP) trains from all combat.
public enum CombatStance
{
    Accurate,
    Aggressive,
    Defensive,
    Controlled,  // retired
    Rapid,
    Longrange    // marksmanship "Distance"
}

/// <summary>
/// Holds the player's combat STYLE (melee/ranged) and STANCE. This used to also run the legacy
/// 2D tick-combat loop; that path was removed when the game went 3D-only. The 3D combat
/// (ActionCombat3D) and the F7 stance UI (CombatStyleUI) read Style/Stance from here, so this
/// stays as the single source of truth for stance state. Self-bootstrapping so it always exists.
/// </summary>
public class CombatManager : MonoBehaviour
{
    public static CombatManager Instance { get; private set; }

    // Shooter pivot: the game starts you ranged (a gun), not melee. The equipped weapon still drives
    // the live Style via PlayerEntity.UpdateCombatStyle — this is only the starting value.
    public CombatStyle Style { get; private set; } = CombatStyle.Ranged;
    public CombatStance Stance { get; private set; } = CombatStance.Accurate;

    /// <summary>The attack options each style offers:
    ///   Melee → Accurate / Aggressive / Defensive; Marksmanship → Accurate / Rapid / Distance;
    ///   Fission → Accurate (its level buys accuracy and nothing else — all damage is in the core).</summary>
    public static CombatStance[] StancesFor(CombatStyle style) => style switch
    {
        CombatStyle.Fission => new[] { CombatStance.Accurate },
        CombatStyle.Melee   => new[] { CombatStance.Accurate, CombatStance.Aggressive, CombatStance.Defensive },
        _                   => new[] { CombatStance.Accurate, CombatStance.Rapid, CombatStance.Longrange },
    };

    /// <summary>Human label for an attack option.</summary>
    public static string StanceLabel(CombatStance s, CombatStyle style) => s switch
    {
        CombatStance.Accurate   => "Accurate",
        CombatStance.Aggressive => "Aggressive",
        CombatStance.Defensive  => "Defensive",
        CombatStance.Rapid      => "Rapid",
        CombatStance.Longrange  => "Distance",
        _ => s.ToString()
    };

    /// <summary>Short "what it does" line + which skill(s) it trains.</summary>
    public static string StanceTrains(CombatStance s, CombatStyle style)
    {
        if (style == CombatStyle.Fission)
            return "better accuracy · trains Fission";

        if (style == CombatStyle.Ranged)
        {
            string reffect = s switch
            {
                CombatStance.Accurate   => "better accuracy",
                CombatStance.Rapid      => "faster fire rate",
                CombatStance.Longrange  => "longer range, better defence",
                _ => ""
            };
            return $"{reffect} · trains Marksmanship";
        }

        string skill = s switch
        {
            CombatStance.Aggressive => "Brutality",
            CombatStance.Defensive  => "Hardening",
            _ => "Bladework"
        };
        string effect = s switch
        {
            CombatStance.Accurate   => "better accuracy",
            CombatStance.Aggressive => "bigger hits, slower",
            CombatStance.Defensive  => "tougher defence, reduced damage",
            _ => ""
        };
        return $"{effect} · trains {skill}";
    }

    /// <summary>The skill trained by a given stance with a melee weapon. Ranged always returns Marksmanship.</summary>
    public static Skill StanceSkill(CombatStance stance, CombatStyle style)
    {
        if (style == CombatStyle.Fission) return Skill.Fission;
        if (style == CombatStyle.Ranged)  return Skill.Marksmanship;
        return stance switch
        {
            CombatStance.Aggressive => Skill.Strength,
            CombatStance.Defensive  => Skill.Defence,
            _                       => Skill.Attack   // Accurate, anything else
        };
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        new GameObject("CombatManager (auto)").AddComponent<CombatManager>();
    }

    void Awake()
    {
        // Only the duplicate component goes: the world scene keeps CombatManager on a shared "Managers" object with
        // GameTick, SkillingManager and CraftingManager — destroying that whole object (after the title screen had
        // already made one) silently killed gathering, crafting and the game tick on the phone.
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void SetStyle(CombatStyle style)
    {
        Style = style;

        // Each style offers its own options, so switching weapons (e.g. a gun on Distance to a blade)
        // would leave a stance the panel no longer shows — and a stance you cannot see is a
        // stance you cannot get out of. Clamp to whatever this style actually offers.
        var allowed = StancesFor(style);
        if (System.Array.IndexOf(allowed, Stance) < 0)
            Stance = allowed.Length > 0 ? allowed[0] : CombatStance.Accurate;
    }

    public void SetStance(CombatStance stance) => Stance = stance;

    // ── special attack energy ────────────────────────────────────────────
    // RuneScape-style: a 0-100 pool that a special spends and time refills, NOT a cooldown. The
    // difference matters — energy lets you bank a spec for when it counts and spend it the instant
    // you want it, where a cooldown decides for you.

    /// <summary>Special-attack energy, 0-100.</summary>
    public float SpecialEnergy { get; private set; } = MaxSpecialEnergy;

    public const float MaxSpecialEnergy = 100f;

    [Tooltip("Energy restored per second. 3.33 refills the bar in 30s, so a 50-cost special is " +
             "available roughly every 15s of not using one.")]
    public float specialRegenPerSecond = 3.33f;

    /// <summary>Fires whenever the energy changes, so the UI bar can follow it without polling.</summary>
    public event System.Action<float> OnSpecialEnergyChanged;

    void Update()
    {
        if (SpecialEnergy >= MaxSpecialEnergy) return;
        SpecialEnergy = Mathf.Min(MaxSpecialEnergy, SpecialEnergy + specialRegenPerSecond * Time.deltaTime);
        OnSpecialEnergyChanged?.Invoke(SpecialEnergy);
    }

    /// <summary>Spend energy if there is enough. Returns false and spends nothing if there isn't.</summary>
    public bool TrySpendSpecial(float cost)
    {
        if (SpecialEnergy + 0.001f < cost) return false;
        SpecialEnergy = Mathf.Max(0f, SpecialEnergy - cost);
        OnSpecialEnergyChanged?.Invoke(SpecialEnergy);
        return true;
    }

    /// <summary>Refill instantly — for dev tools and respawns.</summary>
    public void RestoreSpecial()
    {
        SpecialEnergy = MaxSpecialEnergy;
        OnSpecialEnergyChanged?.Invoke(SpecialEnergy);
    }
}
