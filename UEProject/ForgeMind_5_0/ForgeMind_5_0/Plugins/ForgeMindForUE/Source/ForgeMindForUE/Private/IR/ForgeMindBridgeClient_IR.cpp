// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Bridge entry points for the versioned Asset IR pipeline:
// apply_asset_ir / get_asset_ir. Payloads whose assets[] carry a
// snake_case "assetType" and a "5.x" string version are routed to the
// domain handler registry; anything else falls through to the legacy
// AssetIR v2 implementation so older hosts keep working.

#include "ForgeMindBridgeClient.h"
#include "ForgeMindIRHandler.h"

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleApplyAssetIR(const TSharedPtr<FJsonObject>& Payload)
{
	if (!FForgeMindIRRegistry::Get().IsNewFormat(Payload))
	{
		// IR rules.md section 2.2: a payload that carries assets[] entries but
		// no snake_case assetType on any of them is an unknown-domain request,
		// NOT a legacy v2 request. Falling through to the old pipeline applied
		// such IRs with unpredictable results instead of reporting
		// UNSUPPORTED_ASSET_TYPE, which is exactly the silent-fallback failure
		// mode section 2.2 exists to prevent.
		const TArray<TSharedPtr<FJsonValue>>* Assets = nullptr;
		const bool bHasEntries = Payload.IsValid()
			&& Payload->TryGetArrayField(TEXT("assets"), Assets)
			&& Assets->Num() > 0;

		if (bHasEntries)
		{
			TSharedRef<FJsonObject> Failure = MakeShared<FJsonObject>();
			Failure->SetBoolField(TEXT("success"), false);
			Failure->SetStringField(TEXT("error"),
				TEXT("asset IR entries are missing the required 'assetType' field"));
			Failure->SetStringField(TEXT("errorCode"), TEXT("UNSUPPORTED_ASSET_TYPE"));
			return Failure;
		}
		return HandleApplyAssetIRLegacy(Payload);
	}
	return FForgeMindIRRegistry::Get().ApplyBatch(Payload);
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleGetAssetIR(const TSharedPtr<FJsonObject>& Payload)
{
	// A directory scan request belongs to the legacy list/scan flow; the
	// versioned read path is address-driven ({ path } or { paths: [] }).
	bool bHasPathOrPaths = false;
	if (Payload.IsValid())
	{
		FString Single;
		bHasPathOrPaths = (Payload->TryGetStringField(TEXT("path"), Single) && !Single.IsEmpty())
			|| Payload->HasField(TEXT("paths"));
	}

	if (bHasPathOrPaths)
	{
		return FForgeMindIRRegistry::Get().ReadIR(Payload);
	}
	return HandleGetAssetIRLegacy(Payload);
}