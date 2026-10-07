// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#pragma once

#include "CoreMinimal.h"
#include "Misc/OutputDevice.h"

/**
 * Ring buffer of every UE_LOG message seen since the plugin module
 * started. Registered as an FOutputDevice on GLog in StartupModule, so
 * entries only cover messages logged after registration - use the
 * get_log_output command for the full history on disk.
 *
 * Thread-safe: Serialize arrives on whichever thread did the logging.
 */
class FForgeMindLogCapture : public FOutputDevice
{
public:
	struct FEntry
	{
		FDateTime Time;
		FName Category;
		ELogVerbosity::Type Verbosity = ELogVerbosity::NoLogging;
		FString Text;
	};

	/** Hooks this device into GLog (call from StartupModule). */
	void Start();

	/** Removes this device from GLog (call from ShutdownModule). */
	void Stop();

	/** Snapshot of the buffer in chronological order. */
	void GetEntries(TArray<FEntry>& OutEntries) const;

	/** How many entries fell off the ring since Start. */
	int64 GetDroppedCount() const;

	/** Ring capacity. */
	static constexpr int32 MaxEntries = 2000;

private:
	virtual void Serialize(const TCHAR* V, ELogVerbosity::Type Verbosity, const FName& Category) override;

	mutable FCriticalSection Lock;
	TArray<FEntry> Entries;
	int32 OldestIndex = 0;
	int64 Dropped = 0;
};

/** Process-wide capture instance (module registers/unregisters it). */
FForgeMindLogCapture& GetForgeMindLogCapture();