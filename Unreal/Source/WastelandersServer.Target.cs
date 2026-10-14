using UnrealBuildTool;

// Dedicated server: the MMO's authoritative simulation. Build with a source-built engine
// (dedicated server targets are not supported by the launcher build).
public class WastelandersServerTarget : TargetRules
{
	public WastelandersServerTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Server;
		DefaultBuildSettings = BuildSettingsVersion.Latest;
		IncludeOrderVersion = EngineIncludeOrderVersion.Latest;
		ExtraModuleNames.Add("Wastelanders");
		bUseLoggingInShipping = true;
	}
}
