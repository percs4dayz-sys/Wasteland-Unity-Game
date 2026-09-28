using System.Collections.Generic;
using UnityEngine;
using Synty.SidekickCharacters.API;
using Synty.SidekickCharacters.Database;
using Synty.SidekickCharacters.Database.DTO;
using Synty.SidekickCharacters.Enums;

/// <summary>
/// Applies a <see cref="CharacterAppearance.Look"/> — body build and colours — to a SidekickRuntime.
///
/// Shared by the creator and the world so a character can't look one way on the select screen and
/// another way in game. Two separate mechanisms live behind this:
///
///   BODY BUILD is blend shapes, not meshes. Every SK_HUMN_BASE body part ships exactly one mesh, so
///   there is nothing to swap — build comes from four blend weights the runtime writes during
///   CreateCharacter. Set them BEFORE rebuilding; they take effect on the next build.
///
///   COLOUR is a pixel written into the shared material's colour map at each property's (U,V). It
///   applies immediately and survives rebuilds, because the material outlives the model.
/// </summary>
public static class SidekickLook
{
    /// <summary>Push the body build onto the runtime. Takes effect on the NEXT CreateCharacter call.</summary>
    public static void ApplyBody(SidekickRuntime runtime, CharacterAppearance.Look look)
    {
        if (runtime == null || look == null) return;

        runtime.BodyTypeBlendValue = look.BodyType;
        runtime.MusclesBlendValue  = look.Musculature;

        // One -100..100 slider drives two one-directional blend shapes: negative is skinny, positive
        // is heavy, and only one of them is ever non-zero. Matches ModularCharacterWindow's mapping.
        if (look.BodySize > 0f)
        {
            runtime.BodySizeHeavyBlendValue  = look.BodySize;
            runtime.BodySizeSkinnyBlendValue = 0f;
        }
        else if (look.BodySize < 0f)
        {
            runtime.BodySizeHeavyBlendValue  = 0f;
            runtime.BodySizeSkinnyBlendValue = -look.BodySize;
        }
        else
        {
            runtime.BodySizeHeavyBlendValue  = 0f;
            runtime.BodySizeSkinnyBlendValue = 0f;
        }
    }

    /// <summary>Write the colour choices into the material. Immediate — no rebuild needed.</summary>
    public static void ApplyColors(SidekickRuntime runtime, DatabaseManager db, CharacterAppearance.Look look)
    {
        if (runtime == null || db == null || look == null) return;

        var all = SidekickColorProperty.GetAll(db);
        if (all == null || all.Count == 0) return;

        Paint(runtime, all, HairProperties,       look.HairColor);
        Paint(runtime, all, FacialHairProperties, look.FacialHairColor);
        Paint(runtime, all, BrowProperties,       look.BrowColor);
        Paint(runtime, all, SkinProperties,       look.SkinColor);
    }

    // Property names come straight from sk_color_property. "Hair" has to exclude "Facial Hair" or
    // colouring the hair would drag the beard with it — they're separate rows in the creator.
    static readonly string[] HairProperties       = { "Hair 01", "Hair 02", "Hair Accessory" };
    static readonly string[] FacialHairProperties = { "Facial Hair 01", "Facial Hair 02", "Facial Hair Accessory" };
    static readonly string[] BrowProperties       = { "Eyebrow Left", "Eyebrow Right" };
    static readonly string[] SkinProperties       = { "Skin 01", "Skin 02", "Skin 03", "Ear Left", "Ear Right",
                                                      "Eyelids Left", "Eyelids Right", "Nose" };

    static void Paint(SidekickRuntime runtime, List<SidekickColorProperty> all, string[] names, string hex)
    {
        if (string.IsNullOrEmpty(hex)) return;

        foreach (string name in names)
        {
            var property = all.Find(p => p.Name == name);
            if (property == null) continue;

            runtime.UpdateColor(ColorType.MainColor, new SidekickColorRow
            {
                ColorProperty = property,
                MainColor     = hex,
            });
        }
    }

    /// <summary>The twelve stock builds from sk_body_shape_preset, ordered for cycling.</summary>
    public static List<SidekickBodyShapePreset> BodyShapes(DatabaseManager db)
    {
        var all = SidekickBodyShapePreset.GetAll(db) ?? new List<SidekickBodyShapePreset>();
        all.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return all;
    }

    /// <summary>Copy a stock preset's three values into a Look.</summary>
    public static void SetFromPreset(CharacterAppearance.Look look, SidekickBodyShapePreset preset)
    {
        if (look == null || preset == null) return;
        look.BodyShapeId  = preset.ID;
        look.BodyType     = preset.BodyType;
        look.BodySize     = preset.BodySize;
        look.Musculature  = preset.Musculature;
    }

    // Palettes. Hand-picked rather than pulled from sk_color_preset, because those presets are whole
    // multi-property outfit schemes — applying one would repaint far more than the hair.
    public static readonly string[] HairPalette =
    {
        "1B1512", "3B2A20", "5C4033", "8B5A2B", "B8860B", "D9B380",
        "E8D3A9", "8C3B21", "B34724", "6E6E6E", "9E9E9E", "D8D8D8",
        "3F5E8C", "6B3F8C", "8C3F5E", "3F8C6B",
    };

    public static readonly string[] SkinPalette =
    {
        "F5DCC4", "EBC8A6", "D9A97E", "C58C5A", "A9714B", "8A5A3B",
        "6B452C", "4E321F", "F0C8A0", "DDB08C",
    };
}
