using System;
using System.Collections.Generic;

// NOTE: "Legs" was called "Boots" until 2026-07-31. Boots may return later as a separate slot;
// this one is the leg-armour slot (greaves, leggings) and takes a module socket.
public enum ItemType { Weapon, Shield, Helmet, Chest, Legs, Ammo, Tool, Resource, Consumable, Readable }
// Fission is the third combat style (the "magic" slot). Append only — armourStyle serialises this.
public enum WeaponStyle { None, Melee, Ranged, Fission }

[Serializable]
public class ItemData
{
    public int id;
    public string name;
    public string description;
    public ItemType type;
    public bool stackable;
    public WeaponStyle weaponStyle;
    public int attackBonus;
    public int strengthBonus;
    public int defenceBonus;
    public int attackRange;
    public int attackSpeed;
    /// <summary>Weapon needs BOTH hands: blocks equipping a shield/off-hand alongside it. Ranged guns
    /// are treated as two-handed automatically; set this on greatswords/rifles/etc. too.</summary>
    public bool twoHanded;
    /// <summary>Ranged weapons: shots per magazine before a reload. 0 = WeaponMagazine's default (8).</summary>
    public int magazineSize;
    /// <summary>Ranged weapons: seconds a reload takes. 0 = WeaponMagazine's default (1.5).</summary>
    public float reloadSeconds;
    public int healAmount;
    /// <summary>True for bones/remains that can be buried to train Beastmastery (see PlayerEntity.Bury).</summary>
    public bool buryable;
    /// <summary>Beastmastery XP granted when this item is buried. Only meaningful when buryable.</summary>
    public int beastXP;
    public UnityEngine.Sprite icon;
    /// <summary>Resources path (no extension) of the 3D model shown in the hand when equipped,
    /// e.g. "HeldModels/pickaxe". Null/empty = nothing spawned. See EquipmentVisuals.
    /// For a two-handed Fission weapon this is the RIGHT-hand model.</summary>
    public string heldModel;

    /// <summary>Optional distinct LEFT-hand model, for gear worn on both hands (power gauntlets).
    /// Null = the left hand mirrors <see cref="heldModel"/>. Only read for WeaponStyle.Fission.</summary>
    public string heldModelLeft;

    /// <summary>
    /// Resources path (no extension) of the WORN mesh for armour — a Sidekick modular part, e.g.
    /// "ArmourMeshes/SK_FANT_KNGT_17_10TORS_HU01". Null/empty = the slot shows the bare body.
    /// Swapped onto the player's skeleton by ArmourVisuals; unrelated to heldModel, which is
    /// for things carried in the hand.
    /// </summary>
    public string armourMesh;

    /// <summary>
    /// Which tier palette this piece uses (1-6). Drives the material picked from
    /// Art/Generated3D/TierPalettes — so one mesh recoloured five ways reads as five tiers.
    /// 0 = leave the mesh's own material alone.
    /// </summary>
    public int paletteTier;

    /// <summary>
    /// Which gear SET this piece belongs to, e.g. GearSets.AscendantMelee. Wearing every piece of
    /// a set fires its bonus (see GearSets). Null = not part of a set — most gear.
    /// </summary>
    public string setId;

    /// <summary>
    /// How many combat modules can be socketed into this piece. Per the module scaling matrix:
    /// Weapon 2, Chest 2, Helmet 1, Legs 1, Shield 1 — seven on a full build.
    /// 0 = no sockets. See GearSockets.
    /// </summary>
    public int socketCount;

    /// <summary>
    /// Which armour line this belongs to, which picks the MESH SET rather than the colour:
    /// Melee wears heavy plate (Sidekick FANT_KNGT), Ranged wears light kit (SCFI_CIVL).
    /// None = style-neutral (the starter set), which uses the light meshes.
    /// Only meaningful on armour; weapons use <see cref="weaponStyle"/>.
    /// </summary>
    public WeaponStyle armourStyle;
    /// <summary>Every equip gate lives here, as SKILL level requirements: weapons on their style
    /// stat, armour on Hardening (the defence skill). Combat level gates NOTHING — the old
    /// combatLevelReq field was removed when armour moved onto Hardening.</summary>
    public Dictionary<Skill, int> requirements = new();

    /// <summary>
    /// Chance (0-1) that firing this weapon does NOT consume its ammo. Only the power gauntlets use
    /// it: fission cores are expensive enough that burning one per shot would make the style
    /// unplayable, so the gauntlets recover some of them.
    /// </summary>
    public float ammoSaveChance;

    public bool IsWeapon => type == ItemType.Weapon;
    public bool IsArmor => type is ItemType.Shield or ItemType.Helmet or ItemType.Chest or ItemType.Legs;
    public bool IsTool => type == ItemType.Tool;
    public bool IsConsumable => type == ItemType.Consumable;
    public bool IsAmmo => type == ItemType.Ammo;
    public bool IsReadable => type == ItemType.Readable;
    public bool IsBuryable => buryable;
    public bool IsEquippable => IsWeapon || IsArmor || IsTool || IsAmmo;
}
