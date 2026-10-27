// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Implementation of the plugin material comparison family; see the header for
// why these classes exist (the 5.0 engine declares only Less, on a compile
// path that is unavailable by default).

#include "ForgeMindMaterialCompareExpressions.h"

#include "MaterialCompiler.h"
#include "MaterialShared.h"

#define LOCTEXT_NAMESPACE "ForgeMindMaterialCompare"

UForgeMindMaterialCompareExpression::UForgeMindMaterialCompareExpression(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
{
#if WITH_EDITORONLY_DATA
	MenuCategories.Add(LOCTEXT("Category", "Math"));
	bCollapsed = false;
#endif
}

#if WITH_EDITOR

int32 UForgeMindMaterialCompareExpression::Compile(class FMaterialCompiler* Compiler, int32 OutputIndex)
{
	// Same input shape UMaterialExpressionBinaryOp documents: an unhooked input
	// falls back to its constant override.
	int32 CompiledA = A.GetTracedInput().Expression != nullptr ? A.Compile(Compiler) : Compiler->Constant(ConstA);
	if (CompiledA == INDEX_NONE)
	{
		// The upstream node already reported its own error; propagating silently
		// keeps the message list pointing at the real cause.
		return INDEX_NONE;
	}
	if (!IsFloatNumericType(Compiler->GetType(CompiledA)))
	{
		return Compiler->Errorf(TEXT("Compare input A must be of type float."));
	}
	int32 CompiledB = B.GetTracedInput().Expression != nullptr ? B.Compile(Compiler) : Compiler->Constant(ConstB);
	if (CompiledB == INDEX_NONE)
	{
		return INDEX_NONE;
	}
	if (!IsFloatNumericType(Compiler->GetType(CompiledB)))
	{
		return Compiler->Errorf(TEXT("Compare input B must be of type float."));
	}

	bool bGreaterIsOne = false, bEqualIsOne = false, bLessIsOne = false;
	GetOutcomeMask(bGreaterIsOne, bEqualIsOne, bLessIsOne);
	int32 GreaterOutcome = Compiler->Constant(bGreaterIsOne ? 1.0f : 0.0f);
	int32 EqualOutcome = Compiler->Constant(bEqualIsOne ? 1.0f : 0.0f);
	int32 LessOutcome = Compiler->Constant(bLessIsOne ? 1.0f : 0.0f);
	// Exact equality: the same zero threshold the If node compiles with.
	return Compiler->If(CompiledA, CompiledB, GreaterOutcome, EqualOutcome, LessOutcome, Compiler->Constant(0.0f));
}

uint32 UForgeMindMaterialCompareExpression::GetOutputType(int32 OutputIndex)
{
	return MCT_Float1;
}

void UForgeMindMaterialCompareExpression::GetOutcomeMask(bool& bOutGreaterIsOne, bool& bOutEqualIsOne, bool& bOutLessIsOne) const
{
	// Neutral base form: no outcome compares true, so the mask always reads 0.
	// Only the concrete family members override this.
	bOutGreaterIsOne = false;
	bOutEqualIsOne = false;
	bOutLessIsOne = false;
}

#endif // WITH_EDITOR

UMaterialExpressionGreaterEqual::UMaterialExpressionGreaterEqual(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
{
}

#if WITH_EDITOR
void UMaterialExpressionGreaterEqual::GetCaption(TArray<FString>& OutCaptions) const
{
	OutCaptions.Add(TEXT("A >= B"));
}

void UMaterialExpressionGreaterEqual::GetOutcomeMask(bool& bOutGreaterIsOne, bool& bOutEqualIsOne, bool& bOutLessIsOne) const
{
	bOutGreaterIsOne = true;
	bOutEqualIsOne = true;
	bOutLessIsOne = false;
}
#endif

UMaterialExpressionGreater::UMaterialExpressionGreater(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
{
}

#if WITH_EDITOR
void UMaterialExpressionGreater::GetCaption(TArray<FString>& OutCaptions) const
{
	OutCaptions.Add(TEXT("A > B"));
}

void UMaterialExpressionGreater::GetOutcomeMask(bool& bOutGreaterIsOne, bool& bOutEqualIsOne, bool& bOutLessIsOne) const
{
	bOutGreaterIsOne = true;
	bOutEqualIsOne = false;
	bOutLessIsOne = false;
}
#endif

UMaterialExpressionLessEqual::UMaterialExpressionLessEqual(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
{
}

#if WITH_EDITOR
void UMaterialExpressionLessEqual::GetCaption(TArray<FString>& OutCaptions) const
{
	OutCaptions.Add(TEXT("A <= B"));
}

void UMaterialExpressionLessEqual::GetOutcomeMask(bool& bOutGreaterIsOne, bool& bOutEqualIsOne, bool& bOutLessIsOne) const
{
	bOutGreaterIsOne = false;
	bOutEqualIsOne = true;
	bOutLessIsOne = true;
}
#endif

UMaterialExpressionEqual::UMaterialExpressionEqual(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
{
}

#if WITH_EDITOR
void UMaterialExpressionEqual::GetCaption(TArray<FString>& OutCaptions) const
{
	OutCaptions.Add(TEXT("A == B"));
}

void UMaterialExpressionEqual::GetOutcomeMask(bool& bOutGreaterIsOne, bool& bOutEqualIsOne, bool& bOutLessIsOne) const
{
	bOutGreaterIsOne = false;
	bOutEqualIsOne = true;
	bOutLessIsOne = false;
}
#endif

UMaterialExpressionNotEqual::UMaterialExpressionNotEqual(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
{
}

#if WITH_EDITOR
void UMaterialExpressionNotEqual::GetCaption(TArray<FString>& OutCaptions) const
{
	OutCaptions.Add(TEXT("A != B"));
}

void UMaterialExpressionNotEqual::GetOutcomeMask(bool& bOutGreaterIsOne, bool& bOutEqualIsOne, bool& bOutLessIsOne) const
{
	bOutGreaterIsOne = true;
	bOutEqualIsOne = false;
	bOutLessIsOne = true;
}
#endif

UMaterialExpressionLessMask::UMaterialExpressionLessMask(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
{
}

#if WITH_EDITOR
void UMaterialExpressionLessMask::GetCaption(TArray<FString>& OutCaptions) const
{
	OutCaptions.Add(TEXT("A < B"));
}

void UMaterialExpressionLessMask::GetOutcomeMask(bool& bOutGreaterIsOne, bool& bOutEqualIsOne, bool& bOutLessIsOne) const
{
	bOutGreaterIsOne = false;
	bOutEqualIsOne = false;
	bOutLessIsOne = true;
}
#endif

#undef LOCTEXT_NAMESPACE
