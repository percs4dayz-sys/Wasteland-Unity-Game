#include "Misc/AutomationTest.h"

#if WITH_DEV_AUTOMATION_TESTS

#include "Frontend/WLFrontendSubsystem.h"

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLFrontendNameTest, "Wastelanders.UI.Frontend.Name",
	EAutomationTestFlags::ApplicationContextMask | EAutomationTestFlags::ProductFilter)
bool FWLFrontendNameTest::RunTest(const FString&)
{
	TestEqual(TEXT("spaces become underscores"), UWLFrontendSubsystem::SanitizeName(TEXT("  Rusty Pete ")), FString(TEXT("Rusty_Pete")));
	TestEqual(TEXT("junk stripped"), UWLFrontendSubsystem::SanitizeName(TEXT("a?b&c=d")), FString(TEXT("abcd")));
	TestEqual(TEXT("max 20"), UWLFrontendSubsystem::SanitizeName(FString::ChrN(30, TEXT('x'))).Len(), 20);
	TestFalse(TEXT("too short"), UWLFrontendSubsystem::IsValidName(TEXT("a")));
	TestFalse(TEXT("only junk"), UWLFrontendSubsystem::IsValidName(TEXT("?!")));
	TestTrue(TEXT("valid"), UWLFrontendSubsystem::IsValidName(TEXT("Ro")));
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FWLFrontendLookTest, "Wastelanders.UI.Frontend.Look",
	EAutomationTestFlags::ApplicationContextMask | EAutomationTestFlags::ProductFilter)
bool FWLFrontendLookTest::RunTest(const FString&)
{
	TMap<FName, FString> Look;
	Look.Add(TEXT("Hair"), TEXT("3"));
	Look.Add(TEXT("SkinColor"), TEXT("5"));
	Look.Add(TEXT("Evil"), TEXT("a|b=c"));
	const FString Enc = UWLFrontendSubsystem::EncodeLook(Look);
	const TMap<FName, FString> Dec = UWLFrontendSubsystem::DecodeLook(Enc);
	TestEqual(TEXT("round trip count"), Dec.Num(), 3);
	TestEqual(TEXT("hair"), Dec.FindRef(TEXT("Hair")), FString(TEXT("3")));
	TestEqual(TEXT("separators stripped"), Dec.FindRef(TEXT("Evil")), FString(TEXT("abc")));
	TestEqual(TEXT("empty decodes empty"), UWLFrontendSubsystem::DecodeLook(TEXT("")).Num(), 0);

	// Stepping wraps for parts and clamps for sliders.
	UWLFrontendSubsystem* FE = NewObject<UWLFrontendSubsystem>(GetTransientPackage());
	FE->OptionsPerPartRow = 4;
	FE->StepRow(TEXT("Hair"), -1);
	TestEqual(TEXT("wrap down"), FE->GetRowValue(TEXT("Hair")), FString(TEXT("3")));
	FE->StepRow(TEXT("Hair"), 1);
	TestEqual(TEXT("wrap up"), FE->GetRowValue(TEXT("Hair")), FString(TEXT("0")));
	for (int32 i = 0; i < 20; ++i) { FE->StepRow(TEXT("BodySize"), 1); }
	TestEqual(TEXT("slider clamp"), FE->GetRowValue(TEXT("BodySize")), FString(TEXT("100")));
	FE->SetGender(TEXT("Female"));
	TestEqual(TEXT("female body type"), FE->GetRowValue(TEXT("BodyType")), FString(TEXT("100")));
	TestFalse(TEXT("cannot join without name"), FE->CanJoin());
	FE->CharacterName = TEXT("Rusty");
	TestTrue(TEXT("can join"), FE->CanJoin());
	TestTrue(TEXT("splash consumed once"), !FE->ConsumePendingSplash());
	FE->QueueSplash();
	TestTrue(TEXT("splash queued"), FE->ConsumePendingSplash());
	TestFalse(TEXT("splash gone"), FE->ConsumePendingSplash());
	return true;
}

#endif
