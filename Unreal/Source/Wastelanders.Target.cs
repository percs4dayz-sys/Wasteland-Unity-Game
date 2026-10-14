using UnrealBuildTool;

public class WastelandersTarget : TargetRules
{
	public WastelandersTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Game;
		DefaultBuildSettings = BuildSettingsVersion.Latest;
		IncludeOrderVersion = EngineIncludeOrderVersion.Latest;
		ExtraModuleNames.Add("Wastelanders");
	}
}
