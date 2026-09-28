/// <summary>
/// Anything the player can hover/inspect in the world or UI.
/// DisplayName shows in the top-left hover label; ExamineText prints to the chat terminal.
/// </summary>
public interface IExaminable
{
    string DisplayName { get; }
    string ExamineText { get; }
}

/// <summary>An NPC you can walk up to and talk to (press E / click). The 3D interactor detects this
/// interface rather than a concrete type, so any quest-giver — Old Mara, Roxy, future NPCs — is
/// picked up with no extra wiring.</summary>
public interface ITalkableNPC : IExaminable
{
    void Interact();
}
