// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Blueprint domain IR handler.
//
// Covers assetType "blueprint" plus the two user-defined types the
// blueprint translator also owns ("user_defined_struct",
// "user_defined_enum"). The heavy lifting delegates to the proven
// per-command implementations on the bridge client
// (create_blueprint, add_blueprint_variable, add_blueprint_component,
// create_function_graph, add_blueprint_node, connect_blueprint_pins,
// compile_blueprint, ...) so IR application and the MCP actions share
// exactly one code path.

#include "ForgeMindIRHandler.h"

#include "ForgeMindBlueprintUtils.h"
#include "ForgeMindBridgeClient.h"

#include "Engine/Blueprint.h"
#include "Engine/BlueprintGeneratedClass.h"
#include "Kismet2/StructureEditorUtils.h"
#include "UserDefinedStructure/UserDefinedStructEditorData.h"
#include "Engine/SCS_Node.h"
#include "Engine/SimpleConstructionScript.h"
#include "Engine/UserDefinedEnum.h"
#include "Kismet2/BlueprintEditorUtils.h"
#include "Kismet2/StructureEditorUtils.h"
#include "Engine/UserDefinedStruct.h"
#include "UObject/UnrealType.h"
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphNode.h"
#include "EdGraph/EdGraphPin.h"
#include "EdGraphSchema_K2.h"
#include "K2Node_FunctionEntry.h"
#include "K2Node_FunctionResult.h"
#include "K2Node_Tunnel.h"
#include "K2Node_MathExpression.h"

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

	UBlueprint* AsBlueprint(UObject* Asset)
	{
		return Cast<UBlueprint>(Asset);
	}

	// ---------------------------------------------------------------------------
	// IR grading helper functions (IR grading rules.md)
	// ---------------------------------------------------------------------------

	/**
	 * When LoadAsset returns a UBlueprintGeneratedClass or UPackage instead of
	 * the UBlueprint source object, resolve the actual UBlueprint via the
	 * "<PackagePath>.<AssetName>" convention.
	 */
	UBlueprint* ResolveBlueprintFromPath(const FString& Path)
	{
		const FString Normalized = ForgeMindIR::NormalizePath(Path);
		const FString AssetName = ForgeMindIR::AssetNameOf(Normalized);
		// Try "<Path>.<AssetName>" form first (the canonical UBlueprint object path).
		const FString ObjectForm = Normalized + TEXT(".") + AssetName;
		if (UBlueprint* BP = LoadObject<UBlueprint>(nullptr, *ObjectForm))
		{
			return BP;
		}
		// Fallback: maybe the path already includes the object suffix.
		if (UBlueprint* BP = LoadObject<UBlueprint>(nullptr, *Normalized))
		{
			return BP;
		}
		return nullptr;
	}

	/** Serializes one pin's type+name+direction for function signatures (summary level). */
	TSharedRef<FJsonObject> PinToSummary(const UEdGraphPin* Pin)
	{
		TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
		Entry->SetStringField(TEXT("name"), Pin->PinName.ToString());
		Entry->SetStringField(TEXT("type"), ForgeMindBlueprintUtils::PinTypeToText(Pin->PinType));
		Entry->SetStringField(TEXT("direction"), Pin->Direction == EGPD_Input ? TEXT("input") : TEXT("output"));
		return Entry;
	}

	/** Extracts function inputs/outputs from the entry node's pins (summary level). */
	void ExtractFunctionSignature(UEdGraph* Graph, TArray<TSharedPtr<FJsonValue>>& OutInputs, TArray<TSharedPtr<FJsonValue>>& OutOutputs)
	{
		for (UEdGraphNode* Node : Graph->Nodes)
		{
			if (Node == nullptr) { continue; }
			if (UK2Node_FunctionEntry* Entry = Cast<UK2Node_FunctionEntry>(Node))
			{
				for (UEdGraphPin* Pin : Entry->Pins)
				{
					if (Pin == nullptr || Pin->bHidden) { continue; }
					if (Pin->Direction == EGPD_Output && Pin->PinType.PinCategory != UEdGraphSchema_K2::PC_Exec)
					{
						OutInputs.Add(MakeShared<FJsonValueObject>(PinToSummary(Pin)));
					}
				}
			}
			else if (UK2Node_FunctionResult* Result = Cast<UK2Node_FunctionResult>(Node))
			{
				for (UEdGraphPin* Pin : Result->Pins)
				{
					if (Pin == nullptr || Pin->bHidden) { continue; }
					if (Pin->Direction == EGPD_Input && Pin->PinType.PinCategory != UEdGraphSchema_K2::PC_Exec)
					{
						OutOutputs.Add(MakeShared<FJsonValueObject>(PinToSummary(Pin)));
					}
				}
			}
		}
	}

	/**
	 * Function local variables (stored in UK2Node_FunctionEntry::LocalVariables, not in pins).
	 * Same shape as apply-side functions[].locals: {name, type, defaultValue?}.
	 * VarGuid not exported -- changes every time asset is rebuilt, including it in
	 * snapshots would cause cross-run false positives.
	 */
	void ExtractFunctionLocals(UEdGraph* Graph, TArray<TSharedPtr<FJsonValue>>& OutLocals)
	{
		for (UEdGraphNode* Node : Graph->Nodes)
		{
			UK2Node_FunctionEntry* Entry = Cast<UK2Node_FunctionEntry>(Node);
			if (Entry == nullptr) { continue; }
			for (const FBPVariableDescription& Local : Entry->LocalVariables)
			{
				TSharedRef<FJsonObject> Entry2 = MakeShared<FJsonObject>();
				Entry2->SetStringField(TEXT("name"), Local.VarName.ToString());
				Entry2->SetStringField(TEXT("type"), ForgeMindBlueprintUtils::PinTypeToText(Local.VarType));
				if (!Local.DefaultValue.IsEmpty())
				{
					Entry2->SetStringField(TEXT("defaultValue"), Local.DefaultValue);
				}
				OutLocals.Add(MakeShared<FJsonValueObject>(Entry2));
			}
		}
	}

	/**
	 * Macro graph signature: entry tunnel (bCanHaveOutputs) output pins = macro inputs,
	 * exit tunnel (bCanHaveInputs) input pins = macro outputs. exec pins not counted
	 * (consistent with ExtractFunctionSignature filtering exec).
	 */
	void ExtractMacroSignature(UEdGraph* Graph, TArray<TSharedPtr<FJsonValue>>& OutInputs, TArray<TSharedPtr<FJsonValue>>& OutOutputs)
	{
		for (UEdGraphNode* Node : Graph->Nodes)
		{
			UK2Node_Tunnel* Tunnel = Cast<UK2Node_Tunnel>(Node);
			if (Tunnel == nullptr || Tunnel->GetClass() != UK2Node_Tunnel::StaticClass()) { continue; }
			for (UEdGraphPin* Pin : Tunnel->Pins)
			{
				if (Pin == nullptr || Pin->bHidden) { continue; }
				if (Pin->PinType.PinCategory == UEdGraphSchema_K2::PC_Exec) { continue; }
				if (Tunnel->bCanHaveOutputs && Pin->Direction == EGPD_Output)
				{
					OutInputs.Add(MakeShared<FJsonValueObject>(PinToSummary(Pin)));
				}
				else if (!Tunnel->bCanHaveOutputs && Pin->Direction == EGPD_Input)
				{
					OutOutputs.Add(MakeShared<FJsonValueObject>(PinToSummary(Pin)));
				}
			}
		}
	}

	TSharedRef<FJsonObject> GraphToIR(const UEdGraph* Graph, const FString& GraphName, const FString& Kind, const FString& Level);

	/** Serializes a single node for graph/full level (node id, class, pins, optional coords). */
	TSharedRef<FJsonObject> NodeToJson(const UEdGraphNode* Node, bool bIncludeCoords)
	{
		TSharedRef<FJsonObject> NodeEntry = MakeShared<FJsonObject>();
		NodeEntry->SetStringField(TEXT("id"), Node->NodeGuid.ToString());
		NodeEntry->SetStringField(TEXT("type"), Node->GetClass()->GetName());
		NodeEntry->SetStringField(TEXT("title"), Node->GetNodeTitle(ENodeTitleType::FullTitle).ToString());
		if (bIncludeCoords)
		{
			NodeEntry->SetNumberField(TEXT("posX"), Node->NodePosX);
			NodeEntry->SetNumberField(TEXT("posY"), Node->NodePosY);
		}
		TArray<TSharedPtr<FJsonValue>> PinEntries;
		for (const UEdGraphPin* Pin : Node->Pins)
		{
			if (Pin == nullptr || Pin->bHidden) { continue; }
			TSharedRef<FJsonObject> PinEntry = MakeShared<FJsonObject>();
			PinEntry->SetStringField(TEXT("name"), Pin->PinName.ToString());
			PinEntry->SetStringField(TEXT("direction"), Pin->Direction == EGPD_Input ? TEXT("input") : TEXT("output"));
			PinEntry->SetStringField(TEXT("type"), ForgeMindBlueprintUtils::PinTypeToText(Pin->PinType));
			if (bIncludeCoords && !Pin->DefaultValue.IsEmpty())
			{
				PinEntry->SetStringField(TEXT("defaultValue"), Pin->DefaultValue);
			}
			PinEntries.Add(MakeShared<FJsonValueObject>(PinEntry));
		}
		NodeEntry->SetArrayField(TEXT("pins"), PinEntries);
		if (const UK2Node_MathExpression* Math = Cast<UK2Node_MathExpression>(Node))
		{
			NodeEntry->SetStringField(TEXT("expression"), Math->Expression);
			if (Math->BoundGraph != nullptr)
			{
				NodeEntry->SetObjectField(TEXT("subgraph"), GraphToIR(Math->BoundGraph, TEXT("MathExpression"), TEXT("math"), bIncludeCoords ? TEXT("full") : TEXT("graph")));
			}
		}
		return NodeEntry;
	}

	/** Serializes a graph (nodes + connections) for graph/full level. */
	TSharedRef<FJsonObject> GraphToIR(const UEdGraph* Graph, const FString& GraphName, const FString& Kind, const FString& Level)
	{
		TSharedRef<FJsonObject> GraphEntry = MakeShared<FJsonObject>();
		GraphEntry->SetStringField(TEXT("name"), GraphName);
		GraphEntry->SetStringField(TEXT("kind"), Kind);

		const bool bIncludeCoords = (Level == TEXT("full"));
		TArray<TSharedPtr<FJsonValue>> NodeEntries;
		TArray<TSharedPtr<FJsonValue>> Connections;
		for (const UEdGraphNode* Node : Graph->Nodes)
		{
			if (Node == nullptr) { continue; }
			NodeEntries.Add(MakeShared<FJsonValueObject>(NodeToJson(Node, bIncludeCoords)));
			for (const UEdGraphPin* Pin : Node->Pins)
			{
				if (Pin == nullptr || Pin->bHidden) { continue; }
				if (Pin->Direction == EGPD_Output)
				{
					for (const UEdGraphPin* Linked : Pin->LinkedTo)
					{
						if (Linked == nullptr || Linked->GetOwningNodeUnchecked() == nullptr) { continue; }
						TSharedRef<FJsonObject> Wire = MakeShared<FJsonObject>();
						Wire->SetStringField(TEXT("fromNode"), Node->NodeGuid.ToString());
						Wire->SetStringField(TEXT("fromPin"), Pin->PinName.ToString());
						Wire->SetStringField(TEXT("toNode"), Linked->GetOwningNodeUnchecked()->NodeGuid.ToString());
						Wire->SetStringField(TEXT("toPin"), Linked->PinName.ToString());
						Connections.Add(MakeShared<FJsonValueObject>(Wire));
					}
				}
			}
		}
		GraphEntry->SetArrayField(TEXT("nodes"), NodeEntries);
		GraphEntry->SetArrayField(TEXT("connections"), Connections);
		return GraphEntry;
	}

	// Existence follows the engine's own rule (a name visible anywhere in the
	// class chain counts, FBlueprintEditorUtils::GetClassVariableList) instead
	// of only this blueprint's own NewVariables: AddMemberVariable rejects
	// inherited names and HandleUpdateBlueprintVariable knows how to override
	// an inherited default, so inferring "replace" for them is what actually
	// reaches the intended result.
	bool FindVariable(UBlueprint* Blueprint, const FString& Name)
	{
		return ForgeMindBlueprintUtils::LookupBlueprintVariable(Blueprint, Name).bNameTaken;
	}

	bool FindComponent(UBlueprint* Blueprint, const FString& Name)
	{
		if (Blueprint == nullptr || Blueprint->SimpleConstructionScript == nullptr)
		{
			return false;
		}
		for (USCS_Node* Node : Blueprint->SimpleConstructionScript->GetAllNodes())
		{
			if (Node != nullptr && Node->GetVariableName().ToString().Equals(Name, ESearchCase::IgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	UEdGraph* FindFunctionGraphPtr(UBlueprint* Blueprint, const FString& Name)
	{
		if (Blueprint == nullptr)
		{
			return nullptr;
		}
		// Function-like lookup (FunctionGraphs + interface implementation graphs, which
		// live on ImplementedInterfaces[].Graphs and never on FunctionGraphs). A graph for
		// an implemented message already exists and must be found here -- otherwise its
		// nodes land nowhere and every connection fails with "connection references an
		// unresolved node".
		// Must NOT include macro graphs: callers probe this first and fall through to
		// FindMacroGraphPtr, which is what maps a macro body onto its tunnel nodes.
		TArray<UEdGraph*> Graphs;
		ForgeMindBlueprintUtils::CollectFunctionLikeGraphs(Blueprint, Graphs);
		for (UEdGraph* Graph : Graphs)
		{
			if (Graph != nullptr && Graph->GetName().Equals(Name, ESearchCase::IgnoreCase))
			{
				return Graph;
			}
		}
		return nullptr;
	}

	bool FindFunctionGraph(UBlueprint* Blueprint, const FString& Name)
	{
		return FindFunctionGraphPtr(Blueprint, Name) != nullptr;
	}

	TSharedPtr<FJsonObject> MacroShellPayload(const FString& Path, const TSharedPtr<FJsonObject>& Graph)
	{
		TSharedPtr<FJsonObject> Payload = MakePayload(Path);
		Payload->SetStringField(TEXT("name"), Graph->GetStringField(TEXT("name")));
		const TArray<TSharedPtr<FJsonValue>>* Nodes = nullptr;
		if (Graph->TryGetArrayField(TEXT("nodes"), Nodes))
		{
			for (const auto& Value : *Nodes)
			{
				const auto Entry = Value->AsObject();
				FString Type;
				if (!Entry.IsValid() || !Entry->TryGetStringField(TEXT("type"), Type) || Type != TEXT("FunctionEntry")) continue;
				bool bExec = true;
				Entry->TryGetBoolField(TEXT("exec"), bExec);
				Payload->SetBoolField(TEXT("exec"), bExec);
				TArray<TSharedPtr<FJsonValue>> Inputs, Outputs;
				const TArray<TSharedPtr<FJsonValue>>* Pins = nullptr;
				if (Entry->TryGetArrayField(TEXT("pins"), Pins))
				{
					for (const auto& PinValue : *Pins)
					{
						const auto Pin = PinValue->AsObject();
						if (!Pin.IsValid()) continue;
						auto Spec = MakeShared<FJsonObject>();
						Spec->SetStringField(TEXT("name"), Pin->GetStringField(TEXT("id")));
						Spec->SetStringField(TEXT("type"), Pin->GetStringField(TEXT("type")));
						if (Pin->GetStringField(TEXT("direction")) == TEXT("input")) Inputs.Add(MakeShared<FJsonValueObject>(Spec));
						else Outputs.Add(MakeShared<FJsonValueObject>(Spec));
					}
				}
				Payload->SetArrayField(TEXT("params"), Inputs);
				Payload->SetArrayField(TEXT("outputs"), Outputs);
				break;
			}
		}
		return Payload;
	}

	UEdGraph* FindMacroGraphPtr(UBlueprint* Blueprint, const FString& Name)
	{
		if (Blueprint == nullptr)
		{
			return nullptr;
		}
		for (UEdGraph* Graph : Blueprint->MacroGraphs)
		{
			if (Graph != nullptr && Graph->GetName().Equals(Name, ESearchCase::IgnoreCase))
			{
				return Graph;
			}
		}
		return nullptr;
	}

/** IR node type → add_blueprint_node "kind". */
FString NodeKindFromType(const FString& NodeType, const FString& NodeClass)
{
if (NodeType == TEXT("VariableGet")) return TEXT("getter");
if (NodeType == TEXT("VariableSet")) return TEXT("setter");
// Event graph local variable step-by-step node pair: TemporaryVariable (read, out pin Variable) +
// AssignmentStatement (write, in pin Variable/Value). Falling into default "function" would
// look up the same-named UFunction by name, definitely not found (same failure as loop macros).
if (NodeType == TEXT("TemporaryVariable")) return TEXT("temporaryVariable");
if (NodeType == TEXT("AssignmentStatement")) return TEXT("assignment");
// ForLoop / ForEachLoop / WhileLoop / ForLoopWithBreak / ForEachLoopWithBreak are
// macro instances in StandardMacros (neither K2Node class nor UFunction), must be
// created with kind=macro; falling into default "function" would look up the same-named
// UFunction by name, definitely not found and the entire loop node is lost (no macro pins,
// every connection to it reports BP_PIN_TYPE_MISMATCH; visually the blueprint shows "function error").
if (NodeType == TEXT("MacroInstance") || NodeType == TEXT("ForLoop")
	|| NodeType == TEXT("ForLoopWithBreak") || NodeType == TEXT("ForEachLoop")
	|| NodeType == TEXT("ForEachLoopWithBreak") || NodeType == TEXT("WhileLoop")
	|| NodeType == TEXT("FlipFlop") || NodeType == TEXT("DoOnce")
	|| NodeType == TEXT("DoN") || NodeType == TEXT("Do N")
	|| NodeType == TEXT("Gate"))
	return TEXT("macro");
if (NodeType == TEXT("Self")) return TEXT("self");
// Subsystem getter (UK2Node_GetSubsystem): the DSL emits it as the receiver of a
// subsystem-qualified call (EnhancedInputLocalPlayerSubsystem.AddMappingContext(...)),
// because such a function needs the subsystem object as self and cannot be a static call.
// Missing this branch made the node fall through to "function", where the class lookup on
// the bare name fails - the same "new form, missed one dispatch place" trap as Timeline above.
if (NodeType == TEXT("Subsystem")) return TEXT("subsystem");
if (NodeType == TEXT("MathExpression")) return TEXT("mathexpression");
// Call parent function implementation in subclass (DSL `base.X()`): must be built as callParentFunction
// UK2Node_CallParentFunction. Previously missing this branch -> falls into default "function", creates
// a plain K2Node_CallFunction (also with class="base.X()"), neither has exec pins of parent impl
// nor can resolve the function: all connections BP_PIN_TYPE_MISMATCH, execution chain bypasses it
// entirely -- visually "parent call not created" (Parent_Begin_B / Parent_Tick_B).
// Also: base.Greet() "seems to work" because child class has same-named Greet itself; degrades to
// resolving its **own** function (semantically becomes self-call, not parent call).
if (NodeType == TEXT("CallParentFunction")) return TEXT("callParentFunction");
if (NodeType == TEXT("CallDelegate")) return TEXT("callDelegate");
if (NodeType == TEXT("AddDelegate")) return TEXT("addDelegate");
if (NodeType == TEXT("RemoveDelegate")) return TEXT("removeDelegate");
// Level blueprint (level_blueprint) references existing Actor in level: DSL layer produces
// type="LevelLiteral" (UK2Node_Literal, sole data output pin "Value"),
// bridge's add_blueprint_node creates by kind=levelliteral -- previously missing this
// branch would fall into default "function", looking up node name as UFunction would fail.
if (NodeType == TEXT("LevelLiteral")) return TEXT("levelliteral");
// Timeline node (UK2Node_Timeline): the bridge's "timeline" kind creates the
// blueprint's UTimelineTemplate + node with a default float track. Missing this
// branch falls into default "function": the node name is looked up as a UFunction
// (bSelfMember deferral), but no such function exists (the DSL [Timeline] method is
// lowered to graph nodes, never compiled as a function), so the call node stays
// unresolved - zero pins after compile, add reports BP_NODE_NOT_FOUND and every
// connection to the node fails (run 20261012_2045, Time_Float_B node_001).
if (NodeType == TEXT("Timeline")) return TEXT("timeline");
		if (NodeType == TEXT("Event")) return TEXT("event");
		if (NodeType == TEXT("InputAction")) return TEXT("inputAction");
		// Enhanced Input action event (UK2Node_EnhancedInputAction): a specialized event
		// node, not a UFunction call - BindAction has no UFUNCTION in 5.0.3, so the
		// fall-through to "function" below would leave an unresolved zero-pin node.
		if (NodeType == TEXT("EnhancedInputAction")) return TEXT("enhancedInputAction");
		if (NodeType == TEXT("InputAxis")) return TEXT("inputAxis");
		if (NodeType == TEXT("InputKey")) return TEXT("inputkey");
		if (NodeType == TEXT("CustomEvent")) return TEXT("customevent");
		if (NodeClass == TEXT("BeginPlay") || NodeClass == TEXT("Tick")
			|| NodeClass == TEXT("ActorBeginOverlap") || NodeClass == TEXT("ActorHit"))
		{
			return TEXT("event");
		}
		return TEXT("function");
	}

	/**
	 * Pin default values rejected at node creation (wildcard pins: type propagates only after
	 * driving connections are made, e.g. KismetArrayLibrary::Array_Add's NewItem depends on TargetArray).
	 * Collected and replayed once connections complete (ApplyDeferredPinDefaults).
	 */
	struct FDeferredPinDefault
	{
		FString Path;
		FString NodeId;
		FString Pin;
		FString Value;
	};

	/** Adds one graph node and records its real UE node guid. */
	bool AddGraphNode(const FString& Path, const FString& GraphName, const TSharedPtr<FJsonObject>& Node,
		TMap<FString, FString>& DslIdToNodeId, TArray<FIRWarning>& Warnings, int32& OutNodeCount,
		TArray<FDeferredPinDefault>& OutDeferredDefaults,
		TMap<FString, TMap<FString, FString>>* DslPinRemap = nullptr)
	{
		FString NodeId, NodeType, NodeName, NodeClass;
		Node->TryGetStringField(TEXT("id"), NodeId);
		Node->TryGetStringField(TEXT("type"), NodeType);
		Node->TryGetStringField(TEXT("name"), NodeName);
		Node->TryGetStringField(TEXT("class"), NodeClass);

		if (NodeId.IsEmpty() || NodeType.IsEmpty())
		{
			Warnings.Add({ TEXT("BP_NODE_NOT_FOUND"), TEXT("graph node missing id or type") });
			return false;
		}
		if (NodeType == TEXT("FunctionEntry") || NodeType == TEXT("FunctionResult"))
		{
			// The function/macro shell already owns these nodes — do not
			// create a second one. But DSL connections reference them
			// (entry.then → first statement, data → result inputs), so map
			// the DSL id onto the real node guid; otherwise every connection
			// anchored on the entry is dropped as "unresolved node".
			if (UBlueprint* Blueprint = ForgeMindBlueprintUtils::ResolveBlueprint(Path))
			{
				const bool bEntry = (NodeType == TEXT("FunctionEntry"));
				// Function graphs own UK2Node_FunctionEntry/Result.
				if (UEdGraph* Graph = FindFunctionGraphPtr(Blueprint, GraphName))
				{
					for (UEdGraphNode* GraphNode : Graph->Nodes)
					{
						if (GraphNode == nullptr) { continue; }
						const bool bMatch = bEntry
							? GraphNode->IsA<UK2Node_FunctionEntry>()
							: GraphNode->IsA<UK2Node_FunctionResult>();
						if (bMatch)
						{
							DslIdToNodeId.Add(NodeId, GraphNode->NodeGuid.ToString());
							break;
						}
					}
				}
				// Macro graphs own UK2Node_Tunnel (entry has bCanHaveOutputs,
				// exit has bCanHaveInputs). The DSL emits FunctionEntry/
				// FunctionResult for [Macro] methods, so map them onto the
				// tunnel nodes. Pin names differ: DSL uses then/execute
				// (function convention), tunnels use execute/then (macro
				// convention after the pin-direction fix). Record a remap
				// so ConnectGraph translates pin names on the fly.
				else if (UEdGraph* MacroGraph = FindMacroGraphPtr(Blueprint, GraphName))
				{
					for (UEdGraphNode* GraphNode : MacroGraph->Nodes)
					{
						UK2Node_Tunnel* Tunnel = Cast<UK2Node_Tunnel>(GraphNode);
						if (Tunnel == nullptr || Tunnel->GetClass() != UK2Node_Tunnel::StaticClass())
						{
							continue;
						}
						const bool bMatch = bEntry
							? Tunnel->bCanHaveOutputs
							: Tunnel->bCanHaveInputs;
						if (bMatch)
						{
							DslIdToNodeId.Add(NodeId, GraphNode->NodeGuid.ToString());
							if (DslPinRemap != nullptr)
							{
								TMap<FString, FString>& Remap = (*DslPinRemap).FindOrAdd(NodeId);
								if (bEntry)
								{
									// DSL entry exec-out is "then"; tunnel's is "execute".
									Remap.Add(UEdGraphSchema_K2::PN_Then.ToString(), UEdGraphSchema_K2::PN_Execute.ToString());
								}
								else
								{
									// DSL result exec-in is "execute"; tunnel's is "then".
									Remap.Add(UEdGraphSchema_K2::PN_Execute.ToString(), UEdGraphSchema_K2::PN_Then.ToString());
								}
							}
							break;
						}
					}
				}
			}
			return false;
		}

		FForgeMindBridgeClient* Client = Bridge();
		if (Client == nullptr)
		{
			return false;
		}

		TSharedPtr<FJsonObject> Payload = MakePayload(Path);
		Payload->SetStringField(TEXT("kind"), NodeKindFromType(NodeType, NodeClass));
		Payload->SetStringField(TEXT("graphName"), GraphName);
		Payload->SetStringField(TEXT("name"), NodeName.IsEmpty() ? NodeClass : NodeName);
		FString Expression;
		if (Node->TryGetStringField(TEXT("expression"), Expression))
			Payload->SetStringField(TEXT("expression"), Expression);
		// Parent call node carries no class: parent impl resolved by bridge from blueprint ParentClass
		// (same shape as A-side MCP stepping); attaching nodeClass (e.g. "base.BeginPlay()") would make
		// bridge look up same-named UClass and fail.
		const bool bParentCall = NodeType == TEXT("CallParentFunction");
		if (!NodeClass.IsEmpty() && !bParentCall)
		{
			Payload->SetStringField(TEXT("class"), NodeClass);
		}
		// Cast node: targetClass drives ConfigureNativeFlowNode to set TargetType before
		// AllocateDefaultPins; only then does the node have a typed
		// Cast result output pin (missing = "bad type cast node"; Object input
		// stays wildcard, all connections fail).
		FString TargetClassName;
		if (Node->TryGetStringField(TEXT("targetClass"), TargetClassName) && !TargetClassName.IsEmpty())
		{
			Payload->SetStringField(TEXT("targetClass"), TargetClassName);
		}
		// Pure Cast (DSL's `x as T`): does not occupy execution flow, no exec pins, failure just
		// outputs null. Distinguished from execution Cast for `if (x is T v)` (then / CastFailed branching).
		bool bPureCast = false;
		if (Node->TryGetBoolField(TEXT("pureCast"), bPureCast) && bPureCast)
		{
			Payload->SetBoolField(TEXT("pureCast"), true);
		}
		// CustomEvent node: params ([{name, type}, ...]) drives event
		// parameter output pins (user-defined pins). Missing it, the event function has no parameters;
		// cross-blueprint call nodes also lack parameter pins; entry->downstream data lines all disconnected.
		const TArray<TSharedPtr<FJsonValue>>* NodeParams = nullptr;
		if (Node->TryGetArrayField(TEXT("params"), NodeParams) && NodeParams->Num() > 0)
		{
			Payload->SetArrayField(TEXT("params"), *NodeParams);
		}
		// Custom event RPC: forward net-mode fields (rpc/reliable) to HandleAddBlueprintNode
		// so it sets UK2Node_CustomEvent::FunctionFlags (FUNC_Net | direction | Reliable).
		FString NodeRpc;
		if (Node->TryGetStringField(TEXT("rpc"), NodeRpc) && !NodeRpc.IsEmpty())
		{
			Payload->SetStringField(TEXT("rpc"), NodeRpc);
			bool bNodeReliable = false;
			if (Node->TryGetBoolField(TEXT("reliable"), bNodeReliable))
			{
				Payload->SetBoolField(TEXT("reliable"), bNodeReliable);
			}
		}
		const TSharedPtr<FJsonObject>* Props = nullptr;
		if (Node->TryGetObjectField(TEXT("properties"), Props) && Props->IsValid())
		{
			Payload->SetObjectField(TEXT("properties"), *Props);
		}
		// CreateDelegate: binds target event function name -- ConfigureNativeFlowNode uses it to
		// SetFunction; missing it, the node has no delegate signature, output pin cannot connect to Delegate pin.
		FString FunctionName;
		if (Node->TryGetStringField(TEXT("function"), FunctionName) && !FunctionName.IsEmpty())
		{
			Payload->SetStringField(TEXT("function"), FunctionName);
		}
		// InputAction event type (IE_Pressed / IE_Released): DSL [InputAction("X", Event="Pressed")]
		// sets this on the IR node; the bridge passes it through but UK2Node_InputAction
		// natively carries both "Pressed"/"Released" pins — the field selects which pin to wire.
		FString InputEventField;
		if (Node->TryGetStringField(TEXT("inputEvent"), InputEventField) && !InputEventField.IsEmpty())
		{
			Payload->SetStringField(TEXT("inputEvent"), InputEventField);
		}
		// AddDelegate / RemoveDelegate (DSL form 2): 'targetType' is the receiver
		// variable's declared type -- when delegate property is declared on component class
		// (e.g. UPrimitiveComponent), must resolve via that class chain; not on blueprint Actor class chain.
		FString TargetTypeName;
		if (Node->TryGetStringField(TEXT("targetType"), TargetTypeName) && !TargetTypeName.IsEmpty())
		{
			Payload->SetStringField(TEXT("targetType"), TargetTypeName);
		}
		// Switch node family (SwitchOnInt / SwitchOnString): 'cases' array drives
		// ConfigureNativeFlowNode to generate case output pins (int case pin name = case value,
		// string case pin name = literal string); missing it, node only has Default output pin.
		const TArray<TSharedPtr<FJsonValue>>* NodeCases = nullptr;
		if (Node->TryGetArrayField(TEXT("cases"), NodeCases) && NodeCases->Num() > 0)
		{
			Payload->SetArrayField(TEXT("cases"), *NodeCases);
		}
		// SwitchOnEnum: 'enum' specifies enumeration (/Game/... asset path or short name),
		// missing it, enum output pins won't be generated; Selection cannot connect.
		FString EnumFieldName;
		if (Node->TryGetStringField(TEXT("enum"), EnumFieldName) && !EnumFieldName.IsEmpty())
		{
			Payload->SetStringField(TEXT("enum"), EnumFieldName);
		}
		// MakeStruct / BreakStruct: 'struct' specifies the struct type (short name
		// or asset path, e.g. Vector, /Game/.../FHealth_B), driving pin creation
		// in ConfigureNativeFlowNode. Without it the node has no struct type and
		// no member pins.
		FString StructName;
		if (Node->TryGetStringField(TEXT("struct"), StructName) && !StructName.IsEmpty())
		{
			Payload->SetStringField(TEXT("struct"), StructName);
		}

		// MultiGate / Sequence: 'outputs' is how many execution output pins to allocate.
		// Without it ConfigureNativeFlowNode stops at the two default outputs ("Out 0/1" for
		// MultiGate, "then_0/1" for Sequence) and every connection to a third one fails with
		// "has no pin named 'Out 2'".
		double OutputsField = 0.0;
		if (Node->TryGetNumberField(TEXT("outputs"), OutputsField))
		{
			Payload->SetNumberField(TEXT("outputs"), OutputsField);
		}

		// Timeline length in seconds (DSL [Timeline(Length = 1.0f)]): forwarded as a
		// top-level number so the bridge's timeline kind can size the template.
		// Defaults to 1s on the bridge side when absent.
		double TimelineLengthValue = 0.0;
		if (Node->TryGetNumberField(TEXT("length"), TimelineLengthValue))
		{
			Payload->SetNumberField(TEXT("length"), TimelineLengthValue);
		}

		// Timeline tracks (DSL [Timeline] nested class: `float v = Curve((t, v), ...)`,
		// `Vector p = Curve(...)`, `Color c = Curve(...)`, `EventTrack Fire = At(t, ...)`):
		// forwarded verbatim so the bridge's timeline kind can build the tracks and their
		// curve objects. Absent (the MCP A-side construction form) -> the bridge keeps its
		// legacy single default float track named after the timeline.
		const TArray<TSharedPtr<FJsonValue>>* TracksArray = nullptr;
		if (Node->TryGetArrayField(TEXT("tracks"), TracksArray) && TracksArray->Num() > 0)
		{
			Payload->SetArrayField(TEXT("tracks"), *TracksArray);
		}

		TSharedPtr<FJsonObject> Result = Client->HandleAddBlueprintNode(Payload);
		FString RealNodeId;
		if (Result.IsValid())
		{
			Result->TryGetStringField(TEXT("nodeId"), RealNodeId);
		}
		if (!BridgeBool(Result, TEXT("added")) || RealNodeId.IsEmpty())
		{
			Warnings.Add({ TEXT("BP_NODE_NOT_FOUND"),
				FString::Printf(TEXT("failed to add node '%s' (type=%s): %s"), *NodeId, *NodeType, *BridgeReason(Result)) });
			return false;
		}

		DslIdToNodeId.Add(NodeId, RealNodeId);
		++OutNodeCount;

		if (Props != nullptr && Props->IsValid())
		{
			for (const TPair<FString, TSharedPtr<FJsonValue>>& PinProp : (*Props)->Values)
			{
				FString PinValue;
				switch (PinProp.Value->Type)
				{
				case EJson::String:  PinValue = PinProp.Value->AsString(); break;
				case EJson::Number:
				{
					// JSON numbers with an integral value (e.g. ForLoop macro
					// "LastIndex": 9) must keep an integral literal ("9").
					// SanitizeFloat would emit "9.0", which the int pin
					// validator rejects, and TrySetDefaultValue drops the
					// value without surfacing any error (void + silent).
					const double NumValue = PinProp.Value->AsNumber();
					const int64 IntValue = static_cast<int64>(NumValue);
					if (static_cast<double>(IntValue) == NumValue)
					{
						PinValue = FString::Printf(TEXT("%lld"), IntValue);
					}
					else
					{
						PinValue = FString::SanitizeFloat(NumValue);
					}
				}
				break;
				case EJson::Boolean: PinValue = PinProp.Value->AsBool() ? TEXT("true") : TEXT("false"); break;
				default: continue;
				}
				if (PinValue.IsEmpty())
				{
					continue;
				}
				TSharedPtr<FJsonObject> PinPayload = MakePayload(Path);
				PinPayload->SetStringField(TEXT("node"), RealNodeId);
				PinPayload->SetStringField(TEXT("pin"), PinProp.Key);
				PinPayload->SetStringField(TEXT("value"), PinValue);
				// Check if set_pin_default_value actually took effect: previously result was silently
				// discarded; when property application fails (e.g. macro instance pin not yet in place,
				// ref pins, etc.) it only manifests as "default value not filled in" with no warning,
				// extremely hard to diagnose. Explicitly warn on failure.
				TSharedPtr<FJsonObject> SetPinResult = Client->HandleSetPinDefaultValue(PinPayload);
				if (!BridgeBool(SetPinResult, TEXT("set")))
				{
				// Wildcard pins (e.g. KismetArrayLibrary::Array_Add's NewItem: element type propagates
				// in via TargetArray connection) reject literal defaults before connections -- same as
				// editor's "connect first, then fill defaults" ordering. Don't immediately warn here;
				// defer to replay after connections (see ApplyDeferredPinDefaults); warn if replay also fails.
					OutDeferredDefaults.Add({ Path, RealNodeId, PinProp.Key, PinValue });
				}
			}
		}
		return true;
	}

	/**
	 * Replay pin defaults rejected during AddGraphNode phase: at this point connections are complete,
	 * wildcard pins (Array_Add's NewItem, etc.) have had concrete types propagated by driving
	 * connections, and literal defaults can be written normally.
	 * Consistent with editor's "connect first, then fill defaults" ordering; warn if replay still fails.
	 */
	void ApplyDeferredPinDefaults(const TArray<FDeferredPinDefault>& Deferred, TArray<FIRWarning>& Warnings)
	{
		if (Deferred.Num() == 0)
		{
			return;
		}

		FForgeMindBridgeClient* Client = Bridge();
		if (Client == nullptr)
		{
			return;
		}

		for (const FDeferredPinDefault& Entry : Deferred)
		{
			TSharedPtr<FJsonObject> PinPayload = MakePayload(Entry.Path);
			PinPayload->SetStringField(TEXT("node"), Entry.NodeId);
			PinPayload->SetStringField(TEXT("pin"), Entry.Pin);
			PinPayload->SetStringField(TEXT("value"), Entry.Value);

			TSharedPtr<FJsonObject> SetPinResult = Client->HandleSetPinDefaultValue(PinPayload);
			if (!BridgeBool(SetPinResult, TEXT("set")))
			{
				Warnings.Add({ TEXT("BP_PIN_DEFAULT"),
					FString::Printf(TEXT("property pin '%s' = '%s' on node '%s' was not applied (%s)"),
						*Entry.Pin, *Entry.Value, *Entry.NodeId, *BridgeReason(SetPinResult)) });
			}
		}
	}

	void ConnectGraph(const FString& Path, const TSharedPtr<FJsonObject>& GraphDef,
		const TMap<FString, FString>& DslIdToNodeId, TArray<FIRWarning>& Warnings, int32& OutConnCount,
		const TMap<FString, TMap<FString, FString>>* DslPinRemap = nullptr)
	{
		const TArray<TSharedPtr<FJsonValue>>* Connections = nullptr;
		if (!GraphDef->TryGetArrayField(TEXT("connections"), Connections))
		{
			return;
		}
		FForgeMindBridgeClient* Client = Bridge();
		if (Client == nullptr)
		{
			return;
		}

		for (const TSharedPtr<FJsonValue>& ConnectionValue : *Connections)
		{
			const TSharedPtr<FJsonObject> Connection = ConnectionValue->AsObject();
			if (!Connection.IsValid())
			{
				continue;
			}
			FString FromNode, FromPin, ToNode, ToPin;
			Connection->TryGetStringField(TEXT("sourceNodeId"), FromNode);
			Connection->TryGetStringField(TEXT("sourcePinId"), FromPin);
			Connection->TryGetStringField(TEXT("targetNodeId"), ToNode);
			Connection->TryGetStringField(TEXT("targetPinId"), ToPin);

			const FString* FromReal = DslIdToNodeId.Find(FromNode);
			const FString* ToReal = DslIdToNodeId.Find(ToNode);
			if (FromReal == nullptr || ToReal == nullptr)
			{
				Warnings.Add({ TEXT("BP_NODE_NOT_FOUND"),
					FString::Printf(TEXT("connection references an unresolved node (%s → %s)"), *FromNode, *ToNode) });
				continue;
			}

			// Translate DSL pin names to real pin names for macro tunnel
			// nodes (DSL uses function convention then/execute; tunnels use
			// execute/then after the pin-direction fix).
			if (DslPinRemap != nullptr)
			{
				if (const TMap<FString, FString>* FromRemap = DslPinRemap->Find(FromNode))
				{
					if (const FString* Mapped = FromRemap->Find(FromPin))
					{
						FromPin = *Mapped;
					}
				}
				if (const TMap<FString, FString>* ToRemap = DslPinRemap->Find(ToNode))
				{
					if (const FString* Mapped = ToRemap->Find(ToPin))
					{
						ToPin = *Mapped;
					}
				}
			}

			TSharedPtr<FJsonObject> Payload = MakePayload(Path);
			Payload->SetStringField(TEXT("fromNode"), *FromReal);
			Payload->SetStringField(TEXT("fromPin"), FromPin);
			Payload->SetStringField(TEXT("toNode"), *ToReal);
			Payload->SetStringField(TEXT("toPin"), ToPin);
			// Resolve call result before branching: the failure branch appends the bridge-side
			// real rejection reason (BridgeReason) into message, so Result must be a named variable
			// (inlining it directly into BridgeBool earlier left Result undeclared → C2065, plugin build failure).
			TSharedPtr<FJsonObject> Result = Client->HandleConnectBlueprintPins(Payload);
			if (BridgeBool(Result, TEXT("connected")))
			{
				++OutConnCount;
			}
			else
			{
				// Diagnostic enhancement: connection failures are uniformly tagged BP_PIN_TYPE_MISMATCH,
				// but the bridge-side real rejection reason (pin missing / type mismatch / graph rejection)
				// is appended to message; otherwise failures like LevelLiteral→external member call are untraceable.
				Warnings.Add({ TEXT("BP_PIN_TYPE_MISMATCH"),
					FString::Printf(TEXT("failed to connect %s.%s → %s.%s (%s)"), *FromNode, *FromPin, *ToNode, *ToPin, *BridgeReason(Result)) });
			}
		}
	}
}

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

class FBlueprintIRHandler : public IIRHandler
{
public:
void GetAssetTypes(TArray<FString>& OutAssetTypes) const override
	{
		OutAssetTypes = { TEXT("blueprint"), TEXT("blueprint_interface"), TEXT("user_defined_struct"), TEXT("user_defined_enum"), TEXT("level_blueprint"), TEXT("blueprint_function_library"), TEXT("blueprint_macro_library") };
	}

	UObject* Create(FIRApplyContext& Ctx) override;
	bool Update(UObject* Asset, FIRApplyContext& Ctx) override;
	bool DeleteAsset(FIRApplyContext& Ctx) override;
	TSharedPtr<FJsonObject> Read(UObject* Asset, FIRApplyContext& Ctx) override;
	bool CompileAsset(UObject* Asset, FIRApplyContext& Ctx) override;

private:
	UObject* CreateBlueprintAsset(FIRApplyContext& Ctx);
	UObject* CreateInterfaceAsset(FIRApplyContext& Ctx);
	UObject* CreateStructAsset(FIRApplyContext& Ctx);
	UObject* CreateEnumAsset(FIRApplyContext& Ctx);
	void ApplyBlueprintDefinition(UBlueprint* Blueprint, FIRApplyContext& Ctx);
};

UObject* FBlueprintIRHandler::Create(FIRApplyContext& Ctx)
{
	if (Ctx.AssetType == TEXT("user_defined_struct"))
	{
		return CreateStructAsset(Ctx);
	}
	if (Ctx.AssetType == TEXT("user_defined_enum"))
	{
		return CreateEnumAsset(Ctx);
	}
	if (Ctx.AssetType == TEXT("blueprint_interface"))
	{
		return CreateInterfaceAsset(Ctx);
	}
	return CreateBlueprintAsset(Ctx);
}

// ---------------------------------------------------------------------------
// Blueprint Interface asset creation (assetType "blueprint_interface").
//
// IR definition.messages → HandleCreateBlueprintInterface payload.messages.
// Each message = { name, params:[{name,type}] }; the bridge creates a function
// graph per message (signature only, no body — interface functions have no
// implementation). Reuses the proven HandleCreateBlueprintInterface path that
// MCP-side create_blueprint_interface also walks, so IR and MCP share one code
// path (same rationale as CreateBlueprintAsset delegating to HandleCreateBlueprint).
// ---------------------------------------------------------------------------
UObject* FBlueprintIRHandler::CreateInterfaceAsset(FIRApplyContext& Ctx)
{
	FForgeMindBridgeClient* Client = Bridge();
	if (Client == nullptr)
	{
		Ctx.Fail(TEXT("BP_CLASS_NOT_FOUND"), TEXT("bridge client is not available"));
		return nullptr;
	}

	TSharedPtr<FJsonObject> Payload = MakePayload(Ctx.Path);

	// definition.messages → Payload.messages (interface message signatures)
	const TArray<TSharedPtr<FJsonValue>>* Messages = nullptr;
	if (Ctx.Definition.IsValid()
		&& Ctx.Definition->TryGetArrayField(TEXT("messages"), Messages)
		&& Messages != nullptr)
	{
		Payload->SetArrayField(TEXT("messages"), *Messages);
	}

	const TSharedPtr<FJsonObject> Result = Client->HandleCreateBlueprintInterface(Payload);
	if (!BridgeBool(Result, TEXT("created")))
	{
		Ctx.Fail(TEXT("BP_CLASS_NOT_FOUND"),
			FString::Printf(TEXT("failed to create interface blueprint: %s"), *BridgeReason(Result)));
		return nullptr;
	}

	UBlueprint* Blueprint = ForgeMindBlueprintUtils::ResolveBlueprint(Ctx.Path);
	if (Blueprint == nullptr)
	{
		Ctx.Fail(TEXT("BP_CLASS_NOT_FOUND"),
			FString::Printf(TEXT("interface blueprint created but not loadable at %s"), *Ctx.Path));
		return nullptr;
	}

	return Blueprint;
}

UObject* FBlueprintIRHandler::CreateBlueprintAsset(FIRApplyContext& Ctx)
{
	FForgeMindBridgeClient* Client = Bridge();
	if (Client == nullptr)
	{
		Ctx.Fail(TEXT("BP_CLASS_NOT_FOUND"), TEXT("bridge client is not available"));
		return nullptr;
	}

	// Level blueprints are embedded in the level asset (.umap): there is no
	// standalone .uasset to create. create_level (decision 3=B) has already
	// opened the target level, so reuse its level blueprint and apply the
	// definition in place (mirrors the legacy AssetIR branch).
	if (Ctx.AssetType == TEXT("level_blueprint"))
	{
		UBlueprint* LevelBP = ForgeMindBlueprintUtils::ResolveBlueprint(Ctx.Path);
		if (LevelBP == nullptr)
		{
			Ctx.Fail(TEXT("LEVEL_BP_NOT_FOUND"),
				TEXT("level blueprint requires an open level; call create_level (or open the level) first"));
			return nullptr;
		}
		ApplyBlueprintDefinition(LevelBP, Ctx);
		return LevelBP;
	}

	FString ParentClass = ForgeMindIR::FirstString(Ctx.Definition, { TEXT("extends"), TEXT("parentClass"), TEXT("parent") });
	if (ParentClass.IsEmpty())
	{
		ParentClass = TEXT("Actor");
	}

	TSharedPtr<FJsonObject> Payload = MakePayload(Ctx.Path);
	Payload->SetStringField(TEXT("parentClass"), ParentClass);

	// definition.implements → Payload.implements (interface asset paths).
	// HandleCreateBlueprint attaches each interface via ImplementNewInterface and
	// auto-builds implementation graphs for void messages (see its implements block).
	const TArray<TSharedPtr<FJsonValue>>* Implements = nullptr;
	if (Ctx.Definition.IsValid()
		&& Ctx.Definition->TryGetArrayField(TEXT("implements"), Implements)
		&& Implements != nullptr)
	{
		Payload->SetArrayField(TEXT("implements"), *Implements);
	}

	// definition.replicates → Payload.replicates (Actor CDO bReplicates;
	// HandleCreateBlueprint applies it).
	bool bDefReplicates = false;
	if (Ctx.Definition.IsValid()
		&& Ctx.Definition->TryGetBoolField(TEXT("replicates"), bDefReplicates)
		&& bDefReplicates)
	{
		Payload->SetBoolField(TEXT("replicates"), true);
	}

	TSharedPtr<FJsonObject> Result = Client->HandleCreateBlueprint(Payload);
	if (!BridgeBool(Result, TEXT("created")))
	{
		Ctx.Fail(TEXT("BP_CLASS_NOT_FOUND"), FString::Printf(TEXT("failed to create blueprint: %s"), *BridgeReason(Result)));
		return nullptr;
	}

	UBlueprint* Blueprint = ForgeMindBlueprintUtils::ResolveBlueprint(Ctx.Path);
	if (Blueprint == nullptr)
	{
		Ctx.Fail(TEXT("BP_CLASS_NOT_FOUND"), FString::Printf(TEXT("blueprint created but not loadable at %s"), *Ctx.Path));
		return nullptr;
	}

	ApplyBlueprintDefinition(Blueprint, Ctx);
	return Blueprint;
}

void FBlueprintIRHandler::ApplyBlueprintDefinition(UBlueprint* Blueprint, FIRApplyContext& Ctx)
{
	FForgeMindBridgeClient* Client = Bridge();
	if (Client == nullptr || !Ctx.Definition.IsValid())
	{
		return;
	}
	const FString& Path = Ctx.Path;

	int32 VariableCount = 0;
	int32 ComponentCount = 0;
	int32 FunctionCount = 0;
	int32 NodeCount = 0;
	int32 ConnectionCount = 0;

	// --- members (variables) ---
	const TArray<TSharedPtr<FJsonValue>>* Members = nullptr;
	if (Ctx.Definition->TryGetArrayField(TEXT("members"), Members))
	{
		for (const TSharedPtr<FJsonValue>& Value : *Members)
		{
			const TSharedPtr<FJsonObject> Member = Value->AsObject();
			if (!Member.IsValid())
			{
				continue;
			}
			FString Name, Type;
			Member->TryGetStringField(TEXT("name"), Name);
			Member->TryGetStringField(TEXT("type"), Type);
			if (Name.IsEmpty() || Type.IsEmpty())
			{
				Ctx.Warn(TEXT("INVALID_VALUE"), TEXT("skipped member without name or type"));
				continue;
			}
			TSharedPtr<FJsonObject> Payload = MakePayload(Path);
			Payload->SetStringField(TEXT("name"), Name);
			Payload->SetStringField(TEXT("type"), Type);
			FString Category;
			if (Member->TryGetStringField(TEXT("category"), Category))
			{
				Payload->SetStringField(TEXT("category"), Category);
			}
			FString Tooltip;
			if (Member->TryGetStringField(TEXT("tooltip"), Tooltip))
			{
				Payload->SetStringField(TEXT("tooltip"), Tooltip);
			}
			bool bFlag = false;
			if (Member->TryGetBoolField(TEXT("editAnywhere"), bFlag))
			{
				Payload->SetBoolField(TEXT("instanceEditable"), bFlag);
			}
			if (Member->TryGetBoolField(TEXT("exposeOnSpawn"), bFlag) && bFlag)
			{
				Payload->SetBoolField(TEXT("exposeOnSpawn"), true);
			}
			if (Member->TryGetBoolField(TEXT("replicated"), bFlag) && bFlag)
			{
				Payload->SetStringField(TEXT("replication"), TEXT("replicated"));
			}
			// member.onRep → RepNotify: replication mode 'repNotify' + the OnRep function
			// name. HandleAddBlueprintVariable sets CPF_Net|CPF_RepNotify + RepNotifyFunc.
			FString OnRep;
			if (Member->TryGetStringField(TEXT("onRep"), OnRep) && !OnRep.IsEmpty())
			{
				Payload->SetStringField(TEXT("replication"), TEXT("repNotify"));
				Payload->SetStringField(TEXT("onRep"), OnRep);
			}
			if (Member->HasField(TEXT("defaultValue")))
			{
				Payload->SetField(TEXT("default"), Member->TryGetField(TEXT("defaultValue")));
			}
			// Bitmask metadata: marks the property as a bitmask and optionally
			// associates it with a bitflag enum (MD_Bitmask / MD_BitmaskEnum).
			bool bBitmask = false;
			if (Member->TryGetBoolField(TEXT("bitmask"), bBitmask) && bBitmask)
			{
				Payload->SetBoolField(TEXT("bitmask"), true);
			}
			FString BitmaskEnumName;
			if (Member->TryGetStringField(TEXT("bitmaskEnum"), BitmaskEnumName) && !BitmaskEnumName.IsEmpty())
			{
				Payload->SetStringField(TEXT("bitmaskEnum"), BitmaskEnumName);
			}
			const TSharedPtr<FJsonObject> AddResult = Client->HandleAddBlueprintVariable(Payload);
			if (BridgeBool(AddResult, TEXT("added")))
			{
				++VariableCount;
			}
			else
			{
				// The bridge tells the two failures apart: the name is taken, or
				// a setting belongs to the class that declares an inherited
				// variable. Carry both its code and its reason so the caller can
				// tell them apart without guessing from the message.
				FString ErrorCode = TEXT("MEMBER_ALREADY_EXISTS");
				FString Reason;
				if (AddResult.IsValid())
				{
					AddResult->TryGetStringField(TEXT("errorCode"), ErrorCode);
					Reason = BridgeReason(AddResult);
				}
				Ctx.Warn(ErrorCode, Reason.IsEmpty()
					? FString::Printf(TEXT("variable '%s' was not added"), *Name)
					: FString::Printf(TEXT("variable '%s' was not added: %s"), *Name, *Reason));
			}
		}
	}

	// --- components ---
	const TArray<TSharedPtr<FJsonValue>>* Components = nullptr;
	if (Ctx.Definition->TryGetArrayField(TEXT("components"), Components))
	{
		for (const TSharedPtr<FJsonValue>& Value : *Components)
		{
			const TSharedPtr<FJsonObject> Component = Value->AsObject();
			if (!Component.IsValid())
			{
				continue;
			}
			FString Name, Type;
			Component->TryGetStringField(TEXT("name"), Name);
			Component->TryGetStringField(TEXT("type"), Type);
			if (Name.IsEmpty() || Type.IsEmpty())
			{
				Ctx.Warn(TEXT("INVALID_VALUE"), TEXT("skipped component without name or type"));
				continue;
			}
			TSharedPtr<FJsonObject> Payload = MakePayload(Path);
			Payload->SetStringField(TEXT("name"), Name);
			Payload->SetStringField(TEXT("class"), Type);
			FString Parent;
			if (Component->TryGetStringField(TEXT("parent"), Parent) && !Parent.IsEmpty())
			{
				Payload->SetStringField(TEXT("parent"), Parent);
			}
			const TSharedPtr<FJsonObject>* Props = nullptr;
			if (Component->TryGetObjectField(TEXT("properties"), Props) && Props->IsValid())
			{
				Payload->SetObjectField(TEXT("properties"), *Props);
			}
			if (BridgeBool(Client->HandleAddBlueprintComponent(Payload), TEXT("added")))
			{
				++ComponentCount;
			}
			else
			{
				Ctx.Warn(TEXT("BP_COMPONENT_CONFLICT"), FString::Printf(TEXT("component '%s' was not added"), *Name));
			}
		}
	}

	// --- dispatchers ---
	const TArray<TSharedPtr<FJsonValue>>* Dispatchers = nullptr;
	if (Ctx.Definition->TryGetArrayField(TEXT("dispatchers"), Dispatchers) && Dispatchers != nullptr)
	{
		for (const TSharedPtr<FJsonValue>& Value : *Dispatchers)
		{
			const TSharedPtr<FJsonObject> Dispatcher = Value->AsObject();
			if (!Dispatcher.IsValid())
			{
				continue;
			}
			FString DispName;
			if (!Dispatcher->TryGetStringField(TEXT("name"), DispName) || DispName.IsEmpty())
			{
				Ctx.Warn(TEXT("INVALID_VALUE"), TEXT("skipped dispatcher without name"));
				continue;
			}

			TSharedPtr<FJsonObject> Payload = MakePayload(Path);
			Payload->SetStringField(TEXT("name"), DispName);

			// Optional signature: 'params' array of [{name, type}, ...]
			const TArray<TSharedPtr<FJsonValue>>* DispParams = nullptr;
			if (Dispatcher->TryGetArrayField(TEXT("inputs"), DispParams))
			{
				TArray<TSharedPtr<FJsonValue>> ParamsJson;
				for (const TSharedPtr<FJsonValue>& ParamValue : *DispParams)
				{
					const TSharedPtr<FJsonObject>* ParamObjPtr = nullptr;
					if (ParamValue.IsValid() && ParamValue->TryGetObject(ParamObjPtr) && ParamObjPtr->IsValid())
					{
						ParamsJson.Add(MakeShared<FJsonValueObject>(*ParamObjPtr));
					}
				}
				if (ParamsJson.Num() > 0)
				{
					Payload->SetArrayField(TEXT("params"), ParamsJson);
				}
			}

			if (BridgeBool(Client->HandleAddEventDispatcher(Payload), TEXT("added")))
			{
				++VariableCount;
			}
			else
			{
				Ctx.Warn(TEXT("BP_DISPATCHER_CONFLICT"),
					FString::Printf(TEXT("dispatcher '%s' was not added"), *DispName));
			}
		}
	}

	// --- function shells ---
	const TArray<TSharedPtr<FJsonValue>>* Functions = nullptr;
	const bool bHasFunctions = Ctx.Definition->TryGetArrayField(TEXT("functions"), Functions);
	if (bHasFunctions && Functions != nullptr)
	{
		for (const TSharedPtr<FJsonValue>& Value : *Functions)
		{
			const TSharedPtr<FJsonObject> Function = Value->AsObject();
			if (!Function.IsValid())
			{
				continue;
			}
			FString Name;
			Function->TryGetStringField(TEXT("name"), Name);
			if (Name.IsEmpty())
			{
				continue;
			}
			TSharedPtr<FJsonObject> Payload = MakePayload(Path);
			Payload->SetStringField(TEXT("name"), Name);
			bool bPure = false;
			if (Function->TryGetBoolField(TEXT("pure"), bPure))
			{
				Payload->SetBoolField(TEXT("pure"), bPure);
			}
			const TArray<TSharedPtr<FJsonValue>>* Inputs = nullptr;
			if (Function->TryGetArrayField(TEXT("inputs"), Inputs))
			{
				Payload->SetArrayField(TEXT("params"), *Inputs);
			}
			const TArray<TSharedPtr<FJsonValue>>* Outputs = nullptr;
			if (Function->TryGetArrayField(TEXT("outputs"), Outputs))
			{
				Payload->SetArrayField(TEXT("outputs"), *Outputs);
			}
			const TArray<TSharedPtr<FJsonValue>>* Locals = nullptr;
			if (Function->TryGetArrayField(TEXT("locals"), Locals))
			{
				Payload->SetArrayField(TEXT("locals"), *Locals);
			}
			// function.rpc / reliable / withValidation → HandleCreateFunctionGraph sets the
			// entry node net flags (FUNC_Net | NetServer/NetClient/NetMulticast [+ NetReliable]).
			FString Rpc;
			if (Function->TryGetStringField(TEXT("rpc"), Rpc) && !Rpc.IsEmpty())
			{
				Payload->SetStringField(TEXT("rpc"), Rpc);
				bool bReliable = false;
				if (Function->TryGetBoolField(TEXT("reliable"), bReliable))
				{
					Payload->SetBoolField(TEXT("reliable"), bReliable);
				}
				bool bValidation = false;
				if (Function->TryGetBoolField(TEXT("withValidation"), bValidation) && bValidation)
				{
					Payload->SetBoolField(TEXT("withValidation"), true);
				}
			}
			if (BridgeBool(Client->HandleCreateFunctionGraph(Payload), TEXT("created")))
			{
				++FunctionCount;
			}
		}

		// Compile once so signatures are visible to the node factory.
		Client->HandleCompileBlueprint(MakePayload(Path));
	}

	// --- macro shells ---
	// Macro graphs are defined inline in the graphs array with type=="macro".
	// Create them up front (like function shells) so macro instance nodes in
	// other graphs can reference them. The DSL emits FunctionEntry/
	// FunctionResult nodes for the macro body; AddGraphNode maps those onto
	// the tunnel nodes that HandleCreateMacroGraph creates.
	const TArray<TSharedPtr<FJsonValue>>* Graphs = nullptr;
	bool bHasMacros = false;
	if (Ctx.Definition->TryGetArrayField(TEXT("graphs"), Graphs))
	{
		for (const TSharedPtr<FJsonValue>& Value : *Graphs)
		{
			const TSharedPtr<FJsonObject> Graph = Value->AsObject();
			if (!Graph.IsValid())
			{
				continue;
			}
			FString GraphType;
			Graph->TryGetStringField(TEXT("type"), GraphType);
			if (GraphType != TEXT("macro"))
			{
				continue;
			}
			FString MacroName;
			Graph->TryGetStringField(TEXT("name"), MacroName);
			if (MacroName.IsEmpty())
			{
				continue;
			}
			const auto Created = Client->HandleCreateMacroGraph(MacroShellPayload(Path, Graph));
			if (BridgeBool(Created, TEXT("created")))
			{
				bHasMacros = true;
				++FunctionCount;
			}
			else
			{
				Ctx.Warn(TEXT("BP_NODE_NOT_FOUND"), TEXT("Cannot create macro '") + MacroName + TEXT("': ") + BridgeReason(Created));
			}
		}

		if (bHasMacros)
		{
			// Compile so macro signatures are visible to the node factory.
			Client->HandleCompileBlueprint(MakePayload(Path));
		}
	}

	// --- graphs (event graph + pre-built function/macro shells) ---
	if (Graphs != nullptr)
	{
		for (const TSharedPtr<FJsonValue>& Value : *Graphs)
		{
			const TSharedPtr<FJsonObject> Graph = Value->AsObject();
			if (!Graph.IsValid())
			{
				continue;
			}
			FString GraphName = TEXT("EventGraph");
			Graph->TryGetStringField(TEXT("name"), GraphName);
			FString GraphType = TEXT("event");
			Graph->TryGetStringField(TEXT("type"), GraphType);

		TMap<FString, FString> DslIdToNodeId;
		// Macro tunnel pin remap: DSL node id → (DSL pin → real pin).
		// Populated by AddGraphNode when mapping FunctionEntry/FunctionResult
		// onto macro tunnel nodes; consumed by ConnectGraph.
		TMap<FString, TMap<FString, FString>> DslPinRemap;
		// Pin defaults rejected during node creation, replayed after connections (scope must cover ConnectGraph)
		TArray<FDeferredPinDefault> DeferredDefaults;
		const TArray<TSharedPtr<FJsonValue>>* Nodes = nullptr;
		if (Graph->TryGetArrayField(TEXT("nodes"), Nodes))
		{
			// Two-pass creation: CustomEvent / Event nodes first, then the
			// rest.  Custom events compile into UFunctions, so they must
			// exist (and the blueprint must be compiled) before any
			// K2Node_CallFunction that references them is created —
			// otherwise the CallFunction node has no pins and every
			// connection attempt fails with BP_PIN_TYPE_MISMATCH.
			auto IsCustomEventNode = [](const TSharedPtr<FJsonObject>& Node)
			{
				FString Kind, NodeType;
				Node->TryGetStringField(TEXT("kind"), Kind);
				Node->TryGetStringField(TEXT("type"), NodeType);
				return Kind == TEXT("customevent")
					|| NodeType == TEXT("CustomEvent")
					|| NodeType == TEXT("InputAction")
					|| NodeType == TEXT("EnhancedInputAction")
					|| NodeType == TEXT("InputAxis");
			};

			for (const TSharedPtr<FJsonValue>& NodeValue : *Nodes)
			{
				const TSharedPtr<FJsonObject> Node = NodeValue->AsObject();
				if (Node.IsValid() && IsCustomEventNode(Node))
				{
					AddGraphNode(Path, GraphName, Node, DslIdToNodeId, Ctx.Warnings, NodeCount, DeferredDefaults, &DslPinRemap);
				}
			}

			// Compile so the custom event UFunctions are visible.
			Client->HandleCompileBlueprint(MakePayload(Path));

			// Second pass: all remaining nodes (branches, calls, etc.)
			for (const TSharedPtr<FJsonValue>& NodeValue : *Nodes)
			{
				const TSharedPtr<FJsonObject> Node = NodeValue->AsObject();
				if (Node.IsValid() && !IsCustomEventNode(Node))
				{
					AddGraphNode(Path, GraphName, Node, DslIdToNodeId, Ctx.Warnings, NodeCount, DeferredDefaults, &DslPinRemap);
				}
			}
		}
		ConnectGraph(Path, Graph, DslIdToNodeId, Ctx.Warnings, ConnectionCount, &DslPinRemap);
		ApplyDeferredPinDefaults(DeferredDefaults, Ctx.Warnings);
	}
	}

	// --- function bodies ---
	if (bHasFunctions && Functions != nullptr)
	{
		for (const TSharedPtr<FJsonValue>& Value : *Functions)
		{
			const TSharedPtr<FJsonObject> Function = Value->AsObject();
			if (!Function.IsValid())
			{
				continue;
			}
			FString Name;
			Function->TryGetStringField(TEXT("name"), Name);
			if (Name.IsEmpty() || !FindFunctionGraph(Blueprint, Name))
			{
				continue;
			}
			TMap<FString, FString> DslIdToNodeId;
			TMap<FString, TMap<FString, FString>> DslPinRemap;
			TArray<FDeferredPinDefault> DeferredDefaults;
			const TArray<TSharedPtr<FJsonValue>>* Nodes = nullptr;
			if (Function->TryGetArrayField(TEXT("nodes"), Nodes))
			{
				for (const TSharedPtr<FJsonValue>& NodeValue : *Nodes)
				{
					const TSharedPtr<FJsonObject> Node = NodeValue->AsObject();
					if (Node.IsValid())
					{
						AddGraphNode(Path, Name, Node, DslIdToNodeId, Ctx.Warnings, NodeCount, DeferredDefaults, &DslPinRemap);
					}
				}
			}
			ConnectGraph(Path, Function, DslIdToNodeId, Ctx.Warnings, ConnectionCount, &DslPinRemap);
			ApplyDeferredPinDefaults(DeferredDefaults, Ctx.Warnings);
		}
	}

	Ctx.Extra->SetNumberField(TEXT("variableCount"), VariableCount);
	Ctx.Extra->SetNumberField(TEXT("componentCount"), ComponentCount);
	Ctx.Extra->SetNumberField(TEXT("functionCount"), FunctionCount);
	Ctx.Extra->SetNumberField(TEXT("graphNodeCount"), NodeCount);
	Ctx.Extra->SetNumberField(TEXT("graphConnectionCount"), ConnectionCount);
}

UObject* FBlueprintIRHandler::CreateStructAsset(FIRApplyContext& Ctx)
{
	FForgeMindBridgeClient* Client = Bridge();
	if (Client == nullptr)
	{
		Ctx.Fail(TEXT("ASSET_TYPE_MISMATCH"), TEXT("bridge client is not available"));
		return nullptr;
	}

	TSharedPtr<FJsonObject> Result = Client->HandleCreateBlueprintStruct(MakePayload(Ctx.Path));
	if (!BridgeBool(Result, TEXT("created")))
	{
		Ctx.Fail(TEXT("CREATE_FAILED"), FString::Printf(TEXT("failed to create struct: %s"), *BridgeReason(Result)));
		return nullptr;
	}

	int32 MemberCount = 0;
	const TArray<TSharedPtr<FJsonValue>>* Members = nullptr;
	if (Ctx.Definition.IsValid() && Ctx.Definition->TryGetArrayField(TEXT("members"), Members))
	{
		for (const TSharedPtr<FJsonValue>& Value : *Members)
		{
			const TSharedPtr<FJsonObject> Member = Value->AsObject();
			if (!Member.IsValid())
			{
				continue;
			}
			TSharedPtr<FJsonObject> Payload = MakePayload(Ctx.Path);
			for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Member->Values)
			{
				Payload->SetField(Pair.Key, Pair.Value);
			}
			if (BridgeBool(Client->HandleAddStructMember(Payload), TEXT("added")))
			{
				++MemberCount;
			}
		}
	}
	// Remove the leftover default boolean member that CreateUserDefinedStruct
	// always inserts.  We did not pass members to HandleCreateBlueprintStruct
	// (it would have removed the default itself when Members.Num() > 0), so
	// the default survives.  After adding the caller's members, scan for any
	// member whose internal VarName starts with "MemberVar_" (the engine's
	// auto-generated name for unnamed members) and remove it.
	if (MemberCount > 0)
	{
		UUserDefinedStruct* Struct = LoadObject<UUserDefinedStruct>(nullptr, *Ctx.Path);
		if (Struct != nullptr)
		{
			TArray<FGuid> DefaultGuids;
			{
				const TArray<FStructVariableDescription>& VarDescs = FStructureEditorUtils::GetVarDesc(Struct);
				for (const FStructVariableDescription& Desc : VarDescs)
				{
					if (Desc.VarName.ToString().StartsWith(TEXT("MemberVar_")))
					{
						DefaultGuids.Add(Desc.VarGuid);
					}
				}
			}
			for (const FGuid& DefaultGuid : DefaultGuids)
			{
				FStructureEditorUtils::RemoveVariable(Struct, DefaultGuid);
			}
			if (DefaultGuids.Num() > 0)
			{
				FStructureEditorUtils::OnStructureChanged(Struct, FStructureEditorUtils::EStructureEditorChangeInfo::RemovedVariable);
				FStructureEditorUtils::CompileStructure(Struct);
				Struct->MarkPackageDirty();
				Ctx.Warn(TEXT("DEFAULT_MEMBER_REMOVED"),
					FString::Printf(TEXT("Removed %d leftover default member(s) from struct '%s'"), DefaultGuids.Num(), *Ctx.Path));
			}
		}
	}

	Ctx.Extra->SetNumberField(TEXT("memberCount"), MemberCount);
	return ForgeMindIR::LoadAsset(Ctx.Path);
}

UObject* FBlueprintIRHandler::CreateEnumAsset(FIRApplyContext& Ctx)
{
	FForgeMindBridgeClient* Client = Bridge();
	if (Client == nullptr)
	{
		Ctx.Fail(TEXT("ASSET_TYPE_MISMATCH"), TEXT("bridge client is not available"));
		return nullptr;
	}

	TSharedPtr<FJsonObject> CreatePayload = MakePayload(Ctx.Path);
	// Optional bitflags metadata: marks the enum as a bitflags set (engine
	// FEnumEditorUtils::SetEnumeratorBitflagsTypeState). Required by
	// UK2Node_BitmaskLiteral (ValidateBitflagsEnumType rejects enums without it).
	bool bBitflags = false;
	if (Ctx.Definition.IsValid() && Ctx.Definition->TryGetBoolField(TEXT("bitflags"), bBitflags) && bBitflags)
	{
		CreatePayload->SetBoolField(TEXT("bitflags"), true);
	}
	TSharedPtr<FJsonObject> Result = Client->HandleCreateBlueprintEnum(CreatePayload);
	if (!BridgeBool(Result, TEXT("created")))
	{
		Ctx.Fail(TEXT("CREATE_FAILED"), FString::Printf(TEXT("failed to create enum: %s"), *BridgeReason(Result)));
		return nullptr;
	}

	int32 EntryCount = 0;
	const TArray<TSharedPtr<FJsonValue>>* Values = nullptr;
	if (Ctx.Definition.IsValid() && Ctx.Definition->TryGetArrayField(TEXT("values"), Values))
	{
		for (const TSharedPtr<FJsonValue>& Value : *Values)
		{
			const TSharedPtr<FJsonObject> Entry = Value->AsObject();
			if (!Entry.IsValid())
			{
				continue;
			}
			TSharedPtr<FJsonObject> Payload = MakePayload(Ctx.Path);
			for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Entry->Values)
			{
				Payload->SetField(Pair.Key, Pair.Value);
			}
			if (BridgeBool(Client->HandleAddEnumEntry(Payload), TEXT("added")))
			{
				++EntryCount;
			}
		}
	}
	Ctx.Extra->SetNumberField(TEXT("entryCount"), EntryCount);

	// A DSL enum's C# declaration name (definition.name, e.g. EState) may differ from the asset
	// leaf name (e.g. EState_B). Register a session-level alias "declaration name → asset path" so
	// that when blueprint DSL references parameter/variable types by declaration name (e.g. `EState s`),
	// PinTypeTextToPinType resolves it as a fallback; otherwise function/event nodes fail to create
	// entirely because parameter types cannot be resolved.
	FString DeclaredEnumName;
	if (Ctx.Definition.IsValid()
		&& Ctx.Definition->TryGetStringField(TEXT("name"), DeclaredEnumName)
		&& !DeclaredEnumName.IsEmpty())
	{
		ForgeMindBlueprintUtils::RegisterUserTypeEnumAlias(DeclaredEnumName, Ctx.Path);
	}

	return ForgeMindIR::LoadAsset(Ctx.Path);
}

bool FBlueprintIRHandler::Update(UObject* Asset, FIRApplyContext& Ctx)
{
	FForgeMindBridgeClient* Client = Bridge();
	if (Client == nullptr)
	{
		Ctx.Fail(TEXT("UPDATE_FAILED"), TEXT("bridge client is not available"));
		return false;
	}

	UBlueprint* Blueprint = AsBlueprint(Asset);

	for (const TSharedPtr<FJsonValue>& PatchValue : Ctx.Patches)
	{
		const TSharedPtr<FJsonObject> Patch = PatchValue->AsObject();
		if (!Patch.IsValid())
		{
			continue;
		}

		FString Type, Name;
		Patch->TryGetStringField(TEXT("type"), Type);
		Patch->TryGetStringField(TEXT("name"), Name);
		const bool bDelete = ForgeMindIR::IsDeletePatch(Patch);

		// IR_rules.md §4.3 — the Companion infers the actual op from
		// existence + the delete flag.
		bool bExists = false;
		if (Blueprint != nullptr)
		{
			if (Type == TEXT("variable"))       bExists = FindVariable(Blueprint, Name);
			else if (Type == TEXT("component")) bExists = FindComponent(Blueprint, Name);
			else if (Type == TEXT("function"))  bExists = FindFunctionGraph(Blueprint, Name);
			else if (Type == TEXT("macro"))     bExists = FindMacroGraphPtr(Blueprint, Name) != nullptr;
		}

		FString ActualOp;
		if (bDelete) ActualOp = bExists ? TEXT("delete") : TEXT("failed");
		else         ActualOp = bExists ? TEXT("replace") : TEXT("add");

		bool bOk = false;

		if ((Type == TEXT("variable") || Type == TEXT("member")) && Blueprint != nullptr)
		{
			TSharedPtr<FJsonObject> Payload = MakePayload(Ctx.Path);
			Payload->SetStringField(TEXT("name"), Name);
			if (bDelete)
			{
				bOk = bExists && BridgeBool(Client->HandleRemoveBlueprintVariable(Payload), TEXT("removed"));
			}
			else
			{
				FString VarType;
				Patch->TryGetStringField(TEXT("varType"), VarType);
				if (VarType.IsEmpty())
				{
					Patch->TryGetStringField(TEXT("type"), VarType);
				}
				if (!VarType.IsEmpty())
				{
					Payload->SetStringField(TEXT("type"), VarType);
				}
				FString Category;
				if (Patch->TryGetStringField(TEXT("category"), Category))
				{
					Payload->SetStringField(TEXT("category"), Category);
				}
				// Same flag vocabulary as the create-mode members loop above:
				// editAnywhere -> instanceEditable, exposeOnSpawn / replicated /
				// tooltip go through unchanged. Without these the update path
				// silently dropped every variable setting the DSL can express.
				FString Tooltip;
				if (Patch->TryGetStringField(TEXT("tooltip"), Tooltip))
				{
					Payload->SetStringField(TEXT("tooltip"), Tooltip);
				}
				bool bPatchFlag = false;
				if (Patch->TryGetBoolField(TEXT("editAnywhere"), bPatchFlag))
				{
					Payload->SetBoolField(TEXT("instanceEditable"), bPatchFlag);
				}
				if (Patch->TryGetBoolField(TEXT("exposeOnSpawn"), bPatchFlag) && bPatchFlag)
				{
					Payload->SetBoolField(TEXT("exposeOnSpawn"), true);
				}
				if (Patch->TryGetBoolField(TEXT("replicated"), bPatchFlag) && bPatchFlag)
				{
					Payload->SetStringField(TEXT("replication"), TEXT("replicated"));
				}
				// Bitmask metadata: same vocabulary as the create-mode members loop
				// above - without this the update path silently dropped the bitmask
				// checkbox and its flag enum association.
				if (Patch->TryGetBoolField(TEXT("bitmask"), bPatchFlag) && bPatchFlag)
				{
					Payload->SetBoolField(TEXT("bitmask"), true);
				}
				FString PatchBitmaskEnum;
				if (Patch->TryGetStringField(TEXT("bitmaskEnum"), PatchBitmaskEnum) && !PatchBitmaskEnum.IsEmpty())
				{
					Payload->SetStringField(TEXT("bitmaskEnum"), PatchBitmaskEnum);
				}
				if (Patch->HasField(TEXT("defaultValue")))
				{
					Payload->SetField(TEXT("default"), Patch->TryGetField(TEXT("defaultValue")));
				}
				if (bExists)
				{
					bOk = BridgeBool(Client->HandleUpdateBlueprintVariable(Payload), TEXT("updated"));
				}
				else
				{
					bOk = BridgeBool(Client->HandleAddBlueprintVariable(Payload), TEXT("added"));
				}
			}
		}
		else if (Type == TEXT("component") && Blueprint != nullptr)
		{
			TSharedPtr<FJsonObject> Payload = MakePayload(Ctx.Path);
			Payload->SetStringField(TEXT("name"), Name);
			if (bDelete)
			{
				bOk = bExists && BridgeBool(Client->HandleRemoveBlueprintComponent(Payload), TEXT("removed"));
			}
			else if (bExists)
			{
				bOk = true; // property level refresh below
			}
			else
			{
				FString ComponentType;
				Patch->TryGetStringField(TEXT("componentType"), ComponentType);
				if (ComponentType.IsEmpty())
				{
					Patch->TryGetStringField(TEXT("type"), ComponentType);
				}
				Payload->SetStringField(TEXT("class"), ComponentType);
				// parent (DSL [Component(Attach = "...")]) - same field the create-mode component loop
				// already forwards. Without it here, an update-mode blueprint would add the component at
				// root level and the Components panel shows siblings, which is exactly the symptom the
				// create-mode gap produced (user visual check 2026-10-23).
				FString PatchCompParent;
				if (Patch->TryGetStringField(TEXT("parent"), PatchCompParent) && !PatchCompParent.IsEmpty())
				{
					Payload->SetStringField(TEXT("parent"), PatchCompParent);
				}
				bOk = BridgeBool(Client->HandleAddBlueprintComponent(Payload), TEXT("added"));
			}

			const TSharedPtr<FJsonObject>* Props = nullptr;
			if (bOk && !bDelete && Patch->TryGetObjectField(TEXT("properties"), Props) && Props->IsValid())
			{
				for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : (*Props)->Values)
				{
					TSharedPtr<FJsonObject> PropPayload = MakePayload(Ctx.Path);
					PropPayload->SetStringField(TEXT("component"), Name);
					PropPayload->SetStringField(TEXT("property"), Pair.Key);
					PropPayload->SetField(TEXT("value"), Pair.Value);
					Client->HandleSetComponentProperty(PropPayload);
				}
			}
		}
		else if (Type == TEXT("function") && Blueprint != nullptr)
		{
			TSharedPtr<FJsonObject> Payload = MakePayload(Ctx.Path);
			Payload->SetStringField(TEXT("name"), Name);
			if (bDelete)
			{
				bOk = bExists && BridgeBool(Client->HandleRemoveFunctionGraph(Payload), TEXT("removed"));
			}
			else if (!bExists)
			{
				bool bPure = false;
				if (Patch->TryGetBoolField(TEXT("pure"), bPure))
				{
					Payload->SetBoolField(TEXT("pure"), bPure);
				}
				const TArray<TSharedPtr<FJsonValue>>* Inputs = nullptr;
				if (Patch->TryGetArrayField(TEXT("inputs"), Inputs))
				{
					Payload->SetArrayField(TEXT("params"), *Inputs);
				}
				const TArray<TSharedPtr<FJsonValue>>* Outputs = nullptr;
				if (Patch->TryGetArrayField(TEXT("outputs"), Outputs))
				{
					Payload->SetArrayField(TEXT("outputs"), *Outputs);
				}
				bOk = BridgeBool(Client->HandleCreateFunctionGraph(Payload), TEXT("created"));
			}
			else
			{
				Ctx.Warn(TEXT("MEMBER_ALREADY_EXISTS"),
					FString::Printf(TEXT("function '%s' already exists - replace its body via graphNode patches"), *Name));
				bOk = true;
			}
		}
		else if (Type == TEXT("macro") && Blueprint != nullptr)
		{
			UEdGraph* Existing = FindMacroGraphPtr(Blueprint, Name);
			if (bDelete)
			{
				bOk = Existing != nullptr;
				if (Existing) FBlueprintEditorUtils::RemoveGraph(Blueprint, Existing, EGraphRemoveFlags::Recompile);
			}
			else if (Existing == nullptr)
			{
				bOk = BridgeBool(Client->HandleCreateMacroGraph(MacroShellPayload(Ctx.Path, Patch)), TEXT("created"));
			}
			else
			{
				// Preserve the macro graph object; existing macro instances continue referencing the same graph; refuse to overwrite when signature changes.
				TArray<TSharedPtr<FJsonValue>> Inputs, Outputs;
				ExtractMacroSignature(Existing, Inputs, Outputs);
				const auto Desired = MacroShellPayload(Ctx.Path, Patch);
				auto Matches = [](const TArray<TSharedPtr<FJsonValue>>& Current, const TArray<TSharedPtr<FJsonValue>>& Expected)
				{
					if (Current.Num() != Expected.Num()) return false;
					for (int32 I = 0; I < Current.Num(); ++I)
					{
						FEdGraphPinType ActualType, ExpectedType;
						FString Reason;
						const auto A = Current[I]->AsObject();
						const auto E = Expected[I]->AsObject();
						if (A->GetStringField(TEXT("name")) != E->GetStringField(TEXT("name"))
							|| !ForgeMindBlueprintUtils::PinTypeTextToPinType(A->GetStringField(TEXT("type")), ActualType, Reason)
							|| !ForgeMindBlueprintUtils::PinTypeTextToPinType(E->GetStringField(TEXT("type")), ExpectedType, Reason)
							|| ActualType != ExpectedType) return false;
					}
					return true;
				};
				bOk = Matches(Inputs, Desired->GetArrayField(TEXT("params"))) && Matches(Outputs, Desired->GetArrayField(TEXT("outputs")));
				if (bOk)
				{
					const auto OldNodes = Existing->Nodes;
					for (UEdGraphNode* Node : OldNodes)
						if (Node && Node->GetClass() != UK2Node_Tunnel::StaticClass()) Node->DestroyNode();
				}
				else Ctx.Warn(TEXT("BP_PIN_TYPE_MISMATCH"), TEXT("Cannot replace macro with a different signature: ") + Name);
			}
			if (bOk && !bDelete)
			{
				TMap<FString, FString> NodeIds;
				TMap<FString, TMap<FString, FString>> PinRemap;
				TArray<FDeferredPinDefault> Defaults;
				int32 Count = 0;
				const TArray<TSharedPtr<FJsonValue>>* Nodes = nullptr;
				if (Patch->TryGetArrayField(TEXT("nodes"), Nodes))
				{
					for (const auto& Value : *Nodes)
					{
						if (Value->AsObject().IsValid()) AddGraphNode(Ctx.Path, Name, Value->AsObject(), NodeIds, Ctx.Warnings, Count, Defaults, &PinRemap);
					}
				}
				ConnectGraph(Ctx.Path, Patch, NodeIds, Ctx.Warnings, Count, &PinRemap);
				ApplyDeferredPinDefaults(Defaults, Ctx.Warnings);
			}
		}
		else if (Type == TEXT("graph.nodes") || Type == TEXT("graph.connections"))
		{
			FString GraphName;
			Patch->TryGetStringField(TEXT("graphName"), GraphName);
			TMap<FString, FString> Unused;
			int32 Count = 0;
			// Patch path has no subsequent connection phase; attempt deferred defaults once on-site (only warn on failure)
			TArray<FDeferredPinDefault> DeferredDefaults;
			if (Type == TEXT("graph.nodes"))
			{
				bOk = AddGraphNode(Ctx.Path, GraphName, Patch, Unused, Ctx.Warnings, Count, DeferredDefaults);
				ApplyDeferredPinDefaults(DeferredDefaults, Ctx.Warnings);
			}
			else
			{
				ConnectGraph(Ctx.Path, Patch, Unused, Ctx.Warnings, Count);
				bOk = Count > 0;
			}
			// IR rules.md section 4.5: report the inferred op, do not hard-code "add".
			// Graph nodes/connections are added; a delete patch removes them, so the
			// receipt must say "delete" rather than the false "add" it used to.
			ActualOp = bDelete ? TEXT("delete") : TEXT("add");
		}
		else if (Type == TEXT("member"))
		{
			// user_defined_struct members
			TSharedPtr<FJsonObject> Payload = MakePayload(Ctx.Path);
			for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Patch->Values)
			{
				Payload->SetField(Pair.Key, Pair.Value);
			}
			if (bDelete)
			{
				bOk = BridgeBool(Client->HandleRemoveStructMember(Payload), TEXT("removed"));
			}
			else
			{
				bOk = BridgeBool(Client->HandleAddStructMember(Payload), TEXT("added"));
				if (!bOk)
				{
					bOk = BridgeBool(Client->HandleUpdateStructMember(Payload), TEXT("updated"));
					ActualOp = TEXT("replace");
				}
			}
		}
		else if (Type == TEXT("value"))
		{
			// user_defined_enum entries
			TSharedPtr<FJsonObject> Payload = MakePayload(Ctx.Path);
			for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Patch->Values)
			{
				Payload->SetField(Pair.Key, Pair.Value);
			}
			if (bDelete)
			{
				bOk = BridgeBool(Client->HandleRemoveEnumEntry(Payload), TEXT("removed"));
			}
			else
			{
				bOk = BridgeBool(Client->HandleAddEnumEntry(Payload), TEXT("added"));
				if (!bOk)
				{
					bOk = BridgeBool(Client->HandleUpdateEnumEntry(Payload), TEXT("updated"));
					ActualOp = TEXT("replace");
				}
			}
		}
		else
		{
			Ctx.Warn(TEXT("UNSUPPORTED_PATCH_TYPE"),
				FString::Printf(TEXT("patch type '%s' is not handled by the blueprint domain"), *Type));
			continue;
		}

		if (!bOk)
		{
			ActualOp = TEXT("failed");
		}

		// IR rules.md section 8.3: a delete that targets a member which does not
		// exist is MEMBER_NOT_FOUND (not a generic failure). This is the one
		// place a missing member is definitively known, so the code is attached
		// here rather than left to free-text messages.
		FString ErrorCode;
		FString Message;
		if (!bOk)
		{
			if (bDelete && !bExists)
			{
				ErrorCode = TEXT("MEMBER_NOT_FOUND");
				Message = FString::Printf(TEXT("member '%s' not found - cannot delete"), *Name);
			}
			else
			{
				Message = FString::Printf(TEXT("member '%s' could not be %s"), *Name,
					bDelete ? TEXT("deleted") : (bExists ? TEXT("replaced") : TEXT("added")));
			}
		}
		Ctx.AddPatchResult(ActualOp, Type, Name, bOk, Message, ErrorCode);
	}

	return true;
}

bool FBlueprintIRHandler::DeleteAsset(FIRApplyContext& Ctx)
{
	FString Error;
	if (!ForgeMindIR::DeleteAssetObject(Ctx.Path, Error))
	{
		Ctx.Fail(TEXT("DELETE_FAILED"), Error);
		return false;
	}
	return true;
}

bool FBlueprintIRHandler::CompileAsset(UObject* Asset, FIRApplyContext& Ctx)
{
	FForgeMindBridgeClient* Client = Bridge();
	if (Client == nullptr || AsBlueprint(Asset) == nullptr)
	{
		return IIRHandler::CompileAsset(Asset, Ctx);
	}

	TSharedPtr<FJsonObject> Result = Client->HandleCompileBlueprint(MakePayload(Ctx.Path));
	const bool bCompiled = BridgeBool(Result, TEXT("compiled"));
	if (!bCompiled)
	{
		Ctx.Warn(TEXT("COMPILATION_FAILED"), BridgeReason(Result));
	}
	return bCompiled;
}

TSharedPtr<FJsonObject> FBlueprintIRHandler::Read(UObject* Asset, FIRApplyContext& Ctx)
{
	TSharedRef<FJsonObject> Definition = MakeShared<FJsonObject>();

	UBlueprint* Blueprint = AsBlueprint(Asset);
	if (Blueprint == nullptr)
	{
		// LoadAsset may have returned a UBlueprintGeneratedClass or UPackage
		// instead of the UBlueprint source object. Resolve via the path.
		Blueprint = ResolveBlueprintFromPath(Ctx.Path);
	}
	if (Blueprint == nullptr)
	{
		// Level blueprint (level_blueprint): the level-embedded UBlueprint does **not** follow
		// the "<Path>.<AssetName>" form; must use ResolveBlueprint's
		// LevelScriptActor.ClassGeneratedBy fallback (Create/Update branches already use it).
		// Without this step, get_asset_ir on .umap level assets always returns success=false
		// → diff-comparison VerifyIrSnapshot cannot be used on level cases.
		Blueprint = ForgeMindBlueprintUtils::ResolveBlueprint(Ctx.Path);
	}
	if (Blueprint == nullptr)
	{
		// Not a blueprint — try user_defined_struct / user_defined_enum.
		if (UUserDefinedStruct* Struct = Cast<UUserDefinedStruct>(Asset))
		{
			TArray<TSharedPtr<FJsonValue>> StructMembers;
			for (const FStructVariableDescription& Desc : FStructureEditorUtils::GetVarDesc(Struct))
			{
				TSharedRef<FJsonObject> StructEntry = MakeShared<FJsonObject>();
				StructEntry->SetStringField(TEXT("name"), Desc.VarName.ToString());
				if (!Desc.DefaultValue.IsEmpty())
				{
					StructEntry->SetStringField(TEXT("defaultValue"), Desc.DefaultValue);
				}
				StructMembers.Add(MakeShared<FJsonValueObject>(StructEntry));
			}
			Definition->SetArrayField(TEXT("members"), StructMembers);
			return Definition;
		}
		if (UUserDefinedEnum* Enum = Cast<UUserDefinedEnum>(Asset))
		{
			TArray<TSharedPtr<FJsonValue>> Values;
			const int32 EntryCount = Enum->NumEnums();
			for (int32 i = 0; i < EntryCount; ++i)
			{
				const FString EntryName = Enum->GetNameStringByIndex(i);
				if (EntryName.EndsWith(TEXT("_MAX")))
				{
					continue;
				}
				TSharedRef<FJsonObject> EnumEntry = MakeShared<FJsonObject>();
				EnumEntry->SetStringField(TEXT("name"), EntryName);
				EnumEntry->SetNumberField(TEXT("value"), static_cast<double>(Enum->GetValueByIndex(i)));
				Values.Add(MakeShared<FJsonValueObject>(EnumEntry));
			}
			Definition->SetArrayField(TEXT("values"), Values);
			return Definition;
		}
		Ctx.Warn(TEXT("ASSET_TYPE_MISMATCH"), FString::Printf(TEXT("'%s' is not a blueprint family asset"), *Ctx.Path));
		return Definition;
	}

	// ---- summary level: extends, members, components, functions ----
	if (Blueprint->ParentClass != nullptr)
	{
		Definition->SetStringField(TEXT("extends"), Blueprint->ParentClass->GetName());
	}

	TArray<TSharedPtr<FJsonValue>> Members;
	for (const FBPVariableDescription& Variable : Blueprint->NewVariables)
	{
		TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
		Entry->SetStringField(TEXT("name"), Variable.VarName.ToString());
		Entry->SetStringField(TEXT("type"), ForgeMindBlueprintUtils::PinTypeToText(Variable.VarType));
		if (!Variable.DefaultValue.IsEmpty())
		{
			Entry->SetStringField(TEXT("defaultValue"), Variable.DefaultValue);
		}
		Members.Add(MakeShared<FJsonValueObject>(Entry));
	}
	Definition->SetArrayField(TEXT("members"), Members);

	TArray<TSharedPtr<FJsonValue>> Components;
	if (Blueprint->SimpleConstructionScript != nullptr)
	{
		// Parent map (same construction as list_blueprint_components): the attach relation lives on
		// the SCS node graph while GetAllNodes() returns a flat list, so without this map the
		// read-back lost the hierarchy that apply mode had just written - definition.components
		// carried name/type only, and an IR snapshot could not tell "Mesh under Pivot" from
		// "Mesh beside Pivot" (No. 73, user visual check 2026-10-23).
		//
		// The field is written only for a NON-DEFAULT parent: add_blueprint_component without
		// an explicit "parent" hangs the node under the default scene root, and reading such
		// a blueprint back re-applies with the same default, so reporting it would state what
		// the input never specified. Default-attached components therefore produce the same
		// JSON as before hierarchy reporting (user ruling 2026-10-25: baselines of cases
		// before No. 70 must keep matching untouched; only an explicit attach to a
		// non-default component carries information the IR cannot reconstruct).
		TMap<USCS_Node*, USCS_Node*> ComponentParentMap;
		for (USCS_Node* Node : Blueprint->SimpleConstructionScript->GetAllNodes())
		{
			if (Node == nullptr)
			{
				continue;
			}
			for (USCS_Node* Child : Node->ChildNodes)
			{
				if (Child != nullptr)
				{
					ComponentParentMap.Add(Child, Node);
				}
			}
		}

		for (USCS_Node* Node : Blueprint->SimpleConstructionScript->GetAllNodes())
		{
			if (Node == nullptr || Node->ComponentTemplate == nullptr)
			{
				continue;
			}
			TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
			Entry->SetStringField(TEXT("name"), Node->GetVariableName().ToString());
			Entry->SetStringField(TEXT("type"), Node->ComponentTemplate->GetClass()->GetName());
			if (USCS_Node** Parent = ComponentParentMap.Find(Node); Parent != nullptr && *Parent != nullptr
				&& *Parent != Blueprint->SimpleConstructionScript->GetDefaultSceneRootNode())
			{
				Entry->SetStringField(TEXT("parent"), (*Parent)->GetVariableName().ToString());
			}
			Components.Add(MakeShared<FJsonValueObject>(Entry));
		}
	}
	Definition->SetArrayField(TEXT("components"), Components);

	TArray<TSharedPtr<FJsonValue>> Functions;
	for (UEdGraph* Graph : Blueprint->FunctionGraphs)
	{
		if (Graph == nullptr)
		{
			continue;
		}
		TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
		Entry->SetStringField(TEXT("name"), Graph->GetName());
		Entry->SetStringField(TEXT("type"), TEXT("function"));

		// summary level: function inputs/outputs (signature)
		TArray<TSharedPtr<FJsonValue>> Inputs;
		TArray<TSharedPtr<FJsonValue>> Outputs;
		ExtractFunctionSignature(Graph, Inputs, Outputs);
		Entry->SetArrayField(TEXT("inputs"), Inputs);
		Entry->SetArrayField(TEXT("outputs"), Outputs);

		// Function local variables (same-named field on apply side: functions[].locals). Output only when non-empty —
		// an empty array would also desync the existing baseline (IR snapshot) entirely.
		TArray<TSharedPtr<FJsonValue>> Locals;
		ExtractFunctionLocals(Graph, Locals);
		if (Locals.Num() > 0)
		{
			Entry->SetArrayField(TEXT("locals"), Locals);
		}

		// graph/full level: nodes + connections
		if (Ctx.Level == TEXT("graph") || Ctx.Level == TEXT("full"))
		{
			Entry->SetObjectField(TEXT("graph"), GraphToIR(Graph, Graph->GetName(), TEXT("function"), Ctx.Level));
		}

		Functions.Add(MakeShared<FJsonValueObject>(Entry));
	}
	// Interface implementation graphs compile into real functions but live on
	// ImplementedInterfaces[].Graphs, never on FunctionGraphs (ImplementNewInterface
	// registers them there), so the loop above cannot see them. Append them so IR --
	// and therefore snapshot comparison -- covers implemented interface messages.
	for (auto& Desc : Blueprint->ImplementedInterfaces)
	{
		for (UEdGraph* Graph : Desc.Graphs)
		{
			if (Graph == nullptr) { continue; }
			TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
			Entry->SetStringField(TEXT("name"), Graph->GetName());
			Entry->SetStringField(TEXT("type"), TEXT("function"));

			TArray<TSharedPtr<FJsonValue>> Inputs;
			TArray<TSharedPtr<FJsonValue>> Outputs;
			ExtractFunctionSignature(Graph, Inputs, Outputs);
			Entry->SetArrayField(TEXT("inputs"), Inputs);
			Entry->SetArrayField(TEXT("outputs"), Outputs);

			if (Ctx.Level == TEXT("graph") || Ctx.Level == TEXT("full"))
			{
				Entry->SetObjectField(TEXT("graph"), GraphToIR(Graph, Graph->GetName(), TEXT("function"), Ctx.Level));
			}

			Functions.Add(MakeShared<FJsonValueObject>(Entry));
		}
	}
	Definition->SetArrayField(TEXT("functions"), Functions);

	// graph/full level: event graphs (ubergraph pages)
	if (Ctx.Level == TEXT("graph") || Ctx.Level == TEXT("full"))
	{
		TArray<TSharedPtr<FJsonValue>> EventGraphs;
		for (UEdGraph* Graph : Blueprint->UbergraphPages)
		{
			if (Graph == nullptr)
			{
				continue;
			}
			EventGraphs.Add(MakeShared<FJsonValueObject>(
				GraphToIR(Graph, Graph->GetName(), TEXT("eventGraph"), Ctx.Level)));
		}
		if (EventGraphs.Num() > 0)
		{
			Definition->SetArrayField(TEXT("eventGraphs"), EventGraphs);
		}
	}

	// Macro graphs (Blueprint->MacroGraphs): signature from tunnel pins, body exported in the same form as function graphs.
	// Previously IR contained no macro graphs at all — issues inside macro bodies (execution pins, macro call nodes, loop limit
	// connections) were invisible to IR snapshots, so diff-comparison could only cover call graphs.
	// Like eventGraphs, output only when non-empty to avoid disturbing existing baselines of macro-free assets.
	{
		TArray<TSharedPtr<FJsonValue>> Macros;
		for (UEdGraph* Graph : Blueprint->MacroGraphs)
		{
			if (Graph == nullptr)
			{
				continue;
			}
			TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
			Entry->SetStringField(TEXT("name"), Graph->GetName());
			Entry->SetStringField(TEXT("type"), TEXT("macro"));

			TArray<TSharedPtr<FJsonValue>> Inputs;
			TArray<TSharedPtr<FJsonValue>> Outputs;
			ExtractMacroSignature(Graph, Inputs, Outputs);
			Entry->SetArrayField(TEXT("inputs"), Inputs);
			Entry->SetArrayField(TEXT("outputs"), Outputs);

			if (Ctx.Level == TEXT("graph") || Ctx.Level == TEXT("full"))
			{
				Entry->SetObjectField(TEXT("graph"), GraphToIR(Graph, Graph->GetName(), TEXT("macro"), Ctx.Level));
			}
			Macros.Add(MakeShared<FJsonValueObject>(Entry));
		}
		if (Macros.Num() > 0)
		{
			Definition->SetArrayField(TEXT("macros"), Macros);
		}
	}

	return Definition;
}


TSharedRef<IIRHandler> MakeBlueprintIRHandler()
{
	return MakeShared<FBlueprintIRHandler>();
}