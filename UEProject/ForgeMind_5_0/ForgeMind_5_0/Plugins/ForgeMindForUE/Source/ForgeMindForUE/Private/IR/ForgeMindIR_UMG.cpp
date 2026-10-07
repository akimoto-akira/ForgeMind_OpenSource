// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// UMG (widget_blueprint / widget_animation) and Enhanced Input
// (input_action / input_mapping_context) domain IR handlers.

#include "ForgeMindIRGeneric.h"

#include "ForgeMindBlueprintUtils.h"
#include "ForgeMindBridgeClient.h"

#include "Blueprint/UserWidget.h"
#include "Components/Widget.h"
#include "Dom/JsonValue.h"
#include "Engine/Blueprint.h"
#include "UObject/UnrealType.h"
#include "WidgetBlueprint.h"
#include "Blueprint/WidgetTree.h"

namespace
{
	FForgeMindBridgeClient* Bridge()
	{
		return FForgeMindBridgeClient::ActiveInstance();
	}

	bool BridgeBool(const TSharedPtr<FJsonObject>& Result, const TCHAR* Key)
	{
		bool bValue = false;
		return Result.IsValid() && Result->TryGetBoolField(Key, bValue) && bValue;
	}

	FString BridgeReason(const TSharedPtr<FJsonObject>& Result)
	{
		FString Reason;
		if (Result.IsValid())
		{
			Result->TryGetStringField(TEXT("reason"), Reason);
		}
		return Reason;
	}

	TSharedPtr<FJsonObject> MakePayload(const FString& Path)
	{
		TSharedRef<FJsonObject> Payload = MakeShared<FJsonObject>();
		Payload->SetStringField(TEXT("path"), Path);
		return Payload;
	}

	/** Appends an instance to a UObject array property (triggers, modifiers...). */
	bool AddObjectToArray(UObject* Owner, const FString& ArrayPropertyName, UObject* Element)
	{
		if (Owner == nullptr || Element == nullptr)
		{
			return false;
		}
		FProperty* Property = ForgeMindIR::FindProperty(Owner, ArrayPropertyName);
		FArrayProperty* ArrayProperty = CastField<FArrayProperty>(Property);
		if (ArrayProperty == nullptr)
		{
			return false;
		}
		FObjectProperty* Inner = CastField<FObjectProperty>(ArrayProperty->Inner);
		if (Inner == nullptr)
		{
			return false;
		}

		FScriptArrayHelper Helper(ArrayProperty, ArrayProperty->ContainerPtrToValuePtr<void>(Owner));
		const int32 Index = Helper.AddValue();
		Inner->SetObjectPropertyValue(Helper.GetRawPtr(Index), Element);
		return true;
	}

	/** Creates a sub-object of the given class owned by Outer (input modifiers...). */
	UObject* CreateSubObject(UObject* Outer, const FString& ClassName)
	{
		UClass* Class = ForgeMindIR::ResolveClass(ClassName);
		if (Class == nullptr)
		{
			return nullptr;
		}
		return NewObject<UObject>(Outer, Class);
	}

	void ApplyPropertiesTo(UObject* Target, const TSharedPtr<FJsonObject>& Values, const TSet<FString>& Skip, TArray<FIRWarning>& Warnings)
	{
		if (Target == nullptr || !Values.IsValid())
		{
			return;
		}
		for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Values->Values)
		{
			if (Skip.Contains(Pair.Key))
			{
				continue;
			}
			FString Error;
			if (!ForgeMindIR::SetProperty(Target, Pair.Key, Pair.Value, Error))
			{
				Warnings.Add({ TEXT("PROPERTY_NOT_FOUND"), Error });
			}
		}
	}
}

// ---------------------------------------------------------------------------
// Widget domain
// ---------------------------------------------------------------------------

namespace
{
	const TSet<FString>& WidgetStructuralKeys()
	{
		static TSet<FString> Keys = { TEXT("type"), TEXT("name"), TEXT("slots"), TEXT("child"), TEXT("widgetType"), TEXT("assetPath") };
		return Keys;
	}

	/** Recursively materializes one widgetTree node. */
	void BuildWidgetNode(const FString& Path, const TSharedPtr<FJsonObject>& Node, const FString& ParentName,
		TArray<FIRWarning>& Warnings, int32& OutWidgetCount)
	{
		if (!Node.IsValid())
		{
			return;
		}
		FString Type, Name;
		Node->TryGetStringField(TEXT("type"), Type);
		if (Type.IsEmpty())
		{
			Node->TryGetStringField(TEXT("widgetType"), Type);
		}
		Node->TryGetStringField(TEXT("name"), Name);
		if (Type.IsEmpty() || Name.IsEmpty())
		{
			Warnings.Add({ TEXT("INVALID_VALUE"), TEXT("widget tree node missing type or name") });
			return;
		}

		FForgeMindBridgeClient* Client = Bridge();
		if (Client == nullptr)
		{
			return;
		}

		FString AssetPath;
		Node->TryGetStringField(TEXT("assetPath"), AssetPath);
		const bool bReference = Type == TEXT("WidgetBlueprintReference") && !AssetPath.IsEmpty();
		const FString ClassName = bReference ? AssetPath : Type;

		TSharedPtr<FJsonObject> Payload = MakePayload(Path);
		Payload->SetStringField(TEXT("class"), ClassName);
		Payload->SetStringField(TEXT("name"), Name);
		if (!ParentName.IsEmpty())
		{
			Payload->SetStringField(TEXT("parent"), ParentName);
		}
		Payload->SetBoolField(TEXT("promote"), true);

		TSharedPtr<FJsonObject> Result = Client->HandleAddWidgetChild(Payload);
		if (!BridgeBool(Result, TEXT("added")))
		{
			Warnings.Add({ TEXT("PANEL_SLOT_MISMATCH"),
				FString::Printf(TEXT("widget '%s' (type=%s) was not added: %s"), *Name, *ClassName, *BridgeReason(Result)) });
			return;
		}
		++OutWidgetCount;

		// Per-widget scalar properties.
		for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Node->Values)
		{
			if (WidgetStructuralKeys().Contains(Pair.Key) || Pair.Value->Type == EJson::Object || Pair.Value->Type == EJson::Array)
			{
				continue;
			}
			TSharedPtr<FJsonObject> PropPayload = MakePayload(Path);
			PropPayload->SetStringField(TEXT("component"), Name);
			PropPayload->SetStringField(TEXT("property"), Pair.Key);
			PropPayload->SetField(TEXT("value"), Pair.Value);
			Client->HandleSetComponentProperty(PropPayload);
		}

		// Panel children ("slots": [{ widget: {...} }]).
		const TArray<TSharedPtr<FJsonValue>>* Slots = nullptr;
		if (Node->TryGetArrayField(TEXT("slots"), Slots))
		{
			for (const TSharedPtr<FJsonValue>& SlotValue : *Slots)
			{
				const TSharedPtr<FJsonObject> Slot = SlotValue->AsObject();
				if (!Slot.IsValid())
				{
					continue;
				}
				const TSharedPtr<FJsonObject>* ChildPtr = nullptr;
				Slot->TryGetObjectField(TEXT("widget"), ChildPtr);
				const TSharedPtr<FJsonObject> Child = ChildPtr ? *ChildPtr : nullptr;
				BuildWidgetNode(Path, Child, Name, Warnings, OutWidgetCount);
			}
		}

		// Single-content widgets ("child": {...}).
		const TSharedPtr<FJsonObject>* ChildPtr = nullptr;
		Node->TryGetObjectField(TEXT("child"), ChildPtr);
		const TSharedPtr<FJsonObject> Child = ChildPtr ? *ChildPtr : nullptr;
		BuildWidgetNode(Path, Child, Name, Warnings, OutWidgetCount);
	}
}

class FWidgetIRHandler : public IIRHandler
{
public:
	void GetAssetTypes(TArray<FString>& OutAssetTypes) const override
	{
		OutAssetTypes = { TEXT("widget_blueprint"), TEXT("widget_animation") };
	}

	UObject* Create(FIRApplyContext& Ctx) override;
	bool Update(UObject* Asset, FIRApplyContext& Ctx) override;
	bool DeleteAsset(FIRApplyContext& Ctx) override;
	TSharedPtr<FJsonObject> Read(UObject* Asset, FIRApplyContext& Ctx) override;
	bool CompileAsset(UObject* Asset, FIRApplyContext& Ctx) override;
};

UObject* FWidgetIRHandler::Create(FIRApplyContext& Ctx)
{
	FForgeMindBridgeClient* Client = Bridge();
	if (Client == nullptr)
	{
		Ctx.Fail(TEXT("WIDGET_COMPILATION_FAILED"), TEXT("bridge client is not available"));
		return nullptr;
	}

	if (Ctx.AssetType == TEXT("widget_animation"))
	{
		UClass* Class = ForgeMindIR::ResolveClass(TEXT("WidgetAnimation"));
		if (Class == nullptr)
		{
			Ctx.Fail(TEXT("UNSUPPORTED_ASSET_TYPE"), TEXT("UWidgetAnimation is not available in this editor"));
			return nullptr;
		}
		UObject* Asset = ForgeMindIR::CreateAssetInstance(Class, Ctx.Path, true);
		if (Asset == nullptr)
		{
			Ctx.Fail(TEXT("CREATE_FAILED"), FString::Printf(TEXT("failed to create widget animation at %s"), *Ctx.Path));
			return nullptr;
		}
		double Length = 1.0;
		if (Ctx.Definition.IsValid())
		{
			Ctx.Definition->TryGetNumberField(TEXT("length"), Length);
		}
		FString Error;
		ForgeMindIR::SetProperty(Asset, TEXT("SequenceLength"), MakeShared<FJsonValueNumber>(Length), Error);
		return Asset;
	}

	FString ParentClass = TEXT("UserWidget");
	if (Ctx.Definition.IsValid())
	{
		FString Extends;
		if (Ctx.Definition->TryGetStringField(TEXT("extends"), Extends) && !Extends.IsEmpty())
		{
			ParentClass = Extends;
		}
	}

	TSharedPtr<FJsonObject> Payload = MakePayload(Ctx.Path);
	Payload->SetStringField(TEXT("parentClass"), ParentClass);
	Payload->SetStringField(TEXT("blueprintType"), TEXT("widget"));

	TSharedPtr<FJsonObject> Result = Client->HandleCreateBlueprint(Payload);
	if (!BridgeBool(Result, TEXT("created")))
	{
		Ctx.Fail(TEXT("WIDGET_COMPILATION_FAILED"),
			FString::Printf(TEXT("failed to create widget blueprint: %s"), *BridgeReason(Result)));
		return nullptr;
	}

	int32 WidgetCount = 0;
	const TArray<TSharedPtr<FJsonValue>>* Tree = nullptr;
	if (Ctx.Definition.IsValid() && Ctx.Definition->TryGetArrayField(TEXT("widgetTree"), Tree))
	{
		for (const TSharedPtr<FJsonValue>& Value : *Tree)
		{
			BuildWidgetNode(Ctx.Path, Value->AsObject(), FString(), Ctx.Warnings, WidgetCount);
		}
	}

	const TArray<TSharedPtr<FJsonValue>>* Refs = nullptr;
	int32 AnimationCount = 0;
	if (Ctx.Definition.IsValid() && Ctx.Definition->TryGetArrayField(TEXT("animationReferences"), Refs))
	{
		for (const TSharedPtr<FJsonValue>& Value : *Refs)
		{
			const TSharedPtr<FJsonObject> Ref = Value->AsObject();
			if (!Ref.IsValid())
			{
				continue;
			}
			FString AnimPath = ForgeMindIR::FirstString(Ref, { TEXT("path"), TEXT("assetPath") });
			if (ForgeMindIR::ResolveObjectPath(AnimPath) != nullptr)
			{
				++AnimationCount;
			}
			else
			{
				Ctx.Warn(TEXT("ANIMATION_REFERENCE_NOT_FOUND"),
					FString::Printf(TEXT("widget animation '%s' does not exist"), *AnimPath));
			}
		}
	}

	Ctx.Extra->SetNumberField(TEXT("widgetCount"), WidgetCount);
	Ctx.Extra->SetNumberField(TEXT("animationCount"), AnimationCount);
	return ForgeMindIR::LoadAsset(Ctx.Path);
}

bool FWidgetIRHandler::Update(UObject* Asset, FIRApplyContext& Ctx)
{
	FForgeMindBridgeClient* Client = Bridge();
	if (Client == nullptr)
	{
		Ctx.Fail(TEXT("UPDATE_FAILED"), TEXT("bridge client is not available"));
		return false;
	}

	for (const TSharedPtr<FJsonValue>& PatchEntry : Ctx.Patches)
	{
		const TSharedPtr<FJsonObject> Patch = PatchEntry->AsObject();
		if (!Patch.IsValid())
		{
			continue;
		}
		FString Type, Name, Property;
		Patch->TryGetStringField(TEXT("type"), Type);
		Patch->TryGetStringField(TEXT("name"), Name);
		Patch->TryGetStringField(TEXT("property"), Property);
		const bool bDelete = ForgeMindIR::IsDeletePatch(Patch);

		FString ActualOp = TEXT("failed");
		bool bOk = false;

		if (Type == TEXT("widget"))
		{
			if (bDelete)
			{
				Ctx.Warn(TEXT("UNSUPPORTED_PATCH_TYPE"),
					FString::Printf(TEXT("removing widget '%s' is not supported - use the designer"), *Name));
			}
			else
			{
				int32 Count = 0;
				BuildWidgetNode(Ctx.Path, Patch, FString(), Ctx.Warnings, Count);
				bOk = Count > 0;
				ActualOp = bOk ? TEXT("add") : TEXT("failed");
			}
		}
		else if (Type == TEXT("widgetProperty") || Type == TEXT("slot"))
		{
			TSharedPtr<FJsonObject> Payload = MakePayload(Ctx.Path);
			Payload->SetStringField(TEXT("component"), Name);
			Payload->SetStringField(TEXT("property"), Property);
			Payload->SetField(TEXT("value"), Patch->TryGetField(TEXT("value")));
			TSharedPtr<FJsonObject> Result = Client->HandleSetComponentProperty(Payload);
			bOk = BridgeBool(Result, TEXT("set")) || !BridgeReason(Result).Contains(TEXT("not found"));
			ActualOp = TEXT("replace");
		}
		else
		{
			Ctx.Warn(TEXT("UNSUPPORTED_PATCH_TYPE"),
				FString::Printf(TEXT("patch type '%s' is not handled by the UMG domain"), *Type));
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

bool FWidgetIRHandler::DeleteAsset(FIRApplyContext& Ctx)
{
	FString Error;
	if (!ForgeMindIR::DeleteAssetObject(Ctx.Path, Error))
	{
		Ctx.Fail(TEXT("DELETE_FAILED"), Error);
		return false;
	}
	return true;
}

bool FWidgetIRHandler::CompileAsset(UObject* Asset, FIRApplyContext& Ctx)
{
	FForgeMindBridgeClient* Client = Bridge();
	if (Client == nullptr || Cast<UBlueprint>(Asset) == nullptr)
	{
		return IIRHandler::CompileAsset(Asset, Ctx);
	}
	const bool bCompiled = BridgeBool(Client->HandleCompileBlueprint(MakePayload(Ctx.Path)), TEXT("compiled"));
	if (!bCompiled)
	{
		Ctx.Warn(TEXT("WIDGET_COMPILATION_FAILED"), TEXT("widget blueprint reported compile errors"));
	}
	return bCompiled;
}

TSharedPtr<FJsonObject> FWidgetIRHandler::Read(UObject* Asset, FIRApplyContext& Ctx)
{
	TSharedRef<FJsonObject> Definition = MakeShared<FJsonObject>();

	UWidgetBlueprint* WidgetBP = Cast<UWidgetBlueprint>(Asset);
	if (WidgetBP != nullptr && WidgetBP->WidgetTree != nullptr)
	{
		if (WidgetBP->ParentClass != nullptr)
		{
			Definition->SetStringField(TEXT("extends"), WidgetBP->ParentClass->GetName());
		}
		TArray<UWidget*> Widgets;
		WidgetBP->WidgetTree->GetAllWidgets(Widgets);

		TArray<TSharedPtr<FJsonValue>> Tree;
		for (UWidget* Widget : Widgets)
		{
			if (Widget == nullptr)
			{
				continue;
			}
			TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
			Entry->SetStringField(TEXT("type"), Widget->GetClass()->GetName());
			Entry->SetStringField(TEXT("name"), Widget->GetName());
			Tree.Add(MakeShared<FJsonValueObject>(Entry));
		}
		Definition->SetArrayField(TEXT("widgetTree"), Tree);
	}
	return Definition;
}

TSharedRef<IIRHandler> MakeWidgetIRHandler()
{
	return MakeShared<FWidgetIRHandler>();
}