// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Material step commands: create_material / create_material_function /
// add_material_expression / connect_material_pins / compile_material.
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
#include "Materials/MaterialFunction.h"
#include "Misc/PackageName.h"
#include "RHI.h"
#include "ShaderCompiler.h"
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

	/**
	 * Append parameter entries to a material parameter collection through reflection.
	 *
	 * A collection's parameters live in the UPROPERTY arrays ScalarParameters and
	 * VectorParameters; each element is a struct carrying the base's "ParameterName" (FName)
	 * plus its own "DefaultValue" (float, or FLinearColor for a vector). Going through the
	 * properties rather than the engine's typed helpers keeps this file free of any symbol the
	 * Engine module might not export - the trap that turned Cast on a non-exported class into
	 * an unresolved external (see ResolveLayerBlendInput in ForgeMindMaterialOps.cpp).
	 *
	 * An empty collection is not a usable target for a CollectionParameter node, so a step
	 * command that creates one can fill it here; a material that references a name the
	 * collection does not hold is rejected by the engine while compiling shaders, which no
	 * step reported until both halves of that were fixed.
	 */
	bool AddCollectionParameters(UObject* Collection, const TCHAR* ArrayPropertyName, const TArray<TSharedPtr<FJsonValue>>& Entries, FString& OutError)
	{
		FArrayProperty* Array = Collection != nullptr ? CastField<FArrayProperty>(Collection->GetClass()->FindPropertyByName(ArrayPropertyName)) : nullptr;
		FStructProperty* Element = Array != nullptr ? CastField<FStructProperty>(Array->Inner) : nullptr;
		if (Element == nullptr)
		{
			OutError = FString::Printf(TEXT("the collection carries no '%s' parameter array"), ArrayPropertyName);
			return false;
		}
		FNameProperty* NameProperty = FindFProperty<FNameProperty>(Element->Struct, TEXT("ParameterName"));
		FProperty* DefaultProperty = Element->Struct ? Element->Struct->FindPropertyByName(TEXT("DefaultValue")) : nullptr;
		if (NameProperty == nullptr || DefaultProperty == nullptr)
		{
			OutError = FString::Printf(TEXT("'%s' entries carry no ParameterName/DefaultValue"), ArrayPropertyName);
			return false;
		}

		FScriptArrayHelper Helper(Array, Array->ContainerPtrToValuePtr<void>(Collection));
		for (const TSharedPtr<FJsonValue>& Entry : Entries)
		{
			const TSharedPtr<FJsonObject>* Object = nullptr;
			if (Entry.IsValid() && Entry->TryGetObject(Object) && Object != nullptr)
			{
				FString ParameterName;
				(*Object)->TryGetStringField(TEXT("name"), ParameterName);
				if (ParameterName.IsEmpty())
				{
					OutError = FString::Printf(TEXT("an entry of '%s' has no name"), ArrayPropertyName);
					return false;
				}

				// AddValue constructs the struct, so the base's Id gets a real guid exactly as
				// it would when the details panel adds a row, and the name is simply overwritten
				// below (the struct's default name would otherwise collide inside one collection).
				void* ElementData = Helper.GetRawPtr(Helper.AddValue());
				NameProperty->SetPropertyValue(NameProperty->ContainerPtrToValuePtr<void>(ElementData), FName(*ParameterName));

				if (const TSharedPtr<FJsonValue>* RawDefault = (*Object)->Values.Find(TEXT("defaultValue")); RawDefault != nullptr)
				{
					void* DefaultValueData = DefaultProperty->ContainerPtrToValuePtr<void>(ElementData);
					if (FFloatProperty* ScalarValue = CastField<FFloatProperty>(DefaultProperty))
					{
						double Number = 0.0;
						(*RawDefault)->TryGetNumber(Number);
						ScalarValue->SetPropertyValue(DefaultValueData, static_cast<float>(Number));
					}
					else if (FDoubleProperty* DoubleValue = CastField<FDoubleProperty>(DefaultProperty))
					{
						double Number = 0.0;
						(*RawDefault)->TryGetNumber(Number);
						DoubleValue->SetPropertyValue(DefaultValueData, Number);
					}
					else if (FStructProperty* ColorValue = CastField<FStructProperty>(DefaultProperty))
					{
						TArray<double> Numbers;
						if ((*RawDefault)->Type == EJson::Array)
						{
							for (const TSharedPtr<FJsonValue>& Channel : (*RawDefault)->AsArray())
							{
								double ChannelValue = 0.0;
								if (Channel.IsValid() && Channel->TryGetNumber(ChannelValue))
								{
									Numbers.Add(ChannelValue);
								}
							}
						}
						else
						{
							FString Text;
							if ((*RawDefault)->TryGetString(Text))
							{
								// The shared component-list parser ("r,g,b,a", "[r,g,b]", "(R=..)"): 5.0.3's
								// FString has neither the character-separator Split overload nor Trim(), and
								// reusing this parser keeps the colour text grammar identical to the one the
								// material properties themselves accept.
								ForgeMindMaterial::ParseNumericList(Text, Numbers);
							}
						}
						if (Numbers.Num() >= 3 && ForgeMindMaterial::IsLinearColorStruct(ColorValue->Struct))
						{
							*static_cast<FLinearColor*>(DefaultValueData) = FLinearColor(
								static_cast<float>(Numbers[0]), static_cast<float>(Numbers[1]),
								static_cast<float>(Numbers[2]), static_cast<float>(Numbers.Num() > 3 ? Numbers[3] : 1.0));
						}
					}
				}
				continue;
			}
			OutError = FString::Printf(TEXT("an entry of '%s' is not an object with a name"), ArrayPropertyName);
			return false;
		}
		return true;
	}

	TSharedPtr<FJsonObject> MaterialStepFailure(const TCHAR* Flag, const FString& Reason)
	{
		TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
		Result->SetBoolField(Flag, false);
		Result->SetStringField(TEXT("reason"), Reason);
		return Result;
	}

	/**
	 * Locate the asset the step commands operate on: a material or a material function.
	 * Both carry a material expression graph, so add / connect / compile apply to either;
	 * the four commands only differ in which one create_* built.
	 */
	UObject* ResolveStepAsset(const TSharedPtr<FJsonObject>& Payload, FString& OutReason)
	{
		const FString Path = ReadString(Payload, TEXT("path"), TEXT("assetPath"));
		if (Path.IsEmpty())
		{
			OutReason = TEXT("Required field: path (project asset path, e.g. /Game/Tests/UE5_0/M_Material)");
			return nullptr;
		}
		UObject* Asset = ForgeMindMaterial::ResolveMaterial(Path);
		if (Cast<UMaterial>(Asset) == nullptr && Cast<UMaterialFunction>(Asset) == nullptr)
		{
			OutReason = FString::Printf(TEXT("no material or material function at '%s' (create it with create_material / create_material_function first)"), *Path);
			return nullptr;
		}
		return Asset;
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

	// Every payload key beyond the command envelope is a material setting the caller
	// asked for (e.g. bUseMaterialAttributes). Apply each through the same entry the IR
	// channel's materialSettings uses, and report the ones that did not land instead of
	// silently dropping them - a dropped key is exactly how a material that was never
	// switched into the requested mode still answered created=true.
	TArray<FString> FailedSettings;
	if (Payload.IsValid())
	{
		for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Payload->Values)
		{
			const FString& Key = Pair.Key;
			if (Key == TEXT("path") || Key == TEXT("assetPath") || Key == TEXT("domain") || Key == TEXT("blendMode"))
			{
				continue;
			}
			FString SettingError;
			if (!ForgeMindMaterial::ApplyMaterialSetting(Material, Key, Pair.Value, SettingError))
			{
				FailedSettings.Add(FString::Printf(TEXT("%s: %s"), *Key, *SettingError));
			}
		}
	}
	if (FailedSettings.Num() > 0)
	{
		Result->SetStringField(TEXT("propertyErrors"), FString::Join(FailedSettings, TEXT("; ")));
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
	UObject* Asset = ResolveStepAsset(Payload, Reason);
	if (Asset == nullptr)
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
	if (Class->HasAnyClassFlags(CLASS_Abstract))
	{
		// An abstract class is a real name in the index but not something the graph may hold:
		// the object gets built anyway and only aborts later from a pure virtual call. Say no
		// here and name the reason - the shared construction funnel refuses the same way
		// (ForgeMindMaterial::CreateExpression), this only makes the step's answer readable.
		return MaterialStepFailure(TEXT("added"), FString::Printf(TEXT("expression class '%s' is abstract - a concrete subclass has to be asked for"), *ClassRef));
	}

	const FString ParameterName = ReadString(Payload, TEXT("name"), TEXT("parameterName"));
	UMaterialExpression* Expression = ForgeMindMaterial::CreateExpression(Asset, Class, ParameterName);
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

	// Every property the step asked for has now been written; let the expression run the
	// fixups the details panel would have triggered. UMaterialExpressionCollectionParameter
	// derives its ParameterId from ParameterName only inside PostEditChangeProperty
	// (MaterialExpressions.cpp:15337-15351), and the compiler looks the collection entry up by
	// that Id - so a node that was merely property-written reports
	// "CollectionParameter has invalid parameter <name>" (run 20261029_1616, No. 86).
	Expression->PostEditChange();

	TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetBoolField(TEXT("added"), true);
	Result->SetStringField(TEXT("expressionId"), Expression->GetName());
	Result->SetStringField(TEXT("expressionClass"), ForgeMindMaterial::ExpressionTypeName(Expression));
	TArray<UMaterialExpression*> All;
	ForgeMindMaterial::GetExpressions(Asset, All);
	Result->SetNumberField(TEXT("expressionCount"), All.Num());
	// Same automatic tidy the blueprint graph commands run after mutating the graph:
	// a freshly created expression has no editor position at all, so without this every
	// bridge-assembled material opens as a pile of nodes on one spot.
	ForgeMindMaterial::ArrangeLayout(Asset);
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
	UObject* Asset = ResolveStepAsset(Payload, Reason);
	if (Asset == nullptr)
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

	UMaterialExpression* Source = bConstantForm ? nullptr : ForgeMindMaterial::FindExpression(Asset, FromId);
	if (ForgeMindMaterial::GetExecutionOutput(Source, FromPin) != nullptr)
	{
		// Material execution inputs are named "Input" in the engine graph; they must not go through the numeric input parser.
		if (TargetPin != TEXT("Input"))
		{
			return MaterialStepFailure(TEXT("connected"), TEXT("execution connections require toPin=Input"));
		}
		UMaterialExpression* Target = ForgeMindMaterial::FindExpression(Asset, TargetId);
		FString Error;
		if (!ForgeMindMaterial::ConnectExecutionOutput(Source, FromPin, Target, Error))
		{
			return MaterialStepFailure(TEXT("connected"), Error);
		}
		ForgeMindMaterial::ArrangeLayout(Asset);
		TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
		Result->SetBoolField(TEXT("connected"), true);
		Result->SetStringField(TEXT("mode"), TEXT("exec"));
		Result->SetStringField(TEXT("fromExpression"), FromId);
		Result->SetStringField(TEXT("fromPin"), FromPin);
		Result->SetStringField(TEXT("toExpression"), TargetId);
		Result->SetStringField(TEXT("toPin"), TargetPin);
		return Result;
	}

	FExpressionInput* Input = nullptr;
	if (TargetId == TEXT("Material") || TargetId == TEXT("MaterialInterface"))
	{
		Input = ForgeMindMaterial::GetMaterialOutputInput(Asset, TargetPin);
		if (Input == nullptr)
		{
			return MaterialStepFailure(TEXT("connected"), FString::Printf(TEXT("'%s' is not a material output pin of this material"), *TargetPin));
		}
		// A pin the engine will not read is not a connection: post-process materials accept an
		// Opacity wire and drop it at compile time (UMaterial::IsPropertyActive, Material.cpp:
		// 5286-5289), and until now every step around it still answered connected=true, so a task
		// doc question could stay green while asserting nothing.
		if (!ForgeMindMaterial::IsMaterialOutputActive(Asset, TargetPin))
		{
			return MaterialStepFailure(TEXT("connected"), FString::Printf(
				TEXT("'%s' is not an output this material uses - the engine ignores it for the current domain / blend mode / shader frequency (see UMaterial::IsPropertyActive)"), *TargetPin));
		}
	}
	else
	{
		UMaterialExpression* Target = ForgeMindMaterial::FindExpression(Asset, TargetId);
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
		if (!ForgeMindMaterial::ConnectConstant(Input, Asset, *ConstantField, Error))
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
		ForgeMindMaterial::ArrangeLayout(Asset);
		return Result;
	}

	if (Source == nullptr)
	{
		return MaterialStepFailure(TEXT("connected"), FString::Printf(TEXT("no expression '%s' in the material (use the expressionId returned by add_material_expression)"), *FromId));
	}
	int32 OutputIndex = 0;
	// Resolve against the outputs the source actually declares first: an expression
	// like TextureSample carries named outputs beyond the generic table (RGBA at
	// index 5, while index 0 is RGB), and a name that resolves there must land on
	// the engine's own pin rather than the generic index-0 default - wiring RGBA
	// to output 0 hands the target a float3 and turns into a shader cast error
	// after every step has already reported success. A name match also means the
	// index is a real pin, not a channel, so it is handed to ConnectOutputInput as
	// a named request (DecalDerivative's DDY lives at index 1, inside the range the
	// channel swizzles use).
	const bool bNamedOutput = ForgeMindMaterial::TryResolveNamedOutputIndex(Source, FromPin, OutputIndex);
	if (!bNamedOutput && !ForgeMindMaterial::TryResolveOutputIndex(FromPin, OutputIndex))
	{
		return MaterialStepFailure(TEXT("connected"), FString::Printf(TEXT("'%s' is not an output of a material expression (expected one of Out / RGB / RGBA / R / G / B / A, or one of the output names the source declares)"), *FromPin));
	}
	FString ConnectError;
	if (!ForgeMindMaterial::ConnectOutputInput(Asset, Input, Source, OutputIndex, ConnectError, bNamedOutput))
	{
		return MaterialStepFailure(TEXT("connected"), ConnectError);
	}

	// A new wire - and, for a channel request against a single-output source, the
	// inserted extraction mask - changes the layout-relevant structure of the
	// graph, so re-run the tidy exactly like connect_blueprint_pins does.
	ForgeMindMaterial::ArrangeLayout(Asset);

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
	UObject* Asset = ResolveStepAsset(Payload, Reason);
	if (Asset == nullptr)
	{
		return MaterialStepFailure(TEXT("compiled"), Reason);
	}

	Asset->PostEditChange();
	Asset->MarkPackageDirty();
	// Final tidy pass, mirroring compile_blueprint's re-layout after a graph edit.
	ForgeMindMaterial::ArrangeLayout(Asset);

	// Report what the engine actually made of the graph. Shader compilation is
	// asynchronous in the editor, so the queue is drained first (the same way the
	// editor's own harness does) and the compile state is read back afterwards - until
	// now this step answered compiled=true whatever the material did, which let a
	// material the engine rejected pass its case (user note 2026-10-28, biting again in
	// the run after 20261029_1534). Anything that still cannot be judged here - a
	// material whose shader map nobody requested yet - is said so out loud in
	// shaderMapState instead of being reported as success.
	if (GShaderCompilingManager != nullptr)
	{
		GShaderCompilingManager->FinishAllCompilation();
	}

	TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetStringField(TEXT("path"), Asset->GetPathName());
	bool bCompiled = true;
	TArray<FString> CompileErrors;
	FString ShaderMapState = TEXT("function");
	// The compile state below is a UMaterial question: a material function is compiled
	// into every material that calls it and carries no resource of its own, so for a
	// function the step reports the graph it left behind instead of a shader state.
	if (UMaterial* Material = Cast<UMaterial>(Asset))
	{
		const FMaterialResource* Resource = Material->GetMaterialResource(GMaxRHIFeatureLevel);
		if (Resource == nullptr)
		{
			ShaderMapState = TEXT("missing resource");
		}
		else if (!Resource->IsCompilationFinished())
		{
			ShaderMapState = TEXT("pending");
		}
		else
		{
			ShaderMapState = TEXT("finished");
			for (const FString& Error : Resource->GetCompileErrors())
			{
				CompileErrors.Add(Error);
			}
		}
		if (CompileErrors.Num() == 0 && ShaderMapState == TEXT("finished")
			&& Material->IsCompilingOrHadCompileError(GMaxRHIFeatureLevel))
		{
			// The engine flagged a failure but left no message behind; report the fact
			// rather than letting it turn into a silent pass.
			CompileErrors.Add(TEXT("the engine reported a shader compile error without a message"));
		}
		bCompiled = CompileErrors.Num() == 0;
	}
	Result->SetBoolField(TEXT("compiled"), bCompiled);
	Result->SetStringField(TEXT("materialState"), Cast<UMaterial>(Asset) != nullptr && !bCompiled ? TEXT("failed") : ShaderMapState);
	Result->SetStringField(TEXT("shaderMapState"), ShaderMapState);
	if (CompileErrors.Num() > 0)
	{
		Result->SetStringField(TEXT("reason"), FString::Join(CompileErrors, TEXT("; ")));
		TArray<TSharedPtr<FJsonValue>> ErrorValues;
		for (const FString& Error : CompileErrors)
		{
			ErrorValues.Add(MakeShared<FJsonValueString>(Error));
		}
		Result->SetArrayField(TEXT("compileErrors"), ErrorValues);
	}
	TArray<UMaterialExpression*> Expressions;
	ForgeMindMaterial::GetExpressions(Asset, Expressions);
	Result->SetNumberField(TEXT("expressionCount"), Expressions.Num());
	return Result;
}

// ---------------------------------------------------------------------------
// create_material_function
//
// Payload:
//   path - project asset path, e.g. /Game/Functions/F_MyFunc
//
// A material function is the graph a MaterialFunctionCall node calls: it carries
// expressions like a material, but no output pins of its own - its interface is the
// FunctionInput / FunctionOutput expressions placed in it. The same step commands
// (add_material_expression / connect_material_pins / compile_material) build that
// graph, which is why this command exists next to create_material rather than as a
// separate family: from here on the two asset kinds follow the same path.
//
// The asset is registered with the asset registry and left dirty; the caller
// persists it with save_asset.
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleCreateMaterialFunction(const TSharedPtr<FJsonObject>& Payload)
{
	const FString AssetPath = ReadString(Payload, TEXT("path"), TEXT("assetPath"));
	if (AssetPath.IsEmpty())
	{
		return MaterialStepFailure(TEXT("created"), TEXT("Required field: path (project asset path, e.g. /Game/Functions/F_MyFunc)"));
	}

	FString PackagePath;
	FString AssetName;
	FString Reason;
	if (!PrepareNewAssetLocation(AssetPath, TEXT("/Game/Functions/F_MyFunc"), PackagePath, AssetName, Reason))
	{
		return MaterialStepFailure(TEXT("created"), Reason);
	}

	// The same creation call the Asset IR material handler uses for a material function
	// (CreateAssetInstance registers the asset and leaves the package dirty), so the
	// function a step command builds and the one an IR definition builds start out as the
	// same object.
	UObject* Asset = ForgeMindIR::CreateAssetInstance(UMaterialFunction::StaticClass(), PackagePath, false);
	UMaterialFunction* Function = Cast<UMaterialFunction>(Asset);
	if (Function == nullptr)
	{
		return MaterialStepFailure(TEXT("created"), FString::Printf(TEXT("material function creation failed at '%s'"), *PackagePath));
	}

	TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetBoolField(TEXT("created"), true);
	Result->SetBoolField(TEXT("dirty"), Function->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("path"), FString::Printf(TEXT("%s.%s"), *PackagePath, *AssetName));
	TArray<UMaterialExpression*> Expressions;
	ForgeMindMaterial::GetExpressions(Function, Expressions);
	Result->SetNumberField(TEXT("expressionCount"), Expressions.Num());
	return Result;
}

// ---------------------------------------------------------------------------
// create_material_parameter_collection
//
// Payload:
//   path    - project asset path, e.g. /Game/Tests/UE5_0/MPC_Demo
//   scalars - optional array of { name, defaultValue(number) }
//   vectors - optional array of { name, defaultValue(number[] or "r,g,b,a") }
//
// A MaterialParameterCollection is the asset a CollectionParameter node's "Collection"
// reference points at. Without a way to create it, that reference can only ever fail to
// resolve - and an unresolvable object reference is reported, never quietly stored as null
// (the same rule the material texture reference follows). An empty collection resolves but is
// still nothing to read: the engine rejects the material while compiling it, which the suite
// did not see either until compile_material started reporting shader errors. Both halves are
// covered now: the command can fill the parameter list, and a bad compile ends the run.
//
// The class is resolved by name through reflection rather than by including its header and
// naming UMaterialParameterCollection::StaticClass(): a class whose UCLASS carries no export
// macro links fine here and then fails at link time with an unresolved GetPrivateStaticClass
// (see ResolveLayerBlendInput in ForgeMindMaterialOps.cpp).
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleCreateMaterialParameterCollection(const TSharedPtr<FJsonObject>& Payload)
{
	const FString AssetPath = ReadString(Payload, TEXT("path"), TEXT("assetPath"));
	if (AssetPath.IsEmpty())
	{
		return MaterialStepFailure(TEXT("created"), TEXT("Required field: path (project asset path, e.g. /Game/Tests/UE5_0/MPC_Demo)"));
	}

	FString PackagePath;
	FString AssetName;
	FString Reason;
	if (!PrepareNewAssetLocation(AssetPath, TEXT("/Game/Tests/UE5_0/MPC_Demo"), PackagePath, AssetName, Reason))
	{
		return MaterialStepFailure(TEXT("created"), Reason);
	}

	UClass* CollectionClass = ForgeMindIR::ResolveClass(TEXT("MaterialParameterCollection"));
	if (CollectionClass == nullptr)
	{
		return MaterialStepFailure(TEXT("created"), TEXT("UMaterialParameterCollection is not available in this editor"));
	}
	UObject* Collection = ForgeMindIR::CreateAssetInstance(CollectionClass, PackagePath, false);
	if (Collection == nullptr)
	{
		return MaterialStepFailure(TEXT("created"), FString::Printf(TEXT("material parameter collection creation failed at '%s'"), *PackagePath));
	}

	TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetBoolField(TEXT("created"), true);
	Result->SetBoolField(TEXT("dirty"), Collection->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("path"), FString::Printf(TEXT("%s.%s"), *PackagePath, *AssetName));

	// Fill the parameter list when the payload asks for one. The names have to exist inside
	// the collection or every node pointing at them is a material the engine will refuse.
	TArray<FString> ParameterErrors;
	if (Payload.IsValid())
	{
		for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Payload->Values)
		{
			FString ArrayPropertyName;
			if (Pair.Key.Equals(TEXT("scalars"), ESearchCase::IgnoreCase))
			{
				ArrayPropertyName = TEXT("ScalarParameters");
			}
			else if (Pair.Key.Equals(TEXT("vectors"), ESearchCase::IgnoreCase))
			{
				ArrayPropertyName = TEXT("VectorParameters");
			}
			else
			{
				continue;
			}
			if (Pair.Value.IsValid() && Pair.Value->Type == EJson::Array)
			{
				FString Error;
				if (!AddCollectionParameters(Collection, *ArrayPropertyName, Pair.Value->AsArray(), Error))
				{
					ParameterErrors.Add(Error);
				}
			}
		}
	}
	if (ParameterErrors.Num() > 0)
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetBoolField(TEXT("compiled"), false);
		Result->SetStringField(TEXT("reason"), FString::Join(ParameterErrors, TEXT("; ")));
		return Result;
	}
	// Tell the collection its contents changed. It rebuilds the parameter caches and the
	// uniform buffer layout inside PostEditChangeProperty (and, for an asset that was only
	// just created in memory, that pass is also what builds the layout PostLoad would have
	// prepared) - skipping it leaves a material that references the collection asserting
	// "UniformBufferStruct" inside the shader compile (crash seen running No. 86 after
	// this command was added).
	Collection->PostEditChange();
	Collection->MarkPackageDirty();
	Result->SetBoolField(TEXT("dirty"), Collection->GetPackage()->IsDirty());
	return Result;
}
