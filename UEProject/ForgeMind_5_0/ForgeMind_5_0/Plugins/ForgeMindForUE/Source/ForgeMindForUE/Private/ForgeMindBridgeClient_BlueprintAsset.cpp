// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Blueprint asset-level commands (save_asset, delete_asset) split out of
// ForgeMindBridgeClient_BlueprintGraph.cpp - same class, shared helpers
// live in ForgeMindBlueprintUtils.

#include "ForgeMindBridgeClient.h"

#include "ForgeMindBlueprintUtils.h"

#include "AssetRegistry/AssetRegistryModule.h"
#include "AssetRegistry/ARFilter.h"
#include "Dom/JsonObject.h"
#include "Dom/JsonValue.h"
#include "Engine/Blueprint.h"
#include "Engine/DataTable.h"
#include "Engine/UserDefinedStruct.h"
#include "Engine/UserDefinedEnum.h"
#include "HAL/FileManager.h"
#include "Misc/Paths.h"
#include "Misc/PackageName.h"
#include "UObject/SoftObjectPath.h"
#include "ObjectTools.h"
#include "Kismet2/StructureEditorUtils.h"
#include "Kismet2/EnumEditorUtils.h"
#include "Kismet2/KismetEditorUtilities.h"
#include "Subsystems/AssetEditorSubsystem.h"
#include "Editor.h"
#include "Internationalization/Text.h"
#include "UserDefinedStructure/UserDefinedStructEditorData.h"
#include "UObject/Package.h"
#include "UObject/SavePackage.h"
#include "UObject/UObjectGlobals.h"
#include "UObject/UObjectIterator.h"
// FEnumProperty / FArrayProperty / FScriptArrayHelper - used by create_input_action to
// write a plugin asset's properties through reflection only.
#include "UObject/UnrealType.h"

using namespace ForgeMindBlueprintUtils;

namespace
{
	/**
	 * Saves one loaded package to its .uasset and reports what happened.
	 * The 5.6 SavePackage flavor takes an error string out-parameter and
	 * never raises a dialog, which is exactly what the bridge needs.
	 */
	bool SaveLoadedPackage(UPackage* Package, FString& OutError)
	{
		const FString PackageName = Package->GetName();
		// Map packages (containing UWorld) must be saved as .umap, regular assets as .uasset.
		const FString Extension = Package->ContainsMap()
			? FPackageName::GetMapPackageExtension()
			: FPackageName::GetAssetPackageExtension();
		FString Filename;
		if (!FPackageName::TryConvertLongPackageNameToFilename(PackageName, Filename, Extension) || Filename.IsEmpty())
		{
			// Fallback for a freshly CreatePackage'd asset with no disk footprint:
			// map the /Game mount point onto the project's Content directory.
			const FString RelativePath = PackageName.RightChop(FString(TEXT("/Game")).Len());
			Filename = FPaths::ProjectContentDir() / (RelativePath + Extension);
		}

		// FullyRegistered makes the content browser pick the asset up the
		// moment the file lands on disk; the Error device collects any save
		// failure text instead of raising a dialog.
		FStringOutputDevice ErrorDevice;
		FSavePackageArgs SaveArgs;
		SaveArgs.TopLevelFlags = EObjectFlags::RF_Public | EObjectFlags::RF_Standalone;
		SaveArgs.bSlowTask = false;
		SaveArgs.SaveFlags = SAVE_NoError;
		SaveArgs.Error = &ErrorDevice;

		if (!UPackage::SavePackage(Package, nullptr, *Filename, SaveArgs))
		{
			OutError = ErrorDevice;
			if (OutError.IsEmpty())
			{
				OutError = TEXT("SavePackage failed");
			}
			return false;
		}
		return true;
	}
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleSaveAsset(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	bool bAll = false;
	if (Payload.IsValid())
	{
		Payload->TryGetBoolField(TEXT("all"), bAll);
	}

	if (bAll)
	{
		// Every loaded dirty /Game asset (blueprints and everything else the
		// session touched), saved in load order.
		TArray<UPackage*> DirtyPackages;
		for (TObjectIterator<UPackage> It; It; ++It)
		{
			UPackage* Package = *It;
			if (Package == nullptr || Package->HasAnyFlags(RF_Transient))
			{
				continue;
			}
			if (!Package->GetName().StartsWith(TEXT("/Game/")) || !Package->IsDirty())
			{
				continue;
			}
			DirtyPackages.Add(Package);
		}

		TArray<FString> SavedPaths;
		TArray<FString> Failed;
		for (UPackage* Package : DirtyPackages)
		{
			FString Error;
			if (SaveLoadedPackage(Package, Error))
			{
				SavedPaths.Add(Package->GetName());
			}
			else
			{
				Failed.Add(FString::Printf(TEXT("%s: %s"), *Package->GetName(), *Error));
			}
		}

		Result->SetBoolField(TEXT("saved"), Failed.Num() == 0);
		Result->SetNumberField(TEXT("count"), SavedPaths.Num());
		TArray<TSharedPtr<FJsonValue>> PathValues;
		for (const FString& SavedPath : SavedPaths)
		{
			PathValues.Add(MakeShared<FJsonValueString>(SavedPath));
		}
		Result->SetField(TEXT("paths"), MakeShared<FJsonValueArray>(PathValues));
		if (Failed.Num() > 0)
		{
			TArray<TSharedPtr<FJsonValue>> FailedValues;
			for (const FString& Entry : Failed)
			{
				FailedValues.Add(MakeShared<FJsonValueString>(Entry));
			}
			Result->SetField(TEXT("failed"), MakeShared<FJsonValueArray>(FailedValues));
		}
		return Result;
	}

	FString AssetPath;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty())
	{
		Result->SetBoolField(TEXT("saved"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (asset path, e.g. /Game/Blueprints/BP_Thing or /Game/Inventory/IA_Jump); or all:true to save every dirty /Game asset"));
		return Result;
	}

	UObject* Asset = ResolveAsset(AssetPath);
	if (Asset == nullptr)
	{
		Result->SetBoolField(TEXT("saved"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No asset found at '%s'"), *AssetPath));
		return Result;
	}

	UPackage* Package = Asset->GetPackage();
	if (Package == nullptr)
	{
		Result->SetBoolField(TEXT("saved"), false);
		Result->SetStringField(TEXT("reason"), TEXT("The asset has no package to save"));
		return Result;
	}
	if (!Package->IsDirty())
	{
		// Nothing to write; report success without touching the file.
		Result->SetBoolField(TEXT("saved"), true);
		Result->SetBoolField(TEXT("dirty"), false);
		Result->SetStringField(TEXT("path"), Package->GetName());
		Result->SetStringField(TEXT("note"), TEXT("Package was not dirty; nothing written"));
		return Result;
	}

	FString Error;
	if (!SaveLoadedPackage(Package, Error))
	{
		Result->SetBoolField(TEXT("saved"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Saving '%s' failed: %s"), *Package->GetName(), *Error));
		return Result;
	}

	Result->SetBoolField(TEXT("saved"), true);
	Result->SetBoolField(TEXT("dirty"), Package->IsDirty());
	Result->SetStringField(TEXT("path"), Package->GetName());
	FString FileOnDisk;
	if (!FPackageName::TryConvertLongPackageNameToFilename(Package->GetName(), FileOnDisk, FPackageName::GetAssetPackageExtension()) || FileOnDisk.IsEmpty())
	{
		FileOnDisk = FPaths::ProjectContentDir() / (Package->GetName().RightChop(FString(TEXT("/Game")).Len()) + FPackageName::GetAssetPackageExtension());
	}
	Result->SetStringField(TEXT("file"), FileOnDisk);
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleDeleteAsset(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty())
	{
		Result->SetBoolField(TEXT("deleted"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (asset path, e.g. /Game/Blueprints/BP_Thing or /Game/Inventory/IA_Jump)"));
		return Result;
	}

	// Never delete outside project content - the operation is irreversible.
	if (!AssetPath.StartsWith(TEXT("/Game/")))
	{
		Result->SetBoolField(TEXT("deleted"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Only assets under /Game/ can be deleted"));
		return Result;
	}

	UObject* Asset = ResolveAsset(AssetPath);
	if (Asset == nullptr)
	{
		Result->SetBoolField(TEXT("deleted"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No asset found at '%s'"), *AssetPath));
		return Result;
	}

	UPackage* Package = Asset->GetPackage();
	const FString PackageName = Package != nullptr ? Package->GetName() : AssetPath;

	// Refuse while another /Game asset still points at the blueprint, listing
	// the referencers so the caller can break those references first.
	IAssetRegistry& AssetRegistry = FModuleManager::LoadModuleChecked<FAssetRegistryModule>(TEXT("AssetRegistry")).Get();
	// Hard references are what make a delete unsafe; soft references survive
	// the removal and are not listed.
	UE::AssetRegistry::FDependencyQuery Query(UE::AssetRegistry::EDependencyQuery::Hard);
	TArray<FName> Referencers;
	AssetRegistry.GetReferencers(FName(*PackageName), Referencers, UE::AssetRegistry::EDependencyCategory::Package, Query);
	if (Referencers.Num() > 0)
	{
		Result->SetBoolField(TEXT("deleted"), false);
		TArray<FString> ReferencerNames;
		for (const FName& Referencer : Referencers)
		{
			ReferencerNames.Add(Referencer.ToString());
		}
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Asset is still referenced by: %s - break those references first"), *FString::Join(ReferencerNames, TEXT(", "))));
		TArray<TSharedPtr<FJsonValue>> ReferencerValues;
		for (const FString& ReferencerName : ReferencerNames)
		{
			ReferencerValues.Add(MakeShared<FJsonValueString>(ReferencerName));
		}
		Result->SetField(TEXT("referencers"), MakeShared<FJsonValueArray>(ReferencerValues));
		return Result;
	}

	// ObjectTools performs the reference check, content-browser cleanup and
	// GC; no confirmation dialog and no cancellable slow task keeps this
	// headless-safe.
	TArray<UObject*> ObjectsToDelete = { Asset };
	const int32 DeletedCount = ObjectTools::DeleteObjects(ObjectsToDelete, /*bShowConfirmation=*/false, ObjectTools::EAllowCancelDuringDelete::CancelNotAllowed);
	if (DeletedCount == 0)
	{
		Result->SetBoolField(TEXT("deleted"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Deletion of '%s' was refused by the editor (check the log for the reason)"), *PackageName));
		return Result;
	}

	FString FileOnDisk;
	bool bFileRemoved = false;
	if (FPackageName::TryConvertLongPackageNameToFilename(PackageName, FileOnDisk, FPackageName::GetAssetPackageExtension()) && !FileOnDisk.IsEmpty())
	{
		bFileRemoved = !IFileManager::Get().FileExists(*FileOnDisk);
	}

	Result->SetBoolField(TEXT("deleted"), true);
	Result->SetBoolField(TEXT("fileRemoved"), bFileRemoved);
	Result->SetStringField(TEXT("path"), PackageName);
	if (bFileRemoved)
	{
		Result->SetStringField(TEXT("file"), FileOnDisk);
	}
	return Result;
}

// ---------------------------------------------------------------------------
// create_blueprint_struct
//
// Creates a User-Defined Struct asset (the "Blueprint Structure" the content
// browser creates via right-click → Blueprints → Structure). Unlike a
// UBlueprint (which is a class), this is a UScriptStruct data container —
// its members are described by FEdGraphPinType, the same vocabulary
// add_blueprint_variable uses for blueprint member variables.
//
// Payload keys:
//   path        — project asset path, e.g. /Game/Blueprints/FMyStruct
//   members     — array of {name, type} entries (same type vocabulary as
//                 add_blueprint_variable; containers and object references
//                 are accepted; omit for an empty struct to fill in later)
//
// The struct is compiled, registered with the asset registry and left dirty
// — the caller must save_asset to persist it.
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleCreateBlueprintStruct(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty())
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (project asset path, e.g. /Game/Blueprints/FMyStruct); optional: members (array of {name, type})"));
		return Result;
	}

	// Path normalisation + clobber check: single entry point shared by every
	// create_* asset command (see PrepareNewAssetLocation).
	FString PackagePath;
	FString AssetName;
	FString PathReason;
	if (!PrepareNewAssetLocation(AssetPath, TEXT("/Game/Blueprints/FMyStruct"), PackagePath, AssetName, PathReason))
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), PathReason);
		return Result;
	}

	// Parse members before creating the asset so a bad type does not leave
	// an orphaned package behind.
	struct FMemberSpec { FString Name; FEdGraphPinType PinType; };
	TArray<FMemberSpec> Members;

	const TArray<TSharedPtr<FJsonValue>>* MembersArray = nullptr;
	if (Payload->TryGetArrayField(TEXT("members"), MembersArray) && MembersArray != nullptr)
	{
		for (const TSharedPtr<FJsonValue>& Entry : *MembersArray)
		{
			TSharedPtr<FJsonObject> MemberJson = Entry.IsValid() ? Entry->AsObject() : nullptr;
			if (!MemberJson.IsValid())
			{
				Result->SetBoolField(TEXT("created"), false);
				Result->SetStringField(TEXT("reason"), TEXT("Each member must be a JSON object with 'name' and 'type'"));
				return Result;
			}

			FString MemberName;
			FString MemberType;
			if (!MemberJson->TryGetStringField(TEXT("name"), MemberName) || MemberName.IsEmpty()
				|| !MemberJson->TryGetStringField(TEXT("type"), MemberType) || MemberType.IsEmpty())
			{
				Result->SetBoolField(TEXT("created"), false);
				Result->SetStringField(TEXT("reason"), TEXT("Each member requires 'name' and 'type'"));
				return Result;
			}

			FEdGraphPinType PinType;
			FString TypeReason;
			if (!PinTypeTextToPinType(MemberType, PinType, TypeReason))
			{
				Result->SetBoolField(TEXT("created"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Member '%s' has invalid type '%s': %s"), *MemberName, *MemberType, *TypeReason));
				return Result;
			}

			Members.Add({ MemberName, PinType });
		}
	}

	// Create the package, then use the engine's factory to create the
	// UUserDefinedStruct. CreateUserDefinedStruct handles the NewObject call,
	// internal initialisation and listener registration.
	UPackage* Package = CreatePackage(*PackagePath);
	UUserDefinedStruct* Struct = FStructureEditorUtils::CreateUserDefinedStruct(
		Package, *AssetName, RF_Public | RF_Standalone);
	if (Struct == nullptr)
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Struct creation failed at '%s'"), *PackagePath));
		return Result;
	}

	FAssetRegistryModule::AssetCreated(Struct);

	// Add members one by one. AddVariable appends a new variable description
	// with an auto-generated name and Guid; we then rename it to the
	// requested friendly name via RenameVariable.
	TArray<FString> AddedMembers;
	TArray<FString> Warnings;
	for (const FMemberSpec& Member : Members)
	{
		if (!FStructureEditorUtils::AddVariable(Struct, Member.PinType))
		{
			Warnings.Add(FString::Printf(TEXT("Member '%s' was not added"), *Member.Name));
			continue;
		}

		// The newly added variable is the last entry in VarDesc — grab its
		// Guid so we can rename it to the requested friendly name.
		TArray<FStructVariableDescription>& VarDescs = FStructureEditorUtils::GetVarDesc(Struct);
		if (VarDescs.Num() > 0)
		{
			const FGuid NewVarGuid = VarDescs.Last().VarGuid;
			FStructureEditorUtils::RenameVariable(Struct, NewVarGuid, Member.Name);
		}
		AddedMembers.Add(Member.Name);
	}

	// Remove the leftover default boolean member that CreateUserDefinedStruct
	// always inserts.  We must add the caller's members FIRST because
	// RemoveVariable refuses to make the struct empty (its internal guard is
	// OldNum > 1 with bAllowToMakeEmpty = false).  After adding, scan for any
	// member whose internal VarName starts with "MemberVar_" (the engine's
	// auto-generated name for unnamed members) and remove it.
	if (Members.Num() > 0 && AddedMembers.Num() > 0)
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
		}
	}

	// Notify listeners and recompile so the struct's property layout
	// reflects the new members.
	FStructureEditorUtils::OnStructureChanged(Struct, FStructureEditorUtils::EStructureEditorChangeInfo::AddedVariable);
	FStructureEditorUtils::CompileStructure(Struct);

	Struct->MarkPackageDirty();

	Result->SetBoolField(TEXT("created"), true);
	Result->SetBoolField(TEXT("dirty"), Struct->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("path"), FString::Printf(TEXT("%s.%s"), *PackagePath, *AssetName));
	Result->SetNumberField(TEXT("memberCount"), AddedMembers.Num());
	TArray<TSharedPtr<FJsonValue>> MemberValues;
	for (const FString& AddedMember : AddedMembers)
	{
		MemberValues.Add(MakeShared<FJsonValueString>(AddedMember));
	}
	Result->SetField(TEXT("members"), MakeShared<FJsonValueArray>(MemberValues));
	CheckForDefaultMemberVars(Struct, Warnings);
	AppendWarningsToResult(Result, Warnings);
	return Result;
}

// ---------------------------------------------------------------------------
// create_blueprint_enum
//
// Creates a User-Defined Enum asset (the "Blueprint Enum" the content
// browser creates via right-click → Blueprints → Enumeration). The enum
// is a UEnum with BlueprintType that can be used as a variable type in
// blueprints.
//
// Payload keys:
//   path        — project asset path, e.g. /Game/Blueprints/EMyEnum
//   entries     — array of strings (enumerator display names); omit for
//                 an empty enum to fill in later
//   bitflags    — true sets the enumerator-as-bitflags meta data
//                 (FEnumEditorUtils::SetEnumeratorBitflagsTypeState), the
//                 same flag the enum editor's "Bitflags" checkbox writes.
//                 The editor keys the bitmask property checkbox
//                 (SPropertyEditorNumeric) and the MakeBitmask literal node
//                 (UK2Node_BitmaskLiteral::ValidateBitflagsEnumType) off
//                 this metadata; without it a bitmask variable default can
//                 never round-trip.
//
// The enum is registered with the asset registry and left dirty — the
// caller must save_asset to persist it.
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleCreateBlueprintEnum(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty())
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (project asset path, e.g. /Game/Blueprints/EMyEnum); optional: entries (array of enumerator display names)"));
		return Result;
	}

	// Path normalisation + clobber check: single entry point shared by every
	// create_* asset command (see PrepareNewAssetLocation).
	FString PackagePath;
	FString AssetName;
	FString PathReason;
	if (!PrepareNewAssetLocation(AssetPath, TEXT("/Game/Blueprints/EMyEnum"), PackagePath, AssetName, PathReason))
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), PathReason);
		return Result;
	}

	// Parse the enumerator display names before creating the asset.
	TArray<FString> DisplayNames;
	const TArray<TSharedPtr<FJsonValue>>* EntriesArray = nullptr;
	if (Payload->TryGetArrayField(TEXT("entries"), EntriesArray) && EntriesArray != nullptr)
	{
		for (const TSharedPtr<FJsonValue>& Entry : *EntriesArray)
		{
			// Each entry can be a plain string or a {name, displayName} object.
			if (Entry->Type == EJson::String)
			{
				DisplayNames.Add(Entry->AsString());
			}
			else if (Entry->Type == EJson::Object)
			{
				TSharedPtr<FJsonObject> EntryObj = Entry->AsObject();
				FString Name, DispName;
				if (EntryObj->TryGetStringField(TEXT("name"), Name) && !Name.IsEmpty())
				{
					// 'name' is the display name; 'displayName' is an alias.
					EntryObj->TryGetStringField(TEXT("displayName"), DispName);
					DisplayNames.Add(DispName.IsEmpty() ? Name : DispName);
				}
			}
		}
	}

	// Create the package, then use the engine's factory to create the
	// UUserDefinedEnum. CreateUserDefinedEnum handles the NewObject call
	// and sets up the BlueprintType metadata.
	UPackage* Package = CreatePackage(*PackagePath);
	UUserDefinedEnum* Enum = Cast<UUserDefinedEnum>(FEnumEditorUtils::CreateUserDefinedEnum(
		Package, *AssetName, RF_Public | RF_Standalone));
	if (Enum == nullptr)
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Enum creation failed at '%s'"), *PackagePath));
		return Result;
	}

	FAssetRegistryModule::AssetCreated(Enum);

	// Optional enumerator-as-bitflags metadata: SetEnumeratorBitflagsTypeState
	// is the same write the enum editor's "Bitflags" checkbox takes (the
	// editor-side metadata key FBlueprintMetadata::MD_Bitflags). The MakeBitmask
	// literal node validates its enum against this key, and the bitmask
	// property checkbox lists the entries once the enum carries it.
	bool bBitflags = false;
	if (Payload->TryGetBoolField(TEXT("bitflags"), bBitflags) && bBitflags)
	{
		FEnumEditorUtils::SetEnumeratorBitflagsTypeState(Enum, true);
	}

	// Add each enumerator. AddNewEnumeratorForUserDefinedEnum appends a new
	// entry with an auto-generated unique name; we then set its display name
	// via SetEnumeratorDisplayName.
	TArray<FString> AddedNames;
	int32 EnumeratorIndex = 0;
	for (const FString& DisplayName : DisplayNames)
	{
		FEnumEditorUtils::AddNewEnumeratorForUserDefinedEnum(Enum);
		// The new enumerator is appended at the end, so its index is
		// Enum->NumEnums() - 2 (the last slot is always _MAX).
		const int32 NewIndex = Enum->NumEnums() - 2;
		FEnumEditorUtils::SetEnumeratorDisplayName(Enum, NewIndex, FText::FromString(DisplayName));
		AddedNames.Add(DisplayName);
		EnumeratorIndex++;
	}

	Enum->MarkPackageDirty();

	Result->SetBoolField(TEXT("created"), true);
	Result->SetBoolField(TEXT("dirty"), Enum->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("path"), FString::Printf(TEXT("%s.%s"), *PackagePath, *AssetName));
	Result->SetNumberField(TEXT("entryCount"), AddedNames.Num());
	TArray<TSharedPtr<FJsonValue>> EntryValues;
	for (const FString& AddedName : AddedNames)
	{
		EntryValues.Add(MakeShared<FJsonValueString>(AddedName));
	}
	Result->SetField(TEXT("entries"), MakeShared<FJsonValueArray>(EntryValues));
	return Result;
}

// ---------------------------------------------------------------------------
// create_blueprint_interface
//
// Creates a Blueprint Interface asset (the "Blueprint Interface" the content
// browser creates via right-click → Blueprints → Blueprint Interface). A
// Blueprint Interface is a collection of named function signatures (messages)
// with no implementation; other blueprints implement the interface and
// provide bodies for each message.
//
// Payload keys:
//   path        — project asset path, e.g. /Game/Tests/UE5_0/IDamageable
//   messages    — array of { name, params:[{name,type}] }; each becomes a
//                 function graph on the interface (signature only, no body)
//
// The interface is created via the same FKismetEditorUtilities::CreateBlueprint
// call the engine's UBlueprintInterfaceFactory uses (EditorFactories.cpp:7040):
// ParentClass = UInterface, BPType = BPTYPE_Interface. Each message is then
// added as a function graph by reusing HandleCreateFunctionGraph.
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleCreateBlueprintInterface(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty())
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (project asset path, e.g. /Game/Blueprints/IDamageable); optional: messages (array of { name, params:[{name,type}] })"));
		return Result;
	}

	// Path normalisation + clobber check: single entry point shared by every
	// create_* asset command (see PrepareNewAssetLocation).
	FString PackagePath;
	FString AssetName;
	FString PathReason;
	if (!PrepareNewAssetLocation(AssetPath, TEXT("/Game/Blueprints/IDamageable"), PackagePath, AssetName, PathReason))
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), PathReason);
		return Result;
	}

	// Guard against stale in-memory packages (same logic as create_blueprint).
	UPackage* Package = nullptr;
	if (UPackage* StalePackage = FindObject<UPackage>(nullptr, *PackagePath))
	{
		if (UBlueprint* StaleBP = FindObject<UBlueprint>(StalePackage, *AssetName))
		{
			if (GEditor)
			{
				UAssetEditorSubsystem* AssetEditorSys = GEditor->GetEditorSubsystem<UAssetEditorSubsystem>();
				if (AssetEditorSys)
				{
					AssetEditorSys->CloseAllEditorsForAsset(StaleBP);
				}
			}
			StaleBP->MarkAsGarbage();
			if (UClass* GenClass = StaleBP->GeneratedClass)
			{
				GenClass->MarkAsGarbage();
			}
			StalePackage->MarkAsGarbage();
			CollectGarbage(GARBAGE_COLLECTION_KEEPFLAGS, true);
		}
		if (FindObject<UBlueprint>(nullptr, *FString::Printf(TEXT("%s.%s"), *PackagePath, *AssetName)))
		{
			if (UBlueprint* StillStaleBP = FindObject<UBlueprint>(nullptr, *FString::Printf(TEXT("%s.%s"), *PackagePath, *AssetName)))
			{
				const FString TempName = FString::Printf(TEXT("Stale_%s_%s"), *AssetName, *FGuid::NewGuid().ToString(EGuidFormats::Digits));
				StillStaleBP->Rename(*TempName, StalePackage, REN_DontCreateRedirectors | REN_ForceNoResetLoaders);
				StillStaleBP->MarkAsGarbage();
				CollectGarbage(GARBAGE_COLLECTION_KEEPFLAGS, true);
			}
			if (FindObject<UBlueprint>(nullptr, *FString::Printf(TEXT("%s.%s"), *PackagePath, *AssetName)))
			{
				Result->SetBoolField(TEXT("created"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("A stale blueprint '%s' exists in memory but not on disk. Clear the editor's transient data or restart the editor."), *PackagePath));
				return Result;
			}
		}
		Package = CreatePackage(*PackagePath);
	}
	else
	{
		Package = CreatePackage(*PackagePath);
	}

	// Create the interface blueprint. UE's UBlueprintInterfaceFactory
	// (EditorFactories.cpp:7040) uses exactly this call: ParentClass =
	// UInterface, BPType = BPTYPE_Interface, plain UBlueprint +
	// UBlueprintGeneratedClass.
	UBlueprint* Blueprint = FKismetEditorUtilities::CreateBlueprint(
		UInterface::StaticClass(), Package, FName(*AssetName), BPTYPE_Interface,
		UBlueprint::StaticClass(), UBlueprintGeneratedClass::StaticClass());
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Interface blueprint creation failed at '%s'"), *PackagePath));
		return Result;
	}

	FKismetEditorUtilities::CompileBlueprint(Blueprint);
	FAssetRegistryModule::AssetCreated(Blueprint);

	// Add each message as a function graph on the interface blueprint.
	// HandleCreateFunctionGraph creates the EdGraph + entry/result nodes +
	// parameter pins + compiles, which is exactly what an interface message
	// needs (interface functions have no body — just the signature).
	TArray<FString> AddedMessages;
	const TArray<TSharedPtr<FJsonValue>>* MessagesArray = nullptr;
	if (Payload->TryGetArrayField(TEXT("messages"), MessagesArray) && MessagesArray != nullptr)
	{
		const FString FullPath = FString::Printf(TEXT("%s.%s"), *PackagePath, *AssetName);
		for (const TSharedPtr<FJsonValue>& MessageVal : *MessagesArray)
		{
			const TSharedPtr<FJsonObject>* MessageObjPtr = nullptr;
			if (!MessageVal.IsValid() || !MessageVal->TryGetObject(MessageObjPtr) || !MessageObjPtr->IsValid())
			{
				continue;
			}
			TSharedPtr<FJsonObject> MessageObj = *MessageObjPtr;

			FString MessageName;
			if (!MessageObj->TryGetStringField(TEXT("name"), MessageName) || MessageName.IsEmpty())
			{
				continue;
			}

			// Build a payload for HandleCreateFunctionGraph: { path, name, params }.
			TSharedPtr<FJsonObject> FuncPayload = MakeShared<FJsonObject>();
			FuncPayload->SetStringField(TEXT("path"), FullPath);
			FuncPayload->SetStringField(TEXT("name"), MessageName);

			// Forward 'params' if present.
			const TArray<TSharedPtr<FJsonValue>>* ParamsArray = nullptr;
			if (MessageObj->TryGetArrayField(TEXT("params"), ParamsArray) && ParamsArray != nullptr)
			{
				FuncPayload->SetField(TEXT("params"), MakeShared<FJsonValueArray>(*ParamsArray));
			}

			TSharedPtr<FJsonObject> FuncResult = HandleCreateFunctionGraph(FuncPayload);
			if (FuncResult.IsValid() && FuncResult->GetBoolField(TEXT("created")))
			{
				AddedMessages.Add(MessageName);
			}
		}
	}

	Blueprint->MarkPackageDirty();

	Result->SetBoolField(TEXT("created"), true);
	Result->SetBoolField(TEXT("dirty"), Blueprint->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("path"), FString::Printf(TEXT("%s.%s"), *PackagePath, *AssetName));
	Result->SetNumberField(TEXT("messageCount"), AddedMessages.Num());
	TArray<TSharedPtr<FJsonValue>> MessageValues;
	for (const FString& AddedName : AddedMessages)
	{
		MessageValues.Add(MakeShared<FJsonValueString>(AddedName));
	}
	Result->SetField(TEXT("messages"), MakeShared<FJsonValueArray>(MessageValues));
	return Result;
}

// ---------------------------------------------------------------------------
// create_data_table
//
// Creates a DataTable asset (the "Data Table" the content browser creates via
// right-click -> Miscellaneous -> Data Table). A DataTable is a typed table:
// every row is one instance of the row struct, addressed by an FName row key.
// It is the asset the "Get Data Table Row" node reads, and it has no compile
// concept like a blueprint - save_asset is what persists it.
//
// Payload keys:
//   path        - project asset path, e.g. /Game/Tests/UE5_0/DT_Items
//   rowStruct   - project asset path of the row struct, e.g.
//                 /Game/Tests/UE5_0/FItemRow_A (created beforehand with
//                 create_blueprint_struct; "struct" is accepted as an alias)
//
// The table starts out with no rows and is registered with the asset registry
// and left dirty - the caller must save_asset to persist it.
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleCreateDataTable(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString StructRef;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| (!Payload->TryGetStringField(TEXT("rowStruct"), StructRef)
			&& !Payload->TryGetStringField(TEXT("struct"), StructRef)) || StructRef.IsEmpty())
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (project asset path, e.g. /Game/Blueprints/DT_Items) and rowStruct (project asset path of the row struct, e.g. /Game/Blueprints/FItemRow)"));
		return Result;
	}

	// Path normalisation + clobber check: single entry point shared by every
	// create_* asset command (see PrepareNewAssetLocation).
	FString PackagePath;
	FString AssetName;
	FString PathReason;
	if (!PrepareNewAssetLocation(AssetPath, TEXT("/Game/Blueprints/DT_Items"), PackagePath, AssetName, PathReason))
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), PathReason);
		return Result;
	}

	// Resolve the row struct before creating the package, so an unresolvable
	// reference never leaves an orphaned package behind.
	FString StructReason;
	UScriptStruct* RowStruct = ResolveDataTableRowStruct(StructRef, StructReason);
	if (RowStruct == nullptr)
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), StructReason);
		return Result;
	}

	UPackage* Package = CreatePackage(*PackagePath);
	UDataTable* DataTable = CreateDataTableAsset(Package, AssetName, RowStruct);
	if (DataTable == nullptr)
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("DataTable creation failed at '%s'"), *PackagePath));
		return Result;
	}

	FAssetRegistryModule::AssetCreated(DataTable);
	DataTable->MarkPackageDirty();

	Result->SetBoolField(TEXT("created"), true);
	Result->SetBoolField(TEXT("dirty"), DataTable->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("path"), FString::Printf(TEXT("%s.%s"), *PackagePath, *AssetName));
	Result->SetStringField(TEXT("rowStruct"), RowStruct->GetPathName());
	Result->SetNumberField(TEXT("rowCount"), DataTable->GetRowMap().Num());
	return Result;
}

// ---------------------------------------------------------------------------
// create_input_action
//
// Creates a UInputAction asset (Enhanced Input plugin) - the asset the
// UK2Node_EnhancedInputAction event node binds. The command name was already in
// AdvertisedCommands but had no handler, and the asset-IR path only classifies
// "inputAction" for ordering without a creation branch - so an Enhanced Input
// case could not get its prerequisite asset (requirement doc Question 1 Step 1 "Create
// InputAction asset", run 20261022_1955 / No. 70).
//
// Payload keys:
//   path      - project asset path, e.g. /Game/Tests/UE5_0/IA_Move
//   valueType - optional EInputActionValueType name: Boolean / Axis1D / Axis2D /
//               Axis3D. Default Axis2D (the movement action the doc describes).
//   trigger   - optional InputTrigger class short name, default "InputTriggerDown"
//               ("Down", which supports the triggered / completed events). Pass
//               "None" for a trigger-less action - but note UInputAction then
//               reports GetSupportedTriggerEvents() == None and every exec pin of
//               the event node is labelled "(Unsupported)", so a trigger is created
//               by default instead of leaving a pin-degenerate asset behind.
//
// The action class and the trigger class are resolved softly by object path
// (/Script/EnhancedInput.*) and their properties are written through reflection:
// ForgeMindForUE must not gain a compile-time dependency on an experimental
// plugin. With the plugin off the request is refused with a reason.
// The asset is left dirty - the caller persists it with save_asset.
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleCreateInputAction(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty())
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required field: path (project asset path, e.g. /Game/Tests/UE5_0/IA_Move)"));
		return Result;
	}

	// Path normalisation + clobber check: same shared entry as every other create_*
	// asset command (PrepareNewAssetLocation).
	FString PackagePath;
	FString AssetName;
	FString PathReason;
	if (!PrepareNewAssetLocation(AssetPath, TEXT("/Game/Tests/UE5_0/IA_Move"), PackagePath, AssetName, PathReason))
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), PathReason);
		return Result;
	}

	UClass* ActionClass = FindObject<UClass>(nullptr, TEXT("/Script/EnhancedInput.InputAction"));
	if (ActionClass == nullptr)
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), TEXT("InputAction class is not loaded - the EnhancedInput plugin is not enabled in this project (add \"Name\": \"EnhancedInput\", \"Enabled\": true to the .uproject)"));
		return Result;
	}

	FString ValueTypeName = TEXT("Axis2D");
	Payload->TryGetStringField(TEXT("valueType"), ValueTypeName);
	FString TriggerName = TEXT("InputTriggerDown");
	Payload->TryGetStringField(TEXT("trigger"), TriggerName);

	UClass* TriggerClass = nullptr;
	if (!TriggerName.IsEmpty() && TriggerName != TEXT("None"))
	{
		const FString TriggerObjectPath = FString::Printf(TEXT("/Script/EnhancedInput.%s"), *TriggerName);
		TriggerClass = FindObject<UClass>(nullptr, *TriggerObjectPath);
		if (TriggerClass == nullptr)
		{
			Result->SetBoolField(TEXT("created"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Unknown InputTrigger class '%s' - expected an /Script/EnhancedInput trigger name (InputTriggerDown, InputTriggerPressed, InputTriggerReleased, InputTriggerHold, InputTriggerTap, InputTriggerPulse, ...)"), *TriggerName));
			return Result;
		}
	}

	UPackage* Package = CreatePackage(*PackagePath);
	// RF_Standalone is what keeps an asset object alive next to its package (see
	// CreateDataTableAsset, which uses the same flag set): without it the freshly created
	// asset has no referencer once save_asset clears the dirty flag, the garbage collector
	// collects it, and the leftover in-memory package (no linker) then makes every later
	// lookup by path miss - the enhanced-input node could not load an asset that was
	// provably on disk with correct contents (runs 20261023_0001 / 1132).
	UObject* ActionAsset = NewObject<UObject>(Package, ActionClass, *AssetName, RF_Public | RF_Standalone | RF_Transactional);
	if (ActionAsset == nullptr)
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("UInputAction creation failed at '%s'"), *PackagePath));
		return Result;
	}

	// ValueType is an enum property stored in its underlying integer width, so write
	// through GetUnderlyingProperty() instead of assuming a byte.
	if (FEnumProperty* ValueTypeProp = CastField<FEnumProperty>(ActionClass->FindPropertyByName(TEXT("ValueType"))))
	{
		UEnum* ValueTypeEnum = ValueTypeProp->GetEnum();
		const int64 ValueTypeValue = ValueTypeEnum != nullptr ? ValueTypeEnum->GetValueByNameString(ValueTypeName) : INDEX_NONE;
		if (ValueTypeValue == INDEX_NONE)
		{
			Result->SetBoolField(TEXT("created"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Unknown value type '%s' - EInputActionValueType takes Boolean / Axis1D / Axis2D / Axis3D"), *ValueTypeName));
			return Result;
		}
		ValueTypeProp->GetUnderlyingProperty()->SetIntPropertyValue(
			ValueTypeProp->ContainerPtrToValuePtr<void>(ActionAsset), ValueTypeValue);
	}

	// Triggers is an array of object pointers: append through FScriptArrayHelper and
	// set the element through the inner object property - no plugin type named here.
	FString CreatedTriggerName;
	if (TriggerClass != nullptr)
	{
		if (FArrayProperty* TriggersProp = CastField<FArrayProperty>(ActionClass->FindPropertyByName(TEXT("Triggers"))))
		{
			// The helper takes the address of the array property value inside the container,
			// not the container itself.
			FScriptArrayHelper Helper(TriggersProp, TriggersProp->ContainerPtrToValuePtr<void>(ActionAsset));
			const int32 Index = Helper.AddValue();
			UObject* TriggerObject = NewObject<UObject>(ActionAsset, TriggerClass, *FString::Printf(TEXT("%s_%d"), *TriggerName, Index), RF_Transactional);
			if (FObjectProperty* Inner = CastField<FObjectProperty>(TriggersProp->Inner))
			{
				Inner->SetObjectPropertyValue(Helper.GetRawPtr(Index), TriggerObject);
				CreatedTriggerName = TriggerName;
			}
		}
	}

	FAssetRegistryModule::AssetCreated(ActionAsset);
	ActionAsset->MarkPackageDirty();

	Result->SetBoolField(TEXT("created"), true);
	Result->SetBoolField(TEXT("dirty"), ActionAsset->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("path"), FString::Printf(TEXT("%s.%s"), *PackagePath, *AssetName));
	Result->SetStringField(TEXT("valueType"), ValueTypeName);
	Result->SetStringField(TEXT("trigger"), CreatedTriggerName);
	return Result;
}

// ---------------------------------------------------------------------------
// create_input_mapping_context
//
// Creates a UInputMappingContext asset (Enhanced Input plugin) - the asset the
// "Add Mapping Context" call feeds the enhanced-input LocalPlayer subsystem with
// (requirement doc Tasks/UE_5.0/Blueprint_EnhancedInput.md Question 3 Step 1 "Create InputMappingContext asset
// IMC_Default"). Like create_input_action the command was advertised long before a
// handler existed, and the asset-IR path only reads IMC "mappings" as ordering
// dependencies without any creation branch.
//
// Payload keys:
//   path - project asset path, e.g. /Game/Tests/UE5_0/IMC_Default
//
// The context is created with no key mappings: adding a mapping writes an
// FEnhancedActionKeyMapping (soft action pointer + key) into the asset's Mappings
// array, which is a separate capability (the doc's add_input_mapping command) and
// nothing in the current requirement set needs a bound key. The asset is left
// dirty - the caller persists it with save_asset.
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleCreateInputMappingContext(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty())
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required field: path (project asset path, e.g. /Game/Tests/UE5_0/IMC_Default)"));
		return Result;
	}

	FString PackagePath;
	FString AssetName;
	FString PathReason;
	if (!PrepareNewAssetLocation(AssetPath, TEXT("/Game/Tests/UE5_0/IMC_Default"), PackagePath, AssetName, PathReason))
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), PathReason);
		return Result;
	}

	// Soft resolution by object path: no compile-time dependency on the experimental plugin.
	UClass* MappingContextClass = FindObject<UClass>(nullptr, TEXT("/Script/EnhancedInput.InputMappingContext"));
	if (MappingContextClass == nullptr)
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), TEXT("InputMappingContext class is not loaded - the EnhancedInput plugin is not enabled in this project (add \"Name\": \"EnhancedInput\", \"Enabled\": true to the .uproject)"));
		return Result;
	}

	UPackage* Package = CreatePackage(*PackagePath);
	// Same RF_Standalone requirement as the action above.
	UObject* MappingContextAsset = NewObject<UObject>(Package, MappingContextClass, *AssetName, RF_Public | RF_Standalone | RF_Transactional);
	if (MappingContextAsset == nullptr)
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("UInputMappingContext creation failed at '%s'"), *PackagePath));
		return Result;
	}

	FAssetRegistryModule::AssetCreated(MappingContextAsset);
	MappingContextAsset->MarkPackageDirty();

	Result->SetBoolField(TEXT("created"), true);
	Result->SetBoolField(TEXT("dirty"), MappingContextAsset->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("path"), FString::Printf(TEXT("%s.%s"), *PackagePath, *AssetName));
	return Result;
}

// ---------------------------------------------------------------------------
// Shared helpers for struct/enum member commands
// ---------------------------------------------------------------------------

namespace
{
	/** Strips the trailing .AssetName from a path like /Game/Foo/BP_Thing.BP_Thing */
	FString NormalizePackagePath(const FString& AssetPath)
	{
		FString PackagePath = AssetPath;
		int32 DotIndex = INDEX_NONE;
		if (PackagePath.FindChar(TEXT('.'), DotIndex))
		{
			PackagePath = PackagePath.Left(DotIndex);
		}
		return PackagePath;
	}

	/** Loads a UUserDefinedStruct from a project asset path. */
	UUserDefinedStruct* LoadUserDefinedStruct(const FString& AssetPath)
	{
		const FString PackagePath = NormalizePackagePath(AssetPath);
		const FString ObjectPath = FString::Printf(TEXT("%s.%s"), *PackagePath, *FPaths::GetCleanFilename(PackagePath));
		return LoadObject<UUserDefinedStruct>(nullptr, *ObjectPath);
	}

	/** Loads a UUserDefinedEnum from a project asset path. */
	UUserDefinedEnum* LoadUserDefinedEnum(const FString& AssetPath)
	{
		const FString PackagePath = NormalizePackagePath(AssetPath);
		const FString ObjectPath = FString::Printf(TEXT("%s.%s"), *PackagePath, *FPaths::GetCleanFilename(PackagePath));
		return LoadObject<UUserDefinedEnum>(nullptr, *ObjectPath);
	}

	/** Finds a struct member Guid by either friendly name or internal VarName. */
	bool FindStructMemberGuidLoose(UUserDefinedStruct* Struct, const FString& MemberName, FGuid& OutGuid, FString& OutMatchedName)
	{
		const TArray<FStructVariableDescription>& VarDescs = FStructureEditorUtils::GetVarDesc(Struct);
		// First try friendly name (the display name the user sees)
		for (const FStructVariableDescription& Desc : VarDescs)
		{
			if (Desc.FriendlyName == MemberName)
			{
				OutGuid = Desc.VarGuid;
				OutMatchedName = Desc.FriendlyName;
				return true;
			}
		}
		// Fall back to the internal VarName (e.g. Member_1)
		for (const FStructVariableDescription& Desc : VarDescs)
		{
			if (Desc.VarName.ToString() == MemberName)
			{
				OutGuid = Desc.VarGuid;
				OutMatchedName = Desc.FriendlyName;
				return true;
			}
		}
		return false;
	}

	/**
	 * Finds an enum enumerator index by display name. User-defined enums
	 * store display names as FText; we compare the source string.
	 * Returns false if the name does not match any enumerator.
	 */
	bool FindEnumIndex(UUserDefinedEnum* Enum, const FString& DisplayName, int32& OutIndex)
	{
		const int32 MaxIndex = Enum->NumEnums() - 1; // last is _MAX
		for (int32 i = 0; i < MaxIndex; ++i)
		{
			const FText& DisplayText = Enum->GetDisplayNameTextByIndex(i);
			if (DisplayText.ToString() == DisplayName)
			{
				OutIndex = i;
				return true;
			}
		}
		return false;
	}

	/** Collects all enumerator display names for reporting. */
	TArray<FString> GetEnumDisplayNames(UUserDefinedEnum* Enum)
	{
		TArray<FString> Names;
		const int32 MaxIndex = Enum->NumEnums() - 1;
		for (int32 i = 0; i < MaxIndex; ++i)
		{
			Names.Add(Enum->GetDisplayNameTextByIndex(i).ToString());
		}
		return Names;
	}

	/** Collects all struct member friendly names for reporting. */
	TArray<FString> GetStructMemberNames(UUserDefinedStruct* Struct)
	{
		TArray<FString> Names;
		const TArray<FStructVariableDescription>& VarDescs = FStructureEditorUtils::GetVarDesc(Struct);
		for (const FStructVariableDescription& Desc : VarDescs)
		{
			Names.Add(Desc.FriendlyName);
		}
		return Names;
	}
}

// ---------------------------------------------------------------------------
// add_struct_member
//
// Adds a new member to an existing User-Defined Struct. The member is
// described by {name, type} — the same type vocabulary as
// add_blueprint_variable and create_blueprint_struct.
//
// Payload keys:
//   path        — project asset path of the struct, e.g. /Game/Blueprints/FMyStruct
//   name        — friendly name for the new member
//   type        — pin type text (e.g. "float", "FString", "int32[]")
//
// The struct is recompiled and left dirty — save_asset to persist.
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleAddStructMember(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath, MemberName, MemberType;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("name"), MemberName) || MemberName.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("type"), MemberType) || MemberType.IsEmpty())
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (struct asset path), name (member friendly name), type (pin type text)"));
		return Result;
	}

	UUserDefinedStruct* Struct = LoadUserDefinedStruct(AssetPath);
	if (Struct == nullptr)
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No user-defined struct found at '%s'"), *AssetPath));
		return Result;
	}

	// Parse the type before mutating the struct so a bad type does not
	// leave a half-added member behind.
	FEdGraphPinType PinType;
	FString TypeReason;
	if (!PinTypeTextToPinType(MemberType, PinType, TypeReason))
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Invalid type '%s': %s"), *MemberType, *TypeReason));
		return Result;
	}

	// Refuse duplicate friendly names — the editor itself enforces uniqueness.
	if (!FStructureEditorUtils::IsUniqueVariableFriendlyName(Struct, MemberName))
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("A member named '%s' already exists in the struct"), *MemberName));
		return Result;
	}

	if (!FStructureEditorUtils::AddVariable(Struct, PinType))
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), TEXT("FStructureEditorUtils::AddVariable returned false"));
		return Result;
	}

	// The newly added variable is the last entry — grab its Guid and
	// rename it to the requested friendly name.
	TArray<FStructVariableDescription>& VarDescs = FStructureEditorUtils::GetVarDesc(Struct);
	if (VarDescs.Num() > 0)
	{
		const FGuid NewVarGuid = VarDescs.Last().VarGuid;
		FStructureEditorUtils::RenameVariable(Struct, NewVarGuid, MemberName);
	}

	FStructureEditorUtils::OnStructureChanged(Struct, FStructureEditorUtils::EStructureEditorChangeInfo::AddedVariable);
	FStructureEditorUtils::CompileStructure(Struct);
	Struct->MarkPackageDirty();

	Result->SetBoolField(TEXT("added"), true);
	Result->SetBoolField(TEXT("dirty"), Struct->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("path"), AssetPath);
	Result->SetStringField(TEXT("name"), MemberName);
	Result->SetStringField(TEXT("type"), PinTypeToText(PinType));
	Result->SetNumberField(TEXT("memberCount"), FStructureEditorUtils::GetVarDesc(Struct).Num());
	{
		TArray<FString> Warnings;
		CheckForDefaultMemberVars(Struct, Warnings);
		AppendWarningsToResult(Result, Warnings);
	}
	return Result;
}

// ---------------------------------------------------------------------------
// update_struct_member
//
// Updates an existing struct member: renames it and/or changes its type.
// Either 'newName' or 'newType' (or both) must be provided. The member is
// identified by 'name' (its current friendly name or internal VarName).
//
// Payload keys:
//   path        — project asset path of the struct
//   name        — current friendly name (or internal VarName) of the member to update
//   newName     — (optional) new friendly name
//   newType     — (optional) new pin type text
//
// The struct is recompiled and left dirty — save_asset to persist.
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleUpdateStructMember(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath, MemberName;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("name"), MemberName) || MemberName.IsEmpty())
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (struct asset path), name (current member name); optional: newName, newType"));
		return Result;
	}

	FString NewName, NewType;
	bool bHasNewName = Payload->TryGetStringField(TEXT("newName"), NewName) && !NewName.IsEmpty();
	bool bHasNewType = Payload->TryGetStringField(TEXT("newType"), NewType) && !NewType.IsEmpty();
	if (!bHasNewName && !bHasNewType)
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), TEXT("At least one of 'newName' or 'newType' must be provided"));
		return Result;
	}

	UUserDefinedStruct* Struct = LoadUserDefinedStruct(AssetPath);
	if (Struct == nullptr)
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No user-defined struct found at '%s'"), *AssetPath));
		return Result;
	}

	FGuid MemberGuid;
	FString MatchedName;
	if (!FindStructMemberGuidLoose(Struct, MemberName, MemberGuid, MatchedName))
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No member named '%s' found in struct (existing: %s)"), *MemberName, *FString::Join(GetStructMemberNames(Struct), TEXT(", "))));
		return Result;
	}

	TArray<FString> Changes;

	// Rename if requested
	if (bHasNewName && NewName != MatchedName)
	{
		if (!FStructureEditorUtils::IsUniqueVariableFriendlyName(Struct, NewName))
		{
			Result->SetBoolField(TEXT("updated"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("A member named '%s' already exists in the struct"), *NewName));
			return Result;
		}
		if (!FStructureEditorUtils::RenameVariable(Struct, MemberGuid, NewName))
		{
			Result->SetBoolField(TEXT("updated"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Renaming member '%s' to '%s' failed"), *MatchedName, *NewName));
			return Result;
		}
		Changes.Add(FString::Printf(TEXT("renamed '%s' -> '%s'"), *MatchedName, *NewName));
	}

	// Change type if requested
	if (bHasNewType)
	{
		FEdGraphPinType NewPinType;
		FString TypeReason;
		if (!PinTypeTextToPinType(NewType, NewPinType, TypeReason))
		{
			Result->SetBoolField(TEXT("updated"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Invalid newType '%s': %s"), *NewType, *TypeReason));
			return Result;
		}
		if (!FStructureEditorUtils::ChangeVariableType(Struct, MemberGuid, NewPinType))
		{
			Result->SetBoolField(TEXT("updated"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Changing type of member '%s' to '%s' failed"), *MatchedName, *NewType));
			return Result;
		}
		Changes.Add(FString::Printf(TEXT("type -> '%s'"), *NewType));
	}

	FStructureEditorUtils::OnStructureChanged(Struct, FStructureEditorUtils::EStructureEditorChangeInfo::VariableTypeChanged);
	FStructureEditorUtils::CompileStructure(Struct);
	Struct->MarkPackageDirty();

	Result->SetBoolField(TEXT("updated"), true);
	Result->SetBoolField(TEXT("dirty"), Struct->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("path"), AssetPath);
	Result->SetStringField(TEXT("name"), bHasNewName ? NewName : MatchedName);
	TArray<TSharedPtr<FJsonValue>> ChangeValues;
	for (const FString& Change : Changes)
	{
		ChangeValues.Add(MakeShared<FJsonValueString>(Change));
	}
	Result->SetField(TEXT("changes"), MakeShared<FJsonValueArray>(ChangeValues));
	{
		TArray<FString> Warnings;
		CheckForDefaultMemberVars(Struct, Warnings);
		AppendWarningsToResult(Result, Warnings);
	}
	return Result;
}

// ---------------------------------------------------------------------------
// remove_struct_member
//
// Removes a member from an existing User-Defined Struct. The member is
// identified by 'name' (its friendly name or internal VarName).
//
// Payload keys:
//   path        — project asset path of the struct
//   name        — friendly name (or internal VarName) of the member to remove
//
// The struct is recompiled and left dirty — save_asset to persist.
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleRemoveStructMember(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath, MemberName;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("name"), MemberName) || MemberName.IsEmpty())
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (struct asset path), name (member friendly name to remove)"));
		return Result;
	}

	UUserDefinedStruct* Struct = LoadUserDefinedStruct(AssetPath);
	if (Struct == nullptr)
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No user-defined struct found at '%s'"), *AssetPath));
		return Result;
	}

	FGuid MemberGuid;
	FString MatchedName;
	if (!FindStructMemberGuidLoose(Struct, MemberName, MemberGuid, MatchedName))
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No member named '%s' found in struct (existing: %s)"), *MemberName, *FString::Join(GetStructMemberNames(Struct), TEXT(", "))));
		return Result;
	}

	if (!FStructureEditorUtils::RemoveVariable(Struct, MemberGuid))
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Removing member '%s' failed"), *MatchedName));
		return Result;
	}

	FStructureEditorUtils::OnStructureChanged(Struct, FStructureEditorUtils::EStructureEditorChangeInfo::RemovedVariable);
	FStructureEditorUtils::CompileStructure(Struct);
	Struct->MarkPackageDirty();

	Result->SetBoolField(TEXT("removed"), true);
	Result->SetBoolField(TEXT("dirty"), Struct->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("path"), AssetPath);
	Result->SetStringField(TEXT("name"), MatchedName);
	Result->SetNumberField(TEXT("memberCount"), FStructureEditorUtils::GetVarDesc(Struct).Num());
	{
		TArray<FString> Warnings;
		CheckForDefaultMemberVars(Struct, Warnings);
		AppendWarningsToResult(Result, Warnings);
	}
	return Result;
}

// ---------------------------------------------------------------------------
// add_enum_entry
//
// Adds a new enumerator (display name) to an existing User-Defined Enum.
//
// Payload keys:
//   path        — project asset path of the enum, e.g. /Game/Blueprints/EMyEnum
//   name        — display name for the new enumerator
//   value       — explicit numeric value for the new enumerator; omit for
//                 the auto-incremented index. Bitmask entries need explicit
//                 powers of two (1/2/4): the auto-added entry always takes
//                 the next sequential value (0,1,2...), so an OR-combination
//                 like "A|C" would collide with a different entry.
//
// The enum is left dirty — save_asset to persist.
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleAddEnumEntry(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath, EntryName;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("name"), EntryName) || EntryName.IsEmpty())
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (enum asset path), name (enumerator display name)"));
		return Result;
	}

	UUserDefinedEnum* Enum = LoadUserDefinedEnum(AssetPath);
	if (Enum == nullptr)
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No user-defined enum found at '%s'"), *AssetPath));
		return Result;
	}

	// Refuse duplicate display names
	int32 DummyIndex;
	if (FindEnumIndex(Enum, EntryName, DummyIndex))
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("An enumerator named '%s' already exists in the enum"), *EntryName));
		return Result;
	}

	// AddNewEnumeratorForUserDefinedEnum re-sequences EVERY entry's value to its
	// index (0,1,2...) as it appends, so a bitmask enum built one entry at a time
	// (A=1,B=2,C=4) would end up A=0,B=1,C=4 - every earlier explicit value wiped
	// by the next add. Capture the current values first, add the entry, then rebuild
	// the whole list through SetEnums restoring each prior value and giving the new
	// entry its explicit value. Raw entry names are reused unchanged, so the
	// DisplayNameMap (keyed by raw name) survives.
	TArray<int64> PriorValues;
	const int32 PriorCount = Enum->NumEnums() - 1; // exclude the trailing _MAX
	PriorValues.Reserve(PriorCount);
	for (int32 i = 0; i < PriorCount; ++i)
	{
		PriorValues.Add(Enum->GetValueByIndex(i));
	}

	FEnumEditorUtils::AddNewEnumeratorForUserDefinedEnum(Enum);
	const int32 NewIndex = Enum->NumEnums() - 2;

	double ExplicitValueDouble = 0;
	const bool bHasExplicitValue = Payload->TryGetNumberField(TEXT("value"), ExplicitValueDouble);
	const int64 NewValue = bHasExplicitValue
		? static_cast<int64>(ExplicitValueDouble)
		: (PriorCount > 0 ? PriorValues.Last() + 1 : 0);

	TArray<TPair<FName, int64>> NewNames;
	const int32 NonMaxCount = Enum->NumEnums() - 1;
	NewNames.Reserve(NonMaxCount);
	for (int32 i = 0; i < NonMaxCount; ++i)
	{
		const int64 Value = (i < PriorCount) ? PriorValues[i] : NewValue;
		NewNames.Emplace(Enum->GetNameByIndex(i), Value);
	}
	if (!Enum->SetEnums(NewNames, Enum->GetCppForm()))
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("failed to add enumerator '%s' (the enum rejected the name/value list)"), *EntryName));
		return Result;
	}
	FEnumEditorUtils::EnsureAllDisplayNamesExist(Enum);
	FEnumEditorUtils::SetEnumeratorDisplayName(Enum, NewIndex, FText::FromString(EntryName));

	Enum->MarkPackageDirty();

	Result->SetBoolField(TEXT("added"), true);
	Result->SetBoolField(TEXT("dirty"), Enum->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("path"), AssetPath);
	Result->SetStringField(TEXT("name"), EntryName);
	Result->SetNumberField(TEXT("index"), NewIndex);
	Result->SetNumberField(TEXT("entryCount"), Enum->NumEnums() - 1);
	return Result;
}

// ---------------------------------------------------------------------------
// update_enum_entry
//
// Renames an existing enumerator in a User-Defined Enum. The enumerator is
// identified by 'name' (its current display name).
//
// Payload keys:
//   path        — project asset path of the enum
//   name        — current display name of the enumerator to rename
//   newName     — new display name
//
// The enum is left dirty — save_asset to persist.
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleUpdateEnumEntry(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath, EntryName, NewName;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("name"), EntryName) || EntryName.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("newName"), NewName) || NewName.IsEmpty())
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (enum asset path), name (current enumerator display name), newName (new display name)"));
		return Result;
	}

	UUserDefinedEnum* Enum = LoadUserDefinedEnum(AssetPath);
	if (Enum == nullptr)
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No user-defined enum found at '%s'"), *AssetPath));
		return Result;
	}

	int32 EntryIndex;
	if (!FindEnumIndex(Enum, EntryName, EntryIndex))
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No enumerator named '%s' found in enum (existing: %s)"), *EntryName, *FString::Join(GetEnumDisplayNames(Enum), TEXT(", "))));
		return Result;
	}

	// Refuse duplicate if the new name differs and already exists
	int32 DummyIndex;
	if (NewName != EntryName && FindEnumIndex(Enum, NewName, DummyIndex))
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("An enumerator named '%s' already exists in the enum"), *NewName));
		return Result;
	}

	if (!FEnumEditorUtils::SetEnumeratorDisplayName(Enum, EntryIndex, FText::FromString(NewName)))
	{
		Result->SetBoolField(TEXT("updated"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Renaming enumerator '%s' to '%s' failed"), *EntryName, *NewName));
		return Result;
	}

	Enum->MarkPackageDirty();

	Result->SetBoolField(TEXT("updated"), true);
	Result->SetBoolField(TEXT("dirty"), Enum->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("path"), AssetPath);
	Result->SetStringField(TEXT("name"), NewName);
	Result->SetNumberField(TEXT("index"), EntryIndex);
	return Result;
}

// ---------------------------------------------------------------------------
// remove_enum_entry
//
// Removes an enumerator from an existing User-Defined Enum. The enumerator is
// identified by 'name' (its display name).
//
// Payload keys:
//   path        — project asset path of the enum
//   name        — display name of the enumerator to remove
//
// The enum is left dirty — save_asset to persist.
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleRemoveEnumEntry(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath, EntryName;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("name"), EntryName) || EntryName.IsEmpty())
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (enum asset path), name (enumerator display name to remove)"));
		return Result;
	}

	UUserDefinedEnum* Enum = LoadUserDefinedEnum(AssetPath);
	if (Enum == nullptr)
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No user-defined enum found at '%s'"), *AssetPath));
		return Result;
	}

	int32 EntryIndex;
	if (!FindEnumIndex(Enum, EntryName, EntryIndex))
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No enumerator named '%s' found in enum (existing: %s)"), *EntryName, *FString::Join(GetEnumDisplayNames(Enum), TEXT(", "))));
		return Result;
	}

	FEnumEditorUtils::RemoveEnumeratorFromUserDefinedEnum(Enum, EntryIndex);

	Enum->MarkPackageDirty();

	Result->SetBoolField(TEXT("removed"), true);
	Result->SetBoolField(TEXT("dirty"), Enum->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("path"), AssetPath);
	Result->SetStringField(TEXT("name"), EntryName);
	Result->SetNumberField(TEXT("entryCount"), Enum->NumEnums() - 1);
	return Result;
}

// ---------------------------------------------------------------------------
// scan_assets (L0 — overview)
//
// Forces a synchronous disk scan via SearchAllAssets so the asset registry
// cache is complete even for folders the content browser never opened.
// Returns a directory-level summary: path tree with per-path asset counts
// and a per-class breakdown. Does NOT return individual asset entries —
// use list_assets (L1) to drill into a specific path.
//
// Payload keys:
//   path           — (optional) root path to scan, default /Game/
//   forceRescan    — (optional) force re-scan even if already scanned, default true
//
// Bridge frame limit: 16 MB. The summary is bounded by the number of unique
// directory paths (typically < 1000), so it fits comfortably.
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleScanAssets(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString RootPath = TEXT("/Game/");
	if (Payload.IsValid())
	{
		Payload->TryGetStringField(TEXT("path"), RootPath);
	}
	if (RootPath.IsEmpty())
	{
		RootPath = TEXT("/Game/");
	}
	if (!RootPath.EndsWith(TEXT("/")))
	{
		RootPath /= TEXT("");
	}

	bool bForceRescan = true;
	if (Payload.IsValid())
	{
		Payload->TryGetBoolField(TEXT("forceRescan"), bForceRescan);
	}

	IAssetRegistry& AssetRegistry = FModuleManager::LoadModuleChecked<FAssetRegistryModule>(TEXT("AssetRegistry")).Get();

	if (bForceRescan || !AssetRegistry.IsSearchAllAssets())
	{
		AssetRegistry.SearchAllAssets(/*bSynchronousSearch=*/true);
		AssetRegistry.WaitForCompletion();
	}

	TArray<FAssetData> AllAssets;
	AssetRegistry.GetAllAssets(AllAssets, /*bIncludeOnlyOnDiskAssets=*/true);

	TMap<FName, int32> PathCounts;
	TMap<FString, int32> ClassCounts;
	int32 MultiAssetPackageCount = 0;
	TSet<FName> SeenPackages;

	for (const FAssetData& Asset : AllAssets)
	{
		const FString PackageNameStr = Asset.PackageName.ToString();
		if (!PackageNameStr.StartsWith(RootPath))
		{
			continue;
		}

		const FName PackagePath = Asset.PackagePath;
		int32* Count = PathCounts.Find(PackagePath);
		if (Count)
		{
			++(*Count);
		}
		else
		{
			PathCounts.Add(PackagePath, 1);
		}

		const FString ClassStr = Asset.AssetClass.ToString();
		int32* ClassCount = ClassCounts.Find(ClassStr);
		if (ClassCount)
		{
			++(*ClassCount);
		}
		else
		{
			ClassCounts.Add(ClassStr, 1);
		}

		if (SeenPackages.Contains(Asset.PackageName))
		{
			++MultiAssetPackageCount;
		}
		else
		{
			SeenPackages.Add(Asset.PackageName);
		}
	}

	TArray<FName> SortedPaths;
	PathCounts.GetKeys(SortedPaths);
	SortedPaths.Sort([](const FName& A, const FName& B) { return A.ToString() < B.ToString(); });

	TArray<TSharedPtr<FJsonValue>> PathEntries;
	for (const FName& Path : SortedPaths)
	{
		TSharedRef<FJsonObject> PathEntry = MakeShared<FJsonObject>();
		PathEntry->SetStringField(TEXT("path"), Path.ToString());
		PathEntry->SetNumberField(TEXT("assetCount"), PathCounts[Path]);
		PathEntries.Add(MakeShared<FJsonValueObject>(PathEntry));
	}

	TArray<TPair<FString, int32>> SortedClasses;
	for (const auto& Pair : ClassCounts)
	{
		SortedClasses.Add(Pair);
	}
	SortedClasses.Sort([](const TPair<FString, int32>& A, const TPair<FString, int32>& B) { return A.Value > B.Value; });

	TArray<TSharedPtr<FJsonValue>> ClassEntries;
	for (const auto& Pair : SortedClasses)
	{
		TSharedRef<FJsonObject> ClassEntry = MakeShared<FJsonObject>();
		ClassEntry->SetStringField(TEXT("class"), Pair.Key);
		ClassEntry->SetNumberField(TEXT("count"), Pair.Value);
		ClassEntries.Add(MakeShared<FJsonValueObject>(ClassEntry));
	}

	Result->SetBoolField(TEXT("scanned"), true);
	Result->SetStringField(TEXT("rootPath"), RootPath);
	Result->SetNumberField(TEXT("totalAssets"), AllAssets.Num());
	Result->SetNumberField(TEXT("totalPackages"), SeenPackages.Num());
	Result->SetNumberField(TEXT("multiAssetPackageCount"), MultiAssetPackageCount);
	Result->SetField(TEXT("paths"), MakeShared<FJsonValueArray>(PathEntries));
	Result->SetField(TEXT("classBreakdown"), MakeShared<FJsonValueArray>(ClassEntries));
	return Result;
}

// ---------------------------------------------------------------------------
// list_assets (L1 — paged list)
//
// Lists assets under a specific path with pagination. Each entry returns
// only the essential identity fields (objectPath, name, class, package).
// Use get_asset_info (L2) for full metadata of a single asset.
//
// Payload keys:
//   path           — folder path to list, e.g. /Game/Blueprints (required)
//   recursive      — (optional) search sub-paths, default false
//   classFilter    — (optional) array of class names to include
//   page           — (optional) zero-based page index, default 0
//   pageSize       — (optional) entries per page, default 50, max 200
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleListAssets(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString Path;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), Path)
			&& !Payload->TryGetStringField(TEXT("assetPath"), Path)) || Path.IsEmpty())
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (folder path, e.g. /Game/Blueprints); optional: recursive, classFilter, page, pageSize"));
		return Result;
	}

	bool bRecursive = false;
	if (Payload->HasField(TEXT("recursive")))
	{
		Payload->TryGetBoolField(TEXT("recursive"), bRecursive);
	}

	int32 Page = 0;
	if (Payload->HasField(TEXT("page")))
	{
		double PageVal = 0;
		if (Payload->TryGetNumberField(TEXT("page"), PageVal))
		{
			Page = FMath::Max(0, static_cast<int32>(PageVal));
		}
	}

	int32 PageSize = 50;
	if (Payload->HasField(TEXT("pageSize")))
	{
		double PageSizeVal = 0;
		if (Payload->TryGetNumberField(TEXT("pageSize"), PageSizeVal))
		{
			PageSize = FMath::Clamp(static_cast<int32>(PageSizeVal), 1, 200);
		}
	}

	TSet<FString> ClassFilter;
	const TArray<TSharedPtr<FJsonValue>>* ClassArray = nullptr;
	if (Payload->TryGetArrayField(TEXT("classFilter"), ClassArray) && ClassArray != nullptr)
	{
		for (const TSharedPtr<FJsonValue>& Entry : *ClassArray)
		{
			if (Entry.IsValid() && Entry->Type == EJson::String)
			{
				ClassFilter.Add(Entry->AsString());
			}
		}
	}

	IAssetRegistry& AssetRegistry = FModuleManager::LoadModuleChecked<FAssetRegistryModule>(TEXT("AssetRegistry")).Get();

	FARFilter Filter;
	Filter.bRecursivePaths = bRecursive;
	Filter.PackagePaths.Add(FName(*Path));

	for (const FString& ClassName : ClassFilter)
	{
		Filter.ClassNames.Add(FName(*ClassName));
	}

	TArray<FAssetData> FoundAssets;
	AssetRegistry.GetAssets(Filter, FoundAssets);

	FoundAssets.Sort([](const FAssetData& A, const FAssetData& B)
	{
		return A.ObjectPath.ToString() < B.ObjectPath.ToString();
	});

	const int32 TotalAssets = FoundAssets.Num();
	const int32 TotalPages = FMath::CeilToInt(static_cast<float>(TotalAssets) / PageSize);
	const int32 StartIndex = Page * PageSize;
	const int32 EndIndex = FMath::Min(StartIndex + PageSize, TotalAssets);

	TArray<TSharedPtr<FJsonValue>> AssetEntries;
	TMap<FName, TArray<FString>> PackageAssets;

	for (int32 i = StartIndex; i < EndIndex; ++i)
	{
		const FAssetData& Asset = FoundAssets[i];
		TSharedRef<FJsonObject> AssetEntry = MakeShared<FJsonObject>();
		AssetEntry->SetStringField(TEXT("objectPath"), Asset.ObjectPath.ToString());
		AssetEntry->SetStringField(TEXT("name"), Asset.AssetName.ToString());
		AssetEntry->SetStringField(TEXT("class"), Asset.AssetClass.ToString());
		AssetEntry->SetStringField(TEXT("package"), Asset.PackageName.ToString());
		AssetEntries.Add(MakeShared<FJsonValueObject>(AssetEntry));

		TArray<FString>* PkgAssets = PackageAssets.Find(Asset.PackageName);
		if (PkgAssets)
		{
			PkgAssets->Add(Asset.AssetName.ToString());
		}
		else
		{
			TArray<FString> NewList = { Asset.AssetName.ToString() };
			PackageAssets.Add(Asset.PackageName, NewList);
		}
	}

	TArray<TSharedPtr<FJsonValue>> MultiAssetEntries;
	for (const auto& Pair : PackageAssets)
	{
		if (Pair.Value.Num() > 1)
		{
			TSharedRef<FJsonObject> MultiEntry = MakeShared<FJsonObject>();
			MultiEntry->SetStringField(TEXT("package"), Pair.Key.ToString());
			TArray<TSharedPtr<FJsonValue>> AssetNames;
			for (const FString& Name : Pair.Value)
			{
				AssetNames.Add(MakeShared<FJsonValueString>(Name));
			}
			MultiEntry->SetField(TEXT("assets"), MakeShared<FJsonValueArray>(AssetNames));
			MultiAssetEntries.Add(MakeShared<FJsonValueObject>(MultiEntry));
		}
	}

	Result->SetBoolField(TEXT("found"), true);
	Result->SetStringField(TEXT("path"), Path);
	Result->SetBoolField(TEXT("recursive"), bRecursive);
	Result->SetNumberField(TEXT("totalAssets"), TotalAssets);
	Result->SetNumberField(TEXT("page"), Page);
	Result->SetNumberField(TEXT("pageSize"), PageSize);
	Result->SetNumberField(TEXT("totalPages"), TotalPages);
	Result->SetField(TEXT("assets"), MakeShared<FJsonValueArray>(AssetEntries));
	if (MultiAssetEntries.Num() > 0)
	{
		Result->SetField(TEXT("multiAssetPackages"), MakeShared<FJsonValueArray>(MultiAssetEntries));
	}
	return Result;
}

// ---------------------------------------------------------------------------
// get_asset_info (L2 — single asset detail)
//
// Returns full metadata for a single asset: class, tags, package-level
// co-assets (other exports in the same .uasset), and package dependencies.
// Uses GetAssetByObjectPath with bIncludeOnlyOnDiskAssets=true so the asset
// is NOT loaded into memory — all data comes from the registry cache.
//
// Payload keys:
//   path           — asset path, e.g. /Game/Blueprints/BP_Thing or
//                    /Game/Blueprints/BP_Thing.BP_Thing (required)
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleGetAssetInfo(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty())
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (asset path, e.g. /Game/Blueprints/BP_Thing)"));
		return Result;
	}

	FString PackagePath = AssetPath;
	int32 DotIndex = INDEX_NONE;
	if (PackagePath.FindChar(TEXT('.'), DotIndex))
	{
		PackagePath = PackagePath.Left(DotIndex);
	}
	const FString AssetName = FPaths::GetCleanFilename(PackagePath);
	const FString ObjectPath = FString::Printf(TEXT("%s.%s"), *PackagePath, *AssetName);

	IAssetRegistry& AssetRegistry = FModuleManager::LoadModuleChecked<FAssetRegistryModule>(TEXT("AssetRegistry")).Get();

	FAssetData AssetData = AssetRegistry.GetAssetByObjectPath(FName(*ObjectPath), /*bIncludeOnlyOnDiskAssets=*/true);
	if (!AssetData.IsValid())
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No asset found at '%s'"), *ObjectPath));
		return Result;
	}

	Result->SetBoolField(TEXT("found"), true);
	Result->SetStringField(TEXT("objectPath"), AssetData.ObjectPath.ToString());
	Result->SetStringField(TEXT("packageName"), AssetData.PackageName.ToString());
	Result->SetStringField(TEXT("packagePath"), AssetData.PackagePath.ToString());
	Result->SetStringField(TEXT("assetName"), AssetData.AssetName.ToString());
	Result->SetStringField(TEXT("class"), AssetData.AssetClass.ToString());
	Result->SetBoolField(TEXT("isUAsset"), AssetData.IsUAsset());
	Result->SetBoolField(TEXT("isTopLevelAsset"), AssetData.IsTopLevelAsset());

	const FAssetDataTagMapSharedView& Tags = AssetData.TagsAndValues;
	if (Tags.Num() > 0)
	{
		TSharedRef<FJsonObject> TagsObj = MakeShared<FJsonObject>();
		for (auto It = Tags.CreateConstIterator(); It; ++It)
		{
			const FName Key = It.Key();
			const FAssetTagValueRef Value = It.Value();
			if (Value.IsSet())
			{
				TagsObj->SetStringField(Key.ToString(), Value.AsString());
			}
		}
		Result->SetObjectField(TEXT("tags"), TagsObj);
	}

	TArray<FAssetData> PackageAssets;
	AssetRegistry.GetAssetsByPackageName(AssetData.PackageName, PackageAssets, /*bIncludeOnlyOnDiskAssets=*/true);
	if (PackageAssets.Num() > 1)
	{
		TArray<TSharedPtr<FJsonValue>> CoAssetEntries;
		for (const FAssetData& PkgAsset : PackageAssets)
		{
			if (PkgAsset.AssetName == AssetData.AssetName && PkgAsset.PackageName == AssetData.PackageName)
			{
				continue;
			}
			TSharedRef<FJsonObject> CoAsset = MakeShared<FJsonObject>();
			CoAsset->SetStringField(TEXT("objectPath"), PkgAsset.ObjectPath.ToString());
			CoAsset->SetStringField(TEXT("name"), PkgAsset.AssetName.ToString());
			CoAsset->SetStringField(TEXT("class"), PkgAsset.AssetClass.ToString());
			CoAssetEntries.Add(MakeShared<FJsonValueObject>(CoAsset));
		}
		if (CoAssetEntries.Num() > 0)
		{
			Result->SetField(TEXT("packageAssets"), MakeShared<FJsonValueArray>(CoAssetEntries));
		}
	}

	TArray<FName> Dependencies;
	AssetRegistry.GetDependencies(AssetData.PackageName, Dependencies,
		UE::AssetRegistry::EDependencyCategory::Package,
		UE::AssetRegistry::FDependencyQuery(UE::AssetRegistry::EDependencyQuery::Hard));
	if (Dependencies.Num() > 0)
	{
		TArray<TSharedPtr<FJsonValue>> DepEntries;
		for (const FName& Dep : Dependencies)
		{
			DepEntries.Add(MakeShared<FJsonValueString>(Dep.ToString()));
		}
		Result->SetField(TEXT("dependencies"), MakeShared<FJsonValueArray>(DepEntries));
	}

	TArray<FName> Referencers;
	AssetRegistry.GetReferencers(AssetData.PackageName, Referencers,
		UE::AssetRegistry::EDependencyCategory::Package,
		UE::AssetRegistry::FDependencyQuery(UE::AssetRegistry::EDependencyQuery::Hard));
	if (Referencers.Num() > 0)
	{
		TArray<TSharedPtr<FJsonValue>> RefEntries;
		for (const FName& Ref : Referencers)
		{
			RefEntries.Add(MakeShared<FJsonValueString>(Ref.ToString()));
		}
		Result->SetField(TEXT("referencers"), MakeShared<FJsonValueArray>(RefEntries));
	}

	FString FileOnDisk;
	if (FPackageName::TryConvertLongPackageNameToFilename(AssetData.PackageName.ToString(), FileOnDisk, FPackageName::GetAssetPackageExtension()) && !FileOnDisk.IsEmpty())
	{
		const int64 FileSize = IFileManager::Get().FileSize(*FileOnDisk);
		if (FileSize >= 0)
		{
			Result->SetStringField(TEXT("file"), FileOnDisk);
			Result->SetNumberField(TEXT("fileSize"), static_cast<double>(FileSize));
		}
	}

	return Result;
}