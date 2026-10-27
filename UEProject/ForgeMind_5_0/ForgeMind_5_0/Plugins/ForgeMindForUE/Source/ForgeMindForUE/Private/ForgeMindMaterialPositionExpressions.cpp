// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Implementation of the plugin local-position expression; see the header for why
// this class exists (the 5.0 engine declares no LocalPosition expression).

#include "ForgeMindMaterialPositionExpressions.h"

#include "MaterialCompiler.h"
#include "MaterialShared.h"
#include "Materials/MaterialExpressionWorldPosition.h"

#define LOCTEXT_NAMESPACE "ForgeMindMaterialPosition"

UMaterialExpressionLocalPosition::UMaterialExpressionLocalPosition(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
{
#if WITH_EDITORONLY_DATA
	MenuCategories.Add(LOCTEXT("Category", "Position"));
	bCollapsed = false;
#endif
}

#if WITH_EDITOR

int32 UMaterialExpressionLocalPosition::Compile(class FMaterialCompiler* Compiler, int32 OutputIndex)
{
	// The composition a 5.0 material editor user builds by hand - TransformPosition
	// (World -> Local) over the absolute world position - through the same compiler
	// primitives the TransformPosition node uses for that graph.
	int32 WorldPosition = Compiler->WorldPosition(WPT_Default);
	if (WorldPosition == INDEX_NONE)
	{
		return INDEX_NONE;
	}
	return Compiler->TransformPosition(MCB_World, MCB_Local, WorldPosition);
}

void UMaterialExpressionLocalPosition::GetCaption(TArray<FString>& OutCaptions) const
{
	OutCaptions.Add(TEXT("Local Position"));
}

#endif // WITH_EDITOR

#undef LOCTEXT_NAMESPACE
