using System;
using UnityEngine;
using UnityEngine.Events;

public class TutorialNPC : MonoBehaviour, ITalkableNPC
{
    [SerializeField] private string npcName = "Old Mara";

    public string DisplayName => npcName;
    public string ExamineText => $"{npcName}. A weathered survivor who's lasted longer than most.";
    [SerializeField] private string[] dialogue = {
        "Survivor. Good — I was starting to think I was the only one left out here.",
        "This place is a proving ground. The portal to the north won't open until you've shown some basic competence.",
        "Dig through rubble, scavenge water, salvage wood. Learn to fight, to craft, to sustain yourself.",
        "I've got a few things that might help you get started. Don't waste them.",
        "When all your skills hit level 2, the portal opens. That's all you need to know."
    };

    // Shown once on the second chat — the full skill rundown + a warning about the map's corners.
    [SerializeField] private string[] skillGuide = {
        "Want the rundown? Fine. Here's what you're building, and where to do it.",
        "COMBAT. Bladework is your melee aim — it unlocks sharper blades. Brutality is raw power. Hardening toughens you and lets you wear heavier plate. Endurance is your lifeblood; more of it, the more hits you walk away from.",
        "The trick nobody tells you: hit F7 to change your STANCE. Same swing, different skill — Quick trains Bladework, Heavy trains Brutality, Defensive trains Hardening. Beat on that dummy in each one to round yourself out.",
        "Marksmanship is for guns. Forge a Pipe Pistol at the bench, load some rounds, and put them downrange into the dummy.",
        "GATHERING. Scrapping cracks rubble into scrap. Woodcutting strips wood from debris. Fishing hauls food out of the water.",
        "CRAFTING. The furnace smelts scrap into bars — that's Hearthcraft. The bench builds guns and ammo — Tinkering. The cooking fire turns raw catches into food you can stomach — Sustenance. Eat it to heal.",
        "Beastmastery is your companion. It grows stronger the more it fights at your side. You'll pick yours at the portal before you leave.",
        "Get every one of those to level two and the gate opens. Not a moment before.",
        "Last thing, and listen close: there are things bedded down in the far corners of the wasteland. Big, patient, hungry things. Stay clear of the map's corners until you're hard enough to survive what's waiting in them."
    };

    // Shown when you keep pestering her after she's already kitted you out.
    [SerializeField] private string[] postDialogue = {
        "Don't get greedy, Survivor. Keep pawing at my pack and I'll make you into lunch instead of teaching you how to cook one.",
        "I already gave you a fair start. The rest you earn out there.",
        "Go on. Rubble won't dig itself — and that portal won't open while you're chatting up an old woman."
    };

    private static readonly int[] GiftIds = { 1, 2, 5, 4, 3 }; // Machete, Shield, Rod, Hatchet, Pickaxe

    [Header("Visuals")]
    [SerializeField] private Sprite spriteDown;
    [SerializeField] private Sprite spriteUp;
    [SerializeField] private Sprite spriteLeft;
    [SerializeField] private Sprite spriteRight;
    [Tooltip("3D only: if the model faces the wrong way when you talk to her, nudge this (often 180).")]
    [SerializeField] private float modelYawOffset = 0f;

    private int _postIndex;

    public UnityEvent<string, string> OnDialogue;

    void Start()
    {
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null && spriteDown != null)
            sr.sprite = spriteDown;
    }

    // Public so the 3D Interactor (press E / click-to-move) can start the conversation directly.
    public void Interact()
    {
        var player = PlayerEntity.Instance;
        if (player == null) return;

        FacePlayer(player.transform.position);

        var dlg = DialogueUI.Instance;
        if (dlg == null) return;

        if (dlg.IsConversationWith(npcName))
        {
            dlg.ShowNextLine();
            return;
        }

        // Always top up any missing starter items before saying anything.
        bool itemsReplaced = TryGiveGifts();

        if (!player.HasFlag("mara_gifted"))
        {
            player.SetFlag("mara_gifted");
            dlg.StartDialogue(npcName, dialogue);
            return;
        }

        if (itemsReplaced)
        {
            dlg.StartDialogue(npcName, "Lost your kit already? Try not to make a habit of it.");
            return;
        }

        // Second chat: the full skill guide (what each skill does + where to train it) + boss warning.
        if (!player.HasFlag("mara_guide_shown"))
        {
            player.SetFlag("mara_guide_shown");
            dlg.StartDialogue(npcName, skillGuide);
            return;
        }

        if (postDialogue != null && postDialogue.Length > 0)
            dlg.StartDialogue(npcName, postDialogue[_postIndex++ % postDialogue.Length]);
    }

    private void FacePlayer(Vector3 playerPos)
    {
        var sr = GetComponent<SpriteRenderer>();

        // 3D model (no sprite, or we're in the 3D world): turn the NPC to face the player by YAW
        // ONLY — preserve any X/Z tilt the model needs to stand upright. (LookRotation was flattening
        // her face-down whenever the model's root carried an upright correction.)
        if (sr == null || GameMode.Is3D)
        {
            Vector3 d = playerPos - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.001f)
            {
                Vector3 e = transform.eulerAngles;
                e.y = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + modelYawOffset;
                transform.eulerAngles = e;
            }
            return;
        }

        Vector3 diff = playerPos - transform.position;
        if (Mathf.Abs(diff.x) > Mathf.Abs(diff.y))
        {
            if (diff.x > 0) sr.sprite = spriteRight ?? sr.sprite;
            else sr.sprite = spriteLeft ?? sr.sprite;
        }
        else
        {
            if (diff.y > 0) sr.sprite = spriteUp ?? sr.sprite;
            else sr.sprite = spriteDown ?? sr.sprite;
        }

        // Reset flip since we have directional sprites now
        sr.flipX = false;
    }

    // Returns true if any item was actually handed over.
    private bool TryGiveGifts()
    {
        var player = PlayerEntity.Instance;
        if (player == null) return false;

        var inv   = player.Inventory;
        var equip = player.Equipment;

        bool anyGiven    = false;
        bool fullDetected = false;

        foreach (int id in GiftIds)
        {
            // Already on the player — in inventory or equipped — nothing to replace.
            if (inv.Contains(id)) continue;
            if (equip.GetAll().ContainsValue(id)) continue;

            if (inv.Add(id))
            {
                anyGiven = true;
                var item = ItemRegistry.Get(id);
                HUDController.Emit($"<color=yellow>[INFO]: Received {item.name}</color>");
            }
            else
            {
                fullDetected = true;
            }
        }

        if (fullDetected)
            HUDController.Emit("<color=red>[WARNING]: Your inventory was too full to receive some items!</color>");

        if (anyGiven && !player.HasFlag("mara_gifted"))
            HUDController.Emit("<color=green>[Old Mara]: Use these well. Survival isn't a gift, it's a habit.</color>");

        return anyGiven;
    }
}
