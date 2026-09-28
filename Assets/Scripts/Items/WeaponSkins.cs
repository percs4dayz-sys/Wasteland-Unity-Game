using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cosmetic weapon skins — the "collectible coolness" layer that is intentionally SEPARATE from
/// stats, so it needs no per-instance inventory data (the id-based bag/save is untouched).
///
/// A skin belongs to a weapon CATEGORY (Melee or Ranged). You unlock skins as rare drops, then pick
/// one "look" per category. Whatever weapon you actually wield — even your top-tier rifle — renders
/// with your chosen look. That's the answer to "I like this gun more than my higher-tier one": keep
/// the look you love while wielding the power you need. Nothing is sacrificed; the stats always come
/// from the equipped item.
///
/// Skins that set no <see cref="heldModel"/> just recolor the equipped weapon's existing model via a
/// tint — so this whole system works with ZERO new art (tinted variants already feel like different
/// guns). Drop real skin models into Resources and point <see cref="heldModel"/> at them later.
///
/// Persistence: unlocks are additive save flags ("skin_&lt;id&gt;"); the per-category selection is a
/// single flag ("skinsel_melee_&lt;id&gt;" / "skinsel_ranged_&lt;id&gt;"). Both ride the existing save.
///
/// Controls: K cycles the look for the weapon you're holding. Self-bootstrapping; no scene setup.
/// </summary>
public class WeaponSkins : MonoBehaviour
{
    public class Skin
    {
        public string id;
        public string name;
        public WeaponStyle category;   // Melee or Ranged
        public string heldModel;       // Resources path; null = keep the weapon's own model, just tint
        public Color tint;             // applied to the held model; Color.clear = no tint
        public string rarity;          // flavor for the unlock toast
    }

    // Starter set — all tint-only, so they light up immediately with no art. IDs are stable strings
    // (used in save flags), so reordering this list is safe.
    static readonly List<Skin> All = new()
    {
        // ── Ranged finishes ──
        new Skin { id="rng_rust",  name="Rustpunk Finish",  category=WeaponStyle.Ranged, tint=new Color(0.72f,0.45f,0.30f), rarity="Common" },
        new Skin { id="rng_ash",   name="Ashen Ops",        category=WeaponStyle.Ranged, tint=new Color(0.28f,0.29f,0.32f), rarity="Uncommon" },
        new Skin { id="rng_neon",  name="Neon Circuit",     category=WeaponStyle.Ranged, tint=new Color(0.20f,0.85f,0.95f), rarity="Rare" },
        new Skin { id="rng_gold",  name="Warlord's Gilt",   category=WeaponStyle.Ranged, tint=new Color(0.95f,0.78f,0.25f), rarity="Epic" },

        // ── Melee finishes ──
        new Skin { id="mel_blood", name="Blood-Forged",     category=WeaponStyle.Melee,  tint=new Color(0.75f,0.15f,0.15f), rarity="Common" },
        new Skin { id="mel_chrome",name="Chrome Edge",      category=WeaponStyle.Melee,  tint=new Color(0.90f,0.92f,0.95f), rarity="Uncommon" },
        new Skin { id="mel_toxic", name="Toxic Bite",       category=WeaponStyle.Melee,  tint=new Color(0.45f,0.90f,0.25f), rarity="Rare" },
        new Skin { id="mel_void",  name="Voidsteel",        category=WeaponStyle.Melee,  tint=new Color(0.45f,0.30f,0.70f), rarity="Epic" },
    };

    public static WeaponSkins Instance { get; private set; }
    public KeyCode cycleKey = KeyCode.K;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var go = new GameObject("WeaponSkins (auto)");
        go.AddComponent<WeaponSkins>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        if (ChatInput.IsTyping) return;
        if (Input.GetKeyDown(cycleKey)) CycleHeldWeaponSkin();
    }

    // ── lookup ───────────────────────────────────────────────────────────
    public static Skin Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var s in All) if (s.id == id) return s;
        return null;
    }

    static string UnlockFlag(string id) => "skin_" + id;
    static string SelPrefix(WeaponStyle cat) => cat == WeaponStyle.Ranged ? "skinsel_ranged_" : "skinsel_melee_";

    public static bool IsUnlocked(string id)
    {
        var p = PlayerEntity.Instance;
        return p != null && p.HasFlag(UnlockFlag(id));
    }

    /// <summary>The skin currently chosen for a category, or null (= the weapon's default look).</summary>
    public static Skin Selected(WeaponStyle cat)
    {
        var p = PlayerEntity.Instance;
        if (p == null) return null;
        string prefix = SelPrefix(cat);
        foreach (var f in p.GetAllFlags())
            if (f.StartsWith(prefix)) return Get(f.Substring(prefix.Length));
        return null;
    }

    public static void Select(WeaponStyle cat, string skinId)
    {
        var p = PlayerEntity.Instance;
        if (p == null) return;
        string prefix = SelPrefix(cat);
        // Clear any prior selection for this category (one look at a time).
        foreach (var f in new List<string>(p.GetAllFlags()))
            if (f.StartsWith(prefix)) p.RemoveFlag(f);
        if (!string.IsNullOrEmpty(skinId)) p.SetFlag(prefix + skinId);
        PlayerEntity.Instance?.Equipment?.RaiseChanged();   // rebuild the held model with the new look
    }

    /// <summary>The skin to actually render for an equipped weapon: the category's selection, but only
    /// if it's unlocked. Null → render the weapon's own model untinted.</summary>
    public static Skin AppliedFor(ItemData weapon)
    {
        if (weapon == null || !weapon.IsWeapon || weapon.weaponStyle == WeaponStyle.None) return null;
        var sel = Selected(weapon.weaponStyle);
        return (sel != null && IsUnlocked(sel.id)) ? sel : null;
    }

    // ── unlocking ──────────────────────────────────────────────────────────
    public static void Unlock(string id)
    {
        var p = PlayerEntity.Instance;
        var skin = Get(id);
        if (p == null || skin == null || p.HasFlag(UnlockFlag(id))) return;
        p.SetFlag(UnlockFlag(id));
        HUDController.Emit($"<color=#FFD24A>[SKIN]:</color> You found a weapon finish — <b>{skin.name}</b> " +
                           $"({skin.rarity} {skin.category}). Hold the matching weapon and press K to equip the look.");
    }

    /// <summary>Rare cosmetic drop on a kill: tougher enemies have a better shot at gifting a finish
    /// the player hasn't collected yet. Purely cosmetic — never touches the loot economy.</summary>
    public static void RollDrop(CombatTarget victim)
    {
        var p = PlayerEntity.Instance;
        if (p == null || victim == null || victim.isDummy) return;

        // Chance scales with toughness: ~2% trash → ~25% boss.
        float chance = victim.isBoss ? 0.25f : victim.isMiniBoss ? 0.10f : 0.02f;
        if (victim.dropTable != null && victim.dropTable.cosmeticChance >= 0)
            chance = Mathf.Clamp01(victim.dropTable.cosmeticChance);
        if (chance <= 0) return;
        if (Random.value > chance) return;

        // Pick a not-yet-owned skin at random.
        var locked = new List<Skin>();
        foreach (var s in All) if (!IsUnlocked(s.id)) locked.Add(s);
        if (locked.Count == 0) return;
        Unlock(locked[Random.Range(0, locked.Count)].id);
    }

    // ── K: cycle the look for the weapon in hand ─────────────────────────────
    void CycleHeldWeaponSkin()
    {
        var p = PlayerEntity.Instance;
        if (p == null) return;
        var weapon = p.Equipment.GetItem("Weapon");
        if (weapon == null || !weapon.IsWeapon || weapon.weaponStyle == WeaponStyle.None)
        {
            HUDController.Emit("<color=#FFD24A>[SKIN]:</color> Hold a weapon to change its finish.");
            return;
        }
        var cat = weapon.weaponStyle;

        // Build the cycle: [default look] + every unlocked skin in this category.
        var options = new List<Skin> { null };
        foreach (var s in All) if (s.category == cat && IsUnlocked(s.id)) options.Add(s);
        if (options.Count == 1)
        {
            HUDController.Emit($"<color=#FFD24A>[SKIN]:</color> No {cat.ToString().ToLower()} finishes collected yet — beat tough enemies to find them.");
            return;
        }

        var cur = Selected(cat);
        int idx = 0;
        for (int i = 0; i < options.Count; i++)
            if ((options[i] == null && cur == null) || (options[i] != null && cur != null && options[i].id == cur.id)) { idx = i; break; }
        var next = options[(idx + 1) % options.Count];

        Select(cat, next?.id);
        HUDController.Emit($"<color=#FFD24A>[SKIN]:</color> {cat} finish → <b>{(next != null ? next.name : "Default")}</b>.");
    }
}
