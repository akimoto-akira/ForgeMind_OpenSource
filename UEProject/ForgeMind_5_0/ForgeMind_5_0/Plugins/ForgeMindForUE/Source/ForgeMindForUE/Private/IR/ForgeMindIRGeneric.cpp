// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#include "ForgeMindIRGeneric.h"

#include "UObject/UnrealType.h"

FGenericAssetIRHandler::FGenericAssetIRHandler(FConfig InConfig)
	: Config(MoveTemp(InConfig))
{
}

TSharedPtr<FJsonValue> FGenericAssetIRHandler::PatchValue(const TSharedPtr<FJsonObject>& Patch, const TCHAR* Key)
{
	if (!Patch.IsValid())
	{
		return nullptr;
	}
	return Patch->TryGetField(Key);
}

UObject* FGenericAssetIRHandler::CreateAssetObject(FIRApplyContext& Ctx)
{
	UClass* AssetClass = ForgeMindIR::ResolveClass(Config.DefaultClassName);
	if (AssetClass == nullptr)
	{
		Ctx.Fail(TEXT("UNSUPPORTED_ASSET_TYPE"),
			FString::Printf(TEXT("class '%s' for assetType '%s' is not available in this editor (module not loaded)"),
				*Config.DefaultClassName, *Ctx.AssetType));
		return nullptr;
	}
	UObject* Asset = ForgeMindIR::CreateAssetInstance(AssetClass, Ctx.Path, true);
	if (Asset == nullptr)
	{
		Ctx.Fail(TEXT("CREATE_FAILED"), FString::Printf(TEXT("failed to create asset at %s"), *Ctx.Path));
	}
	return Asset;
}

void FGenericAssetIRHandler::ApplyMappedProperties(UObject* Asset, const TSharedPtr<FJsonObject>& Definition, FIRApplyContext& Ctx)
{
	if (Asset == nullptr || !Definition.IsValid())
	{
		return;
	}
	for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Definition->Values)
	{
		// Structured entries (arrays / objects) are handled by the domain
		// hook; only scalars go through the property map.
		if (Pair.Value->Type == EJson::Array || Pair.Value->Type == EJson::Object)
		{
			continue;
		}
		const FString* Mapped = Config.PropertyMap.Find(Pair.Key);
		const FString PropertyName = Mapped != nullptr ? *Mapped : Pair.Key;

		FString Error;
		if (!ForgeMindIR::SetProperty(Asset, PropertyName, Pair.Value, Error))
		{
			// Unknown keys are warnings, never failures: the IR schema is
			// open-ended on purpose.
			Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), Error);
		}
	}
}

UObject* FGenericAssetIRHandler::Create(FIRApplyContext& Ctx)
{
	UObject* Asset = CreateAssetObject(Ctx);
	if (Asset == nullptr)
	{
		return nullptr;
	}
	ApplyMappedProperties(Asset, Ctx.Definition, Ctx);
	ApplyDefinitionExtras(Asset, Ctx);
	return Asset;
}

bool FGenericAssetIRHandler::Update(UObject* Asset, FIRApplyContext& Ctx)
{
	if (Asset == nullptr)
	{
		Ctx.Fail(TEXT("ASSET_NOT_FOUND"), FString::Printf(TEXT("asset not found at %s"), *Ctx.Path));
		return false;
	}

	for (const TSharedPtr<FJsonValue>& PatchEntry : Ctx.Patches)
	{
		const TSharedPtr<FJsonObject> Patch = PatchEntry->AsObject();
		if (!Patch.IsValid())
		{
			continue;
		}

		FString Type, Name;
		Patch->TryGetStringField(TEXT("type"), Type);
		Patch->TryGetStringField(TEXT("name"), Name);
		const bool bDelete = ForgeMindIR::IsDeletePatch(Patch);

		FString ActualOp;
		bool bOk = false;

		if (!ApplyPatchExtras(Asset, Patch, Ctx, ActualOp, bOk))
		{
			// Fallback: treat the patch type as a UPROPERTY name so any
			// scalar the domain did not claim still lands on the asset.
			const TSharedPtr<FJsonValue> Value = PatchValue(Patch);
			const bool bHasProperty = ForgeMindIR::FindProperty(Asset, Type) != nullptr;
			ActualOp = InferOp(bHasProperty, bDelete);

			if (bDelete)
			{
				Ctx.Warn(Config.UnsupportedPatchCode,
					FString::Printf(TEXT("delete is not supported for patch type '%s'"), *Type));
				bOk = false;
			}
			else if (Value.IsValid())
			{
				FString Error;
				bOk = ForgeMindIR::SetProperty(Asset, Type, Value, Error);
				if (!bOk)
				{
					Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), Error);
				}
			}
			else
			{
				// The patch carries its payload as flat sibling fields
				// (e.g. { type:"torqueControl", maxTorque: 2000 }).
				bool bAnyApplied = false;
				for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Patch->Values)
				{
					if (Pair.Key == TEXT("type") || Pair.Key == TEXT("name") || Pair.Key == TEXT("delete"))
					{
						continue;
					}
					FString Error;
					if (ForgeMindIR::SetProperty(Asset, Pair.Key, Pair.Value, Error))
					{
						bAnyApplied = true;
					}
					else
					{
						Ctx.Warn(TEXT("PROPERTY_NOT_FOUND"), Error);
					}
				}
				bOk = bAnyApplied;
				if (!bOk)
				{
					Ctx.Warn(Config.UnsupportedPatchCode,
						FString::Printf(TEXT("patch type '%s' is not handled by the %s domain"), *Type, *Ctx.AssetType));
					continue;
				}
			}
		}

		if (!bOk && ActualOp != TEXT("failed"))
		{
			ActualOp = TEXT("failed");
		}
		Ctx.AddPatchResult(ActualOp, Type, Name, bOk);
	}

	return true;
}

bool FGenericAssetIRHandler::DeleteAsset(FIRApplyContext& Ctx)
{
	FString Error;
	if (!ForgeMindIR::DeleteAssetObject(Ctx.Path, Error))
	{
		Ctx.Fail(TEXT("DELETE_FAILED"), Error);
		return false;
	}
	return true;
}

TSharedPtr<FJsonObject> FGenericAssetIRHandler::Read(UObject* Asset, FIRApplyContext& Ctx)
{
	TSharedPtr<FJsonObject> Definition = ForgeMindIR::GetProperties(Asset, Config.ReadProperties);
	ReadExtras(Asset, Definition, Ctx);
	return Definition;
}

bool FGenericAssetIRHandler::CompileAsset(UObject* Asset, FIRApplyContext& Ctx)
{
	if (Asset != nullptr)
	{
		Asset->PostEditChange();
		Asset->MarkPackageDirty();
	}
	Ctx.CompileTimeMs = 0.0;
	if (!Config.bHasCompiler)
	{
		return true;
	}
	return true;
}