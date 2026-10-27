// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Material step commands: create_material / add_material_expression /
// connect_material_pins / compile_material.
//
// The requirement docs describe a material build as a sequence of single MCP
// commands (the "step by step" A-side workspace): create the asset, add one expression per
// step, wire a pin per step, compile, save. Until now the Companion had no
// material step command at all - the ~50 command registry only offered the batch
// Asset IR channel (apply_asset_ir / get_asset_ir) for materials, so
// "Material If" answered "Unknown command 'create_material'" (run 20261024_1755,
// No. 74). These four commands close that gap.
//
// They are deliberately built on the same operations the Asset IR material
// handler uses (ForgeMindMaterialOps): an expression created step-by-step and
// one created from an IR definition must be the same object graph, because a
// task doc verifies its A side (step commands) against its B side (DSL -> IR) by
// comparing the IR read back from the editor.

#include "ForgeMindBridgeClient.h"
#include "ForgeMindMaterialOps.h"
#include "ForgeMindBlueprintUtils.h"
#include "ForgeMindIRHandler.h"

#include "AssetRegistry/AssetRegistryModule.h"
#include "Dom/JsonObject.h"
#include "Dom/JsonValue.h"
#include "Factories/MaterialFactoryNew.h"
#include "Materials/Material.h"
#include "Materials/MaterialExpression.h"
#include "Misc/PackageName.h"
#include "RHI.h"
#include "UObject/Package.h"
#include "UObject/UnrealType.h"

using namespace ForgeMindBlueprintUtils;

namespace
{
	/** Read a string field, accepting the assetPath alias the other asset commands use. */
	FString ReadString(const TSharedPtr<FJsonObject>& Payload, const TCHAR* Key, const TCHAR* Alias = nullptr)
	{
		FString Value;
		if (Payload.IsValid() && Payload->TryGetStringField(Key, Value) && !Value.IsEmpty())
		{
			return Value;
		}
		if (Alias != nullptr && Payload.IsValid() && Payload->TryGetStringField(Alias, Value))
		{
			return Value;
		}
		return Value;
	}

	/** Read a property back as a display string, falling back to the requested name. */
	FString ReadBackName(UObject* Object, const TCHAR* PropertyName, const FString& Fallback)
	{
		const TSharedPtr<FJsonValue> Value = ForgeMindMaterial::GetValue(Object, PropertyName);
		if (Value.IsValid() && Value->Type == EJson::String)
		{
			return Value->AsString();
		}
		return Fallback;
	}

	TSharedPtr<FJsonObject> MaterialStepFailure(const TCHAR* Flag, const FString& Reason)
	{
		TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
		Result->SetBoolField(Flag, false);
		Result->SetStringField(TEXT("reason"), Reason);
		return Result;
	}

	/** Locate the material the step commands operate on. */
	UMaterial* ResolveStepMaterial(const TSharedPtr<FJsonObject>& Payload, FString& OutReason)
	{
		const FString Path = ReadString(Payload, TEXT("path"), TEXT("assetPath"));
		if (Path.IsEmpty())
		{
			OutReason = TEXT("Required field: path (project asset path, e.g. /Game/Tests/UE5_0/M_Material)");
			return nullptr;
		}
		UObject* Asset = ForgeMindMaterial::ResolveMaterial(Path);
		UMaterial* Material = Cast<UMaterial>(Asset);
		if (Material == nullptr)
		{
			OutReason = FString::Printf(TEXT("no material at '%s' (create it with create_material first)"), *Path);
			return nullptr;
		}
		return Material;
	}
}

// ---------------------------------------------------------------------------
// create_material
//
// Payload:
//   path      - project asset path, e.g. /Game/Tests/UE5_0/Mat_If_Single_A
//   domain    - optional EMaterialDomain display name: Surface / DeferredDecal /
//               LightFunction / PostProcess / UI. Default Surface.
//   blendMode - optional EBlendMode display name: Opaque / Masked / Modulate /
//               Additive / Translucent / AlphaModulate / Precomputed. Default Opaque.
//
// The material is registered with the asset registry and left dirty; the caller
// persists it with save_asset, exactly like the other create_* commands.
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleCreateMaterial(const TSharedPtr<FJsonObject>& Payload)
{
	const FString AssetPath = ReadString(Payload, TEXT("path"), TEXT("assetPath"));
	if (AssetPath.IsEmpty())
	{
		return MaterialStepFailure(TEXT("created"), TEXT("Required field: path (project asset path, e.g. /Game/Tests/UE5_0/M_Material)"));
	}

	FString PackagePath;
	FString AssetName;
	FString Reason;
	if (!PrepareNewAssetLocation(AssetPath, TEXT("/Game/Tests/UE5_0/M_Material"), PackagePath, AssetName, Reason))
	{
		return MaterialStepFailure(TEXT("created"), Reason);
	}

	UMaterialFactoryNew* Factory = NewObject<UMaterialFactoryNew>();
	// CreateAssetWithFactory splits the path itself (AssetNameOf / PackagePathOf), so it
	// gets the package path: handing it the "Package.Name" form would make it build an
	// object literally named "Name.Name", which no later lookup by asset path finds.
	UObject* Asset = ForgeMindIR::CreateAssetWithFactory(Factory, UMaterial::StaticClass(), PackagePath);
	UMaterial* Material = Cast<UMaterial>(Asset);
	if (Material == nullptr)
	{
		return MaterialStepFailure(TEXT("created"), FString::Printf(TEXT("material creation failed at '%s'"), *PackagePath));
	}

	const FString FullPath = FString::Printf(TEXT("%s.%s"), *PackagePath, *AssetName);

	FString DomainName = TEXT("Surface");
	FString BlendModeName = TEXT("Opaque");
	if (Payload.IsValid())
	{
		Payload->TryGetStringField(TEXT("domain"), DomainName);
		Payload->TryGetStringField(TEXT("blendMode"), BlendModeName);
	}

	TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
	FString Error;
	const bool bDomainSet = ForgeMindMaterial::SetValue(Material, TEXT("MaterialDomain"), MakeShared<FJsonValueString>(DomainName), Error);
	if (!bDomainSet)
	{
		Result->SetStringField(TEXT("domainError"), Error);
	}
	const bool bBlendSet = ForgeMindMaterial::SetValue(Material, TEXT("BlendMode"), MakeShared<FJsonValueString>(BlendModeName), Error);
	if (!bBlendSet)
	{
		Result->SetStringField(TEXT("blendModeError"), Error);
	}

	FAssetRegistryModule::AssetCreated(Material);
	Material->MarkPackageDirty();

	Result->SetBoolField(TEXT("created"), true);
	Result->SetBoolField(TEXT("dirty"), Material->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("path"), FullPath);
	// Report what the asset actually holds, not what was asked for: an enum name
	// that did not resolve stays visible here instead of being echoed back.
	Result->SetStringField(TEXT("domain"), ReadBackName(Material, TEXT("MaterialDomain"), DomainName));
	Result->SetStringField(TEXT("blendMode"), ReadBackName(Material, TEXT("BlendMode"), BlendModeName));
	Result->SetBoolField(TEXT("settingsApplied"), bDomainSet && bBlendSet);
	return Result;
}

// ---------------------------------------------------------------------------
// add_material_expression
//
// Payload:
//   path            - material asset path (created beforehand by create_material)
//   expressionClass - expression class name, with or without the MaterialExpression
//                     prefix: "MaterialExpressionIf" / "If" / "TextureSample"
//   name            - optional parameter name; when present the expression is created
//                     as a parameter (ParameterName written, GUID refreshed)
//   defaultValue    - optional parameter default (number / color text / color array)
//   texture         - optional texture asset path for texture-family expressions; an
//                     unresolvable path fails the step, because a null texture is what
//                     later turns into a material compile error on the nodes wired to it
//
// Any other payload key is written onto the expression as a property, so a step
// can set the fields a task doc names without a new command per field.
//
// Response: { added, expressionId, expressionClass }. expressionId is the object
// name and is what connect_material_pins expects for that node.
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleAddMaterialExpression(const TSharedPtr<FJsonObject>& Payload)
{
	FString Reason;
	UMaterial* Material = ResolveStepMaterial(Payload, Reason);
	if (Material == nullptr)
	{
		return MaterialStepFailure(TEXT("added"), Reason);
	}

	const FString ClassRef = ReadString(Payload, TEXT("expressionClass"), TEXT("type"));
	if (ClassRef.IsEmpty())
	{
		return MaterialStepFailure(TEXT("added"), TEXT("Required field: expressionClass (e.g. MaterialExpressionIf)"));
	}
	UClass* Class = ForgeMindIR::ResolveClass(ForgeMindMaterial::ExpressionClassName(ClassRef));
	if (Class == nullptr)
	{
		return MaterialStepFailure(TEXT("added"), FString::Printf(TEXT("expression class '%s' is not available in this editor"), *ClassRef));
	}

	const FString ParameterName = ReadString(Payload, TEXT("name"), TEXT("parameterName"));
	UMaterialExpression* Expression = ForgeMindMaterial::CreateExpression(Material, Class, ParameterName);
	if (Expression == nullptr)
	{
		return MaterialStepFailure(TEXT("added"), FString::Printf(TEXT("failed to create %s"), *Class->GetName()));
	}

	// The documented texture field is part of what this step asked for, so a path that
	// does not resolve is a failed step rather than a note in the response: writing a
	// texture reference that silently stays null is precisely how a missing asset ends
	// up as a compile error on every node downstream of the sample.
	const FString TexturePath = ReadString(Payload, TEXT("texture"));
	if (!TexturePath.IsEmpty())
	{
		FString TextureError;
		if (!ForgeMindMaterial::SetValue(Expression, TEXT("Texture"), MakeShared<FJsonValueString>(TexturePath), TextureError))
		{
			return MaterialStepFailure(TEXT("added"), FString::Printf(TEXT("%s: %s"), *ClassRef, *TextureError));
		}
	}

	TArray<FString> Failed;
	if (Payload.IsValid())
	{
		for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Payload->Values)
		{
			const FString Key = Pair.Key;
			// path / class selection is the command envelope; the parameter name was
			// already written by CreateExpression (with the GUID refresh that has to
			// follow it), so neither is replayed as a plain property.
			if (Key == TEXT("path") || Key == TEXT("assetPath") || Key == TEXT("expressionClass")
				|| Key == TEXT("type") || Key == TEXT("name") || Key == TEXT("parameterName")
				|| Key == TEXT("texture"))
			{
				continue;
			}
			FString Error;
			if (!ForgeMindMaterial::SetValue(Expression, Key, Pair.Value, Error))
			{
				Failed.Add(FString::Printf(TEXT("%s: %s"), *Key, *Error));
			}
		}
	}

	TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetBoolField(TEXT("added"), true);
	Result->SetStringField(TEXT("expressionId"), Expression->GetName());
	Result->SetStringField(TEXT("expressionClass"), ForgeMindMaterial::ExpressionTypeName(Expression));
	TArray<UMaterialExpression*> All;
	ForgeMindMaterial::GetExpressions(Material, All);
	Result->SetNumberField(TEXT("expressionCount"), All.Num());
	// Same automatic tidy the blueprint graph commands run after mutating the graph:
	// a freshly created expression has no editor position at all, so without this every
	// bridge-assembled material opens as a pile of nodes on one spot.
	ForgeMindMaterial::ArrangeLayout(Material);
	if (Failed.Num() > 0)
	{
		// A field the expression does not carry is reported, not dropped: the step
		// asked for it and the asset does not hold it.
		Result->SetStringField(TEXT("propertyErrors"), FString::Join(Failed, TEXT("; ")));
	}
	return Result;
}

// ---------------------------------------------------------------------------
// connect_material_pins
//
// Link form:
//   path, fromExpression, fromPin, toExpression, toPin
//     - connects the output named by fromPin of fromExpression to the input named
//       by toPin of toExpression. toExpression may be "Material", which targets a
//       material output pin (BaseColor / EmissiveColor / ...).
//     - fromPin selects the output index the same way the DSL swizzles do:
//       "" / Out / RGB / RGBA / Result -> 0, R / G / B / A -> 1..4.
//       The default output connects directly. A channel name (R/G/B/A) is
//       realized the way the editor builds it - a ComponentMask extracting that
//       channel from the source's default output is inserted between source and
//       target, because wiring the raw index would either be silently dropped by
//       ConnectExpression (the pin stays unwired) or land on an output whose
//       name does not mean the requested channel; the response names the
//       inserted node in channelMaskExpressionId.
//     - toPin is the engine real name of the target's FExpressionInput UPROPERTY
//       (e.g. "A" / "B" for Add, "Input" for ComponentMask - the input the editor
//       draws unlabeled), the same name the IR inputs[] entries carry. An empty
//       toPin is rejected instead of guessed: resolution is by what is found.
//
// Constant form:
//   path, expression (or fromExpression), pin (or fromPin), constant
//     - creates the constant expression that carries the value and connects it,
//       which is what the Asset IR channel does for {"value": ...} inputs, so the
//       two channels end with the same nodes.
//     - constant accepts 0.7, "(R=1,G=0,B=0)", "(R=1,G=0,B=0,A=1)" and [r,g,b[,a]].
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleConnectMaterialPins(const TSharedPtr<FJsonObject>& Payload)
{
	FString Reason;
	UMaterial* Material = ResolveStepMaterial(Payload, Reason);
	if (Material == nullptr)
	{
		return MaterialStepFailure(TEXT("connected"), Reason);
	}

	const FString FromId = ReadString(Payload, TEXT("fromExpression"));
	const FString FromPin = ReadString(Payload, TEXT("fromPin"));
	const FString ToId = ReadString(Payload, TEXT("toExpression"));
	const FString ToPin = ReadString(Payload, TEXT("toPin"));

	// Target input of both forms.
	FString TargetId = ToId;
	FString TargetPin = ToPin;
	const TSharedPtr<FJsonValue>* ConstantField = Payload.IsValid() ? Payload->Values.Find(TEXT("constant")) : nullptr;
	const bool bConstantForm = ConstantField != nullptr && (*ConstantField).IsValid();
	if (bConstantForm && TargetId.IsEmpty())
	{
		TargetId = FromId;
		TargetPin = FromPin;
	}

	if (TargetId.IsEmpty() || TargetPin.IsEmpty())
	{
		return MaterialStepFailure(TEXT("connected"), TEXT("Required fields: toExpression and toPin (or, for the constant form, fromExpression, fromPin and constant)"));
	}

	FExpressionInput* Input = nullptr;
	if (TargetId == TEXT("Material") || TargetId == TEXT("MaterialInterface"))
	{
		Input = ForgeMindMaterial::GetMaterialOutputInput(Material, TargetPin);
		if (Input == nullptr)
		{
			return MaterialStepFailure(TEXT("connected"), FString::Printf(TEXT("'%s' is not a material output pin of this material"), *TargetPin));
		}
	}
	else
	{
		UMaterialExpression* Target = ForgeMindMaterial::FindExpression(Material, TargetId);
		if (Target == nullptr)
		{
			return MaterialStepFailure(TEXT("connected"), FString::Printf(TEXT("no expression '%s' in the material (use the expressionId returned by add_material_expression)"), *TargetId));
		}
		Input = ForgeMindMaterial::GetExpressionInput(Target, TargetPin);
		if (Input == nullptr)
		{
			return MaterialStepFailure(TEXT("connected"), FString::Printf(TEXT("'%s' is not an input of %s"), *TargetPin, *ForgeMindMaterial::ExpressionTypeName(Target)));
		}
	}

	TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
	if (bConstantForm)
	{
		FString Error;
		if (!ForgeMindMaterial::ConnectConstant(Input, Material, *ConstantField, Error))
		{
			return MaterialStepFailure(TEXT("connected"), Error);
		}
		Result->SetBoolField(TEXT("connected"), true);
		Result->SetStringField(TEXT("toExpression"), TargetId);
		Result->SetStringField(TEXT("toPin"), TargetPin);
		Result->SetStringField(TEXT("mode"), TEXT("constant"));
		if (Input->Expression != nullptr)
		{
			Result->SetStringField(TEXT("constantExpressionId"), Input->Expression->GetName());
			Result->SetStringField(TEXT("constantExpressionClass"), ForgeMindMaterial::ExpressionTypeName(Input->Expression));
		}
		// A new node - the constant just created - is in the graph, so tidy like the
		// link form below does.
		ForgeMindMaterial::ArrangeLayout(Material);
		return Result;
	}

	UMaterialExpression* Source = ForgeMindMaterial::FindExpression(Material, FromId);
	if (Source == nullptr)
	{
		return MaterialStepFailure(TEXT("connected"), FString::Printf(TEXT("no expression '%s' in the material (use the expressionId returned by add_material_expression)"), *FromId));
	}
	int32 OutputIndex = 0;
	if (!ForgeMindMaterial::TryResolveOutputIndex(FromPin, OutputIndex))
	{
		return MaterialStepFailure(TEXT("connected"), FString::Printf(TEXT("'%s' is not an output of a material expression (expected one of Out / RGB / RGBA / R / G / B / A)"), *FromPin));
	}
	FString ConnectError;
	if (!ForgeMindMaterial::ConnectOutputInput(Material, Input, Source, OutputIndex, ConnectError))
	{
		return MaterialStepFailure(TEXT("connected"), ConnectError);
	}

	// A new wire - and, for a channel request against a single-output source, the
	// inserted extraction mask - changes the layout-relevant structure of the
	// graph, so re-run the tidy exactly like connect_blueprint_pins does.
	ForgeMindMaterial::ArrangeLayout(Material);

	Result->SetBoolField(TEXT("connected"), true);
	Result->SetStringField(TEXT("fromExpression"), FromId);
	Result->SetNumberField(TEXT("fromOutputIndex"), OutputIndex);
	Result->SetStringField(TEXT("toExpression"), TargetId);
	Result->SetStringField(TEXT("toPin"), TargetPin);
	// Report the node the channel realization inserted, the way the constant form
	// reports constantExpressionId: the caller sees the graph it actually got.
	if (Input->Expression != nullptr && Input->Expression != Source)
	{
		Result->SetStringField(TEXT("channelMaskExpressionId"), Input->Expression->GetName());
		Result->SetStringField(TEXT("channelMaskExpressionClass"), ForgeMindMaterial::ExpressionTypeName(Input->Expression));
	}
	return Result;
}

// ---------------------------------------------------------------------------
// compile_material
//
// Payload: path
//
// Drives the same recompile the material editor runs after a graph edit and
// reports the state it ended in. Shader compilation itself is asynchronous in the
// editor, so "pending" is reported instead of pretending the job already landed;
// the asset is left dirty so save_asset persists it.
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleCompileMaterial(const TSharedPtr<FJsonObject>& Payload)
{
	FString Reason;
	UMaterial* Material = ResolveStepMaterial(Payload, Reason);
	if (Material == nullptr)
	{
		return MaterialStepFailure(TEXT("compiled"), Reason);
	}

	Material->PostEditChange();
	Material->MarkPackageDirty();
	// Final tidy pass, mirroring compile_blueprint's re-layout after a graph edit.
	ForgeMindMaterial::ArrangeLayout(Material);

	TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetBoolField(TEXT("compiled"), true);
	Result->SetStringField(TEXT("path"), Material->GetPathName());
	Result->SetStringField(TEXT("materialState"),
		Material->GetMaterialResource(GMaxRHIFeatureLevel) != nullptr ? TEXT("compiled") : TEXT("pending"));
	Result->SetBoolField(TEXT("compilingOrHadCompileError"), Material->IsCompilingOrHadCompileError(GMaxRHIFeatureLevel));
	TArray<UMaterialExpression*> Expressions;
	ForgeMindMaterial::GetExpressions(Material, Expressions);
	Result->SetNumberField(TEXT("expressionCount"), Expressions.Num());
	return Result;
}
