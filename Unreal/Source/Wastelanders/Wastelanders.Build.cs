using UnrealBuildTool;

public class Wastelanders : ModuleRules
{
	public Wastelanders(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		// Parameters named Pawn/Instigator/Owner shadow engine members. Harmless here, so report them
		// as warnings instead of failing the build.
		ShadowVariableWarningLevel = WarningLevel.Warning;

		PublicDependencyModuleNames.AddRange(new string[]
		{
			"Core",
			"CoreUObject",
			"Engine",
			"InputCore",
			"EnhancedInput",
			"GameplayAbilities",
			"GameplayTags",
			"GameplayTasks",
			"NetCore",
			"AIModule",
			"NavigationSystem",
			"UMG",
			"CommonUI",
			"ModelViewViewModel",
			"FieldNotification",
			"StateTreeModule",
			"GameplayStateTreeModule",
			"Json",
			"JsonUtilities",
			"DeveloperSettings",
		});

		PrivateDependencyModuleNames.AddRange(new string[]
		{
			"Slate",
			"SlateCore",
		});
	}
}
