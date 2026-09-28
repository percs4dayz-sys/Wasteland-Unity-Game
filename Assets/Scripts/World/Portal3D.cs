using UnityEngine;

/// <summary>
/// 3D exit portal for the tutorial island. Walk into it:
///   • skills not ready          → tells you what's still missing
///   • ready, but no egg chosen  → Old Mara stops you with one last gift
///   • ready, egg in hand        → warps to the main world
/// Lives on a trigger collider; the glowing visual is a separate object.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Portal3D : MonoBehaviour
{
    public float retriggerCooldown = 2f;   // so standing in it doesn't spam
    float _nextAt;

    void OnTriggerEnter(Collider other)
    {
        if (Time.time < _nextAt) return;
        if (other.GetComponentInParent<PlayerEntity>() == null) return;
        _nextAt = Time.time + retriggerCooldown;
        TryEnter();
    }

    void TryEnter()
    {
        var player = PlayerEntity.Instance;
        if (player == null) return;

        if (!player.Stats.IsReadyToLeaveTutorial())
        {
            var incomplete = player.Stats.GetIncompleteSkills();
            Chat("The portal crackles and pushes you back. Reach level 2 in: " + string.Join(", ", incomplete));
            return;
        }

        // Mara's last-second gift — everyone needs a companion out there.
        if (!player.HasFlag("egg_chosen") && !player.HasFlag("pup_hatched"))
        {
            Speak("Old Mara",
                "Hold it right there, Survivor! One more thing before you step through. " +
                "Pick one of these young beasts — your choice, and you only get the one. " +
                "Everyone needs a companion out there.");
            EggChoiceUI.Show();
            return;
        }

        SceneWarp.WarpToMainWorld();
    }

    static void Speak(string who, string line)
    {
        if (DialogueUI.Instance != null) DialogueUI.Instance.StartDialogue(who, line);
        else HUDController.Instance?.AddChatLine($"<color=green>[{who}]:</color> {line}");
    }

    static void Chat(string line) => HUDController.Instance?.AddChatLine("<color=#88DDFF>[PORTAL]:</color> " + line);
}
