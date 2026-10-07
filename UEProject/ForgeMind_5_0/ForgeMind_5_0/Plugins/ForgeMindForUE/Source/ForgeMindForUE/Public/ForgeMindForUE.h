// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#pragma once

#include "Containers/Ticker.h"
#include "ForgeMindGraphLayout.h"
#include "Modules/ModuleManager.h"
#include "Templates/SharedPointer.h"

class FForgeMindBridgeClient;
struct FGraphPanelPinConnectionFactory;

class FForgeMindForUEModule : public IModuleInterface
{
public:

	/** IModuleInterface implementation */
	virtual void StartupModule() override;
	virtual void ShutdownModule() override;

private:
	/** Deferred reorder of connection factory after all modules loaded. */
	void OnPostEngineInit();

	/** Adds the "About ForgeMind" entry to the editor main menu (Help). */
	void RegisterMenuExtension();

	/** TCP bridge client towards the SiliconLife host. */
	TUniquePtr<FForgeMindBridgeClient> BridgeClient;
	FTSTicker::FDelegateHandle TickerHandle;

	/** Wire style policy factory registered with FEdGraphUtilities. */
	TSharedPtr<FGraphPanelPinConnectionFactory> WireStyleConnectionFactory;

	/** Configurable hotkey (default Q) node arrangement hook. */
	FForgeMindGraphLayout GraphLayout;

	/** Handle for the deferred OnPostEngineInit callback. */
	FDelegateHandle EngineInitHandle;
};