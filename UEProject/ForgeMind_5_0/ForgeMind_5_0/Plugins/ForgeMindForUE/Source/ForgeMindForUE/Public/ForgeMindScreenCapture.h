// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#pragma once

#include "CoreMinimal.h"
#include "Containers/Ticker.h"

/**
 * Frame-by-frame snapshot of UEngine's on-screen debug messages (the
 * AddOnScreenDebugMessage output). UE 5.6 offers no official hook for
 * them - the containers are private members with no accessor and the
 * add function is not virtual - so the implementation file snapshots
 * them from a game-thread ticker using an access-specifier workaround.
 *
 * Dedup semantics: INDEX_NONE-keyed messages are append-only, so new
 * tail entries are captured; keyed messages are overwritten in place,
 * so they are re-captured only when their text changes.
 */
class FForgeMindScreenCapture
{
public:
	struct FEntry
	{
		FDateTime Time;
		uint64 Key = 0;
		FColor Color = FColor::White;
		FString Text;
	};

	/** Starts the game-thread snapshot ticker (call from StartupModule). */
	void Start();

	/** Stops the ticker (call from ShutdownModule). */
	void Stop();

	/** Snapshot of captured messages in chronological order. */
	void GetEntries(TArray<FEntry>& OutEntries) const;

	/** How many entries fell off the ring since Start. */
	int64 GetDroppedCount() const;

	/** Ring capacity. */
	static constexpr int32 MaxEntries = 2000;

private:
	void CaptureTick();
	void AddEntry(uint64 Key, FColor Color, const FString& Text);

	mutable FCriticalSection Lock;
	TArray<FEntry> Entries;
	int32 OldestIndex = 0;
	int64 Dropped = 0;

	// Snapshot state, game thread only.
	TMap<int32, FString> SeenKeyedTexts;
	TArray<FString> PreviousKeylessSignatures;
	FTSTicker::FDelegateHandle TickerHandle;
};

/** Process-wide capture instance (module registers/unregisters it). */
FForgeMindScreenCapture& GetForgeMindScreenCapture();