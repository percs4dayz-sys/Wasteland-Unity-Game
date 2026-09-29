using System.Collections.Generic;

public static class ItemRegistry
{
    private static readonly Dictionary<int, ItemData> _items = new();

    static ItemRegistry()
    {
        Add(new ItemData { id=1,  name="Rusty Machete",    description="A worn blade.",          type=ItemType.Weapon,    stackable=false, weaponStyle=WeaponStyle.Melee,  attackBonus=2, strengthBonus=2, attackRange=1, attackSpeed=4, heldModel="HeldModels/machete" });
        Add(new ItemData { id=2,  name="Scrap Shield",     description="Rough metal plating.",   type=ItemType.Shield,    stackable=false, defenceBonus=2, heldModel="HeldModels/shield_scrap" });
        Add(new ItemData { id=3,  name="Pickaxe",          description="For breaking rubble.",   type=ItemType.Tool,      stackable=false, heldModel="HeldModels/pickaxe" });
        Add(new ItemData { id=4,  name="Hatchet",          description="For breaking debris.",   type=ItemType.Tool,      stackable=false, heldModel="HeldModels/hatchet" });
        Add(new ItemData { id=5,  name="Fishing Rod",      description="For fishing wasteland waters.", type=ItemType.Tool,      stackable=false, heldModel="HeldModels/fishing_rod/source/FishingRod" });
        // NOTE: Gathered resources & food do NOT stack (OSRS-style — each takes a slot).
        // Only Ammo stacks. This is intentional per design.
        Add(new ItemData { id=10, name="Scrap Metal",      description="Rough salvaged metal.",  type=ItemType.Resource,  stackable=false });
        Add(new ItemData { id=11, name="Scrap Bar",        description="Smelted metal bar.",     type=ItemType.Resource,  stackable=false });
        Add(new ItemData { id=12, name="Pipe Pistol",      description="A crude handmade gun.",  type=ItemType.Weapon,    stackable=false, weaponStyle=WeaponStyle.Ranged, attackBonus=3, strengthBonus=3, attackRange=2, attackSpeed=3, magazineSize=6, reloadSeconds=1.2f, requirements=new(){[Skill.Marksmanship]=1}, heldModel="HeldModels/Gun/pisol_pipe" });
        Add(new ItemData { id=13, name="Scrap Rounds",     description="Improvised ammo.",       type=ItemType.Ammo,      stackable=true });
        Add(new ItemData { id=22, name="Raw Shrimp",       description="A wriggling wasteland shrimp.", type=ItemType.Resource,  stackable=false });
        Add(new ItemData { id=23, name="Cooked Shrimp",    description="Charred and edible.",    type=ItemType.Consumable,stackable=false, healAmount=2 });
        Add(new ItemData { id=24, name="Burnt Food",       description="You ruined it. Inedible.", type=ItemType.Resource, stackable=false });
        Add(new ItemData { id=30, name="Scrap Helmet",     description="Beaten metal headgear.", type=ItemType.Helmet,    stackable=false, defenceBonus=1, armourMesh="SK_FANT_KNGT_17" });
        Add(new ItemData { id=31, name="Scrap Chestplate", description="Scrap body armor.",      type=ItemType.Chest,     stackable=false, defenceBonus=2, armourMesh="SK_FANT_KNGT_17" });
        Add(new ItemData { id=32, name="Scrap Leggings",      description="Metal-plated leg guards.",    type=ItemType.Legs,     stackable=false, defenceBonus=1, armourMesh="SK_FANT_KNGT_17" });
        Add(new ItemData { id=40, name="Wood Scrap",       description="Salvaged timber.",       type=ItemType.Resource,  stackable=false });
        // Sulphur is the one gathered resource that DOES stack — every ammo recipe burns 5 per
        // batch at every tier, so making it take a slot each would drown the pack. Deliberate
        // exception to the no-stacking rule above.
        Add(new ItemData { id=41, name="Sulphur",          description="Acrid yellow crystals scraped from a vent. Propellant for every grade of ammunition.", type=ItemType.Resource, stackable=true });
        Add(new ItemData { id=50, name="Mottled Egg",      description="Warm to the touch. Something with teeth stirs inside.", type=ItemType.Resource, stackable=false });
        Add(new ItemData { id=51, name="Heavy Egg",        description="Dense as a boulder. Whatever's in there is in no hurry.", type=ItemType.Resource, stackable=false });
        Add(new ItemData { id=52, name="Speckled Egg",     description="Light and warm. Sometimes it chirps back.", type=ItemType.Resource, stackable=false });

        // Bones & remains (ids 80-86) — legacy: no longer dropped (Beastmastery trains through beast
        // tasks now), kept registered so old saves load and stockpiled bones still bury for XP.
        BeastRemains.Register(Add);

        // Tiered combat gear (melee/ranged/armor/ammo, ids 100-135) — data ready ahead of models & recipes.
        TierGear.Register(Add);              // ids 100-135, melee line (heavy plate)
        TierGear.RegisterStarterSet(Add);    // ids 136-139, quest reward
        TierGear.RegisterEndgameSet(Add);    // ids 140-143, island boss drop (melee)
        TierGear.RegisterRangedArmour(Add);  // ids 144-158, ranged line (light kit)
        TierGear.RegisterGodTierWeapons(Add);// ids 159-161, God-Hunter rare table
        TierModules.Register(Add);           // legacy generic modules, retained for old saves
        ModuleCatalog.Register(Add);         // 70 named Common/Refined combat modules

        // Tiered refining materials (raw ores 60-63, refined bars 70-73) — feed the Workbench recipes.
        TierMaterials.Register(Add);

        // Fission line (ids 400-414): geiger counter, raw + refined cores per tier, power gauntlets.
        FissionItems.Register(Add);
        GatheringTools.Register(Add);

        // Tiered gatherables: raw fish 200-203, cooked food 210-213, raw wood 220-223.
        TierGatherables.Register(Add);

        // Small settlement quests (ids 750-753): cheese crackers, treasure map, toilet paper, rabbit's foot.
        SideQuestItems.Register(Add);

        // Readable journal pages (ids 300+) — lore that guides the player toward the endgame.
        JournalLore.Register(Add);
    }

    /// <summary>
    /// Register one item. Ids MUST be unique: a clash used to overwrite silently, which is how the
    /// combat modules sat half-missing behind the tiered fish/food for months (see TierModules).
    /// Now it screams instead, so the next clash is caught the first time the game boots.
    /// </summary>
    private static void Add(ItemData item)
    {
        if (_items.TryGetValue(item.id, out var existing))
            UnityEngine.Debug.LogError(
                $"[ItemRegistry] DUPLICATE item id {item.id}: '{item.name}' overwrites '{existing.name}'. " +
                "Item ids must be unique — move one of them to a free block.");
        item.socketCount = ModuleCatalog.Capacity(item);
        _items[item.id] = item;
    }
    public static ItemData Get(int id) => _items.TryGetValue(id, out var item) ? item : null;
    public static bool Exists(int id) => _items.ContainsKey(id);

    // ── Auto-items from model filenames (see ModelItemNaming) ─────────────────
    // Per-tier stat curve, mirrored from TierGear so auto weapons/shields sit on the same power ladder.
    static readonly int[] TierGate  = { 1, 20, 40, 60, 80 };        // level / combat-level gate per tier
    static readonly int[] MeleeAtk  = { 6, 16, 30, 46, 65 };
    static readonly int[] MeleeStr  = { 8, 15, 28, 42, 58 };
    static readonly int[] RangedAtk = { 3, 16, 30, 46, 65 };
    static readonly int[] RangedStr = { 3, 3, 5, 8, 12 };
    static readonly int[] ShieldDef = { 2, 8, 15, 24, 34 };

    /// <summary>Scan Resources/HeldModels and register an item for every model whose FILENAME follows
    /// the tier/hand convention (ModelItemNaming). Runs once at startup, after the hardcoded catalog is
    /// built. Auto-items live in a reserved id range (4000+) so they never clash with authored items,
    /// and their id is derived from the name so it's stable across sessions.</summary>
    static bool _modelsRegistered;
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void AutoRegisterModelItems()
    {
        if (_modelsRegistered) return;
        _modelsRegistered = true;
        foreach (var go in UnityEngine.Resources.LoadAll<UnityEngine.GameObject>("HeldModels"))
        {
            if (go == null) continue;
            var p = ModelItemNaming.Parse(go.name);
            if (!p.valid) continue;

            int t = UnityEngine.Mathf.Clamp(p.tier, 1, 5) - 1;
            int id = StableAutoId(go.name);

            var item = new ItemData
            {
                id = id,
                name = p.displayName,
                heldModel = go.name,   // resolved by filename via HeldModels.Load
                stackable = false,
            };

            if (p.isShield)
            {
                item.description = $"Tier-{t + 1} off-hand. +{ShieldDef[t]} defence. Needs Hardening level {TierGate[t]}.";
                item.type = ItemType.Shield;
                item.defenceBonus = ShieldDef[t];
                // Shields are armour, so they gate on Hardening like the rest of it.
                item.requirements[Skill.Defence] = TierGate[t];
            }
            else
            {
                item.type = ItemType.Weapon;
                item.weaponStyle = p.isRanged ? WeaponStyle.Ranged : WeaponStyle.Melee;
                item.twoHanded   = p.twoHanded;
                item.attackRange = p.isRanged ? 3 : 1;
                item.attackSpeed = p.twoHanded ? 5 : 4;
                item.attackBonus   = p.isRanged ? RangedAtk[t] : MeleeAtk[t];
                item.strengthBonus = p.isRanged ? RangedStr[t] : MeleeStr[t];
                item.requirements = new System.Collections.Generic.Dictionary<Skill, int>
                    { [p.isRanged ? Skill.Marksmanship : Skill.Attack] = TierGate[t] };
                string hands = p.twoHanded ? "Two-handed" : "One-handed";
                string style = p.isRanged ? "ranged" : "melee";
                item.description = $"Tier-{t + 1} {hands} {style} weapon.";
            }

            item.socketCount = ModuleCatalog.Capacity(item);
        _items[item.id] = item;   // reserved range → safe to overwrite/register
            UnityEngine.Debug.Log($"[ItemRegistry] Auto-item from model '{go.name}' → id {id}: " +
                                  $"{item.name} (T{t + 1}{(p.isShield ? " shield" : p.twoHanded ? " 2H" : " 1H")}).");
        }
    }

    /// <summary>Deterministic id in 4000–5999 from the model name, with linear probing on collision.</summary>
    static int StableAutoId(string name)
    {
        int h = 17;
        unchecked { foreach (char c in name.ToLowerInvariant()) h = h * 31 + c; }
        int id = 4000 + (System.Math.Abs(h) % 2000);
        while (_items.ContainsKey(id) && id < 6000) id++;   // avoid clashing another auto-item
        return id;
    }

    /// <summary>Every registered item (unordered). Handy for dev/test tooling that needs to
    /// enumerate the whole catalog — e.g. the editor-only gear-test panel.</summary>
    public static IEnumerable<ItemData> All => _items.Values;
}
