using System;

/// <summary>
/// The survivor's journal — readable lore pages that guide the player toward the endgame.
/// Each entry registers a Readable item (ids 300+) and carries its full text plus the condition
/// that hands it to the player (JournalManager grants them; JournalUI displays them).
///
///   • Entry 1 (Day 3)   — handed over when you first reach the mainland.
///   • Entry 2 (Day 27)  — at a mid-game total level (points you at the First Guardian).
///   • Entry 5 (Day 184) — at a late-game total level (points you at the Major Boss).
/// </summary>
public static class JournalLore
{
    public class Entry
    {
        public int id;
        public string itemName;          // inventory name
        public string hook;              // short examine line
        public string title;             // header shown in the reading UI
        public string body;              // full journal text
        public bool onMainlandArrival;   // grant the moment you reach the mainland
        public int totalLevelReq;        // OR grant at this total level (0 = ignore)
    }

    public static readonly Entry[] All =
    {
        new Entry
        {
            id = 300, itemName = "Journal Page — Day 3",
            hook = "A water-stained page from a dead survivor. Click to read.",
            title = "Journal Entry #1  ·  Day 3",
            onMainlandArrival = true,
            body =
"Found in a ruined campsite near the starting area.\n\n" +
"If you're reading this, then the land claimed another fool.\n\n" +
"I woke up here three days ago with nothing but the clothes on my back. No roads. No people. " +
"Just wilderness stretching farther than I could see.\n\n" +
"I spent my first night hiding from the things that roam after dark. That was a mistake.\n" +
"The second night I built a shelter. The third night I survived.\n\n" +
"If you want to live, stop looking for answers and start gathering supplies. Wood. Stone. Food. Water.\n" +
"The land rewards preparation and punishes arrogance.\n\n" +
"I've seen strange structures in the distance. Ancient places. Dangerous places. Something powerful " +
"lives beyond them. I can feel it.\n\n" +
"But anyone chasing monsters before learning to survive is digging their own grave.\n\n" +
"Build first. Fight later."
        },
        new Entry
        {
            id = 301, itemName = "Journal Page — Day 27",
            hook = "Another survivor's journal, left near an ancient ruin. Click to read.",
            title = "Journal Entry #2  ·  Day 27",
            totalLevelReq = 150,
            body =
"Found near the first mini-boss arena.\n\n" +
"I've finally learned what this place really is. It's not random.\n\n" +
"The creatures grow stronger the farther you travel from where we arrived. The land is divided into " +
"layers, almost as if something is testing us.\n\n" +
"Today I found evidence of another survivor. Or what remained of one. Broken armor. Torn pack. Huge " +
"claw marks. Nearby, tracks led toward an ancient ruin. Whatever killed them wasn't a normal beast.\n\n" +
"If you're standing where I stood, you've probably become stronger than most creatures roaming the " +
"wilds. Strong enough to hunt instead of hide. Strong enough to challenge the First Guardian.\n\n" +
"But listen carefully. The Guardian is not the end. It's the gate.\n\n" +
"Defeat it and you'll discover something much worse waiting beyond. I almost turned back when I " +
"realized that. Almost."
        },
        new Entry
        {
            id = 302, itemName = "Journal Page — Day 184",
            hook = "A final, frantic entry found before the Major Boss. Click to read.",
            title = "Journal Entry #5  ·  Day 184",
            totalLevelReq = 400,
            body =
"Found shortly before the Major Boss.\n\n" +
"I understand now. The Guardians were never rulers. They were locks.\n\n" +
"Every one I've defeated has opened another piece of this world. Another path. Another secret. " +
"Another nightmare.\n\n" +
"The creature ahead is different. The others defended territory. This one commands it. I've watched " +
"it from a distance for weeks. The sky darkens when it moves. Predators flee its presence. Even the " +
"Guardians feared it.\n\n" +
"If you're reading this, then either I'm dead or I've become part of this place. Maybe that's what " +
"happens to everyone eventually.\n\n" +
"Still, if you've made it this far, you've already accomplished what I once thought impossible. You " +
"survived. You adapted. You conquered every challenge the land placed before you. The person who " +
"arrived here no longer exists. Only the survivor remains.\n\n" +
"If you intend to face the creature beyond these walls, remember one final lesson:\n\n" +
"Strength wasn't found in the weapons you crafted. Or the armor you forged. Or the monsters you " +
"defeated. Strength was earned every time you chose not to give up.\n\n" +
"Now go finish what I started."
        },
    };

    public static Entry Get(int id) => Array.Find(All, e => e.id == id);

    public static void Register(Action<ItemData> add)
    {
        foreach (var e in All)
            add(new ItemData { id = e.id, name = e.itemName, description = e.hook, type = ItemType.Readable, stackable = false });
    }
}
