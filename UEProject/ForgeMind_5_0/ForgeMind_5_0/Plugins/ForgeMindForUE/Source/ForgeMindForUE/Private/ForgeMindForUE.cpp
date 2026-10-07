// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#include "ForgeMindForUE.h"

#include "EdGraphUtilities.h"
#include "Misc/CoreDelegates.h"
#include "ToolMenuSection.h"
#include "ToolMenus.h"
#include "ForgeMindAboutWindow.h"
#include "ForgeMindBridgeClient.h"
#include "ForgeMindLogCapture.h"
#include "ForgeMindScreenCapture.h"
#include "ForgeMindWireStyleRendering.h"

// ============================================================
// Friend injection: access FEdGraphUtilities::VisualPinConnectionFactories
//
// FEdGraphUtilities declares "friend class FNodeFactory" but the engine's
// FNodeFactory lives in the GraphEditor module and is not publicly
// accessible.  C++ friend declarations are resolved by name - any class
// named FNodeFactory in any namespace can access the private members.
// We declare our own FNodeFactory here solely to reorder our connection
// factory ahead of the MaterialEditor module's factory in the static
// array, ensuring our policy wins for material-schema graphs.
// ============================================================
class FNodeFactory
{
public:
	static TArray<TSharedPtr<FGraphPanelPinConnectionFactory>>& GetConnectionFactories()
	{
		return FEdGraphUtilities::VisualPinConnectionFactories;
	}
};

#define LOCTEXT_NAMESPACE "FForgeMindForUEModule"

void FForgeMindForUEModule::StartupModule()
{
	// Wire rendering style (Editor Preferences driven).
	WireStyleConnectionFactory = MakeShared<FForgeMindPinConnectionFactory>();
	FEdGraphUtilities::RegisterVisualPinConnectionFactory(WireStyleConnectionFactory);

	// Defer the reorder to OnPostEngineInit so that MaterialEditor (which
	// loads in the Default phase, potentially before us) has already
	// registered its factory.  At PostEngineInit every module has loaded
	// and registered, so we can safely move ours to the front.
	EngineInitHandle = FCoreDelegates::OnPostEngineInit.AddRaw(
		this, &FForgeMindForUEModule::OnPostEngineInit);

	// Configurable hotkey (default Q) node arrangement in any graph panel
	GraphLayout.Register();

	// Editor-only bridge; never connect when running a packaged game
	if (IsRunningGame())
	{
		return;
	}

	// Buffer every UE_LOG message for get_debug_output (from here on only)
	GetForgeMindLogCapture().Start();

	// Snapshot on-screen debug messages every frame for get_screen_output
	GetForgeMindScreenCapture().Start();

	BridgeClient = MakeUnique<FForgeMindBridgeClient>();
	BridgeClient->Start();

	// The client needs game-thread ticks for connect retries and message dispatch
	TickerHandle = FTSTicker::GetCoreTicker().AddTicker(
		FTickerDelegate::CreateLambda([this](float DeltaTime)
		{
			if (BridgeClient.IsValid())
			{
				BridgeClient->Tick(DeltaTime);
			}
			return true;
		}));
}

void FForgeMindForUEModule::OnPostEngineInit()
{
	// Move our factory to index 0 so it is consulted before the
	// MaterialEditor module's factory for material-schema graphs.
	auto& Factories = FNodeFactory::GetConnectionFactories();
	const int32 Index = Factories.IndexOfByKey(WireStyleConnectionFactory);
	if (Index > 0)
	{
		Factories.RemoveAt(Index);
		Factories.Insert(WireStyleConnectionFactory, 0);
		UE_LOG(LogTemp, Warning, TEXT("FForgeMind: reordered connection factory to front (was index %d)"), Index);
	}
	else if (Index == 0)
	{
		UE_LOG(LogTemp, Warning, TEXT("FForgeMind: connection factory already at front"));
	}
	else
	{
		UE_LOG(LogTemp, Warning, TEXT("FForgeMind: connection factory not found in array!"));
	}

	// Main window menu entry (Help ▸ About ForgeMind) - registered here so the
	// LevelEditor main menu exists by the time we extend it.
	RegisterMenuExtension();

	FCoreDelegates::OnPostEngineInit.Remove(EngineInitHandle);
	EngineInitHandle.Reset();
	}

	void FForgeMindForUEModule::RegisterMenuExtension()
	{
		// FToolMenuOwnerScoped tags the extension with this module so it can be
		// removed again on shutdown (ExtendMenu itself takes only the menu name).
		FToolMenuOwnerScoped OwnerScoped(this);

		UToolMenu* Menu = UToolMenus::Get()->ExtendMenu(TEXT("LevelEditor.MainMenu.Help"));
		FToolMenuSection& Section = Menu->FindOrAddSection(TEXT("Help"));
		Section.AddMenuEntry(
			TEXT("ForgeMindAbout"),
			LOCTEXT("ForgeMindAbout_Label", "About ForgeMind"),
			LOCTEXT("ForgeMindAbout_Tooltip", "Shows version and build information for the ForgeMind companion plugin"),
			FSlateIcon(),
			FUIAction(FExecuteAction::CreateStatic(&ShowForgeMindAboutWindow)));
	}

void FForgeMindForUEModule::ShutdownModule()
{
	UToolMenus::UnregisterOwner(this);

	FTSTicker::GetCoreTicker().RemoveTicker(TickerHandle);
	if (BridgeClient.IsValid())
	{
		BridgeClient->Stop();
		BridgeClient.Reset();
	}
	GetForgeMindScreenCapture().Stop();
	GetForgeMindLogCapture().Stop();

	GraphLayout.Unregister();

	if (EngineInitHandle.IsValid())
	{
		FCoreDelegates::OnPostEngineInit.Remove(EngineInitHandle);
		EngineInitHandle.Reset();
	}

	if (WireStyleConnectionFactory.IsValid())
	{
		FEdGraphUtilities::UnregisterVisualPinConnectionFactory(WireStyleConnectionFactory);
		WireStyleConnectionFactory.Reset();
	}
}

#undef LOCTEXT_NAMESPACE
	
IMPLEMENT_MODULE(FForgeMindForUEModule, ForgeMindForUE)