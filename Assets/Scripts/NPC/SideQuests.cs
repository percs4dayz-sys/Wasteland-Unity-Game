using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One step of a small settlement quest. Either an NPC stage (<see cref="npc"/> set — that NPC
/// runs it when talked to) or a world-site stage (<see cref="siteName"/> set — a spot in the world
/// the player interacts with). Progress is one saved flag per quest ("sq:id:sN"), so it rides the
/// existing flag save with no SaveData changes.
///
/// First visit plays <see cref="offer"/>. If the requirement is met (null = always) it goes straight on
/// to <see cref="complete"/> and advances; otherwise later visits play <see cref="reminder"/>.
/// </summary>
public class SideQuestStage
{
    public string npc;
    public string[] offer = Array.Empty<string>();
    public string[] reminder = Array.Empty<string>();
    public string[] complete = Array.Empty<string>();
    public Func<PlayerEntity, bool> requirement;
    public bool needsRoom;                              // reward lands in the bag, so wait for a free slot
    public Action<PlayerEntity, Vector3> onVisit;       // every visit while active — must be idempotent
    public Action<PlayerEntity> onComplete;

    // world-site stage
    public string siteName, siteExamine;
    public int siteTier = 1;
    public Color siteColor = new Color(0.55f, 0.4f, 0.25f);
}

public class SideQuest
{
    public string id, title;
    public SideQuestStage[] stages;
}

public static class SideQuests
{
    // ── content ──────────────────────────────────────────────────────────────
    public const string PestRabbitDeadFlag = "sq:pest:rabbit_dead";
    public const string GiantRabbitName = "Giant Rabbit";

    public static readonly SideQuest[] All =
    {
        new SideQuest { id = "vending", title = "The Sacred Vending Machine", stages = new[]
        {
            new SideQuestStage
            {
                npc = "Deacon Pruitt",
                offer = new[]
                {
                    "Traveler. You come at a terrible hour. The Machine has fallen silent.",
                    "For two hundred years it has watched over us. We leave coins at its feet. Some say it hums at dawn. Then this morning the Great Coin Slot began to grind, and now it does nothing.",
                    "I need two scrap bars to forge a new coin mechanism. Bring them, and I will bless your name at the next sermon.",
                },
                reminder = new[] { "Two scrap bars for the coin mechanism, traveler. The faithful grow restless." },
                complete = new[]
                {
                    "<i>He fits the bars into the Great Coin Slot with trembling hands. The Machine shudders... and drops its treasure.</i>",
                    "A packet. Sealed. Orange. The elders' scrolls speak of... cheese crackers.",
                    "Two hundred years of prayer, and this is what it was guarding. Cheese crackers.",
                    "...Tell no one. The faith survives on mystery. Take a packet, I insist. I cannot look at them.",
                },
                requirement = p => p.Inventory.Contains(11, 2),
                needsRoom = true,
                onComplete = p =>
                {
                    p.Inventory.Remove(11, 2);
                    p.Inventory.Add(SideQuestItems.CheeseCrackers);
                    p.Stats.AddXP(Skill.Smithing, 150);
                },
            },
        }},

        new SideQuest { id = "cult", title = "We're Not a Cult", stages = new[]
        {
            new SideQuestStage
            {
                npc = "Gideon Marsh",
                offer = new[]
                {
                    "My brother Tobias joined a cult out east. He stopped answering my letters.",
                    "The Sanctuary of the Warm Light, they call themselves. I'll pay you to bring him home, whatever it takes. Talk some sense into him.",
                },
            },
            new SideQuestStage
            {
                npc = "Brother Tobias",
                offer = new[]
                {
                    "Oh! Visitors. Did Gideon send you? Of course he did.",
                    "Look, we're not a cult. We're a community. With a schedule.",
                    "Come in, sit down. Have some soup. It's hot soup. It has actual vegetables in it.",
                },
            },
            new SideQuestStage
            {
                npc = "Brother Tobias",
                offer = new[]
                {
                    "So here's what I get: electricity. A hot shower, genuinely hot. Three meals a day. Nobody has tried to eat me in eleven weeks.",
                    "The only rule is I fold laundry on Thursdays. Tell my brother I'm staying.",
                    "And honestly? You should ask about the waiting list.",
                },
            },
            new SideQuestStage
            {
                npc = "Gideon Marsh",
                needsRoom = true,
                offer = new[]
                {
                    "He's staying? Did he say why?",
                    "...Hot showers.",
                    "Hm. Hot. Showers.",
                    "Here, your pay, as promised. You didn't bring him home, but I'm not paying you to fail.",
                    "...Where is this compound again? Asking for a friend.",
                },
                onComplete = p => p.Stats.AddXP(Skill.Endurance, 200),
            },
        }},

        new SideQuest { id = "pest", title = "Pest Control", stages = new[]
        {
            new SideQuestStage
            {
                npc = "Farmer Hale",
                offer = new[]
                {
                    "Something's been eating my crops. Whole rows, every night.",
                    "I figured mutant locusts. Radroaches. Something normal. Then I saw it out by the fence.",
                    "Kill it, please. Whatever it is. I'll make it worth your while.",
                },
                reminder = new[] { "It's out past my field. Big. Fluffy. Please don't let it look at you." },
                complete = new[]
                {
                    "You killed it? ...It was a rabbit, wasn't it. I saw the ears.",
                    "Just a rabbit. A very, very large rabbit.",
                    "Take the foot. I'm not keeping it. It's got a look.",
                },
                requirement = p => p.HasFlag(PestRabbitDeadFlag),
                needsRoom = true,
                onVisit = (p, npcPos) => SideQuestActors.EnsureGiantRabbit(p, npcPos),
                onComplete = p =>
                {
                    p.Inventory.Add(SideQuestItems.RabbitFoot);
                    p.Stats.AddXP(Skill.Attack, 200);
                },
            },
        }},

        new SideQuest { id = "map", title = "The Treasure Map", stages = new[]
        {
            new SideQuestStage
            {
                npc = "Old Dusty",
                needsRoom = true,
                offer = new[]
                {
                    "<i>*cough*</i> You. Come closer.",
                    "I found it, kid. The pre-war stash. Three places, I marked them. Do them in order.",
                    "Take the map. It's worth... everything...",
                    "<i>He goes still. The map is in your hand.</i>",
                },
                onComplete = p => p.Inventory.Add(SideQuestItems.TreasureMap),
            },
            new SideQuestStage
            {
                siteName = "Circled Location I", siteTier = 1,
                siteExamine = "An X on Old Dusty's map. The ground has been disturbed.",
                complete = new[]
                {
                    "You dig. A rusted lockbox. Inside: a note.",
                    "'Ha! Not here. Try the next one.' The handwriting is Dusty's. Odd, for a dying man.",
                },
            },
            new SideQuestStage
            {
                siteName = "Circled Location II", siteTier = 2,
                siteExamine = "The second X on Old Dusty's map. Something is buried here.",
                complete = new[]
                {
                    "Another lockbox. Another note.",
                    "'Getting warmer. Also, I lied about dying a little.'",
                },
            },
            new SideQuestStage
            {
                siteName = "Circled Location III", siteTier = 3, needsRoom = true,
                siteExamine = "The third X. A hatch is half-buried in the ground.",
                complete = new[]
                {
                    "You pry open a pre-war vault. Dust. Shelves. Silence. And, in the middle of it all, one pristine roll of two-ply toilet paper.",
                    "A plaque underneath reads: 'FOR THE BRAVE, THE PATIENT, AND THE UNPREPARED.'",
                    "It is, honestly, the most valuable thing you've found all week.",
                },
                onComplete = p =>
                {
                    p.Inventory.Add(SideQuestItems.ToiletPaper);
                    p.Stats.AddXP(Skill.Scrapping, 250);
                },
            },
        }},
    };

    /// <summary>Where each NPC lives and what they say when they have no active stage for you.</summary>
    public class NpcDef
    {
        public string name, examine, quest;
        public int tier;
        public Color color;
        public string[] idle, afterDone;
    }

    public static readonly NpcDef[] Npcs =
    {
        new NpcDef { name = "Deacon Pruitt", quest = "vending", tier = 1, color = new Color(0.45f, 0.3f, 0.5f),
            examine = "Keeper of the Sacred Vending Machine. Wears an apron that says HOLY.",
            idle = new[] { "The Machine sees all. It just can't dispense anything right now." },
            afterDone = new[] { "The faith endures. Do not ask what's in the packet." } },
        new NpcDef { name = "Gideon Marsh", quest = "cult", tier = 1, color = new Color(0.4f, 0.4f, 0.45f),
            examine = "A worried man with a stack of unanswered letters.",
            idle = new[] { "Any word from Tobias?" },
            afterDone = new[] { "I'm not going. I'm just... thinking about hot water." } },
        new NpcDef { name = "Brother Tobias", quest = "cult", tier = 2, color = new Color(0.9f, 0.88f, 0.75f),
            examine = "Wears clean robes. Smells of soap. Suspicious.",
            idle = new[] { "We're not a cult. We're a community with a dress code." },
            afterDone = new[] { "Thursday is laundry day. Won't you join us?" } },
        new NpcDef { name = "Farmer Hale", quest = "pest", tier = 1, color = new Color(0.35f, 0.5f, 0.25f),
            examine = "Stares at his fence a lot.",
            idle = new[] { "My crops. My poor crops." },
            afterDone = new[] { "Harvest is back. I still check the fence every hour." } },
        new NpcDef { name = "Old Dusty", quest = "map", tier = 1, color = new Color(0.65f, 0.55f, 0.35f),
            examine = "A scavenger propped against a wall, breathing shallowly.",
            idle = new[] { "<i>*cough*</i> Closer, kid..." },
            afterDone = new[] { "<i>Old Dusty is not moving. You're fairly sure he's smirking.</i>" } },
    };

    // ── progress (saved player flags) ────────────────────────────────────────
    static string StageFlag(SideQuest q, int n) => $"sq:{q.id}:s{n}";
    static string SeenFlag(SideQuest q, int n) => $"sq:{q.id}:seen{n}";

    public static int Stage(PlayerEntity p, SideQuest q)
    {
        for (int i = 0; i <= q.stages.Length; i++) if (p.HasFlag(StageFlag(q, i))) return i;
        return 0;
    }

    static void SetStage(PlayerEntity p, SideQuest q, int n)
    {
        for (int i = 0; i <= q.stages.Length; i++) p.RemoveFlag(StageFlag(q, i));
        p.SetFlag(StageFlag(q, n));
    }

    public static bool IsDone(PlayerEntity p, string questId)
    {
        var q = Array.Find(All, x => x.id == questId);
        return q != null && Stage(p, q) >= q.stages.Length;
    }

    public static SideQuest Find(string id) => Array.Find(All, q => q.id == id);

    // ── running a stage ──────────────────────────────────────────────────────
    /// <summary>Runs whatever stage <paramref name="q"/> is on for the given speaker.
    /// Returns the lines to show (null = nothing to say) and advances the quest when it completes.</summary>
    static string[] Run(PlayerEntity p, SideQuest q, int n, Vector3 where)
    {
        var s = q.stages[n];
        s.onVisit?.Invoke(p, where);

        bool first = !p.HasFlag(SeenFlag(q, n));
        bool met = s.requirement == null || s.requirement(p);
        if (met && s.needsRoom && p.Inventory.IsFull())
            return new[] { "Your bag is full. Make some room, then come back." };

        p.SetFlag(SeenFlag(q, n));
        if (!met) return first ? s.offer : (s.reminder.Length > 0 ? s.reminder : s.offer);

        var lines = new List<string>();
        if (first) lines.AddRange(s.offer);
        lines.AddRange(s.complete);
        s.onComplete?.Invoke(p);
        SetStage(p, q, n + 1);
        if (n + 1 >= q.stages.Length) HUDController.Emit($"<color=#7FE77F>Quest complete:</color> {q.title}");
        else if (first) HUDController.Emit($"<color=#FFD966>Quest updated:</color> {q.title}");
        return lines.ToArray();
    }

    /// <summary>Talk to an NPC: run the active NPC-stage they own, else fall back to idle lines.</summary>
    public static void Talk(NpcDef def, Vector3 pos)
    {
        var p = PlayerEntity.Instance;
        var dlg = DialogueUI.Instance;
        if (p == null || dlg == null) return;
        if (dlg.IsConversationWith(def.name)) { dlg.ShowNextLine(); return; }

        foreach (var q in All)
        {
            int n = Stage(p, q);
            if (n >= q.stages.Length || q.stages[n].npc != def.name) continue;
            bool firstVisit = !p.HasFlag(SeenFlag(q, n));
            var lines = Run(p, q, n, pos);
            if (lines != null && lines.Length > 0)
            {
                if (firstVisit && n == 0) HUDController.Emit($"<color=#FFD966>Quest started:</color> {q.title}");
                dlg.StartDialogue(def.name, lines);
                return;
            }
        }
        var idle = IsDone(p, def.quest) ? def.afterDone : def.idle;
        dlg.StartDialogue(def.name, idle[UnityEngine.Random.Range(0, idle.Length)]);
    }

    /// <summary>Interact with a world site. Does nothing unless the quest is on exactly that stage.</summary>
    public static void Site(string questId, int stageIndex, Vector3 pos)
    {
        var p = PlayerEntity.Instance;
        var dlg = DialogueUI.Instance;
        var q = Find(questId);
        if (p == null || dlg == null || q == null) return;
        if (Stage(p, q) != stageIndex)
        {
            dlg.StartDialogue("Marked Spot", "Nothing here for you right now.");
            return;
        }
        var lines = Run(p, q, stageIndex, pos);
        if (lines != null && lines.Length > 0) dlg.StartDialogue(q.stages[stageIndex].siteName, lines);
    }
}
