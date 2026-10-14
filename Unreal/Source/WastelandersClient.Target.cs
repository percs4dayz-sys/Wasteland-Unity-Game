using UnrealBuildTool;

// Client-only build: strips server code paths (WITH_SERVER_CODE=0) from shipped player builds.
public class WastelandersClientTarget : TargetRules
{
	public WastelandersClientTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Client;
		DefaultBuildSettings = BuildSettingsVersion.Latest;
		IncludeOrderVersion = EngineIncludeOrderVersion.Latest;
		ExtraModuleNames.Add("Wastelanders");
	}
}
