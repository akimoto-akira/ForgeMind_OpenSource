// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Blueprint component management commands (add_blueprint_component,
// list_blueprint_components, set_component_property, remove_blueprint_component)
// - split out of ForgeMindBridgeClient.cpp.
//
// Design: Blueprint components are managed through the blueprint's
// SimpleConstructionScript (SCS). Each USCS_Node carries a ComponentClass
// and a ComponentTemplate (the default-state subobject). After any structural
// change the blueprint is compiled so the generated class CDO reflects the
// new component set, then marked dirty for persistence.
//
// For SceneComponent children (e.g. SpringArm attached to a Capsule), an optional
// "parent" field specifies the attachment target. The root component is the
// actor's DefaultSceneRootNode; sub-components attach to it by default.

#include "ForgeMindBridgeClient.h"

#include "ForgeMindBlueprintUtils.h"

#include "Components/ActorComponent.h"
#include "Components/SceneComponent.h"
#include "Dom/JsonObject.h"
#include "Dom/JsonValue.h"
#include "Engine/Blueprint.h"
#include "Engine/BlueprintGeneratedClass.h"
#include "Engine/SimpleConstructionScript.h"
#include "Engine/SCS_Node.h"
#include "Components/SkeletalMeshComponent.h"
#include "Animation/AnimBlueprint.h"
#include "JsonObjectConverter.h"
#include "Kismet2/BlueprintEditorUtils.h"
#include "Kismet2/KismetEditorUtilities.h"
#include "Misc/Paths.h"
#include "UObject/UObjectGlobals.h"
#include "UObject/UnrealType.h"

using namespace ForgeMindBlueprintUtils;

namespace
{
	/**
	 * Resolves a component class by name, tolerating the C++ type prefix
	 * (USpringArm -> SpringArm) the same way list_class_hierarchy does.
	 * Returns null if no matching non-abstract UClass is found.
	 */
	UClass* ResolveComponentClass(const FString& ClassName)
	{
		if (ClassName.IsEmpty())
		{
			return nullptr;
		}
		UClass* Class = ForgeMindFindFirstObject<UClass>(*ClassName);
		if (Class == nullptr && ClassName.Len() > 1 && (ClassName[0] == 'U' || ClassName[0] == 'A' || ClassName[0] == 'F'))
		{
			Class = ForgeMindFindFirstObject<UClass>(*ClassName.RightChop(1));
		}
		if (Class == nullptr)
		{
			return nullptr;
		}
		// Components must derive from UActorComponent, and must be concrete:
		// SCS::CreateNode instantiates the component template object, which
		// fails (null template -> null deref) for abstract classes. The editor
		// UI likewise only offers concrete classes in "Add Component".
		if (!Class->IsChildOf(UActorComponent::StaticClass())
			|| Class->HasAnyClassFlags(CLASS_Abstract))
		{
			return nullptr;
		}
		return Class;
	}

	/**
	 * Finds an SCS node by its component name within the blueprint's
	 * SimpleConstructionScript. Searches all nodes (including the root).
	 */
	USCS_Node* FindScsNodeByName(UBlueprint* Blueprint, const FString& ComponentName)
	{
		if (Blueprint == nullptr || Blueprint->SimpleConstructionScript == nullptr)
		{
			return nullptr;
		}
		USimpleConstructionScript* SCS = Blueprint->SimpleConstructionScript;
		if (SCS->GetDefaultSceneRootNode() != nullptr
			&& SCS->GetDefaultSceneRootNode()->GetVariableName().ToString() == ComponentName)
		{
			return SCS->GetDefaultSceneRootNode();
		}
		// SCS also provides FindSCSNode(FName) - a direct map lookup.
		if (USCS_Node* Found = SCS->FindSCSNode(FName(*ComponentName)))
		{
			return Found;
		}
		// Fallback: linear scan of all nodes (covers any edge case the map misses).
		for (USCS_Node* Node : SCS->GetAllNodes())
		{
			if (Node != nullptr && Node->GetVariableName().ToString() == ComponentName)
			{
				return Node;
			}
		}
		return nullptr;
	}

	/**
	 * Builds a JSON description of a single SCS node (component template).
	 * Includes name, class, parent (if any), and property defaults.
	 */
	TSharedRef<FJsonObject> ScsNodeToJson(USCS_Node* Node, USCS_Node* ParentNode)
	{
		TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
		Entry->SetStringField(TEXT("name"), Node->GetVariableName().ToString());
		Entry->SetStringField(TEXT("class"), Node->ComponentClass != nullptr ? Node->ComponentClass->GetName() : TEXT(""));
		Entry->SetStringField(TEXT("parent"), ParentNode != nullptr ? ParentNode->GetVariableName().ToString() : TEXT(""));

		// Component template property defaults
		if (Node->ComponentTemplate != nullptr)
		{
			TSharedRef<FJsonObject> Properties = MakeShared<FJsonObject>();
			FJsonObjectConverter::UStructToJsonObject(Node->ComponentTemplate->GetClass(), Node->ComponentTemplate, Properties);
			Entry->SetObjectField(TEXT("properties"), Properties);
		}
		return Entry;
	}
}

// ---- add_blueprint_component ----

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleAddBlueprintComponent(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString ClassName;
	FString ComponentName;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("class"), ClassName) || ClassName.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("name"), ComponentName) || ComponentName.IsEmpty())
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path), class (component class name, e.g. SpringArmComponent or CameraComponent), name (component name, e.g. CameraBoom); optional: parent (parent component name for attachment, defaults to the root)"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Blueprint '%s' not found"), *AssetPath));
		return Result;
	}

	UClass* ComponentClass = ResolveComponentClass(ClassName);
	if (ComponentClass == nullptr)
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("'%s' is not a valid component class (must derive from ActorComponent and not be abstract)"), *ClassName));
		return Result;
	}

	USimpleConstructionScript* SCS = Blueprint->SimpleConstructionScript;
	if (SCS == nullptr)
	{
		// The blueprint editor creates the SCS lazily - trigger it by
		// asking for the default scene root, which initializes the SCS.
		SCS = Blueprint->SimpleConstructionScript;
		if (SCS == nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), TEXT("Blueprint has no SimpleConstructionScript - this blueprint type does not support components"));
			return Result;
		}
	}

	// Refuse duplicate names within the same blueprint.
	if (FindScsNodeByName(Blueprint, ComponentName) != nullptr)
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("A component named '%s' already exists in this blueprint"), *ComponentName));
		return Result;
	}

	// Resolve optional parent for attachment (SceneComponent hierarchy only).
	FString ParentName;
	USCS_Node* ParentNode = nullptr;
	if (Payload->TryGetStringField(TEXT("parent"), ParentName) && !ParentName.IsEmpty())
	{
		ParentNode = FindScsNodeByName(Blueprint, ParentName);
		if (ParentNode == nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Parent component '%s' not found in this blueprint"), *ParentName));
			return Result;
		}
	}

	// Create the SCS node. CreateNode only allocates the node and its
	// template subobject; it does NOT insert the node into the SCS tree.
	// We must explicitly attach it to a parent (or to the default scene root).
	USCS_Node* NewNode = SCS->CreateNode(ComponentClass, FName(*ComponentName));
	if (NewNode == nullptr)
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Failed to create SCS node for '%s'"), *ClassName));
		return Result;
	}

	// Attach to the specified parent, or to the default scene root.
	if (ParentNode != nullptr)
	{
		ParentNode->AddChildNode(NewNode);
	}
	else
	{
		USCS_Node* RootNode = SCS->GetDefaultSceneRootNode();
		if (RootNode != nullptr)
		{
			// Only SceneComponents support attachment hierarchy.
			// Non-scene components (e.g. input components) are added as
			// root-level nodes via SCS->AddNode instead.
			if (ComponentClass->IsChildOf(USceneComponent::StaticClass()))
			{
				RootNode->AddChildNode(NewNode);
			}
			else
			{
				SCS->AddNode(NewNode);
			}
		}
		else
		{
			// No default scene root - add as a root node directly.
			SCS->AddNode(NewNode);
		}
	}

	// Apply optional initial properties from the "properties" object.
	const TSharedPtr<FJsonObject>* PropsObj;
	if (Payload->TryGetObjectField(TEXT("properties"), PropsObj) && PropsObj->IsValid())
	{
		UObject* Template = NewNode->ComponentTemplate;
		if (Template != nullptr)
		{
			for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : (*PropsObj)->Values)
			{
				FProperty* Property = FindFProperty<FProperty>(Template->GetClass(), *Pair.Key);
				if (Property == nullptr)
				{
					continue;
				}
				void* ValueData = Property->ContainerPtrToValuePtr<void>(Template);
				if (Pair.Value->Type == EJson::Object || Pair.Value->Type == EJson::Array)
				{
					FJsonObjectConverter::JsonValueToUProperty(Pair.Value, Property, ValueData);
				}
				else
				{
					FString ImportText;
					if (Pair.Value->Type == EJson::String)
					{
						ImportText = Pair.Value->AsString();
					}
					else if (Pair.Value->Type == EJson::Number)
					{
						ImportText = FString::SanitizeFloat(Pair.Value->AsNumber());
					}
					else if (Pair.Value->Type == EJson::Boolean)
					{
						ImportText = Pair.Value->AsBool() ? TEXT("true") : TEXT("false");
					}
					if (!ImportText.IsEmpty())
					{
						const TCHAR* TextPtr = *ImportText;
						Property->ImportText(TextPtr, ValueData, PPF_PropertyWindow, Template);
					}
				}
				// Notify per-property so the template picks up the change.
				FPropertyChangedEvent ChangedEvent(Property);
				Template->PreEditChange(Property);
				Template->PostEditChangeProperty(ChangedEvent);
			}
		}
	}

	// Compile + dirty so the generated class reflects the new component.
	FBlueprintEditorUtils::MarkBlueprintAsStructurallyModified(Blueprint);
	FKismetEditorUtilities::CompileBlueprint(Blueprint);
	Blueprint->MarkPackageDirty();

	Result->SetBoolField(TEXT("added"), true);
	Result->SetStringField(TEXT("name"), ComponentName);
	Result->SetStringField(TEXT("class"), ComponentClass->GetName());
	Result->SetStringField(TEXT("parent"), ParentNode != nullptr ? ParentNode->GetVariableName().ToString() : TEXT(""));
	Result->SetBoolField(TEXT("dirty"), Blueprint->GetPackage()->IsDirty());
	return Result;
}

// ---- list_blueprint_components ----

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleListBlueprintComponents(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty())
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required field: path (blueprint asset path)"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Blueprint '%s' not found"), *AssetPath));
		return Result;
	}

	USimpleConstructionScript* SCS = Blueprint->SimpleConstructionScript;
	if (SCS == nullptr)
	{
		Result->SetBoolField(TEXT("found"), true);
		Result->SetNumberField(TEXT("count"), 0);
		Result->SetArrayField(TEXT("components"), TArray<TSharedPtr<FJsonValue>>());
		return Result;
	}

	// Build a parent map so each node knows its parent name.
	TMap<USCS_Node*, USCS_Node*> ParentMap;
	for (USCS_Node* Node : SCS->GetAllNodes())
	{
		if (Node == nullptr)
		{
			continue;
		}
		for (USCS_Node* Child : Node->ChildNodes)
		{
			ParentMap.Add(Child, Node);
		}
	}

	TArray<TSharedPtr<FJsonValue>> Components;
	for (USCS_Node* Node : SCS->GetAllNodes())
	{
		if (Node == nullptr)
		{
			continue;
		}
		USCS_Node** Parent = ParentMap.Find(Node);
		Components.Add(MakeShared<FJsonValueObject>(ScsNodeToJson(Node, Parent ? *Parent : nullptr)));
	}

	Result->SetBoolField(TEXT("found"), true);
	Result->SetNumberField(TEXT("count"), Components.Num());
	Result->SetArrayField(TEXT("components"), Components);
	return Result;
}

// ---- set_component_property ----

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleSetComponentProperty(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString ComponentName;
	FString PropertyName;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("component"), ComponentName) || ComponentName.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("property"), PropertyName) || PropertyName.IsEmpty()
		|| !Payload->HasField(TEXT("value")))
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path, component (component name), property, value"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Blueprint '%s' not found"), *AssetPath));
		return Result;
	}

	USCS_Node* Node = FindScsNodeByName(Blueprint, ComponentName);
	UObject* Template = nullptr;
	bool bIsNativeComponent = false;
	if (Node != nullptr)
	{
		Template = Node->ComponentTemplate;
	}
	else
	{
		// Fallback: the component may be a native (C++ defined) component
		// inherited from the parent class (e.g. Mesh on a Character). In that
		// case it does not exist in the SCS - look it up on the CDO.
		UClass* GenClass = Blueprint->GeneratedClass;
		if (GenClass == nullptr)
		{
			GenClass = Blueprint->ParentClass;
		}
		if (GenClass != nullptr)
		{
			AActor* CDO = Cast<AActor>(GenClass->GetDefaultObject());
			if (CDO != nullptr)
			{
				// Search all native components on the CDO by name.
				// Match either the component's FName (object name) or the
				// C++ property name in the owner class (e.g. "CharacterMovement"
				// on ACharacter, even though the object FName might differ).
				TArray<UActorComponent*> Components;
				CDO->GetComponents(Components);
				for (UActorComponent* Comp : Components)
				{
					if (Comp == nullptr)
					{
						continue;
					}
					if (Comp->GetFName() == FName(*ComponentName))
					{
						Template = Comp;
						bIsNativeComponent = true;
						break;
					}
				}
				// Fallback: scan FProperty fields on the owner class for a
				// matching object property whose subobject value equals Comp.
				if (Template == nullptr)
				{
					for (UActorComponent* Comp : Components)
					{
						if (Comp == nullptr)
						{
							continue;
						}
						UClass* OwnerClass = Comp->GetOwner() ? Comp->GetOwner()->GetClass() : CDO->GetClass();
						if (OwnerClass == nullptr)
						{
							OwnerClass = CDO->GetClass();
						}
						for (TFieldIterator<FObjectProperty> It(OwnerClass); It; ++It)
						{
							FObjectProperty* Prop = *It;
							if (Prop && Prop->GetFName() == FName(*ComponentName))
							{
								void* PropPtr = Prop->ContainerPtrToValuePtr<void>(CDO);
								if (PropPtr && *((UObject**)PropPtr) == Comp)
								{
									Template = Comp;
									bIsNativeComponent = true;
									break;
								}
							}
						}
						if (Template != nullptr)
						{
							break;
						}
					}
				}
			}
		}
		if (Template == nullptr)
		{
			Result->SetBoolField(TEXT("set"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Component '%s' not found in blueprint '%s' (checked SCS nodes and native CDO components)"), *ComponentName, *AssetPath));
			return Result;
		}
	}
	if (Template == nullptr)
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Component '%s' has no template object"), *ComponentName));
		return Result;
	}

	FProperty* Property = FindFProperty<FProperty>(Template->GetClass(), *PropertyName);
	if (Property == nullptr)
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Property '%s' not found on component '%s'"), *PropertyName, *ComponentName));
		return Result;
	}

	const TSharedPtr<FJsonValue> JsonValue = Payload->TryGetField(TEXT("value"));
	if (!JsonValue.IsValid())
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Missing required field: value"));
		return Result;
	}

	void* ValueData = Property->ContainerPtrToValuePtr<void>(Template);
	bool bImported = false;

	// Struct/container properties don't accept JSON through ImportText, so
	// write them via the JsonUtilities converter instead.
	if (JsonValue->Type == EJson::Object || JsonValue->Type == EJson::Array)
	{
		bImported = FJsonObjectConverter::JsonValueToUProperty(JsonValue, Property, ValueData);
	}
	else
	{
		FString ImportText;
		if (JsonValue->Type == EJson::String)
		{
			ImportText = JsonValue->AsString();
		}
		else if (JsonValue->Type == EJson::Number)
		{
			ImportText = FString::SanitizeFloat(JsonValue->AsNumber());
		}
		else if (JsonValue->Type == EJson::Boolean)
		{
			ImportText = JsonValue->AsBool() ? TEXT("true") : TEXT("false");
		}
		else
		{
			Result->SetBoolField(TEXT("set"), false);
			Result->SetStringField(TEXT("reason"), TEXT("Unsupported JSON value type for property import"));
			return Result;
		}
		const TCHAR* TextPtr = *ImportText;
		bImported = Property->ImportText(TextPtr, ValueData, PPF_PropertyWindow, Template) != nullptr;
	}

	if (!bImported)
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Value cannot be assigned to property '%s'"), *PropertyName));
		return Result;
	}

	// Notify the template of the change and recompile.
	FPropertyChangedEvent ChangedEvent(Property);
	Template->PreEditChange(Property);
	Template->PostEditChangeProperty(ChangedEvent);
	if (!bIsNativeComponent)
	{
		FBlueprintEditorUtils::MarkBlueprintAsStructurallyModified(Blueprint);
	}
	FKismetEditorUtilities::CompileBlueprint(Blueprint);
	Blueprint->MarkPackageDirty();

	Result->SetBoolField(TEXT("set"), true);
	Result->SetStringField(TEXT("component"), ComponentName);
	Result->SetStringField(TEXT("property"), PropertyName);
	Result->SetBoolField(TEXT("dirty"), Blueprint->GetPackage()->IsDirty());
	return Result;
}

// ---- remove_blueprint_component ----

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleRemoveBlueprintComponent(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString ComponentName;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("component"), ComponentName) || ComponentName.IsEmpty())
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path, component (component name)"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Blueprint '%s' not found"), *AssetPath));
		return Result;
	}

	USimpleConstructionScript* SCS = Blueprint->SimpleConstructionScript;
	if (SCS == nullptr)
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Blueprint has no SimpleConstructionScript"));
		return Result;
	}

	USCS_Node* Node = FindScsNodeByName(Blueprint, ComponentName);
	if (Node == nullptr)
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Component '%s' not found in blueprint '%s'"), *ComponentName, *AssetPath));
		return Result;
	}

	// Refuse to remove the default scene root.
	if (Node == SCS->GetDefaultSceneRootNode())
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), TEXT("The default scene root cannot be removed"));
		return Result;
	}

	// Remove the node from the SCS - this also handles child reparenting.
	SCS->RemoveNode(Node);

	// Compile + dirty so the generated class no longer has the component.
	FBlueprintEditorUtils::MarkBlueprintAsStructurallyModified(Blueprint);
	FKismetEditorUtilities::CompileBlueprint(Blueprint);
	Blueprint->MarkPackageDirty();

	Result->SetBoolField(TEXT("removed"), true);
	Result->SetStringField(TEXT("component"), ComponentName);
	Result->SetBoolField(TEXT("dirty"), Blueprint->GetPackage()->IsDirty());
	return Result;
}