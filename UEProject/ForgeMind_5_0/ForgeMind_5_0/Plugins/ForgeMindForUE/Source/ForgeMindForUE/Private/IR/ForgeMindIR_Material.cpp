// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Material domain IR handler: material / material_function /
// material_instance_constant.
//
// Expression graphs are built in two passes (create → wire) because the
// IR allows forward references between nodes.

#include "ForgeMindIRHandler.h"
#include "ForgeMindIRGeneric.h"

#include "AssetToolsModule.h"
#include "Engine/Texture.h"
#include "Factories/MaterialFactoryNew.h"
#include "IAssetTools.h"
#include "Materials/Material.h"
#include "Materials/MaterialExpression.h"
#include "Materials/MaterialExpressionConstant.h"
#include "Materials/MaterialInstanceConstant.h"
#include "RHI.h"
#include "UObject/UnrealType.h"

namespace
{
	/** "TextureSample" → "MaterialExpressionTextureSample". */
	FString ExpressionClassName(const FString& Type)
	{
		if (Type.IsEmpty())
		{
			return Type;
		}
		if (Type.StartsWith(TEXT("MaterialExpression")))
		{
			return Type;
		}
		if (Type == TEXT("LinearInterpolate") || Type == TEXT("Lerp")) return TEXT("MaterialExpressionLinearInterpolate");
		if (Type == TEXT("Fractional") || Type == TEXT("Frac"))        return TEXT("MaterialExpressionFrac");
		return TEXT("MaterialExpression") + Type;
	}

	/** Resolves the editor-only data object without naming its type. */
	UObject* GetEditorOnlyData(UObject* Material)
	{
		if (Material == nullptr)
		{
			return nullptr;
		}
		FProperty* Property = FindFProperty<FProperty>(Material->GetClass(), TEXT("EditorOnlyData"));
		FObjectProperty* ObjectProperty = CastField<FObjectProperty>(Property);
		if (ObjectProperty == nullptr)
		{
			return nullptr;
		}
		return ObjectProperty->GetObjectPropertyValue(ObjectProperty->ContainerPtrToValuePtr<void>(Material));
	}

	FExpressionInput* GetExpressionInput(UObject* Owner, const FString& Name)
	{
		FProperty* Property = ForgeMindIR::FindProperty(Owner, Name);
		FStructProperty* StructProperty = CastField<FStructProperty>(Property);
		if (StructProperty == nullptr || StructProperty->Struct == nullptr
			|| StructProperty->Struct->GetName() != TEXT("ExpressionInput"))
		{
			return nullptr;
		}
		return reinterpret_cast<FExpressionInput*>(StructProperty->ContainerPtrToValuePtr<void>(Owner));
	}

	UMaterialExpressionConstant* CreateConstantExpression(UObject* EditorData, double Value)
	{
		UMaterialExpressionConstant* Const = NewObject<UMaterialExpressionConstant>(EditorData, UMaterialExpressionConstant::StaticClass(), NAME_None, RF_Transactional);
		Const->R = static_cast<float>(Value);
		Const->Material = Cast<UMaterial>(EditorData);

		// Register in the material's expression collection so the compiler can reach it.
		FProperty* ListProperty = ForgeMindIR::FindProperty(EditorData, TEXT("ExpressionCollection"));
		if (ListProperty == nullptr)
		{
			ListProperty = ForgeMindIR::FindProperty(EditorData, TEXT("Expressions"));
		}
		if (FArrayProperty* ArrayProperty = CastField<FArrayProperty>(ListProperty))
		{
			FObjectProperty* Inner = CastField<FObjectProperty>(ArrayProperty->Inner);
			if (Inner != nullptr)
			{
				FScriptArrayHelper Helper(ArrayProperty, ArrayProperty->ContainerPtrToValuePtr<void>(EditorData));
				Inner->SetObjectPropertyValue(Helper.GetRawPtr(Helper.AddValue()), Const);
			}
		}
		return Const;
	}

	bool SetInputConstant(FExpressionInput* Input, UObject* EditorData, double Value)
	{
		if (Input == nullptr || EditorData == nullptr)
		{
			return false;
		}
		if (UMaterialExpressionConstant* Const = CreateConstantExpression(EditorData, Value))
		{
			Input->Connect(0, Const);
			return true;
		}
		return false;
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
	void ApplyParameters(UObject* Material, UObject* EditorData, FIRApplyContext& Ctx, TMap<FString, UMaterialExpression*>& NodesOut);
	void ApplyGraph(UObject* Material, UObject* EditorData, FIRApplyContext& Ctx, TMap<FString, UMaterialExpression*>& NodesOut);
	void ApplyOutputs(UObject* Material, UObject* EditorData, FIRApplyContext& Ctx, const TMap<FString, UMaterialExpression*>& Nodes);
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
	UObject* EditorData = GetEditorOnlyData(Material);

	ApplyMaterialSettings(Material, Ctx);

	TMap<FString, UMaterialExpression*> Nodes;
	ApplyParameters(Material, EditorData, Ctx, Nodes);
	ApplyGraph(Material, EditorData, Ctx, Nodes);
	ApplyOutputs(Material, EditorData, Ctx, Nodes);

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
	ApplyGraph(Asset, GetEditorOnlyData(Asset), Ctx, Nodes);
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
		if (!ForgeMindIR::SetProperty(Material, Pair.Key, Pair.Value, Error))
		{
			// Retry on the editor-only data (some settings moved there).
			if (UObject* EditorData = GetEditorOnlyData(Material))
			{
				if (!ForgeMindIR::SetProperty(EditorData, Pair.Key, Pair.Value, Error))
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

void FMaterialIRHandler::ApplyParameters(UObject* Material, UObject* EditorData, FIRApplyContext& Ctx, TMap<FString, UMaterialExpression*>& NodesOut)
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
		if (Type == TEXT("Vector") || Type == TEXT("LinearColor") || Type == TEXT("Vector4"))
		{
			ClassName = TEXT("MaterialExpressionVectorParameter");
		}
		else if (Type == TEXT("Texture2D") || Type == TEXT("Texture") || Type == TEXT("TextureCube"))
		{
			ClassName = TEXT("MaterialExpressionTextureSampleParameter2D");
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

		UMaterialExpression* Expression = NewObject<UMaterialExpression>(Material, Class, NAME_None, RF_Transactional);
		if (Expression == nullptr)
		{
			continue;
		}
		if (EditorData != nullptr)
		{
			Expression->Material = Cast<UMaterial>(Material);
			Expression->Function = nullptr;
		}
		Expression->UpdateParameterGuid(true, true);

		FString Error;
		ForgeMindIR::SetProperty(Expression, TEXT("ParameterName"), MakeShared<FJsonValueString>(Name), Error);
		FString Group;
		if (Parameter->TryGetStringField(TEXT("group"), Group))
		{
			ForgeMindIR::SetProperty(Expression, TEXT("Group"), MakeShared<FJsonValueString>(Group), Error);
		}
		if (Parameter->HasField(TEXT("defaultValue")))
		{
			ForgeMindIR::SetProperty(Expression, TEXT("DefaultValue"), Parameter->TryGetField(TEXT("defaultValue")), Error);
		}
		double Min = 0.0, Max = 0.0;
		if (Parameter->TryGetNumberField(TEXT("min"), Min))
		{
			ForgeMindIR::SetProperty(Expression, TEXT("SliderMin"), MakeShared<FJsonValueNumber>(Min), Error);
		}
		if (Parameter->TryGetNumberField(TEXT("max"), Max))
		{
			ForgeMindIR::SetProperty(Expression, TEXT("SliderMax"), MakeShared<FJsonValueNumber>(Max), Error);
		}
		FString SamplerType;
		if (Parameter->TryGetStringField(TEXT("samplerType"), SamplerType))
		{
			ForgeMindIR::SetProperty(Expression, TEXT("SamplerType"), MakeShared<FJsonValueString>(SamplerType), Error);
		}

		// Register the parameter in the material's expression list.
		if (EditorData != nullptr)
		{
			FProperty* ListProperty = ForgeMindIR::FindProperty(EditorData, TEXT("ExpressionCollection"));
			if (ListProperty == nullptr)
			{
				ListProperty = ForgeMindIR::FindProperty(EditorData, TEXT("Expressions"));
			}
			if (FArrayProperty* ArrayProperty = CastField<FArrayProperty>(ListProperty))
			{
				FObjectProperty* Inner = CastField<FObjectProperty>(ArrayProperty->Inner);
				if (Inner != nullptr)
				{
					FScriptArrayHelper Helper(ArrayProperty, ArrayProperty->ContainerPtrToValuePtr<void>(EditorData));
					Inner->SetObjectPropertyValue(Helper.GetRawPtr(Helper.AddValue()), Expression);
				}
			}
		}

		NodesOut.Add(TEXT("param_") + Name, Expression);
	}
}

void FMaterialIRHandler::ApplyGraph(UObject* Material, UObject* EditorData, FIRApplyContext& Ctx, TMap<FString, UMaterialExpression*>& NodesOut)
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

		UClass* Class = ForgeMindIR::ResolveClass(ExpressionClassName(Type));
		if (Class == nullptr)
		{
			Ctx.Warn(TEXT("MAT_INVALID_EXPRESSION"), FString::Printf(TEXT("expression class for '%s' not found"), *Type));
			continue;
		}

		UMaterialExpression* Expression = NewObject<UMaterialExpression>(Material, Class, NAME_None, RF_Transactional);
		if (Expression == nullptr)
		{
			continue;
		}
		if (EditorData != nullptr)
		{
			Expression->Material = Cast<UMaterial>(Material);

			// Register the node in the material's expression list (mirrors parameter handling above).
			FProperty* ListProperty = ForgeMindIR::FindProperty(EditorData, TEXT("ExpressionCollection"));
			if (ListProperty == nullptr)
			{
				ListProperty = ForgeMindIR::FindProperty(EditorData, TEXT("Expressions"));
			}
			if (FArrayProperty* ArrayProperty = CastField<FArrayProperty>(ListProperty))
			{
				FObjectProperty* Inner = CastField<FObjectProperty>(ArrayProperty->Inner);
				if (Inner != nullptr)
				{
					FScriptArrayHelper Helper(ArrayProperty, ArrayProperty->ContainerPtrToValuePtr<void>(EditorData));
					Inner->SetObjectPropertyValue(Helper.GetRawPtr(Helper.AddValue()), Expression);
				}
			}
		}

		// Node level scalar properties (constant, texture, function asset...).
		for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Node->Values)
		{
			if (Pair.Key == TEXT("id") || Pair.Key == TEXT("type") || Pair.Key == TEXT("inputs"))
			{
				continue;
			}
			FString Error;
			ForgeMindIR::SetProperty(Expression, Pair.Key, Pair.Value, Error);
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
				int32 OutputIndex = 0;
				Pending.Input->TryGetNumberField(TEXT("outputIndex"), OutputIndex);
				if (FExpressionInput* Input = GetExpressionInput(Target, Pending.InputName))
				{
					Input->Connect(OutputIndex, Source);
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
			if (FExpressionInput* Input = GetExpressionInput(Target, Pending.InputName))
			{
				if (ConstantValue->Type == EJson::Number)
				{
					SetInputConstant(Input, EditorData, ConstantValue->AsNumber());
				}
				else
				{
					FString Error;
					ForgeMindIR::SetProperty(Target, Pending.InputName, ConstantValue, Error);
				}
			}
			else
			{
				FString Error;
				ForgeMindIR::SetProperty(Target, Pending.InputName, ConstantValue, Error);
			}
			continue;
		}

		if (Pending.Input->HasField(TEXT("asset")))
		{
			FString Error;
			if (!ForgeMindIR::SetProperty(Target, Pending.InputName, Pending.Input->TryGetField(TEXT("asset")), Error))
			{
				Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), Error);
			}
		}
	}
}

void FMaterialIRHandler::ApplyOutputs(UObject* Material, UObject* EditorData, FIRApplyContext& Ctx, const TMap<FString, UMaterialExpression*>& Nodes)
{
	if (EditorData == nullptr || !Ctx.Definition.IsValid())
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
		FExpressionInput* Input = GetExpressionInput(EditorData, Pair.Key);
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
				Input->Connect(OutputIndex, Source);
				continue;
			}
			Ctx.Warn(TEXT("MAT_UNCONNECTED_OUTPUT"), FString::Printf(TEXT("output '%s' references unknown node '%s'"), *Pair.Key, *SourceId));
			continue;
		}

		double Value = 0.0;
		if (Output->TryGetNumberField(TEXT("value"), Value))
		{
			SetInputConstant(Input, EditorData, Value);
		}
	}
}

bool FMaterialIRHandler::Update(UObject* Asset, FIRApplyContext& Ctx)
{
	UMaterial* Material = Cast<UMaterial>(Asset);
	UObject* EditorData = GetEditorOnlyData(Asset);

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
			bOk = Material != nullptr && ForgeMindIR::SetProperty(Material, Key, Patch->TryGetField(TEXT("value")), Error);
			if (!bOk && EditorData != nullptr)
			{
				bOk = ForgeMindIR::SetProperty(EditorData, Key, Patch->TryGetField(TEXT("value")), Error);
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
			FExpressionInput* Input = GetExpressionInput(EditorData, OutputName);
			ActualOp = FGenericAssetIRHandler::InferOp(Input != nullptr, bDelete);
			if (bDelete)
			{
				Ctx.Warn(TEXT("UNSUPPORTED_PATCH_TYPE"),
					FString::Printf(TEXT("clearing output '%s' is not supported - set it to a constant instead"), *OutputName));
			}
			else if (Input != nullptr)
			{
				double Value = 0.0;
				if (Patch->TryGetNumberField(TEXT("value"), Value))
				{
					bOk = SetInputConstant(Input, EditorData, Value);
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

	if (UObject* EditorData = GetEditorOnlyData(Asset))
	{
		TArray<TSharedPtr<FJsonValue>> Nodes;
		FProperty* ListProperty = ForgeMindIR::FindProperty(EditorData, TEXT("ExpressionCollection"));
		if (ListProperty == nullptr)
		{
			ListProperty = ForgeMindIR::FindProperty(EditorData, TEXT("Expressions"));
		}
		if (FArrayProperty* ArrayProperty = CastField<FArrayProperty>(ListProperty))
		{
			FObjectProperty* Inner = CastField<FObjectProperty>(ArrayProperty->Inner);
			FScriptArrayHelper Helper(ArrayProperty, ArrayProperty->ContainerPtrToValuePtr<void>(EditorData));
			for (int32 i = 0; i < Helper.Num(); ++i)
			{
				UObject* Element = Inner->GetObjectPropertyValue(Helper.GetRawPtr(i));
				if (Element == nullptr)
				{
					continue;
				}
				TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
				Entry->SetStringField(TEXT("type"), Element->GetClass()->GetName());
				Entry->SetStringField(TEXT("id"), Element->GetName());
				Nodes.Add(MakeShared<FJsonValueObject>(Entry));
			}
		}
		Definition->SetArrayField(TEXT("nodes"), Nodes);
	}
	else
	{
		Ctx.Warn(TEXT("MAT_UNCONNECTED_OUTPUT"), TEXT("material has no editor-only data to serialize"));
	}

	return Definition;
}

TSharedRef<IIRHandler> MakeMaterialIRHandler()
{
	return MakeShared<FMaterialIRHandler>();
}