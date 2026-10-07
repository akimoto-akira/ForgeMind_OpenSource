// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#include "ForgeMindLogCapture.h"

#include "Misc/OutputDeviceRedirector.h"

void FForgeMindLogCapture::Start()
{
	// GLog is the redirector every UE_LOG flows through.
	GLog->AddOutputDevice(this);
}

void FForgeMindLogCapture::Stop()
{
	if (GLog)
	{
		GLog->RemoveOutputDevice(this);
	}
}

void FForgeMindLogCapture::Serialize(const TCHAR* V, ELogVerbosity::Type Verbosity, const FName& Category)
{
	// Never log from within Serialize (would recurse); guard anyway in
	// case a future change does.
	static thread_local bool bInside = false;
	if (bInside)
	{
		return;
	}
	bInside = true;

	{
		FScopeLock ScopeLock(&Lock);

		FEntry Entry;
		Entry.Time = FDateTime::UtcNow();
		Entry.Category = Category;
		// Strip the SetColor/BreakOnLog flag bits, keep the level itself.
		Entry.Verbosity = static_cast<ELogVerbosity::Type>(static_cast<uint8>(Verbosity) & ELogVerbosity::VerbosityMask);
		Entry.Text = V;

		if (Entries.Num() < MaxEntries)
		{
			Entries.Add(MoveTemp(Entry));
		}
		else
		{
			Entries[OldestIndex] = MoveTemp(Entry);
			OldestIndex = (OldestIndex + 1) % MaxEntries;
			++Dropped;
		}
	}

	bInside = false;
}

void FForgeMindLogCapture::GetEntries(TArray<FEntry>& OutEntries) const
{
	FScopeLock ScopeLock(&Lock);

	OutEntries.Reset(Entries.Num());
	if (Entries.Num() < MaxEntries)
	{
		// Ring not full yet - storage order is chronological.
		OutEntries = Entries;
	}
	else
	{
		for (int32 Offset = 0; Offset < Entries.Num(); ++Offset)
		{
			OutEntries.Add(Entries[(OldestIndex + Offset) % Entries.Num()]);
		}
	}
}

int64 FForgeMindLogCapture::GetDroppedCount() const
{
	FScopeLock ScopeLock(&Lock);
	return Dropped;
}

FForgeMindLogCapture& GetForgeMindLogCapture()
{
	static FForgeMindLogCapture Instance;
	return Instance;
}