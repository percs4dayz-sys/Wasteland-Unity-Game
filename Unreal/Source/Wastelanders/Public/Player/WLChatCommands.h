#pragma once

#include "CoreMinimal.h"

class AWLPlayerController;

/**
 * Server-side "/" chat commands (Unity ChatCommands.cs). Runs on the authority with the issuing
 * controller; replies go back through ClientGameMessage.
 *
 *   /help            list commands
 *   /stuck, /unstuck return to a spawn point (5 minute cooldown: a safety net, not fast travel)
 *   /played          time played this session, total kills
 *
 * Developer commands (non-shipping builds, or accounts flagged as GM) live behind /dev:
 *   /dev item <id> [qty]   /dev xp <skill> <amount>   /dev heal
 *
 * Unity's /ai command (a DeepSeek API call made from the client with a key in the scene) is not
 * ported: shipping a third-party API key in an MMO client is a security problem. If an in-game
 * assistant is wanted it should be a server-side service.
 */
struct WASTELANDERS_API FWLChatCommands
{
	static void Execute(AWLPlayerController* PC, const FString& CommandLine);

	static constexpr double StuckCooldownSeconds = 300.0;
};
