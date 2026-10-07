// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Configurable base used by the domain handlers whose asset classes live
// in modules this plugin does not link (Niagara, Chaos, Landscape, Groom,
// Paper2D, MetaSounds, Sequencer, ...). The class is resolved by name at
// runtime and the definition/patches are applied through UPROPERTY
// reflection, so no compile-time dependency on those modules is needed.
// Domains with richer structure override the Apply/Read hooks.

#pragma once

#include "CoreMinimal.h"
#include "ForgeMindIRHandler.h"

class FGenericAssetIRHandler : public IIRHandler
{
public:
	struct FConfig
	{
		/** assetType strings (snake_case) this handler claims. */
		TArray<FString> AssetTypes;

		/** Default UClass short name used when creating the asset. */
		FString DefaultClassName;

		/** JSON definition key → UPROPERTY name. */
		TMap<FString, FString> PropertyMap;

		/** UPROPERTY names serialized back by Read(). */
		TArray<FString> ReadProperties;

		/** Set when the domain has a real compiler worth timing. */
		bool bHasCompiler = false;

		/** Warning code emitted when a patch type is not recognised. */
		FString UnsupportedPatchCode = TEXT("UNSUPPORTED_PATCH_TYPE");
	};

	explicit FGenericAssetIRHandler(FConfig InConfig);

	void GetAssetTypes(TArray<FString>& OutAssetTypes) const override { OutAssetTypes = Config.AssetTypes; }

	UObject* Create(FIRApplyContext& Ctx) override;
	bool Update(UObject* Asset, FIRApplyContext& Ctx) override;
	bool DeleteAsset(FIRApplyContext& Ctx) override;
	TSharedPtr<FJsonObject> Read(UObject* Asset, FIRApplyContext& Ctx) override;
	bool CompileAsset(UObject* Asset, FIRApplyContext& Ctx) override;

	/** Infers actualOp for a member that may or may not exist (IR rules.md section 4.3). */
	static FString InferOp(bool bExists, bool bDelete)
	{
		if (bDelete) return bExists ? FString(TEXT("delete")) : FString(TEXT("failed"));
		return bExists ? FString(TEXT("replace")) : FString(TEXT("add"));
	}

protected:
	/** Domain specific definition handling (sub objects, graphs, arrays...). */
	virtual void ApplyDefinitionExtras(UObject* Asset, FIRApplyContext& Ctx) {}

	/**
	 * Domain specific patch handling. Return false when the patch type is
	 * not owned by this domain so the base can report it.
	 */
	virtual bool ApplyPatchExtras(UObject* Asset, const TSharedPtr<FJsonObject>& Patch,
		FIRApplyContext& Ctx, FString& OutActualOp, bool& bOutOk) { return false; }

	/** Domain specific read-back appended to the definition object. */
	virtual void ReadExtras(UObject* Asset, const TSharedPtr<FJsonObject>& Definition, FIRApplyContext& Ctx) {}

	/** Override to build the asset with a factory instead of NewObject. */
	virtual UObject* CreateAssetObject(FIRApplyContext& Ctx);

	/** Applies Config.PropertyMap plus any unmapped keys that match a UPROPERTY. */
	void ApplyMappedProperties(UObject* Asset, const TSharedPtr<FJsonObject>& Definition, FIRApplyContext& Ctx);

	/** Reads "value" or a named field off a patch object. */
	static TSharedPtr<FJsonValue> PatchValue(const TSharedPtr<FJsonObject>& Patch, const TCHAR* Key = TEXT("value"));

	FConfig Config;
};