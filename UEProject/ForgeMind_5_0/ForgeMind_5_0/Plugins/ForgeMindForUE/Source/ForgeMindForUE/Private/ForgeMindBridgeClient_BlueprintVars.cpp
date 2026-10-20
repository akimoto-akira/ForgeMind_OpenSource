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
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path, e.g. /Game/Blueprints/BP_Thing), name (variable name), type (bool, byte, int, int64, float, double, name, string, text, vector, rotator, transform, or a UClass name for an object reference; append '[]' or wrap in array<>/set<>/map<KeyType,ValueType> for container variables); optional category (variable category, default 'Default'), instanceEditable (bool), exposeOnSpawn (bool), tooltip (string, the hover hint in the details panel), bitmask (bool) and bitmaskEnum (flag enum reference: asset path, short name or DSL declaration name)"));
		return Result;
	}

	// 'exposeOnSpawn' and 'instanceEditable' are independent switches - see
	// CheckExposeOnSpawnInstanceEditable. Only an explicit contradiction is
	// refused here, before anything is mutated; a missing instanceEditable key
	// is applied exactly as asked and reported back as a hint.
	FString ExposeOnSpawnReason;
	const EExposeOnSpawnCheck ExposeCheck = CheckExposeOnSpawnInstanceEditable(Payload, ExposeOnSpawnReason);
	if (ExposeCheck == EExposeOnSpawnCheck::Contradictory)
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("errorCode"), TEXT("INVALID_VALUE"));
		Result->SetStringField(TEXT("reason"), ExposeOnSpawnReason);
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
			for (const TCHAR* Setting : { TEXT("category"), TEXT("instanceEditable"), TEXT("exposeOnSpawn"), TEXT("replication"), TEXT("bitmask"), TEXT("bitmaskEnum") })
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

	// Optional category, instance-editability, expose-on-spawn and tooltip tweak
	// the description/metadata before the compile below: the compiler copies
	// Category onto the property and derives CPF_ExposeOnSpawn from the
	// MD_ExposeOnSpawn metadata entry (KismetCompiler.cpp), while the tooltip is
	// the MD_Tooltip metadata entry the details panel shows on hover.
	// AddMemberVariable defaults to CPF_DisableEditOnInstance, so
	// instanceEditable:true clears it.
	FString CategoryText;
	bool bExposeOnSpawn = false;
	bool bInstanceEditable = false;
	FString TooltipText;
	FString ReplicationMode;
	FString OnRepFunc;
	bool bBitmask = false;
	FString BitmaskEnumName;
	const bool bHasCategory = Payload->TryGetStringField(TEXT("category"), CategoryText);
	const bool bHasExposeOnSpawn = Payload->TryGetBoolField(TEXT("exposeOnSpawn"), bExposeOnSpawn);
	const bool bHasInstanceEditable = Payload->TryGetBoolField(TEXT("instanceEditable"), bInstanceEditable);
	const bool bHasTooltip = Payload->TryGetStringField(TEXT("tooltip"), TooltipText);
	const bool bHasReplication = Payload->TryGetStringField(TEXT("replication"), ReplicationMode);
	Payload->TryGetStringField(TEXT("onRep"), OnRepFunc);
	const bool bHasBitmask = Payload->TryGetBoolField(TEXT("bitmask"), bBitmask);
	const bool bHasBitmaskEnum = Payload->TryGetStringField(TEXT("bitmaskEnum"), BitmaskEnumName);
	if (bHasCategory || bHasInstanceEditable || (bHasExposeOnSpawn && bExposeOnSpawn) || bHasTooltip || bHasReplication
		|| bHasBitmask || bHasBitmaskEnum)
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
			if (bHasTooltip)
			{
				// Empty string clears the hint - SetMetaData with an empty value
				// would leave a blank tooltip entry behind.
				if (TooltipText.IsEmpty())
				{
					Variable->RemoveMetaData(FBlueprintMetadata::MD_Tooltip);
				}
				else
				{
					Variable->SetMetaData(FBlueprintMetadata::MD_Tooltip, TooltipText);
				}
			}
			// Replication mirrors FBPVariableDescription / OnChangeReplication:
			// 'replicated' sets CPF_Net; 'repNotify' (or a non-empty onRep) sets
			// CPF_Net|CPF_RepNotify + the OnRep function name; 'none' clears both. The
			// add path previously ignored 'replication' entirely (a silent no-op).
			if (bHasReplication)
			{
				const FString ModeLower = ReplicationMode.ToLower();
				if (ModeLower == TEXT("none"))
				{
					Variable->PropertyFlags &= ~(CPF_Net | CPF_RepNotify);
					Variable->RepNotifyFunc = NAME_None;
				}
				else if (ModeLower == TEXT("repnotify") || !OnRepFunc.IsEmpty())
				{
					Variable->PropertyFlags |= CPF_Net | CPF_RepNotify;
					// UE names the RepNotify callback OnRep_<VarName> automatically; neither the
					// editor nor the DSL authors a custom name. Default to that when the caller
					// gave none (so an explicit onRep, if ever passed, still wins).
					Variable->RepNotifyFunc = OnRepFunc.IsEmpty()
						? FName(*FString::Printf(TEXT("OnRep_%s"), *VarName))
						: FName(*OnRepFunc);
				}
				else // 'replicated'
				{
					Variable->PropertyFlags |= CPF_Net;
					Variable->PropertyFlags &= ~CPF_RepNotify;
					Variable->RepNotifyFunc = NAME_None;
				}
			}
			// Bitmask metadata: the editor's bitmask checkbox control keys off the
			// property-level MD_Bitmask metadata (SPropertyEditorNumeric) and
			// MD_BitmaskEnum associates the property with the flag enum. When the
			// caller asks for bitmask without naming an enum, the variable's own
			// type is the flag enum (a bitmask variable of an enum type is exactly
			// how the editor authors one) - the compiler copies both metadata
			// entries onto the property with Category.
			// MD_BitmaskEnum must carry the enum's SHORT name (GetName/FName, e.g.
			// "EMyFlags_A"), NOT a path: the variable details panel's "Bitmask Enum"
			// combo builds its options from each loaded enum's GetFName() and matches
			// the current value by direct string comparison (BlueprintDetailsCustomization.cpp
			// GetBitmaskEnumTypeName / OnBitmaskEnumTypeChanged), and the numeric
			// bitmask editor + pin type resolve it with FindObject<UEnum>(ANY_PACKAGE,
			// name). A raw asset path or DSL declaration name matches none of these and
			// shows "None". So resolve the reference first (ResolveUserEnumReference:
			// path / short name / DSL alias) then write the resolved enum's short name;
			// an unresolvable name is reported, not written as junk.
			if (bHasBitmask || bHasBitmaskEnum)
			{
				Variable->SetMetaData(FBlueprintMetadata::MD_Bitmask, bBitmask ? TEXT("true") : TEXT("false"));
				FString EnumForMeta = BitmaskEnumName;
				if (EnumForMeta.IsEmpty() && !TypeName.IsEmpty())
				{
					EnumForMeta = TypeName;
				}
				if (!EnumForMeta.IsEmpty())
				{
					if (UEnum* BitmaskEnum = ResolveUserEnumReference(EnumForMeta))
					{
						Variable->SetMetaData(FBlueprintMetadata::MD_BitmaskEnum, *BitmaskEnum->GetName());
						// Hard dependency: the details panel's "Bitmask Enum" combo only
						// lists enums currently in memory (TObjectIterator<UEnum>), so a
						// bitmask variable whose enum is referenced by nothing else shows
						// "None" after an editor reopen. The metadata string does not create
						// a package dependency; FEdGraphPinType serializes PinSubCategoryObject
						// as an object reference (EdGraphPin.cpp), so pointing it at the enum
						// makes the blueprint depend on the enum package and load it with the
						// blueprint. The compiler still creates an FIntProperty (PC_Int ignores
						// the subcategory); the engine's own bitmask pins carry this subcategory.
						Variable->VarType.PinSubCategoryObject = BitmaskEnum;
						// Bitmask default: the authored default is an enum entry name ("A"),
						// an owner-qualified name ("EMyFlags.A") or a combination ("A|C").
						// Resolve it to its numeric value and store that on the variable
						// description BEFORE compiling, so every compile reproduces the CDO
						// default. The direct CDO write in EnsureCompiledVariableDefault alone
						// does not survive a recompile: the compiler re-parses the description's
						// DefaultValue text as an int, and a non-numeric entry name parses to 0,
						// wiping the value on the next compile (e.g. the CompileAndSave that
						// follows this command).
						if (!DefaultValue.IsEmpty() && !DefaultValue.IsNumeric())
						{
							int64 CombinedValue = 0;
							FString InternalName, TranslateReason;
							if (TranslateEnumDefaultText(BitmaskEnum, DefaultValue, InternalName, TranslateReason, &CombinedValue))
							{
								DefaultValue = FString::Printf(TEXT("%lld"), CombinedValue);
								Variable->DefaultValue = DefaultValue;
							}
							else
							{
								DefaultWarnings.Add(FString::Printf(
									TEXT("bitmask default '%s' did not resolve against enum '%s': %s (the default keeps its authored text and may not survive a recompile)"),
									*DefaultValue, *BitmaskEnum->GetName(), *TranslateReason));
							}
						}
					}
					else
					{
						DefaultWarnings.Add(FString::Printf(
							TEXT("bitmaskEnum '%s' did not resolve to a loadable UEnum; MD_BitmaskEnum was not set (the bitmask checkbox still applies)"),
							*EnumForMeta));
					}
				}
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
	if (ExposeCheck == EExposeOnSpawnCheck::MissingInstanceEditable)
	{
		// AddMemberVariable gives a new variable CPF_DisableEditOnInstance, so
		// the variable now really is instance-editable-neutral and the engine
		// will nudge about that at compile time. Say so here instead of letting
		// it surface as an unexplained compile warning.
		TArray<TSharedPtr<FJsonValue>> Hints;
		Hints.Add(MakeShared<FJsonValueString>(TEXT("exposeOnSpawnWithoutInstanceEditable")));
		Result->SetArrayField(TEXT("hints"), Hints);
	}
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
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path), name (variable name); optional settings: category (string), instanceEditable, private, config, transient, saveGame, advancedDisplay, deprecated, exposeOnSpawn, exposeToCinematics, bitmask (booleans), deprecatedMessage (string), tooltip (string), replication ('none' or 'replicated'), replicationCondition (e.g. none, initialOnly, ownerOnly), bitmaskEnum (flag enum reference: asset path, short name or DSL declaration name; empty clears)"));
		return Result;
	}

	// 'exposeOnSpawn' and 'instanceEditable' are independent switches - see
	// CheckExposeOnSpawnInstanceEditable. Refuse an explicit contradiction
	// before touching the variable; a missing instanceEditable key leaves the
	// flag exactly as it is and only reports a hint.
	FString ExposeOnSpawnReason;
	const EExposeOnSpawnCheck ExposeCheck = CheckExposeOnSpawnInstanceEditable(Payload, ExposeOnSpawnReason);
	if (ExposeCheck == EExposeOnSpawnCheck::Contradictory)
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("errorCode"), TEXT("INVALID_VALUE"));
		Result->SetStringField(TEXT("reason"), ExposeOnSpawnReason);
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

				Result->SetBoolField(TEXT("updated"), true);
							Result->SetBoolField(TEXT("dirty"), Blueprint->GetPackage()->IsDirty());
							Result->SetStringField(TEXT("variable"), VarName);
							Result->SetStringField(TEXT("default"), AppliedText);
							Result->SetStringField(TEXT("inheritedFrom"), Lookup.OwnerClass->GetName());
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

	// A new default arrives as JSON and is normalized to the engine's text form
	// for this variable's pin type (structs need special handling, see
	// BlueprintDefaultFromJsonValue) - the same two steps add_blueprint_variable
	// and set_blueprint_variable_default go through. Normalizing up front keeps a
	// bad default from half-applying the other settings.
	FString NewDefaultValue;
	FString DefaultReason;
	TArray<FString> DefaultWarnings;
	const TSharedPtr<FJsonValue> DefaultJson = Payload->TryGetField(TEXT("default"));
	const bool bDefaultPresent = DefaultJson.IsValid();
	if (bDefaultPresent
		&& !BlueprintDefaultFromJsonValue(DefaultJson, Variable.VarType, NewDefaultValue, DefaultReason, DefaultWarnings))
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), DefaultReason);
		AppendWarningsToResult(Result, DefaultWarnings);
		return Result;
	}

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
	FString TooltipText; bool bTooltipPresent = false;
	FString ReplicationMode; bool bReplicationPresent = false;
	FString ConditionText; bool bConditionPresent = false;
	bool bBitmask = false, bBitmaskPresent = false;
	FString BitmaskEnumText; bool bBitmaskEnumPresent = false;
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
		|| !TryGetOptionalString(Payload, TEXT("tooltip"), TooltipText, bTooltipPresent, KeyReason)
		|| !TryGetOptionalString(Payload, TEXT("replication"), ReplicationMode, bReplicationPresent, KeyReason)
		|| !TryGetOptionalString(Payload, TEXT("replicationCondition"), ConditionText, bConditionPresent, KeyReason)
		|| !TryGetOptionalBool(Payload, TEXT("bitmask"), bBitmask, bBitmaskPresent, KeyReason)
		|| !TryGetOptionalString(Payload, TEXT("bitmaskEnum"), BitmaskEnumText, bBitmaskEnumPresent, KeyReason))
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), KeyReason);
		return Result;
	}

	const bool bAnyKeyPresent = bDefaultPresent || bInstanceEditablePresent || bPrivatePresent || bConfigPresent || bTransientPresent
		|| bSaveGamePresent || bAdvancedDisplayPresent || bDeprecatedPresent || bExposeOnSpawnPresent
		|| bExposeToCinematicsPresent || bCategoryPresent || bDeprecatedMessagePresent || bTooltipPresent || bReplicationPresent || bConditionPresent
		|| bBitmaskPresent || bBitmaskEnumPresent;
	if (!bAnyKeyPresent)
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Nothing to change - pass at least one of default, category, instanceEditable, private, config, transient, saveGame, advancedDisplay, deprecated, deprecatedMessage, exposeOnSpawn, exposeToCinematics, tooltip, replication, replicationCondition, bitmask, bitmaskEnum"));
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

	// Property flags follow the editor's variable details panel semantics
	// (BlueprintDetailsCustomization / BlueprintEditorUtils flag setters):
	// 'instanceEditable' clears CPF_DisableEditOnInstance, 'exposeToCinematics'
	// is CPF_Interp, the rest map 1:1. ('private' is metadata, see below.)
	// The response reports 'updated' only - whether one given key really moved
	// the value is deliberately not part of the contract.
	ApplyBoolFlag(Variable.PropertyFlags, CPF_DisableEditOnInstance, !bInstanceEditable, bInstanceEditablePresent);
	ApplyBoolFlag(Variable.PropertyFlags, CPF_Config, bConfig, bConfigPresent);
	ApplyBoolFlag(Variable.PropertyFlags, CPF_Transient, bTransient, bTransientPresent);
	ApplyBoolFlag(Variable.PropertyFlags, CPF_SaveGame, bSaveGame, bSaveGamePresent);
	ApplyBoolFlag(Variable.PropertyFlags, CPF_AdvancedDisplay, bAdvancedDisplay, bAdvancedDisplayPresent);
	ApplyBoolFlag(Variable.PropertyFlags, CPF_Deprecated, bDeprecated, bDeprecatedPresent);
	ApplyBoolFlag(Variable.PropertyFlags, CPF_Interp, bExposeToCinematics, bExposeToCinematicsPresent);

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
	}
	if (bConditionPresent)
	{
		Variable.ReplicationCondition = NewCondition;
	}
	if (bCategoryPresent)
	{
		Variable.Category = FText::FromString(CategoryText);
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
	}
	if (bTooltipPresent)
	{
		if (TooltipText.IsEmpty())
		{
			FBlueprintEditorUtils::RemoveBlueprintVariableMetaData(Blueprint, FName(*VarName), nullptr, FBlueprintMetadata::MD_Tooltip);
		}
		else
		{
			FBlueprintEditorUtils::SetBlueprintVariableMetaData(Blueprint, FName(*VarName), nullptr, FBlueprintMetadata::MD_Tooltip, TooltipText);
		}
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
	}
	// Bitmask metadata mirrors add_blueprint_variable: MD_Bitmask is the checkbox,
	// MD_BitmaskEnum must carry the resolved enum's SHORT name (the details panel's
	// "Bitmask Enum" combo matches options by enum FName; an empty value clears it,
	// an unresolvable one is reported instead of written as junk).
	if (bBitmaskPresent)
	{
		FBlueprintEditorUtils::SetBlueprintVariableMetaData(Blueprint, FName(*VarName), nullptr, FBlueprintMetadata::MD_Bitmask, bBitmask ? TEXT("true") : TEXT("false"));
	}
	if (bBitmaskEnumPresent)
	{
		if (BitmaskEnumText.IsEmpty())
		{
			FBlueprintEditorUtils::RemoveBlueprintVariableMetaData(Blueprint, FName(*VarName), nullptr, FBlueprintMetadata::MD_BitmaskEnum);
		}
		else if (UEnum* BitmaskEnum = ResolveUserEnumReference(BitmaskEnumText))
		{
			FBlueprintEditorUtils::SetBlueprintVariableMetaData(Blueprint, FName(*VarName), nullptr, FBlueprintMetadata::MD_BitmaskEnum, *BitmaskEnum->GetName());
			// Same hard dependency as the add path: point the variable's pin type at the
			// enum so the blueprint package depends on it and the details panel combo can
			// resolve it after an editor reopen.
			if (FBPVariableDescription* VarDesc = Blueprint->NewVariables.FindByPredicate(
				[&VarName](const FBPVariableDescription& Desc) { return Desc.VarName == FName(*VarName); }))
			{
				VarDesc->VarType.PinSubCategoryObject = BitmaskEnum;
			}
		}
		else
		{
			DefaultWarnings.Add(FString::Printf(
				TEXT("bitmaskEnum '%s' did not resolve to a loadable UEnum; MD_BitmaskEnum was not changed"),
				*BitmaskEnumText));
		}
	}
	if (bDefaultPresent)
	{
		// Defaults live on the variable description and feed the generated
		// class, so they are applied with the settings above, before the single
		// recompile below.
		Variable.DefaultValue = NewDefaultValue;
	}

	// Flags feed the generated class, so treat this as a structural change,
	// recompile, and keep the package dirty so the editor's save flow
	// persists it to disk.
	FBlueprintEditorUtils::MarkBlueprintAsStructurallyModified(Blueprint);
	FKismetEditorUtilities::CompileBlueprint(Blueprint);
	Blueprint->MarkPackageDirty();

	// Verify the new default really landed in the compiled CDO, the same way
	// add_blueprint_variable does - a value the compiler could not apply must
	// not report success.
	if (bDefaultPresent)
	{
		FString VerifyReason;
		if (!EnsureCompiledVariableDefault(Blueprint, VarName, NewDefaultValue, VerifyReason))
		{
			Result->SetBoolField(TEXT("updated"), false);
			Result->SetStringField(TEXT("reason"), VerifyReason);
			AppendWarningsToResult(Result, DefaultWarnings);
			return Result;
		}
	}

	Result->SetBoolField(TEXT("updated"), true);
		Result->SetBoolField(TEXT("dirty"), Blueprint->GetPackage()->IsDirty());
		Result->SetStringField(TEXT("variable"), VarName);
		if (bDefaultPresent)
		{
			Result->SetStringField(TEXT("default"), NewDefaultValue);
		}
		Result->SetStringField(TEXT("path"), ObjectPath);
		if (ExposeCheck == EExposeOnSpawnCheck::MissingInstanceEditable)
		{
			// The flag was left untouched, so the variable keeps whatever
			// instance-editability it had - if that is CPF_DisableEditOnInstance,
			// the engine nudges about 'Expose on Spawn' at compile time. Report it
			// here rather than letting it look like an unexplained warning.
			TArray<TSharedPtr<FJsonValue>> Hints;
			Hints.Add(MakeShared<FJsonValueString>(TEXT("exposeOnSpawnWithoutInstanceEditable")));
			Result->SetArrayField(TEXT("hints"), Hints);
		}
		AppendWarningsToResult(Result, DefaultWarnings);
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
