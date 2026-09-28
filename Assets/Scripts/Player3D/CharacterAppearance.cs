using System.Collections.Generic;
using UnityEngine;
using Synty.SidekickCharacters.Enums;

/// <summary>
/// The player's chosen face and body, persisted between the character creator and the world.
///
/// Stored in PlayerPrefs rather than the save file on purpose: CharacterSelectUI.StartGame() calls
/// DeleteSave() for a fresh run, so anything written into the save at creation time would be wiped
/// moments later. Name and gender already live in PlayerPrefs for the same reason — appearance
/// follows the pattern rather than inventing a second one.
///
/// Format is one flat string, "partType=partName|partType=partName|…", which keeps it readable in
/// the registry and avoids a JSON round-trip for what is at most ~20 short pairs.
/// </summary>
public static class CharacterAppearance
{
    const string Key = "PlayerAppearance";

    /// <summary>True once a character has actually been created — lets the world tell a created
    /// character apart from a dev session that skipped the creator.</summary>
    public static bool Exists => !string.IsNullOrEmpty(PlayerPrefs.GetString(Key, ""));

    public static void Save(IDictionary<CharacterPartType, string> parts)
    {
        if (parts == null || parts.Count == 0) { Clear(); return; }

        var sb = new System.Text.StringBuilder();
        foreach (var kv in parts)
        {
            if (string.IsNullOrEmpty(kv.Value)) continue;
            if (sb.Length > 0) sb.Append('|');
            sb.Append((int)kv.Key).Append('=').Append(kv.Value);
        }

        PlayerPrefs.SetString(Key, sb.ToString());
        PlayerPrefs.Save();
        Debug.Log($"[CharacterAppearance] Saved {parts.Count} parts.");
    }

    /// <summary>The saved appearance, or an empty dictionary if none. Never null.</summary>
    public static Dictionary<CharacterPartType, string> Load()
    {
        var result = new Dictionary<CharacterPartType, string>();
        string raw = PlayerPrefs.GetString(Key, "");
        if (string.IsNullOrEmpty(raw)) return result;

        foreach (string pair in raw.Split('|'))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            if (!int.TryParse(pair.Substring(0, eq), out int typeId)) continue;
            if (!System.Enum.IsDefined(typeof(CharacterPartType), typeId)) continue;

            string partName = pair.Substring(eq + 1);
            if (string.IsNullOrEmpty(partName)) continue;

            // Defensive: a gear part must never end up in the base body, or unequipping armour
            // would reveal more armour. The creator already filters, but saves outlive code.
            if (CharacterCreatorConfig.IsGear(partName)
                && !partName.StartsWith(CharacterCreatorConfig.StarterOutfitPrefix,
                                        System.StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[CharacterAppearance] Ignoring gear part '{partName}' found in a " +
                                 "saved appearance — body parts only.");
                continue;
            }

            result[(CharacterPartType)typeId] = partName;
        }

        return result;
    }

    public static void Clear()
    {
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.DeleteKey(ExtrasKey);
        PlayerPrefs.Save();
    }

    // ── body shape and colours ───────────────────────────────────────────
    //
    // Parts alone don't describe a Sidekick character. Body build is blend shapes, not meshes (every
    // SK_HUMN_BASE body part ships exactly one mesh), and hair/skin colour is pixels written into the
    // shared material's colour map. Neither survives in the part list, so they get their own record.

    const string ExtrasKey = "PlayerAppearanceExtras";

    /// <summary>Everything about a character that isn't a part choice. Values are the Sidekick blend
    /// inputs (-100..100) and RGB hex strings without the leading '#'.</summary>
    public class Look
    {
        public int BodyShapeId = -1;      // sk_body_shape_preset id, -1 = untouched
        public float BodyType;            // -100 masculine … +100 feminine
        public float BodySize;            // -100 slim … +100 heavy
        public float Musculature;         // -100 … +100
        public string HairColor;
        public string FacialHairColor;
        public string BrowColor;
        public string SkinColor;
    }

    public static void SaveLook(Look look)
    {
        if (look == null) { PlayerPrefs.DeleteKey(ExtrasKey); PlayerPrefs.Save(); return; }

        var sb = new System.Text.StringBuilder();
        sb.Append("bodyId=").Append(look.BodyShapeId)
          .Append("|bodyType=").Append(look.BodyType.ToString(Inv))
          .Append("|bodySize=").Append(look.BodySize.ToString(Inv))
          .Append("|muscle=").Append(look.Musculature.ToString(Inv));
        Append(sb, "hair", look.HairColor);
        Append(sb, "facialHair", look.FacialHairColor);
        Append(sb, "brow", look.BrowColor);
        Append(sb, "skin", look.SkinColor);

        PlayerPrefs.SetString(ExtrasKey, sb.ToString());
        PlayerPrefs.Save();
        Debug.Log($"[CharacterAppearance] Saved look (body preset {look.BodyShapeId}).");
    }

    /// <summary>The saved look, or null when the character predates this data.</summary>
    public static Look LoadLook()
    {
        string raw = PlayerPrefs.GetString(ExtrasKey, "");
        if (string.IsNullOrEmpty(raw)) return null;

        var look = new Look();
        foreach (string pair in raw.Split('|'))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            string k = pair.Substring(0, eq), v = pair.Substring(eq + 1);
            switch (k)
            {
                case "bodyId":     if (int.TryParse(v, out int id)) look.BodyShapeId = id; break;
                case "bodyType":   look.BodyType     = ParseF(v); break;
                case "bodySize":   look.BodySize     = ParseF(v); break;
                case "muscle":     look.Musculature  = ParseF(v); break;
                case "hair":       look.HairColor       = v; break;
                case "facialHair": look.FacialHairColor = v; break;
                case "brow":       look.BrowColor       = v; break;
                case "skin":       look.SkinColor       = v; break;
            }
        }
        return look;
    }

    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    // Invariant culture throughout: a machine with a comma decimal separator would otherwise write
    // "-12,5" and read it back as garbage.
    static float ParseF(string s) =>
        float.TryParse(s, System.Globalization.NumberStyles.Float, Inv, out float f) ? f : 0f;

    static void Append(System.Text.StringBuilder sb, string key, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        sb.Append('|').Append(key).Append('=').Append(value);
    }
}
