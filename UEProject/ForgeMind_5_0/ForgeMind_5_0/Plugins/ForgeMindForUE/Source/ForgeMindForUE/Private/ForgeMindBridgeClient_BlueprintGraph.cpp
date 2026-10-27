// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Blueprint graph commands split out of ForgeMindBridgeClient.cpp -
// same class, shared helpers live in ForgeMindBlueprintUtils.

#include "ForgeMindBridgeClient.h"

#include "ForgeMindBlueprintUtils.h"
#include "ForgeMindGraphLayout.h"

#include "AssetRegistry/AssetRegistryModule.h"
#include "UObject/ConstructorHelpers.h"
#include "UObject/Class.h"
#include "Misc/DefaultValueHelper.h"
#include "Misc/OutputDeviceNull.h"
#include "Dom/JsonObject.h"
#include "Dom/JsonValue.h"
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphNode.h"
#include "EdGraphNode_Comment.h"
#include "EdGraph/EdGraphPin.h"
#include "EdGraphSchema_K2.h"
#include "K2Node_FunctionEntry.h"
#include "K2Node_FunctionResult.h"
#include "Engine/Blueprint.h"
#include "Engine/BlueprintGeneratedClass.h"
#include "HAL/FileManager.h"
#include "K2Node_CallFunction.h"
#include "K2Node_Message.h"
#include "K2Node_CallParentFunction.h"
#include "K2Node_CallArrayFunction.h"
#include "K2Node_ComponentBoundEvent.h"
#include "K2Node_CustomEvent.h"
#include "K2Node_AssignmentStatement.h"
#include "K2Node_TemporaryVariable.h"
#include "K2Node_DynamicCast.h"
#include "K2Node_GetClassDefaults.h"
#include "K2Node_EnumEquality.h"
#include "K2Node_EnumInequality.h"
#include "K2Node_EnumLiteral.h"
#include "K2Node_BitmaskLiteral.h"
// Format Text node (UK2Node_FormatText): pure editor K2 node with no backing
// UFUNCTION - the engine's UKismetTextLibrary::Format is BlueprintInternalUseOnly,
// expanded by this node at compile time.
#include "K2Node_FormatText.h"
// Timeline node (UK2Node_Timeline): creates the blueprint's timeline template
// (UTimelineTemplate) plus the graph node; the node's AllocateDefaultPins
// resolves tracks from the template.
#include "K2Node_Timeline.h"
#include "Engine/TimelineTemplate.h"
#include "Kismet2/BlueprintEditorUtils.h"
// Timeline track curve objects (DSL Curve((t, v), ...) / At(t, ...)): the tracks
// own UCurveFloat / UCurveVector / UCurveLinearColor instances holding the
// keyframes; FRichCurve carries the keys themselves.
#include "Curves/CurveFloat.h"
#include "Curves/CurveVector.h"
#include "Curves/CurveLinearColor.h"
#include "Curves/RichCurve.h"
// Enum iteration: UK2Node_ForEachElementInEnum ("ForEach {EnumName}"). Its LoopBody /
// EnumValue pins only exist when the node's Enum member is set before AllocateDefaultPins.
#include "K2Node_ForEachElementInEnum.h"
#include "K2Node_Event.h"
#include "K2Node_BaseMCDelegate.h"
#include "K2Node_CallDelegate.h"
#include "K2Node_CreateDelegate.h"
#include "K2Node_AddDelegate.h"
#include "K2Node_RemoveDelegate.h"
#include "K2Node_ExecutionSequence.h"
#include "K2Node_MultiGate.h"
#include "K2Node_IfThenElse.h"
#include "K2Node_Literal.h"
#include "K2Node_MacroInstance.h"
#include "K2Node_MathExpression.h"
#include "K2Node_SpawnActorFromClass.h"
#include "K2Node_AddComponent.h"
#include "K2Node_MakeArray.h"
#include "K2Node_MakeContainer.h"
#include "K2Node_MakeMap.h"
#include "K2Node_MakeSet.h"
#include "K2Node_MakeStruct.h"
#include "K2Node_BreakStruct.h"
#include "K2Node_GetDataTableRow.h"
#include "K2Node_Self.h"
#include "K2Node_Select.h"
#include "K2Node_StructOperation.h"
#include "K2Node_SwitchEnum.h"
#include "K2Node_SwitchInteger.h"
#include "K2Node_SwitchString.h"
#include "K2Node_Tunnel.h"
#include "K2Node_Variable.h"
#include "K2Node_VariableGet.h"
#include "K2Node_VariableSet.h"
#include "K2Node_GetSubsystem.h"
#include "K2Node_InputAxisEvent.h"
#include "K2Node_InputAction.h"
#include "K2Node_InputKey.h"
#include "GameFramework/InputSettings.h"
#include "Engine/Level.h"
#include "Engine/World.h"
#include "Editor.h"
#include "Kismet/GameplayStatics.h"
#include "Kismet2/BlueprintEditorUtils.h"
#include "Kismet2/KismetEditorUtilities.h"
#include "Misc/OutputDevice.h"
#include "Misc/PackageName.h"
#include "Misc/Paths.h"
#include "ObjectTools.h"
#include "UObject/Package.h"
#include "UObject/SavePackage.h"
#include "UObject/UnrealType.h"
#include "UObject/UObjectGlobals.h"
#include "UObject/UObjectIterator.h"

using namespace ForgeMindBlueprintUtils;

namespace
{
	/** FString::Join does not exist in 5.6; the utils version is static. */
	FString JoinStrings(const TArray<FString>& Items, const TCHAR* Delim)
	{
		FString Out;
		for (const FString& Item : Items)
		{
			if (!Out.IsEmpty())
			{
				Out += Delim;
			}
			Out += Item;
		}
		return Out;
	}

	/**
	 * Native K2 flow-control nodes are neither UFunctions nor macro graphs,
	 * so the regular add_blueprint_node lookups never find them. Match them
	 * by their context-menu name instead; extend here when more are needed.
	 *
	 * Supported names (case-insensitive):
	 *   Branch               — UK2Node_IfThenElse
	 *   SwitchOnInt          — UK2Node_SwitchInteger  (cases via payload 'cases')
	 *   SwitchOnString       — UK2Node_SwitchString   (cases via payload 'cases')
	 *   SwitchOnEnum         — UK2Node_SwitchEnum     (enum via payload 'enum')
	 *   EqualEnum            — UK2Node_EnumEquality   (dedicated enum "Equal (Enum)" node; A/B wildcards conform from connections)
	 *   NotEqualEnum         — UK2Node_EnumInequality (dedicated enum "!=" node, same wildcard conform)
	 *   EnumLiteral          — UK2Node_EnumLiteral    (enum dropdown literal; enum via payload 'enum', entry via 'properties' on the typed "Enum" input pin)
	 *   Cast                 — UK2Node_DynamicCast    (target class via payload 'targetClass')
	 *   Select               — UK2Node_Select         (option count via payload 'options'; enum via payload 'enum' → SetEnum, option pins then named by the enum entries)
	 *   SpawnActorFromClass  — UK2Node_SpawnActorFromClass (spawn class via payload 'targetClass' → Class pin default)
	 *   AddComponent         — UK2Node_AddComponent   (component class via payload 'targetClass' → TemplateType)
	 */
	bool IsNativeFlowNodeName(const FString& Name)
	{
	return Name == TEXT("Branch")
		|| Name == TEXT("Sequence")
		|| Name == TEXT("MultiGate")
		|| Name == TEXT("SwitchOnInt")
		|| Name == TEXT("SwitchOnString")
		|| Name == TEXT("SwitchOnEnum")
		|| Name == TEXT("EqualEnum")
		|| Name == TEXT("NotEqualEnum")
		|| Name == TEXT("EnumLiteral")
		|| Name == TEXT("ForEachElementInEnum")
		|| Name == TEXT("Cast")
		|| Name == TEXT("Select")
		|| Name == TEXT("CreateDelegate")
		|| Name == TEXT("SpawnActorFromClass")
		|| Name == TEXT("AddComponent")
		// P8: data construction nodes
		|| Name == TEXT("MakeArray")
		|| Name == TEXT("MakeSet")
		|| Name == TEXT("MakeMap")
		|| Name == TEXT("MakeStruct")
		|| Name == TEXT("BreakStruct")
		|| Name == TEXT("ConstructObjectFromClass")
		// GetClassDefaults: UK2Node_GetClassDefaults ("Get Class Defaults") -
		// pure K2 node with no backing UFunction; resolved by context-menu
		// name like the flow nodes above. Target class via payload
		// 'targetClass' -> Class pin default (see ConfigureNativeFlowNode).
		|| Name == TEXT("GetClassDefaults")
		// GetDataTableRow: UK2Node_GetDataTableRow ("Get Data Table Row") - the
		// engine's dedicated node. It cannot be a plain CallFunction on
		// UDataTableFunctionLibrary::GetDataTableRowFromName: that function's
		// OutRow is a CustomStructureParam wildcard that only resolves from the
		// DataTable pin's DefaultObject, and a pin fed by a variable link has
		// none (the bridge refuses a default on a wired pin). UK2Node_GetDataTableRow
		// covers that case - GetDataTableRowStructType falls back to the Out Row
		// pin's own link when the table pin carries no DefaultObject.
		|| Name == TEXT("GetDataTableRow")
		// MakeBitmask: UK2Node_BitmaskLiteral ("Make Bitmask") - pure node that
		// wraps a bitflags enum literal; expands to MakeLiteralInt at compile.
		// BitflagsEnum is set from payload 'enum' in ConfigureNativeFlowNode.
		|| Name == TEXT("MakeBitmask")
		// FormatText: UK2Node_FormatText ("Format Text") - pure editor K2 node
		// with no backing UFUNCTION (UKismetTextLibrary::Format is
		// BlueprintInternalUseOnly, expanded by this node at compile).
		// "Format" text pin + "Result" output; {0}-placeholder argument pins
		// are created by the node's PinDefaultValueChanged after the Format
		// pin's default text is set (set_pin_default_value routes PC_Text
		// pins through Schema->TrySetDefaultText for exactly that).
		|| Name == TEXT("FormatText");
	}

	/**
	 * Factory for the names accepted by IsNativeFlowNodeName.
	 * Parameterized nodes (Switch, Cast, Select) read their configuration
	 * from the payload; the caller passes it through so the factory can
	 * configure the node before AllocateDefaultPins.
	 */
	UK2Node* CreateNativeFlowNode(const FString& Name, UEdGraph* Graph, const TSharedPtr<FJsonObject>& Payload)
	{
	if (Name == TEXT("Branch"))
	{
		return NewObject<UK2Node_IfThenElse>(Graph);
	}
	if (Name == TEXT("Sequence"))
	{
		return NewObject<UK2Node_ExecutionSequence>(Graph);
	}
	if (Name == TEXT("MultiGate"))
	{
		return NewObject<UK2Node_MultiGate>(Graph);
	}
		if (Name == TEXT("SwitchOnInt"))
		{
			return NewObject<UK2Node_SwitchInteger>(Graph);
		}
		if (Name == TEXT("SwitchOnString"))
		{
			return NewObject<UK2Node_SwitchString>(Graph);
		}
		if (Name == TEXT("SwitchOnEnum"))
		{
			return NewObject<UK2Node_SwitchEnum>(Graph);
		}
		if (Name == TEXT("EqualEnum"))
		{
			return NewObject<UK2Node_EnumEquality>(Graph);
		}
		if (Name == TEXT("NotEqualEnum"))
		{
			return NewObject<UK2Node_EnumInequality>(Graph);
		}
		if (Name == TEXT("EnumLiteral"))
		{
			return NewObject<UK2Node_EnumLiteral>(Graph);
		}
		if (Name == TEXT("ForEachElementInEnum"))
		{
			return NewObject<UK2Node_ForEachElementInEnum>(Graph);
		}
		if (Name == TEXT("Cast"))
		{
			return NewObject<UK2Node_DynamicCast>(Graph);
		}
		if (Name == TEXT("CreateDelegate"))
		{
			return NewObject<UK2Node_CreateDelegate>(Graph);
		}
		if (Name == TEXT("SpawnActorFromClass"))
		{
			return NewObject<UK2Node_SpawnActorFromClass>(Graph);
		}
		if (Name == TEXT("AddComponent"))
		{
			return NewObject<UK2Node_AddComponent>(Graph);
		}
		if (Name == TEXT("Select"))
		{
			return NewObject<UK2Node_Select>(Graph);
		}
		// P8: data construction nodes - MakeContainer family (MakeArray/MakeSet/MakeMap)
		// and struct operation family (MakeStruct/BreakStruct). The constructors
		// set ContainerType and the AllocateDefaultPins call generates wildcard
		// pins (container family) or struct-member pins (struct family).
		if (Name == TEXT("MakeArray"))
		{
			return NewObject<UK2Node_MakeArray>(Graph);
		}
		if (Name == TEXT("MakeSet"))
		{
			return NewObject<UK2Node_MakeSet>(Graph);
		}
		if (Name == TEXT("MakeMap"))
		{
			return NewObject<UK2Node_MakeMap>(Graph);
		}
		if (Name == TEXT("MakeStruct"))
		{
			return NewObject<UK2Node_MakeStruct>(Graph);
		}
		if (Name == TEXT("BreakStruct"))
		{
			return NewObject<UK2Node_BreakStruct>(Graph);
		}
		// GetDataTableRow: UK2Node_GetDataTableRow (Get Data Table Row). Pins are
		// exec in/out, "DataTable" (object), "RowName" (FName) and "ReturnValue"
		// (the Out Row wildcard). See IsNativeFlowNodeName for why this is the
		// node a table held in a variable has to use.
		if (Name == TEXT("GetDataTableRow"))
		{
			return NewObject<UK2Node_GetDataTableRow>(Graph);
		}
		// GetClassDefaults: UK2Node_GetClassDefaults (Get Class Defaults).
		// Pure node with no UFunction behind it; output pins are per-property
		// pins generated from the Class input pin's class - they are created
		// in ConfigureNativeFlowNode (via PinDefaultValueChanged -> 
		// OnClassPinChanged) after the target class is known, so the factory
		// only spawns the node without allocating pins.
		if (Name == TEXT("GetClassDefaults"))
		{
			return NewObject<UK2Node_GetClassDefaults>(Graph);
		}
		// MakeBitmask: UK2Node_BitmaskLiteral ("Make Bitmask"). Pure node with
		// "Bitmask" input and "ReturnValue" output pins (both PC_Int + PSC_Bitmask).
		// BitflagsEnum must be set before AllocateDefaultPins (pins take their
		// enum type from it); ConfigureNativeFlowNode handles that from payload 'enum'.
		if (Name == TEXT("MakeBitmask"))
		{
			return NewObject<UK2Node_BitmaskLiteral>(Graph);
		}
		// FormatText: UK2Node_FormatText (Format Text). Pins are "Format"
		// (PC_Text) input + "Result" (PC_Text) output; {0}-placeholder argument
		// pins (PC_Wildcard) are auto-created by the node's
		// PinDefaultValueChanged when the Format pin's default text is set
		// (set_pin_default_value routes PC_Text pins through
		// Schema->TrySetDefaultText for exactly that).
		if (Name == TEXT("FormatText"))
		{
			return NewObject<UK2Node_FormatText>(Graph);
		}
		// ConstructObjectFromClass: UK2Node_ConstructObjectFromClass is abstract,
		// UK2Node_GenericCreateObject (its concrete subclass) has ExpandNode but
		// no CreateNodeHandler registered with the blueprint compiler — the node
		// is silently dropped when the blueprint is saved & reloaded.
		// Instead, create a UK2Node_CallFunction targeting
		// UGameplayStatics::SpawnObject (the same function GenericCreateObject's
		// ExpandNode lowers to). The node survives compilation and serialization.
		if (Name == TEXT("ConstructObjectFromClass"))
		{
			UK2Node_CallFunction* CallNode = NewObject<UK2Node_CallFunction>(Graph);
			CallNode->FunctionReference.SetExternalMember(
				GET_FUNCTION_NAME_CHECKED(UGameplayStatics, SpawnObject),
				UGameplayStatics::StaticClass());
			return CallNode;
		}
		return nullptr;
	}

	/**
	 * Resolves a class by bare name: engine type prefix tolerance
	 * (UBoxComponent -> BoxComponent), blueprint-generated "_C" suffix,
	 * then known asset roots (same fallback chain as the Cast branch).
	 * Used by the AddDelegate/RemoveDelegate branch to find the delegate
	 * property on the receiver variable's declared type ('targetType').
	 */
	UClass* ResolveClassFromName(const FString& Name)
	{
		UClass* Found = ForgeMindFindFirstObject<UClass>(*Name);
		if (Found == nullptr && Name.Len() > 1 && (Name[0] == TEXT('U') || Name[0] == TEXT('A') || Name[0] == TEXT('F')))
		{
			Found = ForgeMindFindFirstObject<UClass>(*Name.RightChop(1));
		}
		if (Found == nullptr)
		{
			// The real name of a blueprint generated class is ClassName + "_C" (aligned with Cast branch resolution rules)
			Found = ForgeMindFindFirstObject<UClass>(*(Name + TEXT("_C")));
		}
		if (Found == nullptr)
		{
			for (const FString& Root : { FString(TEXT("/Game/Tests")), FString(TEXT("/Game")) })
			{
				FString BpAssetPath = FString::Printf(TEXT("%s/%s.%s"), *Root, *Name, *Name);
				if (UBlueprint* Bp = LoadObject<UBlueprint>(nullptr, *BpAssetPath))
				{
					Found = Bp->GeneratedClass;
					if (Found != nullptr) break;
				}
			}
		}
		return Found;
	}

	/**
	 * Post-creation configuration for parameterized native flow nodes.
	 * Called after CreateNewGuid/PostPlacedNewNode but BEFORE AllocateDefaultPins
	 * so the node can use its configuration to drive pin creation.
	 *
	 * - SwitchOnInt:    'cases' (array of integers) adds output exec pins
	 *                   named after each value; bHasDefaultPin defaults to true.
	 *                   Pins are allocated inside this function (Default pin
	 *                   first); returns true so the caller skips its own
	 *                   AllocateDefaultPins call.
	 * - SwitchOnString:  'cases' (array of strings) adds output exec pins
	 *                   via PinNames; bHasDefaultPin defaults to true.
	 *                   Pins are allocated inside this function, same as
	 *                   SwitchOnInt (returns true).
	 * - SwitchOnEnum:   'enum' (UEnum path or name) binds the enum; case pins
	 *                   are auto-generated for every entry. No default pin.
	 * - Cast:           'targetClass' (class path or name) sets TargetType
	 *                   so the cast result pin has the right type.
	 * - Select:          'options' (int) sets the initial option pin count;
	 *                   the first option pin's type drives the wildcard.
	 */
	/**
	 * Resolves the 'enum' payload field of enum-parameterized nodes
	 * (SwitchOnEnum, EnumLiteral). Resolution is a single convergence point:
	 * ResolveUserEnumReference (/Game|/Script asset path, UEnum short name,
	 * E-prefixed short name, DSL user-enum declaration alias). Returns nullptr
	 * when unresolvable.
	 */
	UEnum* ResolveEnumPayloadValue(const FString& EnumPath)
	{
		return ResolveUserEnumReference(EnumPath);
	}

	bool ConfigureNativeFlowNode(UK2Node* Node, const FString& Name, const TSharedPtr<FJsonObject>& Payload)
	{
		if (Name == TEXT("SwitchOnInt"))
		{
			UK2Node_SwitchInteger* Switch = Cast<UK2Node_SwitchInteger>(Node);
			if (Switch == nullptr) { return false; }
			Switch->bHasDefaultPin = true;
			// Default pin must be created before case pins: the engine's
			// UK2Node_SwitchInteger::ReallocatePinsDuringReconstruction assumes
			// OldPins[0] is the Default pin (loop starts from i=1); if case pins come first,
			// compile reconstruction treats OldPins[0]'s "0" as Default and skips it, renames "1"
			// to "0" -- visually only "0" and "Default" remain.
			// Also, case output pins are named by case value (engine AddPinToSwitchNode
			// names sequentially from StartIndex; non-sequential case values will be misaligned).
			Switch->AllocateDefaultPins();
			const TArray<TSharedPtr<FJsonValue>>* Cases = nullptr;
			if (Payload.IsValid() && Payload->TryGetArrayField(TEXT("cases"), Cases))
			{
				for (const TSharedPtr<FJsonValue>& CaseValue : *Cases)
				{
					double Num = 0.0;
					if (CaseValue.IsValid() && CaseValue->TryGetNumber(Num))
					{
						const FName PinName(*FString::Printf(TEXT("%d"), static_cast<int32>(Num)));
						if (Switch->FindPin(PinName) == nullptr)
						{
							Switch->CreatePin(EGPD_Output, UEdGraphSchema_K2::PC_Exec, PinName);
						}
					}
				}
			}
			// Pins already allocated in this branch; caller should not re-allocate
			// (re-calling creates another set of Default/exec/Selection pins).
			return true;
		}
		else if (Name == TEXT("SwitchOnString"))
		{
			UK2Node_SwitchString* Switch = Cast<UK2Node_SwitchString>(Node);
			if (Switch == nullptr) { return false; }
			Switch->bHasDefaultPin = true;
			// Same as SwitchOnInt: Default pin must be created before case pins (engine
			// reconstruction assumes OldPins[0] is Default). Case pins must also be created
			// only once here: if CreatePin is called before the outer AllocateDefaultPins,
			// its CreateCasePins will create another set of same-named pins -- the copy
			// connected to wires will be discarded during compile reconstruction, visually
			// showing disconnected case outputs (e.g. "A" branch).
			Switch->AllocateDefaultPins();
			// PinNames drives both the case exec pins and their labels.
			const TArray<TSharedPtr<FJsonValue>>* Cases = nullptr;
			if (Payload.IsValid() && Payload->TryGetArrayField(TEXT("cases"), Cases))
			{
				for (const TSharedPtr<FJsonValue>& CaseValue : *Cases)
				{
					FString CaseStr;
					if (CaseValue.IsValid() && CaseValue->TryGetString(CaseStr) && !CaseStr.IsEmpty())
					{
						const FName PinName(*CaseStr);
						if (!Switch->PinNames.Contains(PinName))
						{
							Switch->PinNames.Add(PinName);
							Switch->CreatePin(EGPD_Output, UEdGraphSchema_K2::PC_Exec, PinName);
						}
					}
				}
			}
			return true;
		}
		else if (Name == TEXT("SwitchOnEnum"))
		{
			UK2Node_SwitchEnum* Switch = Cast<UK2Node_SwitchEnum>(Node);
			if (Switch == nullptr) { return false; }
			// The enum can be specified by asset path (e.g. /Script/Engine.EBlendMode)
			// or by short name (e.g. EBlendMode) - same resolution chain as EnumLiteral.
			FString EnumPath;
			if (Payload.IsValid() && Payload->TryGetStringField(TEXT("enum"), EnumPath) && !EnumPath.IsEmpty())
			{
				// Resolution result must land on node's Enum member: AllocateDefaultPins generates
				// case output pins by it; UK2Node_SwitchEnum::IsConnectionDisallowed
				// also rejects connections where Enum differs from the other pin's Enum (null Enum rejects all).
				if (UEnum* EnumObj = ResolveEnumPayloadValue(EnumPath))
				{
					Switch->Enum = EnumObj;
				}
			}
		}
		else if (Name == TEXT("EnumLiteral"))
		{
			UK2Node_EnumLiteral* Literal = Cast<UK2Node_EnumLiteral>(Node);
			if (Literal == nullptr) { return false; }
			// 'enum' binds the enum type; same resolution chain as SwitchOnEnum.
			// The chosen ENTRY is not a node property - it lives in the "Enum"
			// INPUT pin's DefaultValue (the engine's dropdown writes there,
			// K2Node_EnumLiteral.cpp AddSearchMetaDataInfo reads it back from the
			// pin). Enum must land before AllocateDefaultPins: the "Enum" input and
			// "Return Value" output pins both take their PC_Byte+enum type from it.
			// The entry itself is applied afterwards via the node's 'properties'
			// ("Enum" → entry author name) when the pin is already enum-typed, so
			// set_pin_default_value's enum translation (display name → internal
			// NewEnumeratorN) applies - no wildcard-ordering problem like the
			// Array_Add NewItem pin had.
			FString EnumPath;
			if (Payload.IsValid() && Payload->TryGetStringField(TEXT("enum"), EnumPath) && !EnumPath.IsEmpty())
			{
				if (UEnum* EnumObj = ResolveEnumPayloadValue(EnumPath))
				{
					Literal->Enum = EnumObj;
				}
			}
			// Enum unresolved → pins stay as the engine creates them for a null
			// enum and ValidateNodeDuringCompilation reports "Undefined Enum" at
			// compile - visible instead of silently pin-less.
		}
	else if (Name == TEXT("MakeBitmask"))
	{
		UK2Node_BitmaskLiteral* BitmaskNode = Cast<UK2Node_BitmaskLiteral>(Node);
		if (BitmaskNode == nullptr) { return false; }
		// 'enum' binds the bitflags enum type; same resolution chain as
		// EnumLiteral / SwitchOnEnum. Must land BEFORE AllocateDefaultPins:
		// the "Bitmask" input and "ReturnValue" output pins both take their
		// PC_Int+PSC_Bitmask type from BitflagsEnum. The enum must carry the
		// Bitflags metadata (set by create_blueprint_enum's bitflags payload);
		// otherwise ReconstructNode's ValidateBitflagsEnumType clears it.
		FString EnumPath;
		if (Payload.IsValid() && Payload->TryGetStringField(TEXT("enum"), EnumPath) && !EnumPath.IsEmpty())
		{
			if (UEnum* EnumObj = ResolveEnumPayloadValue(EnumPath))
			{
				BitmaskNode->BitflagsEnum = EnumObj;
			}
		}
	}
	else if (Name == TEXT("ForEachElementInEnum"))
	{
		UK2Node_ForEachElementInEnum* ForEach = Cast<UK2Node_ForEachElementInEnum>(Node);
		if (ForEach == nullptr) { return false; }
		// 'enum' binds the enum type; same resolution chain as SwitchOnEnum / EnumLiteral.
		// It must land BEFORE AllocateDefaultPins: the node only creates SkipHidden /
		// LoopBody / EnumValue when Enum is set (K2Node_ForEachElementInEnum.cpp:150-163),
		// so an unset enum yields a node whose loop pins are missing -- invisible in the
		// response, fatal for every downstream connection.
		FString EnumPath;
		if (Payload.IsValid() && Payload->TryGetStringField(TEXT("enum"), EnumPath) && !EnumPath.IsEmpty())
		{
			if (UEnum* EnumObj = ResolveEnumPayloadValue(EnumPath))
			{
				ForEach->Enum = EnumObj;
			}
		}
	}
	else if (Name == TEXT("CreateDelegate"))
	{
		// CreateDelegate: wraps a CustomEvent as a delegate value for
		// AddDelegate / RemoveDelegate's "Delegate" pin. The binding target is
		// specified by the 'function' field name in payload (e.g. "Handle") -- CustomEvent itself
		// has no delegate output pin, must route through this node.
		UK2Node_CreateDelegate* CreateNode = Cast<UK2Node_CreateDelegate>(Node);
		if (CreateNode == nullptr) { return false; }

		// In UE5 SelectedFunctionName is a public UPROPERTY (BlueprintSearchable).
		// Assign directly instead of calling SetFunction(FName) -- SetFunction internally only assigns,
		// but the caller (HandleAnyChange(true) at the end of flow orchestrator) subsequently triggers
		// HandleAnyChangeWithoutNotifying, which in the case where IsValid() is false and Delegate output pin
		// is unconnected will clear SelectedFunctionName to NAME_None (Inspector shows
		// "Select Function..."). For the case where CustomEvent is the target, UE still cannot
		// ResolveMember<UFunction> (CustomEvent has not been compiled into UFunction yet),
		// so explicitly write SelectedFunctionName and force-restore once after HandleAnyChange
		// (see CreateDelegate branch handling in flow orchestrator).
		FString FunctionName;
		if (Payload.IsValid()
			&& Payload->TryGetStringField(TEXT("function"), FunctionName)
			&& !FunctionName.IsEmpty())
		{
			CreateNode->SelectedFunctionName = FName(*FunctionName);
		}
	}
		else if (Name == TEXT("Cast"))
		{
			UK2Node_DynamicCast* CastNode = Cast<UK2Node_DynamicCast>(Node);
			if (CastNode == nullptr) { return false; }
// Pure Cast (DSL's `x as T`): no exec pins, failed conversion just outputs null,
		// does not interrupt execution flow. SetPurity when pins are not yet established only sets bIsPureCast,
		// then AllocateDefaultPins produces pins as a pure node (IsNodePure -> bIsPureCast);
		// therefore it must be set before AllocateDefaultPins.
		// `if (x is T v)` uses execution Cast (default non-pure, then / CastFailed branching).
			bool bPureCast = false;
			if (Payload.IsValid())
			{
				Payload->TryGetBoolField(TEXT("pureCast"), bPureCast);
			}
			if (bPureCast)
			{
				CastNode->SetPurity(true);
			}
			// Resolve the target class: a full path (e.g. /Script/Engine.Actor)
			// or a class name (e.g. Actor, APawn). The blueprint's own class is
			// NOT used — Cast needs a concrete target type.
			FString TargetClassName;
			if (Payload.IsValid() && Payload->TryGetStringField(TEXT("targetClass"), TargetClassName) && !TargetClassName.IsEmpty())
			{
				UClass* TargetClass = nullptr;
				if (TargetClassName.StartsWith(TEXT("/")))
				{
					TargetClass = LoadObject<UClass>(nullptr, *TargetClassName);
				}
				if (TargetClass == nullptr)
				{
					TargetClass = ForgeMindFindFirstObject<UClass>(*TargetClassName);
				}
				if (TargetClass == nullptr && !TargetClassName.StartsWith(TEXT("U")) && !TargetClassName.StartsWith(TEXT("A")))
				{
					// Try both common prefixes
					TargetClass = ForgeMindFindFirstObject<UClass>(*FString::Printf(TEXT("A%s"), *TargetClassName));
					if (TargetClass == nullptr)
					{
						TargetClass = ForgeMindFindFirstObject<UClass>(*FString::Printf(TEXT("U%s"), *TargetClassName));
					}
				}
				if (TargetClass == nullptr)
				{
// The real name of a blueprint generated class is ClassName + "_C" (aligned with the
				// resolution rules of the CallFunction path); sibling blueprints in the same batch may not
				// be loaded into memory yet, so if not found, load the blueprint with the same name from
				// known asset root directories and get its GeneratedClass.
				// TargetType must be assigned before AllocateDefaultPins so the node can produce
				// typed data output pins (Cast result).
					TargetClass = ForgeMindFindFirstObject<UClass>(*(TargetClassName + TEXT("_C")));
				}
				if (TargetClass == nullptr)
				{
					for (const FString& Root : { FString(TEXT("/Game/Tests")), FString(TEXT("/Game")) })
					{
						FString BpAssetPath = FString::Printf(TEXT("%s/%s.%s"), *Root, *TargetClassName, *TargetClassName);
						if (UBlueprint* Bp = LoadObject<UBlueprint>(nullptr, *BpAssetPath))
						{
							TargetClass = Bp->GeneratedClass;
							if (TargetClass != nullptr) break;
						}
					}
				}
				if (TargetClass != nullptr)
				{
					CastNode->TargetType = TargetClass;
				}
			}
		}
		else if (Name == TEXT("AddComponent"))
		{
			UK2Node_AddComponent* AddComponentNode = Cast<UK2Node_AddComponent>(Node);
			if (AddComponentNode == nullptr) { return false; }
			// Function reference must be bound before creating pins (node itself carries no function name);
			// TemplateType drives the component class related pins in AllocateDefaultPins.
			AddComponentNode->FunctionReference.SetExternalMember(
				UK2Node_AddComponent::GetAddComponentFunctionName(), AActor::StaticClass());
			FString TargetClassName;
			if (Payload.IsValid() && Payload->TryGetStringField(TEXT("targetClass"), TargetClassName) && !TargetClassName.IsEmpty())
			{
				UClass* ComponentClass = nullptr;
				if (TargetClassName.StartsWith(TEXT("/")))
				{
					ComponentClass = LoadObject<UClass>(nullptr, *TargetClassName);
				}
				if (ComponentClass == nullptr)
				{
					ComponentClass = ForgeMindFindFirstObject<UClass>(*TargetClassName);
				}
				if (ComponentClass == nullptr && !TargetClassName.StartsWith(TEXT("U")))
				{
					ComponentClass = ForgeMindFindFirstObject<UClass>(*FString::Printf(TEXT("U%s"), *TargetClassName));
				}
				if (ComponentClass == nullptr)
				{
					ComponentClass = ForgeMindFindFirstObject<UClass>(*(TargetClassName + TEXT("_C")));
				}
				if (ComponentClass != nullptr)
				{
					AddComponentNode->TemplateType = ComponentClass;
				}
			}
		}
		else if (Name == TEXT("MultiGate") || Name == TEXT("Sequence"))
		{
			// UK2Node_MultiGate derives from UK2Node_ExecutionSequence, so one branch covers
			// both: AllocateDefaultPins creates two outputs ("Out 0/1" for MultiGate,
			// "then_0/1" for Sequence), and payload 'outputs' asks for more. Extra outputs go
			// through the shared UK2Node_ExecutionSequence::AddInputPin, whose name comes from
			// GetPinNameGivenIndex -- MultiGate overrides it to "Out %d", so the numbering stays
			// consistent. Capped at 32 (UK2Node_MultiGate::CanAddPin tracks used gates in an int32).
			UK2Node_ExecutionSequence* SeqNode = Cast<UK2Node_ExecutionSequence>(Node);
			if (SeqNode == nullptr) { return false; }
			int32 OutputCount = 2;
			if (Payload.IsValid())
			{
				Payload->TryGetNumberField(TEXT("outputs"), OutputCount);
			}
			OutputCount = FMath::Clamp(OutputCount, 2, 32);
			SeqNode->AllocateDefaultPins();
			for (int32 i = 2; i < OutputCount; ++i)
			{
				SeqNode->AddInputPin();
			}
			return true;
		}
		else if (Name == TEXT("Select"))
		{
			UK2Node_Select* SelectNode = Cast<UK2Node_Select>(Node);
			if (SelectNode == nullptr) { return false; }
			// Select on Enum: payload 'enum' binds the enum through the engine's public
			// SetEnum, same resolution chain as SwitchOnEnum / EnumLiteral.
			//
			// Why it must land here: ConfigureNativeFlowNode runs BEFORE AllocateDefaultPins
			// (the add-node path calls AllocateDefaultPins only when this returns false), and
			// UK2Node_Select::AllocateDefaultPins re-invokes SetEnum(Enum, true) then creates
			// one option pin per EnumEntry (K2Node_Select.cpp:192-215) - so the option pins
			// come out named by the enum instead of the default "Option 0/1".
			//
			// Pin naming (verified in engine source, do not "fix"): SetEnum fills EnumEntries
			// with GetNameStringByIndex, which UUserDefinedEnum does NOT override - a
			// user-created enum's entries are NewEnumerator0/1/2 and those become the real pin
			// names. The authored names (Red/Green/Blue) land in PinFriendlyName
			// (K2Node_Select.cpp:225-228), which the existing set_pin_default / connect
			// GetDisplayName() fallback already resolves. No new fallback is added here - per
			// collaboration spec point 8 name resolution stays "must be queryable".
			FString EnumPath;
			bool bEnumBound = false;
			if (Payload.IsValid() && Payload->TryGetStringField(TEXT("enum"), EnumPath) && !EnumPath.IsEmpty())
			{
				if (UEnum* EnumObj = ResolveEnumPayloadValue(EnumPath))
				{
					SelectNode->SetEnum(EnumObj);
					bEnumBound = true;
				}
			}
			// Select starts with 2 option pins by default; add more if requested. Skipped when
			// an enum is bound: AllocateDefaultPins sizes the option pins from EnumEntries, and
			// growing them here too would fight that count.
			if (!bEnumBound)
			{
				int32 OptionCount = 2;
				if (Payload.IsValid())
				{
					Payload->TryGetNumberField(TEXT("options"), OptionCount);
				}
				OptionCount = FMath::Max(2, OptionCount);
				// The constructor creates 2 option pins; add the remainder.
				for (int32 i = 2; i < OptionCount; ++i)
				{
					SelectNode->AddInputPin();
				}
			}
		}
		else if (Name == TEXT("MakeArray") || Name == TEXT("MakeSet") || Name == TEXT("MakeMap"))
		{
			// MakeContainer family: NumInputs drives the input pin count.
			// The constructor sets ContainerType (Array/Set/Map) and
			// AllocateDefaultPins creates that many wildcard input pins
			// plus one wildcard output pin. Types are inferred at connect
			// time via NotifyPinConnectionListChanged. MakeMap creates
			// key/value pin pairs (2 pins per NumInputs unit).
			UK2Node_MakeContainer* Container = Cast<UK2Node_MakeContainer>(Node);
			if (Container == nullptr) { return false; }
			int32 InputCount = 1;
			if (Payload.IsValid())
			{
				Payload->TryGetNumberField(TEXT("inputs"), InputCount);
			}
			Container->NumInputs = FMath::Max(1, InputCount);
		}
		else if (Name == TEXT("MakeStruct") || Name == TEXT("BreakStruct"))
		{
			// Struct operation family: StructType drives pin creation.
			// AllocateDefaultPins enumerates the struct's BlueprintVisible
			// properties and creates one input (MakeStruct) or output
			// (BreakStruct) pin per property, plus the struct output/input.
			UK2Node_StructOperation* StructOp = Cast<UK2Node_StructOperation>(Node);
			if (StructOp == nullptr) { return false; }
			FString StructName;
			if (!Payload.IsValid() || !Payload->TryGetStringField(TEXT("struct"), StructName) || StructName.IsEmpty())
			{
				// No struct specified - leave StructType null; the node will
				// show "No Struct" and the user can set it later.
				return false;
			}
			// Resolve the struct: try asset path first (e.g.
			// /Script/CoreUObject.LinearColor), then by short name
			// (e.g. LinearColor, FVector).
			UScriptStruct* StructObj = nullptr;
			if (StructName.StartsWith(TEXT("/")))
			{
				StructObj = LoadObject<UScriptStruct>(nullptr, *StructName);
			}
			if (StructObj == nullptr)
			{
				StructObj = ForgeMindFindFirstObject<UScriptStruct>(*StructName);
			}
			// Try common vector/rotator/transform shortcuts
			if (StructObj == nullptr)
			{
			if (StructName == TEXT("Vector")) { StructObj = TBaseStructure<FVector>::Get(); }
			else if (StructName == TEXT("Rotator")) { StructObj = TBaseStructure<FRotator>::Get(); }
			else if (StructName == TEXT("Transform")) { StructObj = TBaseStructure<FTransform>::Get(); }
			else if (StructName == TEXT("LinearColor") || StructName == TEXT("Color")) { StructObj = TBaseStructure<FLinearColor>::Get(); }
			else if (StructName == TEXT("Vector2D")) { StructObj = TBaseStructure<FVector2D>::Get(); }
			else if (StructName == TEXT("HitResult")) { StructObj = TBaseStructure<FHitResult>::Get(); }
			}
			if (StructObj != nullptr)
			{
				StructOp->StructType = StructObj;
			}
		}
		else if (Name == TEXT("GetClassDefaults"))
		{
			UK2Node_GetClassDefaults* CdoNode = Cast<UK2Node_GetClassDefaults>(Node);
			if (CdoNode == nullptr) { return false; }
			// AllocateDefaultPins creates only the Class input pin for this
			// node family; PostPlacedNewNode ran earlier with no pins
			// (FindClassPin returns null), so pin creation starts here.
			CdoNode->AllocateDefaultPins();
			UEdGraphPin* ClassPin = CdoNode->FindClassPin();
			FString TargetClassName;
			if (ClassPin != nullptr && Payload.IsValid()
				&& Payload->TryGetStringField(TEXT("targetClass"), TargetClassName)
				&& !TargetClassName.IsEmpty())
			{
				// Same resolution chain as the Cast branch: full path, bare
				// name, blueprint "_C" suffix, then asset-root load.
				UClass* TargetClass = nullptr;
				if (TargetClassName.StartsWith(TEXT("/")))
				{
					TargetClass = LoadObject<UClass>(nullptr, *TargetClassName);
				}
				if (TargetClass == nullptr)
				{
					TargetClass = ForgeMindFindFirstObject<UClass>(*TargetClassName);
				}
				if (TargetClass == nullptr)
				{
					TargetClass = ForgeMindFindFirstObject<UClass>(*(TargetClassName + TEXT("_C")));
				}
				if (TargetClass == nullptr)
				{
					for (const FString& Root : { FString(TEXT("/Game/Tests")), FString(TEXT("/Game")) })
					{
						FString BpAssetPath = FString::Printf(TEXT("%s/%s.%s"), *Root, *TargetClassName, *TargetClassName);
						if (UBlueprint* Bp = LoadObject<UBlueprint>(nullptr, *BpAssetPath))
						{
							TargetClass = Bp->GeneratedClass;
							if (TargetClass != nullptr) break;
						}
					}
				}
				if (TargetClass != nullptr)
				{
					// PostPlacedNewNode already defaulted the Class pin to the
					// owner blueprint's generated class (BPTYPE_Normal); override
					// it with the target class, then route through the engine's
					// own pin-changed path: PinDefaultValueChanged (virtual)
					// triggers OnClassPinChanged, which regenerates the
					// per-property output pins from the new input class
					// (FClassDefaultsOptionalPinManager) and rewires old pins.
					ClassPin->DefaultObject = TargetClass;
					CdoNode->PinDefaultValueChanged(ClassPin);
				}
			}
			// Pins allocated inside this branch; the caller must skip its own
			// AllocateDefaultPins (a second call would add a duplicate Class pin).
			return true;
		}
		return false;
	}

	/**
	 * Picks the graph new nodes land in: an explicit graphName wins, then
	 * the first event (uber) graph, then the first function graph.
	 */
	UEdGraph* ResolveTargetGraph(UBlueprint* Blueprint, const FString& GraphName, FString& OutReason)
	{
		// Single lookup entry point (ForgeMindBlueprintUtils): ubergraph pages,
		// function graphs, macro graphs and interface implementation graphs. The
		// last group matters here -- a blueprint that implements an interface
		// already owns a graph per message, and it must be found, not created.
		TArray<UEdGraph*> Graphs;
		CollectTargetableGraphs(Blueprint, Graphs);

		TArray<FString> Available;
		UEdGraph* Found = nullptr;
		for (UEdGraph* Graph : Graphs)
		{
			if (Graph == nullptr) { continue; }
			Available.Add(Graph->GetName());
			if (!GraphName.IsEmpty() && Graph->GetName() == GraphName)
			{
				Found = Graph;
				break;
			}
		}

		if (!GraphName.IsEmpty())
		{
			if (Found == nullptr)
			{
				OutReason = FString::Printf(TEXT("No graph named '%s'; available graphs: %s"), *GraphName, *JoinStrings(Available, TEXT(", ")));
			}
			return Found;
		}
		for (UEdGraph* Graph : Graphs)
		{
			if (Graph != nullptr)
			{
				return Graph;
			}
		}
		OutReason = TEXT("The blueprint has no editable graphs yet");
		return nullptr;
	}

	/** Place new nodes to the right of the rightmost existing node. */
	void FindPlacementPosition(const UEdGraph* Graph, int32& OutX, int32& OutY)
	{
		OutX = 0;
		OutY = 0;
		int32 MaxX = INT32_MIN;
		for (const UEdGraphNode* Node : Graph->Nodes)
		{
			if (Node != nullptr && Node->NodePosX > MaxX)
			{
				MaxX = Node->NodePosX;
				OutX = Node->NodePosX + 300;
				OutY = Node->NodePosY;
			}
		}
		if (MaxX == INT32_MIN)
		{
			OutX = 0;
			OutY = 0;
		}
	}

	/** One pin entry; connections back-reference node ids (NodeGuid strings). */
	TSharedRef<FJsonObject> PinToJson(const UEdGraphPin* Pin)
	{
		TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
		Entry->SetStringField(TEXT("name"), Pin->PinName.ToString());
		Entry->SetStringField(TEXT("direction"), Pin->Direction == EGPD_Input ? TEXT("input") : TEXT("output"));
		Entry->SetStringField(TEXT("type"), PinTypeToText(Pin->PinType));

		TArray<TSharedPtr<FJsonValue>> Links;
		for (const UEdGraphPin* Linked : Pin->LinkedTo)
		{
			if (Linked == nullptr || Linked->GetOwningNodeUnchecked() == nullptr)
			{
				continue;
			}
			TSharedRef<FJsonObject> Link = MakeShared<FJsonObject>();
			Link->SetStringField(TEXT("node"), Linked->GetOwningNodeUnchecked()->NodeGuid.ToString());
			Link->SetStringField(TEXT("pin"), Linked->PinName.ToString());
			Links.Add(MakeShared<FJsonValueObject>(Link));
		}
		if (Links.Num() > 0)
		{
			Entry->SetArrayField(TEXT("links"), Links);
		}

		// Node default values: an unwired input pin carries the node's default for
		// that argument -- DSL literals land here as pin defaults. Emit it through the
		// engine's canonical accessor GetDefaultAsString (DefaultObject path /
		// DefaultTextValue / DefaultValue -- the same vocabulary set_pin_default_value
		// reads back with, so dump and write path share one resolver). A connected pin
		// ignores its default, so only unwired input pins take this branch; wired and
		// output pins keep the raw DefaultValue form below.
		if (Pin->Direction == EGPD_Input && Links.Num() == 0)
		{
			const FString DefaultStr = Pin->GetDefaultAsString();
			if (!DefaultStr.IsEmpty())
			{
				Entry->SetStringField(TEXT("defaultValue"), DefaultStr);
			}
		}
		else if (!Pin->DefaultValue.IsEmpty())
		{
			Entry->SetStringField(TEXT("defaultValue"), Pin->DefaultValue);
		}
		return Entry;
	}

	/** One graph entry: nodes (with pins) plus a flattened wire list. */
	TSharedRef<FJsonObject> GraphToJson(const UEdGraph* Graph, const TCHAR* Kind)
	{
		TSharedRef<FJsonObject> GraphEntry = MakeShared<FJsonObject>();
		GraphEntry->SetStringField(TEXT("name"), Graph->GetName());
		GraphEntry->SetStringField(TEXT("kind"), Kind);

		// Nodes are reported depth-first so collapsed-graph sub pages show up
		// right after the composite node that owns them.
		TArray<TSharedPtr<FJsonValue>> NodeEntries;
		TArray<TSharedPtr<FJsonValue>> Connections;
		TSet<const UEdGraph*> Visited;
		TArray<const UEdGraph*> Pending;
		Pending.Push(Graph);
		while (Pending.Num() > 0)
		{
			const UEdGraph* Current = Pending.Pop();
			if (Visited.Contains(Current))
			{
				continue;
			}
			Visited.Add(Current);

			for (const UEdGraphNode* Node : Current->Nodes)
			{
				if (Node == nullptr)
				{
					continue;
				}
				TSharedRef<FJsonObject> NodeEntry = MakeShared<FJsonObject>();
				NodeEntry->SetStringField(TEXT("id"), Node->NodeGuid.ToString());
				NodeEntry->SetStringField(TEXT("class"), Node->GetClass()->GetName());
				NodeEntry->SetStringField(TEXT("title"), Node->GetNodeTitle(ENodeTitleType::FullTitle).ToString());
				NodeEntry->SetNumberField(TEXT("posX"), Node->NodePosX);
				NodeEntry->SetNumberField(TEXT("posY"), Node->NodePosY);
				// Comment boxes carry their own dimensions; emit them so the
				// caller can verify the refit result after move/remove.
				if (Node->IsA<UEdGraphNode_Comment>())
				{
					NodeEntry->SetNumberField(TEXT("width"), Node->NodeWidth);
					NodeEntry->SetNumberField(TEXT("height"), Node->NodeHeight);
				}
				if (!Node->NodeComment.IsEmpty())
				{
					NodeEntry->SetStringField(TEXT("comment"), Node->NodeComment);
				}
				if (Node->bHasCompilerMessage)
				{
					NodeEntry->SetStringField(TEXT("error"), Node->ErrorMsg);
				}
				// Macro tunnels: expose which end is which (bCanHaveOutputs = entry,
				// bCanHaveInputs = exit). A macro with no exec pins has no
				// direction to tell them apart by, so the caller needs this.
				if (const UK2Node_Tunnel* Tunnel = Cast<UK2Node_Tunnel>(Node))
				{
					NodeEntry->SetBoolField(TEXT("canHaveOutputs"), Tunnel->bCanHaveOutputs);
				}
				TArray<TSharedPtr<FJsonValue>> PinEntries;
				for (const UEdGraphPin* Pin : Node->Pins)
				{
					if (Pin == nullptr || Pin->bHidden)
					{
						continue;
					}
					PinEntries.Add(MakeShared<FJsonValueObject>(PinToJson(Pin)));
					if (Pin->Direction == EGPD_Output)
					{
						for (const UEdGraphPin* Linked : Pin->LinkedTo)
						{
							if (Linked == nullptr || Linked->GetOwningNodeUnchecked() == nullptr)
							{
								continue;
							}
							TSharedRef<FJsonObject> Wire = MakeShared<FJsonObject>();
							Wire->SetStringField(TEXT("fromNode"), Node->NodeGuid.ToString());
							Wire->SetStringField(TEXT("fromPin"), Pin->PinName.ToString());
							Wire->SetStringField(TEXT("toNode"), Linked->GetOwningNodeUnchecked()->NodeGuid.ToString());
							Wire->SetStringField(TEXT("toPin"), Linked->PinName.ToString());
							Connections.Add(MakeShared<FJsonValueObject>(Wire));
						}
					}
				}
				NodeEntry->SetArrayField(TEXT("pins"), PinEntries);
				NodeEntries.Add(MakeShared<FJsonValueObject>(NodeEntry));
			}

			for (const UEdGraph* Sub : Current->SubGraphs)
			{
				if (Sub != nullptr)
				{
					Pending.Push(Sub);
				}
			}
		}

		GraphEntry->SetArrayField(TEXT("nodes"), NodeEntries);
		GraphEntry->SetArrayField(TEXT("connections"), Connections);
		return GraphEntry;
	}
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleListBlueprintGraph(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	// 'path' is the canonical blueprint key; accept 'assetPath' as an alias
	// since the tool's own top-level 'path' carries the .uproject instead.
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty())
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path, e.g. /Game/Blueprints/BP_Thing); optional graphName (restrict to one graph by its name; omit to list all graphs)"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s' (create one via create_blueprint first)"), *AssetPath));
		return Result;
	}

	FString GraphName;
	const bool bHasGraphName = Payload->TryGetStringField(TEXT("graphName"), GraphName);

	TArray<TSharedPtr<FJsonValue>> GraphEntries;
	TArray<FString> AvailableNames;
	auto AppendGraph = [&GraphEntries, &AvailableNames, &GraphName, bHasGraphName](const UEdGraph* Graph, const TCHAR* Kind)
	{
		if (Graph == nullptr)
		{
			return;
		}
		AvailableNames.Add(Graph->GetName());
		if (!bHasGraphName || Graph->GetName() == GraphName)
		{
			GraphEntries.Add(MakeShared<FJsonValueObject>(GraphToJson(Graph, Kind)));
		}
	};

	// UbergraphPages hold the event graphs (incl. custom event graphs);
	// the transient EventGraphs array is a compiled duplicate of them.
	for (const UEdGraph* Graph : Blueprint->UbergraphPages)
	{
		AppendGraph(Graph, TEXT("eventGraph"));
	}
	for (const UEdGraph* Graph : Blueprint->FunctionGraphs)
	{
		AppendGraph(Graph, TEXT("function"));
	}
	for (const UEdGraph* Graph : Blueprint->MacroGraphs)
	{
		AppendGraph(Graph, TEXT("macro"));
	}
	// Interface implementation graphs: registered on ImplementedInterfaces[].Graphs
	// only, so they never show up in the three loops above even though the blueprint
	// already owns them (implemented messages must be found, not created).
	for (auto& Desc : Blueprint->ImplementedInterfaces)
	{
		for (const UEdGraph* Graph : Desc.Graphs)
		{
			AppendGraph(Graph, TEXT("function"));
		}
	}

	if (bHasGraphName && GraphEntries.Num() == 0)
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No graph named '%s' in this blueprint; available graphs: %s"), *GraphName, *JoinStrings(AvailableNames, TEXT(", "))));
		return Result;
	}

	Result->SetBoolField(TEXT("found"), true);
	Result->SetStringField(TEXT("path"), Blueprint->GetPathName());
	Result->SetArrayField(TEXT("graphs"), GraphEntries);
	return Result;
}

namespace
{
	/** Whether a reflected function is usable as a blueprint node at all. */
	bool IsBlueprintCallable(const UFunction* Function)
	{
		if (!Function->HasAnyFunctionFlags(FUNC_BlueprintCallable | FUNC_BlueprintPure | FUNC_BlueprintEvent))
		{
			return false;
		}
		// Delegate signatures are bound, not called; private members and the
		// compiler's ubergraph plumbing are not user-facing nodes.
		return !Function->HasAnyFunctionFlags(FUNC_Private | FUNC_Delegate | FUNC_UbergraphFunction);
	}

	/**
	 * Classification convention: a callable with a return value is a
	 * "function", one without is an "event" - and an out (non-const
	 * reference) parameter counts as a return value.
	 */
	TSharedRef<FJsonObject> CallableToJson(const UFunction* Function, const UClass* FromClass)
	{
		bool bHasOut = false;
		TArray<TSharedPtr<FJsonValue>> ParamEntries;
		for (TFieldIterator<FProperty> It(Function); It && It->HasAnyPropertyFlags(CPF_Parm); ++It)
		{
			if (It->HasAnyPropertyFlags(CPF_ReturnParm))
			{
				continue;
			}
			const bool bOut = It->HasAnyPropertyFlags(CPF_OutParm) && !It->HasAnyPropertyFlags(CPF_ConstParm);
			bHasOut |= bOut;
			TSharedRef<FJsonObject> Param = MakeShared<FJsonObject>();
			Param->SetStringField(TEXT("name"), It->GetName());
			Param->SetStringField(TEXT("type"), PropertyTypeToText(*It));
			if (bOut)
			{
				Param->SetBoolField(TEXT("out"), true);
			}
			ParamEntries.Add(MakeShared<FJsonValueObject>(Param));
		}

		const bool bReturns = Function->GetReturnProperty() != nullptr;
		TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
		Entry->SetStringField(TEXT("name"), Function->GetName());
		Entry->SetStringField(TEXT("kind"), (bReturns || bHasOut) ? TEXT("function") : TEXT("event"));
		const FString Category = Function->GetMetaData(TEXT("Category"));
		if (!Category.IsEmpty())
		{
			Entry->SetStringField(TEXT("category"), Category);
		}
		if (Function->HasAnyFunctionFlags(FUNC_BlueprintPure))
		{
			Entry->SetBoolField(TEXT("pure"), true);
		}
		if (Function->HasAnyFunctionFlags(FUNC_Static))
		{
			Entry->SetBoolField(TEXT("static"), true);
		}
		if (FromClass != nullptr)
		{
			Entry->SetStringField(TEXT("from"), FromClass->GetName());
		}
		Entry->SetArrayField(TEXT("params"), ParamEntries);
		return Entry;
	}
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleListBlueprintCatalog(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty())
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path); optional includeInherited (bool, default false - also list callables inherited from the parent class chain), optional class (class name such as KismetSystemLibrary - list that class's blueprint-callable functions in addition to the blueprint's own; library classes are never part of the parent chain, use this for e.g. Print String)"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s' (create one via create_blueprint first)"), *AssetPath));
		return Result;
	}

	bool bIncludeInherited = false;
	bool bHasInheritedKey = false;
	FString InheritedReason;
	if (!TryGetOptionalBool(Payload, TEXT("includeInherited"), bIncludeInherited, bHasInheritedKey, InheritedReason))
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), InheritedReason);
		return Result;
	}

	FString ClassName;
	const bool bHasClass = Payload->TryGetStringField(TEXT("class"), ClassName);

	TArray<TSharedPtr<FJsonValue>> Entries;
	TSet<FName> SeenNames;

	// Own blueprint-defined functions and macros (never "inherited").
	for (const UEdGraph* Graph : Blueprint->FunctionGraphs)
	{
		if (Graph == nullptr || SeenNames.Contains(Graph->GetFName()))
		{
			continue;
		}
		SeenNames.Add(Graph->GetFName());
		// The compiled signature (when present) supplies the parameter list.
		UFunction* Signature = Blueprint->GeneratedClass != nullptr ? Blueprint->GeneratedClass->FindFunctionByName(Graph->GetFName()) : nullptr;
		TSharedRef<FJsonObject> Entry = Signature != nullptr
			? CallableToJson(Signature, nullptr)
			: [&] { auto E = MakeShared<FJsonObject>(); E->SetStringField(TEXT("name"), Graph->GetName()); E->SetStringField(TEXT("kind"), TEXT("function")); E->SetArrayField(TEXT("params"), TArray<TSharedPtr<FJsonValue>>()); return E; }();
		Entry->SetStringField(TEXT("kind"), TEXT("function"));
		Entries.Add(MakeShared<FJsonValueObject>(Entry));
	}
	for (const UEdGraph* Graph : Blueprint->MacroGraphs)
	{
		if (Graph == nullptr || SeenNames.Contains(Graph->GetFName()))
		{
			continue;
		}
		SeenNames.Add(Graph->GetFName());
		TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
		Entry->SetStringField(TEXT("name"), Graph->GetName());
		Entry->SetStringField(TEXT("kind"), TEXT("macro"));
		Entries.Add(MakeShared<FJsonValueObject>(Entry));
	}
	// Custom events declared in the ubergraph pages.
	for (const UEdGraph* Graph : Blueprint->UbergraphPages)
	{
		if (Graph == nullptr)
		{
			continue;
		}
		for (const UEdGraphNode* Node : Graph->Nodes)
		{
			const UK2Node_CustomEvent* CustomEvent = Cast<UK2Node_CustomEvent>(Node);
			if (CustomEvent == nullptr || CustomEvent->CustomFunctionName.IsNone() || SeenNames.Contains(CustomEvent->CustomFunctionName))
			{
				continue;
			}
			SeenNames.Add(CustomEvent->CustomFunctionName);
			TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
			Entry->SetStringField(TEXT("name"), CustomEvent->CustomFunctionName.ToString());
			Entry->SetStringField(TEXT("kind"), TEXT("event"));
			Entries.Add(MakeShared<FJsonValueObject>(Entry));
		}
	}

	// Inherited callables: walk the parent class chain per class so each
	// entry can report where it comes from. Compiled functions of parent
	// blueprints are part of their generated classes and show up here too.
	if (bIncludeInherited)
	{
		for (const UClass* Class = Blueprint->ParentClass; Class != nullptr; Class = Class->GetSuperClass())
		{
			for (TFieldIterator<UFunction> It(Class, EFieldIteratorFlags::ExcludeSuper); It; ++It)
			{
				const UFunction* Function = *It;
				if (!IsBlueprintCallable(Function) || SeenNames.Contains(Function->GetFName()))
				{
					continue;
				}
				SeenNames.Add(Function->GetFName());
				Entries.Add(MakeShared<FJsonValueObject>(CallableToJson(Function, Class)));
			}
		}
	}

	// Optional extra class (e.g. a function library outside the parent
	// chain such as KismetSystemLibrary).
	if (bHasClass)
	{
		UClass* ExtraClass = ForgeMindFindFirstObject<UClass>(*ClassName);
		if (ExtraClass == nullptr && !ClassName.StartsWith(TEXT("U")))
		{
			ExtraClass = ForgeMindFindFirstObject<UClass>(*FString::Printf(TEXT("U%s"), *ClassName));
		}
		if (ExtraClass == nullptr)
		{
			Result->SetBoolField(TEXT("found"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No class named '%s' found (use list_class_hierarchy to look up valid names)"), *ClassName));
			return Result;
		}
		for (TFieldIterator<UFunction> It(ExtraClass, EFieldIteratorFlags::ExcludeSuper); It; ++It)
		{
			const UFunction* Function = *It;
			if (!IsBlueprintCallable(Function) || SeenNames.Contains(Function->GetFName()))
			{
				continue;
			}
			SeenNames.Add(Function->GetFName());
			Entries.Add(MakeShared<FJsonValueObject>(CallableToJson(Function, ExtraClass)));
		}
	}

	Result->SetBoolField(TEXT("found"), true);
	Result->SetStringField(TEXT("path"), Blueprint->GetPathName());
	Result->SetArrayField(TEXT("entries"), Entries);
	return Result;
}

// ---------------------------------------------------------------------------
// Timeline track curve helpers
//
// DSL [Timeline] nested classes declare tracks with their curve data:
//   float v  = Curve((0.0f, 0.0f), (1.0f, 1.0f));   // inline keyframes
//   Vector p = Curve((0.0f, Vector(0, 0, 0)), ...);  // composite value
//   float w  = Curve("/Game/Curves/CV_RampUp");      // external curve asset
//   EventTrack Fire = At(0.5f, 1.5f);                // event keyframe times
// The translator emits them as a "tracks" array (each
// {name, type, keys:[{t,v}], times:[...] | curveAsset}) and the timeline kind
// turns them into UCurveFloat / UCurveVector / UCurveLinearColor objects inside
// the blueprint's package - the same shape the timeline editor produces
// (TimelineTemplate.cpp warns and duplicates curves whose outer is not a
// package, so the package is used as the outer here).
// ---------------------------------------------------------------------------

/** Reads one IR key entry { "t": <seconds>, "v": <number> | [<component>, ...] }. */
static bool ForgeMindReadCurveKey(const TSharedPtr<FJsonValue>& KeyVal, double& OutTime, TArray<double>& OutComponents)
{
	const TSharedPtr<FJsonObject>* KeyObjPtr = nullptr;
	if (!KeyVal.IsValid() || !KeyVal->TryGetObject(KeyObjPtr) || !KeyObjPtr->IsValid())
	{
		return false;
	}
	const TSharedPtr<FJsonObject>& KeyObj = *KeyObjPtr;
	if (!KeyObj->TryGetNumberField(TEXT("t"), OutTime))
	{
		return false;
	}

	// UE 5.0's FJsonObject::TryGetField takes the name only and returns the value
	// by value (the two-argument out-pointer form does not exist in this version).
	const TSharedPtr<FJsonValue> KeyValue = KeyObj->TryGetField(TEXT("v"));
	if (!KeyValue.IsValid())
	{
		return false;
	}

	const TArray<TSharedPtr<FJsonValue>>* ComponentArray = nullptr;
	if (KeyValue->TryGetArray(ComponentArray))
	{
		for (const TSharedPtr<FJsonValue>& Component : *ComponentArray)
		{
			double Value = 0.0;
			if (Component.IsValid())
			{
				Component->TryGetNumber(Value);
			}
			OutComponents.Add(Value);
		}
	}
	else
	{
		double Value = 0.0;
		if (KeyValue->TryGetNumber(Value))
		{
			OutComponents.Add(Value);
		}
	}
	return true;
}

/** Writes the IR keyframes into one rich curve per component (in index order). */
static void ForgeMindApplyKeys(TArray<FRichCurve*> Curves, const TArray<TSharedPtr<FJsonValue>>& Keys)
{
	for (const TSharedPtr<FJsonValue>& KeyVal : Keys)
	{
		double Time = 0.0;
		TArray<double> Components;
		if (!ForgeMindReadCurveKey(KeyVal, Time, Components))
		{
			continue;
		}
		for (int32 Index = 0; Index < Curves.Num(); ++Index)
		{
			if (Curves[Index] == nullptr)
			{
				continue;
			}
			const double Value = Components.IsValidIndex(Index) ? Components[Index] : 0.0;
			const FKeyHandle Handle = Curves[Index]->AddKey(static_cast<float>(Time), static_cast<float>(Value));
			Curves[Index]->SetKeyInterpMode(Handle, RCIM_Linear);
		}
	}
}

/**
 * Curve object factory for INTERNAL timeline curves.
 *
 * Mirrors the editor's own add-track path (STimelineEditor::CreateNewTrack):
 *   NewTrack.CurveFloat = NewObject<UCurveFloat>(OwnerClass, NAME_None, RF_Public);
 * the curve is a sub-object of the blueprint's generated class - NOT a
 * package-level asset. Creating it as a standalone object inside a package makes
 * it surface as its own asset in the content browser / asset manager (one
 * .uasset showing up as several asset icons). The official design is that
 * building a timeline produces NO visible curve asset (visual feedback
 * 2026-10-13); internal curves live under the class and UTimelineTemplate
 * duplicates them into the compiled class at compile time (it only duplicates
 * when the curve's outer is NOT a package).
 *
 * NOTE: the bridge never invents keyframes. It only writes the keys the caller
 * (DSL "tracks" / MCP payload) supplied - it knows the track names, types and
 * the timeline length, but nothing about the intended value range, so seeding
 * default keys here would fabricate data (reverted on 2026-10-13 after review).
 * Curve data must come from the test / DSL on both the A and the B side.
 */
template <typename CurveType>
static CurveType* ForgeMindNewCurve(UObject* Outer, FName DesiredName)
{
	if (Outer == nullptr)
	{
		return nullptr;
	}
	const FName UniqueName = MakeUniqueObjectName(Outer, CurveType::StaticClass(), DesiredName);
	return NewObject<CurveType>(Outer, UniqueName, RF_Public | RF_Transactional);
}

/**
 * External curve asset reference (DSL `Curve("/Game/...")`): the loaded asset
 * keeps its package as outer, which is exactly how UTimelineTemplate tells
 * internal from external curves, so bIsExternalCurve must be set on the track -
 * otherwise the compile-time copy would swallow a shared asset.
 */
template <typename CurveType>
static CurveType* ForgeMindLoadExternalCurve(const FString& AssetPath)
{
	return LoadObject<CurveType>(nullptr, *AssetPath);
}

/** Float track curve: Curve((t, v), ...) keyframes, exactly as supplied. */
static UCurveFloat* ForgeMindBuildCurveFloat(UObject* Outer, FName CurveName,
	const TSharedPtr<FJsonObject>& TrackDef)
{
	UCurveFloat* Curve = ForgeMindNewCurve<UCurveFloat>(Outer, CurveName);
	if (Curve == nullptr)
	{
		return nullptr;
	}
	TArray<FRichCurve*> Curves;
	Curves.Add(&Curve->FloatCurve);
	const TArray<TSharedPtr<FJsonValue>>* Keys = nullptr;
	if (TrackDef->TryGetArrayField(TEXT("keys"), Keys))
	{
		ForgeMindApplyKeys(Curves, *Keys);
	}
	return Curve;
}

/** Vector track curve: the value is a [x, y, z] component array per keyframe. */
static UCurveVector* ForgeMindBuildCurveVector(UObject* Outer, FName CurveName,
	const TSharedPtr<FJsonObject>& TrackDef)
{
	UCurveVector* Curve = ForgeMindNewCurve<UCurveVector>(Outer, CurveName);
	if (Curve == nullptr)
	{
		return nullptr;
	}
	TArray<FRichCurve*> Curves;
	Curves.Add(&Curve->FloatCurves[0]);
	Curves.Add(&Curve->FloatCurves[1]);
	Curves.Add(&Curve->FloatCurves[2]);
	const TArray<TSharedPtr<FJsonValue>>* Keys = nullptr;
	if (TrackDef->TryGetArrayField(TEXT("keys"), Keys))
	{
		ForgeMindApplyKeys(Curves, *Keys);
	}
	return Curve;
}

/** Color track curve: the value is an [r, g, b, a] component array per keyframe. */
static UCurveLinearColor* ForgeMindBuildCurveLinearColor(UObject* Outer, FName CurveName,
	const TSharedPtr<FJsonObject>& TrackDef)
{
	UCurveLinearColor* Curve = ForgeMindNewCurve<UCurveLinearColor>(Outer, CurveName);
	if (Curve == nullptr)
	{
		return nullptr;
	}
	TArray<FRichCurve*> Curves;
	Curves.Add(&Curve->FloatCurves[0]);
	Curves.Add(&Curve->FloatCurves[1]);
	Curves.Add(&Curve->FloatCurves[2]);
	Curves.Add(&Curve->FloatCurves[3]);
	const TArray<TSharedPtr<FJsonValue>>* Keys = nullptr;
	if (TrackDef->TryGetArrayField(TEXT("keys"), Keys))
	{
		ForgeMindApplyKeys(Curves, *Keys);
	}
	return Curve;
}

/**
 * Event track curve: At(t, ...) lists only the firing times, so every keyframe
 * carries value 0 - BlueprintGeneratedClass builds one timeline event per key of
 * this curve (TimelineTemplate::EventTracks[].CurveKeys).
 */
static UCurveFloat* ForgeMindBuildEventCurve(UObject* Outer, FName CurveName, const TArray<TSharedPtr<FJsonValue>>& Times)
{
	UCurveFloat* Curve = ForgeMindNewCurve<UCurveFloat>(Outer, CurveName);
	if (Curve == nullptr)
	{
		return nullptr;
	}
	for (const TSharedPtr<FJsonValue>& TimeVal : Times)
	{
		double Time = 0.0;
		if (!TimeVal.IsValid() || !TimeVal->TryGetNumber(Time))
		{
			continue;
		}
		const FKeyHandle Handle = Curve->FloatCurve.AddKey(static_cast<float>(Time), 0.0f);
		Curve->FloatCurve.SetKeyInterpMode(Handle, RCIM_Linear);
	}
	return Curve;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleAddBlueprintNode(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString MemberName;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty())
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path), name; optional kind ('getter'/'setter' - variable access nodes, 'macro' - macro instance, 'temporaryVariable' - event-graph local variable read node (optional params[0].type declares the variable type), 'assignment' - local variable write node (engine-named Variable/Value data pins); omitted or 'function' - function-call/event node, discover candidates via list_blueprint_catalog); for kind function: class (owner class for library functions, e.g. KismetSystemLibrary); native data construction nodes (kind omitted, name = one of MakeArray/MakeSet/MakeMap/MakeStruct/BreakStruct): MakeArray/MakeSet take 'inputs' (int, element count, default 1), MakeMap takes 'inputs' (int, key-value pair count, default 1), MakeStruct/BreakStruct take 'struct' (struct name or path, e.g. Vector, LinearColor, /Script/CoreUObject.Vector); graphName (target graph, defaults to the first event graph), x/y (node position, defaults to the right of the rightmost node)"));
		return Result;
	}

	// Check kind first — 'self' nodes don't require a name.
	FString KindCheck;
	Payload->TryGetStringField(TEXT("kind"), KindCheck);
	KindCheck = KindCheck.ToLower();
	const bool bSelfKind = KindCheck == TEXT("self");

	if (!bSelfKind)
	{
		if (!Payload->TryGetStringField(TEXT("name"), MemberName) || MemberName.IsEmpty())
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path), name; optional kind ('getter'/'setter' - variable access nodes, 'macro' - macro instance, 'temporaryVariable' - event-graph local variable read node (optional params[0].type declares the variable type), 'assignment' - local variable write node (engine-named Variable/Value data pins); omitted or 'function' - function-call/event node, discover candidates via list_blueprint_catalog); for kind function: class (owner class for library functions, e.g. KismetSystemLibrary); native data construction nodes (kind omitted, name = one of MakeArray/MakeSet/MakeMap/MakeStruct/BreakStruct): MakeArray/MakeSet take 'inputs' (int, element count, default 1), MakeMap takes 'inputs' (int, key-value pair count, default 1), MakeStruct/BreakStruct take 'struct' (struct name or path, e.g. Vector, LinearColor, /Script/CoreUObject.Vector); graphName (target graph, defaults to the first event graph), x/y (node position, defaults to the right of the rightmost node)"));
			return Result;
		}
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s' (create one via create_blueprint first)"), *AssetPath));
		return Result;
	}

	// kind selects the node family: getter/setter build variable access
	// nodes, macro instanciates a macro graph; anything else (the default)
	// resolves 'name' as a function or event.
	FString Kind;
	Payload->TryGetStringField(TEXT("kind"), Kind);
	Kind = Kind.ToLower();
	const bool bGetter = Kind == TEXT("getter") || Kind == TEXT("get");
	const bool bSetter = Kind == TEXT("setter") || Kind == TEXT("set");
	const bool bMacro = Kind == TEXT("macro");
	const bool bCustomEvent = Kind == TEXT("customevent");
	const bool bInputAxis = Kind == TEXT("inputaxis");
	const bool bInputAction = Kind == TEXT("inputaction");
	// Enhanced Input action event (UK2Node_EnhancedInputAction, plugin module
	// InputBlueprintNodes) - distinct from the legacy bInputAction above, which binds a
	// name from the project's ActionMappings. Payload 'targetClass' carries the
	// UInputAction asset.
	const bool bEnhancedInputAction = Kind == TEXT("enhancedinputaction");
	const bool bInputKey = Kind == TEXT("inputkey");
	const bool bSubsystem = Kind == TEXT("subsystem");
const bool bSelf = Kind == TEXT("self");
// Kind has been ToLower()'d above, so compare against all-lowercase
// strings — "callDelegate" etc. would never match after lowercasing.
const bool bCallDelegate = Kind == TEXT("calldelegate");
const bool bAddDelegate = Kind == TEXT("adddelegate");
const bool bRemoveDelegate = Kind == TEXT("removedelegate");
const bool bDispatcherKind = bCallDelegate || bAddDelegate || bRemoveDelegate;
const bool bComponentBoundEvent = Kind == TEXT("componentboundevent");
// Event-graph local variable nodes: UK2Node_TemporaryVariable (read) +
// UK2Node_AssignmentStatement (write). The engine registers the temp var's
// output net as a forced-shared local terminal in event graphs
// (FKCHandler_TemporaryVariable::RegisterNet) - the same pair macro/tunnel
// expansion emits, so they compile in ubergraph pages.
const bool bTemporaryVariable = Kind == TEXT("temporaryvariable");
const bool bAssignment = Kind == TEXT("assignment");
// Call parent function implementation in subclass (Blueprint "Parent: FunctionName" node, UK2Node_CallParentFunction).
const bool bCallParentFunction = Kind == TEXT("callparentfunction");
// Level Actor literal reference (Blueprint "Create Reference to Actor" node,
// UK2Node_Literal): name = Actor name placed in the current level.
const bool bLevelLiteral = Kind == TEXT("levelliteral");
// Interface message call node (UK2Node_Message): calls a function on an
// interface-typed object. 'class' = interface generated class path, 'name' =
// message function name. The node's self pin is PC_Object (engine internally
// casts to interface at compile time via ExpandNode).
const bool bMessage = Kind == TEXT("message");
// Timeline node (UK2Node_Timeline): 'name' = timeline variable name; creates
// the UTimelineTemplate + node with a default float track.
const bool bTimeline = Kind == TEXT("timeline");
const bool bMathExpression = Kind == TEXT("mathexpression");
if (!Kind.IsEmpty() && !bGetter && !bSetter && !bMacro && !bCustomEvent && !bInputAxis && !bInputAction && !bEnhancedInputAction && !bInputKey && !bSubsystem && !bSelf && !bDispatcherKind && !bComponentBoundEvent && !bTemporaryVariable && !bAssignment && !bCallParentFunction && !bLevelLiteral && !bMessage && !bTimeline && !bMathExpression && Kind != TEXT("function") && Kind != TEXT("event"))
{
Result->SetBoolField(TEXT("added"), false);
Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Unknown kind '%s'; supported kinds: function (default), getter, setter, macro, customevent, inputAxis, inputAction, enhancedInputAction, inputKey, subsystem, self, callDelegate, addDelegate, removeDelegate, componentBoundEvent, temporaryVariable, assignment, callParentFunction, levelLiteral, message, timeline"), *Kind));
return Result;
}
const bool bFunctionKind = !bGetter && !bSetter && !bMacro && !bCustomEvent && !bInputAxis && !bInputAction && !bEnhancedInputAction && !bInputKey && !bSubsystem && !bSelf && !bDispatcherKind && !bComponentBoundEvent && !bTemporaryVariable && !bAssignment && !bCallParentFunction && !bLevelLiteral && !bMessage && !bTimeline && !bMathExpression;
// Set when a function-call target resolves to a UInterface — create UK2Node_Message
// instead of K2Node_CallFunction (DSL d.ApplyDamage(5) where d is interface-typed).
bool bInterfaceMessage = false;

// Native flow nodes (Branch, ...) are resolved by name alone - they are
// not UFunctions, so the reflection lookup below must not run for them.
const bool bFlowNode = bFunctionKind && IsNativeFlowNodeName(MemberName);
// Dispatcher nodes (CallDelegate, AddDelegate, RemoveDelegate) are also
// not UFunctions; they reference a multicast delegate property on the
// blueprint. Skip the function lookup for them too.
bool bSelfMember = false;
bool bExternalMember = false;
	// Cross-blueprint call target class (class field explicitly given), elevated to function scope for node branch construction.
	const UClass* LookupClass = nullptr;

	const UFunction* Function = nullptr;
	if (bFunctionKind && !bFlowNode)
	{
		// Where to look up the function: an explicit class wins (function
		// libraries are outside the blueprint's parent chain), otherwise the
		// blueprint's own skeleton class which also covers the whole chain.
		LookupClass = nullptr;
		FString ClassName;
		if (Payload->TryGetStringField(TEXT("class"), ClassName) && !ClassName.IsEmpty())
		{
			UClass* ExtraClass = ForgeMindFindFirstObject<UClass>(*ClassName);
			if (ExtraClass == nullptr && !ClassName.StartsWith(TEXT("U")))
			{
				ExtraClass = ForgeMindFindFirstObject<UClass>(*FString::Printf(TEXT("U%s"), *ClassName));
			}
			if (ExtraClass == nullptr)
			{
				// Blueprint parent class/cross-blueprint host: bare name resolves generated class (_C fallback + asset root load),
				// same chain as create_blueprint's parentClass.
				ExtraClass = ForgeMindResolveBlueprintClassByName(ClassName);
			}
			if (ExtraClass == nullptr)
			{
				Result->SetBoolField(TEXT("added"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No class named '%s' found (use list_class_hierarchy to look up valid names)"), *ClassName));
				return Result;
			}
			LookupClass = ExtraClass;
			// Interface message call disguised as a function call (DSL d.ApplyDamage(5)
			// where d is interface-typed): LookupClass is a UInterface — create a
			// UK2Node_Message instead of K2Node_CallFunction (message self pin is
			// PC_Object, CallFunction would create a self-context pin and fail to
			// compile against the interface signature).
			if (LookupClass->IsChildOf(UInterface::StaticClass()))
			{
				bInterfaceMessage = true;
			}
		}
		else
		{
			// Both members are TSubclassOf<class UObject>; convert to UClass*
			// explicitly (the two TSubclassOf instantiations share no ternary
			// common type).
			const UClass* SkeletonClass = Blueprint->SkeletonGeneratedClass;
			LookupClass = SkeletonClass != nullptr ? SkeletonClass : static_cast<UClass*>(Blueprint->ParentClass);
			if (LookupClass == nullptr)
			{
				Result->SetBoolField(TEXT("added"), false);
				Result->SetStringField(TEXT("reason"), TEXT("The blueprint has no generated class yet - compile it first"));
				return Result;
			}
		}

		// FindFunctionByName searches the whole struct including supers.
		Function = LookupClass->FindFunctionByName(FName(*MemberName));
		if (Function == nullptr && !ClassName.IsEmpty())
		{
			// Blueprint generated class function table may not be ready yet (sibling blueprints just compiled, or
			// generated class/skeleton class name collision causes FindFirstObject to hit one). Load the blueprint asset,
			// check from both GeneratedClass and SkeletonGeneratedClass sides -
			// Edit-time graph nodes referencing skeleton class functions is an engine norm; rebinding by name at compile time.
			for (const FString& Root : { FString(TEXT("/Game/Tests")), FString(TEXT("/Game")) })
			{
				FString BpAssetPath = FString::Printf(TEXT("%s/%s.%s"), *Root, *ClassName, *ClassName);
				UBlueprint* DefBp = LoadObject<UBlueprint>(nullptr, *BpAssetPath);
				if (DefBp == nullptr) { continue; }
				if (DefBp->GeneratedClass != nullptr)
				{
					Function = DefBp->GeneratedClass->FindFunctionByName(FName(*MemberName));
				}
				if (Function == nullptr && DefBp->SkeletonGeneratedClass != nullptr)
				{
					Function = DefBp->SkeletonGeneratedClass->FindFunctionByName(FName(*MemberName));
				}
				if (Function != nullptr) { break; }
			}
		}
		if (Function == nullptr)
		{
			if (ClassName.IsEmpty())
			{
				// Blueprint internal self-call: called function (e.g. DSL inline declared support function) not yet compiled
				// into skeleton class. Mark bSelfMember so the node resolves this blueprint's own function by name at
				// blueprint compile time (FunctionReference::SetSelfMember), bypassing creation timing.
				bSelfMember = true;
			}
			else
			{
				// Cross-blueprint call: target class (class field explicitly given) is determined, but called function
				// may not yet be compiled into that class's function table (e.g. sibling blueprints not yet compiled,
				// or just created and not loaded back into memory). Mark bExternalMember so the node resolves the
				// target class function by name at caller blueprint compile time (FunctionReference::SetExternalMember),
				// isomorphic with self-call fix and event nodes (EventReference::SetExternalMember).
				bExternalMember = true;
			}
		}
	}

	FString GraphName;
	Payload->TryGetStringField(TEXT("graphName"), GraphName);
	FString GraphReason;
	UEdGraph* Graph = ResolveTargetGraph(Blueprint, GraphName, GraphReason);
	if (Graph == nullptr)
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), GraphReason);
		return Result;
	}

	int32 PosX = 0;
	int32 PosY = 0;
	FindPlacementPosition(Graph, PosX, PosY);
	double NumberValue = 0.0;
	if (Payload->TryGetNumberField(TEXT("x"), NumberValue))
	{
		PosX = static_cast<int32>(NumberValue);
	}
	if (Payload->TryGetNumberField(TEXT("y"), NumberValue))
	{
		PosY = static_cast<int32>(NumberValue);
	}

	UEdGraphNode* NewNode = nullptr;
	if (bMathExpression)
	{
		FString Expression;
		if (!Payload->TryGetStringField(TEXT("expression"), Expression) || Expression.IsEmpty())
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), TEXT("MathExpression requires a non-empty expression"));
			return Result;
		}
		TArray<TPair<FName, FEdGraphPinType>> Inputs;
		TSet<FName> Names;
		const TArray<TSharedPtr<FJsonValue>>* Params = nullptr;
		if (Payload->TryGetArrayField(TEXT("params"), Params))
		{
			for (const TSharedPtr<FJsonValue>& Value : *Params)
			{
				const TSharedPtr<FJsonObject> Spec = Value->AsObject();
				FString Name, Type, Reason;
				FEdGraphPinType PinType;
				if (!Spec.IsValid() || !Spec->TryGetStringField(TEXT("name"), Name) || Name.IsEmpty()
					|| Names.Contains(FName(*Name)) || !Spec->TryGetStringField(TEXT("type"), Type)
					|| !PinTypeTextToPinType(Type, PinType, Reason) || PinType.PinCategory == UEdGraphSchema_K2::PC_Exec)
				{
					Result->SetBoolField(TEXT("added"), false);
					Result->SetStringField(TEXT("reason"), TEXT("Invalid MathExpression input: ") + Name + TEXT(" ") + Reason);
					return Result;
				}
				Names.Add(FName(*Name));
				Inputs.Emplace(FName(*Name), PinType);
			}
		}
		UK2Node_MathExpression* MathNode = NewObject<UK2Node_MathExpression>(Graph);
		MathNode->SetFlags(RF_Transactional);
		Graph->AddNode(MathNode);
		MathNode->CreateNewGuid();
		MathNode->PostPlacedNewNode();
		MathNode->AllocateDefaultPins();
		// Declare input types before resolution; the engine preserves these pins, preventing Vector from being guessed as double.
		for (const auto& Input : Inputs)
		{
			MathNode->GetEntryNode()->CreateUserDefinedPin(Input.Key, Input.Value, EGPD_Output, false);
		}
		MathNode->OnRenameNode(Expression);
		FCompilerResultsLog MathLog;
		MathLog.bSilentMode = true;
		MathNode->ValidateNodeDuringCompilation(MathLog);
		if (MathLog.NumErrors > 0 || MathNode->FindPin(UEdGraphSchema_K2::PN_ReturnValue, EGPD_Output) == nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), TEXT("MathExpression could not compile: ") + Expression + TEXT(" ") + MathNode->ErrorMsg);
			MathNode->DestroyNode();
			return Result;
		}
		MathNode->NodePosX = PosX;
		MathNode->NodePosY = PosY;
		NewNode = MathNode;
	}
	else if (bMessage || bInterfaceMessage)
	{
		// Interface message call: 'class' = interface generated class path,
		// 'name' = message function name. Resolve the interface class, find
		// the message UFunction, then create a UK2Node_Message (inherits
		// UK2Node_CallFunction — same setup sequence, but CreateSelfPin
		// produces a PC_Object pin instead of a self-context pin).
		UClass* InterfaceClass = nullptr;
		const UFunction* MessageFunction = nullptr;
		if (bInterfaceMessage)
		{
			// Function-call branch already resolved LookupClass (a UInterface)
			// and Function — reuse them directly (no payload re-read needed).
			InterfaceClass = const_cast<UClass*>(LookupClass);
			MessageFunction = Function;
		}
		else
		{
			FString InterfaceClassPath;
			if (!Payload->TryGetStringField(TEXT("class"), InterfaceClassPath) || InterfaceClassPath.IsEmpty())
			{
				Result->SetBoolField(TEXT("added"), false);
				Result->SetStringField(TEXT("reason"), TEXT("kind 'message' requires 'class' (interface generated class path, e.g. /Game/Tests/IDamageable.IDamageable_C)"));
				return Result;
			}
			InterfaceClass = LoadObject<UClass>(nullptr, *InterfaceClassPath);
			if (InterfaceClass == nullptr)
			{
				// Try ResolveBlueprint -> GeneratedClass for asset paths without _C suffix.
				UBlueprint* InterfaceBP = ResolveBlueprint(InterfaceClassPath);
				if (InterfaceBP != nullptr)
				{
					InterfaceClass = InterfaceBP->GeneratedClass;
				}
			}
			if (InterfaceClass == nullptr)
			{
				Result->SetBoolField(TEXT("added"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No interface class found at '%s'"), *InterfaceClassPath));
				return Result;
			}
			MessageFunction = InterfaceClass->FindFunctionByName(FName(*MemberName));
			if (MessageFunction == nullptr)
			{
				Result->SetBoolField(TEXT("added"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Interface '%s' has no message named '%s'"), *InterfaceClass->GetName(), *MemberName));
				return Result;
			}
		}
		UK2Node_Message* MessageNode = NewObject<UK2Node_Message>(Graph);
		MessageNode->CreateNewGuid();
		MessageNode->PostPlacedNewNode();
		MessageNode->SetFromFunction(MessageFunction);
		MessageNode->SetFlags(RF_Transactional);
		MessageNode->AllocateDefaultPins();
		MessageNode->NodePosX = PosX;
		MessageNode->NodePosY = PosY;
		Graph->AddNode(MessageNode);
		NewNode = MessageNode;
	}
	else if (bGetter || bSetter)
	{
		// Variable access node: the skeleton class carries every visible
		// member variable (own + inherited), which is exactly the self
		// context these nodes use.
		const UClass* VarClass = Blueprint->SkeletonGeneratedClass;
		if (VarClass == nullptr)
		{
			VarClass = static_cast<const UClass*>(Blueprint->ParentClass);
		}
		FProperty* VariableProperty = nullptr;
		if (VarClass != nullptr)
		{
			// IncludeSuper mirrors FindFunctionByName's chain search; the
			// checked FindField variant logs fatal when the name is missing.
			for (TFieldIterator<FProperty> It(VarClass, EFieldIteratorFlags::IncludeSuper); It; ++It)
			{
				if (It->GetFName() == FName(*MemberName))
				{
					VariableProperty = *It;
					break;
				}
			}
		}
		// Widget Blueprint: named widgets in the Widget Tree are exposed as
		// FObjectProperty on the generated class after compilation. If the
		// skeleton class didn't have the property, try the full generated
		// class and also check the Widget Tree directly.
		if (VariableProperty == nullptr)
		{
			const UClass* GenClass = Blueprint->GeneratedClass ? Blueprint->GeneratedClass : nullptr;
			if (GenClass != nullptr && GenClass != VarClass)
			{
				for (TFieldIterator<FProperty> It(GenClass, EFieldIteratorFlags::IncludeSuper); It; ++It)
				{
					if (It->GetFName() == FName(*MemberName))
					{
						VariableProperty = *It;
						break;
					}
				}
			}
		}
		// Function-local variable: not a member FProperty on the class, it lives
		// on the function's entry node (UK2Node_FunctionEntry::LocalVariables).
		// Resolve it by name in the target graph's scope before giving up.
		FBPVariableDescription* LocalVarDesc = (VariableProperty == nullptr)
			? FBlueprintEditorUtils::FindLocalVariable(Blueprint, Graph, FName(*MemberName))
			: nullptr;
		// External-object variable access: the variable may live on ANOTHER
		// class entirely (e.g. a SaveGame object's Score member written from
		// an actor's function). Opt-in via payload 'class' (owner class name,
		// same convention as the kind-function library-class field; resolved
		// with the AddDelegate branch's prefix-strip/_C/asset-root chain).
		// ConfigureVarNode with a non-null source calls
		// FMemberReference::SetExternalMember (EdGraphSchema_K2.cpp
		// ConfigureVarNode) - a non-self-context variable node. AllocateDefaultPins
		// then creates the object Target input pin (engine-named PN_Self
		// "self", K2Node_Variable.cpp CreatePinsForVariable), which the
		// "__target" pin-name resolution maps to.
		UClass* ExternalOwnerClass = nullptr;
		if (VariableProperty == nullptr && LocalVarDesc == nullptr)
		{
			FString OwnerClassName;
			if (Payload->TryGetStringField(TEXT("class"), OwnerClassName) && !OwnerClassName.IsEmpty())
			{
				ExternalOwnerClass = ResolveClassFromName(OwnerClassName);
				if (ExternalOwnerClass == nullptr)
				{
					Result->SetBoolField(TEXT("added"), false);
					Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No class named '%s' found for external variable access (use list_class_hierarchy to look up valid names)"), *OwnerClassName));
					return Result;
				}
				VariableProperty = CastField<FProperty>(ExternalOwnerClass->FindPropertyByName(FName(*MemberName)));
				if (VariableProperty == nullptr)
				{
					Result->SetBoolField(TEXT("added"), false);
					Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No variable named '%s' on class '%s'"), *MemberName, *ExternalOwnerClass->GetName()));
					return Result;
				}
			}
		}
		if (VariableProperty == nullptr && LocalVarDesc == nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No variable named '%s' on this blueprint or its parent classes (discover variables via list_blueprint_variables; for another object's member pass 'class' = its owner class name)"), *MemberName));
			return Result;
		}

		UK2Node_Variable* VarNode = bGetter
			? static_cast<UK2Node_Variable*>(NewObject<UK2Node_VariableGet>(Graph))
			: static_cast<UK2Node_Variable*>(NewObject<UK2Node_VariableSet>(Graph));
		if (LocalVarDesc != nullptr)
		{
			// Local variable: bind via FMemberReference::SetLocalMember (name +
			// function-graph scope). The value pin is named after the variable,
			// identical to the member path, so downstream data wiring is unchanged.
			const FString ScopeName = FBlueprintEditorUtils::GetTopLevelGraph(Graph)->GetName();
			VarNode->VariableReference.SetLocalMember(FName(*MemberName), ScopeName, LocalVarDesc->VarGuid);
		}
		else
		{
			// Self context (nullptr source = same configuration the editor
			// applies when dragging the variable in) or external-object
			// access (ExternalOwnerClass - the node gains a Target input pin).
			UEdGraphSchema_K2::ConfigureVarNode(VarNode, FName(*MemberName), ExternalOwnerClass, Blueprint);
		}
		VarNode->SetFlags(RF_Transactional);
		// Engine spawn order (FEdGraphSchemaAction_K2NewNode::CreateNode):
		// add to the graph first, then guid/post-place/pins.
		Graph->AddNode(VarNode);
		VarNode->CreateNewGuid();
		VarNode->PostPlacedNewNode();
		VarNode->AllocateDefaultPins();
		VarNode->NodePosX = PosX;
		VarNode->NodePosY = PosY;
		NewNode = VarNode;
	}
	else if (bMacro)
	{
		// Macro resolution order: the blueprint's own macro graphs, an
		// explicit object path, then the engine's standard macro libraries
		// (DoOnce, ForEachLoop, Gate, Sequence, ... live in StandardMacros).
		UEdGraph* MacroGraph = nullptr;
		for (UEdGraph* Candidate : Blueprint->MacroGraphs)
		{
			if (Candidate != nullptr && Candidate->GetName() == MemberName)
			{
				MacroGraph = Candidate;
				break;
			}
		}
		if (MacroGraph == nullptr && MemberName.StartsWith(TEXT("/")))
		{
			MacroGraph = LoadObject<UEdGraph>(nullptr, *MemberName);
		}
		if (MacroGraph == nullptr)
		{
			// System macro libraries are UBlueprint assets whose macro graphs
			// live in Blueprint->MacroGraphs. LoadPackage + FindObject is used
			// instead of LoadObject because engine editor resources may need
			// the package to be loaded first (LoadObject alone can return null
			// for /Engine/EditorBlueprintResources/... assets).
			struct FMacroLib { const TCHAR* PkgPath; const TCHAR* ObjName; };
			static const FMacroLib MacroLibraries[] =
			{
				{ TEXT("/Engine/EditorKismetResources/StandardMacros"),         TEXT("StandardMacros") },
				{ TEXT("/Engine/EditorBlueprintResources/StandardMacros"),       TEXT("StandardMacros") },
				{ TEXT("/Engine/EditorBlueprintResources/ActorMacros"),          TEXT("ActorMacros") },
				{ TEXT("/Engine/EditorBlueprintResources/ActorComponentMacros"), TEXT("ActorComponentMacros") },
			};
			for (const FMacroLib& Lib : MacroLibraries)
			{
				UPackage* Pkg = LoadPackage(nullptr, Lib.PkgPath, LOAD_None);
				if (Pkg == nullptr)
				{
					continue;
				}
				UBlueprint* MacroBlueprint = FindObject<UBlueprint>(Pkg, Lib.ObjName);
				if (MacroBlueprint == nullptr)
				{
					continue;
				}
				for (UEdGraph* Candidate : MacroBlueprint->MacroGraphs)
				{
					if (Candidate != nullptr && Candidate->GetName() == MemberName)
					{
						MacroGraph = Candidate;
						break;
					}
				}
				if (MacroGraph != nullptr)
				{
					break;
				}
			}
		}
		if (MacroGraph == nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No macro named '%s' found - name a macro graph of this blueprint, a system macro (DoOnce, ForEachLoop, ForLoop, Gate, MultiGate, Sequence, WhileLoop, ...), or a full object path such as /Engine/EditorBlueprintResources/StandardMacros.DoOnce"), *MemberName));
			return Result;
		}

		UK2Node_MacroInstance* MacroNode = NewObject<UK2Node_MacroInstance>(Graph);
		MacroNode->SetMacroGraph(MacroGraph);
		MacroNode->SetFlags(RF_Transactional);
		Graph->AddNode(MacroNode);
		MacroNode->CreateNewGuid();
		MacroNode->PostPlacedNewNode();
		MacroNode->AllocateDefaultPins();
		MacroNode->NodePosX = PosX;
		MacroNode->NodePosY = PosY;
		NewNode = MacroNode;
	}
	else if (bCustomEvent)
	{
		// Custom events live in ubergraph pages (event graphs) and function
		// graphs, but not in macro graphs.
		const bool bIsUbergraph = Blueprint->UbergraphPages.Contains(Graph);
		const bool bIsFunction = Blueprint->FunctionGraphs.Contains(Graph);
		if (!bIsUbergraph && !bIsFunction)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Custom event '%s' can only be placed in an event graph or function graph; '%s' is a macro graph"), *MemberName, *Graph->GetName()));
			return Result;
		}

		// Check for a naming conflict with an existing custom event in any
		// ubergraph page (the same slot used for overridable event nodes).
		FName DesiredName = FName(*MemberName);
		for (const UEdGraph* Page : Blueprint->UbergraphPages)
		{
			if (Page == nullptr) { continue; }
			for (const UEdGraphNode* Node : Page->Nodes)
			{
				const UK2Node_CustomEvent* Existing = Cast<UK2Node_CustomEvent>(Node);
				if (Existing != nullptr && !Existing->CustomFunctionName.IsNone()
					&& Existing->CustomFunctionName == DesiredName)
				{
					Result->SetBoolField(TEXT("added"), false);
					Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Custom event '%s' already exists in this blueprint (each custom event must have a unique name)"), *MemberName));
					return Result;
				}
			}
		}

		// Optional signature: a 'params' array of [{name, type}, ...] adds the
		// event's parameters as user-defined output pins. Validate every entry up
		// front (built-in/duplicate name collisions, type vocabulary) so a bad
		// entry fails cleanly before the node is created. Custom event parameters
		// are outputs - the engine rejects input pins on these nodes.
		struct FCustomEventParamSpec { FName Name; FEdGraphPinType Type; };
		TArray<FCustomEventParamSpec> ParamSpecs;
		const TArray<TSharedPtr<FJsonValue>>* ParamArray = nullptr;
		if (Payload->TryGetArrayField(TEXT("params"), ParamArray))
		{
			TSet<FName> ReservedNames;
			ReservedNames.Add(UEdGraphSchema_K2::PN_Then);
			ReservedNames.Add(UK2Node_Event::DelegateOutputName);
			for (const TSharedPtr<FJsonValue>& ParamValue : *ParamArray)
			{
				const TSharedPtr<FJsonObject>* ParamObjPtr = nullptr;
				if (!ParamValue.IsValid() || !ParamValue->TryGetObject(ParamObjPtr) || !ParamObjPtr->IsValid())
				{
					Result->SetBoolField(TEXT("added"), false);
					Result->SetStringField(TEXT("reason"), TEXT("Each entry in 'params' must be an object with 'name' and 'type'"));
					return Result;
				}
				const TSharedPtr<FJsonObject>& ParamObj = *ParamObjPtr;

				FString ParamName;
				FString ParamTypeText;
				if (!ParamObj->TryGetStringField(TEXT("name"), ParamName) || ParamName.TrimStartAndEnd().IsEmpty())
				{
					Result->SetBoolField(TEXT("added"), false);
					Result->SetStringField(TEXT("reason"), TEXT("Each entry in 'params' needs a non-empty 'name'"));
					return Result;
				}
				if (!ParamObj->TryGetStringField(TEXT("type"), ParamTypeText) || ParamTypeText.TrimStartAndEnd().IsEmpty())
				{
					Result->SetBoolField(TEXT("added"), false);
					Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Param '%s' needs a 'type' (same vocabulary as add_blueprint_variable: bool/int/float/string/vector/Actor/...)"), *ParamName));
					return Result;
				}

				const FName ParamFName(*ParamName);
				if (ReservedNames.Contains(ParamFName))
				{
					Result->SetBoolField(TEXT("added"), false);
					Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Param '%s' collides with a built-in pin or an earlier parameter of the same name"), *ParamName));
					return Result;
				}
				ReservedNames.Add(ParamFName);

				FEdGraphPinType ParamPinType;
				FString TypeReason;
				if (!PinTypeTextToPinType(ParamTypeText, ParamPinType, TypeReason))
				{
					Result->SetBoolField(TEXT("added"), false);
					Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Param '%s' has an invalid type '%s': %s"), *ParamName, *ParamTypeText, *TypeReason));
					return Result;
				}

				FCustomEventParamSpec Spec;
				Spec.Name = ParamFName;
				Spec.Type = ParamPinType;
				ParamSpecs.Add(Spec);
			}
		}

		// UK2Node_CustomEvent::CreateFromFunction(Function != nullptr) is NOT safe
		// for a bare event (no UFunction). Replicate the factory's creation steps:
		UK2Node_CustomEvent* CustomEventNode = NewObject<UK2Node_CustomEvent>(Graph);
		CustomEventNode->CustomFunctionName = FName(*MemberName);
		CustomEventNode->SetFlags(RF_Transactional);
		Graph->Modify();
		Graph->AddNode(CustomEventNode, true, true);
		CustomEventNode->CreateNewGuid();
		CustomEventNode->PostPlacedNewNode();
		CustomEventNode->AllocateDefaultPins();
		// Attach the validated signature as user-defined output pins (the same
		// path the editor's '+' button uses - populates UserDefinedPins, which
		// the compiler reads to build the event's function signature).
		for (const FCustomEventParamSpec& Spec : ParamSpecs)
		{
			CustomEventNode->CreateUserDefinedPin(Spec.Name, Spec.Type, EGPD_Output, /*bUseUniqueName=*/false);
		}
		CustomEventNode->NodePosX = PosX;
		CustomEventNode->NodePosY = PosY;
		// Replication on a custom event (blueprint RPC): set the node's FunctionFlags so the
		// compiler emits the event's UFunction as a net RPC (mirrors the editor's "Replication"
		// dropdown: FUNC_Net | server/client/multicast [| FUNC_NetReliable]).
		{
			FString RpcMode;
			if (Payload->TryGetStringField(TEXT("rpc"), RpcMode) && !RpcMode.IsEmpty())
			{
				uint32 NetFlags = FUNC_Net;
				if (RpcMode.Equals(TEXT("server"), ESearchCase::IgnoreCase))
				{
					NetFlags |= FUNC_NetServer;
				}
				else if (RpcMode.Equals(TEXT("client"), ESearchCase::IgnoreCase))
				{
					NetFlags |= FUNC_NetClient;
				}
				else if (RpcMode.Equals(TEXT("multicast"), ESearchCase::IgnoreCase)
					|| RpcMode.Equals(TEXT("netmulticast"), ESearchCase::IgnoreCase))
				{
					NetFlags |= FUNC_NetMulticast;
				}
				bool bRpcReliable = false;
				if (Payload->TryGetBoolField(TEXT("reliable"), bRpcReliable) && bRpcReliable)
				{
					NetFlags |= FUNC_NetReliable;
				}
				CustomEventNode->FunctionFlags |= NetFlags;
			}
		}
		// SnapToGrid(GetDefault<UEditorStyleSettings>()->GridSnapSize) - UnrealEd
		// not a plugin dependency; skip grid snap (the editor will snap on open).
		if (CustomEventNode == nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Failed to create custom event '%s'"), *MemberName));
			return Result;
		}
		NewNode = CustomEventNode;
	}
	else if (bTimeline)
	{
		// Timeline node (UK2Node_Timeline): creates the blueprint's timeline
		// template (UTimelineTemplate - the variable the generated
		// TimelineComponent binds to) plus the graph node. The node's
		// AllocateDefaultPins resolves the template and emits one output pin
		// per track (float track -> PC_Real output named after the track),
		// alongside the fixed Play / PlayFromStart / Stop / Reverse /
		// ReverseFromEnd / SetNewTime exec inputs and Update / Finished exec
		// outputs. Only valid in event graphs / function graphs.
		const bool bIsUbergraph = Blueprint->UbergraphPages.Contains(Graph);
		const bool bIsFunction = Blueprint->FunctionGraphs.Contains(Graph);
		if (!bIsUbergraph && !bIsFunction)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Timeline '%s' can only be placed in an event graph or function graph; '%s' is a macro graph"), *MemberName, *Graph->GetName()));
			return Result;
		}

		// One timeline template per variable name (the node resolves the
		// template by TimelineName == VariableName).
		if (Blueprint->FindTimelineTemplateByVariableName(FName(*MemberName)) != nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Timeline '%s' already exists in this blueprint"), *MemberName));
			return Result;
		}

		// Optional length in seconds (DSL [Timeline("T", 1.0f)]); defaults to 1s.
		double TimelineLengthValue = 1.0;
		Payload->TryGetNumberField(TEXT("length"), TimelineLengthValue);

		Blueprint->Modify();
		Graph->Modify();

		// Template first: the node's AllocateDefaultPins reads tracks from it.
		UTimelineTemplate* Template = FBlueprintEditorUtils::AddNewTimeline(Blueprint, FName(*MemberName));
		if (Template == nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Failed to create the timeline template for '%s'"), *MemberName));
			return Result;
		}
		Template->TimelineLength = static_cast<float>(TimelineLengthValue);
		Template->LengthMode = ETimelineLengthMode::TL_TimelineLength;

		// Tracks. The DSL [Timeline] nested class declares them explicitly
		// ("tracks": [{name, type, keys/times/curveAsset}, ...]) - the node emits
		// one output pin per track, named after the track. Without a "tracks"
		// payload (the MCP A-side construction form) the legacy single default
		// float track named after the timeline is kept.
		//
		// Every track MUST be registered in the display order besides being added
		// to its array: UK2Node_Timeline::AllocateDefaultPins enumerates tracks
		// through UTimelineTemplate::GetNumDisplayTracks(), which returns
		// TrackDisplayOrder.Num() - NOT the size of the track arrays
		// (TimelineTemplate.cpp). A track that only lives in FloatTracks is
		// invisible to the node: its value output pin is never created at all
		// (run 20261012_2025: connect "T" -> InString failed with
		// "Node 'T' has no pin named 'T'"). The editor's own add-track path
		// registers both (STimelineEditor.cpp: array Add + AddDisplayTrack),
		// so mirror it here. TrackIndex is captured BEFORE the array Add (it is
		// the index the new track will occupy) - same as the editor.
		const TArray<TSharedPtr<FJsonValue>>* TracksArray = nullptr;
		const bool bHasTracks = Payload->TryGetArrayField(TEXT("tracks"), TracksArray)
			&& TracksArray != nullptr && TracksArray->Num() > 0;

		if (bHasTracks)
		{
			// Internal curves are sub-objects of the blueprint's generated class
			// (same as STimelineEditor::CreateNewTrack). They must NOT become
			// package-level assets: building a timeline produces no visible curve
			// asset in the content browser / asset manager (visual feedback
			// 2026-10-13).
			UObject* CurveOuter = Template->GetOuter();

			for (const TSharedPtr<FJsonValue>& TrackVal : *TracksArray)
			{
				const TSharedPtr<FJsonObject>* TrackObjPtr = nullptr;
				if (!TrackVal.IsValid() || !TrackVal->TryGetObject(TrackObjPtr) || !TrackObjPtr->IsValid())
				{
					continue;
				}
				const TSharedPtr<FJsonObject>& TrackDef = *TrackObjPtr;

				FString TrackName;
				if (!TrackDef->TryGetStringField(TEXT("name"), TrackName) || TrackName.IsEmpty())
				{
					continue;
				}
				FString TrackType;
				TrackDef->TryGetStringField(TEXT("type"), TrackType);
				const FName TrackFName(*TrackName);
				const FName CurveName(*FString::Printf(TEXT("%s_%s_Curve"), *MemberName, *TrackName));

				// Named 'TrackCurveAsset', not 'AssetPath': the enclosing handler
				// already declares AssetPath (the blueprint path) and a same-named
				// local would hide it (C4456).
				FString TrackCurveAsset;
				TrackDef->TryGetStringField(TEXT("curveAsset"), TrackCurveAsset);

				if (TrackType == TEXT("event"))
				{
					// Event track: keyframe times only; each key is a firing point,
					// the output is an exec pin named after the track.
					FTTTrackId TrackId(FTTTrackBase::TT_Event, Template->EventTracks.Num());
					FTTEventTrack& EventTrack = Template->EventTracks.AddDefaulted_GetRef();
					EventTrack.SetTrackName(TrackFName, Template);
					const TArray<TSharedPtr<FJsonValue>>* Times = nullptr;
					if (TrackDef->TryGetArrayField(TEXT("times"), Times))
					{
						EventTrack.CurveKeys = ForgeMindBuildEventCurve(CurveOuter, CurveName, *Times);
					}
					Template->AddDisplayTrack(TrackId);
				}
				else if (TrackType == TEXT("vector"))
				{
					FTTTrackId TrackId(FTTTrackBase::TT_VectorInterp, Template->VectorTracks.Num());
					FTTVectorTrack& VectorTrack = Template->VectorTracks.AddDefaulted_GetRef();
					VectorTrack.SetTrackName(TrackFName, Template);
					VectorTrack.bIsExternalCurve = !TrackCurveAsset.IsEmpty();
					VectorTrack.CurveVector = TrackCurveAsset.IsEmpty()
						? ForgeMindBuildCurveVector(CurveOuter, CurveName, TrackDef)
						: ForgeMindLoadExternalCurve<UCurveVector>(TrackCurveAsset);
					Template->AddDisplayTrack(TrackId);
				}
				else if (TrackType == TEXT("color"))
				{
					FTTTrackId TrackId(FTTTrackBase::TT_LinearColorInterp, Template->LinearColorTracks.Num());
					FTTLinearColorTrack& ColorTrack = Template->LinearColorTracks.AddDefaulted_GetRef();
					ColorTrack.SetTrackName(TrackFName, Template);
					ColorTrack.bIsExternalCurve = !TrackCurveAsset.IsEmpty();
					ColorTrack.CurveLinearColor = TrackCurveAsset.IsEmpty()
						? ForgeMindBuildCurveLinearColor(CurveOuter, CurveName, TrackDef)
						: ForgeMindLoadExternalCurve<UCurveLinearColor>(TrackCurveAsset);
					Template->AddDisplayTrack(TrackId);
				}
				else
				{
					// Float track (default): inline keyframes or an external
					// CurveFloat reference.
					FTTTrackId TrackId(FTTTrackBase::TT_FloatInterp, Template->FloatTracks.Num());
					FTTFloatTrack& FloatTrack = Template->FloatTracks.AddDefaulted_GetRef();
					FloatTrack.SetTrackName(TrackFName, Template);
					FloatTrack.bIsExternalCurve = !TrackCurveAsset.IsEmpty();
					FloatTrack.CurveFloat = TrackCurveAsset.IsEmpty()
						? ForgeMindBuildCurveFloat(CurveOuter, CurveName, TrackDef)
						: ForgeMindLoadExternalCurve<UCurveFloat>(TrackCurveAsset);
					Template->AddDisplayTrack(TrackId);
				}
			}
		}
		else
		{
			// Legacy default float track named after the timeline (the MCP A-side
			// construction form relies on it when it passes no tracks). The track
			// gets a real (empty) curve object - CurveFloat used to stay null here,
			// so the timeline editor showed a track with no curve at all. Keyframes
			// are NEVER invented: a caller that wants a populated curve must pass
			// tracks (DSL "tracks" / MCP payload), on both the A and the B side.
			FTTTrackId FloatTrackId(FTTTrackBase::TT_FloatInterp, Template->FloatTracks.Num());
			FTTFloatTrack& FloatTrack = Template->FloatTracks.AddDefaulted_GetRef();
			FloatTrack.SetTrackName(FName(*MemberName), Template);
			FloatTrack.bIsExternalCurve = false;
			FloatTrack.CurveFloat = ForgeMindBuildCurveFloat(
				Template->GetOuter(),
				FName(*FString::Printf(TEXT("%s_%s_Curve"), *MemberName, *MemberName)),
				MakeShared<FJsonObject>());
			Template->AddDisplayTrack(FloatTrackId);
		}

		UK2Node_Timeline* TimelineNode = NewObject<UK2Node_Timeline>(Graph);
		TimelineNode->TimelineName = FName(*MemberName);
		TimelineNode->SetFlags(RF_Transactional);
		Graph->AddNode(TimelineNode, true, true);
		TimelineNode->CreateNewGuid();
		TimelineNode->PostPlacedNewNode();
		TimelineNode->AllocateDefaultPins();
		TimelineNode->NodePosX = PosX;
		TimelineNode->NodePosY = PosY;
		NewNode = TimelineNode;
	}
	else if (bInputAxis)
	{
		// Input axis event (K2Node_InputAxisEvent): binds a named axis from
		// the project's AxisMappings to fire every tick the axis is non-zero.
		// Only valid in ubergraph pages (event graphs), like other events.
		if (!Blueprint->UbergraphPages.Contains(Graph))
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Input axis events can only be placed in an event graph; '%s' is not one (omit graphName to use the default event graph)"), *Graph->GetName()));
			return Result;
		}

		// Validate: the axis must be registered in the project's AxisMappings
		// (Project Settings > Input) - an unregistered name binds nothing at
		// runtime (the engine only warns at compile time), so validate at
		// creation and reject.
		UInputSettings* InputSettings = GetMutableDefault<UInputSettings>();
		if (InputSettings != nullptr)
		{
			TArray<FName> AxisNames;
			InputSettings->GetAxisNames(AxisNames);
			if (!AxisNames.Contains(FName(*MemberName)))
			{
				Result->SetBoolField(TEXT("added"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Input axis '%s' is not registered in the project's AxisMappings (Project Settings > Input); register it before creating the event"), *MemberName));
				return Result;
			}
		}

		// Check for duplicate: only one InputAxis node per axis name.
		for (const UEdGraph* Page : Blueprint->UbergraphPages)
		{
			if (Page == nullptr) { continue; }
			for (const UEdGraphNode* Node : Page->Nodes)
			{
				const UK2Node_InputAxisEvent* Existing = Cast<UK2Node_InputAxisEvent>(Node);
				if (Existing != nullptr && Existing->InputAxisName == FName(*MemberName))
				{
					Result->SetBoolField(TEXT("added"), false);
					Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Input axis event '%s' already exists in this blueprint"), *MemberName));
					return Result;
				}
			}
		}

		UK2Node_InputAxisEvent* AxisNode = NewObject<UK2Node_InputAxisEvent>(Graph);
		AxisNode->InputAxisName = FName(*MemberName);
		AxisNode->Initialize(FName(*MemberName));
		AxisNode->SetFlags(RF_Transactional);
		Graph->Modify();
		Graph->AddNode(AxisNode, true, true);
		AxisNode->CreateNewGuid();
		AxisNode->PostPlacedNewNode();
		AxisNode->AllocateDefaultPins();
		AxisNode->NodePosX = PosX;
		AxisNode->NodePosY = PosY;
		NewNode = AxisNode;
	}
	else if (bInputAction)
	{
		// Input action node (UK2Node_InputAction): the editor-visible node that
		// binds a named action from the project's ActionMappings. Natively has
		// "Pressed"/"Released" exec out pins + "Key" data out pin (same layout
		// as K2Node_InputKey). GetNodeTitle returns a localized "InputAction
		// {name}" string. At compile time ExpandNode spawns internal
		// K2Node_InputActionEvent nodes — we never create those directly.
		if (!Blueprint->UbergraphPages.Contains(Graph))
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Input action events can only be placed in an event graph; '%s' is not one (omit graphName to use the default event graph)"), *Graph->GetName()));
			return Result;
		}

		// Validate: the action must be registered in the project's ActionMappings
		// (Project Settings > Input) - an unregistered name binds nothing at
		// runtime (the engine has no compile-time check for this node), so
		// validate at creation and reject.
		UInputSettings* InputSettings = GetMutableDefault<UInputSettings>();
		if (InputSettings != nullptr)
		{
			TArray<FName> ActionNames;
			InputSettings->GetActionNames(ActionNames);
			if (!ActionNames.Contains(FName(*MemberName)))
			{
				Result->SetBoolField(TEXT("added"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Input action '%s' is not registered in the project's ActionMappings (Project Settings > Input); register it before creating the event"), *MemberName));
				return Result;
			}
		}

		// Check for duplicate: only one InputAction node per action name.
		// UK2Node_InputAction carries both "Pressed" and "Released" pins on a
		// single node, so there is no per-event-type duplication.
		for (const UEdGraph* Page : Blueprint->UbergraphPages)
		{
			if (Page == nullptr) { continue; }
			for (const UEdGraphNode* Node : Page->Nodes)
			{
				const UK2Node_InputAction* Existing = Cast<UK2Node_InputAction>(Node);
				if (Existing != nullptr && Existing->InputActionName == FName(*MemberName))
				{
					Result->SetBoolField(TEXT("added"), false);
					Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Input action node '%s' already exists in this blueprint"), *MemberName));
					return Result;
				}
			}
		}

		UK2Node_InputAction* ActionNode = NewObject<UK2Node_InputAction>(Graph);
		ActionNode->InputActionName = FName(*MemberName);
		ActionNode->SetFlags(RF_Transactional);
		Graph->Modify();
		Graph->AddNode(ActionNode, true, true);
		ActionNode->CreateNewGuid();
		ActionNode->PostPlacedNewNode();
		ActionNode->AllocateDefaultPins();
		ActionNode->NodePosX = PosX;
		ActionNode->NodePosY = PosY;
		NewNode = ActionNode;
	}
	else if (bEnhancedInputAction)
	{
		// Enhanced Input action event (UK2Node_EnhancedInputAction).
		// Why a dedicated kind exists at all: UEnhancedInputComponent::BindAction has no
		// UFUNCTION in 5.0.3 (it is a set of C++ templates plus a plain UObject*/FName
		// overload; the class's only UFUNCTION is the BlueprintInternalUseOnly
		// GetBoundActionValue, EnhancedInputComponent.h:432), so no call node can ever be
		// resolved by function name - asking add_blueprint_node for "BindAction" produces an
		// unresolved zero-pin node (run 20261022_1955, EInput_Bind_A). The blueprint-facing
		// shape of "an action fired" is this specialized event node.
		//
		// Resolved softly by class path: the node lives in the EnhancedInput plugin's
		// InputBlueprintNodes module (UncookedOnly), and ForgeMindForUE must not grow a
		// hard dependency on an experimental plugin - when the plugin is off the request is
		// refused with a reason instead of building a fake node.
		UClass* EnhancedActionClass = FindObject<UClass>(nullptr, TEXT("/Script/InputBlueprintNodes.K2Node_EnhancedInputAction"));
		if (EnhancedActionClass == nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), TEXT("K2Node_EnhancedInputAction is not loaded - the EnhancedInput plugin is not enabled in this project (add \"Name\": \"EnhancedInput\", \"Enabled\": true to the .uproject)"));
			return Result;
		}
		if (!Blueprint->UbergraphPages.Contains(Graph))
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Enhanced input action events can only be placed in an event graph; '%s' is not one (omit graphName to use the default event graph)"), *Graph->GetName()));
			return Result;
		}

		// The asset to bind: payload 'targetClass' carries the UInputAction (same field the
		// Cast / AddComponent / GetClassDefaults branches use for a class or asset reference).
		FString ActionRef;
		if (!Payload.IsValid() || !Payload->TryGetStringField(TEXT("targetClass"), ActionRef) || ActionRef.IsEmpty())
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), TEXT("Enhanced input action event needs the bound action: pass payload 'targetClass' = the UInputAction asset (e.g. /Game/Tests/UE5_0/IA_Move.IA_Move)"));
			return Result;
		}
		FString ActionObjectPath = ActionRef;
		ConstructorHelpers::StripObjectClass(ActionObjectPath);
		// A DSL/MCP caller usually passes the **package** form ("/Game/Tests/UE5_0/IA_Move").
		// That is not a valid object path, and FindObject only searches loaded objects, so the
		// freshly created (and saved but still unloaded) asset came back null - "create it
		// first" while it did exist (run 20261022_2342). Complete the object name and go
		// through StaticLoadObject, which covers both the loaded and the on-disk case.
		if (!ActionObjectPath.Contains(TEXT(".")) && !ActionObjectPath.EndsWith(TEXT("/")))
		{
			// Last '/'-separated segment (FPackageName has no segment helper in 5.0).
			int32 SlashIndex = INDEX_NONE;
			ActionObjectPath.FindLastChar(TEXT('/'), SlashIndex);
			const FString LeafName = SlashIndex != INDEX_NONE
				? ActionObjectPath.Mid(SlashIndex + 1)
				: ActionObjectPath;
			if (!LeafName.IsEmpty())
			{
				ActionObjectPath = FString::Printf(TEXT("%s.%s"), *ActionObjectPath, *LeafName);
			}
		}
		UObject* ActionAsset = nullptr;
		if (!ActionObjectPath.IsEmpty() && ActionObjectPath != TEXT("None"))
		{
			// 1) already-resident object (created earlier in this same editor session and not yet
			//    collected), 2) the package loaded from disk and the export taken out of it,
			//    3) the generic static load as the last resort.
			//
			// Step 2 is what makes this work for an asset created by create_input_action minutes
			// earlier in the same session: its UPackage exists in memory without a linker, so
			// FindObject misses (the object was collected — nothing else referenced it yet) and
			// StaticLoadObject also returns null because it will not link that half-loaded
			// package (run 20261023_0013: the .uasset on disk held a correct IA_Move export of
			// class InputAction while both lookups came back empty). LoadPackage forces the link.
			ActionAsset = FindObject<UObject>(nullptr, *ActionObjectPath);
			if (ActionAsset == nullptr)
			{
				int32 DotIndex = INDEX_NONE;
				if (ActionObjectPath.FindLastChar(TEXT('.'), DotIndex))
				{
					const FString PackagePart = ActionObjectPath.Left(DotIndex);
					const FString ObjectPart = ActionObjectPath.Mid(DotIndex + 1);
					if (UPackage* AssetPackage = LoadPackage(nullptr, *PackagePart, LOAD_None))
					{
						ActionAsset = FindObject<UObject>(AssetPackage, *ObjectPart);
					}
				}
			}
			if (ActionAsset == nullptr)
			{
				ActionAsset = StaticLoadObject(UObject::StaticClass(), nullptr, *ActionObjectPath);
			}
		}
		if (ActionAsset == nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			// Report the path actually attempted (after the package→object completion above): the
			// caller's spelling and the resolved one differ often enough that naming only the
			// input made this unreadable when the asset did exist on disk (run 20261023_0001).
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Could not load the InputAction asset - requested '%s', resolved '%s'; create it before binding the action (list_assets can confirm the path)"), *ActionRef, *ActionObjectPath));
			return Result;
		}
		UClass* InputActionClass = FindObject<UClass>(nullptr, TEXT("/Script/EnhancedInput.InputAction"));
		if (InputActionClass != nullptr && !ActionAsset->IsA(InputActionClass))
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("'%s' is a %s, not a UInputAction - the enhanced input action event binds an action asset"), *ActionRef, *ActionAsset->GetClass()->GetName()));
			return Result;
		}

		// Set the asset before AddNode/AllocateDefaultPins: the node derives its data output
		// pin category from the action's ValueType inside AllocateDefaultPins, and it creates
		// the 'InputAction' output pin only when InputAction is non-null.
		UEdGraphNode* EnhancedNode = NewObject<UEdGraphNode>(Graph, EnhancedActionClass);
		FObjectProperty* ActionProp = CastField<FObjectProperty>(EnhancedActionClass->FindPropertyByName(TEXT("InputAction")));
		if (ActionProp == nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), TEXT("UK2Node_EnhancedInputAction has no 'InputAction' property in this engine version - the node shape changed, this branch needs updating"));
			return Result;
		}
		ActionProp->SetObjectPropertyValue(ActionProp->ContainerPtrToValuePtr<void>(EnhancedNode), ActionAsset);

		EnhancedNode->SetFlags(RF_Transactional);
		Graph->Modify();
		Graph->AddNode(EnhancedNode, true, true);
		EnhancedNode->CreateNewGuid();
		EnhancedNode->PostPlacedNewNode();
		EnhancedNode->AllocateDefaultPins();
		EnhancedNode->NodePosX = PosX;
		EnhancedNode->NodePosY = PosY;
		NewNode = EnhancedNode;
	}
	else if (bInputKey)
	{
		// Input key event (UK2Node_InputKey): binds a raw key (FKey, e.g. "E").
		// Unlike InputAction (single "then" out + inputEvent selector), the node
		// natively carries TWO exec output pins "Pressed"/"Released" plus a "Key"
		// data out pin (FKey::StaticStruct) - the connector picks which one to wire.
		if (!Blueprint->UbergraphPages.Contains(Graph))
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Input key events can only be placed in an event graph; '%s' is not one (omit graphName to use the default event graph)"), *Graph->GetName()));
			return Result;
		}

		// Check for duplicate: only one InputKey node per key.
		for (const UEdGraph* Page : Blueprint->UbergraphPages)
		{
			if (Page == nullptr) { continue; }
			for (const UEdGraphNode* Node : Page->Nodes)
			{
				const UK2Node_InputKey* Existing = Cast<UK2Node_InputKey>(Node);
				if (Existing != nullptr && Existing->InputKey == FKey(*MemberName))
				{
					Result->SetBoolField(TEXT("added"), false);
					Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Input key event '%s' already exists in this blueprint"), *MemberName));
					return Result;
				}
			}
		}

		UK2Node_InputKey* KeyNode = NewObject<UK2Node_InputKey>(Graph);
		KeyNode->InputKey = FKey(*MemberName);
		KeyNode->SetFlags(RF_Transactional);
		Graph->Modify();
		Graph->AddNode(KeyNode, true, true);
		KeyNode->CreateNewGuid();
		KeyNode->PostPlacedNewNode();
		KeyNode->AllocateDefaultPins();
		KeyNode->NodePosX = PosX;
		KeyNode->NodePosY = PosY;
		NewNode = KeyNode;
	}
	else if (bSelf)
	{
		// Self reference node (K2Node_Self): outputs the blueprint instance
		// ('self') so it can be passed to static functions that require an
		// Object context, e.g. K2_SetTimer's Object pin.
		UK2Node_Self* SelfNode = NewObject<UK2Node_Self>(Graph);
		SelfNode->SetFlags(RF_Transactional);
		Graph->Modify();
		Graph->AddNode(SelfNode, true, true);
		SelfNode->CreateNewGuid();
		SelfNode->PostPlacedNewNode();
		SelfNode->AllocateDefaultPins();
		SelfNode->NodePosX = PosX;
		SelfNode->NodePosY = PosY;
		NewNode = SelfNode;
	}
	else if (bSubsystem)
	{
		// Subsystem getter node (K2Node_GetSubsystem): resolves a UGameInstanceSubsystem,
		// ULocalPlayerSubsystem or UWorldSubsystem from 'self' (the blueprint's
		// own outer chain). 'name' is the subsystem class name (e.g.
		// EnhancedInputLocalPlayerSubsystem, GameInstanceSubsystem, ...).
		UClass* SubsystemClass = ForgeMindFindFirstObject<UClass>(*MemberName);
		if (SubsystemClass == nullptr && !MemberName.StartsWith(TEXT("U")))
		{
			SubsystemClass = ForgeMindFindFirstObject<UClass>(*FString::Printf(TEXT("U%s"), *MemberName));
		}
		if (SubsystemClass == nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No subsystem class named '%s' found"), *MemberName));
			return Result;
		}

		// Verify the class is actually a subsystem
		// Skip validation - the Initialize call will handle invalid classes gracefully

		UK2Node_GetSubsystem* SubsystemNode = NewObject<UK2Node_GetSubsystem>(Graph);
		SubsystemNode->Initialize(SubsystemClass);
		SubsystemNode->SetFlags(RF_Transactional);
		Graph->Modify();
		Graph->AddNode(SubsystemNode, true, true);
		SubsystemNode->CreateNewGuid();
		SubsystemNode->PostPlacedNewNode();
		SubsystemNode->AllocateDefaultPins();
		SubsystemNode->NodePosX = PosX;
		SubsystemNode->NodePosY = PosY;
		NewNode = SubsystemNode;
	}
	else if (bFlowNode)
	{
		// Native flow-control node (Branch, Switch*, Cast, Select): no
		// reflection involved, plain spawn sequence like the other node
		// families above. Parameterized nodes (Switch, Cast, Select) get
		// their configuration from the payload before AllocateDefaultPins.
		UK2Node* FlowNode = CreateNativeFlowNode(MemberName, Graph, Payload);
		if (FlowNode == nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Failed to create native flow node '%s'"), *MemberName));
			return Result;
		}
		FlowNode->SetFlags(RF_Transactional);
		Graph->AddNode(FlowNode);
		FlowNode->CreateNewGuid();
		FlowNode->PostPlacedNewNode();
		// Parameterized nodes get their configuration from the payload.
		// SwitchOnInt / SwitchOnString allocate their own pins inside
		// ConfigureNativeFlowNode (Default pin must be created before case pins, and case
		// pins must not be duplicated -- see function internal comments); when returning true, caller skips
		// AllocateDefaultPins, other nodes still go through unified allocation.
		const bool bPinsPreAllocated = ConfigureNativeFlowNode(FlowNode, MemberName, Payload);
		if (!bPinsPreAllocated)
		{
			FlowNode->AllocateDefaultPins();
		}
		// CreateDelegate's delegate output pin signature is resolved from SelectedFunctionName,
		// usually needs one refresh after AllocateDefaultPins (HandleAnyChange) to make the correct
		// delegate signature appear on the output pin. But HandleAnyChange internally calls
		// HandleAnyChangeWithoutNotifying, which will clear SelectedFunctionName to NAME_None
		// (Inspector shows "Select Function...") when these two conditions both hold:
		//   (a) IsValid() returns false;
		//   (b) Delegate output pin's LinkedTo.Num() == 0 (not connected out).
		// For compiled UFunction targets, IsValid() passes so it won't clear.
		// For CustomEvent targets (not yet on SkeletonGeneratedClass),
		// IsValid() will definitely fail, HandleAnyChange will clear; more importantly even after
		// triggering CompileBlueprint once, there is no guarantee SkeletonGeneratedClass syncs --
		// any subsequent PinConnectionListChanged / PinTypeChanged triggered
		// HandleAnyChangeWithoutNotifying will similarly make IsValid() fail, and only escapes
		// clearing because the Delegate output pin is connected.
		// Strategy: skip HandleAnyChange for CustomEvent (unresolvable) targets,
		// write SelectedFunctionName directly and NotifyGraphChanged, so connections and compile
		// phase lets K2 compiler uniformly resolve output pin signature. UFunction targets take the original path.
		if (UK2Node_CreateDelegate* CreateNode = Cast<UK2Node_CreateDelegate>(FlowNode))
		{
			FString TargetFunctionName;
			bool bHasFunction = Payload.IsValid()
				&& Payload->TryGetStringField(TEXT("function"), TargetFunctionName)
				&& !TargetFunctionName.IsEmpty();

			if (bHasFunction)
			{
				const FName TargetFName(*TargetFunctionName);

				// Register expected event name: subsequent steps (connections, intermediate compile
				// triggered source node reconstruction) may clear SelectedFunctionName via
				// HandleAnyChange when Delegate output pin is unconnected;
				// CompileAndCollectDiagnostics entry restores this per-compile.
				RememberCreateDelegateName(CreateNode->NodeGuid, TargetFName);

				UClass* ScopeClass = CreateNode->GetScopeClass();
				bool bResolvable = false;
				if (ScopeClass != nullptr)
				{
					FMemberReference ProbeRef;
					ProbeRef.SetDirect(TargetFName, FGuid(), TSubclassOf<UObject>(ScopeClass), false);
					bResolvable = (ProbeRef.ResolveMember<UFunction>() != nullptr);
				}

				if (bResolvable)
				{
					// Compiled UFunction target: output pin signature is fully resolvable,
					// won't enter clear branch, takes original HandleAnyChange(true).
					CreateNode->HandleAnyChange(true);

					// Fallback restore: even if logically it won't clear, double refresh timing may
					// still clear; write once as safety net.
					if (CreateNode->GetFunctionName().IsNone())
					{
						CreateNode->SelectedFunctionName = TargetFName;
					}
}
	else
	{
					// CustomEvent target: skip HandleAnyChange, write selection directly to node,
					// and lightly NotifyGraphChanged to refresh UI (does not trigger
					// HandleAnyChangeWithoutNotifying's parse+clear flow).
					// Delegate output pin is temporarily generic delegate; at connection and compile phase,
					// K2 schema infers uniformly from AddDelegate/RemoveDelegate's Delegate pin type.
					CreateNode->SelectedFunctionName = TargetFName;
					if (UEdGraph* EventGraph = CreateNode->GetGraph())
					{
						EventGraph->NotifyGraphChanged();
					}
				}
			}
			else
			{
				// No 'function' field provided (UE original way, user picks manually later), take original path.
				CreateNode->HandleAnyChange(true);
			}
		}
		// BreakStruct/MakeStruct: AllocateDefaultPins creates output pins
		// from StructType, but the node may need reconstruction to expose
		// all output pins (e.g. FHitResult's 20+ member pins).
		if (MemberName == TEXT("BreakStruct") || MemberName == TEXT("MakeStruct"))
		{
			FlowNode->ReconstructNode();
		}
		// AddComponent: when editor places node, palette action creates component template object
		// and back-fills TemplateName pin default; programmatic creation must do the same, otherwise
		// compiles with "unknown template".
		if (MemberName == TEXT("AddComponent"))
		{
			if (UK2Node_AddComponent* AddComponentNode = Cast<UK2Node_AddComponent>(FlowNode))
			{
				// MakeNewComponentTemplate() decides template class only by ReturnValue pin's
				// PinSubCategoryObject (K2Node_AddComponent.cpp
				// MakeNewComponentTemplate internally Cast<UClass>(ReturnPin->...)),
				// and that subtype is back-filled by PostReconstructNode() based on TemplateType.
				// After AllocateDefaultPins() pins are still base class UActorComponent,
				// building template directly would create base class component (title degrades to "Add Actor Component"),
				// semantic would not be target component class. First reconstruct node to get TemplateType onto pins,
				// then build template. No connections established yet, reconstruction won't lose any.
				AddComponentNode->ReconstructNode();
				AddComponentNode->MakeNewComponentTemplate();
			}
		}
		FlowNode->NodePosX = PosX;
		FlowNode->NodePosY = PosY;
		NewNode = FlowNode;
	}
	else if (bDispatcherKind)
	{
		// Event dispatcher node (CallDelegate / AddDelegate / RemoveDelegate):
		// 'name' is the dispatcher (multicast delegate) member variable name.
		// The blueprint must be compiled so the FMulticastDelegateProperty
		// exists on the generated class — otherwise SetFromProperty fails.
		UClass* GeneratedClass = Blueprint->GeneratedClass;
		if (GeneratedClass == nullptr)
		{
			// Blueprint not yet compiled — compile now so the property exists.
			FKismetEditorUtilities::CompileBlueprint(Blueprint);
			GeneratedClass = Blueprint->GeneratedClass;
		}
		FProperty* DelegateProp = GeneratedClass != nullptr
			? GeneratedClass->FindPropertyByName(FName(*MemberName))
			: nullptr;
		// Component event binding (DSL form 2): `Trigger.OnComponentBeginOverlap.Add(...)`'s
		// delegate property is declared on component class (e.g. UPrimitiveComponent), not on blueprint
		// Actor class chain. Translator puts the receiver variable's declared type into 'targetType' --
		// search its class chain again (e.g. OnComponentBeginOverlap on BoxComponent chain).
		if (DelegateProp == nullptr)
		{
			FString TargetTypeName;
			if (Payload.IsValid()
				&& Payload->TryGetStringField(TEXT("targetType"), TargetTypeName)
				&& !TargetTypeName.IsEmpty())
			{
				UClass* TargetClass = ResolveClassFromName(TargetTypeName);
				if (TargetClass != nullptr)
				{
					DelegateProp = TargetClass->FindPropertyByName(FName(*MemberName));
				}
			}
		}
		if (DelegateProp == nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No event dispatcher named '%s' found on '%s' (add one via add_event_dispatcher first)"), *MemberName, *AssetPath));
			return Result;
		}

		UK2Node_BaseMCDelegate* DelegateNode = nullptr;
		if (bCallDelegate)
		{
			DelegateNode = NewObject<UK2Node_CallDelegate>(Graph);
		}
		else if (bAddDelegate)
		{
			DelegateNode = NewObject<UK2Node_AddDelegate>(Graph);
		}
		else // bRemoveDelegate
		{
			DelegateNode = NewObject<UK2Node_RemoveDelegate>(Graph);
		}

		// self context determination: delegate property declared on blueprint family class (self-declared Event
		// Dispatcher, like OnDamage) -> bSelfContext=true; declared on engine class (instance event,
		// like OnActorBeginOverlap) -> bSelfContext=false.
		//
		// Key: SetFromProperty's third parameter (OwnerClass) is directly set as MemberParent by
		// FMemberReference::SetFromField (MemberReference.h L114), which then determines
		// the type of the self("Target") pin in AllocateDefaultPins -- passing GeneratedClass
		// pins the pin type to the blueprint generated class (display name like "Bind Actor A"),
		// rejecting any Actor variable connections. So non-self context must pass the property's
		// real owner class (AActor, etc).
		UClass* DelegateOwnerClass = DelegateProp->GetOwnerClass();
		const bool bSelfContext = DelegateOwnerClass != nullptr
			&& (DelegateOwnerClass == GeneratedClass
				|| DelegateOwnerClass == Blueprint->SkeletonGeneratedClass);
		DelegateNode->SetFromProperty(DelegateProp, bSelfContext,
			bSelfContext ? GeneratedClass : DelegateOwnerClass);
		DelegateNode->SetFlags(RF_Transactional);
		DelegateNode->CreateNewGuid();
		DelegateNode->PostPlacedNewNode();
		Graph->Modify();
		Graph->AddNode(DelegateNode, true, true);
		DelegateNode->AllocateDefaultPins();
		DelegateNode->NodePosX = PosX;
		DelegateNode->NodePosY = PosY;
		NewNode = DelegateNode;
	}
	else if (bComponentBoundEvent)
	{
		// "Bind Event to Component" (UK2Node_ComponentBoundEvent): bind events on this blueprint's SCS
		// component instance (like OnComponentBeginOverlap). Engine semantics:
		// - Component located via ComponentPropertyName (this blueprint's component variable name); there
		//   is no connectable component input pin -> payload 'component' field is required;
		// - Bound event given real name via 'function' field (like OnComponentBeginOverlap),
		//   resolves FMulticastDelegateProperty on component class;
		// - CustomFunctionName is generated by engine per BndEvt__ rules (not customizable),
		//   payload 'name' is IR reference only; event signature output pins generated by engine per
		//   signature (OtherActor / OtherComp / ...).
		FString ComponentVarName;
		if (!Payload->TryGetStringField(TEXT("component"), ComponentVarName) || ComponentVarName.IsEmpty())
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), TEXT("componentboundevent requires the 'component' field (a variable name of a blueprint SCS component)"));
			return Result;
		}
		FString EventName;
		if (!Payload->TryGetStringField(TEXT("function"), EventName) || EventName.IsEmpty())
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), TEXT("componentboundevent requires the 'function' field (the bound event name, e.g. OnComponentBeginOverlap)"));
			return Result;
		}

		UClass* GeneratedClass = Blueprint->GeneratedClass;
		if (GeneratedClass == nullptr)
		{
			FKismetEditorUtilities::CompileBlueprint(Blueprint);
			GeneratedClass = Blueprint->GeneratedClass;
		}

		FObjectProperty* ComponentProperty = GeneratedClass != nullptr
			? CastField<FObjectProperty>(GeneratedClass->FindPropertyByName(FName(*ComponentVarName)))
			: nullptr;
		if (ComponentProperty == nullptr || ComponentProperty->PropertyClass == nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No component variable named '%s' found on '%s' (add the component first)"), *ComponentVarName, *AssetPath));
			return Result;
		}

		FMulticastDelegateProperty* DelegateProp = FindFProperty<FMulticastDelegateProperty>(
			ComponentProperty->PropertyClass, FName(*EventName));
		if (DelegateProp == nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No event dispatcher named '%s' found on component class '%s'"), *EventName, *ComponentProperty->PropertyClass->GetName()));
			return Result;
		}

		UK2Node_ComponentBoundEvent* BoundNode = NewObject<UK2Node_ComponentBoundEvent>(Graph);
		BoundNode->CreateNewGuid();
		BoundNode->PostPlacedNewNode();
		Graph->Modify();
		Graph->AddNode(BoundNode, true, true);
		// Initialize needs GetBlueprint() (node already in graph) and EventReference pin generation,
		// must be before AllocateDefaultPins.
		BoundNode->InitializeComponentBoundEventParams(ComponentProperty, DelegateProp);
		BoundNode->SetFlags(RF_Transactional);
		BoundNode->AllocateDefaultPins();
		BoundNode->NodePosX = PosX;
		BoundNode->NodePosY = PosY;
		NewNode = BoundNode;
	}
else if (bLevelLiteral)
	{
		// Level Actor literal reference (UK2Node_Literal, Blueprint "Create Reference to
		// Actor" node). name = Actor name placed in current level (list_level_actors for
		// enumeration). Engine node semantics (K2Node_Literal.cpp):
		//   - AllocateDefaultPins creates single output pin (ValuePinName = "Value", PC_Object wildcard);
		//   - SetObjectRef(UObject*) solidifies pin type to target Actor's class, and rewrites
		//     PinName/PinFriendlyName to node title (GetNodeTitle display name = Actor name)
		//     -- so subsequent connections use "ReturnValue"/"Value" (position fallback: first output
		//     data pin) or "__target" both work;
		//   - At compile time FKCHandler_LiteralStatement::RegisterNet directly registers the
		//     referenced level Actor as a constant (GetReferencedLevelActor()).
		FString ActorName;
		Payload->TryGetStringField(TEXT("name"), ActorName);
		if (ActorName.IsEmpty())
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), TEXT("levelLiteral requires 'name' (the placed level actor's name, e.g. Target; enumerate via list_level_actors)"));
			return Result;
		}
		UWorld* World = GEditor != nullptr ? GEditor->GetEditorWorldContext().World() : nullptr;
		ULevel* Level = World != nullptr ? World->GetCurrentLevel() : nullptr;
		AActor* LevelActor = nullptr;
		if (Level != nullptr)
		{
			for (const TObjectPtr<AActor>& Candidate : Level->Actors)
			{
				if (IsValid(Candidate) && Candidate->GetName() == ActorName)
				{
					LevelActor = Candidate;
					break;
				}
			}
		}
		if (LevelActor == nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No placed actor named '%s' in the current level (enumerate via list_level_actors)"), *ActorName));
			return Result;
		}

		UK2Node_Literal* LiteralNode = NewObject<UK2Node_Literal>(Graph);
		LiteralNode->CreateNewGuid();
		LiteralNode->PostPlacedNewNode();
		LiteralNode->SetFlags(RF_Transactional);
		Graph->Modify();
		Graph->AddNode(LiteralNode, true, true);
		// SetObjectRef needs the already-AllocateDefaultPins'd ValuePin (GetValuePin takes
		// Pins[0]); first create the pins as wildcard, then bind the specific object.
		LiteralNode->AllocateDefaultPins();
		LiteralNode->SetObjectRef(LevelActor);
		LiteralNode->NodePosX = PosX;
		LiteralNode->NodePosY = PosY;
		NewNode = LiteralNode;
	}
	else if (bTemporaryVariable)
	{
		// Event-graph local variable read node (UK2Node_TemporaryVariable).
		// Optional 'params' [{name, type}, ...]: the first entry's 'type'
		// declares the variable type (same vocabulary as
		// add_blueprint_variable); without it the node stays wildcard and
		// conforms on first connection (the engine's macro-expansion
		// behavior - GetMenuActions registers a wildcard spawner too). The
		// output pin is engine-named 'Variable' (AllocateDefaultPins);
		// VariableType must be set BEFORE AllocateDefaultPins, which copies
		// it onto the pin.
		FEdGraphPinType VarType;
		const TArray<TSharedPtr<FJsonValue>>* TempParamArray = nullptr;
		if (Payload->TryGetArrayField(TEXT("params"), TempParamArray) && TempParamArray->Num() > 0)
		{
			const TSharedPtr<FJsonObject>* TempParamObjPtr = nullptr;
			FString ParamTypeText;
			if (!(*TempParamArray)[0]->TryGetObject(TempParamObjPtr) || !TempParamObjPtr->IsValid()
				|| !(*TempParamObjPtr)->TryGetStringField(TEXT("type"), ParamTypeText)
				|| ParamTypeText.TrimStartAndEnd().IsEmpty())
			{
				Result->SetBoolField(TEXT("added"), false);
				Result->SetStringField(TEXT("reason"), TEXT("temporaryVariable 'params' first entry must be an object with a 'type' (same vocabulary as add_blueprint_variable)"));
				return Result;
			}
			FString TypeReason;
			if (!PinTypeTextToPinType(ParamTypeText, VarType, TypeReason))
			{
				Result->SetBoolField(TEXT("added"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("temporaryVariable has an invalid type '%s': %s"), *ParamTypeText, *TypeReason));
				return Result;
			}
		}

		// Factory-order node creation (FEdGraphSchemaAction_K2NewNode::CreateNode).
		// 'name' is only a node label here - the engine node itself has no
		// variable name (GetDescriptiveCompiledName derives Temp_<category>).
		UK2Node_TemporaryVariable* TempVarNode = NewObject<UK2Node_TemporaryVariable>(Graph);
		TempVarNode->VariableType = VarType;
		TempVarNode->SetFlags(RF_Transactional);
		Graph->Modify();
		Graph->AddNode(TempVarNode, true, true);
		TempVarNode->CreateNewGuid();
		TempVarNode->PostPlacedNewNode();
		TempVarNode->AllocateDefaultPins();
		TempVarNode->NodePosX = PosX;
		TempVarNode->NodePosY = PosY;
		NewNode = TempVarNode;
	}
	else if (bAssignment)
	{
		// Event-graph local variable write node (UK2Node_AssignmentStatement,
		// the engine's "Assign" statement - FKCHandler_AssignmentStatement
		// requires the 'Variable' pin to be connected; 'Value' takes a
		// literal or a net). Both data pins are wildcard and conform to the
		// connected type (NotifyPinConnectionListChanged enforces the pair).
		UK2Node_AssignmentStatement* AssignNode = NewObject<UK2Node_AssignmentStatement>(Graph);
		AssignNode->SetFlags(RF_Transactional);
		Graph->Modify();
		Graph->AddNode(AssignNode, true, true);
		AssignNode->CreateNewGuid();
		AssignNode->PostPlacedNewNode();
		AssignNode->AllocateDefaultPins();
		AssignNode->NodePosX = PosX;
		AssignNode->NodePosY = PosY;
		NewNode = AssignNode;
	}
	else if (bCallParentFunction)
	{
		// Call parent function implementation in subclass (Blueprint "Parent: FunctionName" node): name = overridden
		// member's engine real name (ReceiveBeginPlay / ReceiveTick / blueprint own function name).
		// Parent function resolution is semantically identical to engine's right-click "Add call to parent function"
		// (FBlueprintEditor::OnAddParentNode -> GetCallableParentFunction):
		// Blueprint parent class function table lookup by name (FindFunctionByName aggregates parent chain).
		UClass* ParentClass = Blueprint->ParentClass;
		// Semantically same as engine OnAddParentNode: first get the same-named function on the child class side
		// (the function the override represents), then trace up to its parent implementation via
		// Schema::GetCallableParentFunction; when not found on child class side, fall back to parent class
		// function table lookup by name (e.g. child class hasn't compiled that function yet).
		const UEdGraphSchema_K2* K2Schema = GetDefault<UEdGraphSchema_K2>();
		UClass* ChildClass = Blueprint->SkeletonGeneratedClass != nullptr
			? Blueprint->SkeletonGeneratedClass
			: ParentClass;
		UFunction* ParentFunc = nullptr;
		if (ChildClass != nullptr)
		{
			if (UFunction* ChildFunc = ChildClass->FindFunctionByName(FName(*MemberName)))
			{
				ParentFunc = K2Schema->GetCallableParentFunction(ChildFunc);
			}
		}
		if (ParentFunc == nullptr && ParentClass != nullptr)
		{
			ParentFunc = ParentClass->FindFunctionByName(FName(*MemberName));
		}
		if (ParentFunc == nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(
				TEXT("No function named '%s' on the blueprint's parent class '%s'"),
				*MemberName, ParentClass != nullptr ? *ParentClass->GetName() : TEXT("(none)")));
			return Result;
		}
		UK2Node_CallParentFunction* ParentNode = NewObject<UK2Node_CallParentFunction>(Graph);
		ParentNode->SetFlags(RF_Transactional);
		Graph->Modify();
		Graph->AddNode(ParentNode, true, true);
		ParentNode->CreateNewGuid();
		// Engine order (BlueprintEditor.cpp OnAddParentNode): SetFromFunction must be before
		// AllocateDefaultPins -- pin allocation depends on FunctionReference.
		ParentNode->SetFromFunction(ParentFunc);
		ParentNode->AllocateDefaultPins();
		ParentNode->PostPlacedNewNode();
		// Self-check + reconstruction fallback: when function reference cannot be resolved, AllocateDefaultPins
		// produces no pins or only data pins (same silent-failure family as self-member "0-pin node"). Such
		// nodes cannot connect into execution chain - the caller's then will path around it entirely, leaving
		// an orphaned parent node (Parent_Begin_B / Parent_Tick_B visual feedback; test shows event parent
		// impl ReceiveBeginPlay / ReceiveTick hits this form, plain parent function Greet works fine).
		// First reconstruct per engine convention (function reference may need re-resolution); if still no
		// exec pins, explicitly error and remove the node -- no more silently orphaning it.
		const bool bPureParent = ParentFunc->HasAnyFunctionFlags(FUNC_BlueprintPure);
		auto HasExecPins = [ParentNode]()
		{
			return ParentNode->FindPin(UEdGraphSchema_K2::PN_Execute) != nullptr
				&& ParentNode->FindPin(UEdGraphSchema_K2::PN_Then) != nullptr;
		};
		if (!bPureParent && !HasExecPins())
		{
			ParentNode->ReconstructNode();
		}
		if (!bPureParent && !HasExecPins())
		{
				// Forensics: report ownership class and actual pin list together (A side MCP same name/kind
				// succeeds, B side DSL event parent impl lacks exec pins -- need to see what pins it produced)
			FString PinDump;
			for (const UEdGraphPin* Pin : ParentNode->Pins)
			{
				PinDump += FString::Printf(TEXT("%s[%s/%s] "),
					*Pin->PinName.ToString(),
					Pin->Direction == EGPD_Input ? TEXT("in") : TEXT("out"),
					*Pin->PinType.PinCategory.ToString());
			}
			Graph->RemoveNode(ParentNode);
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(
				TEXT("Parent function '%s' (owner '%s', pure=%d) produced a node without exec in/out pins (pins: %s) - the function reference was not resolved, so it cannot be chained"),
				*MemberName,
				ParentFunc->GetOwnerClass() != nullptr ? *ParentFunc->GetOwnerClass()->GetName() : TEXT("(none)"),
				bPureParent ? 1 : 0, *PinDump));
			return Result;
		}
		ParentNode->NodePosX = PosX;
		ParentNode->NodePosY = PosY;
		NewNode = ParentNode;
	}
	else if (bSelfMember)
	{
		// Self-call: resolve this blueprint's own function by name (compile-time resolution).
		UK2Node_CallFunction* CallNode = NewObject<UK2Node_CallFunction>(Graph);
		CallNode->CreateNewGuid();
		CallNode->PostPlacedNewNode();
		CallNode->FunctionReference.SetSelfMember(FName(*MemberName));
		CallNode->SetFlags(RF_Transactional);
		CallNode->AllocateDefaultPins();
		CallNode->NodePosX = PosX;
		CallNode->NodePosY = PosY;
		Graph->AddNode(CallNode);
		NewNode = CallNode;
	}
	else if (bExternalMember)
	{
		// Cross-blueprint call: target class determined (LookupClass), but function table not yet resolved
		// at node creation time (sibling blueprints not compiled/not loaded back). Resolve target class
		// function by name (compile-time resolution); isomorphic with event nodes EventReference::SetExternalMember.
		UK2Node_CallFunction* CallNode = NewObject<UK2Node_CallFunction>(Graph);
		CallNode->CreateNewGuid();
		CallNode->PostPlacedNewNode();
		CallNode->FunctionReference.SetExternalMember(FName(*MemberName), const_cast<UClass*>(LookupClass));
		CallNode->SetFlags(RF_Transactional);
		CallNode->AllocateDefaultPins();
		CallNode->NodePosX = PosX;
		CallNode->NodePosY = PosY;
		Graph->AddNode(CallNode);
		NewNode = CallNode;
	}
	else
	{
		const bool bCallable = Function->HasAnyFunctionFlags(FUNC_BlueprintCallable | FUNC_BlueprintPure);
		const bool bOverridableEvent = Function->HasAnyFunctionFlags(FUNC_BlueprintEvent);

		if (!bCallable && bOverridableEvent)
		{
			// Overridable event (BeginPlay, Tick, ...): event entry points only
			// live in ubergraph pages, and the engine allows one node per event.
			if (!Blueprint->UbergraphPages.Contains(Graph))
			{
				Result->SetBoolField(TEXT("added"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Event nodes can only be placed in an event graph; '%s' is not one (omit graphName to use the default event graph)"), *Graph->GetName()));
				return Result;
			}
			for (const UEdGraph* Page : Blueprint->UbergraphPages)
			{
				if (Page == nullptr)
				{
					continue;
				}
				for (const UEdGraphNode* Node : Page->Nodes)
				{
					const UK2Node_Event* EventNode = Cast<UK2Node_Event>(Node);
					if (EventNode != nullptr && EventNode->EventReference.GetMemberName() == Function->GetFName())
					{
						// Return the existing node's ID so the caller can
						// still connect subsequent nodes to it.
						Result->SetBoolField(TEXT("added"), true);
						Result->SetStringField(TEXT("nodeId"), EventNode->NodeGuid.ToString());
						Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Event '%s' already exists — reusing existing node"), *MemberName));
						return Result;
					}
				}
			}

			// Same sequence the engine uses for default events
			// (FKismetEditorUtilities::AddDefaultEventNode, Kismet2.cpp).
			UK2Node_Event* EventNode = NewObject<UK2Node_Event>(Graph);
			EventNode->EventReference.SetExternalMember(Function->GetFName(), Function->GetOwnerClass());
			EventNode->bOverrideFunction = true;
			EventNode->CreateNewGuid();
			EventNode->PostPlacedNewNode();
			EventNode->SetFlags(RF_Transactional);
			EventNode->AllocateDefaultPins();
			EventNode->bCommentBubblePinned = true;
			EventNode->bCommentBubbleVisible = true;
			EventNode->NodePosX = PosX;
			EventNode->NodePosY = PosY;
			Graph->AddNode(EventNode);
			NewNode = EventNode;
		}
		else if (bCallable)
		{
			// Same sequence the engine uses when spawning call nodes
			// programmatically (AnimationBlueprintEditorModule.cpp).
			//
			// Array functions (UFunction with ArrayParm metadata, KismetArrayLibrary family:
			// Array_Length / Array_Get / Array_Add / Array_Remove / ...) must be created with engine's
			// specialized node class UK2Node_CallArrayFunction (dragged from palette - this is that class):
			// its TargetArray / Item pins are wildcards; after connections are made, the node's own
			// NotifyPinConnectionListChanged / PostReconstructNode solidifies them to the data type
			// carried by connections, and propagates through ArrayParm groups (TargetArray ->
			// Item/ReturnValue). Ordinary UK2Node_CallFunction lacks this logic -- its ConformContainerPins
			// only recognizes SetParam/MapParam metadata groups; array nodes entering compiler as wildcards
			// would report "Target Array type not yet determined" (UndeterminedPinType_Error).
			UK2Node_CallFunction* CallNode = nullptr;
			if (!Function->GetMetaData(FBlueprintMetadata::MD_ArrayParam).IsEmpty())
			{
				UK2Node_CallArrayFunction* ArrayNode = NewObject<UK2Node_CallArrayFunction>(Graph);
				CallNode = ArrayNode;
			}
			else
			{
				CallNode = NewObject<UK2Node_CallFunction>(Graph);
			}
			CallNode->CreateNewGuid();
			CallNode->PostPlacedNewNode();
			CallNode->SetFromFunction(Function);
			CallNode->SetFlags(RF_Transactional);
			CallNode->AllocateDefaultPins();
			CallNode->NodePosX = PosX;
			CallNode->NodePosY = PosY;
			Graph->AddNode(CallNode);
			NewNode = CallNode;
		}
		else
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("'%s' is neither blueprint-callable nor an overridable event, so it cannot become a node"), *MemberName));
			return Result;
		}
	}

	// Automatic tidy shared with the T-key hotkey: traces every chain back
	// to its root and re-lays the graph out from there, so freshly added
	// nodes stay aligned with their neighbors.
	FForgeMindGraphLayout::ArrangeGraph(Graph);

	Result->SetStringField(TEXT("nodeId"), NewNode->NodeGuid.ToString());
	Result->SetStringField(TEXT("nodeClass"), NewNode->GetClass()->GetName());
	Result->SetStringField(TEXT("graph"), Graph->GetName());
	Result->SetNumberField(TEXT("posX"), PosX);
	Result->SetNumberField(TEXT("posY"), PosY);
	CompileAndCollectDiagnostics(Blueprint, Result);
	// Compile hard error (BS_Error / BS_Unknown, same authoritative criterion as
	// bHasErrors inside CompileAndCollectDiagnostics) AND **this node itself is dead**
	// (after compile Reconstruct, still zero pins -- member/function resolution failed,
	// e.g. MakeLatentActionInfo which doesn't exist in engine) ->
	// response **does not return added field**.
	// Previously unconditional added:true: dead node reports success, compileMessages warnings
	// swallowed by test framework; failure delayed to subsequent connect exposed at wrong location
	// (level streaming 20261005_1257 was this).
	// User decision (2026/10/05): substance failure must not report success - caller (test framework AddNode's
	// added assertion / IR assembly BridgeBool(Result,"added")) treats missing added as failure
	// and throws to stop.
	// Pins.Num()==0 is the key qualifier: test graph-building flow is "add node by node -> connect pin by pin ->
	// save & compile"; intermediate compile errors (e.g. SpawnActor's SpawnTransform by-ref
	// pin not yet connected, 20261005_1340) come from **other nodes'** incomplete connections; newly added node itself
	// has complete pins -- intercepting based on overall graph state would false-positive normal flow; only dead
	// node's own zero-pin state is terminal failure.
	if ((Blueprint->Status == BS_Error || Blueprint->Status == BS_Unknown)
		&& NewNode->Pins.Num() == 0)
	{
		Result->SetStringField(TEXT("reason"),
			TEXT("Node is unresolved (zero pins after compile) - response carries no 'added'; see compileMessages"));
		return Result;
	}
	Result->SetBoolField(TEXT("added"), true);
	return Result;
}

/**
 * Base→derived object reference connection: UE rejects it directly ("X and Y object
 * references are incompatible"), and the autocast table has no such conversion either
 * (FAutocastFunctionMap only covers value-type conversions). The editor's answer is to
 * insert a dynamic Cast node — here we provide the same semantics: produce a pure Cast
 * ('as T', outputs null on failure, does not break the execution flow), source → Cast.Object,
 * Cast output pin ("As <T>", dynamically named via GetCastResultPin) → target.
 * Typical form: UObject output pin (Conv_SoftObjectReferenceToObject) fed to a typed variable
 * (SoftRef_Resolve_B's `Actor a = Ref.TryGet()`). Only takes over when both pins are PC_Object
 * and the source class is a true base of the target class; otherwise DISALLOWs with the
 * original error.
 */
static bool TryConnectViaDynamicCast(UEdGraphPin* PinA, UEdGraphPin* PinB, const UEdGraphSchema_K2* Schema)
{
	UEdGraphPin* OutPin = PinA->Direction == EGPD_Output ? PinA : PinB;
	UEdGraphPin* InPin = PinA->Direction == EGPD_Output ? PinB : PinA;
	if (OutPin->PinType.PinCategory != UEdGraphSchema_K2::PC_Object
		|| InPin->PinType.PinCategory != UEdGraphSchema_K2::PC_Object)
	{
		return false;
	}
	UClass* SourceClass = Cast<UClass>(OutPin->PinType.PinSubCategoryObject.Get());
	UClass* TargetClass = Cast<UClass>(InPin->PinType.PinSubCategoryObject.Get());
	// Base→derived: target class must inherit from source class (e.g. UObject→Actor).
	// Reversing the IsChildOf direction makes the branch never fire (UObject->IsChildOf(Actor)
	// is false; SoftRef_Resolve_B's second pass still falls back to the original rejection).
	if (SourceClass == nullptr || TargetClass == nullptr
		|| SourceClass == TargetClass || !TargetClass->IsChildOf(SourceClass))
	{
		return false;
	}

	// UE 5.0's UEdGraphPin has no GetGraph() (added in later versions); get the graph via the owning node.
	UEdGraph* PinGraph = OutPin->GetOwningNode() != nullptr ? OutPin->GetOwningNode()->GetGraph() : nullptr;
	if (PinGraph == nullptr) { return false; }
	UK2Node_DynamicCast* CastNode = NewObject<UK2Node_DynamicCast>(PinGraph);
	CastNode->TargetType = TargetClass;
	// Pure Cast must be set before AllocateDefaultPins — SetPurity only sets bIsPureCast;
	// after that, AllocateDefaultPins creates pins following the pure-node shape (see the
	// Cast branch comment in HandleAddBlueprintNode).
	CastNode->SetPurity(true);
	CastNode->SetFlags(RF_Transactional);
	PinGraph->AddNode(CastNode, true, true);
	CastNode->CreateNewGuid();
	CastNode->PostPlacedNewNode();
	CastNode->AllocateDefaultPins();
	CastNode->NodePosX = OutPin->GetOwningNode()->NodePosX - 200;
	CastNode->NodePosY = OutPin->GetOwningNode()->NodePosY + 120;

	// Input pin real name PN_ObjectToCast = "Object" (EdGraphSchema_K2.cpp:701);
	// output pin name is "As <TargetClass>" dynamically formed (K2Node_DynamicCast.cpp:63);
	// use GetCastResultPin() to avoid fragile name construction.
	UEdGraphPin* CastIn = CastNode->FindPin(UEdGraphSchema_K2::PN_ObjectToCast, EGPD_Input);
	UEdGraphPin* CastOut = CastNode->GetCastResultPin();
	if (CastIn == nullptr || CastOut == nullptr
		|| !Schema->TryCreateConnection(OutPin, CastIn)
		|| !Schema->TryCreateConnection(CastOut, InPin))
	{
		// Mid-failure leaves no wreckage: destroy the node then fall back to the original DISALLOW error.
		CastNode->DestroyNode();
		return false;
	}
	return true;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleConnectBlueprintPins(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString FromNodeText;
	FString FromPinName;
	FString ToNodeText;
	FString ToPinName;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("fromNode"), FromNodeText) || FromNodeText.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("fromPin"), FromPinName) || FromPinName.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("toNode"), ToNodeText) || ToNodeText.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("toPin"), ToPinName) || ToPinName.IsEmpty())
	{
		Result->SetBoolField(TEXT("connected"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path), fromNode (node id from list_blueprint_graph), fromPin (pin name), toNode (node id), toPin (pin name); pin order does not matter - input/output directions are normalized automatically"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("connected"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s' (create one via create_blueprint first)"), *AssetPath));
		return Result;
	}

	FGuid FromGuid;
	FGuid ToGuid;
	if (!FGuid::Parse(FromNodeText, FromGuid) || !FGuid::Parse(ToNodeText, ToGuid))
	{
		Result->SetBoolField(TEXT("connected"), false);
		Result->SetStringField(TEXT("reason"), TEXT("fromNode/toNode must be the node id strings reported by list_blueprint_graph"));
		return Result;
	}

	UEdGraphNode* FromNode = FindNodeByGuid(Blueprint, FromGuid);
	UEdGraphNode* ToNode = FindNodeByGuid(Blueprint, ToGuid);
	if (FromNode == nullptr || ToNode == nullptr)
	{
		Result->SetBoolField(TEXT("connected"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Node not found (%s) - re-read the graph via list_blueprint_graph; node ids change when nodes are recreated"), FromNode == nullptr ? *FromNodeText : *ToNodeText));
		return Result;
	}

	auto PinNamesOf = [](const UEdGraphNode* Node)
	{
		TArray<FString> Names;
		for (const UEdGraphPin* Pin : Node->Pins)
		{
			if (Pin != nullptr)
			{
				Names.Add(FString::Printf(TEXT("%s (%s)"), *Pin->PinName.ToString(), Pin->Direction == EGPD_Input ? TEXT("input") : TEXT("output")));
			}
		}
		FString Joined;
		for (const FString& Name : Names)
		{
			if (!Joined.IsEmpty()) { Joined += TEXT(", "); }
			Joined += Name;
		}
		return Joined;
	};

	// SwitchEnum case output pins' engine real names are NewEnumeratorN (UUserDefinedEnum's
	// internal enum names); Idle/Run/... on UI are just friendly names mapped by DisplayNameMap.
	// DSL can only provide friendly names (translator side has no enum member ordinal info) --
	// reverse lookup from display name/authored name to real pin name (K2Node_SwitchEnum::SetEnum's
	// EnumEntries, i.e. GetNameStringByIndex, see engine source).
	auto FindEnumCasePin = [](UEdGraphNode* Node, const FString& PinName) -> UEdGraphPin*
	{
		const UK2Node_SwitchEnum* SwitchEnum = Cast<UK2Node_SwitchEnum>(Node);
		if (SwitchEnum == nullptr || SwitchEnum->Enum == nullptr)
		{
			return nullptr;
		}
		const UEnum* EnumObj = SwitchEnum->Enum;
		// Last entry is always _MAX, skip matching
		for (int32 Index = 0; Index < EnumObj->NumEnums() - 1; ++Index)
		{
			const bool bDisplayMatch = EnumObj->GetDisplayNameTextByIndex(Index).ToString().Equals(PinName, ESearchCase::IgnoreCase);
			const bool bAuthoredMatch = EnumObj->GetAuthoredNameStringByIndex(Index).Equals(PinName, ESearchCase::IgnoreCase);
			if (bDisplayMatch || bAuthoredMatch)
			{
				return Node->FindPin(FName(*EnumObj->GetNameStringByIndex(Index)));
			}
		}
		return nullptr;
	};

	UEdGraphPin* FromPin = FromNode->FindPin(FName(*FromPinName));
	if (FromPin == nullptr)
	{
		// BreakStruct output pins (Actor, Component, ...) are created lazily
		// from the struct's properties; if they're missing, reconstruct the
		// node and retry the lookup.
		if (FromNode->IsA<UK2Node_BreakStruct>())
		{
			FromNode->ReconstructNode();
			FromPin = FromNode->FindPin(FName(*FromPinName));
		}
	}
	// Fallback: if the pin name doesn't match exactly, try matching by
	// position index. The DSL uses generic pin names (self, A, B, C, ...)
	// when it can't query the exact UE pin names from the catalog.
	if (FromPin == nullptr)
	{
		FromPin = FindPinByIndex(FromNode, FromPinName);
	}
	if (FromPin == nullptr)
	{
		FromPin = FindEnumCasePin(FromNode, FromPinName);
	}
	if (FromPin == nullptr)
	{
		// DisplayName fallback for UserDefinedStruct member pins
		// (see set_pin_default_value for full rationale).
		for (UEdGraphPin* CandidatePin : FromNode->Pins)
		{
			if (CandidatePin && CandidatePin->GetDisplayName().ToString() == FromPinName)
			{
				FromPin = CandidatePin;
				break;
			}
		}
	}
	if (FromPin == nullptr)
	{
		Result->SetBoolField(TEXT("connected"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Node '%s' has no pin named '%s'; its pins are: %s"), *FromNode->GetNodeTitle(ENodeTitleType::FullTitle).ToString(), *FromPinName, *PinNamesOf(FromNode)));
		return Result;
	}
	UEdGraphPin* ToPin = ToNode->FindPin(FName(*ToPinName));
	if (ToPin == nullptr)
	{
		if (ToNode->IsA<UK2Node_BreakStruct>())
		{
			ToNode->ReconstructNode();
			ToPin = ToNode->FindPin(FName(*ToPinName));
		}
	}
	// Fallback: position-based pin matching for the target node too.
	if (ToPin == nullptr)
	{
		ToPin = FindPinByIndex(ToNode, ToPinName);
	}
	if (ToPin == nullptr)
	{
		ToPin = FindEnumCasePin(ToNode, ToPinName);
	}
	if (ToPin == nullptr)
	{
		// DisplayName fallback for UserDefinedStruct member pins
		// (see set_pin_default_value for full rationale).
		for (UEdGraphPin* CandidatePin : ToNode->Pins)
		{
			if (CandidatePin && CandidatePin->GetDisplayName().ToString() == ToPinName)
			{
				ToPin = CandidatePin;
				break;
			}
		}
	}
	if (ToPin == nullptr)
	{
		Result->SetBoolField(TEXT("connected"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Node '%s' has no pin named '%s'; its pins are: %s"), *ToNode->GetNodeTitle(ENodeTitleType::FullTitle).ToString(), *ToPinName, *PinNamesOf(ToNode)));
		return Result;
	}

	// The K2 schema validates direction + types (exec and data alike) and
	// reports why it refuses; TryCreateConnection re-validates and applies
	// exclusive-exec semantics (breaking the previous link when needed).
	const UEdGraphSchema_K2* Schema = GetDefault<UEdGraphSchema_K2>();
	const FPinConnectionResponse Response = Schema->CanCreateConnection(FromPin, ToPin);
	bool bViaAutocast = false;
	if (Response.Response == CONNECT_RESPONSE_DISALLOW)
	{
		// Autocast: when CanCreateConnection refuses due to type mismatch (e.g.
		// int → string), attempt to create an automatic conversion node
		// (Conv_IntToString, Conv_FloatToString, ...) — the same mechanism
		// UE's editor uses when dragging a wire between mismatched pins.
		// CreateAutomaticConversionNodeAndConnections calls SearchForAutocastFunction
		// to look up FAutocastFunctionMap, spawns a UK2Node_CallFunction for the
		// conversion, and wires source → conversion → target. Returns false when
		// no autocast exists (e.g. exec→data, both inputs) — in that case fall
		// through to the original DISALLOW error.
		if (Schema->CreateAutomaticConversionNodeAndConnections(FromPin, ToPin))
		{
			bViaAutocast = true;
		}
		else if (TryConnectViaDynamicCast(FromPin, ToPin, Schema))
		{
			bViaAutocast = true;
		}
		else
		{
			Result->SetBoolField(TEXT("connected"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Connection refused: %s"), *Response.Message.ToString()));
			return Result;
		}
	}
	if (!bViaAutocast && !Schema->TryCreateConnection(FromPin, ToPin))
	{
		Result->SetBoolField(TEXT("connected"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Connection could not be made (the graph rejected the link)"));
		return Result;
	}

	// Wildcard type propagation: KismetArrayLibrary functions (Array_Add,
	// Array_Length, ...) use wildcard pins whose type is inferred from the
	// connected source. The schema's TryCreateConnection already called
	// PinConnectionListChanged, which triggers UK2Node_CallFunction's
	// ConformContainerPins to propagate types within SetParam groups.
	//
	// However, ConformContainerPins relies on GetTargetFunction() which may
	// return nullptr during editor interaction (when the generated class
	// layout is not ready). In that case, wildcard pins stay wildcard even
	// though they are connected to typed sources.
	//
	// The fix: after the connection is made, manually propagate the type
	// from connected source pins to any remaining wildcard pins on both
	// nodes. This mirrors what ConformContainerPins does (reading the first
	// connected pin's terminal type and writing it to all wildcard pins in
	// the same SetParam group), but works without needing GetTargetFunction.
	//
	// ReconstructNode is called first to ensure the node layout is fresh,
	// then we do a manual pass to propagate types that ReconstructNode may
	// have reset back to wildcard.
	auto PropagateWildcardTypes = [](UEdGraphNode* Node)
	{
		if (Node == nullptr) { return; }

		// UK2Node_GetDataTableRow is exempt. Its Out Row wildcard is typed by the
		// node itself (GetDataTableRowStructType: the table's RowStruct, or - when
		// the table arrives over a variable link and the DataTable pin has no
		// DefaultObject - the Out Row pin's own link). Copying the DataTable
		// input's object type onto that output turned it into a UDataTable
		// reference, and every row read then refused to connect.
		if (Node->IsA<UK2Node_GetDataTableRow>()) { return; }

		// Collect the terminal type from the first non-wildcard connected
		// input pin. Array library functions have TargetArray + NewItem as
		// wildcard pins in the same SetParam group; connecting either one
		// should propagate the type to the other.
		FEdGraphPinType ResolvedType;
		bool bHasResolvedType = false;

		// Fallback source: an already-conformed (non-wildcard) connected input
		// pin. The schema hardens a wildcard pin the moment it is linked (e.g.
		// TargetArray becomes int[] on connect), so on the NEXT connect of the
		// same node no wildcard-connected input remains to probe - the conformed
		// container pin is then the only carrier of the resolved type (without
		// this fallback, Array_Get's ReturnValue stays wildcard and refuses any
		// data-pin connection with "incompatible with string").
		UEdGraphPin* ConformedInputPin = nullptr;

		for (UEdGraphPin* Pin : Node->Pins)
		{
			if (Pin == nullptr || Pin->Direction != EGPD_Input) { continue; }
			// Skip exec pins and non-wildcard pins
			if (Pin->PinType.PinCategory == UEdGraphSchema_K2::PC_Exec) { continue; }
			if (Pin->PinType.PinCategory != UEdGraphSchema_K2::PC_Wildcard)
			{
				if (ConformedInputPin == nullptr && Pin->LinkedTo.Num() > 0)
				{
					ConformedInputPin = Pin;
				}
				continue;
			}
			// Found a wildcard input pin - check if it has a connection
			if (Pin->LinkedTo.Num() > 0)
			{
				UEdGraphPin* SourcePin = Pin->LinkedTo[0];
				if (SourcePin != nullptr && SourcePin->PinType.PinCategory != UEdGraphSchema_K2::PC_Wildcard)
				{
					ResolvedType = SourcePin->PinType;
					bHasResolvedType = true;
					break;
				}
			}
		}

		if (!bHasResolvedType && ConformedInputPin != nullptr)
		{
			ResolvedType = ConformedInputPin->PinType;
			bHasResolvedType = true;
		}

		if (!bHasResolvedType) { return; }

		// Propagate the resolved type to all remaining wildcard input pins
		// on this node (e.g. NewItem on Array_Add gets the FItemData type
		// from TargetArray's connection to Items).
		for (UEdGraphPin* Pin : Node->Pins)
		{
			if (Pin == nullptr || Pin->Direction != EGPD_Input) { continue; }
			if (Pin->PinType.PinCategory == UEdGraphSchema_K2::PC_Wildcard
				&& Pin->PinType.PinCategory != UEdGraphSchema_K2::PC_Exec)
			{
				// Preserve the container type (array vs non-array):
				// TargetArray is wildcard[] (array), NewItem is wildcard (single).
				// If the resolved type is an array and this pin is NOT an array,
				// use the value type instead.
				FEdGraphPinType PinType = ResolvedType;
				if (ResolvedType.IsArray() && !Pin->PinType.IsArray())
				{
					// NewItem pin: use the inner element type, not the array type.
					// For struct arrays (e.g. struct<FItemData>[]), the element
					// type is in PinCategory/PinSubCategoryObject (PC_Struct +
					// FItemData), NOT in PinValueType (which is only populated
					// for wildcard terminal types). So first try PinValueType,
					// and if it's empty/wildcard, fall back to the base type.
					if (ResolvedType.PinValueType.TerminalCategory != NAME_None
						&& ResolvedType.PinValueType.TerminalCategory != UEdGraphSchema_K2::PC_Wildcard)
					{
						PinType.PinCategory = ResolvedType.PinValueType.TerminalCategory;
						PinType.PinSubCategory = ResolvedType.PinValueType.TerminalSubCategory;
						PinType.PinSubCategoryObject = ResolvedType.PinValueType.TerminalSubCategoryObject;
					}
					else
					{
						// PinValueType not populated - the base PinCategory/
						// PinSubCategoryObject already holds the element type
						// (e.g. PC_Struct + FItemData for struct arrays)
						PinType.PinCategory = ResolvedType.PinCategory;
						PinType.PinSubCategory = ResolvedType.PinSubCategory;
						PinType.PinSubCategoryObject = ResolvedType.PinSubCategoryObject;
					}
					PinType.ContainerType = EPinContainerType::None;
					PinType.PinValueType = FEdGraphTerminalType();
				}
				else if (!ResolvedType.IsArray() && Pin->PinType.IsArray())
				{
					// TargetArray pin: wrap the element type in an array
					PinType.ContainerType = EPinContainerType::Array;
					PinType.PinValueType.TerminalCategory = ResolvedType.PinCategory;
					PinType.PinValueType.TerminalSubCategory = ResolvedType.PinSubCategory;
					PinType.PinValueType.TerminalSubCategoryObject = ResolvedType.PinSubCategoryObject;
				}

				Pin->PinType = PinType;
			}
		}

		// Also propagate to wildcard OUTPUT pins (e.g. the "Item" output on
		// Array_Get, the return value on some functions)
		for (UEdGraphPin* Pin : Node->Pins)
		{
			if (Pin == nullptr || Pin->Direction != EGPD_Output) { continue; }
			if (Pin->PinType.PinCategory == UEdGraphSchema_K2::PC_Wildcard)
			{
				FEdGraphPinType PinType = ResolvedType;
				if (ResolvedType.IsArray())
				{
					// Output pins typically return the element type, not the array.
					// Plain arrays (int[], Object[], ...) express the element type in
					// PinCategory/PinSubCategoryObject with TerminalCategory unset -
					// only maps/wildcard containers put it in PinValueType. Without
					// the fallback below, Array_Get's ReturnValue was written as a
					// PinCategory=None type that no pin could connect to.
					if (ResolvedType.PinValueType.TerminalCategory != NAME_None
						&& ResolvedType.PinValueType.TerminalCategory != UEdGraphSchema_K2::PC_Wildcard)
					{
						PinType.PinCategory = ResolvedType.PinValueType.TerminalCategory;
						PinType.PinSubCategory = ResolvedType.PinValueType.TerminalSubCategory;
						PinType.PinSubCategoryObject = ResolvedType.PinValueType.TerminalSubCategoryObject;
					}
					else
					{
						PinType.PinCategory = ResolvedType.PinCategory;
						PinType.PinSubCategory = ResolvedType.PinSubCategory;
						PinType.PinSubCategoryObject = ResolvedType.PinSubCategoryObject;
					}
					PinType.ContainerType = EPinContainerType::None;
				}
				Pin->PinType = PinType;
			}
		}
	};
	// Drive node refresh after connection: nodes with wildcard pins (array function family, etc)
	// reconstruct pins via ReconstructNode; then node's own PostReconstructNode ->
	// PinConnectionListChanged solidifies wildcards to data types carried by connections,
	// and propagates within ArrayParm groups (TargetArray -> Item/ReturnValue). Just relying on
	// the manual PinType direct-write fallback below is insufficient -- types written bypassing
	// the node's own refresh logic will revert to wildcard on next node reconstruction
	// (load/compile rebuild), entering compiler as "type not yet determined".
	auto RefreshWildcardPins = [](UEdGraphNode* Node)
	{
		if (Node == nullptr) { return; }
		for (UEdGraphPin* Pin : Node->Pins)
		{
			if (Pin != nullptr && Pin->PinType.PinCategory == UEdGraphSchema_K2::PC_Wildcard)
			{
				Node->ReconstructNode();
				return;
			}
		}
	};
	RefreshWildcardPins(FromNode);
	RefreshWildcardPins(ToNode);

	PropagateWildcardTypes(FromNode);
	PropagateWildcardTypes(ToNode);

	// After type propagation, notify the nodes so they can do any
	// additional internal cleanup.
	FromNode->NodeConnectionListChanged();
	ToNode->NodeConnectionListChanged();

	// A new wire changes the layout-relevant structure of the graph, so
	// re-run the tidy pass exactly like add_blueprint_node does.
	FForgeMindGraphLayout::ArrangeGraph(FromNode->GetGraph());

	Result->SetBoolField(TEXT("connected"), true);
	CompileAndCollectDiagnostics(Blueprint, Result);
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleSetPinDefaultValue(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString NodeText;
	FString PinName;
	FString Value;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("node"), NodeText) || NodeText.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("pin"), PinName) || PinName.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("value"), Value))
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path), node (node id from list_blueprint_graph), pin (input pin name), value (depends on pin type: \"true\"/\"false\" for bool, \"2.5\" for float, any text for string; for object reference pins use an asset path like /Game/Props/SM_Chair.SM_Chair or a class path; for struct pins like Vector use \"1.0,2.0,3.0\" or \"X=1.0,Y=2.0,Z=3.0\")"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s' (create one via create_blueprint first)"), *AssetPath));
		return Result;
	}

	FGuid NodeGuid;
	if (!FGuid::Parse(NodeText, NodeGuid))
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), TEXT("node must be the node id string reported by list_blueprint_graph"));
		return Result;
	}

	UEdGraphNode* Node = FindNodeByGuid(Blueprint, NodeGuid);
	if (Node == nullptr)
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Node '%s' not found - re-read the graph via list_blueprint_graph; node ids change when nodes are recreated"), *NodeText));
		return Result;
	}

	UEdGraphPin* Pin = Node->FindPin(FName(*PinName));
	if (Pin == nullptr)
	{
		// Fallback: try positional resolution for __argN / __target names
		Pin = FindPinByIndex(Node, PinName);
	}
	if (Pin == nullptr)
	{
		// Fallback: UserDefinedStruct member pins carry an internal name with
		// a GUID suffix (e.g. "Cur_2A4B6C8D...") assigned by
		// FMemberVariableNameHelper::Generate, while the user-facing name
		// lives in the pin's DisplayName. Match by DisplayName so callers
		// can use the friendly name (e.g. "Cur") directly. This affects
		// K2Node_MakeStruct / BreakStruct pins for user-defined structs.
		for (UEdGraphPin* CandidatePin : Node->Pins)
		{
			if (CandidatePin && CandidatePin->GetDisplayName().ToString() == PinName)
			{
				Pin = CandidatePin;
				break;
			}
		}
	}
	if (Pin == nullptr)
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Node '%s' has no pin named '%s'"), *Node->GetNodeTitle(ENodeTitleType::FullTitle).ToString(), *PinName));
		return Result;
	}
	if (Pin->Direction != EGPD_Input)
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Pin '%s' is an output pin; only input pins carry default values"), *PinName));
		return Result;
	}
	const UEdGraphSchema_K2* Schema = GetDefault<UEdGraphSchema_K2>();
	if (Schema->IsExecPin(*Pin))
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Pin '%s' is an exec pin; exec pins have no default values"), *PinName));
		return Result;
	}
	if (!Pin->LinkedTo.IsEmpty())
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Pin '%s' is already wired; a connected pin ignores its default value - break the link first"), *PinName));
		return Result;
	}
	// Mutable pass-by-reference pins (bIsReference && !bIsConst) - e.g. the
	// int& Value input of a macro's Increment node - carry no literal default.
	// They MUST be wired to a variable (an l-value) so the tunnel/macro
	// expansion has something to read/write. Setting a scalar/string default
	// here is semantically invalid and only surfaces as a confusing compile
	// warning ("Target ... resolution failed") - or worse, an unstable macro
	// expansion. Reject it at the API boundary instead of deferring to
	// the compiler.
	//
	// Const-ref pins (bIsReference && bIsConst) are different: the engine
	// derives bIsReference from HasAll(OutParm|ReferenceParm)
	// (EdGraphSchema_K2.cpp CreatePinTypeFromFProperty), so a `const T&`
	// param like KismetArrayLibrary::Array_Add's NewItem
	// (AutoCreateRefTerm = "NewItem") ALSO lands here - but the editor keeps
	// literal defaults enabled for it (BlueprintNodeStatics: bDefaultValueIsIgnored
	// is false for const-ref params of AutoCreateRefTerm functions); the
	// compile auto-creates a ref term from the literal. Only mutable refs
	// are rejected.
	if (Pin->PinType.bIsReference && !Pin->PinType.bIsConst)
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Pin '%s' is a pass-by-reference (ref) pin; it cannot take a literal default and must be wired to a variable"), *PinName));
		return Result;
	}

	// Enum pins author their defaults as display/numeric/internal entry forms
	// ("Red" / "0" / "NewEnumerator0"), but the engine's
	// DefaultValueSimpleValidation (EdGraphSchema_K2.cpp) resolves via
	// GetIndexByNameString which matches internal entry names only - the same
	// internal-name-only wall the variable-default path hit (see
	// TranslateEnumDefaultText). Translate here so both data sources share one
	// resolver instead of drifting apart (spec point 8).
	if (Pin->PinType.PinCategory == UEdGraphSchema_K2::PC_Byte)
	{
		if (const UEnum* Enum = Cast<UEnum>(Pin->PinType.PinSubCategoryObject.Get()))
		{
			FString Translated;
			FString EnumReason;
			if (!TranslateEnumDefaultText(Enum, Value, Translated, EnumReason))
			{
				Result->SetBoolField(TEXT("set"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Pin '%s' %s"), *PinName, *EnumReason));
				return Result;
			}
			Value = Translated;
		}
	}

	Pin->Modify();

	// PinType-aware value assignment: object reference pins store their
	// value in DefaultObject (not the string), struct pins use the engine's
	// import-text format, and scalar/string pins keep the literal as-is.
	const bool bIsObjectPin = (Pin->PinType.PinCategory == UEdGraphSchema_K2::PC_Object
		|| Pin->PinType.PinCategory == UEdGraphSchema_K2::PC_Class
		|| Pin->PinType.PinCategory == UEdGraphSchema_K2::PC_Interface
		|| Pin->PinType.PinCategory == UEdGraphSchema_K2::PC_SoftObject
		|| Pin->PinType.PinCategory == UEdGraphSchema_K2::PC_SoftClass);

	if (bIsObjectPin)
	{
		// Object reference pins: the value is an asset path like
		// /Game/StarterContent/Props/SM_Chair.SM_Chair or a class path
		// /Script/Engine.StaticMesh. Strip the exported class prefix the
		// engine's copy-paste path adds (Class'/Game/.../Asset.Asset')
		// and try to load it. An empty value or "None" clears the reference.
		FString ObjectPath = Value;
		ConstructorHelpers::StripObjectClass(ObjectPath);

		UObject* ResolvedObject = nullptr;
		if (!ObjectPath.IsEmpty() && ObjectPath != TEXT("None"))
		{
			// Class pin (TSubclassOf -> PC_Class): value may be a bare class name from DSL typeof(BP_Save)
			// -- first go through ResolveClassFromName (engine class UClass real name is the unprefixed
			// name; blueprint class goes through "_C" / asset root directory chain, same resolution
			// convention as targetClass / Cast / external variable owner), then fallback to full-path
			// loading. Previously only recognized paths; bare name through ForgeMindFindFirstObject<UObject>
			// might mistakenly grab a Package/UBlueprint of the same name instead of UClass
			// (Save_Create_B's SaveGameClass has this form).
			if (Pin->PinType.PinCategory == UEdGraphSchema_K2::PC_Class)
			{
				ResolvedObject = ResolveClassFromName(ObjectPath);
			}
			if (ResolvedObject == nullptr && FPackageName::IsValidObjectPath(ObjectPath))
			{
				ResolvedObject = LoadObject<UObject>(nullptr, *ObjectPath);
			}
			if (ResolvedObject == nullptr)
			{
				// Try as a class path (/Script/Engine.Actor)
				ResolvedObject = ForgeMindFindFirstObject<UObject>(*ObjectPath);
			}
		}

		// Use the schema's setter so pin-watching and validation fire.
		Schema->TrySetDefaultObject(*Pin, ResolvedObject);
	}
	else if (Pin->PinType.PinCategory == UEdGraphSchema_K2::PC_Text)
	{
		// Text pins carry their value in DefaultTextValue (FText), not the
		// DefaultValue string: TrySetDefaultValue only writes the FString
		// field, so the node's PinDefaultValueChanged callback (e.g.
		// UK2Node_FormatText reading FormatPin->DefaultTextValue to
		// auto-create {0} argument pins) sees an empty text and never
		// regenerates argument pins, and the compiled FText reads empty.
		// Route through the schema's text setter like object pins route
		// through TrySetDefaultObject - it validates non-transient text,
		// writes DefaultTextValue and fires PinDefaultValueChanged.
		Schema->TrySetDefaultText(*Pin, FText::FromString(Value));
	}
	else
	{
		// Scalar, string, name, and struct pins all use DefaultValue.
		// For structs (Vector, Rotator, Transform, ...) the engine reads
		// import-text format from DefaultValue - the same form the details
		// panel writes. Plain string/scalar values pass through unchanged.
		//
		// TrySetDefaultValue is void: when the engine rejects a literal
		// (e.g. "9.0" on an int pin) it silently does nothing and reports
		// no error to the caller - which made failures look like success
		// (set:true, no BP_PIN_DEFAULT warning) while the pin stayed at
		// its default. Pre-validate with the same engine API the setter
		// uses internally, so a rejected value returns set:false with the
		// exact reason instead of disappearing.
		const FString ValidationError = Schema->IsPinDefaultValid(Pin, Value, nullptr, FText());
		if (!ValidationError.IsEmpty())
		{
			Result->SetBoolField(TEXT("set"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Pin '%s' default value '%s' was rejected: %s"), *PinName, *Value, *ValidationError));
			return Result;
		}
		// struct pin defaults pre-validated per engine consumer intersection criteria (20261001_2223
		// second pass root cause; previously IsPinDefaultValid criteria was too broad -- two bad formats
		// both could set:true):
		// - FVector/FRotator: UI split-subpins (EdGraphSchema_K2::SerializeSplitsPins L7141 splits on
		//   commas then Atof assigns; L5112's AutogeneratedDefaultValue is the bare numeric string
		//   "0, 0, 0") -- "X=100"/"(X=100" yields 0 via Atof, node shows (0,0,0) but compiler doesn't
		//   report error; official format is RecombinePin L7245 rewrites to bare numeric string
		//   "100,0,0". Strictly validate each part as pure numeric.
		//   Note: FVector3f not listed -- UE 5.0's FVector3f is UE::Math::TVector<float>,
		//   not USTRUCT (no StaticStruct); TBaseStructure<FVector3f>::Get() cannot be instantiated
		//   (Class.h L3837); blueprint vector pin type is FVector (TVector<double>).
		// - Other structs (including FLinearColor): compile to literal (KismetCompilerVMBackend
		//   L897) goes through UScriptStruct::ImportText, must have parentheses "(R=1,G=0,B=0,A=1)";
		//   missing parentheses triggers GLog "Missing opening parenthesis" at compile time
		//   (Class.cpp L2881), not entering node messages -- CompileAndCollectDiagnostics can't catch.
		//   Silently try import; reject on failure.
		if (Pin->PinType.PinCategory == UEdGraphSchema_K2::PC_Struct && !Pin->PinType.IsContainer())
		{
			UScriptStruct* StructType = Cast<UScriptStruct>(Pin->PinType.PinSubCategoryObject.Get());
			if (StructType == TBaseStructure<FVector>::Get()
				|| StructType == TBaseStructure<FRotator>::Get())
			{
				TArray<FString> Parts;
				Value.ParseIntoArray(Parts, TEXT(","), false);
				bool bValidVectorLiteral = Parts.Num() == 3;
				for (const FString& Part : Parts)
				{
					if (!FDefaultValueHelper::IsStringValidFloat(Part.TrimStartAndEnd()))
					{
						bValidVectorLiteral = false;
						break;
					}
				}
				if (!bValidVectorLiteral)
				{
					Result->SetBoolField(TEXT("set"), false);
					Result->SetStringField(TEXT("reason"), FString::Printf(
						TEXT("Pin '%s' is a Vector/Rotator pin; its default value must be three comma-separated numbers like \"100, 0, 0\" - '%s' compiles but the editor shows 0 for every component"),
						*PinName, *Value));
					return Result;
				}
			}
			else if (StructType != nullptr)
			{
				// Silent trial import: ImportText failure path dereferences ErrorText directly, must silence output
				FOutputDeviceNull SilentErrors;
				uint8* StructData = (uint8*)FMemory_Alloca(StructType->GetStructureSize());
				StructType->InitializeStruct(StructData);
				if (StructType->ImportText(*Value, StructData, nullptr, PPF_None, &SilentErrors, Pin->PinName.ToString()) == nullptr)
				{
					Result->SetBoolField(TEXT("set"), false);
					Result->SetStringField(TEXT("reason"), FString::Printf(
						TEXT("Pin '%s' is a struct pin; its default value must be import-text format with parentheses like \"(R=1,G=0,B=0,A=1)\" - '%s' would fail at compile time with 'Missing opening parenthesis'"),
						*PinName, *Value));
					return Result;
				}
			}
		}
		Schema->TrySetDefaultValue(*Pin, Value);
	}
	Blueprint->Modify();

	Result->SetBoolField(TEXT("set"), true);
	// Read back the actual pin default value after writing (GetDefaultAsString: internal representation
	// compatible with AutogeneratedDefaultValue / ImportText, EdGraphPin.h L468-471). TrySetDefaultValue
	// is void; when its internal IsPinDefaultValid second-pass silently rejects, DefaultValue remains the
	// old value -- readback makes "set:true but node doesn't take effect" type issues immediately visible
	// in logs (20261001_2118 first run: struct value set:true but shows (0,0,0) lesson).
	Result->SetStringField(TEXT("value"), Pin->GetDefaultAsString());
	CompileAndCollectDiagnostics(Blueprint, Result);
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleDeleteBlueprintNode(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString NodeText;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("node"), NodeText) || NodeText.IsEmpty())
	{
		Result->SetBoolField(TEXT("deleted"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path), node (node id from list_blueprint_graph)"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("deleted"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s' (create one via create_blueprint first)"), *AssetPath));
		return Result;
	}

	FGuid NodeGuid;
	if (!FGuid::Parse(NodeText, NodeGuid))
	{
		Result->SetBoolField(TEXT("deleted"), false);
		Result->SetStringField(TEXT("reason"), TEXT("node must be the node id string reported by list_blueprint_graph"));
		return Result;
	}

	UEdGraphNode* Node = FindNodeByGuid(Blueprint, NodeGuid);
	if (Node == nullptr)
	{
		Result->SetBoolField(TEXT("deleted"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Node '%s' not found - re-read the graph via list_blueprint_graph; node ids change when nodes are recreated"), *NodeText));
		return Result;
	}

	UEdGraph* Graph = Node->GetGraph();
	if (Graph == nullptr)
	{
		Result->SetBoolField(TEXT("deleted"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Node has no owning graph - re-read the graph via list_blueprint_graph"));
		return Result;
	}

	// Count the wires the removal is about to break (reported back so the
	// caller can tell what got disconnected).
	int32 BrokenLinks = 0;
	for (const UEdGraphPin* Pin : Node->Pins)
	{
		if (Pin != nullptr)
		{
			BrokenLinks += Pin->LinkedTo.Num();
		}
	}

	// Same sequence the graph editor uses for node deletion: RemoveNode
	// breaks every link through the schema and notifies the graph; the
	// unreferenced node object is then reclaimed by GC.
	Node->Modify();
	Graph->RemoveNode(Node);
	Blueprint->Modify();

	// Removal changes the layout-relevant structure; tidy like the other
	// mutating actions do.
	FForgeMindGraphLayout::ArrangeGraph(Graph);

	Result->SetBoolField(TEXT("deleted"), true);
	Result->SetNumberField(TEXT("brokenLinks"), BrokenLinks);
	CompileAndCollectDiagnostics(Blueprint, Result);
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleBreakBlueprintPins(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString NodeText;
	FString PinName;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("node"), NodeText) || NodeText.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("pin"), PinName) || PinName.IsEmpty())
	{
		Result->SetBoolField(TEXT("broken"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path), node (node id from list_blueprint_graph), pin (pin name); optional toNode + toPin break only that one link instead of every link on the pin"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("broken"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s' (create one via create_blueprint first)"), *AssetPath));
		return Result;
	}

	FGuid NodeGuid;
	if (!FGuid::Parse(NodeText, NodeGuid))
	{
		Result->SetBoolField(TEXT("broken"), false);
		Result->SetStringField(TEXT("reason"), TEXT("node must be the node id string reported by list_blueprint_graph"));
		return Result;
	}

	UEdGraphNode* Node = FindNodeByGuid(Blueprint, NodeGuid);
	if (Node == nullptr)
	{
		Result->SetBoolField(TEXT("broken"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Node '%s' not found - re-read the graph via list_blueprint_graph; node ids change when nodes are recreated"), *NodeText));
		return Result;
	}

	UEdGraphPin* Pin = Node->FindPin(FName(*PinName));
	if (Pin == nullptr)
	{
		Result->SetBoolField(TEXT("broken"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Node '%s' has no pin named '%s'"), *Node->GetNodeTitle(ENodeTitleType::FullTitle).ToString(), *PinName));
		return Result;
	}

	int32 BrokenLinks = 0;

	FString ToNodeText;
	FString ToPinName;
	const bool bSingleLink = Payload->TryGetStringField(TEXT("toNode"), ToNodeText) && !ToNodeText.IsEmpty()
		&& Payload->TryGetStringField(TEXT("toPin"), ToPinName) && !ToPinName.IsEmpty();

	if (bSingleLink)
	{
		// Surgical mode: break exactly one wire, refuse when it does not exist.
		FGuid ToGuid;
		if (!FGuid::Parse(ToNodeText, ToGuid))
		{
			Result->SetBoolField(TEXT("broken"), false);
			Result->SetStringField(TEXT("reason"), TEXT("toNode must be the node id string reported by list_blueprint_graph"));
			return Result;
		}
		UEdGraphNode* ToNode = FindNodeByGuid(Blueprint, ToGuid);
		if (ToNode == nullptr)
		{
			Result->SetBoolField(TEXT("broken"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("toNode '%s' not found - re-read the graph via list_blueprint_graph"), *ToNodeText));
			return Result;
		}
		UEdGraphPin* ToPin = ToNode->FindPin(FName(*ToPinName));
		if (ToPin == nullptr)
		{
			Result->SetBoolField(TEXT("broken"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Node '%s' has no pin named '%s'"), *ToNode->GetNodeTitle(ENodeTitleType::FullTitle).ToString(), *ToPinName));
			return Result;
		}
		if (!Pin->LinkedTo.Contains(ToPin))
		{
			Result->SetBoolField(TEXT("broken"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Pin '%s' is not linked to pin '%s' - nothing to break"), *PinName, *ToPinName));
			return Result;
		}

		Pin->BreakLinkTo(ToPin);
		BrokenLinks = 1;
	}
	else
	{
		// Blanket mode: break every wire attached to the pin.
		BrokenLinks = Pin->LinkedTo.Num();
		if (BrokenLinks == 0)
		{
			Result->SetBoolField(TEXT("broken"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Pin '%s' has no links to break"), *PinName));
			return Result;
		}
		while (Pin->LinkedTo.Num() > 0)
		{
			Pin->BreakLinkTo(Pin->LinkedTo[0]);
		}
	}

	Blueprint->Modify();

	// Broken wires change the layout-relevant structure; tidy like the
	// other mutating actions do.
	FForgeMindGraphLayout::ArrangeGraph(Node->GetGraph());

	Result->SetBoolField(TEXT("broken"), true);
	Result->SetNumberField(TEXT("brokenLinks"), BrokenLinks);
	CompileAndCollectDiagnostics(Blueprint, Result);
	return Result;
}