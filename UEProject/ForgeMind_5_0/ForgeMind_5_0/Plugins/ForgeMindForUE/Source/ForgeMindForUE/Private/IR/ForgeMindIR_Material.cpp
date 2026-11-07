// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Material domain IR handler: material / material_function /
// material_instance_constant.
//
// Expression graphs are built in two passes (create → wire) because the
// IR allows forward references between nodes.
//
// Expression creation, registration, pin lookup and constant wiring come from
// ForgeMindMaterialOps, the same layer the material step commands
// (create_material / add_material_expression / connect_material_pins) use, so a
// graph built from an IR definition and one built step by step end up identical -
// the property a task doc verifies by comparing the IR of its A and B assets.

#include "ForgeMindIRHandler.h"
#include "ForgeMindIRGeneric.h"
#include "ForgeMindMaterialOps.h"

#include "AssetToolsModule.h"
#include "Engine/Texture.h"
#include "Factories/MaterialFactoryNew.h"
#include "IAssetTools.h"
#include "Materials/Material.h"
#include "Materials/MaterialExpression.h"
#include "Materials/MaterialExpressionCustom.h"
#include "Materials/MaterialExpressionSetMaterialAttributes.h"
#include "Materials/MaterialExpressionLandscapeLayerBlend.h"
#include "MaterialShared.h"
#include "Materials/MaterialExpressionFunctionInput.h"
#include "Materials/MaterialExpressionMaterialFunctionCall.h"
#include "Materials/MaterialExpressionParameter.h"
#include "Materials/MaterialExpressionStaticBool.h"
#include "Materials/MaterialInstanceConstant.h"
#include "RHI.h"
#include "UObject/UnrealType.h"

namespace
{
	/** Node properties that are part of the IR schema rather than expression state. */
	bool IsStructuralNodeKey(const FString& Key)
	{
		return Key == TEXT("id") || Key == TEXT("type") || Key == TEXT("inputs") || Key == TEXT("functionAsset");
	}

	/** True for the two signature node types a material function graph carries. */
	bool IsFunctionIoType(const FString& Type, const TCHAR* Bare)
	{
		return Type.Equals(Bare, ESearchCase::IgnoreCase)
			|| Type.Equals(FString(TEXT("MaterialExpression")) + Bare, ESearchCase::IgnoreCase);
	}

	/**
	 * The interface name a signature node stands for. The function translator emits the
	 * nodes as "funcIn_<name>" / "funcOut_<name>" and keeps the name, type and default in
	 * the definition's functionInputs / functionOutputs entries - the graph node on its
	 * own carries no name at all.
	 */
	FString FunctionIoName(const FString& NodeId, const TCHAR* Prefix)
	{
		if (NodeId.StartsWith(Prefix, ESearchCase::IgnoreCase))
		{
			return NodeId.Mid(FCString::Strlen(Prefix));
		}
		return FString();
	}

	/** The functionInputs / functionOutputs entry of a given name. */
	TSharedPtr<FJsonObject> FindFunctionIoEntry(const TSharedPtr<FJsonObject>& Definition, const TCHAR* ArrayName, const FString& Name)
	{
		if (!Definition.IsValid() || Name.IsEmpty())
		{
			return nullptr;
		}
		const TArray<TSharedPtr<FJsonValue>>* Entries = nullptr;
		if (!Definition->TryGetArrayField(ArrayName, Entries))
		{
			return nullptr;
		}
		for (const TSharedPtr<FJsonValue>& Value : *Entries)
		{
			const TSharedPtr<FJsonObject> Entry = Value.IsValid() ? Value->AsObject() : nullptr;
			FString EntryName;
			if (Entry.IsValid() && Entry->TryGetStringField(TEXT("name"), EntryName)
				&& EntryName.Equals(Name, ESearchCase::IgnoreCase))
			{
				return Entry;
			}
		}
		return nullptr;
	}

	/**
	 * IR / DSL data-type words -> the EFunctionInputType name the engine enum resolves.
	 * The IR keeps the DSL's own words (Float1 / Float3 / Vector ...  per the domain IR
	 * rules), so the translation to the engine's spelling happens here once.
	 */
	FString FunctionInputTypeWord(const FString& DataType)
	{
		if (DataType.Equals(TEXT("Float1"), ESearchCase::IgnoreCase) || DataType.Equals(TEXT("Scalar"), ESearchCase::IgnoreCase))
		{
			return TEXT("Scalar");
		}
		if (DataType.Equals(TEXT("Float2"), ESearchCase::IgnoreCase) || DataType.Equals(TEXT("Vector2"), ESearchCase::IgnoreCase))
		{
			return TEXT("Vector2");
		}
		if (DataType.Equals(TEXT("Float3"), ESearchCase::IgnoreCase) || DataType.Equals(TEXT("Vector3"), ESearchCase::IgnoreCase))
		{
			return TEXT("Vector3");
		}
		if (DataType.Equals(TEXT("Float4"), ESearchCase::IgnoreCase) || DataType.Equals(TEXT("Vector4"), ESearchCase::IgnoreCase)
			|| DataType.Equals(TEXT("LinearColor"), ESearchCase::IgnoreCase))
		{
			return TEXT("Vector4");
		}
		if (DataType.Equals(TEXT("Bool"), ESearchCase::IgnoreCase) || DataType.Equals(TEXT("StaticBool"), ESearchCase::IgnoreCase))
		{
			return TEXT("StaticBool");
		}
		// Texture2D / TextureCube / MaterialAttributes ... already spell the engine name.
		return DataType;
	}

	/**
	 * Name and type of a FunctionInput node, taken from the functionInputs entry it stands
	 * for. Without this the function exposes an unnamed input of the default type, and a
	 * caller addressing it by the name the task doc gives finds nothing to wire.
	 */
	void ApplyFunctionInputInterface(UMaterialExpression* Expression, FIRApplyContext& Ctx, const FString& NodeId)
	{
		const FString Name = FunctionIoName(NodeId, TEXT("funcIn_"));
		if (Name.IsEmpty())
		{
			Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), FString::Printf(TEXT("node '%s' is a function input without a name"), *NodeId));
			return;
		}
		FString Error;
		if (!ForgeMindMaterial::SetValue(Expression, TEXT("InputName"), MakeShared<FJsonValueString>(Name), Error))
		{
			Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), FString::Printf(TEXT("function input '%s': %s"), *Name, *Error));
		}

		const TSharedPtr<FJsonObject> Entry = FindFunctionIoEntry(Ctx.Definition, TEXT("functionInputs"), Name);
		if (!Entry.IsValid())
		{
			return;
		}
		FString DataType;
		if (Entry->TryGetStringField(TEXT("paramType"), DataType) || Entry->TryGetStringField(TEXT("type"), DataType))
		{
			if (!ForgeMindMaterial::SetValue(Expression, TEXT("InputType"), MakeShared<FJsonValueString>(FunctionInputTypeWord(DataType)), Error))
			{
				Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), FString::Printf(TEXT("function input '%s': %s"), *Name, *Error));
			}
		}
		if (Entry->HasField(TEXT("defaultValue")))
		{
			// An unconnected input falls back to the preview value, which is what the
			// editor calls a function input's default.
			if (!ForgeMindMaterial::SetValue(Expression, TEXT("PreviewValue"), Entry->TryGetField(TEXT("defaultValue")), Error))
			{
				Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), FString::Printf(TEXT("function input '%s' default: %s"), *Name, *Error));
			}
			if (!ForgeMindMaterial::SetValue(Expression, TEXT("bUsePreviewValueAsDefault"), MakeShared<FJsonValueBoolean>(true), Error))
			{
				Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), FString::Printf(TEXT("function input '%s': %s"), *Name, *Error));
			}
		}
	}

	/** Name of a FunctionOutput node, taken from the functionOutputs entry it stands for. */
	void ApplyFunctionOutputInterface(UMaterialExpression* Expression, FIRApplyContext& Ctx, const FString& NodeId)
	{
		const FString Name = FunctionIoName(NodeId, TEXT("funcOut_"));
		if (Name.IsEmpty())
		{
			Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), FString::Printf(TEXT("node '%s' is a function output without a name"), *NodeId));
			return;
		}
		FString Error;
		if (!ForgeMindMaterial::SetValue(Expression, TEXT("OutputName"), MakeShared<FJsonValueString>(Name), Error))
		{
			Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), FString::Printf(TEXT("function output '%s': %s"), *Name, *Error));
		}
	}

	/** Properties read back per expression node when the caller asks for a graph/full IR. */
	void CollectExpressionProperties(UMaterialExpression* Expression, TSharedRef<FJsonObject> Entry)
	{
		const FString ClassName = ForgeMindMaterial::ExpressionTypeName(Expression);
		if (Expression->IsA(UMaterialExpressionParameter::StaticClass()))
		{
			if (const TSharedPtr<FJsonValue> Name = ForgeMindMaterial::GetValue(Expression, TEXT("ParameterName")))
			{
				Entry->SetStringField(TEXT("parameterName"), Name->AsString());
			}
			if (const TSharedPtr<FJsonValue> Default = ForgeMindMaterial::GetValue(Expression, TEXT("DefaultValue")))
			{
				Entry->SetField(TEXT("defaultValue"), Default);
			}
		}
		// Constant family: the value the node carries (R / R,G / Constant colour).
		if (ClassName.Contains(TEXT("Constant"), ESearchCase::CaseSensitive))
		{
			if (const TSharedPtr<FJsonValue> Constant = ForgeMindMaterial::GetValue(Expression, TEXT("Constant")))
			{
				Entry->SetField(TEXT("constant"), Constant);
			}
			else
			{
				TArray<TSharedPtr<FJsonValue>> Components;
				for (const TCHAR* Key : { TEXT("R"), TEXT("G") })
				{
					if (const TSharedPtr<FJsonValue> Component = ForgeMindMaterial::GetValue(Expression, Key))
					{
						Components.Add(Component);
					}
				}
				if (Components.Num() > 0)
				{
					Entry->SetArrayField(TEXT("constant"), Components);
				}
			}
		}
		// StaticBool carries its value in the Value bitfield; the node-level
		// "constant" field is what the IR schema and the write side use for it.
		else if (Expression->IsA(UMaterialExpressionStaticBool::StaticClass()))
		{
			if (const TSharedPtr<FJsonValue> Constant = ForgeMindMaterial::GetValue(Expression, TEXT("Value")))
			{
				Entry->SetField(TEXT("constant"), Constant);
			}
		}
		if (const TSharedPtr<FJsonValue> LocalName = ForgeMindMaterial::GetValue(Expression, TEXT("LocalName")))
		{
			Entry->SetField(TEXT("localName"), LocalName);
		}
		// Texture family: the referenced asset (null when the path did not resolve).
		if (const TSharedPtr<FJsonValue> Texture = ForgeMindMaterial::GetValue(Expression, TEXT("Texture")))
		{
			Entry->SetField(TEXT("texture"), Texture);
		}
		// A Custom node exists to carry authored content, and none of it came back: the write side
		// accepts Code / OutputType / Description as node-level properties, while a read that only
		// walked FExpressionInput members handed two materials with identical wires and different
		// HLSL back as equal - so neither the A/B comparison nor a baseline could notice the code
		// changing. The arrays are emitted only when populated, which keeps the shape of graphs that
		// use the single default output identical to what was baselined.
		if (UMaterialExpressionCustom* Custom = Cast<UMaterialExpressionCustom>(Expression))
		{
			if (const TSharedPtr<FJsonValue> Code = ForgeMindMaterial::GetValue(Expression, TEXT("Code")))
			{
				Entry->SetField(TEXT("code"), Code);
			}
			if (const TSharedPtr<FJsonValue> CustomOutputType = ForgeMindMaterial::GetValue(Expression, TEXT("OutputType")))
			{
				Entry->SetField(TEXT("outputType"), CustomOutputType);
			}
			if (const TSharedPtr<FJsonValue> Description = ForgeMindMaterial::GetValue(Expression, TEXT("Description")))
			{
				Entry->SetField(TEXT("description"), Description);
			}
			if (Custom->AdditionalOutputs.Num() > 0)
			{
				TArray<TSharedPtr<FJsonValue>> CustomOutputs;
				for (const FCustomOutput& Output : Custom->AdditionalOutputs)
				{
					TSharedRef<FJsonObject> OutputEntry = MakeShared<FJsonObject>();
					OutputEntry->SetStringField(TEXT("name"), Output.OutputName.ToString());
					const UEnum* TypeEnum = StaticEnum<ECustomMaterialOutputType>();
					OutputEntry->SetStringField(TEXT("outputType"), TypeEnum != nullptr
						? TypeEnum->GetNameStringByValue(static_cast<int64>(Output.OutputType))
						: FString::Printf(TEXT("%d"), static_cast<int32>(Output.OutputType)));
					CustomOutputs.Add(MakeShared<FJsonValueObject>(OutputEntry));
				}
				Entry->SetArrayField(TEXT("additionalOutputs"), CustomOutputs);
			}
			if (Custom->AdditionalDefines.Num() > 0)
			{
				TArray<TSharedPtr<FJsonValue>> Defines;
				for (const FCustomDefine& Define : Custom->AdditionalDefines)
				{
					TSharedRef<FJsonObject> DefineEntry = MakeShared<FJsonObject>();
					DefineEntry->SetStringField(TEXT("name"), Define.DefineName);
					DefineEntry->SetStringField(TEXT("value"), Define.DefineValue);
					Defines.Add(MakeShared<FJsonValueObject>(DefineEntry));
				}
				Entry->SetArrayField(TEXT("additionalDefines"), Defines);
			}
			if (Custom->IncludeFilePaths.Num() > 0)
			{
				TArray<TSharedPtr<FJsonValue>> Paths;
				for (const FString& IncludePath : Custom->IncludeFilePaths)
				{
					Paths.Add(MakeShared<FJsonValueString>(IncludePath));
				}
				Entry->SetArrayField(TEXT("includeFilePaths"), Paths);
			}
		}
		// Landscape layer rows, in authored order: the row count and each LayerName are what makes a
		// blend multi-layer, and neither is an FExpressionInput, so nothing else in the read reports
		// them. Emitted only when the node has rows, keeping the shape of a bare blend as baselined.
		// Read through reflection rather than Cast<UMaterialExpressionLandscapeLayerBlend>:
		// UHT marks the class LANDSCAPE_API but does not export its GetPrivateStaticClass, so a Cast
		// resolves the class lazily and fails to link, and StaticEnum<ELandscapeLayerBlendType> is the
		// same problem - the enum is taken from the BlendType property instead.
		if (const FArrayProperty* LayersProp = CastField<FArrayProperty>(
				Expression->GetClass()->FindPropertyByName(TEXT("Layers"))))
		{
			const FStructProperty* LayerStructProp = CastField<FStructProperty>(LayersProp->Inner);
			if (LayerStructProp != nullptr && LayerStructProp->Struct
				&& LayerStructProp->Struct->GetName().Equals(TEXT("LayerBlendInput")))
			{
				FScriptArrayHelper LayersArray(LayersProp, LayersProp->ContainerPtrToValuePtr<void>(Expression));
				if (LayersArray.Num() > 0)
				{
					const FProperty* NameProp = LayerStructProp->Struct->FindPropertyByName(TEXT("LayerName"));
					// UE 5.0.3 stores TEnumAsByte<...> as FByteProperty with its Enum member set;
					// FByteObjectProperty only exists from later engine versions.
					const FByteProperty* BlendProp = CastField<FByteProperty>(
						LayerStructProp->Struct->FindPropertyByName(TEXT("BlendType")));
					TArray<TSharedPtr<FJsonValue>> Layers;
					for (int32 Index = 0; Index < LayersArray.Num(); ++Index)
					{
						const void* Row = LayersArray.GetRawPtr(Index);
						TSharedRef<FJsonObject> LayerEntry = MakeShared<FJsonObject>();
						if (const FNameProperty* NameField = CastField<FNameProperty>(NameProp))
						{
							LayerEntry->SetStringField(TEXT("layerName"),
								NameField->GetPropertyValue(NameField->ContainerPtrToValuePtr<void>(Row)).ToString());
						}
						if (BlendProp != nullptr)
						{
							const uint8 BlendValue = BlendProp->GetPropertyValue(BlendProp->ContainerPtrToValuePtr<void>(Row));
							const UEnum* BlendTypeEnum = BlendProp->Enum;
							LayerEntry->SetStringField(TEXT("blendType"), BlendTypeEnum != nullptr
								? BlendTypeEnum->GetNameStringByValue(static_cast<int64>(BlendValue))
								: FString::Printf(TEXT("%d"), static_cast<int32>(BlendValue)));
						}
						Layers.Add(MakeShared<FJsonValueObject>(LayerEntry));
					}
					Entry->SetArrayField(TEXT("layers"), Layers);
				}
			}
		}
	}

	/** Connected FExpressionInput members of one object, as IR input entries. */
	void CollectExpressionInputs(UObject* Owner, TArray<TSharedPtr<FJsonValue>>& OutInputs,
		const TMap<UMaterialExpression*, FString>& Ids)
	{
		// Pins already reported, tracked by address. The reflection walk and the engine's own
		// GetInputs() hand out the same FExpressionInput address, so an address is the only key that
		// cannot report one wire twice. A name is not: UMaterialExpressionFunctionOutput does not
		// override GetInputName, so the engine answers NAME_None (rendered "None") for its A pin,
		// which matched no member name and got emitted a second time - that duplicate broke the
		// F_MyFunc baseline in run 20261031_1159.
		TArray<const FExpressionInput*> EmittedPins;

		for (TFieldIterator<FProperty> It(Owner->GetClass()); It; ++It)
		{
			const FStructProperty* StructProperty = CastField<FStructProperty>(*It);
			if (StructProperty == nullptr || StructProperty->Struct == nullptr
				|| !StructProperty->Struct->GetName().Equals(TEXT("ExpressionInput")))
			{
				continue;
			}
			// A static array member (ArrayDim > 1) is not one pin. The engine's own GetInputs()
			// expands it to one entry per element and GetInputName() names each of them - the two
			// base implementations walk the identical reflection path, MaterialExpressions.cpp:1026
			// and 1067 - so taking ContainerPtrToValuePtr() alone reports only element 0 and labels it
			// with the property name. On QualitySwitch that turned the Low slot into a pin called
			// "Inputs", while the caller had wired it as "Low" through the engine interface: the same
			// wire read back under a name no channel would ever ask for. Array pins belong to the
			// interface pass below, which uses the engine's own names.
			if (StructProperty->ArrayDim > 1)
			{
				continue;
			}
			const void* ValueData = StructProperty->ContainerPtrToValuePtr<void>(Owner);
			const FExpressionInput* Input = static_cast<const FExpressionInput*>(ValueData);
			if (Input == nullptr || Input->Expression == nullptr)
			{
				continue;
			}
			TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
			Entry->SetStringField(TEXT("name"), StructProperty->GetName());
			Entry->SetStringField(TEXT("expression"), Ids.FindRef(Input->Expression));
			Entry->SetNumberField(TEXT("outputIndex"), Input->OutputIndex);
			OutInputs.Add(MakeShared<FJsonValueObject>(Entry));
			EmittedPins.Add(Input);
		}

		// Execution output property references in the initializer point to successor nodes, opposite in direction from data inputs.
		if (UMaterialExpression* Expression = Cast<UMaterialExpression>(Owner))
		{
			TArray<FExpressionExecOutputEntry> ExecOutputs;
			Expression->GetExecOutputs(ExecOutputs);
			for (const FExpressionExecOutputEntry& Output : ExecOutputs)
			{
				if (UMaterialExpression* Target = Output.Output->GetExpression())
				{
					TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
					Entry->SetStringField(TEXT("name"), Output.Name.ToString());
					Entry->SetStringField(TEXT("expression"), Ids.FindRef(Target));
					Entry->SetStringField(TEXT("kind"), TEXT("exec"));
					OutInputs.Add(MakeShared<FJsonValueObject>(Entry));
				}
			}
		}
		if (UMaterialExpressionSetMaterialAttributes* Set = Cast<UMaterialExpressionSetMaterialAttributes>(Owner))
		{
			for (int32 Index = 0; Index < Set->Inputs.Num(); ++Index)
			{
				const FExpressionInput& Input = Set->Inputs[Index];
				if (Input.Expression == nullptr || (Index > 0 && !Set->AttributeSetTypes.IsValidIndex(Index - 1))) continue;
				TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
				Entry->SetStringField(TEXT("name"), Index == 0 ? TEXT("MaterialAttributes")
					: FMaterialAttributeDefinitionMap::GetAttributeName(Set->AttributeSetTypes[Index - 1]));
				Entry->SetStringField(TEXT("expression"), Ids.FindRef(Input.Expression));
				Entry->SetNumberField(TEXT("outputIndex"), Input.OutputIndex);
				OutInputs.Add(MakeShared<FJsonValueObject>(Entry));
				EmittedPins.Add(&Input);
			}
		}
		if (UMaterialExpressionCustom* Custom = Cast<UMaterialExpressionCustom>(Owner))
		{
			for (const FCustomInput& Input : Custom->Inputs)
			{
				if (Input.Input.Expression == nullptr) continue;
				TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
				Entry->SetStringField(TEXT("name"), Input.InputName.ToString());
				Entry->SetStringField(TEXT("expression"), Ids.FindRef(Input.Input.Expression));
				Entry->SetNumberField(TEXT("outputIndex"), Input.Input.OutputIndex);
				OutInputs.Add(MakeShared<FJsonValueObject>(Entry));
				EmittedPins.Add(&Input.Input);
			}
		}

		// Same blind spot for a LandscapeLayerBlend node: its layers live in the dynamic Layers
		// array (each FLayerBlendInput carrying LayerName plus the LayerInput / HeightInput wires),
		// so the static-member walk above reports the blend node as having no inputs at all - a
		// one-layer and a two-layer blend read back identically and every layer wire stays
		// invisible while connect_material_pins still answers "connected". Entries key on the
		// LayerName, exactly what the write side (ResolveLayerBlendInput) matches and appends.
		// The type is identified by name, not Cast<>: UHT marks the class LANDSCAPE_API but does not
		// export its GetPrivateStaticClass, so a Cast leaves an unresolved external at link time - the
		// same reason ForgeMindMaterial's layer resolution works this way.
		if (UClass* BlendClass = ForgeMindIR::ResolveClass(TEXT("MaterialExpressionLandscapeLayerBlend"));
			BlendClass != nullptr && Owner->IsA(BlendClass))
		{
			UMaterialExpressionLandscapeLayerBlend* Blend = static_cast<UMaterialExpressionLandscapeLayerBlend*>(Owner);
			for (const FLayerBlendInput& Layer : Blend->Layers)
			{
				const FString LayerName = Layer.LayerName.ToString();
				if (Layer.LayerInput.Expression != nullptr)
				{
					TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
					Entry->SetStringField(TEXT("name"), LayerName);
					Entry->SetStringField(TEXT("expression"), Ids.FindRef(Layer.LayerInput.Expression));
					Entry->SetNumberField(TEXT("outputIndex"), Layer.LayerInput.OutputIndex);
					OutInputs.Add(MakeShared<FJsonValueObject>(Entry));
					EmittedPins.Add(&Layer.LayerInput);
				}
				if (Layer.HeightInput.Expression != nullptr)
				{
					TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
					Entry->SetStringField(TEXT("name"), LayerName + TEXT(".HeightInput"));
					Entry->SetStringField(TEXT("expression"), Ids.FindRef(Layer.HeightInput.Expression));
					Entry->SetNumberField(TEXT("outputIndex"), Layer.HeightInput.OutputIndex);
					OutInputs.Add(MakeShared<FJsonValueObject>(Entry));
					EmittedPins.Add(&Layer.HeightInput);
				}
			}
		}

		// A function call's inputs are the entries of its dynamic FunctionInputs array,
		// not static class members, so the reflection walk above never sees them: a wire
		// written through the shared pin channel (step command or IR pass 2) would stay
		// invisible in read-back IR, leaving the call node's connections unverified
		// while every build step still reports success.
		if (const UMaterialExpressionMaterialFunctionCall* Call = Cast<UMaterialExpressionMaterialFunctionCall>(Owner))
		{
			for (const FFunctionExpressionInput& FnInput : Call->FunctionInputs)
			{
				if (FnInput.Input.Expression == nullptr)
				{
					continue;
				}
				// Pin name: the function input's own name - the same name the write side
				// matches on (FindFunctionCallInput) - so read-back entries key exactly
				// like the toPin the caller asked for. The transient expression pointer
				// can be null on a reloaded asset; the wire's Input.InputName copy
				// (written by GetInputsAndOutputs) still carries the name then.
				const FName PinName = FnInput.ExpressionInput != nullptr
					? FnInput.ExpressionInput->InputName
					: FnInput.Input.InputName;
				TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
				Entry->SetStringField(TEXT("name"), PinName.ToString());
				Entry->SetStringField(TEXT("expression"), Ids.FindRef(FnInput.Input.Expression));
				Entry->SetNumberField(TEXT("outputIndex"), FnInput.Input.OutputIndex);
				OutInputs.Add(MakeShared<FJsonValueObject>(Entry));
				EmittedPins.Add(&FnInput.Input);
			}
		}

		// And the same blind spot for pins that are not named members at all: QualitySwitch and
		// FeatureLevelSwitch hold their per-level wires in the static array Inputs[], which the
		// reflection walk cannot see and which the editor labels only through GetInputName(Index).
		// Resolution order mirrors ForgeMindMaterial::GetExpressionInput (members first, then the
		// expression interface), and anything named already by the passes above is skipped - so for
		// every graph baselined to date this emits nothing new: those nodes report their pin names
		// through members (A / B / Value / Position ...) or through the special cases above (Custom
		// arguments, SetMaterialAttributes pins, layer names, function-call inputs), and the names
		// match. Without this pass a per-level wire would be written by both channels and still be
		// invisible in the read-back IR, so an unwired switch and a wired one would look identical.
		if (UMaterialExpression* Expression = Cast<UMaterialExpression>(Owner))
		{
			// A named struct member is the reflection walk's jurisdiction even when that walk does not
			// report it: the walk matches the struct by the exact name "ExpressionInput", while a Break
			// or Blend node's MaterialAttributes pin is the derived FMaterialAttributesInput, so that
			// wire is deliberately left out here exactly as it was before this pass existed. Picking it
			// up would also label it with UMaterialExpressionBreakMaterialAttributes::GetInputName, which
			// answers a localized display string (varies by language) - not a stable identifier
			// and not a name the write side matches on. Closing that read-side blind spot changes several
			// baselines and is reported to the user as its own task.
			TArray<const void*> MemberPinAddresses;
			for (TFieldIterator<FStructProperty> It(Owner->GetClass()); It; ++It)
			{
				// Only what the member walk really reports counts as its jurisdiction, and that excludes
				// array elements on purpose - they are named by the engine interface instead. A derived
				// single member (Break / Blend MaterialAttributes, ArrayDim 1) still stays out of the read
				// exactly as it was before this pass existed, so no existing baseline changes shape.
				if ((*It)->ArrayDim > 1)
				{
					continue;
				}
				MemberPinAddresses.Add((*It)->ContainerPtrToValuePtr<void>(Owner));
			}

			const TArray<FExpressionInput*> InterfaceInputs = Expression->GetInputs();
			for (int32 Index = 0; Index < InterfaceInputs.Num(); ++Index)
			{
				const FExpressionInput* Input = InterfaceInputs[Index];
				if (Input == nullptr || Input->Expression == nullptr)
				{
					continue;
				}
				// The same pin reached through the engine interface - already reported under its
				// member name - is skipped by address, which is also what keeps every graph
				// baselined before this pass byte-identical.
				if (EmittedPins.Contains(Input) || MemberPinAddresses.Contains(static_cast<const void*>(Input)))
				{
					continue;
				}
				const FName ResolvedName = Expression->GetInputName(Index);
				if (ResolvedName.IsNone())
				{
					// A pin the engine cannot name would be written back as the literal "None" and
					// could never be matched by the write side, so it is not reported at all.
					continue;
				}
				TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
				Entry->SetStringField(TEXT("name"), ResolvedName.ToString());
				Entry->SetStringField(TEXT("expression"), Ids.FindRef(Input->Expression));
				Entry->SetNumberField(TEXT("outputIndex"), Input->OutputIndex);
				OutInputs.Add(MakeShared<FJsonValueObject>(Entry));
				EmittedPins.Add(Input);
			}
		}
	}
}

class FMaterialIRHandler : public IIRHandler
{
public:
	void GetAssetTypes(TArray<FString>& OutAssetTypes) const override
	{
		OutAssetTypes = { TEXT("material"), TEXT("material_function"), TEXT("material_instance_constant") };
	}

	UObject* Create(FIRApplyContext& Ctx) override;
	bool Update(UObject* Asset, FIRApplyContext& Ctx) override;
	bool DeleteAsset(FIRApplyContext& Ctx) override;
	TSharedPtr<FJsonObject> Read(UObject* Asset, FIRApplyContext& Ctx) override;
	bool CompileAsset(UObject* Asset, FIRApplyContext& Ctx) override;

private:
	UObject* CreateMaterial(FIRApplyContext& Ctx);
	UObject* CreateMaterialFunction(FIRApplyContext& Ctx);
	UObject* CreateMaterialInstance(FIRApplyContext& Ctx);

	void ApplyMaterialSettings(UMaterial* Material, FIRApplyContext& Ctx);
	void ApplyParameters(UObject* Material, FIRApplyContext& Ctx, TMap<FString, UMaterialExpression*>& NodesOut);
	void ApplyGraph(UObject* Material, FIRApplyContext& Ctx, TMap<FString, UMaterialExpression*>& NodesOut);
	void ApplyOutputs(UObject* Material, FIRApplyContext& Ctx, const TMap<FString, UMaterialExpression*>& Nodes);
};

UObject* FMaterialIRHandler::Create(FIRApplyContext& Ctx)
{
	if (Ctx.AssetType == TEXT("material_function"))
	{
		return CreateMaterialFunction(Ctx);
	}
	if (Ctx.AssetType == TEXT("material_instance_constant"))
	{
		return CreateMaterialInstance(Ctx);
	}
	return CreateMaterial(Ctx);
}

UObject* FMaterialIRHandler::CreateMaterial(FIRApplyContext& Ctx)
{
	UMaterialFactoryNew* Factory = NewObject<UMaterialFactoryNew>();
	UObject* Asset = ForgeMindIR::CreateAssetWithFactory(Factory, UMaterial::StaticClass(), Ctx.Path);
	if (Asset == nullptr)
	{
		Ctx.Fail(TEXT("CREATE_FAILED"), FString::Printf(TEXT("failed to create material at %s"), *Ctx.Path));
		return nullptr;
	}

	UMaterial* Material = Cast<UMaterial>(Asset);

	ApplyMaterialSettings(Material, Ctx);

	TMap<FString, UMaterialExpression*> Nodes;
	ApplyParameters(Material, Ctx, Nodes);
	ApplyGraph(Material, Ctx, Nodes);
	ApplyOutputs(Material, Ctx, Nodes);
	// Tidy the freshly built graph through the same operation the material step commands
	// run, so the two channels also leave the asset in the same visual state.
	ForgeMindMaterial::ArrangeLayout(Material);

	Ctx.Extra->SetNumberField(TEXT("expressionCount"), Nodes.Num());
	return Material;
}

UObject* FMaterialIRHandler::CreateMaterialFunction(FIRApplyContext& Ctx)
{
	UClass* Class = ForgeMindIR::ResolveClass(TEXT("MaterialFunction"));
	if (Class == nullptr)
	{
		Ctx.Fail(TEXT("UNSUPPORTED_ASSET_TYPE"), TEXT("UMaterialFunction is not available in this editor"));
		return nullptr;
	}
	UObject* Asset = ForgeMindIR::CreateAssetInstance(Class, Ctx.Path, true);
	if (Asset == nullptr)
	{
		Ctx.Fail(TEXT("CREATE_FAILED"), FString::Printf(TEXT("failed to create material function at %s"), *Ctx.Path));
		return nullptr;
	}

	TMap<FString, UMaterialExpression*> Nodes;
	ApplyGraph(Asset, Ctx, Nodes);
	ForgeMindMaterial::ArrangeLayout(Asset);
	Ctx.Extra->SetNumberField(TEXT("expressionCount"), Nodes.Num());
	return Asset;
}

UObject* FMaterialIRHandler::CreateMaterialInstance(FIRApplyContext& Ctx)
{
	FString ParentPath;
	if (Ctx.Definition.IsValid())
	{
		ParentPath = ForgeMindIR::FirstString(Ctx.Definition, { TEXT("parent"), TEXT("parentMaterial") });
	}
	if (ParentPath.IsEmpty())
	{
		Ctx.Fail(TEXT("MAT_PARENT_NOT_FOUND"), TEXT("material_instance_constant requires a 'parent' path"));
		return nullptr;
	}
	UObject* Parent = ForgeMindIR::ResolveObjectPath(ParentPath);
	if (Parent == nullptr)
	{
		Ctx.Fail(TEXT("MAT_PARENT_NOT_FOUND"), FString::Printf(TEXT("parent material '%s' not found"), *ParentPath));
		return nullptr;
	}

	UClass* Class = ForgeMindIR::ResolveClass(TEXT("MaterialInstanceConstant"));
	if (Class == nullptr)
	{
		Ctx.Fail(TEXT("UNSUPPORTED_ASSET_TYPE"), TEXT("UMaterialInstanceConstant is not available in this editor"));
		return nullptr;
	}
	UObject* Asset = ForgeMindIR::CreateAssetInstance(Class, Ctx.Path, true);
	UMaterialInstanceConstant* Instance = Cast<UMaterialInstanceConstant>(Asset);
	if (Instance == nullptr)
	{
		Ctx.Fail(TEXT("CREATE_FAILED"), FString::Printf(TEXT("failed to create material instance at %s"), *Ctx.Path));
		return nullptr;
	}

	// Bind the parent material through the UPROPERTY (mirrors the factory's InitialParent).
	FString Error;
	ForgeMindIR::SetProperty(Instance, TEXT("Parent"), MakeShared<FJsonValueString>(ParentPath), Error);

	const TArray<TSharedPtr<FJsonValue>>* Overrides = nullptr;
	int32 OverrideCount = 0;
	if (Ctx.Definition.IsValid() && Ctx.Definition->TryGetArrayField(TEXT("parameterOverrides"), Overrides))
	{
		for (const TSharedPtr<FJsonValue>& Value : *Overrides)
		{
			const TSharedPtr<FJsonObject> Override = Value->AsObject();
			if (!Override.IsValid())
			{
				continue;
			}
			FString Name, ParamType;
			Override->TryGetStringField(TEXT("name"), Name);
			Override->TryGetStringField(TEXT("type"), ParamType);
			if (ParamType.IsEmpty())
			{
				Override->TryGetStringField(TEXT("paramType"), ParamType);
			}
			if (Name.IsEmpty())
			{
				continue;
			}
			const FName ParamName(*Name);
			if (ParamType == TEXT("Scalar"))
			{
				double Number = 0.0;
				if (Override->TryGetNumberField(TEXT("value"), Number) || Override->TryGetNumberField(TEXT("defaultValue"), Number))
				{
					Instance->SetScalarParameterValueEditorOnly(ParamName, static_cast<float>(Number));
					++OverrideCount;
				}
			}
			else if (ParamType == TEXT("Vector") || ParamType == TEXT("LinearColor") || ParamType == TEXT("Vector4"))
			{
				FLinearColor Color = FLinearColor::White;
				const TSharedPtr<FJsonValue>* Value4 = Override->Values.Find(TEXT("value"));
				if (Value4 != nullptr && (*Value4)->Type == EJson::Array)
				{
					const TArray<TSharedPtr<FJsonValue>>& Items = (*Value4)->AsArray();
					if (Items.Num() >= 4)
					{
						Color = FLinearColor(Items[0]->AsNumber(), Items[1]->AsNumber(), Items[2]->AsNumber(), Items[3]->AsNumber());
					}
					else if (Items.Num() == 3)
					{
						Color = FLinearColor(Items[0]->AsNumber(), Items[1]->AsNumber(), Items[2]->AsNumber(), 1.0f);
					}
				}
				Instance->SetVectorParameterValueEditorOnly(ParamName, Color);
				++OverrideCount;
			}
			else if (ParamType == TEXT("Texture2D") || ParamType == TEXT("Texture") || ParamType == TEXT("TextureCube"))
			{
				FString TexturePath = ForgeMindIR::FirstString(Override, { TEXT("asset"), TEXT("value"), TEXT("texture") });
				UTexture* Texture = Cast<UTexture>(ForgeMindIR::ResolveObjectPath(TexturePath, UTexture::StaticClass()));
				if (Texture != nullptr)
				{
					Instance->SetTextureParameterValueEditorOnly(ParamName, Texture);
					++OverrideCount;
				}
				else
				{
					Ctx.Warn(TEXT("MAT_PARAMETER_NOT_FOUND"), FString::Printf(TEXT("texture '%s' not found"), *TexturePath));
				}
			}
		}
	}

	// instanceProperties → BasePropertyOverrides via reflection.
	const TArray<TSharedPtr<FJsonValue>>* InstanceProps = nullptr;
	if (Ctx.Definition.IsValid() && Ctx.Definition->TryGetArrayField(TEXT("instanceProperties"), InstanceProps))
	{
		for (const TSharedPtr<FJsonValue>& Value : *InstanceProps)
		{
			const TSharedPtr<FJsonObject> Entry = Value->AsObject();
			if (!Entry.IsValid())
			{
				continue;
			}
			FString PropertyName;
			Entry->TryGetStringField(TEXT("property"), PropertyName);
			FString SetError;
			if (!ForgeMindIR::SetProperty(Instance, PropertyName, Entry->TryGetField(TEXT("value")), SetError))
			{
				Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), SetError);
			}
		}
	}

	Ctx.Extra->SetNumberField(TEXT("overrideCount"), OverrideCount);
	return Instance;
}

void FMaterialIRHandler::ApplyMaterialSettings(UMaterial* Material, FIRApplyContext& Ctx)
{
	if (Material == nullptr || !Ctx.Definition.IsValid())
	{
		return;
	}
	const TSharedPtr<FJsonObject>* Settings = nullptr;
	if (!Ctx.Definition->TryGetObjectField(TEXT("materialSettings"), Settings) || !Settings->IsValid())
	{
		return;
	}
	for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : (*Settings)->Values)
	{
		FString Error;
		if (!ForgeMindMaterial::ApplyMaterialSetting(Material, Pair.Key, Pair.Value, Error))
		{
			Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), Error);
		}
	}
}

void FMaterialIRHandler::ApplyParameters(UObject* Material, FIRApplyContext& Ctx, TMap<FString, UMaterialExpression*>& NodesOut)
{
	if (!Ctx.Definition.IsValid())
	{
		return;
	}
	const TArray<TSharedPtr<FJsonValue>>* Parameters = nullptr;
	if (!Ctx.Definition->TryGetArrayField(TEXT("parameters"), Parameters))
	{
		return;
	}

	for (const TSharedPtr<FJsonValue>& Value : *Parameters)
	{
		const TSharedPtr<FJsonObject> Parameter = Value->AsObject();
		if (!Parameter.IsValid())
		{
			continue;
		}
		FString Name, Type;
		Parameter->TryGetStringField(TEXT("name"), Name);
		Parameter->TryGetStringField(TEXT("type"), Type);
		if (Name.IsEmpty() || Type.IsEmpty())
		{
			continue;
		}

		FString ClassName = TEXT("MaterialExpressionScalarParameter");
		bool bTextureParameter = false;
		if (Type == TEXT("Vector") || Type == TEXT("LinearColor") || Type == TEXT("Vector4"))
		{
			ClassName = TEXT("MaterialExpressionVectorParameter");
		}
		else if (Type == TEXT("Texture2D") || Type == TEXT("Texture") || Type == TEXT("TextureCube"))
		{
			ClassName = TEXT("MaterialExpressionTextureSampleParameter2D");
			bTextureParameter = true;
		}
		else if (Type == TEXT("Bool"))
		{
			ClassName = TEXT("MaterialExpressionStaticBoolParameter");
		}

		UClass* Class = ForgeMindIR::ResolveClass(ClassName);
		if (Class == nullptr)
		{
			Ctx.Warn(TEXT("MAT_INVALID_EXPRESSION"), FString::Printf(TEXT("parameter class '%s' not found"), *ClassName));
			continue;
		}

		// CreateExpression owns the shared parameter plumbing: name, GUID refresh and
		// registration in the expression list - identical to add_material_expression.
		UMaterialExpression* Expression = ForgeMindMaterial::CreateExpression(Material, Class, Name);
		if (Expression == nullptr)
		{
			Ctx.Warn(TEXT("MAT_INVALID_EXPRESSION"), FString::Printf(TEXT("failed to create parameter '%s'"), *Name));
			continue;
		}

		FString Error;
		FString Group;
		if (Parameter->TryGetStringField(TEXT("group"), Group))
		{
			ForgeMindMaterial::SetValue(Expression, TEXT("Group"), MakeShared<FJsonValueString>(Group), Error);
		}
		if (Parameter->HasField(TEXT("defaultValue")))
		{
			// A texture parameter's default is its texture; the other families keep the
			// DefaultValue property the IR names.
			const TCHAR* ValueProperty = bTextureParameter ? TEXT("Texture") : TEXT("DefaultValue");
			if (!ForgeMindMaterial::SetValue(Expression, ValueProperty, Parameter->TryGetField(TEXT("defaultValue")), Error))
			{
				Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), Error);
			}
		}
		double Min = 0.0, Max = 0.0;
		if (Parameter->TryGetNumberField(TEXT("min"), Min))
		{
			ForgeMindMaterial::SetValue(Expression, TEXT("SliderMin"), MakeShared<FJsonValueNumber>(Min), Error);
		}
		if (Parameter->TryGetNumberField(TEXT("max"), Max))
		{
			ForgeMindMaterial::SetValue(Expression, TEXT("SliderMax"), MakeShared<FJsonValueNumber>(Max), Error);
		}
		FString SamplerType;
		if (Parameter->TryGetStringField(TEXT("samplerType"), SamplerType))
		{
			ForgeMindMaterial::SetValue(Expression, TEXT("SamplerType"), MakeShared<FJsonValueString>(SamplerType), Error);
		}

		NodesOut.Add(TEXT("param_") + Name, Expression);
	}
}

void FMaterialIRHandler::ApplyGraph(UObject* Material, FIRApplyContext& Ctx, TMap<FString, UMaterialExpression*>& NodesOut)
{
	if (!Ctx.Definition.IsValid())
	{
		return;
	}
	const TArray<TSharedPtr<FJsonValue>>* GraphNodes = nullptr;
	if (!Ctx.Definition->TryGetArrayField(TEXT("nodes"), GraphNodes))
	{
		return;
	}

	// Pass 1 — instantiate every expression.
	struct FPendingInput
	{
		FString Id;
		FString InputName;
		TSharedPtr<FJsonObject> Input;
	};
	TArray<FPendingInput> PendingInputs;

	for (const TSharedPtr<FJsonValue>& Value : *GraphNodes)
	{
		const TSharedPtr<FJsonObject> Node = Value->AsObject();
		if (!Node.IsValid())
		{
			continue;
		}
		FString Id, Type;
		Node->TryGetStringField(TEXT("id"), Id);
		Node->TryGetStringField(TEXT("type"), Type);
		if (Id.IsEmpty())
		{
			continue;
		}
		// A material function's interface is expressed with the same two node types a
		// material function graph shows in the editor: FunctionInput / FunctionOutput.
		// They are what a caller sees as the call node's pins, so they are built here
		// like any other node - skipping them left a function with a body and no
		// signature, and a call node with nothing to wire.
		const bool bFunctionInput = IsFunctionIoType(Type, TEXT("FunctionInput"));
		const bool bFunctionOutput = IsFunctionIoType(Type, TEXT("FunctionOutput"));

		UClass* Class = ForgeMindIR::ResolveClass(ForgeMindMaterial::ExpressionClassName(Type));
		if (Class == nullptr)
		{
			Ctx.Warn(TEXT("MAT_INVALID_EXPRESSION"), FString::Printf(TEXT("expression class for '%s' not found"), *Type));
			continue;
		}

		// CreateExpression is the single construction point shared with the material
		// step commands, so an IR node and the same node added step by step are the
		// same object: same outer, same material link, same expression list slot.
		UMaterialExpression* Expression = ForgeMindMaterial::CreateExpression(Material, Class, FString());
		if (Expression == nullptr)
		{
			Ctx.Warn(TEXT("MAT_INVALID_EXPRESSION"), FString::Printf(TEXT("failed to create node '%s' (%s)"), *Id, *Type));
			continue;
		}

		// A function call's asset reference is structural to the IR schema but is exactly
		// what the node exists for, so it is written explicitly - the way the step command
		// writes its texture - and a reference that does not resolve is reported. A call
		// node left without a function has no pins and compiles to nothing, while every
		// step around it still answers success.
		FString FunctionAsset;
		if (Node->TryGetStringField(TEXT("functionAsset"), FunctionAsset) && !FunctionAsset.IsEmpty())
		{
			FString Error;
			if (!ForgeMindMaterial::SetValue(Expression, TEXT("functionAsset"), MakeShared<FJsonValueString>(FunctionAsset), Error))
			{
				Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), FString::Printf(TEXT("node '%s': %s"), *Id, *Error));
			}
		}

		if (bFunctionInput)
		{
			ApplyFunctionInputInterface(Expression, Ctx, Id);
		}
		else if (bFunctionOutput)
		{
			ApplyFunctionOutputInterface(Expression, Ctx, Id);
		}

		// Node level scalar properties (constant, texture, ...).
		for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Node->Values)
		{
			if (IsStructuralNodeKey(Pair.Key))
			{
				continue;
			}
			FString Error;
			if (!ForgeMindMaterial::SetValue(Expression, Pair.Key, Pair.Value, Error))
			{
				Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), FString::Printf(TEXT("node '%s': %s"), *Id, *Error));
			}
		}

		const TArray<TSharedPtr<FJsonValue>>* Inputs = nullptr;
		if (Node->TryGetArrayField(TEXT("inputs"), Inputs))
		{
			for (const TSharedPtr<FJsonValue>& InputValue : *Inputs)
			{
				const TSharedPtr<FJsonObject> Input = InputValue->AsObject();
				if (!Input.IsValid())
				{
					continue;
				}
				FString InputName;
				Input->TryGetStringField(TEXT("name"), InputName);
				PendingInputs.Add(FPendingInput{ Id, InputName, Input });
			}
		}

		NodesOut.Add(Id, Expression);
	}

	// Pass 2 — wire inputs (forward references now resolve).
	for (const FPendingInput& Pending : PendingInputs)
	{
		UMaterialExpression* Target = NodesOut.FindRef(Pending.Id);
		if (Target == nullptr)
		{
			continue;
		}

		FString SourceId;
		const bool bHasSource = Pending.Input->TryGetStringField(TEXT("expression"), SourceId) && !SourceId.IsEmpty();

		if (bHasSource)
		{
			if (UMaterialExpression* Source = NodesOut.FindRef(SourceId))
			{
				if (ForgeMindMaterial::GetExecutionOutput(Target, Pending.InputName) != nullptr)
				{
					// Reuse existing property initializer: LoopBody = Body is an output property pointing to the successor node.
					FString Error;
					if (!ForgeMindMaterial::ConnectExecutionOutput(Target, Pending.InputName, Source, Error))
					{
						Ctx.Warn(TEXT("MAT_PIN_NOT_AVAILABLE"), Error);
					}
					continue;
				}
				if (FExpressionInput* Input = ForgeMindMaterial::GetExpressionInput(Target, Pending.InputName))
				{
					// Two ways to name the source side, kept apart on purpose: the numeric
					// outputIndex is what the swizzle tables emit (a channel request, 1..4 =
					// R/G/B/A), while outputName carries one of the outputs the source itself
					// declares (DecalDerivative's DDY at index 1). A name that resolves is a real
					// pin and connects directly; a name the source does not declare is reported
					// rather than quietly reinterpreted as index 0.
					int32 OutputIndex = 0;
					FString OutputName;
					bool bNamedOutput = false;
					if (Pending.Input->TryGetStringField(TEXT("outputName"), OutputName) && !OutputName.IsEmpty())
					{
						if (!ForgeMindMaterial::TryResolveNamedOutputIndex(Source, OutputName, OutputIndex))
						{
							Ctx.Warn(TEXT("MAT_PIN_NOT_AVAILABLE"),
								FString::Printf(TEXT("input '%s' on node '%s': '%s' is not an output the source declares"), *Pending.InputName, *Pending.Id, *OutputName));
							continue;
						}
						bNamedOutput = true;
					}
					else
					{
						Pending.Input->TryGetNumberField(TEXT("outputIndex"), OutputIndex);
					}
					FString Error;
					if (!ForgeMindMaterial::ConnectOutputInput(Material, Input, Source, OutputIndex, Error, bNamedOutput))
					{
						Ctx.Warn(TEXT("MAT_PIN_NOT_AVAILABLE"),
							FString::Printf(TEXT("input '%s' on node '%s': %s"), *Pending.InputName, *Pending.Id, *Error));
					}
				}
				else
				{
					Ctx.Warn(TEXT("MAT_PIN_NOT_AVAILABLE"),
						FString::Printf(TEXT("input '%s' on node '%s' is not an expression input"), *Pending.InputName, *Pending.Id));
				}
			}
			else
			{
				Ctx.Warn(TEXT("MAT_CIRCULAR_REFERENCE"),
					FString::Printf(TEXT("node '%s' references unknown expression '%s'"), *Pending.Id, *SourceId));
			}
			continue;
		}

		if (Pending.Input->HasField(TEXT("value")))
		{
			const TSharedPtr<FJsonValue> ConstantValue = Pending.Input->TryGetField(TEXT("value"));
			if (FExpressionInput* Input = ForgeMindMaterial::GetExpressionInput(Target, Pending.InputName))
			{
				// A literal on an expression input becomes the constant node the editor
				// would show - scalar, 2/3/4-component colour - which is exactly what
				// connect_material_pins does with its "constant" field.
				FString Error;
				if (!ForgeMindMaterial::ConnectConstant(Input, Material, ConstantValue, Error))
				{
					Ctx.Warn(TEXT("MAT_PIN_NOT_AVAILABLE"),
						FString::Printf(TEXT("input '%s' on node '%s': %s"), *Pending.InputName, *Pending.Id, *Error));
				}
			}
			else
			{
				// Not an expression input: a plain property carrying the same name
				// (SamplerType, ConstB, ...) is what the value addresses.
				FString Error;
				if (!ForgeMindMaterial::SetValue(Target, Pending.InputName, ConstantValue, Error))
				{
					Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), Error);
				}
			}
			continue;
		}

		if (Pending.Input->HasField(TEXT("asset")))
		{
			FString Error;
			if (!ForgeMindMaterial::SetValue(Target, Pending.InputName, Pending.Input->TryGetField(TEXT("asset")), Error))
			{
				Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), Error);
			}
		}
	}

	// Every node now carries everything the IR said about it, so run the same post-edit
	// fixups the material editor performs after a paste. A collection parameter derives its
	// ParameterId from ParameterName only inside PostEditChangeProperty
	// (MaterialExpressions.cpp:15337-15351) and the compiler looks the collection entry up by
	// that Id - without this pass the IR channel would build a node that never resolves its
	// parameter, while the step-command channel (which post-edits per step) worked.
	for (const TPair<FString, UMaterialExpression*>& NodePair : NodesOut)
	{
		if (NodePair.Value != nullptr)
		{
			NodePair.Value->PostEditChange();
		}
	}
}

void FMaterialIRHandler::ApplyOutputs(UObject* Material, FIRApplyContext& Ctx, const TMap<FString, UMaterialExpression*>& Nodes)
{
	if (!Ctx.Definition.IsValid())
	{
		return;
	}
	const TSharedPtr<FJsonObject>* Outputs = nullptr;
	if (!Ctx.Definition->TryGetObjectField(TEXT("materialOutputs"), Outputs) || !Outputs->IsValid())
	{
		return;
	}

	for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : (*Outputs)->Values)
	{
		const TSharedPtr<FJsonObject> Output = Pair.Value->AsObject();
		if (!Output.IsValid())
		{
			continue;
		}
		// Material outputs are not name-addressable properties on every engine
		// version; GetMaterialOutputInput is the single place that maps a pin name
		// (BaseColor / EmissiveColor / ...) onto the input that holds it.
		FExpressionInput* Input = ForgeMindMaterial::GetMaterialOutputInput(Material, Pair.Key);
		if (Input == nullptr)
		{
			Ctx.Warn(TEXT("MAT_PIN_NOT_AVAILABLE"), FString::Printf(TEXT("output '%s' is not available"), *Pair.Key));
			continue;
		}
		// The pin existing and the pin being read are two different questions: a post-process
		// material accepts an Opacity wire and then never compiles it (UMaterial::IsPropertyActive,
		// Material.cpp:5286-5289), which is how a whole question of a task doc passed while
		// asserting nothing. Warn rather than stay silent, so the DSL side fails like the step
		// command side now does.
		if (!ForgeMindMaterial::IsMaterialOutputActive(Material, Pair.Key))
		{
			Ctx.Warn(TEXT("MAT_PIN_NOT_AVAILABLE"), FString::Printf(
				TEXT("output '%s' is ignored by the engine for this material's settings"), *Pair.Key));
			continue;
		}

		FString SourceId;
		if (Output->TryGetStringField(TEXT("sourceNodeId"), SourceId) && !SourceId.IsEmpty())
		{
			if (UMaterialExpression* Source = Nodes.FindRef(SourceId))
			{
				// Same split as the node inputs above: sourceOutputName names one of the
				// source's declared outputs, sourceOutputIndex stays the swizzle/channel form.
				int32 OutputIndex = 0;
				FString SourceOutputName;
				bool bNamedOutput = false;
				if (Output->TryGetStringField(TEXT("sourceOutputName"), SourceOutputName) && !SourceOutputName.IsEmpty())
				{
					if (!ForgeMindMaterial::TryResolveNamedOutputIndex(Source, SourceOutputName, OutputIndex))
					{
						Ctx.Warn(TEXT("MAT_UNCONNECTED_OUTPUT"),
							FString::Printf(TEXT("output '%s': '%s' is not an output the source declares"), *Pair.Key, *SourceOutputName));
						continue;
					}
					bNamedOutput = true;
				}
				else
				{
					Output->TryGetNumberField(TEXT("sourceOutputIndex"), OutputIndex);
				}
				FString Error;
				if (!ForgeMindMaterial::ConnectOutputInput(Material, Input, Source, OutputIndex, Error, bNamedOutput))
				{
					Ctx.Warn(TEXT("MAT_UNCONNECTED_OUTPUT"), FString::Printf(TEXT("output '%s': %s"), *Pair.Key, *Error));
				}
				continue;
			}
			Ctx.Warn(TEXT("MAT_UNCONNECTED_OUTPUT"), FString::Printf(TEXT("output '%s' references unknown node '%s'"), *Pair.Key, *SourceId));
			continue;
		}

		if (Output->HasField(TEXT("value")))
		{
			FString Error;
			if (!ForgeMindMaterial::ConnectConstant(Input, Material, Output->TryGetField(TEXT("value")), Error))
			{
				Ctx.Warn(TEXT("MAT_UNCONNECTED_OUTPUT"), FString::Printf(TEXT("output '%s': %s"), *Pair.Key, *Error));
			}
		}
	}
}

bool FMaterialIRHandler::Update(UObject* Asset, FIRApplyContext& Ctx)
{
	UMaterial* Material = Cast<UMaterial>(Asset);
	UObject* EditorData = ForgeMindMaterial::GetEditorOnlyData(Asset);

	for (const TSharedPtr<FJsonValue>& PatchEntry : Ctx.Patches)
	{
		const TSharedPtr<FJsonObject> Patch = PatchEntry->AsObject();
		if (!Patch.IsValid())
		{
			continue;
		}
		FString Type, Name;
		Patch->TryGetStringField(TEXT("type"), Type);
		Patch->TryGetStringField(TEXT("name"), Name);
		const bool bDelete = ForgeMindIR::IsDeletePatch(Patch);

		FString ActualOp = TEXT("failed");
		bool bOk = false;

		if (Type == TEXT("materialSetting"))
		{
			FString Key = ForgeMindIR::FirstString(Patch, { TEXT("settingKey"), TEXT("name") });
			ActualOp = TEXT("replace");
			FString Error;
			bOk = Material != nullptr && ForgeMindMaterial::SetValue(Material, Key, Patch->TryGetField(TEXT("value")), Error);
			if (!bOk && EditorData != nullptr)
			{
				bOk = ForgeMindMaterial::SetValue(EditorData, Key, Patch->TryGetField(TEXT("value")), Error);
			}
			if (!bOk)
			{
				Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), Error);
			}
			Ctx.AddPatchResult(ActualOp, Type, Key, bOk);
			continue;
		}

		if (Type == TEXT("materialOutput"))
		{
			FString OutputName = ForgeMindIR::FirstString(Patch, { TEXT("outputName"), TEXT("name") });
			FExpressionInput* Input = ForgeMindMaterial::GetMaterialOutputInput(Asset, OutputName);
			ActualOp = FGenericAssetIRHandler::InferOp(Input != nullptr, bDelete);
			if (!bDelete && Input != nullptr && !ForgeMindMaterial::IsMaterialOutputActive(Asset, OutputName))
			{
				Ctx.Warn(TEXT("MAT_PIN_NOT_AVAILABLE"), FString::Printf(
					TEXT("output '%s' is ignored by the engine for this material's settings"), *OutputName));
			}
			else if (bDelete)
			{
				Ctx.Warn(TEXT("UNSUPPORTED_PATCH_TYPE"),
					FString::Printf(TEXT("clearing output '%s' is not supported - set it to a constant instead"), *OutputName));
			}
			else if (Input != nullptr)
			{
				FString SourceId;
				if (Patch->TryGetStringField(TEXT("sourceNodeId"), SourceId) && !SourceId.IsEmpty())
				{
					// The patch names an existing expression of the graph; resolve it by
					// object name through the same lookup the step commands use.
					if (UMaterialExpression* Source = ForgeMindMaterial::FindExpression(Asset, SourceId))
					{
						// Named output (sourceOutputName) first, numeric (sourceOutputIndex) otherwise,
						// exactly as the create/update paths above do.
						int32 OutputIndex = 0;
						FString Error;
						FString SourceOutputName;
						bool bNamedOutput = false;
						if (Patch->TryGetStringField(TEXT("sourceOutputName"), SourceOutputName) && !SourceOutputName.IsEmpty())
						{
							if (!ForgeMindMaterial::TryResolveNamedOutputIndex(Source, SourceOutputName, OutputIndex))
							{
								Error = FString::Printf(TEXT("'%s' is not an output the source declares"), *SourceOutputName);
							}
							else
							{
								bNamedOutput = true;
							}
						}
						else
						{
							Patch->TryGetNumberField(TEXT("sourceOutputIndex"), OutputIndex);
						}
						bOk = Error.IsEmpty()
							? ForgeMindMaterial::ConnectOutputInput(Asset, Input, Source, OutputIndex, Error, bNamedOutput)
							: false;
						if (!bOk)
						{
							Ctx.Warn(TEXT("MAT_UNCONNECTED_OUTPUT"),
								FString::Printf(TEXT("output '%s': %s"), *OutputName, *Error));
						}
					}
					else
					{
						Ctx.Warn(TEXT("MAT_UNCONNECTED_OUTPUT"),
							FString::Printf(TEXT("output '%s' references unknown node '%s'"), *OutputName, *SourceId));
					}
				}
				else if (Patch->HasField(TEXT("value")))
				{
					FString Error;
					bOk = ForgeMindMaterial::ConnectConstant(Input, Asset, Patch->TryGetField(TEXT("value")), Error);
					if (!bOk)
					{
						Ctx.Warn(TEXT("MAT_UNCONNECTED_OUTPUT"), FString::Printf(TEXT("output '%s': %s"), *OutputName, *Error));
					}
				}
				else
				{
					Ctx.Warn(TEXT("MAT_UNCONNECTED_OUTPUT"),
						FString::Printf(TEXT("output '%s' needs a sourceNodeId or value"), *OutputName));
				}
			}
			Ctx.AddPatchResult(ActualOp, Type, OutputName, bOk);
			continue;
		}

		if (Type == TEXT("parameterOverride") && Material == nullptr)
		{
			// material_instance_constant scalar/vector/texture overrides.
			UMaterialInstanceConstant* Instance = Cast<UMaterialInstanceConstant>(Asset);
			FString ParamType;
			Patch->TryGetStringField(TEXT("paramType"), ParamType);
			if (ParamType.IsEmpty())
			{
				Patch->TryGetStringField(TEXT("type"), ParamType);
			}
			ActualOp = TEXT("replace");
			if (Instance != nullptr)
			{
				const FName ParamName(*Name);
				if (bDelete)
				{
					Ctx.Warn(TEXT("UNSUPPORTED_PATCH_TYPE"),
						FString::Printf(TEXT("clearing parameter override '%s' is not supported - set it to its default instead"), *Name));
					bOk = false;
				}
				else if (ParamType == TEXT("Scalar"))
				{
					double Number = 0.0;
					Patch->TryGetNumberField(TEXT("value"), Number);
					Instance->SetScalarParameterValueEditorOnly(ParamName, static_cast<float>(Number));
					bOk = true;
				}
				else if (ParamType == TEXT("Texture2D") || ParamType == TEXT("Texture"))
				{
					FString Path = ForgeMindIR::FirstString(Patch, { TEXT("value"), TEXT("asset") });
					UTexture* Texture = Cast<UTexture>(ForgeMindIR::ResolveObjectPath(Path, UTexture::StaticClass()));
					if (Texture != nullptr)
					{
						Instance->SetTextureParameterValueEditorOnly(ParamName, Texture);
						bOk = true;
					}
				}
				else
				{
					FString Error;
					bOk = ForgeMindIR::SetProperty(Instance, Name, Patch->TryGetField(TEXT("value")), Error);
				}
			}
			Ctx.AddPatchResult(ActualOp, Type, Name, bOk);
			continue;
		}

		if (Type == TEXT("instanceProperty"))
		{
			FString PropertyName = ForgeMindIR::FirstString(Patch, { TEXT("property"), TEXT("name") });
			ActualOp = TEXT("replace");
			FString Error;
			bOk = ForgeMindIR::SetProperty(Asset, PropertyName, Patch->TryGetField(TEXT("value")), Error);
			if (!bOk)
			{
				Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), Error);
			}
			Ctx.AddPatchResult(ActualOp, Type, PropertyName, bOk);
			continue;
		}

		Ctx.Warn(TEXT("UNSUPPORTED_PATCH_TYPE"),
			FString::Printf(TEXT("patch type '%s' is not handled by the material domain"), *Type));
	}
	// Patches move wires and add constants, so the graph gets the same tidy pass a
	// create or a step command gets.
	ForgeMindMaterial::ArrangeLayout(Asset);
	return true;
}

bool FMaterialIRHandler::DeleteAsset(FIRApplyContext& Ctx)
{
	FString Error;
	if (!ForgeMindIR::DeleteAssetObject(Ctx.Path, Error))
	{
		Ctx.Fail(TEXT("DELETE_FAILED"), Error);
		return false;
	}
	return true;
}

bool FMaterialIRHandler::CompileAsset(UObject* Asset, FIRApplyContext& Ctx)
{
	if (Asset == nullptr)
	{
		return false;
	}
	// PostEditChange drives the material recompile + shader map rebuild.
	Asset->PostEditChange();
	Asset->MarkPackageDirty();
	Ctx.CompileTimeMs = 0.0;

	if (UMaterial* Material = Cast<UMaterial>(Asset))
	{
		Ctx.Extra->SetStringField(TEXT("materialState"),
			Material->GetMaterialResource(GMaxRHIFeatureLevel) != nullptr ? TEXT("compiled") : TEXT("pending"));
	}
	return true;
}

TSharedPtr<FJsonObject> FMaterialIRHandler::Read(UObject* Asset, FIRApplyContext& Ctx)
{
	TSharedRef<FJsonObject> Definition = MakeShared<FJsonObject>();

	if (UMaterialInstanceConstant* Instance = Cast<UMaterialInstanceConstant>(Asset))
	{
		if (Instance->Parent != nullptr)
		{
			Definition->SetStringField(TEXT("parent"), Instance->Parent->GetPathName());
		}
		TArray<TSharedPtr<FJsonValue>> Overrides;
		for (const FScalarParameterValue& Value : Instance->ScalarParameterValues)
		{
			TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
			Entry->SetStringField(TEXT("name"), Value.ParameterInfo.Name.ToString());
			Entry->SetStringField(TEXT("type"), TEXT("Scalar"));
			Entry->SetNumberField(TEXT("value"), Value.ParameterValue);
			Overrides.Add(MakeShared<FJsonValueObject>(Entry));
		}
		for (const FVectorParameterValue& Value : Instance->VectorParameterValues)
		{
			TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
			Entry->SetStringField(TEXT("name"), Value.ParameterInfo.Name.ToString());
			Entry->SetStringField(TEXT("type"), TEXT("LinearColor"));
			TArray<TSharedPtr<FJsonValue>> Channels = {
				MakeShared<FJsonValueNumber>(Value.ParameterValue.R),
				MakeShared<FJsonValueNumber>(Value.ParameterValue.G),
				MakeShared<FJsonValueNumber>(Value.ParameterValue.B),
				MakeShared<FJsonValueNumber>(Value.ParameterValue.A),
			};
			Entry->SetArrayField(TEXT("value"), Channels);
			Overrides.Add(MakeShared<FJsonValueObject>(Entry));
		}
		for (const FTextureParameterValue& Value : Instance->TextureParameterValues)
		{
			TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
			Entry->SetStringField(TEXT("name"), Value.ParameterInfo.Name.ToString());
			Entry->SetStringField(TEXT("type"), TEXT("Texture2D"));
			Entry->SetStringField(TEXT("asset"), Value.ParameterValue != nullptr ? Value.ParameterValue->GetPathName() : FString());
			Overrides.Add(MakeShared<FJsonValueObject>(Entry));
		}
		Definition->SetArrayField(TEXT("parameterOverrides"), Overrides);
		return Definition;
	}

	UMaterial* Material = Cast<UMaterial>(Asset);
	if (Material != nullptr)
	{
		TSharedRef<FJsonObject> Settings = MakeShared<FJsonObject>();
		// "ShadingModel" is the switch, "ShadingModels" the field derived from it: the root node's
		// ShadingModel pin is only honored while ShadingModel == MSM_FromMaterialExpression
		// (UMaterial::IsShadingModelFromMaterialExpression, Material.cpp:5235; the field is rebuilt from
		// the graph on every recompile only in that case, Material.cpp:2112 -> 4027-4040). Reporting just
		// the field would hide exactly the case where a caller wired the pin and the engine ignored it.
		static const TCHAR* Names[] = {
			TEXT("MaterialDomain"), TEXT("BlendMode"), TEXT("ShadingModel"), TEXT("ShadingModels"),
			TEXT("TwoSided"), TEXT("OpacityMaskClipValue"), TEXT("bUseMaterialAttributes")
		};
		// Read through the material domain's own value reader, not the generic reflection
		// reader: these three keys are TEnumAsByte properties, and the generic reader
		// serializes a byte enum as its raw number (a number cannot be asserted against
		// the authored name and cannot be fed back as authored IR).
		for (const TCHAR* Name : Names)
		{
			if (TSharedPtr<FJsonValue> Value = ForgeMindMaterial::GetValue(Material, Name))
			{
				Settings->SetField(Name, Value);
			}
		}
		// Extend the read-back only for materials that actually use execution flow, preserving the existing baseline shape for ordinary materials.
		if (Material->bEnableExecWire)
		{
			Settings->SetBoolField(TEXT("bEnableExecWire"), true);
			Settings->SetBoolField(TEXT("compiledWithExecutionFlow"), Material->IsCompiledWithExecutionFlow());
		}
		Definition->SetObjectField(TEXT("materialSettings"), Settings);
	}

	TArray<UMaterialExpression*> Expressions;
	ForgeMindMaterial::GetExpressions(Asset, Expressions);
	if (Expressions.Num() == 0 && !ForgeMindMaterial::HasExpressionList(Asset))
	{
		Ctx.Warn(TEXT("MAT_UNCONNECTED_OUTPUT"), TEXT("the asset carries no expression list to serialize"));
	}

	const bool bWithWiring = Ctx.Level == TEXT("graph") || Ctx.Level == TEXT("full");

	// The object name is the identity both channels hand out (add_material_expression
	// returns it; an IR definition's ids stay internal to that definition), so a
	// caller comparing two independently built assets can re-key it to a stable one.
	TMap<UMaterialExpression*, FString> Ids;
	for (UMaterialExpression* Expression : Expressions)
	{
		Ids.Add(Expression, Expression->GetName());
	}

	TArray<TSharedPtr<FJsonValue>> Nodes;
	for (UMaterialExpression* Expression : Expressions)
	{
		if (Expression == nullptr)
		{
			continue;
		}
		TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
		Entry->SetStringField(TEXT("type"), ForgeMindMaterial::ExpressionTypeName(Expression));
		Entry->SetStringField(TEXT("id"), Expression->GetName());
		if (bWithWiring)
		{
			CollectExpressionProperties(Expression, Entry);
			TArray<TSharedPtr<FJsonValue>> Inputs;
			CollectExpressionInputs(Expression, Inputs, Ids);
			if (Inputs.Num() > 0)
			{
				Entry->SetArrayField(TEXT("inputs"), Inputs);
			}
		}
		Nodes.Add(MakeShared<FJsonValueObject>(Entry));
	}
	Definition->SetArrayField(TEXT("nodes"), Nodes);

	if (bWithWiring)
	{
		// Material outputs in engine property order, so two assets built through
		// different channels enumerate the same pins in the same sequence.
		TArray<TPair<FString, FExpressionInput*>> ConnectedOutputs;
		ForgeMindMaterial::GetConnectedMaterialOutputs(Asset, ConnectedOutputs);
		TSharedRef<FJsonObject> Outputs = MakeShared<FJsonObject>();
		for (const TPair<FString, FExpressionInput*>& Pair : ConnectedOutputs)
		{
			TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
			Entry->SetStringField(TEXT("sourceNodeId"), Ids.FindRef(Pair.Value->Expression));
			Entry->SetNumberField(TEXT("sourceOutputIndex"), Pair.Value->OutputIndex);
			Outputs->SetField(Pair.Key, MakeShared<FJsonValueObject>(Entry));
		}
		Definition->SetObjectField(TEXT("materialOutputs"), Outputs);
	}

	return Definition;
}

TSharedRef<IIRHandler> MakeMaterialIRHandler()
{
	return MakeShared<FMaterialIRHandler>();
}