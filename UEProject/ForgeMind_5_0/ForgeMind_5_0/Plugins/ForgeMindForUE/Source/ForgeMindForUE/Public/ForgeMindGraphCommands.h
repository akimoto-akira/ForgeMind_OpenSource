// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#pragma once

#include "CoreMinimal.h"
#include "Framework/Commands/Commands.h"
#include "InputCoreTypes.h"

class FUICommandInfo;

/**
 * Commands for the ForgeMind graph layout feature.
 *
 * The single command 'ArrangeNodes' is bound to a configurable hotkey
 * (default Q) that triggers auto-arrangement of the focused graph panel.
 * Users can remap it via Edit > Editor Preferences > Keyboard Shortcuts
 * under the "ForgeMind" context.
 */
class FForgeMindGraphCommands : public TCommands<FForgeMindGraphCommands>
{
public:
	FForgeMindGraphCommands()
		: TCommands<FForgeMindGraphCommands>(
			TEXT("ForgeMind"),
			NSLOCTEXT("ForgeMind", "ForgeMindContext", "ForgeMind"),
			NAME_None,
			FAppStyle::GetAppStyleSetName())
	{
	}

	virtual ~FForgeMindGraphCommands() = default;

	/** Registers the command definitions (call once at module startup). */
	FORGEMINDFORUE_API virtual void RegisterCommands() override;

	/** Auto-arrange all nodes in the focused graph panel. */
	TSharedPtr<FUICommandInfo> ArrangeNodes;
};