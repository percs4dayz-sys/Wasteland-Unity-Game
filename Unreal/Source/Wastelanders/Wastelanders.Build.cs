using UnrealBuildTool;

public class Wastelanders : ModuleRules
{
	public Wastelanders(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

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
