using UnityEngine;

/// <summary>
/// Parses player-typed chat commands (lines starting with '/'). Centralized so the 2D HUD chat and
/// the 3D world chat route through the same logic and behave identically. Returns true when the text
/// was handled as a command (so the caller skips echoing it as normal chat).
/// </summary>
public static class ChatCommands
{
    // /stuck is a safety net for getting wedged or falling out of the world — not fast travel.
    // A cooldown keeps it from being abused to skip getting around the map.
    const float StuckCooldownSeconds = 300f;   // 5 minutes
    static float _lastStuckTime = -9999f;       // far in the past so the first use is always allowed

    public static bool TryHandle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string text = raw.Trim();
        if (!text.StartsWith("/")) return false;

        string cmd = text.Split(' ')[0].ToLowerInvariant();
        switch (cmd)
        {
            case "/stuck":
            case "/unstuck":
                DoStuck();
                return true;
            case "/village":
                DoVillage(text.Split(' '));
                return true;
            case "/help":
                HUDController.Emit("<color=#80C0FF>Commands:</color> /stuck — return to your spawn point if you get stuck or fall out of the world.\n/ai <prompt> — ask the in-game assistant a question.");
                return true;
            case "/ai":
            case "/ask":
            case "/assistant":
                string prompt = text.Length > cmd.Length ? text.Substring(cmd.Length).Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    HUDController.Emit("<color=#80C0FF>[AI]</color> Ask me something by typing /ai followed by your prompt.");
                    return true;
                }

                HUDController.Emit("<color=#80C0FF>[AI]</color> Thinking...");

                var assistant = Object.FindAnyObjectByType<DeepSeekIntegration>();
                if (assistant == null)
                {
                    assistant = new GameObject("DeepSeekIntegration").AddComponent<DeepSeekIntegration>();
                    HUDController.Emit("<color=#FFD966>[AI]</color> I created a temporary integration object. Set your API key in the Inspector before using it.");
                }

                assistant.AskDeepSeek(prompt, response =>
                {
                    string reply = string.IsNullOrWhiteSpace(response)
                        ? "<color=#FF8080>[AI]</color> I did not receive a reply. Check the API key and connection."
                        : $"<color=#80C0FF>[AI]</color> {response}";
                    HUDController.Emit(reply);
                });
                return true;
            default:
                HUDController.Emit($"<color=#FF8080>Unknown command:</color> {cmd}. Type /help.");
                return true;
        }
    }

    /// <summary>/village here [radius] — make the spot you're standing on the village centre (rebuilds it).
    /// /village clear — go back to the scene's TownSite_SW_Harbor marker. /village — show where it is.</summary>
    static void DoVillage(string[] parts)
    {
        string sub = parts.Length > 1 ? parts[1].ToLowerInvariant() : "";
        var player = PlayerEntity.Instance;
        switch (sub)
        {
            case "here":
                if (player == null) { HUDController.Emit("<color=#FF8080>/village here needs you in the world.</color>"); return; }
                float radius = parts.Length > 2 && float.TryParse(parts[2], out float r) ? Mathf.Clamp(r, 10f, 120f) : WorldAnchors.DefaultVillageRadius;
                WorldAnchors.SetVillage(player.transform.position, radius);
                HUDController.Emit($"<color=#80C0FF>Village centre set here (radius {radius:0} m).</color> Rebuilding in a moment.");
                return;
            case "clear":
                WorldAnchors.ClearVillage();
                HUDController.Emit("<color=#80C0FF>Village anchor reset to the scene marker.</color>");
                return;
            default:
                if (WorldAnchors.TryGet(WorldAnchors.VillageKey, out Vector3 pos, out float rad))
                    HUDController.Emit($"<color=#80C0FF>Village centre:</color> ({pos.x:0}, {pos.y:0}, {pos.z:0}), radius {rad:0} m. Use /village here to move it.");
                else
                    HUDController.Emit("<color=#FFC040>No village found in this scene.</color> Stand in it and type /village here.");
                return;
        }
    }

    static void DoStuck()
    {
        var player = PlayerEntity.Instance;
        var respawn = player != null ? player.GetComponent<PlayerRespawn>() : null;
        if (respawn == null)
        {
            HUDController.Emit("<color=#FF8080>/stuck isn't available here.</color>");
            return;
        }

        float elapsed = Time.unscaledTime - _lastStuckTime;
        if (elapsed < StuckCooldownSeconds)
        {
            int wait = Mathf.CeilToInt(StuckCooldownSeconds - elapsed);
            string pretty = wait >= 60 ? $"{wait / 60}m {wait % 60}s" : $"{wait}s";
            HUDController.Emit($"<color=#FFC040>/stuck is on cooldown</color> — try again in {pretty}.");
            return;
        }

        _lastStuckTime = Time.unscaledTime;
        respawn.ReturnToSpawn();
        HUDController.Emit("<color=#80FF80>Returned to your spawn point.</color>");
    }
}
