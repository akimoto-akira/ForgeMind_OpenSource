// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Material comparison expressions the 5.0 engine does not declare. The engine
// ships only UMaterialExpressionLess (MaterialExpressionBinaryOp.h) and its
// compile path is the new HLSL generator only: UMaterialExpressionBinaryOp::
// Compile returns "Not supported with legacy compiler", and the new generator
// is off by default in 5.0 (r.MaterialEnableNewHLSLGenerator is 0 and
// ECVF_ReadOnly), so even the native Less never compiles in a default material.
//
// The task docs describe the full 0/1-mask comparison family (Tasks/UE_5.0/Material/
// Material_Compare.md names Less / Greater / LessEqual / GreaterEqual / Equal /
// NotEqual), so the plugin completes the five missing ones as real expression
// classes that compile on the default (legacy) translator through the If
// primitive: each comparison is If(A, B, greaterOutcome, equalOutcome,
// lessOutcome) with constant 1/0 outcomes per its truth table. Inputs follow
// the engine's BinaryOp shape (A / B with ConstA / ConstB overrides for
// unhooked inputs), so a comparison added by name accepts the same wiring the
// task doc step lists describe.

#pragma once

#include "CoreMinimal.h"
#include "Materials/MaterialExpression.h"
#include "ForgeMindMaterialCompareExpressions.generated.h"

/**
 * Shared shape and compile path of the plugin comparison family. A and B
 * compile to float, the three If outcomes resolve to the subclass truth
 * table's 1/0 constants, and the result is a float1 0/1 mask.
 *
 * The class is reflection-abstract only (the engine's lowercase "abstract"
 * specifier, exactly as UMaterialExpressionBinaryOp declares it): no CDO, no
 * creation through add_material_expression. It deliberately carries no
 * compile-time pure virtual - the generated class machinery instantiates
 * concrete classes, and the default outcome mask below makes that sound.
 */
UCLASS(abstract, MinimalAPI)
class UForgeMindMaterialCompareExpression : public UMaterialExpression
{
	GENERATED_UCLASS_BODY()

public:

	UPROPERTY(meta = (RequiredInput = "false", ToolTip = "Defaults to 'ConstA' if not specified"))
	FExpressionInput A;

	UPROPERTY(meta = (RequiredInput = "false", ToolTip = "Defaults to 'ConstB' if not specified"))
	FExpressionInput B;

	/** only used if A is not hooked up */
	UPROPERTY(EditAnywhere, Category = MaterialExpressionCompare, meta = (OverridingInputProperty = "A"))
	float ConstA;

	/** only used if B is not hooked up */
	UPROPERTY(EditAnywhere, Category = MaterialExpressionCompare, meta = (OverridingInputProperty = "B"))
	float ConstB;

#if WITH_EDITOR
	//~ Begin UMaterialExpression Interface
	virtual int32 Compile(class FMaterialCompiler* Compiler, int32 OutputIndex) override;
	virtual uint32 GetOutputType(int32 OutputIndex) override;
	//~ End UMaterialExpression Interface

	/**
	 * The comparison's truth table over the If outcomes (A>B, A==B, A<B): which
	 * outcome yields 1. The base form is the neutral "no outcome is true"
	 * comparison; each concrete family member overrides it.
	 */
	virtual void GetOutcomeMask(bool& bOutGreaterIsOne, bool& bOutEqualIsOne, bool& bOutLessIsOne) const;
#endif
};

/** A >= B - emits 1 when A is greater than or equal to B. */
UCLASS(MinimalAPI)
class UMaterialExpressionGreaterEqual : public UForgeMindMaterialCompareExpression
{
	GENERATED_UCLASS_BODY()

#if WITH_EDITOR
	virtual void GetCaption(TArray<FString>& OutCaptions) const override;
	virtual FText GetKeywords() const override { return FText::FromString(TEXT("compare greater or equal")); }
protected:
	virtual void GetOutcomeMask(bool& bOutGreaterIsOne, bool& bOutEqualIsOne, bool& bOutLessIsOne) const override;
#endif
};

/** A > B - emits 1 when A is strictly greater than B. */
UCLASS(MinimalAPI)
class UMaterialExpressionGreater : public UForgeMindMaterialCompareExpression
{
	GENERATED_UCLASS_BODY()

#if WITH_EDITOR
	virtual void GetCaption(TArray<FString>& OutCaptions) const override;
	virtual FText GetKeywords() const override { return FText::FromString(TEXT("compare greater than")); }
protected:
	virtual void GetOutcomeMask(bool& bOutGreaterIsOne, bool& bOutEqualIsOne, bool& bOutLessIsOne) const override;
#endif
};

/** A <= B - emits 1 when A is less than or equal to B. */
UCLASS(MinimalAPI)
class UMaterialExpressionLessEqual : public UForgeMindMaterialCompareExpression
{
	GENERATED_UCLASS_BODY()

#if WITH_EDITOR
	virtual void GetCaption(TArray<FString>& OutCaptions) const override;
	virtual FText GetKeywords() const override { return FText::FromString(TEXT("compare less or equal")); }
protected:
	virtual void GetOutcomeMask(bool& bOutGreaterIsOne, bool& bOutEqualIsOne, bool& bOutLessIsOne) const override;
#endif
};

/** A == B - emits 1 when A equals B. */
UCLASS(MinimalAPI)
class UMaterialExpressionEqual : public UForgeMindMaterialCompareExpression
{
	GENERATED_UCLASS_BODY()

#if WITH_EDITOR
	virtual void GetCaption(TArray<FString>& OutCaptions) const override;
	virtual FText GetKeywords() const override { return FText::FromString(TEXT("compare equal")); }
protected:
	virtual void GetOutcomeMask(bool& bOutGreaterIsOne, bool& bOutEqualIsOne, bool& bOutLessIsOne) const override;
#endif
};

/** A != B - emits 1 when A does not equal B. */
UCLASS(MinimalAPI)
class UMaterialExpressionNotEqual : public UForgeMindMaterialCompareExpression
{
	GENERATED_UCLASS_BODY()

#if WITH_EDITOR
	virtual void GetCaption(TArray<FString>& OutCaptions) const override;
	virtual FText GetKeywords() const override { return FText::FromString(TEXT("compare not equal")); }
protected:
	virtual void GetOutcomeMask(bool& bOutGreaterIsOne, bool& bOutEqualIsOne, bool& bOutLessIsOne) const override;
#endif
};

/**
 * A < B - the plugin-side replacement for the engine's UMaterialExpressionLess.
 * The engine class (MaterialExpressionBinaryOp.h) compiles only through the new
 * HLSL generator (r.MaterialEnableNewHLSLGenerator, off by default in 5.0), so
 * the name "MaterialExpressionLess" never compiles on the legacy translator.
 * This class carries the same truth table on the shared If-based compile path;
 * the bridge routes op "Less" here (see ForgeMindMaterial::ExpressionClassName).
 */
UCLASS(MinimalAPI)
class UMaterialExpressionLessMask : public UForgeMindMaterialCompareExpression
{
	GENERATED_UCLASS_BODY()

#if WITH_EDITOR
	virtual void GetCaption(TArray<FString>& OutCaptions) const override;
	virtual FText GetKeywords() const override { return FText::FromString(TEXT("compare less than")); }
protected:
	virtual void GetOutcomeMask(bool& bOutGreaterIsOne, bool& bOutEqualIsOne, bool& bOutLessIsOne) const override;
#endif
};
