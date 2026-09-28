using System.Collections.Generic;
using Synty.SidekickCharacters.Enums;

/// <summary>
/// The rules for what a player may choose when creating a character, and what they're stuck with.
///
/// DESIGN: character creation is FACE AND BODY ONLY. No gear is selectable, because every armour
/// piece in the game is a Sidekick outfit part that drops from enemies — letting players pick those
/// at creation would hand out endgame looks for free and destroy the visual progression the tier
/// palettes exist to provide. New characters wear a fixed starter outfit instead.
///
/// The split falls out of Synty's own folder layout, so this is a filter rather than a hand-authored
/// list of 157 meshes:
///   Resources/Meshes/Species/Humans/  → SK_HUMN_BASE  (99 parts) — creation
///   Resources/Meshes/Outfits/Starter/ → SK_FANT_KNGT / SK_SCFI_CIVL / SK_HORR_VILN — loot only
///
/// Part names encode their slot as the CharacterPartType value, e.g.
///   SK_FANT_KNGT_17_10TORS_HU01
///                  │  └── 10 = Torso
///                  └───── set 17
/// so <see cref="TryParse"/> resolves a mesh name to its slot with no mapping table to maintain.
/// </summary>
public static class CharacterCreatorConfig
{
    /// <summary>Prefix of the only part family the creator may offer — the bare human body/face.</summary>
    public const string BasePrefix = "SK_HUMN_BASE";

    /// <summary>The fixed clothing every new character starts in. Not selectable, not a drop.
    /// SCFI_CIVL reads as civilian survivor kit rather than armour, so it doesn't look like loot.</summary>
    public const string StarterOutfitPrefix = "SK_SCFI_CIVL";
    public const int StarterOutfitSet = 9;

    /// <summary>
    /// What the player picks in the creator: the face and head. These have no gear equivalent, so
    /// nothing here can collide with a drop.
    /// </summary>
    public static readonly CharacterPartType[] SelectableFace =
    {
        CharacterPartType.Head,
        CharacterPartType.Hair,
        CharacterPartType.EyebrowLeft,
        CharacterPartType.EyebrowRight,
        CharacterPartType.EyeLeft,
        CharacterPartType.EyeRight,
        CharacterPartType.EarLeft,
        CharacterPartType.EarRight,
        CharacterPartType.FacialHair,
        CharacterPartType.Nose,
    };

    /// <summary>
    /// Body slots. The player picks their BARE body here (build/skin), but these same slots are what
    /// armour later covers — so the choice is remembered and restored when gear comes off, rather
    /// than being lost the first time a chestplate is equipped.
    /// </summary>
    public static readonly CharacterPartType[] SelectableBody =
    {
        CharacterPartType.Torso,
        CharacterPartType.ArmUpperLeft,  CharacterPartType.ArmUpperRight,
        CharacterPartType.ArmLowerLeft,  CharacterPartType.ArmLowerRight,
        CharacterPartType.HandLeft,      CharacterPartType.HandRight,
        CharacterPartType.Hips,
        CharacterPartType.LegLeft,       CharacterPartType.LegRight,
        CharacterPartType.FootLeft,      CharacterPartType.FootRight,
    };

    /// <summary>Slots the starter outfit fills, and that armour drops later replace.</summary>
    public static readonly CharacterPartType[] ClothingSlots = SelectableBody;

    /// <summary>
    /// Which Sidekick parts each EQUIPMENT SLOT covers. One armour item replaces several parts —
    /// a chestplate isn't just a torso, it's torso + arms + hands + shoulder/elbow attachments.
    /// Unequipping restores exactly these part types from the player's creator choices.
    ///
    /// Shield and Weapon are absent on purpose: they're held items, handled by EquipmentVisuals
    /// against the hand bones, not body parts.
    /// </summary>
    public static readonly Dictionary<string, CharacterPartType[]> SlotCoverage =
        new Dictionary<string, CharacterPartType[]>
        {
            ["Helmet"] = new[]
            {
                CharacterPartType.AttachmentHead,
            },
            ["Chest"] = new[]
            {
                CharacterPartType.Torso,
                CharacterPartType.ArmUpperLeft,  CharacterPartType.ArmUpperRight,
                CharacterPartType.ArmLowerLeft,  CharacterPartType.ArmLowerRight,
                CharacterPartType.HandLeft,      CharacterPartType.HandRight,
                CharacterPartType.AttachmentShoulderLeft, CharacterPartType.AttachmentShoulderRight,
                CharacterPartType.AttachmentElbowLeft,    CharacterPartType.AttachmentElbowRight,
                CharacterPartType.AttachmentBack,
            },
            ["Legs"] = new[]
            {
                CharacterPartType.Hips,
                CharacterPartType.LegLeft,   CharacterPartType.LegRight,
                CharacterPartType.FootLeft,  CharacterPartType.FootRight,
                CharacterPartType.AttachmentKneeLeft,  CharacterPartType.AttachmentKneeRight,
                CharacterPartType.AttachmentHipsFront,  CharacterPartType.AttachmentHipsBack,
                CharacterPartType.AttachmentHipsLeft,   CharacterPartType.AttachmentHipsRight,
            },
        };

    /// <summary>Part types this slot's armour replaces, or empty for held/unhandled slots.</summary>
    public static CharacterPartType[] CoverageFor(string slot)
        => slot != null && SlotCoverage.TryGetValue(slot, out var types)
            ? types
            : System.Array.Empty<CharacterPartType>();

    /// <summary>True if this part may appear in the character creator. Anything from an outfit set
    /// is rejected outright — that's the whole rule, in one place.</summary>
    public static bool IsSelectable(string meshName)
    {
        if (string.IsNullOrEmpty(meshName)) return false;
        return meshName.StartsWith(BasePrefix, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True if this part is gear — reserved for drops, never offered at creation.</summary>
    public static bool IsGear(string meshName) => !IsSelectable(meshName);

    /// <summary>
    /// Pull the set family, set number and slot out of a Sidekick mesh name.
    /// "SK_FANT_KNGT_17_10TORS_HU01" → family "SK_FANT_KNGT", set 17, slot Torso.
    /// </summary>
    public static bool TryParse(string meshName, out string family, out int set, out CharacterPartType slot)
    {
        family = null; set = 0; slot = default;
        if (string.IsNullOrEmpty(meshName)) return false;

        // Strip any path and extension the caller may have passed in.
        int slash = meshName.LastIndexOfAny(new[] { '/', '\\' });
        if (slash >= 0) meshName = meshName.Substring(slash + 1);
        int dot = meshName.IndexOf('.');
        if (dot >= 0) meshName = meshName.Substring(0, dot);

        // SK_<GENRE>_<SET>_<setNum>_<slotNum><SLOT>_<species>
        var bits = meshName.Split('_');
        if (bits.Length < 5) return false;

        family = $"{bits[0]}_{bits[1]}_{bits[2]}";
        if (!int.TryParse(bits[3], out set)) return false;

        // bits[4] looks like "10TORS" — the leading number IS the CharacterPartType value.
        string slotChunk = bits[4];
        int digits = 0;
        while (digits < slotChunk.Length && char.IsDigit(slotChunk[digits])) digits++;
        if (digits == 0) return false;
        if (!int.TryParse(slotChunk.Substring(0, digits), out int slotId)) return false;
        if (!System.Enum.IsDefined(typeof(CharacterPartType), slotId)) return false;

        slot = (CharacterPartType)slotId;
        return true;
    }

    /// <summary>Every part type the creator lets the player touch, face and body together.</summary>
    public static IEnumerable<CharacterPartType> AllSelectableSlots()
    {
        foreach (var p in SelectableFace) yield return p;
        foreach (var p in SelectableBody) yield return p;
    }
}
