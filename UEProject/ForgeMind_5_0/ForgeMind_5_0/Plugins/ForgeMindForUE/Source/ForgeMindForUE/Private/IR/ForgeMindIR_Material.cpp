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
		// Texture family: the referenced asset (null when the path did not resolve).
		if (const TSharedPtr<FJsonValue> Texture = ForgeMindMaterial::GetValue(Expression, TEXT("Texture")))
		{
			Entry->SetField(TEXT("texture"), Texture);
		}
	}

	/** Connected FExpressionInput members of one object, as IR input entries. */
	void CollectExpressionInputs(UObject* Owner, TArray<TSharedPtr<FJsonValue>>& OutInputs,
		const TMap<UMaterialExpression*, FString>& Ids)
	{
		for (TFieldIterator<FProperty> It(Owner->GetClass()); It; ++It)
		{
			const FStructProperty* StructProperty = CastField<FStructProperty>(*It);
			if (StructProperty == nullptr || StructProperty->Struct == nullptr
				|| !StructProperty->Struct->GetName().Equals(TEXT("ExpressionInput")))
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
		if (!ForgeMindMaterial::SetValue(Material, Pair.Key, Pair.Value, Error))
		{
			// Retry on the editor-only data object, which is where later engine
			// versions moved some of these settings to.
			if (UObject* EditorData = ForgeMindMaterial::GetEditorOnlyData(Material))
			{
				if (!ForgeMindMaterial::SetValue(EditorData, Pair.Key, Pair.Value, Error))
				{
					Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), Error);
				}
			}
			else
			{
				Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), Error);
			}
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
		if (Type == TEXT("FunctionInput") || Type == TEXT("FunctionOutput"))
		{
			// Material function signature nodes are owned by the function.
			continue;
		}

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

		// Node level scalar properties (constant, texture, function asset...).
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
				if (FExpressionInput* Input = ForgeMindMaterial::GetExpressionInput(Target, Pending.InputName))
				{
					int32 OutputIndex = 0;
					Pending.Input->TryGetNumberField(TEXT("outputIndex"), OutputIndex);
					FString Error;
					if (!ForgeMindMaterial::ConnectOutputInput(Material, Input, Source, OutputIndex, Error))
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

		FString SourceId;
		if (Output->TryGetStringField(TEXT("sourceNodeId"), SourceId) && !SourceId.IsEmpty())
		{
			if (UMaterialExpression* Source = Nodes.FindRef(SourceId))
			{
				int32 OutputIndex = 0;
				Output->TryGetNumberField(TEXT("sourceOutputIndex"), OutputIndex);
				FString Error;
				if (!ForgeMindMaterial::ConnectOutputInput(Material, Input, Source, OutputIndex, Error))
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
			if (bDelete)
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
						int32 OutputIndex = 0;
						Patch->TryGetNumberField(TEXT("sourceOutputIndex"), OutputIndex);
						FString Error;
						bOk = ForgeMindMaterial::ConnectOutputInput(Asset, Input, Source, OutputIndex, Error);
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
		static const TCHAR* Names[] = {
			TEXT("MaterialDomain"), TEXT("BlendMode"), TEXT("ShadingModels"), TEXT("TwoSided"),
			TEXT("OpacityMaskClipValue"), TEXT("bUseMaterialAttributes")
		};
		for (const TCHAR* Name : Names)
		{
			if (TSharedPtr<FJsonValue> Value = ForgeMindIR::GetProperty(Material, Name))
			{
				Settings->SetField(Name, Value);
			}
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