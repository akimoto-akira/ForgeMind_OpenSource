// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Blueprint function/macro graph creation and compile commands split out
// of ForgeMindBridgeClient_BlueprintGraph.cpp - same class, shared helpers
// live in ForgeMindBlueprintUtils.

#include "ForgeMindBridgeClient.h"

#include "ForgeMindBlueprintUtils.h"

#include "Dom/JsonObject.h"
#include "Dom/JsonValue.h"
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphNode.h"
#include "EdGraph/EdGraphPin.h"
#include "EdGraphSchema_K2.h"
#include "Engine/Blueprint.h"
#include "K2Node_FunctionEntry.h"
#include "K2Node_FunctionResult.h"
#include "K2Node_Tunnel.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "Kismet2/BlueprintEditorUtils.h"
#include "Kismet2/KismetEditorUtilities.h"

using namespace ForgeMindBlueprintUtils;

namespace
{
	// One parsed {name, type} entry of a graph signature ('params' inputs or
	// 'outputs' return values of create_function_graph / create_macro_graph).
	struct FGraphPinSpec { FName Name; FEdGraphPinType Type; };

	// Parses one JSON array of {name, type} entries into OutSpecs. Names must
	// be non-empty, unique within UsedNames (the caller seeds it with the
	// graph's reserved pin names and shares it across the param/output lists,
	// which compile into one parameter set) and resolvable through the
	// add_blueprint_variable type vocabulary. When bAllowExec is set, the
	// extra type 'exec' resolves to a PC_Exec pin (macro tunnels only). On
	// failure writes created:false + reason into Result. The field may be
	// omitted entirely (empty list).
	bool ParseSignatureSpecs(const TSharedPtr<FJsonObject>& Payload, const TCHAR* FieldName, const TCHAR* Role, TSet<FName>& UsedNames, TArray<FGraphPinSpec>& OutSpecs, const TSharedPtr<FJsonObject>& Result, bool bAllowExec = false)
	{
		const TArray<TSharedPtr<FJsonValue>>* SpecArray = nullptr;
		if (!Payload->TryGetArrayField(FieldName, SpecArray))
		{
			return true; // field omitted - empty signature
		}
		for (const TSharedPtr<FJsonValue>& SpecValue : *SpecArray)
		{
			const TSharedPtr<FJsonObject>* SpecObjPtr = nullptr;
			if (!SpecValue.IsValid() || !SpecValue->TryGetObject(SpecObjPtr) || !SpecObjPtr->IsValid())
			{
				Result->SetBoolField(TEXT("created"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Each entry in '%s' must be an object with 'name' and 'type'"), FieldName));
				return false;
			}
			const TSharedPtr<FJsonObject>& SpecObj = *SpecObjPtr;

			FString SpecName;
			FString SpecTypeText;
			if (!SpecObj->TryGetStringField(TEXT("name"), SpecName) || SpecName.TrimStartAndEnd().IsEmpty())
			{
				Result->SetBoolField(TEXT("created"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Each entry in '%s' needs a non-empty 'name'"), FieldName));
				return false;
			}
			if (!SpecObj->TryGetStringField(TEXT("type"), SpecTypeText) || SpecTypeText.TrimStartAndEnd().IsEmpty())
			{
				Result->SetBoolField(TEXT("created"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("%s '%s' needs a 'type' (same vocabulary as add_blueprint_variable: bool/int/float/string/vector/Actor/...)"), Role, *SpecName));
				return false;
			}

			const FName SpecFName(*SpecName);
			if (UsedNames.Contains(SpecFName))
			{
				Result->SetBoolField(TEXT("created"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("%s '%s' collides with a built-in pin or an earlier parameter/return value of the same name (inputs and outputs share one parameter set)"), Role, *SpecName));
				return false;
			}
			UsedNames.Add(SpecFName);

			FEdGraphPinType SpecPinType;
			if (bAllowExec && SpecTypeText.TrimStartAndEnd().Equals(TEXT("exec"), ESearchCase::IgnoreCase))
			{
				SpecPinType.PinCategory = UEdGraphSchema_K2::PC_Exec;
			}
			else
			{
				FString TypeReason;
				if (!PinTypeTextToPinType(SpecTypeText, SpecPinType, TypeReason))
				{
					Result->SetBoolField(TEXT("created"), false);
					Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("%s '%s' has an invalid type '%s': %s"), Role, *SpecName, *SpecTypeText, *TypeReason));
					return false;
				}
			}

			FGraphPinSpec Spec;
			Spec.Name = SpecFName;
			Spec.Type = SpecPinType;
			OutSpecs.Add(Spec);
		}
		return true;
	}
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleCreateFunctionGraph(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString GraphName;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("name"), GraphName) || GraphName.IsEmpty())
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path), name (function graph name)"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s'"), *AssetPath));
		return Result;
	}

	if (GraphNameExists(Blueprint, GraphName))
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("A graph named '%s' already exists in this blueprint"), *GraphName));
		return Result;
	}

	// Optional 'pure' flag: a pure function has no exec pins and is compiled
	// with FUNC_BlueprintPure (what the DSL's [Function(Pure = true)] maps to).
	bool bPure = false;
	Payload->TryGetBoolField(TEXT("pure"), bPure);


	// Optional signature: 'params' become the function's input parameters
	// (output pins on the entry node - data flows from the entry into the
	// body) and 'outputs' become its return values (input pins on the result
	// node). Both lists compile into the same UFunction parameter set, so
	// names must be unique across the two lists and clear of the exec pins'
	// reserved names. Validate every entry up front so a bad entry fails
	// cleanly before the graph is created.
	TArray<FGraphPinSpec> ParamSpecs;
	TArray<FGraphPinSpec> OutputSpecs;

	TSet<FName> UsedNames;
	UsedNames.Add(UEdGraphSchema_K2::PN_Then);    // entry's exec-out pin
	UsedNames.Add(UEdGraphSchema_K2::PN_Execute); // result's exec-in pin

	if (!ParseSignatureSpecs(Payload, TEXT("params"), TEXT("Input"), UsedNames, ParamSpecs, Result)
		|| !ParseSignatureSpecs(Payload, TEXT("outputs"), TEXT("Output"), UsedNames, OutputSpecs, Result))
	{
		return Result;
	}

	UEdGraph* Graph = NewObject<UEdGraph>(Blueprint, *GraphName);
	// FGraphNodeCreator::CreateNode -> CreateDefaultNodesForGraph ->
	// Cast<UEdGraphSchema_K2>(Graph->Schema) crashes if Schema is null.
	// Use the UClass* assignment path that UE's CreateNewGraph also uses.
	Graph->Schema = UEdGraphSchema_K2::StaticClass();

	// Entry node (exec-out + optional delegate-out).
	FGraphNodeCreator<UK2Node_FunctionEntry> EntryCreator(*Graph);
	UK2Node_FunctionEntry* EntryNode = EntryCreator.CreateNode();
	EntryNode->FunctionReference.SetExternalMember(FName(*GraphName), Blueprint->SkeletonGeneratedClass);
	// User-created functions are editable (the editor's paste flow does the
	// same). SyncWithEntryNode treats a result's user-defined pins as a
	// signature mismatch and deletes them whenever the entry is NOT editable,
	// so this flag is what keeps the return-value pins alive across compiles.
	EntryNode->bIsEditable = true;
	EntryNode->CreateNewGuid();
	EntryNode->PostPlacedNewNode();
	EntryNode->AllocateDefaultPins();
	// Attach the validated inputs as user-defined output pins (the same path
	// the editor's '+' button uses - populates UserDefinedPins, which the
	// compiler reads to build the function's parameter list).
	for (const FGraphPinSpec& Spec : ParamSpecs)
	{
		EntryNode->CreateUserDefinedPin(Spec.Name, Spec.Type, EGPD_Output, /*bUseUniqueName=*/false);
	}
	EntryNode->NodePosX = 0;
	EntryNode->NodePosY = 0;
	EntryCreator.Finalize();

	// Mirror the editor's CreateFunctionGraph bIsUserCreated path: without
	// these extra flags the compiled function is not blueprint-callable, so
	// call nodes for it are rejected ("neither blueprint-callable nor an
	// overridable event"). The compiler copies them via GetExtraFlags().
	int32 ExtraFunctionFlags = FUNC_BlueprintCallable | FUNC_BlueprintEvent | FUNC_Public;
	// Function library alignment with editor: BPTYPE_FunctionLibrary already makes IsStaticFunctionGraph
	// return true (EdGraphSchema_K2.cpp:5678 checks BlueprintType), at compile time bIsStaticFunction
	// = true auto-injects __WorldContext + sets MD_WorldContext metadata, function auto-static
	// (call node has no self pin). No longer manually adding FUNC_Static -- it would cause the entry node
	// to create __WorldContext pin through a second path (FUNC_Static check), but bIsStaticFunction
	// (which only checks BlueprintType) is false so MD_WorldContext metadata wouldn't be set,
	// creating an inconsistent state where parameter exists but metadata is missing. FunctionLibrary
	// functions also cannot be events (FUNC_BlueprintEvent), remove it to align with editor default.
	const bool bFunctionLibrary = Blueprint->ParentClass != nullptr
		&& Blueprint->ParentClass->IsChildOf(UBlueprintFunctionLibrary::StaticClass());
	if (bFunctionLibrary)
	{
		ExtraFunctionFlags = FUNC_BlueprintCallable | FUNC_Public;
	}
	if (bPure)
	{
		// FUNC_BlueprintPure is carried in the entry node's ExtraFlags (see
		// UK2Node_FunctionEntry) and ends up on the compiled UFunction.
		ExtraFunctionFlags |= FUNC_BlueprintPure;
	}
	if (const UEdGraphSchema_K2* K2Schema = Cast<UEdGraphSchema_K2>(Graph->GetSchema()))
	{
		K2Schema->AddExtraFunctionFlags(Graph, ExtraFunctionFlags);
	}

	// Return node so the graph is structurally valid (no orphan exec).
	FGraphNodeCreator<UK2Node_FunctionResult> ReturnCreator(*Graph);
	UK2Node_FunctionResult* ReturnNode = ReturnCreator.CreateNode();
	ReturnNode->FunctionReference = EntryNode->FunctionReference;
	ReturnNode->bIsEditable = true; // mirrors the entry (SyncWithEntryNode keeps them in step)
	ReturnNode->CreateNewGuid();
	ReturnNode->PostPlacedNewNode();
	ReturnNode->AllocateDefaultPins();
	// Attach the validated return values as user-defined input pins (result
	// nodes only accept inputs - the engine rejects output pins on them).
	for (const FGraphPinSpec& Spec : OutputSpecs)
	{
		ReturnNode->CreateUserDefinedPin(Spec.Name, Spec.Type, EGPD_Input, /*bUseUniqueName=*/false);
	}
	ReturnNode->NodePosX = EntryNode->NodePosX + 256;
	ReturnNode->NodePosY = EntryNode->NodePosY;
	ReturnCreator.Finalize();

	// --- Local variables (function-scoped) ---
	// "locals": [ { name, type[, defaultValue] }, ... ] maps to UE function-local
	// variables: they live on UK2Node_FunctionEntry::LocalVariables and are
	// materialised as properties of the compiled UFunction, which is what makes
	// getter/setter nodes for them resolvable. Must run after the entry node
	// exists (AddLocalVariable looks it up on the graph).
	const TArray<TSharedPtr<FJsonValue>>* LocalsArray = nullptr;
	if (Payload->TryGetArrayField(TEXT("locals"), LocalsArray) && LocalsArray != nullptr)
	{
		for (const TSharedPtr<FJsonValue>& LocalVal : *LocalsArray)
		{
			const TSharedPtr<FJsonObject>* LocalObjPtr = nullptr;
			if (!LocalVal.IsValid() || !LocalVal->TryGetObject(LocalObjPtr) || !LocalObjPtr->IsValid())
			{
				continue;
			}

			FString LocalName;
			if (!(*LocalObjPtr)->TryGetStringField(TEXT("name"), LocalName) || LocalName.IsEmpty())
			{
				continue;
			}

			FString LocalTypeText = TEXT("float");
			(*LocalObjPtr)->TryGetStringField(TEXT("type"), LocalTypeText);
			FString LocalDefault;
			(*LocalObjPtr)->TryGetStringField(TEXT("defaultValue"), LocalDefault);

			FEdGraphPinType LocalPinType;
			FString LocalTypeReason;
			if (!PinTypeTextToPinType(LocalTypeText, LocalPinType, LocalTypeReason))
			{
				Result->SetBoolField(TEXT("created"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Local variable '%s' has an invalid type '%s': %s"), *LocalName, *LocalTypeText, *LocalTypeReason));
				return Result;
			}

			FBlueprintEditorUtils::AddLocalVariable(Blueprint, Graph, FName(*LocalName), LocalPinType, LocalDefault);
		}
	}

	// Pure functions carry no exec pins. The flag alone does not reallocate
	// pins, so reconstruct both terminators to drop (or keep) them accordingly.
	if (bPure)
	{
		EntryNode->ReconstructNode();
		ReturnNode->ReconstructNode();
	}

	// Engine parity fix: UEdGraphSchema_K2::CreateFunctionGraphTerminators
	// auto-connects entry.then -> result.execute right after creating both
	// nodes, with the comment "Auto-connect the pins for entry and exit, so
	// that by default the signature is properly generated". Without this wire
	// the function body has no exec path to the result node, return-value
	// pins silently fall back to defaults, and the compiler only emits a
	// non-blocking Warning ("ReturnNode Exec pin has no connections").
	// Connect only when BOTH exec pins are unwired so user logic is preserved.
	// Pure functions have no exec path at all, so nothing is wired for them.
	if (!OutputSpecs.IsEmpty() && !bPure)
	{
		UEdGraphPin* EntryThen = EntryNode->FindPin(UEdGraphSchema_K2::PN_Then);
		UEdGraphPin* ResultExec = ReturnNode->FindPin(UEdGraphSchema_K2::PN_Execute);
		if (EntryThen != nullptr && ResultExec != nullptr
			&& EntryThen->LinkedTo.IsEmpty() && ResultExec->LinkedTo.IsEmpty())
		{
			EntryThen->MakeLinkTo(ResultExec);
		}
	}

	Blueprint->FunctionGraphs.Add(Graph);
	FBlueprintEditorUtils::MarkBlueprintAsStructurallyModified(Blueprint);
	MarkBlueprintModified(Blueprint);

	// Compile so the function becomes discoverable as a callable.
	FKismetEditorUtilities::CompileBlueprint(Blueprint);
	CompileAndCollectDiagnostics(Blueprint, Result);

	Result->SetBoolField(TEXT("created"), true);
	Result->SetStringField(TEXT("path"), AssetPath);
	Result->SetStringField(TEXT("name"), GraphName);
	Result->SetStringField(TEXT("kind"), TEXT("function"));
	Result->SetNumberField(TEXT("inputs"), ParamSpecs.Num());
	Result->SetNumberField(TEXT("outputs"), OutputSpecs.Num());

	// A function with no return value has no data output — consider creating
	// a custom event instead. Warn (non-blocking) so the caller can decide.
	if (OutputSpecs.IsEmpty() && !bPure)
	{
		TArray<TSharedPtr<FJsonValue>> Warnings;
		TSharedRef<FJsonObject> WarnEntry = MakeShared<FJsonObject>();
		WarnEntry->SetStringField(TEXT("code"), TEXT("NO_RETURN_VALUE"));
		WarnEntry->SetStringField(TEXT("message"),
			TEXT("If a function has no return value, consider creating an event instead of a function"));
		Warnings.Add(MakeShared<FJsonValueObject>(WarnEntry));
		Result->SetArrayField(TEXT("warnings"), Warnings);
	}

	return Result;
}


TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleRemoveFunctionGraph(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString GraphName;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("name"), GraphName) || GraphName.IsEmpty())
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path), name (function graph name)"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s'"), *AssetPath));
		return Result;
	}

	// Find the function graph by name. FunctionGraphs is the authoritative
	// list for user-created functions; the ubergraph and macro graphs live
	// in separate arrays, so a name clash across them is impossible.
	UEdGraph* GraphToRemove = nullptr;
	for (UEdGraph* FunctionGraph : Blueprint->FunctionGraphs)
	{
		if (FunctionGraph != nullptr && FunctionGraph->GetFName() == FName(*GraphName))
		{
			GraphToRemove = FunctionGraph;
			break;
		}
	}
	if (GraphToRemove == nullptr)
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No function graph named '%s' found in blueprint '%s' (discover graphs via list_blueprint_graph)"), *GraphName, *AssetPath));
		return Result;
	}

	// RemoveGraph handles the full teardown: breaks all pin links, removes
	// the graph from Blueprint->FunctionGraphs, deletes the entry/result
	// nodes and any user nodes, and marks the blueprint structurally
	// modified so the next compile drops the UFunction from the class.
	FBlueprintEditorUtils::RemoveGraph(Blueprint, GraphToRemove);

	FKismetEditorUtilities::CompileBlueprint(Blueprint);
	CompileAndCollectDiagnostics(Blueprint, Result);

	Result->SetBoolField(TEXT("removed"), true);
	Result->SetStringField(TEXT("path"), AssetPath);
	Result->SetStringField(TEXT("name"), GraphName);
	return Result;
}


TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleCreateMacroGraph(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString GraphName;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("name"), GraphName) || GraphName.IsEmpty())
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path), name (macro graph name)"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s'"), *AssetPath));
		return Result;
	}

	if (GraphNameExists(Blueprint, GraphName))
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("A graph named '%s' already exists in this blueprint"), *GraphName));
		return Result;
	}

	// Optional signature: 'params' become the macro's input pins on the entry
	// tunnel and 'outputs' its output pins on the exit tunnel. Unlike function
	// graphs, exec pins are legal here (UK2Node_Tunnel::CanModifyExecutionWires
	// is always true - the same path the editor's Inputs/Outputs panel uses),
	// so 'type' may also be 'exec' and a macro can carry several of them.
	// Names must be unique across both lists and clear of the default exec pin
	// names. Validate every entry up front so a bad entry fails cleanly before
	// the graph is created.
	//
	// 'exec' (bool, default true): build the standard execute/then exec pair on the
	// tunnels. A macro whose body is pure data flow (e.g. `return a + b;`) has no
	// execution flow at all and needs no exec pins - the editor's own macro with
	// only pure nodes has none either, and the DSL passes exec:false for those
	// (2026-10-04 user visual check: prefer no exec pins). With exec:false the
	// reserved-name set below stays empty so params/outputs may still use those
	// names, and the exec pair is simply not created.
	bool bWantExecPair = true;
	Payload->TryGetBoolField(TEXT("exec"), bWantExecPair);

	TArray<FGraphPinSpec> ParamSpecs;
	TArray<FGraphPinSpec> OutputSpecs;

	TSet<FName> UsedNames;
	if (bWantExecPair)
	{
		UsedNames.Add(UEdGraphSchema_K2::PN_Then);    // entry tunnel's default exec-out
		UsedNames.Add(UEdGraphSchema_K2::PN_Execute); // exit tunnel's default exec-in
	}

	auto IsExecSpec = [](const TSharedPtr<FJsonObject>& SpecObj) -> bool
	{
		FString TypeText;
		return SpecObj->TryGetStringField(TEXT("type"), TypeText)
			&& TypeText.TrimStartAndEnd().Equals(TEXT("exec"), ESearchCase::IgnoreCase);
	};

	// Count the exec pins up front (they default to then/execute and are
	// reported separately from the data pins).
	int32 ExecParamCount = 0;
	int32 ExecOutputCount = 0;
	const TArray<TSharedPtr<FJsonValue>>* PreParamArray = nullptr;
	if (Payload->TryGetArrayField(TEXT("params"), PreParamArray))
	{
		for (const TSharedPtr<FJsonValue>& V : *PreParamArray)
		{
			const TSharedPtr<FJsonObject>* ObjPtr = nullptr;
			if (V.IsValid() && V->TryGetObject(ObjPtr) && ObjPtr->IsValid() && IsExecSpec(*ObjPtr)) { ++ExecParamCount; }
		}
	}
	const TArray<TSharedPtr<FJsonValue>>* PreOutputArray = nullptr;
	if (Payload->TryGetArrayField(TEXT("outputs"), PreOutputArray))
	{
		for (const TSharedPtr<FJsonValue>& V : *PreOutputArray)
		{
			const TSharedPtr<FJsonObject>* ObjPtr = nullptr;
			if (V.IsValid() && V->TryGetObject(ObjPtr) && ObjPtr->IsValid() && IsExecSpec(*ObjPtr)) { ++ExecOutputCount; }
		}
	}

	// Full validation via the shared parser; macro tunnels additionally
	// accept the 'exec' type (multiple exec pins are legal on a macro).
	if (!ParseSignatureSpecs(Payload, TEXT("params"), TEXT("Input"), UsedNames, ParamSpecs, Result, /*bAllowExec=*/true)
		|| !ParseSignatureSpecs(Payload, TEXT("outputs"), TEXT("Output"), UsedNames, OutputSpecs, Result, /*bAllowExec=*/true))
	{
		return Result;
	}

	UEdGraph* Graph = NewObject<UEdGraph>(Blueprint, *GraphName);
	// Ensure Schema is set before AddMacroGraph potentially creates nodes.
	Graph->Schema = UEdGraphSchema_K2::StaticClass();
	FBlueprintEditorUtils::AddMacroGraph(Blueprint, Graph, /*bIsUserCreated=*/true, /*SignatureFromClass=*/nullptr);

	// Locate the entry/exit tunnels that AddMacroGraph just created.
	UK2Node_Tunnel* EntryTunnel = nullptr;
	UK2Node_Tunnel* ExitTunnel = nullptr;
	for (UEdGraphNode* Node : Graph->Nodes)
	{
		UK2Node_Tunnel* Tunnel = Cast<UK2Node_Tunnel>(Node);
		if (Tunnel == nullptr || Tunnel->GetClass() != UK2Node_Tunnel::StaticClass())
		{
			continue;
		}
		if (Tunnel->bCanHaveOutputs) { EntryTunnel = Tunnel; }
		else if (Tunnel->bCanHaveInputs) { ExitTunnel = Tunnel; }
	}
	if (EntryTunnel == nullptr || ExitTunnel == nullptr)
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), TEXT("The macro graph was created but its entry/exit tunnels could not be located"));
		return Result;
	}

	// A bare macro has no pins at all - give it the standard exec pair so it is
	// immediately usable. The macro instance copies every tunnel pin flipped,
	// so entry's exec output is named 'execute' and exit's exec input 'then' -
	// this makes the macro instance pins match the regular node convention
	// (execute = input, then = output), avoiding direction-mismatch surprises.
	// Pure-data-flow macros opt out via 'exec':false and get data pins only.
	FEdGraphPinType ExecPinType;
	ExecPinType.PinCategory = UEdGraphSchema_K2::PC_Exec;
	if (bWantExecPair)
	{
		EntryTunnel->CreateUserDefinedPin(UEdGraphSchema_K2::PN_Execute, ExecPinType, EGPD_Output, /*bUseUniqueName=*/false);
		ExitTunnel->CreateUserDefinedPin(UEdGraphSchema_K2::PN_Then, ExecPinType, EGPD_Input, /*bUseUniqueName=*/false);
	}

	// Attach the validated signature - entry tunnel output pins (exec + data
	// params), exit tunnel input pins (exec + data outputs).
	for (const FGraphPinSpec& Spec : ParamSpecs)
	{
		EntryTunnel->CreateUserDefinedPin(Spec.Name, Spec.Type, EGPD_Output, /*bUseUniqueName=*/false);
	}
	for (const FGraphPinSpec& Spec : OutputSpecs)
	{
		ExitTunnel->CreateUserDefinedPin(Spec.Name, Spec.Type, EGPD_Input, /*bUseUniqueName=*/false);
	}

	MarkBlueprintModified(Blueprint);

	FKismetEditorUtilities::CompileBlueprint(Blueprint);
	CompileAndCollectDiagnostics(Blueprint, Result);

	Result->SetBoolField(TEXT("created"), true);
	Result->SetStringField(TEXT("path"), AssetPath);
	Result->SetStringField(TEXT("name"), GraphName);
	Result->SetStringField(TEXT("kind"), TEXT("macro"));
	Result->SetNumberField(TEXT("inputs"), ParamSpecs.Num());
	Result->SetNumberField(TEXT("outputs"), OutputSpecs.Num());
	Result->SetNumberField(TEXT("execInputs"), (bWantExecPair ? 1 : 0) + ExecParamCount);
	Result->SetNumberField(TEXT("execOutputs"), (bWantExecPair ? 1 : 0) + ExecOutputCount);
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleCompileBlueprint(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty())
	{
		Result->SetBoolField(TEXT("compiled"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path, e.g. /Game/Blueprints/BP_Thing); optional full: true to force a full recompile instead of an incremental one"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("compiled"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s' (create one via create_blueprint first)"), *AssetPath));
		return Result;
	}

	// Optional: force a full (non-incremental) recompile. The editor's
	// default path is incremental; full recompiles are slower but catch
	// stale-state issues incremental mode may miss.
	bool bFullRecompile = false;
	bool bHasKey = false;
	FString FullReason;
	if (!TryGetOptionalBool(Payload, TEXT("full"), bFullRecompile, bHasKey, FullReason))
	{
		Result->SetBoolField(TEXT("compiled"), false);
		Result->SetStringField(TEXT("reason"), FullReason);
		return Result;
	}

	if (bFullRecompile)
	{
		// MarkBlueprintAsStructurallyModified is also called inside
		// CompileAndCollectDiagnostics, but setting it here first ensures
		// the compile treats the blueprint as structurally changed (full
		// recompile) even if an intermediate process cleared the flag.
		FBlueprintEditorUtils::MarkBlueprintAsStructurallyModified(Blueprint);
	}

	// CompileAndCollectDiagnostics internally calls MarkBlueprintAsStructurallyModified
	// + CompileBlueprint + MarkPackageDirty, then harvests per-node compiler
	// messages (Error/Warning) into a structured array for self-checking.
	CompileAndCollectDiagnostics(Blueprint, Result);

	// Summarize severity counts so the caller can quickly tell whether
	// the compile was clean or had non-blocking warnings.
	int32 ErrorCount = 0;
	int32 WarningCount = 0;
	const TArray<TSharedPtr<FJsonValue>>* Messages = nullptr;
	if (Result->TryGetArrayField(TEXT("compileMessages"), Messages))
	{
		for (const TSharedPtr<FJsonValue>& MsgValue : *Messages)
		{
			const TSharedPtr<FJsonObject>* MsgObjPtr = nullptr;
			if (MsgValue.IsValid() && MsgValue->TryGetObject(MsgObjPtr) && MsgObjPtr->IsValid())
			{
				int32 Severity = 0;
				(*MsgObjPtr)->TryGetNumberField(TEXT("severity"), Severity);
				if (Severity == 0) { ++ErrorCount; }
				else { ++WarningCount; }
			}
		}
	}

	Result->SetBoolField(TEXT("compiled"), ErrorCount == 0);
	Result->SetStringField(TEXT("path"), Blueprint->GetPathName());
	Result->SetNumberField(TEXT("errors"), ErrorCount);
	Result->SetNumberField(TEXT("warnings"), WarningCount);
	return Result;
}