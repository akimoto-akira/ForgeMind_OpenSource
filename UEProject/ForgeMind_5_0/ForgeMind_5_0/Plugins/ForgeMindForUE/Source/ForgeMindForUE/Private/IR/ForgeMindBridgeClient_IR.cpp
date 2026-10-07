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