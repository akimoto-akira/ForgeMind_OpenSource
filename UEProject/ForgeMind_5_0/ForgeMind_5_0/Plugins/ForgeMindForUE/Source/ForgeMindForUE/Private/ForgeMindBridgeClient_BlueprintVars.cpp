// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Blueprint variable commands split out of ForgeMindBridgeClient.cpp -
// same class, shared helpers live in ForgeMindBlueprintUtils.

#include "ForgeMindBridgeClient.h"

#include "ForgeMindBlueprintUtils.h"

#include "Dom/JsonObject.h"
#include "Dom/JsonValue.h"
#include "EdGraphSchema_K2.h"
#include "Engine/Blueprint.h"
#include "Engine/BlueprintGeneratedClass.h"
#include "Kismet2/BlueprintEditorUtils.h"
#include "Kismet2/KismetEditorUtilities.h"
#include "Misc/Paths.h"
#include "UObject/UObjectGlobals.h"
#include "UObject/UnrealType.h"

using namespace ForgeMindBlueprintUtils;

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleAddBlueprintVariable(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString VarName;
	FString TypeName;
	// 'path' is the canonical blueprint key; accept 'assetPath' as an alias
	// since the tool's own top-level 'path' carries the .uproject instead.
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("name"), VarName) || VarName.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("type"), TypeName) || TypeName.IsEmpty())
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path, e.g. /Game/Blueprints/BP_Thing), name (variable name), type (bool, byte, int, int64, float, double, name, string, text, vector, rotator, transform, or a UClass name for an object reference; append '[]' or wrap in array<>/set<>/map<KeyType,ValueType> for container variables); optional category (variable category, default 'Default'), instanceEditable (bool) and exposeOnSpawn (bool)"));
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

	// Map the type description onto an FEdGraphPinType. The shared parser
	// covers the base keywords, the container forms reported by
	// list_blueprint_variables ("X[]", "set<X>", "map<K,V>") and the C++
	// template spellings (TArray<X> / TSet<X> / TMap<K,V>).
	FEdGraphPinType PinType;
	FString TypeReason;
	if (!PinTypeTextToPinType(TypeName, PinType, TypeReason))
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), TypeReason);
		return Result;
	}

	// The optional default arrives as JSON and is normalized to the engine's
	// text form for the pin type (structs need special handling, see
	// BlueprintDefaultFromJsonValue).
	FString DefaultValue;
	const TSharedPtr<FJsonValue> DefaultJson = Payload->TryGetField(TEXT("default"));
	TArray<FString> DefaultWarnings;
	if (DefaultJson.IsValid())
	{
		FString DefaultReason;
		if (!BlueprintDefaultFromJsonValue(DefaultJson, PinType, DefaultValue, DefaultReason, DefaultWarnings))
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), DefaultReason);
			AppendWarningsToResult(Result, DefaultWarnings);
			return Result;
		}
	}

	// Cannot mask a variable in any superclass - the engine rejects that.
	if (!FBlueprintEditorUtils::AddMemberVariable(Blueprint, FName(*VarName), PinType, DefaultValue))
	{
		// AddMemberVariable only refuses empty names and names that are
		// already visible in the class chain, so a refusal on a non-empty
		// name means this variable is inherited. A child blueprint cannot
		// redeclare it; what it can do - and what the Class Defaults tab does
		// when a designer types a new value there - is override the default
		// on its own CDO. Settings that belong to the declaring class are
		// accepted only when they merely restate it; a request that would
		// change it fails instead of being dropped.
		const FBlueprintVariableLookup Lookup = LookupBlueprintVariable(Blueprint, VarName);
		if (Lookup.bInherited && Lookup.Property != nullptr)
		{
			TArray<FString> Conflicts;
			CollectInheritedSettingConflicts(Lookup, Payload, Conflicts);
			if (Conflicts.Num() > 0)
			{
				Result->SetBoolField(TEXT("added"), false);
				Result->SetBoolField(TEXT("inheritedOverride"), false);
				Result->SetStringField(TEXT("errorCode"), TEXT("INVALID_VALUE"));
				Result->SetStringField(TEXT("reason"), FString::Printf(
					TEXT("'%s' is inherited from '%s': these settings belong to the class that declares the variable and cannot be changed from a child blueprint (only its default value can) and were not applied: %s"),
					*VarName, *Lookup.OwnerClass->GetName(), *FString::Join(Conflicts, TEXT(", "))));
				return Result;
			}

			FString AppliedText;
			FString WriteReason;
			TArray<FString> InheritedWarnings;
			if (DefaultJson.IsValid()
				&& !WritePropertyDefaultToClassCDO(Blueprint, Lookup, DefaultJson, AppliedText, WriteReason, InheritedWarnings))
			{
				Result->SetBoolField(TEXT("added"), false);
				Result->SetBoolField(TEXT("inheritedOverride"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(
					TEXT("'%s' is inherited from '%s', so only its default value can be overridden from a child blueprint (%s)"),
					*VarName, *Lookup.OwnerClass->GetName(), *WriteReason));
				AppendWarningsToResult(Result, InheritedWarnings);
				return Result;
			}

			// Settings that restate the declaring class are no-ops here; report
			// them so a reader can tell them apart from applied ones.
			TArray<TSharedPtr<FJsonValue>> Restated;
			for (const TCHAR* Setting : { TEXT("category"), TEXT("instanceEditable"), TEXT("exposeOnSpawn"), TEXT("replication") })
			{
				if (Payload->HasField(Setting))
				{
					Restated.Add(MakeShared<FJsonValueString>(Setting));
				}
			}
			if (Restated.Num() > 0)
			{
				Result->SetArrayField(TEXT("inheritedSettingsRestated"), Restated);
			}

			Result->SetBoolField(TEXT("added"), true);
			Result->SetBoolField(TEXT("inheritedOverride"), DefaultJson.IsValid());
			Result->SetBoolField(TEXT("dirty"), Blueprint->GetPackage()->IsDirty());
			Result->SetStringField(TEXT("variable"), VarName);
			Result->SetStringField(TEXT("type"), TypeName);
			Result->SetStringField(TEXT("inheritedFrom"), Lookup.OwnerClass->GetName());
			Result->SetStringField(TEXT("path"), ObjectPath);
			if (!AppliedText.IsEmpty())
			{
				Result->SetStringField(TEXT("default"), AppliedText);
			}
			AppendWarningsToResult(Result, InheritedWarnings);
			return Result;
		}

		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("errorCode"), TEXT("MEMBER_ALREADY_EXISTS"));
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Failed to add variable '%s' to '%s' (the name is already used by this blueprint or one of its parent classes)"), *VarName, *ObjectPath));
		return Result;
	}

	// Optional category, instance-editability and expose-on-spawn tweak the
	// description/metadata before the compile below: the compiler copies
	// Category onto the property and derives CPF_ExposeOnSpawn from the
	// MD_ExposeOnSpawn metadata entry (KismetCompiler.cpp). AddMemberVariable
	// defaults to CPF_DisableEditOnInstance, so instanceEditable:true clears it.
	FString CategoryText;
	bool bExposeOnSpawn = false;
	bool bInstanceEditable = false;
	const bool bHasCategory = Payload->TryGetStringField(TEXT("category"), CategoryText);
	const bool bHasExposeOnSpawn = Payload->TryGetBoolField(TEXT("exposeOnSpawn"), bExposeOnSpawn);
	const bool bHasInstanceEditable = Payload->TryGetBoolField(TEXT("instanceEditable"), bInstanceEditable);
	if (bHasCategory || bHasInstanceEditable || (bHasExposeOnSpawn && bExposeOnSpawn))
	{
		FBPVariableDescription* Variable = Blueprint->NewVariables.FindByPredicate(
			[&VarName](const FBPVariableDescription& Desc) { return Desc.VarName == FName(*VarName); });
		if (Variable != nullptr)
		{
			if (bHasCategory)
			{
				Variable->Category = FText::FromString(CategoryText);
			}
			if (bHasInstanceEditable)
			{
				if (bInstanceEditable)
				{
					Variable->PropertyFlags &= ~CPF_DisableEditOnInstance;
				}
				else
				{
					Variable->PropertyFlags |= CPF_DisableEditOnInstance;
				}
			}
			if (bHasExposeOnSpawn && bExposeOnSpawn)
			{
				Variable->SetMetaData(FBlueprintMetadata::MD_ExposeOnSpawn, TEXT("true"));
			}
		}
	}

	// Recompile so the generated class carries the new property, then keep
	// the package dirty so the editor's save flow persists it to disk.
	FKismetEditorUtilities::CompileBlueprint(Blueprint);
	Blueprint->MarkPackageDirty();

	// Verify the default really landed in the compiled CDO when one was given.
	if (!DefaultValue.IsEmpty())
	{
		FString VerifyReason;
		if (!EnsureCompiledVariableDefault(Blueprint, VarName, DefaultValue, VerifyReason))
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Variable '%s' was added but %s"), *VarName, *VerifyReason));
			return Result;
		}
	}

	Result->SetBoolField(TEXT("added"), true);
	Result->SetBoolField(TEXT("dirty"), Blueprint->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("variable"), VarName);
	Result->SetStringField(TEXT("type"), TypeName);
	Result->SetStringField(TEXT("path"), ObjectPath);
	AppendWarningsToResult(Result, DefaultWarnings);
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleUpdateBlueprintVariable(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString VarName;
	// 'path' is the canonical blueprint key; accept 'assetPath' as an alias
	// since the tool's own top-level 'path' carries the .uproject instead.
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("name"), VarName) || VarName.IsEmpty())
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path), name (variable name); optional settings: category (string), instanceEditable, private, config, transient, saveGame, advancedDisplay, deprecated, exposeOnSpawn, exposeToCinematics (booleans), deprecatedMessage (string), replication ('none' or 'replicated'), replicationCondition (e.g. none, initialOnly, ownerOnly)"));
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
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s' (create one via create_blueprint first)"), *ObjectPath));
		return Result;
	}

	const int32 VarIndex = FBlueprintEditorUtils::FindNewVariableIndex(Blueprint, FName(*VarName));
	if (VarIndex == INDEX_NONE)
	{
		// Not declared here. The name may still exist on an ancestor, where a
		// child's only writable part is the value (its own Class Defaults
		// entry) - flags, category and replication are declared by the class
		// that owns the variable, so a request that would change them fails.
		const FBlueprintVariableLookup Lookup = LookupBlueprintVariable(Blueprint, VarName);
		const bool bInheritedOwned = Lookup.bInherited && Lookup.Property != nullptr && Lookup.OwnerClass != nullptr;
		const TSharedPtr<FJsonValue> DefaultJson = Payload->TryGetField(TEXT("default"));
		if (bInheritedOwned)
		{
			TArray<FString> Conflicts;
			CollectInheritedSettingConflicts(Lookup, Payload, Conflicts);
			if (Conflicts.Num() > 0)
			{
				Result->SetBoolField(TEXT("updated"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(
					TEXT("'%s' is inherited from '%s': these settings belong to the class that declares the variable and cannot be changed from a child blueprint (only its default value can) and were not applied: %s"),
					*VarName, *Lookup.OwnerClass->GetName(), *FString::Join(Conflicts, TEXT(", "))));
				return Result;
			}

			if (DefaultJson.IsValid())
			{
				FString AppliedText;
				FString WriteReason;
				TArray<FString> InheritedWarnings;
				if (!WritePropertyDefaultToClassCDO(Blueprint, Lookup, DefaultJson, AppliedText, WriteReason, InheritedWarnings))
				{
					Result->SetBoolField(TEXT("updated"), false);
					Result->SetStringField(TEXT("reason"), WriteReason);
					AppendWarningsToResult(Result, InheritedWarnings);
					return Result;
				}

				TArray<TSharedPtr<FJsonValue>> ChangedValues;
				ChangedValues.Add(MakeShared<FJsonValueString>(TEXT("default")));
				Result->SetBoolField(TEXT("updated"), true);
				Result->SetBoolField(TEXT("dirty"), Blueprint->GetPackage()->IsDirty());
				Result->SetStringField(TEXT("variable"), VarName);
				Result->SetStringField(TEXT("default"), AppliedText);
				Result->SetStringField(TEXT("inheritedFrom"), Lookup.OwnerClass->GetName());
				Result->SetArrayField(TEXT("changed"), ChangedValues);
				Result->SetStringField(TEXT("path"), ObjectPath);
				AppendWarningsToResult(Result, InheritedWarnings);
				return Result;
			}
		}

		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), bInheritedOwned
			? FString::Printf(TEXT("'%s' is inherited from '%s', so only its default value can be overridden from a child blueprint - pass 'default' (its flags, category and replication belong to the declaring class)"), *VarName, *Lookup.OwnerClass->GetName())
			: FString::Printf(TEXT("No variable named '%s' on '%s' (add one via add_blueprint_variable first)"), *VarName, *ObjectPath));
		return Result;
	}
	FBPVariableDescription& Variable = Blueprint->NewVariables[VarIndex];

	// Read the optional settings first so a malformed payload changes nothing.
	FString KeyReason;
	bool bInstanceEditable = false, bInstanceEditablePresent = false;
	bool bPrivate = false, bPrivatePresent = false;
	bool bConfig = false, bConfigPresent = false;
	bool bTransient = false, bTransientPresent = false;
	bool bSaveGame = false, bSaveGamePresent = false;
	bool bAdvancedDisplay = false, bAdvancedDisplayPresent = false;
	bool bDeprecated = false, bDeprecatedPresent = false;
	bool bExposeOnSpawn = false, bExposeOnSpawnPresent = false;
	bool bExposeToCinematics = false, bExposeToCinematicsPresent = false;
	FString CategoryText; bool bCategoryPresent = false;
	FString DeprecatedMessage; bool bDeprecatedMessagePresent = false;
	FString ReplicationMode; bool bReplicationPresent = false;
	FString ConditionText; bool bConditionPresent = false;
	if (!TryGetOptionalBool(Payload, TEXT("instanceEditable"), bInstanceEditable, bInstanceEditablePresent, KeyReason)
		|| !TryGetOptionalBool(Payload, TEXT("private"), bPrivate, bPrivatePresent, KeyReason)
		|| !TryGetOptionalBool(Payload, TEXT("config"), bConfig, bConfigPresent, KeyReason)
		|| !TryGetOptionalBool(Payload, TEXT("transient"), bTransient, bTransientPresent, KeyReason)
		|| !TryGetOptionalBool(Payload, TEXT("saveGame"), bSaveGame, bSaveGamePresent, KeyReason)
		|| !TryGetOptionalBool(Payload, TEXT("advancedDisplay"), bAdvancedDisplay, bAdvancedDisplayPresent, KeyReason)
		|| !TryGetOptionalBool(Payload, TEXT("deprecated"), bDeprecated, bDeprecatedPresent, KeyReason)
		|| !TryGetOptionalBool(Payload, TEXT("exposeOnSpawn"), bExposeOnSpawn, bExposeOnSpawnPresent, KeyReason)
		|| !TryGetOptionalBool(Payload, TEXT("exposeToCinematics"), bExposeToCinematics, bExposeToCinematicsPresent, KeyReason)
		|| !TryGetOptionalString(Payload, TEXT("category"), CategoryText, bCategoryPresent, KeyReason)
		|| !TryGetOptionalString(Payload, TEXT("deprecatedMessage"), DeprecatedMessage, bDeprecatedMessagePresent, KeyReason)
		|| !TryGetOptionalString(Payload, TEXT("replication"), ReplicationMode, bReplicationPresent, KeyReason)
		|| !TryGetOptionalString(Payload, TEXT("replicationCondition"), ConditionText, bConditionPresent, KeyReason))
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), KeyReason);
		return Result;
	}

	const bool bAnyKeyPresent = bInstanceEditablePresent || bPrivatePresent || bConfigPresent || bTransientPresent
		|| bSaveGamePresent || bAdvancedDisplayPresent || bDeprecatedPresent || bExposeOnSpawnPresent
		|| bExposeToCinematicsPresent || bCategoryPresent || bDeprecatedMessagePresent || bReplicationPresent || bConditionPresent;
	if (!bAnyKeyPresent)
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), TEXT("No optional settings provided - pass at least one of category, instanceEditable, private, config, transient, saveGame, advancedDisplay, deprecated, deprecatedMessage, exposeOnSpawn, exposeToCinematics, replication, replicationCondition"));
		return Result;
	}

	// Validate the replication mode before mutating anything.
	const FString ReplicationLower = ReplicationMode.ToLower();
	if (bReplicationPresent && ReplicationLower != TEXT("none") && ReplicationLower != TEXT("replicated"))
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("replication '%s' is not supported (use 'none' or 'replicated'; 'repNotify' needs an OnRep function graph - set that up in the editor)"), *ReplicationMode));
		return Result;
	}
	ELifetimeCondition NewCondition = COND_None;
	if (bConditionPresent && !ParseReplicationCondition(ConditionText, NewCondition, KeyReason))
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), KeyReason);
		return Result;
	}

	TArray<FString> Changed;

	// Property flags follow the editor's variable details panel semantics
	// (BlueprintDetailsCustomization / BlueprintEditorUtils flag setters):
	// 'instanceEditable' clears CPF_DisableEditOnInstance, 'exposeToCinematics'
	// is CPF_Interp, the rest map 1:1. ('private' is metadata, see below.)
	if (ApplyBoolFlag(Variable.PropertyFlags, CPF_DisableEditOnInstance, !bInstanceEditable, bInstanceEditablePresent))
	{
		Changed.Add(TEXT("instanceEditable"));
	}
	if (ApplyBoolFlag(Variable.PropertyFlags, CPF_Config, bConfig, bConfigPresent))
	{
		Changed.Add(TEXT("config"));
	}
	if (ApplyBoolFlag(Variable.PropertyFlags, CPF_Transient, bTransient, bTransientPresent))
	{
		Changed.Add(TEXT("transient"));
	}
	if (ApplyBoolFlag(Variable.PropertyFlags, CPF_SaveGame, bSaveGame, bSaveGamePresent))
	{
		Changed.Add(TEXT("saveGame"));
	}
	if (ApplyBoolFlag(Variable.PropertyFlags, CPF_AdvancedDisplay, bAdvancedDisplay, bAdvancedDisplayPresent))
	{
		Changed.Add(TEXT("advancedDisplay"));
	}
	if (ApplyBoolFlag(Variable.PropertyFlags, CPF_Deprecated, bDeprecated, bDeprecatedPresent))
	{
		Changed.Add(TEXT("deprecated"));
	}
	if (ApplyBoolFlag(Variable.PropertyFlags, CPF_Interp, bExposeToCinematics, bExposeToCinematicsPresent))
	{
		Changed.Add(TEXT("exposeToCinematics"));
	}

	// Replication mirrors OnChangeReplication: 'none' clears CPF_Net, the
	// rep-notify function and the condition; 'replicated' sets CPF_Net.
	if (bReplicationPresent)
	{
		if (ReplicationLower == TEXT("none"))
		{
			Variable.PropertyFlags &= ~(CPF_Net | CPF_RepNotify);
			Variable.RepNotifyFunc = NAME_None;
			Variable.ReplicationCondition = COND_None;
		}
		else
		{
			Variable.PropertyFlags |= CPF_Net;
			Variable.PropertyFlags &= ~CPF_RepNotify;
			Variable.RepNotifyFunc = NAME_None;
		}
		Changed.Add(TEXT("replication"));
	}
	if (bConditionPresent)
	{
		Variable.ReplicationCondition = NewCondition;
		Changed.Add(TEXT("replicationCondition"));
	}
	if (bCategoryPresent)
	{
		Variable.Category = FText::FromString(CategoryText);
		Changed.Add(TEXT("category"));
	}

	// Metadata-backed settings go through the editor utilities so they fire
	// the same change notifications as the details panel (ExposeOnSpawn is
	// derived into CPF_ExposeOnSpawn by the compiler, DeprecationMessage
	// feeds the deprecation compiler warning). 'private' is the MD_Private
	// metadata entry (OnPrivateChanged) - 5.6 has no CPF_Private flag.
	if (bPrivatePresent)
	{
		if (bPrivate)
		{
			FBlueprintEditorUtils::SetBlueprintVariableMetaData(Blueprint, FName(*VarName), nullptr, FBlueprintMetadata::MD_Private, TEXT("true"));
		}
		else
		{
			FBlueprintEditorUtils::RemoveBlueprintVariableMetaData(Blueprint, FName(*VarName), nullptr, FBlueprintMetadata::MD_Private);
		}
		Changed.Add(TEXT("private"));
	}
	if (bExposeOnSpawnPresent)
	{
		if (bExposeOnSpawn)
		{
			FBlueprintEditorUtils::SetBlueprintVariableMetaData(Blueprint, FName(*VarName), nullptr, FBlueprintMetadata::MD_ExposeOnSpawn, TEXT("true"));
		}
		else
		{
			FBlueprintEditorUtils::RemoveBlueprintVariableMetaData(Blueprint, FName(*VarName), nullptr, FBlueprintMetadata::MD_ExposeOnSpawn);
		}
		Changed.Add(TEXT("exposeOnSpawn"));
	}
	if (bDeprecatedMessagePresent)
	{
		if (DeprecatedMessage.IsEmpty())
		{
			FBlueprintEditorUtils::RemoveBlueprintVariableMetaData(Blueprint, FName(*VarName), nullptr, FBlueprintMetadata::MD_DeprecationMessage);
		}
		else
		{
			FBlueprintEditorUtils::SetBlueprintVariableMetaData(Blueprint, FName(*VarName), nullptr, FBlueprintMetadata::MD_DeprecationMessage, DeprecatedMessage);
		}
		Changed.Add(TEXT("deprecatedMessage"));
	}

	// Flags feed the generated class, so treat this as a structural change,
	// recompile, and keep the package dirty so the editor's save flow
	// persists it to disk.
	FBlueprintEditorUtils::MarkBlueprintAsStructurallyModified(Blueprint);
	FKismetEditorUtilities::CompileBlueprint(Blueprint);
	Blueprint->MarkPackageDirty();

	TArray<TSharedPtr<FJsonValue>> ChangedValues;
	for (const FString& Key : Changed)
	{
		ChangedValues.Add(MakeShared<FJsonValueString>(Key));
	}
	Result->SetBoolField(TEXT("updated"), true);
	Result->SetBoolField(TEXT("dirty"), Blueprint->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("variable"), VarName);
	Result->SetArrayField(TEXT("changed"), ChangedValues);
	Result->SetStringField(TEXT("path"), ObjectPath);
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleListBlueprintVariables(const TSharedPtr<FJsonObject>& Payload)
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
		Result->SetStringField(TEXT("reason"), TEXT("Required field: path (blueprint asset path, e.g. /Game/Blueprints/BP_Thing); optional includeInherited (bool, default false) also lists variables inherited from parent blueprints and C++ classes"));
		return Result;
	}

	bool bIncludeInherited = false;
	if (Payload->HasField(TEXT("includeInherited")) && !Payload->TryGetBoolField(TEXT("includeInherited"), bIncludeInherited))
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), TEXT("'includeInherited' must be a boolean"));
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
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s' (create one via create_blueprint first)"), *ObjectPath));
		return Result;
	}

	TArray<TSharedPtr<FJsonValue>> Variables;
	TSet<FName> SeenNames;

	// The blueprint's own variables come straight from NewVariables - the
	// descriptions are the source of truth the details panel edits, and only
	// these can be changed via update/remove_blueprint_variable.
	for (const FBPVariableDescription& Variable : Blueprint->NewVariables)
	{
		Variables.Add(MakeShared<FJsonValueObject>(BlueprintVariableToJson(Variable, Blueprint->GeneratedClass)));
		SeenNames.Add(Variable.VarName);
	}

	int32 InheritedCount = 0;
	if (bIncludeInherited)
	{
		// Walk the parent chain the way the MyBlueprint panel's "Show
		// Inherited Variables" does: blueprint parents contribute their
		// NewVariables descriptions, native classes their compiled
		// CPF_BlueprintVisible properties (same visibility filter).
		UClass* ParentClass = Blueprint->ParentClass;
		while (ParentClass != nullptr)
		{
			if (Cast<UBlueprintGeneratedClass>(ParentClass) != nullptr)
			{
				const UBlueprint* ParentBlueprint = Cast<UBlueprint>(ParentClass->ClassGeneratedBy);
				if (ParentBlueprint != nullptr)
				{
					for (const FBPVariableDescription& Variable : ParentBlueprint->NewVariables)
					{
						if (SeenNames.Contains(Variable.VarName))
						{
							continue;
						}
						TSharedRef<FJsonObject> Entry = BlueprintVariableToJson(Variable, ParentBlueprint->GeneratedClass);
						Entry->SetStringField(TEXT("inheritedFrom"), ParentBlueprint->GetPathName());
						Variables.Add(MakeShared<FJsonValueObject>(Entry));
						SeenNames.Add(Variable.VarName);
						++InheritedCount;
					}
				}
			}
			else
			{
				for (TFieldIterator<FProperty> It(ParentClass, EFieldIteratorFlags::ExcludeSuper); It; ++It)
				{
					FProperty* Property = *It;
					if (Property->HasAnyPropertyFlags(CPF_Parm)
						|| !Property->HasAllPropertyFlags(CPF_BlueprintVisible)
						|| Property->IsA(FDelegateProperty::StaticClass())
						|| Property->IsA(FMulticastDelegateProperty::StaticClass())
						|| SeenNames.Contains(Property->GetFName()))
					{
						continue;
					}
					TSharedRef<FJsonObject> Entry = NativePropertyToJson(Property, ParentClass);
					Entry->SetStringField(TEXT("inheritedFrom"), ParentClass->GetName());
					Variables.Add(MakeShared<FJsonValueObject>(Entry));
					SeenNames.Add(Property->GetFName());
					++InheritedCount;
				}
			}
			ParentClass = ParentClass->GetSuperClass();
		}
	}

	Result->SetBoolField(TEXT("found"), true);
	Result->SetStringField(TEXT("path"), ObjectPath);
	Result->SetNumberField(TEXT("count"), Variables.Num());
	Result->SetNumberField(TEXT("inheritedCount"), InheritedCount);
	Result->SetArrayField(TEXT("variables"), Variables);
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleSetBlueprintVariableDefault(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString VarName;
	// 'path' is the canonical blueprint key; accept 'assetPath' as an alias
	// since the tool's own top-level 'path' carries the .uproject instead.
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("name"), VarName) || VarName.IsEmpty())
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path, e.g. /Game/Blueprints/BP_Thing), name (variable name), default (new default value)"));
		return Result;
	}

	// The default arrives as JSON and is normalized to the engine's text
	// form for the variable's pin type (structs need special handling, see
	// BlueprintDefaultFromJsonValue).
	FString DefaultValue;
	const TSharedPtr<FJsonValue> DefaultJson = Payload->TryGetField(TEXT("default"));
	if (!DefaultJson.IsValid())
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Missing required field 'default' (the new default value - a scalar for plain types, or {\"X\":x,\"Y\":y,\"Z\":z} / [x,y,z] / \"x,y,z\" for vectors, {\"Pitch\":p,\"Yaw\":y,\"Roll\":r} for rotators, {\"Translation\":{...},\"Rotation\":{...},\"Scale\":{...}} for transforms)"));
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
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s' (create one via create_blueprint first)"), *ObjectPath));
		return Result;
	}

	// First, try the blueprint's own NewVariables (user-created variables
	// added via add_blueprint_variable). If not found there, fall through
	// to the CDO native-property path below – this covers inherited C++
	// properties like DefaultPawnClass on a GameMode, ViewPitchMin/Max on
	// a PlayerCameraManager, or bUseControllerRotationYaw on a Character.
	FBPVariableDescription* Variable = Blueprint->NewVariables.FindByPredicate(
		[&VarName](const FBPVariableDescription& Desc) { return Desc.VarName == FName(*VarName); });

	if (Variable != nullptr)
	{
		// Struct defaults need the variable's pin type for normalization.
		FString DefaultReason;
		TArray<FString> DefaultWarnings;
		if (!BlueprintDefaultFromJsonValue(DefaultJson, Variable->VarType, DefaultValue, DefaultReason, DefaultWarnings))
		{
			Result->SetBoolField(TEXT("updated"), false);
			Result->SetStringField(TEXT("reason"), DefaultReason);
			AppendWarningsToResult(Result, DefaultWarnings);
			return Result;
		}

		Variable->DefaultValue = DefaultValue;

		// Default values feed the generated class, so treat this as a structural
		// change, recompile, and keep the package dirty so the editor's save
		// flow persists it to disk.
		FBlueprintEditorUtils::MarkBlueprintAsStructurallyModified(Blueprint);
		FKismetEditorUtilities::CompileBlueprint(Blueprint);
		Blueprint->MarkPackageDirty();

		// Verify the default really landed in the compiled CDO.
		{
			FString VerifyReason;
			if (!EnsureCompiledVariableDefault(Blueprint, VarName, DefaultValue, VerifyReason))
			{
				Result->SetBoolField(TEXT("updated"), false);
				Result->SetStringField(TEXT("reason"), VerifyReason);
				return Result;
			}
		}

		Result->SetBoolField(TEXT("updated"), true);
		Result->SetBoolField(TEXT("dirty"), Blueprint->GetPackage()->IsDirty());
		Result->SetStringField(TEXT("variable"), VarName);
		Result->SetStringField(TEXT("default"), DefaultValue);
		Result->SetStringField(TEXT("path"), ObjectPath);
		AppendWarningsToResult(Result, DefaultWarnings);
		return Result;
	}

	// --- CDO property fallback ---
	// The property is not a user-created variable of this blueprint: it is
	// either inherited from a parent blueprint or a native property. Its value
	// lives on the generated class CDO - the same object the Details panel
	// edits on the Class Defaults tab - which is also how a child blueprint
	// overrides a parent's default. WritePropertyDefaultToClassCDO is the
	// single entry point for that write.
	const FBlueprintVariableLookup Lookup = LookupBlueprintVariable(Blueprint, VarName);
	if (Lookup.Property == nullptr)
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No variable or native property named '%s' on '%s' (checked blueprint variables and inherited CDO properties)"), *VarName, *ObjectPath));
		return Result;
	}

	FString AppliedText;
	FString WriteReason;
	TArray<FString> WriteWarnings;
	if (!WritePropertyDefaultToClassCDO(Blueprint, Lookup, DefaultJson, AppliedText, WriteReason, WriteWarnings))
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), WriteReason);
		AppendWarningsToResult(Result, WriteWarnings);
		return Result;
	}

	Result->SetBoolField(TEXT("updated"), true);
	Result->SetBoolField(TEXT("dirty"), Blueprint->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("variable"), VarName);
	Result->SetStringField(TEXT("default"), AppliedText);
	if (Lookup.bInherited)
	{
		Result->SetStringField(TEXT("inheritedFrom"), Lookup.OwnerClass->GetName());
	}
	Result->SetStringField(TEXT("path"), ObjectPath);
	AppendWarningsToResult(Result, WriteWarnings);
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleRemoveBlueprintVariable(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString VarName;
	// 'path' is the canonical blueprint key; accept 'assetPath' as an alias
	// since the tool's own top-level 'path' carries the .uproject instead.
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("name"), VarName) || VarName.IsEmpty())
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path, e.g. /Game/Blueprints/BP_Thing), name (variable name)"));
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
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s'"), *ObjectPath));
		return Result;
	}

	if (!Blueprint->NewVariables.ContainsByPredicate(
		[&VarName](const FBPVariableDescription& Desc) { return Desc.VarName == FName(*VarName); }))
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No variable named '%s' on '%s'"), *VarName, *ObjectPath));
		return Result;
	}

	// The editor's own guard before removing a variable (MyBlueprint panel):
	// refuse while any graph still references it.
	if (FBlueprintEditorUtils::IsVariableUsed(Blueprint, FName(*VarName)))
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetBoolField(TEXT("inUse"), true);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Variable '%s' is still referenced by graphs in '%s'; disconnect or remove those references before deleting it"), *VarName, *ObjectPath));
		return Result;
	}

	FBlueprintEditorUtils::RemoveMemberVariable(Blueprint, FName(*VarName));

	// Removing a member changes the generated class layout - recompile and
	// keep the package dirty so the editor's save flow persists the removal.
	FBlueprintEditorUtils::MarkBlueprintAsStructurallyModified(Blueprint);
	FKismetEditorUtilities::CompileBlueprint(Blueprint);
	Blueprint->MarkPackageDirty();

	Result->SetBoolField(TEXT("removed"), true);
	Result->SetBoolField(TEXT("dirty"), Blueprint->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("variable"), VarName);
	Result->SetStringField(TEXT("path"), ObjectPath);
	return Result;
}