using UnityEngine;

/// <summary>
/// A townsperson you can talk to: tap / click / walk up and press E, and they say their piece in the
/// dialogue box — one line per visit, cycling. No quests yet; this is the spot to hang them on later.
/// Needs a collider on (or under) the same object so the interactor and taps can find it.
/// </summary>
public class WandererNPC : MonoBehaviour, ITalkableNPC
{
    public string npcName = "Wastelander";
    [TextArea(2, 5)] public string examine = "A scavenger in a gas mask, pack stuffed with other people's junk.";
    [TextArea(2, 6)] public string[] lines =
    {
        "Roxy's the one worth listening to. I just carry things and complain about it.",
        "Water out past the creek is clean enough if you don't mind it looking back at you.",
        "Heard something clicking out in the fields. Big. Glowing. I walked the other way.",
        "You bank your stuff, right? Every crate in every town opens the same stash. Don't ask me how.",
    };

    int _next;

    public string DisplayName => npcName;
    public string ExamineText => examine;

    public void Interact()
    {
        var dlg = DialogueUI.Instance;
        if (dlg == null || lines == null || lines.Length == 0) return;
        if (dlg.IsConversationWith(npcName)) { dlg.ShowNextLine(); return; }
        dlg.StartDialogue(npcName, lines[_next++ % lines.Length]);
    }
}
