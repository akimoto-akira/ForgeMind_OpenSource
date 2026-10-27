// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// "Local Position" - the shaded point's position in the primitive's local space.
// The 5.0 engine declares no such expression class (FindClassesFlexible returns 0
// matches; the class arrives in later engine versions), and the task doc's Q2
// (Tasks/UE_5.0/Material/Material_Transform.md) feeds it into TransformPosition(Local -> World).
// A 5.0 material builds it by hand as TransformPosition(World -> Local) over the
// absolute world position, which is exactly what Compile does through the same
// legacy compiler primitives the TransformPosition node uses.

#pragma once

#include "CoreMinimal.h"
#include "Materials/MaterialExpression.h"
#include "ForgeMindMaterialPositionExpressions.generated.h"

/**
 * The shaded point's position in the primitive's local space: the absolute world
 * position transformed into the local basis through the same compiler primitives
 * the TransformPosition node uses (WorldPosition(WPT_Default) ->
 * TransformPosition(MCB_World, MCB_Local)).
 */
UCLASS(MinimalAPI)
class UMaterialExpressionLocalPosition : public UMaterialExpression
{
	GENERATED_UCLASS_BODY()

#if WITH_EDITOR
	//~ Begin UMaterialExpression Interface
	virtual int32 Compile(class FMaterialCompiler* Compiler, int32 OutputIndex) override;
	virtual void GetCaption(TArray<FString>& OutCaptions) const override;
	virtual FText GetKeywords() const override { return FText::FromString(TEXT("position local")); }
	//~ End UMaterialExpression Interface
#endif
};
