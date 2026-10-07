// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Event dispatcher management: add_event_dispatcher creates a blueprint
// multicast delegate member (PC_MCDelegate) plus its delegate-signature
// graph, mirroring the engine's FBlueprintEditor::AddNewEventDispatcher flow.

#include "ForgeMindBridgeClient.h"
#include "ForgeMindBlueprintUtils.h"

#include "Dom/JsonObject.h"
#include "Dom/JsonValue.h"
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphPin.h"
#include "EdGraphSchema_K2.h"
#include "K2Node_FunctionEntry.h"
#include "K2Node_FunctionResult.h"
#include "K2Node_EditablePinBase.h"
#include "K2Node_CallDelegate.h"
#include "K2Node_AddDelegate.h"
#include "K2Node_RemoveDelegate.h"
#include "K2Node_CustomEvent.h"
#include "Engine/Blueprint.h"
#include "Engine/BlueprintGeneratedClass.h"
#include "Kismet2/BlueprintEditorUtils.h"
#include "Kismet2/KismetEditorUtilities.h"
#include "Misc/Paths.h"
#include "UObject/UnrealType.h"
#include "UObject/UObjectGlobals.h"

using namespace ForgeMindBlueprintUtils;

// ---------------------------------------------------------------------------
// add_event_dispatcher
// Payload: { path, name, params?: [{name, type}, ...] }
// Creates a PC_MCDelegate member variable + delegate-signature graph with
// optional input parameters, exactly as the editor's "New Event Dispatcher"
// button does (BlueprintEditor.cpp::AddNewEventDispatcher).
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleAddEventDispatcher(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString DispatcherName;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("name"), DispatcherName) || DispatcherName.IsEmpty())
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path), name (dispatcher name); optional params ([{name, type}, ...])"));
		return Result;
	}

	// Normalize to the full object path (package.asset) for LoadObject.
	FString PackagePath = AssetPath;
	int32 DotIndex = INDEX_NONE;
	if (PackagePath.FindChar(TEXT('.'), DotIndex))
	{
		PackagePath = PackagePath.Left(DotIndex);
	}
	const FString ObjectPath = FString::Printf(TEXT("%s.%s"), *PackagePath, *FPaths::GetCleanFilename(PackagePath));

	UBlueprint* Blueprint = LoadObject<UBlueprint>(nullptr, *ObjectPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s' (create one via create_blueprint first)"), *ObjectPath));
		return Result;
	}

	// Check for name collision with an existing dispatcher or variable.
	FName DispatcherFName(*DispatcherName);
	for (const FBPVariableDescription& Var : Blueprint->NewVariables)
	{
		if (Var.VarName == DispatcherFName)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("A variable or dispatcher named '%s' already exists"), *DispatcherName));
			return Result;
		}
	}

	// 1. Add the multicast delegate member variable (PC_MCDelegate).
	Blueprint->Modify();
	FEdGraphPinType DelegateType;
	DelegateType.PinCategory = UEdGraphSchema_K2::PC_MCDelegate;
	if (!FBlueprintEditorUtils::AddMemberVariable(Blueprint, DispatcherFName, DelegateType))
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Failed to add event dispatcher '%s' to '%s'"), *DispatcherName, *ObjectPath));
		return Result;
	}

	// 2. Create the delegate-signature graph.
	const UEdGraphSchema_K2* K2Schema = GetDefault<UEdGraphSchema_K2>();
	UEdGraph* SignatureGraph = FBlueprintEditorUtils::CreateNewGraph(Blueprint, DispatcherFName, UEdGraph::StaticClass(), UEdGraphSchema_K2::StaticClass());
	if (SignatureGraph == nullptr)
	{
		FBlueprintEditorUtils::RemoveMemberVariable(Blueprint, DispatcherFName);
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Failed to create delegate signature graph for '%s'"), *DispatcherName));
		return Result;
	}

	SignatureGraph->bEditable = false;
	K2Schema->CreateDefaultNodesForGraph(*SignatureGraph);
	K2Schema->CreateFunctionGraphTerminators(*SignatureGraph, (UClass*)nullptr);
	K2Schema->AddExtraFunctionFlags(SignatureGraph, FUNC_BlueprintCallable | FUNC_BlueprintEvent | FUNC_Public);
	K2Schema->MarkFunctionEntryAsEditable(SignatureGraph, true);
	Blueprint->DelegateSignatureGraphs.Add(SignatureGraph);

	// 3. Add optional input parameters to the signature graph's entry node.
	const TArray<TSharedPtr<FJsonValue>>* ParamsArray = nullptr;
	if (Payload->TryGetArrayField(TEXT("params"), ParamsArray))
	{
		// Find the function entry node in the signature graph.
		UK2Node_FunctionEntry* EntryNode = nullptr;
		for (UEdGraphNode* Node : SignatureGraph->Nodes)
		{
			EntryNode = Cast<UK2Node_FunctionEntry>(Node);
			if (EntryNode != nullptr) break;
		}
		if (EntryNode != nullptr)
		{
			for (const TSharedPtr<FJsonValue>& ParamValue : *ParamsArray)
			{
				const TSharedPtr<FJsonObject>* ParamObjPtr = nullptr;
				if (!ParamValue.IsValid() || !ParamValue->TryGetObject(ParamObjPtr) || !ParamObjPtr->IsValid())
				{
					continue;
				}
				const TSharedPtr<FJsonObject>& ParamObj = *ParamObjPtr;

				FString ParamName;
				FString ParamTypeText;
				if (!ParamObj->TryGetStringField(TEXT("name"), ParamName) || ParamName.IsEmpty())
				{
					continue;
				}
				if (!ParamObj->TryGetStringField(TEXT("type"), ParamTypeText) || ParamTypeText.IsEmpty())
				{
					continue;
				}

				FEdGraphPinType ParamPinType;
				FString TypeReason;
				if (PinTypeTextToPinType(ParamTypeText, ParamPinType, TypeReason))
				{
					EntryNode->Modify();
					EntryNode->CreateUserDefinedPin(*ParamName, ParamPinType, EGPD_Output, false);
				}
			}
			// CreateUserDefinedPin already populates the pin list; calling
			// AllocateDefaultPins here would rebuild from the function flag
			// set and wipe the user-defined pins just added (the same reason
			// CustomEvent / FunctionEntry creation paths do not call it
			// after CreateUserDefinedPin). Omit it so the dispatcher keeps
			// its parameters through the compile below.
		}
	}

	FBlueprintEditorUtils::MarkBlueprintAsStructurallyModified(Blueprint);
	FKismetEditorUtilities::CompileBlueprint(Blueprint);
	Blueprint->MarkPackageDirty();

	Result->SetBoolField(TEXT("added"), true);
	Result->SetStringField(TEXT("dispatcher"), DispatcherName);
	Result->SetStringField(TEXT("path"), ObjectPath);
	return Result;
}