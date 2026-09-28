using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Filename-driven item convention: name a held-model asset with a few tokens and the game builds a
/// usable item from it automatically (see ItemRegistry auto-registration). Tokens can appear anywhere
/// in the name, in any order, separated by _ - space or just run together:
///
///   TIER   : T1 T2 T3 T4 T5      → gates + stat tier (REQUIRED — this is what marks a model as an item)
///   HAND   : 1H  2H  SHIELD      → one-handed (right), two-handed (both), or off-hand shield (left)
///   STYLE  : any of gun/rifle/pistol/smg/blaster/ranged/bow  → ranged; otherwise melee
///   NAME   : whatever's left      → the display name
///
/// Examples:  "T1_2H_Claymore"  → Tier-1 two-handed melee "Claymore"
///            "Claymore_T1_2H"  → same (order doesn't matter)
///            "T3_Rifle_Vulcan" → Tier-3 ranged "Vulcan" (ranged is always two-handed)
///            "T2_Shield_Bulwark" → Tier-2 off-hand shield "Bulwark"
///            "T1_1H_ScrapKnife"  → Tier-1 one-handed melee "Scrap Knife"
///
/// A model with NO tier token is ignored here (stays a plain held model for a hand-authored item).
/// </summary>
public struct ParsedModelItem
{
    public bool valid;       // had a tier token → treat as an auto-item
    public int  tier;        // 1..5
    public bool isShield;    // off-hand
    public bool twoHanded;
    public bool isRanged;
    public string displayName;
}

public static class ModelItemNaming
{
    static readonly string[] RangedWords = { "gun", "rifle", "pistol", "smg", "blaster", "ranged", "bow", "cannon", "launcher" };

    public static ParsedModelItem Parse(string fileName)
    {
        var p = new ParsedModelItem();
        if (string.IsNullOrEmpty(fileName)) return p;
        string lower = fileName.ToLowerInvariant();

        var tierM = Regex.Match(lower, @"t([1-5])");
        if (!tierM.Success) return p;          // no tier token → not a convention item
        p.valid = true;
        p.tier  = int.Parse(tierM.Groups[1].Value);

        // Strip the tier token so a run-together "T12H" reads the "2H" (the digit-guard below would
        // otherwise reject "2h" sitting right after the tier's "1").
        string rest = lower.Remove(tierM.Index, tierM.Length);

        p.isShield  = rest.Contains("shield") || rest.Contains("offhand");
        p.twoHanded = Regex.IsMatch(rest, @"(^|[^0-9])2h");
        bool oneHand = Regex.IsMatch(rest, @"(^|[^0-9])1h");
        foreach (var w in RangedWords) if (rest.Contains(w)) { p.isRanged = true; break; }
        if (p.isRanged) p.twoHanded = true;    // guns always need both hands
        if (oneHand)    p.twoHanded = false;

        p.displayName = ExtractName(fileName);
        return p;
    }

    /// <summary>Strip the tokens and tidy the rest into a display name (fallback: the raw filename).</summary>
    static string ExtractName(string fileName)
    {
        // Split on delimiters AND camelCase boundaries so "T1_2HClaymore" / "ClaymoreT12H" still name well.
        string spaced = Regex.Replace(fileName, @"([a-z])([A-Z])", "$1 $2");
        var parts = Regex.Split(spaced, @"[_\-\s]+");
        var kept = new StringBuilder();
        foreach (var raw in parts)
        {
            string t = raw.Trim();
            if (t.Length == 0) continue;
            string tl = t.ToLowerInvariant();
            if (Regex.IsMatch(tl, @"^t[1-5]$")) continue;
            if (tl is "1h" or "2h" or "shield" or "offhand" or "melee") continue;
            bool isStyle = false;
            foreach (var w in RangedWords) if (tl == w) { isStyle = true; break; }
            if (isStyle) continue;
            kept.Append(kept.Length > 0 ? " " : "").Append(char.ToUpperInvariant(t[0])).Append(t.Substring(1));
        }
        return kept.Length > 0 ? kept.ToString() : fileName;
    }
}
