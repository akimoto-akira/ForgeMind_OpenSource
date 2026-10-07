// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#pragma once

#include "CoreMinimal.h"
#include "Dom/JsonValue.h"
#include "EdGraph/EdGraphPin.h"
#include "UObject/CoreNetTypes.h"

class UBlueprint;
class UEdGraphNode;
class FProperty;
class UClass;
class UEnum;
class FJsonObject;
class UUserDefinedStruct;
struct FBPVariableDescription;

/**
 * Helpers shared by the blueprint-variable bridge commands
 * (add/set_default/update/list/remove_blueprint_variable). The text
 * vocabularies produced and consumed here are mutually compatible:
 * type text round-trips through add_blueprint_variable, replication
 * condition text through update_blueprint_variable and default value
 * text through set_blueprint_variable_default.
 */
namespace ForgeMindBlueprintUtils
{
	/**
	 * Verifies a compiled variable's default really landed in the CDO and
	 * writes it there when the compiler left the property untouched
	 * (the same write path the Details panel uses for existing variables).
	 */
	bool EnsureCompiledVariableDefault(UBlueprint* Blueprint, const FString& VarName, const FString& ExpectedDefault, FString& OutReason);

	/**
	 * Where a variable name lives on a blueprint's class. This is the single
	 * answer both the add path and the IR op inference need, because
	 * FBlueprintEditorUtils::AddMemberVariable rejects every name that is
	 * already visible anywhere in the class chain
	 * (FBlueprintEditorUtils::GetClassVariableList walks the parent
	 * blueprints, their SCS variables, their timelines and the native
	 * properties) - a child blueprint can never redeclare a parent's variable,
	 * it can only override its default.
	 */
	struct FBlueprintVariableLookup
	{
		/** This blueprint's own declaration, when it has one. */
		FBPVariableDescription* LocalDesc = nullptr;
		/** The compiled property (local or inherited); null while nothing is compiled. */
		FProperty* Property = nullptr;
		/** Class that declares Property - the generated class or the owning ancestor. */
		UClass* OwnerClass = nullptr;
		/** The name is taken, but not by this blueprint - an ancestor's variable or a native property. */
		bool bInherited = false;
		/** The name is taken anywhere in the class chain (also SCS component variables and timelines). */
		bool bNameTaken = false;
	};

	FBlueprintVariableLookup LookupBlueprintVariable(UBlueprint* Blueprint, const FString& VarName);

	/**
	 * The one write path for "give this class property a default": writes into
	 * the generated class CDO, which is the object the Class Defaults tab
	 * edits - so the same call serves a variable this blueprint declares and
	 * one it only inherits (a child overrides a parent's default exactly this
	 * way, and the compiler carries the value over to the next CDO through
	 * CopyPropertiesForUnrelatedObjects). The JSON default is normalized
	 * through the pin type derived from the property, so scalars, structs,
	 * containers and object references all take the same text form the
	 * compiler itself would produce.
	 */
	bool WritePropertyDefaultToClassCDO(UBlueprint* Blueprint, const FBlueprintVariableLookup& Lookup,
		const TSharedPtr<FJsonValue>& DefaultJson, FString& OutAppliedText, FString& OutReason,
		TArray<FString>& OutWarnings);

	/**
	 * Which of the settings a payload asks for cannot be applied to an
	 * inherited variable. Flags, category and replication are declared by the
	 * class that owns the variable, so from a child blueprint they can only be
	 * re-stated, never changed: a request that contradicts the declaring
	 * class is unsatisfiable and is reported here instead of being dropped
	 * (dropping it is how a DSL ends up looking like it took effect). A
	 * request that matches is not a conflict. OutConflicts receives one
	 * "key=value" entry per unsatisfiable setting.
	 */
	void CollectInheritedSettingConflicts(const FBlueprintVariableLookup& Lookup,
		const TSharedPtr<FJsonObject>& Payload, TArray<FString>& OutConflicts);

	/**
	 * Converts a JSON default value into the text form the blueprint
	 * compiler expects (engine-verified): struct defaults are parsed by
	 * FBlueprintEditorUtils::PropertyValueFromString, which for FVector/
	 * FRotator wants comma separated components (rotator order
	 * Pitch,Yaw,Roll), for FTransform the pipe separated
	 * "tx,ty,tz|pitch,yaw,roll|sx,sy,sz" form and for any other struct
	 * the property text form (Prop=Value,...). Unknown struct member
	 * names are collected into OutWarnings instead of being dropped
	 * silently.
	 */
	bool BlueprintDefaultFromJsonValue(const TSharedPtr<FJsonValue>& DefaultJson, const FEdGraphPinType& PinType, FString& OutDefault, FString& OutReason, TArray<FString>& OutWarnings);

	/** Attaches non-fatal warnings to a response object (and the editor log). */
	void AppendWarningsToResult(const TSharedPtr<FJsonObject>& Result, const TArray<FString>& Warnings);

	/** Optional key reader: key presence decides, wrong-typed values are rejected. */
	bool TryGetOptionalBool(const TSharedPtr<FJsonObject>& Payload, const TCHAR* Key, bool& OutValue, bool& bPresent, FString& OutReason);
	bool TryGetOptionalString(const TSharedPtr<FJsonObject>& Payload, const TCHAR* Key, FString& OutValue, bool& bPresent, FString& OutReason);

	/** Replication condition text <-> enum (camelCase names round-trip). */
	bool ParseReplicationCondition(const FString& InText, ELifetimeCondition& OutCondition, FString& OutReason);
	const TCHAR* ReplicationConditionToText(ELifetimeCondition Condition);

	/** Sets or clears a single flag bit when the payload key was present. */
	bool ApplyBoolFlag(uint64& PropertyFlags, uint64 Flag, bool bEnable, bool bPresent);

	/**
	 * Type text in the vocabulary add_blueprint_variable accepts, so a
	 * reported type can be fed straight back into add.
	 */
	FString PinTypeToText(const FEdGraphPinType& PinType);
	FString PropertyTypeToText(const FProperty* Property);

	/**
	 * Inverse of PinTypeToText for add_blueprint_variable: accepts the base
	 * keywords, the reported "X[]" / "set<X>" / "map<K,V>" forms plus the
	 * C++ spellings TArray<X> / TSet<X> / TMap<K,V>. Map semantics follow
	 * the compiler: the base pin type is the key, PinValueType the value.
	 */
	bool PinTypeTextToPinType(const FString& TypeText, FEdGraphPinType& OutPinType, FString& OutReason);

	/** Response entry builders for list_blueprint_variables. */
	void AddVariableSettingsToJson(FJsonObject& Entry, uint64 PropertyFlags, ELifetimeCondition Condition,
		bool bPrivate, bool bExposeOnSpawn, const FString& DeprecatedMessage, bool bHasDeprecatedMessage,
		FName RepNotifyFunc);
	TSharedRef<FJsonObject> BlueprintVariableToJson(const FBPVariableDescription& Variable, const UClass* GeneratedClass);
	TSharedRef<FJsonObject> NativePropertyToJson(const FProperty* Property, const UClass* OwnerClass);

	// ---- Blueprint graph helpers (shared across split .cpp files) ----

	/** Same normalization the variable handlers use (package.asset form). */
	UBlueprint* ResolveBlueprint(const FString& AssetPath);

	/**
	 * Generic asset resolver: loads any UObject from the given asset path
	 * (e.g. /Game/Folder/MyAsset or /Game/Folder/MyAsset.MyAsset).
	 * Works for blueprints, structs, enums, input actions, data assets,
	 * widget blueprints, and any other UObject-based asset.
	 * Returns nullptr if the asset does not exist or is not loaded.
	 */
	UObject* ResolveAsset(const FString& AssetPath);

	/** Finds a node by its NodeGuid across the blueprint's editable graphs. */
	UEdGraphNode* FindNodeByGuid(UBlueprint* Blueprint, const FGuid& Guid);

	/**
	 * Fallback pin matcher: when the DSL provides generic pin names
	 * (self, A, B, C, ...), match by position index among same-direction
	 * data pins. "self" → first input data pin; "A" → second; "B" → third;
	 * also handles "ReturnValue" → first output data pin, and member
	 * names like "X"/"Y"/"Z" → matching output pin by name (already
	 * handled by FindPin, this is only a positional fallback).
	 */
	UEdGraphPin* FindPinByIndex(UEdGraphNode* Node, const FString& PinName);

	/**
	 * Session-level registry: the event name a CreateDelegate node was
	 * created with. UK2Node_CreateDelegate::HandleAnyChange clears
	 * SelectedFunctionName whenever IsValid() fails while the delegate
	 * output pin is still unconnected - which is exactly the state between
	 * add_blueprint_node and the first connect_blueprint_pins (and again
	 * during intermediate compiles that trigger source-node
	 * reconstruction). Remember the intended name so it can be restored
	 * right before any compile.
	 */
	void RememberCreateDelegateName(const FGuid& NodeGuid, const FName& FunctionName);

	/** Re-applies every remembered CreateDelegate function name that was cleared. */
	void RestoreCreateDelegateNames(UBlueprint* Blueprint);

	/**
	 * Session-level registry: DSL user-defined enum alias. The C# declaration name of a DSL enum
	 * (e.g., EState) may differ from the asset name (/Game/.../EState_B), while Blueprint DSL references
	 * the declaration name when referencing parameter/variable types. CreateEnumAsset (IR side) registers
	 * the "declaration name -> asset path" alias after enum creation; PinTypeTextToPinType falls back
	 * to the alias when resolution by asset name fails.
	 */
	void RegisterUserTypeEnumAlias(const FString& DeclaredName, const FString& AssetPath);

	/**
	 * Resolve enum asset by the alias registered above (LoadObject); returns nullptr if no alias or load fails.
	 */
	UEnum* ResolveUserTypeEnumAlias(const FString& DeclaredName);

	/**
	 * Compile + dirty, mirroring the variable commands, then harvest the
	 * per-node compiler messages into a response array so the caller can
	 * self-check (list_blueprint_graph also reports them per node).
	 */
	void CompileAndCollectDiagnostics(UBlueprint* Blueprint, TSharedPtr<FJsonObject>& Result);

	/** Scoped guard that marks a blueprint structurally modified and leaves it dirty. */
	void MarkBlueprintModified(UBlueprint* Blueprint);

	/** Checks whether a graph with the given name already exists (ubergraphs, functions, macros). */
	bool GraphNameExists(UBlueprint* Blueprint, const FString& Name);

	/**
	 * Scans a UserDefinedStruct for members whose internal VarName starts with
	 * "MemberVar_" (the auto-generated name CreateUserDefinedStruct / AddVariable
	 * assigns before RenameVariable gives it a friendly name). Such members are
	 * leftover defaults that were never properly renamed — the caller should
	 * consider deleting them. If any are found, a warning string is appended to
	 * the Warnings array.
	 */
	void CheckForDefaultMemberVars(class UUserDefinedStruct* Struct, TArray<FString>& Warnings);
}