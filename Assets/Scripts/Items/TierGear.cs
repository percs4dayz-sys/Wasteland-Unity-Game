using System;
using System.Collections.Generic;

/// <summary>
/// Tiered combat gear — DATA ONLY (no models needed yet). Registered into ItemRegistry at startup.
/// Stats escalate per tier and gate to stop cheesing:
///   • MELEE weapons gate on the Melee level; attackBonus = accuracy, strengthBonus = max hit.
///   • RANGED weapons gate on Marksmanship; the top two (Precision / Energy Rifle) reach much further.
///   • ARMOR gates on COMBAT LEVEL (defence is worn-armor-only now — no defence skill).
///
/// These bonuses ARE live in the 3D action combat: attackBonus/strengthBonus feed the player's
/// attack & max-hit rolls (ActionCombat3D.ResolveHit) and defenceBonus feeds the player's defence
/// roll against enemies (Enemy3D.Attack), all via CombatMath. Ammo carries ranged strength.
/// (The legacy 2D tick path uses the same CombatMath but isn't the primary mode.) IDs 100-135.
/// </summary>
public static class TierGear
{
    public static void Register(Action<ItemData> add)
    {
        // ── MELEE  (gate: Melee level; attackBonus → accuracy, strengthBonus → max hit) ──
        // Stat allocations straight from the combat doc: T1 +4/+4 → T5 +130/+160 → God +300/+380.
        // At level 99 that lands max hits of 11 (T1), 37 (T5) and 74 (God), with accuracy rolls of
        // 7.5k / 21k / 40k — the numbers the hit-chance matrix is built on. Don't tune these in
        // isolation; they're paired with the enemy defence rolls in the same doc.
        add(Weapon(100, "Pipe Melee",           "A heavy length of pipe. Crude but it swings.", WeaponStyle.Melee, atk: 4,   str: 5,   range: 1, speed: 5, gate: Skill.Attack, lvl: 1));
        add(Weapon(101, "Scrap Blade",          "Sharpened scrap. Tier 1 melee.",               WeaponStyle.Melee, atk: 4,   str: 4,   range: 1, speed: 4, gate: Skill.Attack, lvl: 1));
        add(Weapon(102, "Steel Blade",          "Forged steel. Tier 2 melee.",                  WeaponStyle.Melee, atk: 25,  str: 30,  range: 1, speed: 4, gate: Skill.Attack, lvl: 20));
        add(Weapon(103, "Energy-Infused Blade", "Crackles with charge. Tier 3 melee.",          WeaponStyle.Melee, atk: 60,  str: 70,  range: 1, speed: 4, gate: Skill.Attack, lvl: 40));
        add(Weapon(104, "Vibro-Blade",          "High-frequency edge. Tier 4 melee.",           WeaponStyle.Melee, atk: 90,  str: 110, range: 1, speed: 3, gate: Skill.Attack, lvl: 60));
        add(Weapon(105, "Myomer Blade",         "Self-sharpening myomer. Tier 5 melee.",        WeaponStyle.Melee, atk: 130, str: 160, range: 1, speed: 3, gate: Skill.Attack, lvl: 80));

        // ── RANGED (gate: Marksmanship; top two reach further). Weapons give ACCURACY (attackBonus);
        //    the bulk of ranged STRENGTH (max hit) comes from the AMMO below, OSRS-style.
        // (Pipe Pistol = tier-1 ranged, already registered as id 12.)
        //    Magazines give each tier a distinct rhythm: the T2/T3 rifles are steady workhorses, the
        //    Precision Rifle hits from far off but holds few rounds and reloads slowly (sniper feel),
        //    and the Energy Rifle barely ever stops firing. (Pipe Pistol: 6-round mag, snappy reload.)
        // Doc ranged accuracy: T1 +8 → T5 +180 → God +450, all on the weapon.
        add(Weapon(110, "Mid-tier Rifle",            "Reliable tier 2 firearm.",                 WeaponStyle.Ranged, atk: 35,  str: 5,  range: 3, speed: 4, gate: Skill.Marksmanship, lvl: 20, mag: 10, reload: 1.8f));
        add(Weapon(111, "Alloy Rifle",               "Lightweight tier 3 firearm.",              WeaponStyle.Ranged, atk: 75,  str: 10, range: 3, speed: 4, gate: Skill.Marksmanship, lvl: 40, mag: 14, reload: 1.8f));
        add(Weapon(112, "Precision Rifle",           "Long barrel — strikes from well outside the pack. Tier 4.", WeaponStyle.Ranged, atk: 120, str: 15, range: 5, speed: 5, gate: Skill.Marksmanship, lvl: 60, mag: 5,  reload: 2.4f));
        add(Weapon(113, "Experimental Energy Rifle", "Extreme range, devastating. Tier 5.",      WeaponStyle.Ranged, atk: 180, str: 20, range: 6, speed: 4, gate: Skill.Marksmanship, lvl: 80, mag: 20, reload: 2.0f));

        // ── AMMO (stackable; carries the bulk of ranged STRENGTH — ammo choice drives max hit) ──
        // Weapon str + ammo str must total the doc's ranged strength per tier:
        // T1 7, T2 30, T3 65, T4 105, T5 145. (Pipe Pistol is id 12 with str 2.)
        add(Ammo(115, "Low-Velocity Lead Ammo",  "Basic improvised rounds.",     rangedStr: 5));
        add(Ammo(116, "Standard Ballistic Ammo", "Dependable factory rounds.",   rangedStr: 25));
        add(Ammo(117, "High-Grain Ammo",         "Heavier charge, harder hits.", rangedStr: 55));
        add(Ammo(118, "Armor-Piercing Ammo",     "Punches through plating.",     rangedStr: 90));
        add(Ammo(119, "Experimental Ammo",       "Volatile high-end rounds.",    rangedStr: 125));

        // ── ARMOR (gate: Combat level) ──
        // Full-set defence totals come straight from the combat doc: T1 10, T2 45, T3 105,
        // T4 185, T5 280, God 520. They only work because the defence ROLL uses the real
        // Hardening level (Enemy3D) — (level + 8) * (bonus + 64). Pin that level low and these
        // numbers stop mattering, which is exactly the trap to avoid.
        // T2 — Riveted (combat 20) — full set 45
        add(Armor(120, "Riveted Helmet",  ItemType.Helmet, def: 9,  lvl: 20));
        add(Armor(121, "Riveted Vest",    ItemType.Chest,  def: 18, lvl: 20));
        add(Armor(122, "Riveted Greaves", ItemType.Legs,  def: 8,  lvl: 20));
        add(Armor(123, "Riveted Buckler", ItemType.Shield, def: 10, lvl: 20));
        // T3 — Hardened Alloy (combat 40) — full set 105
        add(Armor(124, "Hardened Alloy Helm",    ItemType.Helmet, def: 21, lvl: 40));
        add(Armor(125, "Hardened Alloy Plate",   ItemType.Chest,  def: 42, lvl: 40));
        add(Armor(126, "Hardened Alloy Greaves", ItemType.Legs,  def: 18, lvl: 40));
        add(Armor(127, "Hardened Alloy Shield",  ItemType.Shield, def: 24, lvl: 40));
        // T4 — Tactical Ballistic (combat 60) — full set 185
        add(Armor(128, "Tactical Ballistic Helm",    ItemType.Helmet, def: 37, lvl: 60));
        add(Armor(129, "Tactical Ballistic Armor",   ItemType.Chest,  def: 74, lvl: 60));
        add(Armor(130, "Tactical Ballistic Greaves", ItemType.Legs,  def: 32, lvl: 60));
        add(Armor(131, "Tactical Ballistic Shield",  ItemType.Shield, def: 42, lvl: 60));
        // T5 — Power-Assisted (combat 80) — full set 280
        add(Armor(132, "Power-Assisted Helm",    ItemType.Helmet, def: 56,  lvl: 80));
        add(Armor(133, "Power-Assisted Armor",   ItemType.Chest,  def: 112, lvl: 80));
        add(Armor(134, "Power-Assisted Greaves", ItemType.Legs,  def: 48,  lvl: 80));
        add(Armor(135, "Power-Assisted Shield",  ItemType.Shield, def: 64,  lvl: 80));
    }

    // ── builders ──
    static ItemData Weapon(int id, string name, string desc, WeaponStyle style,
                           int atk, int str, int range, int speed, Skill gate, int lvl,
                           int mag = 0, float reload = 0f)
        => new ItemData
        {
            id = id, name = name, description = desc, type = ItemType.Weapon, stackable = false,
            weaponStyle = style, attackBonus = atk, strengthBonus = str, attackRange = range, attackSpeed = speed,
            magazineSize = mag, reloadSeconds = reload,
            requirements = new Dictionary<Skill, int> { [gate] = lvl },
            // Per-item held model: drop a model at Assets/Resources/HeldModels/Weapons/<Name>.fbx
            // (name with spaces/hyphens removed). If it's not added yet, EquipmentVisuals falls back
            // to the machete/pistol placeholder so it still renders.
            heldModel = "HeldModels/Weapons/" + name.Replace(" ", "").Replace("-", "")
        };

    /// <summary>
    /// Starter set (ids 136-139) — the reward for an early quest, so a fresh player has something
    /// on their back before the level-20 Riveted set. Combat level 1 so it's wearable immediately.
    /// </summary>
    public static void RegisterStarterSet(Action<ItemData> add)
    {
        // Full set 10 — the doc's Tier 1 (bronze equivalent). Slight on purpose: it exists so a
        // new player isn't naked, not to matter.
        add(Armor(136, "Salvaged Helm",    ItemType.Helmet, def: 2, lvl: 1, style: WeaponStyle.None));
        add(Armor(137, "Salvaged Vest",    ItemType.Chest,  def: 4, lvl: 1, style: WeaponStyle.None));
        add(Armor(138, "Salvaged Greaves", ItemType.Legs,  def: 2, lvl: 1, style: WeaponStyle.None));
        add(Armor(139, "Salvaged Buckler", ItemType.Shield, def: 2, lvl: 1, style: WeaponStyle.None));
    }

    /// <summary>
    /// Endgame set (ids 140-143) — drops from the island boss only. Strictly better than
    /// Power-Assisted (the level-80 tier) so it stays worth farming after everything else is maxed.
    /// </summary>
    /// <summary>
    /// God Tier weapons + ammo (ids 159-161) — the God-Hunter's rare table. Doc spec:
    /// melee +300/+380, ranged +450 accuracy with ammo carrying strength to +420 total.
    /// At level 99 these reach max hits of 74-76 melee and 81-83 ranged, which is what makes
    /// the 4,500 HP God-Hunter farmable instead of a wall.
    /// </summary>
    public static void RegisterGodTierWeapons(Action<ItemData> add)
    {
        add(Weapon(159, "Ascendant Warblade",
                   "God-tier melee. The edge doesn't dull and it never quite stops moving.",
                   WeaponStyle.Melee, atk: 300, str: 380, range: 1, speed: 3,
                   gate: Skill.Attack, lvl: 90));

        add(Weapon(160, "Ascendant Railgun",
                   "God-tier ranged. Range limited only by what you can see.",
                   WeaponStyle.Ranged, atk: 450, str: 60, range: 7, speed: 4,
                   gate: Skill.Marksmanship, lvl: 90, mag: 24, reload: 1.6f));

        // Weapon 60 + ammo 360 = the doc's 420 ranged strength.
        add(Ammo(161, "Ascendant Slug", "God-tier munitions. Each round costs a fortune.", rangedStr: 360));
    }

    public static void RegisterEndgameSet(Action<ItemData> add)
    {
        // GOD TIER — the doc's top row, full set 520 (vs T5's 280). Drops piece by piece from the
        // island boss at low odds, so completing it is a long chase. Wearing all four also fires
        // the GearSets bonus, which is the real payoff on top of the raw defence.
        add(Armor(140, "Ascendant Helm",    ItemType.Helmet, def: 104, lvl: 90, setId: GearSets.AscendantMelee));
        add(Armor(141, "Ascendant Plate",   ItemType.Chest,  def: 208, lvl: 90, setId: GearSets.AscendantMelee));
        add(Armor(142, "Ascendant Greaves", ItemType.Legs,  def: 88,  lvl: 90, setId: GearSets.AscendantMelee));
        add(Armor(143, "Ascendant Bulwark", ItemType.Shield, def: 120, lvl: 90, setId: GearSets.AscendantMelee));
    }

    /// <summary>
    /// Module sockets per slot, from the module scaling matrix — seven on a full build:
    /// Weapon 2 · Chest 2 · Helmet 1 · Legs 1 · Shield 1. Ammo and tools take none.
    /// </summary>
    static int SocketsFor(ItemType slot) => slot switch
    {
        ItemType.Weapon => 2,
        ItemType.Chest  => 2,
        ItemType.Helmet => 1,
        ItemType.Legs  => 1,
        ItemType.Shield => 1,
        _               => 0,
    };

    /// <summary>
    /// Combat-level gate → palette tier. One mesh recoloured per tier is how the whole armour
    /// progression reads visually, so this mapping is what makes a level-40 chest look different
    /// from a level-20 one without any new geometry.
    /// </summary>
    static int PaletteFor(int lvl) => lvl switch
    {
        <= 1  => 1,   // Salvaged starter set
        <= 20 => 2,   // Riveted
        <= 40 => 3,   // Hardened Alloy
        <= 60 => 4,   // Tactical Ballistic
        <= 80 => 5,   // Power-Assisted
        _     => 6,   // Ascendant — endgame, its own look
    };

    /// <summary>
    /// Ranged armour line (ids 144-155) — the light counterpart to the heavy melee sets above.
    /// Same four level gates and the same palettes, so both lines progress in step; they differ in
    /// MESH (light kit vs heavy plate) and in trading a little defence for mobility flavour.
    /// No shields: ranged weapons are two-handed, so the slot would never be usable.
    /// </summary>
    public static void RegisterRangedArmour(Action<ItemData> add)
    {
        // Ranged trades raw defence for ACCURACY: less plate than the melee line at the same level,
        // but each piece sharpens your shooting. acc only counts while using a ranged weapon
        // (Equipment.TotalAttackBonusFor), so it never quietly buffs a sword.
        // Three slots rather than four (no shield), so defence totals run ~80% of the melee line —
        // that's the trade for the accuracy. Sets: T2 36, T3 84, T4 148, T5 224, God 416.
        // Tier 2 — level 20
        add(Armor(144, "Scout Hood",     ItemType.Helmet, def: 8,  lvl: 20, style: WeaponStyle.Ranged, acc: 4));
        add(Armor(145, "Scout Jacket",   ItemType.Chest,  def: 18, lvl: 20, style: WeaponStyle.Ranged, acc: 6));
        add(Armor(146, "Scout Leggings",    ItemType.Legs,  def: 10, lvl: 20, style: WeaponStyle.Ranged, acc: 3));

        // Tier 3 — level 40
        add(Armor(147, "Ranger Hood",    ItemType.Helmet, def: 19, lvl: 40, style: WeaponStyle.Ranged, acc: 9));
        add(Armor(148, "Ranger Coat",    ItemType.Chest,  def: 42, lvl: 40, style: WeaponStyle.Ranged, acc: 13));
        add(Armor(149, "Ranger Leggings",   ItemType.Legs,  def: 23, lvl: 40, style: WeaponStyle.Ranged, acc: 7));

        // Tier 4 — level 60
        add(Armor(150, "Marksman Visor", ItemType.Helmet, def: 33, lvl: 60, style: WeaponStyle.Ranged, acc: 15));
        add(Armor(151, "Marksman Rig",   ItemType.Chest,  def: 74, lvl: 60, style: WeaponStyle.Ranged, acc: 22));
        add(Armor(152, "Marksman Leggings", ItemType.Legs,  def: 41, lvl: 60, style: WeaponStyle.Ranged, acc: 11));

        // Tier 5 — level 80
        add(Armor(153, "Deadeye Visor",  ItemType.Helmet, def: 50,  lvl: 80, style: WeaponStyle.Ranged, acc: 22));
        add(Armor(154, "Deadeye Rig",    ItemType.Chest,  def: 112, lvl: 80, style: WeaponStyle.Ranged, acc: 33));
        add(Armor(155, "Deadeye Leggings",  ItemType.Legs,  def: 62,  lvl: 80, style: WeaponStyle.Ranged, acc: 17));

        // GOD TIER — island boss, ranged half of the Ascendant chase. Set 416 + the GearSets bonus.
        add(Armor(156, "Ascendant Visor", ItemType.Helmet, def: 94,  lvl: 90, style: WeaponStyle.Ranged, acc: 40, setId: GearSets.AscendantRanged));
        add(Armor(157, "Ascendant Rig",   ItemType.Chest,  def: 208, lvl: 90, style: WeaponStyle.Ranged, acc: 60, setId: GearSets.AscendantRanged));
        add(Armor(158, "Ascendant Leggings", ItemType.Legs,  def: 114, lvl: 90, style: WeaponStyle.Ranged, acc: 30, setId: GearSets.AscendantRanged));
    }

    /// <summary>
    /// TWO mesh sets, one per armour line, recoloured per tier — the melee line wears heavy plate,
    /// the ranged line wears the lighter kit. Which one a piece uses comes from its
    /// <see cref="ItemData.armourStyle"/>; the tier COLOUR comes from <see cref="ItemData.paletteTier"/>,
    /// which PaletteFor() already sets (1 Salvaged · 2 Riveted · 3 Alloy · 4 Tactical · 5
    /// Power-Assisted · 6 Ascendant).
    ///
    /// These are the only two complete families in Resources/Meshes/Outfits/Starter. Note the
    /// ranged one is ALSO the current unarmoured default (CharacterCreatorConfig.StarterOutfit*),
    /// so until that changes or a third set is imported, ranged armour looks like wearing nothing.
    /// </summary>
    const string MeleeArmourMesh  = "SK_FANT_KNGT_17";   // heavy plate
    const string RangedArmourMesh = "SK_SCFI_CIVL_09";   // light kit

    static string MeshForStyle(WeaponStyle style)
        => style == WeaponStyle.Ranged ? RangedArmourMesh : MeleeArmourMesh;

    static ItemData Armor(int id, string name, ItemType slot, int def, int lvl,
                          WeaponStyle style = WeaponStyle.Melee, int acc = 0, string setId = null)
        => new ItemData
        {
            // Without this, ArmourVisuals.Refresh skips the item entirely and equipping armour is a
            // visual no-op — which is exactly what every one of these 18 items did before.
            armourMesh = MeshForStyle(style),
            id = id, name = name,
            description = acc > 0
                ? $"Tier armor — +{def} defence, +{acc} ranged accuracy. Needs Hardening level {lvl}."
                : $"Tier armor — +{def} defence. Needs Hardening level {lvl}.",
            type = slot, stackable = false, defenceBonus = def, attackBonus = acc,
            setId = setId,
            socketCount = SocketsFor(slot),
            // Armour gates on HARDENING — the defence skill — and on nothing else. Combat level
            // gates nothing in this game. Style (melee plate vs ranged kit) only picks the mesh and
            // the stat spread, it is not a second requirement, so a Fission main can wear either.
            requirements = new Dictionary<Skill, int> { [Skill.Defence] = lvl },
            paletteTier = PaletteFor(lvl),
            armourStyle = style,
            // Only shields are held (left hand). Bespoke shield models don't exist yet, so all six
            // shields share three Polytope Studio shields (owned asset), spread across the palette
            // tiers. Swap ShieldModelFor for per-shield names once real models are made.
            heldModel = slot == ItemType.Shield ? ShieldModelFor(PaletteFor(lvl)) : null
        };

    /// <summary>Which held shield model a palette tier uses. Three Polytope shields cover six
    /// slots — low tiers get the plain one, mid the reinforced, high the ornate.</summary>
    static string ShieldModelFor(int palette) =>
          palette <= 2 ? "HeldModels/Shields/PTShield01"
        : palette <= 4 ? "HeldModels/Shields/PTShield06"
        :                "HeldModels/Shields/PTShield19";

    static ItemData Ammo(int id, string name, string desc, int rangedStr)
        => new ItemData { id = id, name = name, description = desc, type = ItemType.Ammo, stackable = true, strengthBonus = rangedStr };
}
