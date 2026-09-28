using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Attach to the chat terminal panel (BottomTerminal).
/// Left-clicking anywhere on the chat box advances the current NPC
/// conversation — like clicking "Continue" in a classic RPG.
///
/// The panel's own Image (with raycastTarget = true) absorbs the click,
/// so it also stops the click from passing through to the game world behind it.
/// Clicking the chat input field still focuses it (the input handles its own clicks).
/// </summary>
public class DialogueClickAdvance : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        // Only respond to left clicks; ignore right/middle.
        if (eventData.button != PointerEventData.InputButton.Left) return;

        // Advances the active conversation. Harmless no-op if none is running.
        DialogueUI.Instance?.ShowNextLine();
    }
}
