// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#include "ForgeMindScreenCapture.h"

#include "Engine/Engine.h"

// UE 5.6 offers no official hook for on-screen debug messages:
// UEngine::AddOnScreenDebugMessage is not virtual, the two containers
// (ScreenMessages / PriorityScreenMessages) are private with no
// accessor, and the guarding critical section is a file-static inside
// UnrealEngine.cpp. Access them through the standard
// explicit-template-instantiation member-pointer loophole: access
// checking is skipped for template arguments of an explicit
// instantiation definition, so the member pointers themselves are
// legal to form and layout-safe across engine versions.
namespace
{
	template <typename Tag, typename Tag::Type MemberPointer>
	struct FPrivateMemberBridge
	{
		friend typename Tag::Type GetPrivateMember(Tag)
		{
			return MemberPointer;
		}
	};

	struct FScreenMessagesTag
	{
		using Type = TMap<int32, FScreenMessageString> UEngine::*;
		friend Type GetPrivateMember(FScreenMessagesTag);
	};

	struct FPriorityScreenMessagesTag
	{
		using Type = TArray<FScreenMessageString> UEngine::*;
		friend Type GetPrivateMember(FPriorityScreenMessagesTag);
	};

	template struct FPrivateMemberBridge<FScreenMessagesTag, &UEngine::ScreenMessages>;
	template struct FPrivateMemberBridge<FPriorityScreenMessagesTag, &UEngine::PriorityScreenMessages>;
}

static const TMap<int32, FScreenMessageString>& ScreenMessagesOf(UEngine& Engine)
{
	return Engine.*GetPrivateMember(FScreenMessagesTag());
}

static const TArray<FScreenMessageString>& PriorityScreenMessagesOf(UEngine& Engine)
{
	return Engine.*GetPrivateMember(FPriorityScreenMessagesTag());
}

/** (color, text) identity used for cross-frame dedup. */
static FString SignatureFor(FColor Color, const FString& Text)
{
	return FString::Printf(TEXT("%08X|%s"), Color.DWColor(), *Text);
}

void FForgeMindScreenCapture::Start()
{
	TickerHandle = FTSTicker::GetCoreTicker().AddTicker(
		FTickerDelegate::CreateLambda([this](float DeltaTime)
		{
			CaptureTick();
			return true;
		}));
}

void FForgeMindScreenCapture::Stop()
{
	FTSTicker::GetCoreTicker().RemoveTicker(TickerHandle);
}

void FForgeMindScreenCapture::CaptureTick()
{
	if (!GEngine)
	{
		return;
	}

	// Note on locking: the engine guards these containers with a
	// file-static critical section we cannot reach. Expiry removal only
	// happens on the game thread (same thread as this ticker), but adds
	// can in principle come from worker threads; a concurrent reallocation
	// mid-iteration is a theoretical race accepted for this debug bridge.

	// Keyed messages are overwritten in place: re-capture only when the
	// (color, text) signature changes, forget keys that disappeared.
	TSet<int32> PresentKeys;
	for (const TTuple<int32, FScreenMessageString>& Pair : ScreenMessagesOf(*GEngine))
	{
		PresentKeys.Add(Pair.Key);
		const FString Signature = SignatureFor(Pair.Value.DisplayColor, Pair.Value.ScreenMessage);
		const FString* Seen = SeenKeyedTexts.Find(Pair.Key);
		if (!Seen || *Seen != Signature)
		{
			SeenKeyedTexts.Add(Pair.Key) = Signature;
			AddEntry(static_cast<uint64>(Pair.Key), Pair.Value.DisplayColor, Pair.Value.ScreenMessage);
		}
	}
	for (TMap<int32, FString>::TIterator It(SeenKeyedTexts); It; ++It)
	{
		if (!PresentKeys.Contains(It.Key()))
		{
			It.RemoveCurrent();
		}
	}

	// Keyless (INDEX_NONE key) messages are append-only until they
	// expire: diff this frame's signature multiset against the last one
	// and report the newly added occurrences.
	TArray<FString> CurrentKeyless;
	const TArray<FScreenMessageString>& PriorityMessages = PriorityScreenMessagesOf(*GEngine);
	CurrentKeyless.Reserve(PriorityMessages.Num());
	for (const FScreenMessageString& Message : PriorityMessages)
	{
		CurrentKeyless.Add(SignatureFor(Message.DisplayColor, Message.ScreenMessage));
	}

	TArray<FString> Remaining = MoveTemp(PreviousKeylessSignatures);
	for (int32 Index = 0; Index < CurrentKeyless.Num(); ++Index)
	{
		const int32 Found = Remaining.Find(CurrentKeyless[Index]);
		if (Found == INDEX_NONE)
		{
			const FScreenMessageString& Message = PriorityMessages[Index];
			AddEntry(static_cast<uint64>(INDEX_NONE), Message.DisplayColor, Message.ScreenMessage);
		}
		else
		{
			Remaining.RemoveAt(Found);
		}
	}
	PreviousKeylessSignatures = MoveTemp(CurrentKeyless);
}

void FForgeMindScreenCapture::AddEntry(uint64 Key, FColor Color, const FString& Text)
{
	FScopeLock ScopeLock(&Lock);

	FEntry Entry;
	Entry.Time = FDateTime::UtcNow();
	Entry.Key = Key;
	Entry.Color = Color;
	Entry.Text = Text;

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

void FForgeMindScreenCapture::GetEntries(TArray<FEntry>& OutEntries) const
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

int64 FForgeMindScreenCapture::GetDroppedCount() const
{
	FScopeLock ScopeLock(&Lock);
	return Dropped;
}

FForgeMindScreenCapture& GetForgeMindScreenCapture()
{
	static FForgeMindScreenCapture Instance;
	return Instance;
}