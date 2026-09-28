using System;
using UnityEngine;

/// <summary>
/// Roxy — the flirty mainland guide who hands out the seven "starter training" quests, one per
/// non-combat skill (the gathering + crafting + Beastmastery loop). She replaces relying on the
/// tutorial island: a friendly face on the mainland who tells you HOW to earn your first level in
/// each skill, in her own... distinctive voice.
///
/// PATH 1 (this version): pure flavor/guidance. Talking to her walks through the quests one at a
/// time; the actual skill level-ups are handled by the normal gameplay systems (fish a spot, smash
/// rubble, etc.). No accept/track/turn-in yet.
///
/// PATH 2 (later): wire each quest to the matching skill so it auto-completes on your first level in
/// that skill and Roxy hands a reward. The data below (one Quest per skill) is laid out so that's a
/// "fill in the tracking" change rather than a rewrite — see the `skill` field on each Quest.
///
/// Hook-up: drop this on the Roxy model in the mainland scene. The 3D Interactor (click / press E)
/// calls Interact(), exactly like TutorialNPC. Self-contained — no other wiring needed.
/// </summary>
public class RoxyNPC : MonoBehaviour, ITalkableNPC
{
    [SerializeField] private string npcName = "Roxy";

    public string DisplayName => npcName;
    public string ExamineText => $"{npcName}. Trouble in all the best ways. She's got work for you.";

    [Tooltip("3D only: if the model faces the wrong way when you talk to her, nudge this (often 180).")]
    [SerializeField] private float modelYawOffset = 0f;

    /// <summary>One starter quest. `skill` is unused in Path 1 but already wired so Path 2 can match a
    /// completed level-up to the right quest without restructuring this data.</summary>
    [Serializable]
    public class Quest
    {
        public Skill skill;
        public string title;
        [TextArea(4, 12)] public string line;
        /// <summary>Item Roxy hands over when she gives this quest (the tool/material you need to do it).
        /// 0 = nothing. Given once per quest. See ItemRegistry ids.</summary>
        public int giveItemId;
        public int giveQty = 1;
    }

    /// <summary>The Beastmastery lesson: Slayer-style beast tasks (see BeastTasks). Scenes that saved
    /// the older "bury these bones" version are updated on the fly by MigrateBeastLesson.</summary>
    const string BeastLessonLine =
        "Alright, gorgeous, time to get a little blood on those boots. Out here you're only as good as the beast at your side — so I'm letting you pick one of my little monsters. Here's the deal: your beast always has a hunt lined up. A head count of one nasty kind of critter, matched to how far you've come — the better your Beastmastery, the meaner the prey. Call your beast with B, put them down together, and every kill on the list makes you a better Beastmaster. Kills without your beast don't count, sweetcheeks — no cheating on your pet. Finish the count and the next hunt finds you on its own. Now go on, choose your new best friend.";

    // Pre-filled with Roxy's seven quests. All editable in the Inspector.
    [SerializeField]
    private Quest[] quests =
    {
        new Quest { skill = Skill.Fishing, giveItemId = 5, title = "Hooked on You",   // Fishing Rod
            line = "Well, look at those forearm muscles. You look like you could wrestle a mutant brute, but let's start with something a bit more... relaxing. See that puddle of glowing sludge over there? That's a 'Basic Fishing Spot.' Grab this rod and go pull something out of it. Don't worry, the extra glowing limbs on the fish just mean more protein. The very first wet, slimy thing you yank out will magically trigger a 'Level Up' notification in your brain. Don't fight it, babe, just let the dopamine wash over you. Now go on, show me your technique." },

        new Quest { skill = Skill.Woodcutting, giveItemId = 4, title = "Stripping Down",   // Hatchet
            line = "Mmm, I love a man who isn't afraid of hard labor. Here, take this hatchet — you'll need something to swing. See those dead trees out past the square? Go chop one down for me. Just keep whacking until a piece of 'Wood Scrap' pops into existence. The universe has this hilarious rule where your first successful piece of garbage instantly makes you a certified expert Woodcutter. It makes absolutely no sense, but hey, I don't make the code, I just live in it. Go break a sweat, handsome. I'll be right here watching." },

        new Quest { skill = Skill.Scrapping, giveItemId = 3, title = "Heavy Metal Romance",   // Pickaxe
            line = "You know what's hotter than a guy with a sword? A guy with a pickaxe — here, take mine. There's a 'Rubble Pile' over there that's just begging for a pounding. Go smash it up until you find some 'Scrap Metal.' The second that rusty piece of junk hits your inventory, the game is going to grant you your first level in Scrapping. It's a dark, miserable world, but watching you smash old trash into neat little resource nodes really brightens up my apocalypse. Get to it!" },

        new Quest { skill = Skill.Cooking, giveItemId = 22, title = "Something's Fishy",   // Raw Shrimp (to cook)
            line = "Here — I saved you one of the radioactive flopping things I pulled out of the water earlier. Now, as much as I love a raw diet, if you eat that thing right now, your teeth will probably fall out. Take it over to that Cooking Fire and roast it. The moment you turn that biohazard into a 'Cooked Meal,' your Cooking skill will level up. It's a literal life-saver. Plus, if you don't burn it to a crisp, maybe I'll let you feed me a bite. Come on, cook for me, chef." },

        new Quest { skill = Skill.Smithing, giveItemId = 10, title = "Sparks Will Fly",   // Scrap Metal (to smelt)
            line = "Is it hot out here, or is it just you? Let's turn up the heat. Here's a chunk of scrap metal — shove it into the Furnace to melt it into a 'Scrap Bar.' That first bar unlocks your Smithing skill — the same one you'll level forging that bar into weapons and armor later. I highly recommend hanging around the fire while you're at it — the lighting does wonders for your jawline, and the wasteland gets so terribly cold at night when you're sleeping alone." },

        new Quest { skill = Skill.Smithing, giveItemId = 11, title = "Hardening Your Assets",   // Scrap Bar (to craft)
            line = "Here's a shiny scrap bar to get you started — because walking around this wasteland half-naked is my job, not yours, even if I highly enjoy the view. Take that bar over to the Workbench and forge your very first item. 'Scrap Armor' is the tier-1 unlock, which is perfect because I prefer my men heavily protected... at least until we get back to camp. Forging it levels the same Smithing skill you started at the furnace. Go build yourself protection, sweetcheeks. You're going to need it." },

        new Quest { skill = Skill.Beastmastery, giveItemId = 0, title = "Deader is Better",   // opens the beast picker (see Interact)
            line = BeastLessonLine },

        // Advanced quest: introduces the fission gathering loop + hands the Geiger Counter (id 400).
        new Quest { skill = Skill.Scrapping, giveItemId = FissionItems.GeigerCounter, title = "Hot Stuff",
            line = "Ooh, now we're getting to the dangerous part — my favorite. Take this Geiger Counter, handsome. Hold onto it and it'll start clicking whenever one of the big Elemental Golems is stomping around out there. The faster it ticks, the closer you are, so let it lead you right to the brute. Then? You put it DOWN. Beat that walking reactor into a corpse. Once it's cold, you crack open its body and strip the fission cores out of it — same as smashing rubble, just with more radiation and better loot. Farm it 'til there's nothing left, then follow the clicks to wherever the next one crawled up. Refine what you pull, load it into a pair of power gauntlets, and you'll be punching holes in the world. Bring me back something that glows, sweetcheeks — I'll make it worth your while." },
    };

    // Said the very first time you walk up to her, before the first quest.
    [SerializeField]
    private string greeting =
        "Hey there, survivor. Aren't you a sight for sore eyes out here. Stick with me and I'll teach you how to stay alive — lesson by lesson. Talk to me whenever you're ready for the next one.";

    private int _index;
    private bool _greeted;
    public const string CombatTrainingFlag = "roxy_combat_training";
    public const string FissionGivenFlag = "roxy_fission_given";
    static readonly Skill[] CombatRequirements = { Skill.Attack, Skill.Strength, Skill.Defence, Skill.Marksmanship };

    public static bool ReadyForFission(PlayerStats stats)
    {
        if (stats == null) return false;
        foreach (var skill in CombatRequirements) if (stats.GetLevel(skill) < 10) return false;
        return true;
    }

    public static string CombatProgress(PlayerStats stats) =>
        $"Attack {stats.GetLevel(Skill.Attack)}/10, Strength {stats.GetLevel(Skill.Strength)}/10, " +
        $"Defence {stats.GetLevel(Skill.Defence)}/10, Marksmanship {stats.GetLevel(Skill.Marksmanship)}/10";

    // Separate the advanced handoff from the serialized starter lessons, including old scenes.
    int StarterCount
    {
        get
        {
            int index = Array.FindIndex(quests, q => q != null && q.giveItemId == FissionItems.GeigerCounter);
            return index >= 0 ? index : quests.Length;
        }
    }

    public static bool TryGiveFissionKit(PlayerEntity player)
    {
        if (player == null || !ReadyForFission(player.Stats)) return false;
        foreach (int item in new[] { FissionItems.GeigerCounter, FissionItems.Gauntlets })
        {
            string flag = "roxy_kit:" + item;
            if (player.HasFlag(flag)) continue;
            bool owns = player.Inventory.Contains(item) || player.Bank.Contains(item) ||
                player.Equipment.GetItemId(item == FissionItems.GeigerCounter ? "Tool" : "Weapon") == item;
            if (!owns && !player.Inventory.Add(item)) return false;
            // Save each award immediately; a full bag between awards must not duplicate the first.
            player.SetFlag(flag);
        }
        player.SetFlag(FissionGivenFlag);
        player.RemoveFlag(CombatTrainingFlag);
        return true;
    }

    void AdvancedLesson(PlayerEntity player, DialogueUI dlg)
    {
        if (player.HasFlag(FissionGivenFlag))
        {
            dlg.StartDialogue(npcName, "Follow your Geiger Counter toward an Earth Golem in the starter region. Defeat it, scrap its remains with your pickaxe, then refine the raw cores at a furnace. Equip your power gauntlets and load the refined cores as ammunition. Scrapping supplies the cores, Refinement prepares them, and firing the gauntlets trains Fission. Higher-tier golems supply stronger cores. Try not to glow brighter than me, sweetcheeks.");
            return;
        }
        if (!ReadyForFission(player.Stats))
        {
            StarterGuide.AwaitCombatTraining();
            dlg.StartDialogue(npcName, "You've learned the basics, gorgeous. Before I introduce you to Fission, get Attack, Strength, Defence AND Marksmanship to level 10. All four. Come back when you've done that and I'll give you a Geiger Counter to find your first Elemental Golem.", CombatProgress(player.Stats));
            return;
        }
        if (!TryGiveFissionKit(player))
        {
            dlg.StartDialogue(npcName, "Make room in your bag for your Geiger Counter and power gauntlets, then talk to me again. I'm keeping your lesson ready.");
            return;
        }
        dlg.StartDialogue(npcName, "— Hot Stuff —", "All four combat skills at level 10. Now we're getting dangerous. Your Geiger Counter and power gauntlets are ready. Keep the counter in your bag or equip it as a tool; its readout points you toward a nearby Elemental Golem. If you already stored your counter in the bank, take it out first.",
            "Start with an Earth Golem here in the starter region. Defeat it with your usual weapons, then use your pickaxe to scrap its body for raw fission cores. That's Scrapping. Take the cores to a furnace and refine them — that trains Refinement. Finally, equip the power gauntlets and the refined cores in your ammo slot. Dealing damage with them trains Fission. Better golems yield stronger cores as those skills grow. Go make something glow, handsome.");
        StarterGuide.LessonGiven(Skill.Scrapping, FissionItems.GeigerCounter);
    }

    // Called by the 3D Interactor (click-to-move / press E), same contract as TutorialNPC.
    public void Interact()
    {
        var player = PlayerEntity.Instance;
        if (player == null) return;
        LoadProgress(player);

        FacePlayer(player.transform.position);

        var dlg = DialogueUI.Instance;
        if (dlg == null) return;
        dlg.EnableBankGuideFor(npcName);
        if (BankGuideUI.IsShowing) return;

        // Mid-conversation: just advance the current lines.
        if (dlg.IsConversationWith(npcName))
        {
            dlg.ShowNextLine();
            return;
        }

        // Separate flag: inserting this lesson must not shift saved quest or gift indices.
        if (!player.HasFlag(BankGuideUI.LessonFlag) && BankGuideUI.HasTownBanks)
        {
            BankGuideUI.Show(npcName, true);
            return;
        }

        // She's set the Beastmastery lesson but you closed the picker without choosing: re-offer it every
        // visit until you pick. Your first beast task arrives the moment you do (see BeastTasks).
        if (player.HasFlag("roxy_beast_given") && !player.HasFlag("egg_chosen"))
        {
            dlg.StartDialogue(npcName,
                "Still haven't picked? Go on — choose your companion. The moment you do, your first hunt is waiting for you. And be nice, they bite.");
            EggChoiceUI.Show();
            return;
        }

        player.SetFlag(StarterGuide.MetFlag);   // the new-player guide stops pointing everyone at her

        if (quests == null || quests.Length == 0)
        {
            if (!_greeted) { _greeted = true; dlg.StartDialogue(npcName, greeting); }
            return;
        }
        // Talking again replays the active lesson; it must not advance the quest index
        // and replace its objective before the player has actually trained the skill.
        if (StarterGuide.TryActiveLesson(player, out var activeSkill, out int activeItem))
        {
            if (activeSkill == Skill.Beastmastery)
            {
                if (!player.HasFlag("egg_chosen"))
                {
                    dlg.StartDialogue(npcName, BeastLessonLine);
                    EggChoiceUI.Show();
                }
                else
                {
                    // Mid-hunt: say where the count stands instead of replaying the whole lesson, so it's
                    // clear she's still waiting on this hunt and hasn't moved on.
                    var task = BeastTasks.Current;
                    dlg.StartDialogue(npcName, task != null
                        ? $"You're not done yet, sweetcheeks. {task.done}/{task.required} {task.creature} down. Keep your beast out and finish the count, then come see me."
                        : "Your beast is sniffing out your first hunt. Keep it out with you, finish the whole count, then come see me.");
                    CompanionManager.Instance?.Summon();
                }
            }
            else
            {
                var active = Array.Find(quests, lesson => lesson != null && lesson.skill == activeSkill && lesson.giveItemId == activeItem);
                if (active != null) dlg.StartDialogue(npcName, $"— {active.title} —", active.line);
                else dlg.StartDialogue(npcName, "Finish the lesson shown in your objective, then come back to me.");
            }
            return;
        }
        if (_index >= StarterCount) { AdvancedLesson(player, dlg); return; }
        int qi = _index;
        var q = quests[qi];
        // The Beastmastery lesson hands you your first beast right away (the picker opens with her
        // lines); the flag makes her re-offer it if you close the picker without choosing.
        bool beastLesson = q.skill == Skill.Beastmastery;
        if (beastLesson) player.SetFlag("roxy_beast_given");

        // Hand over the tool/material this quest needs — once. If the bag's full and it can't land, we
        // DON'T mark it given, so she'll try again next time you talk.
        string giftFlag = "roxy_gift:" + qi;
        if (!player.HasFlag(giftFlag))
        {
            if (!GiveQuestItem(q)) return;
            player.SetFlag(giftFlag);
        }

        // First-ever chat: greet, then immediately roll into the first quest.
        if (!_greeted)
        {
            _greeted = true;
            _index = 1;
            SaveProgress(player);
            dlg.StartDialogue(npcName, greeting, $"— {q.title} —", q.line);
            StarterGuide.LessonGiven(q.skill, q.giveItemId);   // camera glide + objective marker on where it's done
            if (beastLesson && !player.HasFlag("egg_chosen")) EggChoiceUI.Show();
            return;
        }

        _index++;
        SaveProgress(player);
        dlg.StartDialogue(npcName, $"— {q.title} —", q.line);
        StarterGuide.LessonGiven(q.skill, q.giveItemId);   // camera glide + objective marker on where it's done
        if (beastLesson && !player.HasFlag("egg_chosen")) EggChoiceUI.Show();
    }

    // Her place in the lessons lives in the player's saved flags, so closing the game doesn't start her over
    // at "Hey there, survivor" with lesson one.
    const string GreetedFlag = "roxy_greeted", NextFlag = "roxy_next:";
    PlayerEntity _loadedFor;

    void LoadProgress(PlayerEntity player)
    {
        if (_loadedFor == player) return;
        _loadedFor = player;
        if (player.HasFlag(GreetedFlag)) _greeted = true;
        foreach (var f in player.GetAllFlags())
            if (f.StartsWith(NextFlag) && int.TryParse(f.Substring(NextFlag.Length), out int next)) _index = Mathf.Max(_index, next);
    }

    void SaveProgress(PlayerEntity player)
    {
        if (_greeted) player.SetFlag(GreetedFlag);
        var old = new System.Collections.Generic.List<string>();
        foreach (var f in player.GetAllFlags()) if (f.StartsWith(NextFlag)) old.Add(f);
        foreach (var f in old) player.RemoveFlag(f);
        player.SetFlag(NextFlag + _index);
    }

    void Awake() => MigrateBeastLesson();

    /// <summary>Scenes saved before the beast-task rework still carry the old "bury these bones" lesson
    /// in their serialized quest list (serialized values beat the code defaults). Swap it for the task
    /// lesson and stop handing out bones, so every Roxy teaches the same thing with no scene edits.</summary>
    void MigrateBeastLesson()
    {
        if (quests == null) return;
        foreach (var q in quests)
        {
            if (q == null || q.skill != Skill.Beastmastery) continue;
            if (q.giveItemId == BeastRemains.CrackedBones) q.giveItemId = 0;
            if (q.line != null && q.line.IndexOf("bury", StringComparison.OrdinalIgnoreCase) >= 0) q.line = BeastLessonLine;
        }
    }

    /// <summary>Give the quest's tool/material to the player. Returns true if it's handled (landed in the bag,
    /// the player already has one, or there was nothing to give). A full bag is a no: she keeps the lesson
    /// for next time instead of stuffing the item somewhere or handing out duplicates.</summary>
    private bool GiveQuestItem(Quest q)
    {
        if (q == null || q.giveItemId <= 0) return true;
        var player = PlayerEntity.Instance;
        var item = ItemRegistry.Get(q.giveItemId);
        if (player == null || item == null) return false;

        int qty = Mathf.Max(1, q.giveQty);
        if (player.Inventory.Contains(q.giveItemId, qty) || IsEquipped(player, q.giveItemId))
        {
            HUDController.Emit($"<color=#FF7AB0>[Roxy]:</color> You've already got a {item.name}, so I'll hang on to mine.");
            return true;
        }
        if (player.Bank != null && player.Bank.Contains(q.giveItemId, qty))
        {
            HUDController.Emit($"<color=#FF7AB0>[Roxy]:</color> You've already got a {item.name} in your bank, sweetcheeks. Take it out for this one.");
            return true;
        }

        string n = qty > 1 ? qty + "x " : "";
        if (player.Inventory.Add(q.giveItemId, qty))
        {
            HUDController.Emit($"<color=#FF7AB0>[Roxy]:</color> Here, take {n}{item.name} — you'll want it for this one. <i>*winks*</i>");
            return true;
        }
        HUDController.Emit($"<color=#FF7AB0>[Roxy]:</color> Your bag's stuffed. Make some room and I'll hand you the {item.name}.");
        return false;
    }

    static bool IsEquipped(PlayerEntity player, int itemId)
    {
        if (player.Equipment == null) return false;
        foreach (var worn in player.Equipment.GetAll().Values) if (worn == itemId) return true;
        return false;
    }

    private void FacePlayer(Vector3 playerPos)
    {
        // Yaw-only turn so any upright tilt the model needs is preserved (matches TutorialNPC's 3D path).
        Vector3 d = playerPos - transform.position; d.y = 0f;
        if (d.sqrMagnitude > 0.001f)
        {
            Vector3 e = transform.eulerAngles;
            e.y = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + modelYawOffset;
            transform.eulerAngles = e;
        }
    }
}
