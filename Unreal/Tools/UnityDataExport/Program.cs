using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

/// <summary>
/// Dumps the Unity game's code-defined data into Unreal DataTable JSON (array of rows, each with a
/// "Name" row key and properties named after the UE USTRUCT fields in Source/Wastelanders/Public/Data).
/// Property names here MUST match the UPROPERTY names in WastelandersDataTypes.h.
/// </summary>
public static class Program
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static int Main(string[] args)
    {
        string here = AppContext.BaseDirectory;
        string repo = FindRepoRoot(Directory.GetCurrentDirectory()) ?? FindRepoRoot(here)
                      ?? throw new Exception("Run from inside the repository.");
        string outDir = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(repo, "Unreal", "Content", "Data");
        Directory.CreateDirectory(outDir);
        UnityEngine.Resources.Root = Path.Combine(repo, "Assets", "Resources");

        // Building the recipe list also triggers ItemRegistry.AutoRegisterModelItems.
        var furnace = CraftingRecipes.Furnace();
        var cooking = CraftingRecipes.Cooking();
        var workbench = CraftingRecipes.Workbench();

        Write(outDir, "DT_Items.json", ExportItems());
        Write(outDir, "DT_Recipes.json", ExportRecipes(("Furnace", furnace), ("CookingFire", cooking), ("Workbench", workbench)));
        Write(outDir, "DT_Skills.json", ExportSkills());
        Write(outDir, "DT_Modules.json", ExportModules());
        Write(outDir, "DT_GearSets.json", ExportGearSets());
        Write(outDir, "XPTable_Reference.json", ExportXP());
        Console.WriteLine($"Exported to {outDir}");
        return 0;
    }

    static string FindRepoRoot(string start)
    {
        for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "Assets")) && Directory.Exists(Path.Combine(d.FullName, "Unreal")))
                return d.FullName;
        return null;
    }

    static void Write(string dir, string file, object rows)
    {
        File.WriteAllText(Path.Combine(dir, file), JsonSerializer.Serialize(rows, Json) + "\n", new UTF8Encoding(false));
        Console.WriteLine($"  {file}");
    }

    static List<Dictionary<string, object>> ExportItems()
    {
        var rows = new List<Dictionary<string, object>>();
        foreach (var i in ItemRegistry.All.OrderBy(i => i.id))
        {
            rows.Add(new Dictionary<string, object>
            {
                ["Name"] = $"Item_{i.id}",
                ["ItemId"] = i.id,
                ["DisplayName"] = i.name ?? "",
                ["Description"] = i.description ?? "",
                ["ItemType"] = i.type.ToString(),
                ["bStackable"] = i.stackable,
                ["WeaponStyle"] = i.weaponStyle.ToString(),
                ["ArmourStyle"] = i.armourStyle.ToString(),
                ["AttackBonus"] = i.attackBonus,
                ["StrengthBonus"] = i.strengthBonus,
                ["DefenceBonus"] = i.defenceBonus,
                ["AttackRange"] = i.attackRange,
                ["AttackSpeedTicks"] = i.attackSpeed,
                ["bTwoHanded"] = i.twoHanded,
                ["MagazineSize"] = i.magazineSize,
                ["ReloadSeconds"] = i.reloadSeconds,
                ["HealAmount"] = i.healAmount,
                ["bBuryable"] = i.buryable,
                ["BeastXP"] = i.beastXP,
                ["AmmoSaveChance"] = i.ammoSaveChance,
                ["PaletteTier"] = i.paletteTier,
                ["SetId"] = i.setId ?? "",
                ["SocketCount"] = i.socketCount,
                ["Requirements"] = i.requirements.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            });
        }
        return rows;
    }

    static List<Dictionary<string, object>> ExportRecipes(params (string station, List<CraftingRecipe> list)[] stations)
    {
        var rows = new List<Dictionary<string, object>>();
        var used = new HashSet<string>();
        foreach (var (station, list) in stations)
            foreach (var r in list)
            {
                string key = $"{station}_{Slug(r.name)}";
                for (int n = 2; !used.Add(key); n++) key = $"{station}_{Slug(r.name)}_{n}";
                rows.Add(new Dictionary<string, object>
                {
                    ["Name"] = key,
                    ["DisplayName"] = r.name,
                    ["Station"] = station,
                    ["Inputs"] = r.inputs.Select(x => new Dictionary<string, object> { ["ItemId"] = x.itemId, ["Quantity"] = x.quantity }).ToList(),
                    ["OutputItemId"] = r.outputItemId,
                    ["OutputQuantity"] = r.outputQty,
                    ["Skill"] = r.skill.ToString(),
                    ["LevelRequired"] = r.levelRequired,
                    ["XPGranted"] = r.xpGranted,
                    ["RequiredFlag"] = r.requiredFlag ?? "",
                    ["BurnItemId"] = r.burnItemId,
                    ["NoBurnLevel"] = r.noBurnLevel,
                });
            }
        return rows;
    }

    static List<Dictionary<string, object>> ExportSkills() => SkillData.GetDefaults().Select(s => new Dictionary<string, object>
    {
        ["Name"] = s.skill.ToString(),
        ["Skill"] = s.skill.ToString(),
        ["DisplayName"] = s.displayName,
        ["Description"] = s.description,
        ["LevelUpFlavour"] = s.levelUpFlavour,
        ["MinLevel"] = XPTable.MinLevelFor(s.skill),
        ["MaxLevel"] = XPTable.MaxLevelFor(s.skill),
    }).ToList();

    static List<Dictionary<string, object>> ExportModules() => ModuleCatalog.All.SelectMany(d => new[] { false, true }.Select(refined =>
        new Dictionary<string, object>
        {
            ["Name"] = $"Module_{600 + d.index * 2 + (refined ? 1 : 0)}",
            ["ItemId"] = 600 + d.index * 2 + (refined ? 1 : 0),
            ["CatalogIndex"] = d.index,
            ["DisplayName"] = d.name,
            ["Slot"] = d.slot,
            ["Effect"] = d.effect.ToString(),
            ["Tier"] = d.Tier,
            ["Grade"] = refined ? 2 : 1,
            ["Description"] = d.description,
        })).ToList();

    static List<Dictionary<string, object>> ExportGearSets()
    {
        var f = typeof(GearSets).GetField("Defs", BindingFlags.NonPublic | BindingFlags.Static);
        var defs = (Dictionary<string, GearSets.SetDef>)f.GetValue(null);
        return defs.Values.Select(d => new Dictionary<string, object>
        {
            ["Name"] = d.id,
            ["DisplayName"] = d.displayName,
            ["PieceCount"] = d.pieceCount,
            ["Style"] = d.style.ToString(),
            ["DamageMultiplier"] = d.damageMultiplier,
            ["BonusDefence"] = d.bonusDefence,
            ["CompleteMessage"] = d.completeMessage,
        }).ToList();
    }

    /// <summary>Not a DataTable: golden values the UE automation tests compare the C++ curve against.</summary>
    static object ExportXP() => new Dictionary<string, object>
    {
        ["Standard"] = Enumerable.Range(1, XPTable.MaxLevel).ToDictionary(l => l.ToString(), l => XPTable.XPForLevel(Skill.Attack, l)),
        ["Endurance"] = Enumerable.Range(XPTable.EnduranceStart, XPTable.EnduranceMax - XPTable.EnduranceStart + 1)
                                  .ToDictionary(l => l.ToString(), l => XPTable.XPForLevel(Skill.Endurance, l)),
    };

    static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }
}
