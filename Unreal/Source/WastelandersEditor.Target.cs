using UnrealBuildTool;

public class WastelandersEditorTarget : TargetRules
{
	public WastelandersEditorTarget(TargetInfo Target) : base(Target)
	{
		Type = TargetType.Editor;
		DefaultBuildSettings = BuildSettingsVersion.Latest;
		IncludeOrderVersion = EngineIncludeOrderVersion.Latest;
		ExtraModuleNames.AddRange(new string[] { "Wastelanders", "WastelandersEditor" });
	}
}
