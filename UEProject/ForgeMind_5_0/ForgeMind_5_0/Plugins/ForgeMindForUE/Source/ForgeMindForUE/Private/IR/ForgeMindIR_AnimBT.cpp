// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Animation Blueprint (anim_blueprint) and AI (behavior_tree / blackboard)
// domain IR handlers.

#include "ForgeMindIRHandler.h"
#include "ForgeMindIRGeneric.h"

#include "ForgeMindBlueprintUtils.h"
#include "ForgeMindBridgeClient.h"

#include "AssetRegistry/AssetRegistryModule.h"
#include "Engine/Blueprint.h"
#include "Kismet2/KismetEditorUtilities.h"
#include "Misc/PackageName.h"
#include "UObject/Package.h"
#include "UObject/UnrealType.h"

namespace
{
	FForgeMindBridgeClient* Bridge()
	{
		return FForgeMindBridgeClient::ActiveInstance();
	}

	TSharedPtr<FJsonObject> MakePayload(const FString& Path)
	{
		TSharedRef<FJsonObject> Payload = MakeShared<FJsonObject>();
		Payload->SetStringField(TEXT("path"), Path);
		return Payload;
	}

	bool BridgeBool(const TSharedPtr<FJsonObject>& Result, const TCHAR* Key)
	{
		bool bValue = false;
		return Result.IsValid() && Result->TryGetBoolField(Key, bValue) && bValue;
	}

	/** Finds a UClass by name, tolerating the leading U/A/F prefix. */
	UClass* ResolveParentClass(const FString& Name)
	{
		if (Name.IsEmpty())
		{
			return nullptr;
		}
		return ForgeMindIR::ResolveClass(Name);
	}

	UObject* CreateBlueprintAsset(const FString& Path, const FString& ParentClassName,
		UClass* BlueprintClassType, UClass* GeneratedClassType, FString& OutError)
	{
		UClass* ParentClass = ResolveParentClass(ParentClassName);
		if (ParentClass == nullptr)
		{
			OutError = FString::Printf(TEXT("parent class '%s' was not found"), *ParentClassName);
			return nullptr;
		}

		UPackage* Package = CreatePackage(*Path);
		if (Package == nullptr)
		{
			OutError = FString::Printf(TEXT("cannot create package for '%s'"), *Path);
			return nullptr;
		}
		Package->FullyLoad();

		UBlueprint* Blueprint = FKismetEditorUtilities::CreateBlueprint(
			ParentClass, Package, FName(*ForgeMindIR::AssetNameOf(Path)),
			BPTYPE_Normal, BlueprintClassType, GeneratedClassType);
		if (Blueprint == nullptr)
		{
			OutError = FString::Printf(TEXT("CreateBlueprint failed for '%s'"), *Path);
			return nullptr;
		}
		FAssetRegistryModule::AssetCreated(Blueprint);
		Package->MarkPackageDirty();
		return Blueprint;
	}
}

// ---------------------------------------------------------------------------
// AnimBlueprint
// ---------------------------------------------------------------------------

class FAnimBlueprintIRHandler : public IIRHandler
{
public:
	void GetAssetTypes(TArray<FString>& OutAssetTypes) const override
	{
		OutAssetTypes = { TEXT("anim_blueprint") };
	}

	UObject* Create(FIRApplyContext& Ctx) override;
	bool Update(UObject* Asset, FIRApplyContext& Ctx) override;
	bool DeleteAsset(FIRApplyContext& Ctx) override;
	TSharedPtr<FJsonObject> Read(UObject* Asset, FIRApplyContext& Ctx) override;
	bool CompileAsset(UObject* Asset, FIRApplyContext& Ctx) override;
};

UObject* FAnimBlueprintIRHandler::Create(FIRApplyContext& Ctx)
{
	UClass* AnimBPClass = ForgeMindIR::ResolveClass(TEXT("AnimBlueprint"));
	UClass* AnimBPGeneratedClass = ForgeMindIR::ResolveClass(TEXT("AnimBlueprintGeneratedClass"));
	if (AnimBPClass == nullptr)
	{
		Ctx.Fail(TEXT("UNSUPPORTED_ASSET_TYPE"), TEXT("UAnimBlueprint is not available in this editor"));
		return nullptr;
	}

	FString ParentClass = TEXT("AnimInstance");
	FString Skeleton;
	if (Ctx.Definition.IsValid())
	{
		FString Value;
		if (Ctx.Definition->TryGetStringField(TEXT("parentClass"), Value) && !Value.IsEmpty())
		{
			ParentClass = Value;
		}
		Ctx.Definition->TryGetStringField(TEXT("targetSkeleton"), Skeleton);
		if (Skeleton.IsEmpty())
		{
			Ctx.Definition->TryGetStringField(TEXT("targetSkeletonPath"), Skeleton);
		}
	}

	FString Error;
	UObject* Asset = CreateBlueprintAsset(Ctx.Path, ParentClass, AnimBPClass, AnimBPGeneratedClass, Error);
	if (Asset == nullptr)
	{
		Ctx.Fail(TEXT("CREATE_FAILED"), Error);
		return nullptr;
	}

	if (!Skeleton.IsEmpty())
	{
		FString SetError;
		if (!ForgeMindIR::SetProperty(Asset, TEXT("TargetSkeleton"), MakeShared<FJsonValueString>(Skeleton), SetError))
		{
			Ctx.Warn(TEXT("INVALID_SKELETON"), SetError);
		}
	}

	// Variables reuse the proven blueprint command.
	int32 VariableCount = 0;
	FForgeMindBridgeClient* Client = Bridge();
	const TArray<TSharedPtr<FJsonValue>>* Variables = nullptr;
	if (Client != nullptr && Ctx.Definition.IsValid() && Ctx.Definition->TryGetArrayField(TEXT("variables"), Variables))
	{
		for (const TSharedPtr<FJsonValue>& Value : *Variables)
		{
			const TSharedPtr<FJsonObject> Variable = Value->AsObject();
			if (!Variable.IsValid())
			{
				continue;
			}
			FString Name, Type;
			Variable->TryGetStringField(TEXT("name"), Name);
			Variable->TryGetStringField(TEXT("dataType"), Type);
			if (Type.IsEmpty())
			{
				Variable->TryGetStringField(TEXT("type"), Type);
			}
			if (Name.IsEmpty() || Type.IsEmpty())
			{
				continue;
			}
			TSharedPtr<FJsonObject> Payload = MakePayload(Ctx.Path);
			Payload->SetStringField(TEXT("name"), Name);
			Payload->SetStringField(TEXT("type"), Type);
			FString Category;
			if (Variable->TryGetStringField(TEXT("category"), Category))
			{
				Payload->SetStringField(TEXT("category"), Category);
			}
			if (Variable->HasField(TEXT("defaultValue")))
			{
				Payload->SetField(TEXT("default"), Variable->TryGetField(TEXT("defaultValue")));
			}
			if (BridgeBool(Client->HandleAddBlueprintVariable(Payload), TEXT("added")))
			{
				++VariableCount;
			}
		}
	}

	if (Ctx.Definition.IsValid() && Ctx.Definition->HasField(TEXT("animGraphs")))
	{
		Ctx.Warn(TEXT("UNCONNECTED_NODE"),
			TEXT("animGraphs node construction is not applied by the IR handler - build the AnimGraph in the editor"));
	}

	Ctx.Extra->SetNumberField(TEXT("variableCount"), VariableCount);
	return Asset;
}

bool FAnimBlueprintIRHandler::Update(UObject* Asset, FIRApplyContext& Ctx)
{
	FForgeMindBridgeClient* Client = Bridge();
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

		if (Type == TEXT("assetProperty"))
		{
			FString PropertyName = ForgeMindIR::FirstString(Patch, { TEXT("name") });
			ActualOp = FGenericAssetIRHandler::InferOp(
				ForgeMindIR::FindProperty(Asset, PropertyName) != nullptr, bDelete);
			FString Error;
			bOk = !bDelete && ForgeMindIR::SetProperty(Asset, PropertyName, Patch->TryGetField(TEXT("value")), Error);
			if (!bOk && !bDelete)
			{
				Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), Error);
			}
		}
		else if (Type == TEXT("variable") && Client != nullptr)
		{
			TSharedPtr<FJsonObject> Payload = MakePayload(Ctx.Path);
			Payload->SetStringField(TEXT("name"), Name);
			FString DataType;
			Patch->TryGetStringField(TEXT("dataType"), DataType);
			if (DataType.IsEmpty())
			{
				Patch->TryGetStringField(TEXT("type"), DataType);
			}
			Payload->SetStringField(TEXT("type"), DataType);
			if (Patch->HasField(TEXT("defaultValue")))
			{
				Payload->SetField(TEXT("default"), Patch->TryGetField(TEXT("defaultValue")));
			}
			if (bDelete)
			{
				ActualOp = TEXT("delete");
				bOk = BridgeBool(Client->HandleRemoveBlueprintVariable(Payload), TEXT("removed"));
			}
			else
			{
				bOk = BridgeBool(Client->HandleAddBlueprintVariable(Payload), TEXT("added"));
				if (!bOk)
				{
					ActualOp = TEXT("replace");
					bOk = BridgeBool(Client->HandleUpdateBlueprintVariable(Payload), TEXT("updated"));
				}
			}
		}
		else
		{
			Ctx.Warn(TEXT("UNSUPPORTED_PATCH_TYPE"),
				FString::Printf(TEXT("patch type '%s' is not handled by the anim_blueprint domain"), *Type));
			continue;
		}

		if (!bOk)
		{
			ActualOp = TEXT("failed");
		}
		Ctx.AddPatchResult(ActualOp, Type, Name, bOk);
	}
	return true;
}

bool FAnimBlueprintIRHandler::DeleteAsset(FIRApplyContext& Ctx)
{
	FString Error;
	if (!ForgeMindIR::DeleteAssetObject(Ctx.Path, Error))
	{
		Ctx.Fail(TEXT("DELETE_FAILED"), Error);
		return false;
	}
	return true;
}

bool FAnimBlueprintIRHandler::CompileAsset(UObject* Asset, FIRApplyContext& Ctx)
{
	FForgeMindBridgeClient* Client = Bridge();
	if (Client != nullptr && Cast<UBlueprint>(Asset) != nullptr)
	{
		return BridgeBool(Client->HandleCompileBlueprint(MakePayload(Ctx.Path)), TEXT("compiled"));
	}
	return IIRHandler::CompileAsset(Asset, Ctx);
}

TSharedPtr<FJsonObject> FAnimBlueprintIRHandler::Read(UObject* Asset, FIRApplyContext& Ctx)
{
	TSharedRef<FJsonObject> Definition = MakeShared<FJsonObject>();
	if (UBlueprint* Blueprint = Cast<UBlueprint>(Asset))
	{
		if (Blueprint->ParentClass != nullptr)
		{
			Definition->SetStringField(TEXT("parentClass"), Blueprint->ParentClass->GetName());
		}
		TArray<TSharedPtr<FJsonValue>> Variables;
		for (const FBPVariableDescription& Variable : Blueprint->NewVariables)
		{
			TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
			Entry->SetStringField(TEXT("name"), Variable.VarName.ToString());
			Entry->SetStringField(TEXT("dataType"), ForgeMindBlueprintUtils::PinTypeToText(Variable.VarType));
			Variables.Add(MakeShared<FJsonValueObject>(Entry));
		}
		Definition->SetArrayField(TEXT("variables"), Variables);
	}
	if (TSharedPtr<FJsonValue> Skeleton = ForgeMindIR::GetProperty(Asset, TEXT("TargetSkeleton")))
	{
		Definition->SetField(TEXT("targetSkeleton"), Skeleton);
	}
	return Definition;
}

TSharedRef<IIRHandler> MakeAnimBlueprintIRHandler()
{
	return MakeShared<FAnimBlueprintIRHandler>();
}

// ---------------------------------------------------------------------------
// BehaviorTree / Blackboard
// ---------------------------------------------------------------------------

namespace
{
	/** Sets an object member inside a struct held by an array element. */
	bool SetStructObjectMember(void* StructMemory, const UStruct* StructType, const FString& MemberName, UObject* Value)
	{
		FProperty* Member = ForgeMindIR::FindPropertyOnStruct(StructType, MemberName);
		FObjectProperty* ObjectMember = CastField<FObjectProperty>(Member);
		if (ObjectMember == nullptr)
		{
			return false;
		}
		ObjectMember->SetObjectPropertyValue(ObjectMember->ContainerPtrToValuePtr<void>(StructMemory), Value);
		return true;
	}

	UObject* CreateBTNode(UObject* Outer, const FString& TypeName)
	{
		UClass* Class = ForgeMindIR::ResolveClass(TypeName);
		if (Class == nullptr)
		{
			return nullptr;
		}
		return NewObject<UObject>(Outer, Class, NAME_None, RF_Transactional);
	}

	/** Recursively builds the runtime BT node tree from an IR node object. */
	UObject* BuildBTNode(UObject* Outer, const TSharedPtr<FJsonObject>& Node, TArray<FIRWarning>& Warnings)
	{
		if (!Node.IsValid())
		{
			return nullptr;
		}
		FString Type;
		Node->TryGetStringField(TEXT("type"), Type);
		if (Type.IsEmpty())
		{
			Warnings.Add({ TEXT("INVALID_NODE_GUID"), TEXT("behaviour tree node without a type") });
			return nullptr;
		}

		// Composite children may be nested under { "composite": {...} }.
		const TSharedPtr<FJsonObject>* InnerPtr = nullptr;
		Node->TryGetObjectField(TEXT("composite"), InnerPtr);
		const TSharedPtr<FJsonObject> Inner = InnerPtr ? *InnerPtr : nullptr;
		const TSharedPtr<FJsonObject>& Effective = Inner.IsValid() ? Inner : Node;
		Effective->TryGetStringField(TEXT("type"), Type);

		UObject* Created = CreateBTNode(Outer, Type);
		if (Created == nullptr)
		{
			Warnings.Add({ TEXT("INVALID_NODE_GUID"),
				FString::Printf(TEXT("behaviour tree node class '%s' was not found"), *Type) });
			return nullptr;
		}

		// SimpleParallel exposes its two branches as named members.
		const TSharedPtr<FJsonObject>* MainBranchPtr = nullptr;
		Effective->TryGetObjectField(TEXT("mainBranch"), MainBranchPtr);
		const TSharedPtr<FJsonObject> MainBranch = MainBranchPtr ? *MainBranchPtr : nullptr;
		if (MainBranch.IsValid())
		{
			if (UObject* BranchNode = BuildBTNode(Outer, MainBranch, Warnings))
			{
				FString SetError;
				ForgeMindIR::SetProperty(Created, TEXT("MainTask"),
					MakeShared<FJsonValueString>(BranchNode->GetPathName()), SetError);
			}
		}

		const TArray<TSharedPtr<FJsonValue>>* Children = nullptr;
		if (Effective->TryGetArrayField(TEXT("children"), Children))
		{
			FProperty* ChildrenProperty = ForgeMindIR::FindProperty(Created, TEXT("Children"));
			if (FArrayProperty* ChildrenArray = CastField<FArrayProperty>(ChildrenProperty))
			{
				FScriptArrayHelper Helper(ChildrenArray, ChildrenArray->ContainerPtrToValuePtr<void>(Created));
				for (const TSharedPtr<FJsonValue>& ChildValue : *Children)
				{
					const TSharedPtr<FJsonObject> ChildNode = ChildValue->AsObject();
					UObject* Child = BuildBTNode(Outer, ChildNode, Warnings);
					if (Child == nullptr)
					{
						continue;
					}
					const int32 Index = Helper.AddValue();
					void* Slot = Helper.GetRawPtr(Index);
					const UStruct* SlotStruct = CastField<FStructProperty>(ChildrenArray->Inner)
						? CastField<FStructProperty>(ChildrenArray->Inner)->Struct : nullptr;
					if (SlotStruct == nullptr)
					{
						continue;
					}
					if (!SetStructObjectMember(Slot, SlotStruct, TEXT("ChildComposite"), Child))
					{
						SetStructObjectMember(Slot, SlotStruct, TEXT("ChildTask"), Child);
					}
				}
			}
		}

		return Created;
	}
}

class FBehaviorTreeIRHandler : public IIRHandler
{
public:
	void GetAssetTypes(TArray<FString>& OutAssetTypes) const override
	{
		OutAssetTypes = { TEXT("behavior_tree"), TEXT("blackboard"), TEXT("blackboard_data") };
	}

	UObject* Create(FIRApplyContext& Ctx) override;
	bool Update(UObject* Asset, FIRApplyContext& Ctx) override;
	bool DeleteAsset(FIRApplyContext& Ctx) override;
	TSharedPtr<FJsonObject> Read(UObject* Asset, FIRApplyContext& Ctx) override;

private:
	UObject* CreateBehaviorTree(FIRApplyContext& Ctx);
	UObject* CreateBlackboard(FIRApplyContext& Ctx);
};

UObject* FBehaviorTreeIRHandler::CreateBehaviorTree(FIRApplyContext& Ctx)
{
	UClass* Class = ForgeMindIR::ResolveClass(TEXT("BehaviorTree"));
	if (Class == nullptr)
	{
		Ctx.Fail(TEXT("UNSUPPORTED_ASSET_TYPE"), TEXT("UBehaviorTree is not available in this editor"));
		return nullptr;
	}
	UObject* Asset = ForgeMindIR::CreateAssetInstance(Class, Ctx.Path, true);
	if (Asset == nullptr)
	{
		Ctx.Fail(TEXT("CREATE_FAILED"), FString::Printf(TEXT("failed to create behavior tree at %s"), *Ctx.Path));
		return nullptr;
	}

	// Blackboard reference.
	FString BlackboardPath;
	if (Ctx.Definition.IsValid())
	{
		BlackboardPath = ForgeMindIR::FirstString(Ctx.Definition, { TEXT("blackboardAsset"), TEXT("blackboard") });
	}
	if (!BlackboardPath.IsEmpty())
	{
		UObject* Blackboard = ForgeMindIR::ResolveObjectPath(BlackboardPath);
		if (Blackboard == nullptr)
		{
			Ctx.Warn(TEXT("MISSING_BLACKBOARD_REF"),
				FString::Printf(TEXT("blackboard asset '%s' was not found"), *BlackboardPath));
		}
		else
		{
			FString Error;
			ForgeMindIR::SetProperty(Asset, TEXT("BlackboardAsset"), MakeShared<FJsonValueString>(Blackboard->GetPathName()), Error);
		}
	}

	// Root composite.
	const TSharedPtr<FJsonObject>* RootPtr = nullptr;
	if (Ctx.Definition.IsValid())
	{
		Ctx.Definition->TryGetObjectField(TEXT("root"), RootPtr);
	}
	const TSharedPtr<FJsonObject> Root = RootPtr ? *RootPtr : nullptr;
	if (Root.IsValid())
	{
		if (UObject* RootNode = BuildBTNode(Asset, Root, Ctx.Warnings))
		{
			FString Error;
			if (!ForgeMindIR::SetProperty(Asset, TEXT("RootNode"), MakeShared<FJsonValueString>(RootNode->GetPathName()), Error))
			{
				Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), Error);
			}
			else
			{
				Ctx.Extra->SetBoolField(TEXT("rootNodeSet"), true);
			}
		}
	}
	return Asset;
}

UObject* FBehaviorTreeIRHandler::CreateBlackboard(FIRApplyContext& Ctx)
{
	UClass* Class = ForgeMindIR::ResolveClass(TEXT("BlackboardData"));
	if (Class == nullptr)
	{
		Ctx.Fail(TEXT("UNSUPPORTED_ASSET_TYPE"), TEXT("UBlackboardData is not available in this editor"));
		return nullptr;
	}
	UObject* Asset = ForgeMindIR::CreateAssetInstance(Class, Ctx.Path, true);
	if (Asset == nullptr)
	{
		Ctx.Fail(TEXT("CREATE_FAILED"), FString::Printf(TEXT("failed to create blackboard at %s"), *Ctx.Path));
		return nullptr;
	}

	const TArray<TSharedPtr<FJsonValue>>* Keys = nullptr;
	int32 KeyCount = 0;
	if (Ctx.Definition.IsValid() && Ctx.Definition->TryGetArrayField(TEXT("keys"), Keys))
	{
		FProperty* KeysProperty = ForgeMindIR::FindProperty(Asset, TEXT("Keys"));
		FArrayProperty* KeysArray = CastField<FArrayProperty>(KeysProperty);
		if (KeysArray != nullptr)
		{
			FScriptArrayHelper Helper(KeysArray, KeysArray->ContainerPtrToValuePtr<void>(Asset));
			for (const TSharedPtr<FJsonValue>& Value : *Keys)
			{
				const TSharedPtr<FJsonObject> Key = Value->AsObject();
				if (!Key.IsValid())
				{
					continue;
				}
				FString Name, KeyType;
				Key->TryGetStringField(TEXT("name"), Name);
				Key->TryGetStringField(TEXT("keyType"), KeyType);
				if (Name.IsEmpty() || KeyType.IsEmpty())
				{
					Ctx.Warn(TEXT("INVALID_KEY_TYPE"), TEXT("blackboard key missing name or keyType"));
					continue;
				}

				UClass* KeyTypeClass = ForgeMindIR::ResolveClass(TEXT("BlackboardKeyType_") + KeyType);
				if (KeyTypeClass == nullptr)
				{
					Ctx.Warn(TEXT("INVALID_KEY_TYPE"),
						FString::Printf(TEXT("blackboard key type '%s' was not found"), *KeyType));
					continue;
				}

				const int32 Index = Helper.AddValue();
				void* Slot = Helper.GetRawPtr(Index);
				const UStruct* SlotStruct = CastField<FStructProperty>(KeysArray->Inner)
					? CastField<FStructProperty>(KeysArray->Inner)->Struct : nullptr;
				if (SlotStruct == nullptr)
				{
					continue;
				}
				FProperty* EntryName = ForgeMindIR::FindPropertyOnStruct(SlotStruct, TEXT("EntryName"));
				if (FNameProperty* NameProperty = CastField<FNameProperty>(EntryName))
				{
					NameProperty->SetPropertyValue(NameProperty->ContainerPtrToValuePtr<void>(Slot), FName(*Name));
				}

				UObject* KeyTypeInstance = NewObject<UObject>(Asset, KeyTypeClass, NAME_None, RF_Transactional);
				SetStructObjectMember(Slot, SlotStruct, TEXT("KeyType"), KeyTypeInstance);
				++KeyCount;
			}
		}
	}
	Ctx.Extra->SetNumberField(TEXT("keyCount"), KeyCount);
	return Asset;
}

UObject* FBehaviorTreeIRHandler::Create(FIRApplyContext& Ctx)
{
	return Ctx.AssetType == TEXT("behavior_tree") ? CreateBehaviorTree(Ctx) : CreateBlackboard(Ctx);
}

bool FBehaviorTreeIRHandler::Update(UObject* Asset, FIRApplyContext& Ctx)
{
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

		FString ActualOp = TEXT("failed");
		bool bOk = false;

		if (Type == TEXT("blackboardRef"))
		{
			FString Path = ForgeMindIR::FirstString(Patch, { TEXT("asset"), TEXT("value") });
			UObject* Blackboard = ForgeMindIR::ResolveObjectPath(Path);
			ActualOp = TEXT("replace");
			FString Error;
			bOk = Blackboard != nullptr
				&& ForgeMindIR::SetProperty(Asset, TEXT("BlackboardAsset"), MakeShared<FJsonValueString>(Blackboard->GetPathName()), Error);
			if (!bOk)
			{
				Ctx.Warn(TEXT("MISSING_BLACKBOARD_REF"), FString::Printf(TEXT("blackboard '%s' not found"), *Path));
			}
		}
		else
		{
			Ctx.Warn(TEXT("UNSUPPORTED_PATCH_TYPE"),
				FString::Printf(TEXT("patch type '%s' is not handled by the behavior_tree domain"), *Type));
			continue;
		}

		if (!bOk)
		{
			ActualOp = TEXT("failed");
		}
		Ctx.AddPatchResult(ActualOp, Type, Name, bOk);
	}
	return true;
}

bool FBehaviorTreeIRHandler::DeleteAsset(FIRApplyContext& Ctx)
{
	FString Error;
	if (!ForgeMindIR::DeleteAssetObject(Ctx.Path, Error))
	{
		Ctx.Fail(TEXT("DELETE_FAILED"), Error);
		return false;
	}
	return true;
}

TSharedPtr<FJsonObject> FBehaviorTreeIRHandler::Read(UObject* Asset, FIRApplyContext& Ctx)
{
	TSharedRef<FJsonObject> Definition = MakeShared<FJsonObject>();
	if (Ctx.AssetType == TEXT("behavior_tree"))
	{
		if (TSharedPtr<FJsonValue> Blackboard = ForgeMindIR::GetProperty(Asset, TEXT("BlackboardAsset")))
		{
			Definition->SetField(TEXT("blackboardAsset"), Blackboard);
		}
		Definition->SetBoolField(TEXT("hasRootNode"),
			ForgeMindIR::GetProperty(Asset, TEXT("RootNode")).IsValid());
	}
	else
	{
		if (TSharedPtr<FJsonValue> Keys = ForgeMindIR::GetProperty(Asset, TEXT("Keys")))
		{
			Definition->SetField(TEXT("keys"), Keys);
		}
	}
	return Definition;
}

TSharedRef<IIRHandler> MakeBehaviorTreeIRHandler()
{
	return MakeShared<FBehaviorTreeIRHandler>();
}