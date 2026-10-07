// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#include "ForgeMindGraphCommands.h"

#define LOCTEXT_NAMESPACE "ForgeMind"

void FForgeMindGraphCommands::RegisterCommands()
{
	UI_COMMAND(ArrangeNodes,
		"Arrange Graph Nodes",
		"Auto-arranges all nodes in the focused graph panel, tracing every chain back to its root and re-laying out downstream nodes so branches never overlap. Works on any graph editor: blueprint event graphs, functions, macros, material graphs, sound cues, behavior trees, etc.",
		EUserInterfaceActionType::Button,
		FInputChord(EKeys::Q));
}

#undef LOCTEXT_NAMESPACE