/// <summary>
/// Single source of truth for "is the player currently typing in a chat box". Both the scene-wired
/// 2D HUD chat (HUDController) and the auto-built 3D world chat (Hud3DChat) feed into this, so every
/// system that must ignore keystrokes meant for the chat line (movement, hotkeys, clicks, interact)
/// can check one place instead of only knowing about the 2D HUD.
/// </summary>
public static class ChatInput
{
    /// <summary>True while either chat's input field has focus.</summary>
    public static bool IsTyping =>
        (HUDController.Instance != null && HUDController.Instance.IsChatFocused()) ||
        (Hud3DChat.Instance != null && Hud3DChat.Instance.IsInputFocused());
}
