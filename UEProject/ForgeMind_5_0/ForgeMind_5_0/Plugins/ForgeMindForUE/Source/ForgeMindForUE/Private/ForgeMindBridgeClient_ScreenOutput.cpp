// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Debug-output handlers. Both use array-valued filter conditions where
// an empty array means "no filter" for that dimension, and both keep
// the newest matches when truncating to the limit.
//
// get_debug_output  - UE_LOG messages buffered in memory since the
//   plugin module started (FForgeMindLogCapture):
//     keywords   substring match on the text (any matches)
//     categories log category names (case-insensitive)
//     levels     fatal/error/warning/display/log/verbose/veryverbose
//
// get_screen_output - AddOnScreenDebugMessage output snapshotted every
//   frame (FForgeMindScreenCapture):
//     keywords   substring match on the text (any matches)
//     colors     common color names or hex ("red", "#00FF00"), RGB only

#include "ForgeMindBridgeClient.h"

#include "Dom/JsonValue.h"
#include "ForgeMindLogCapture.h"
#include "ForgeMindScreenCapture.h"

// ---------------------------------------------------------------------------
// Shared helpers
// ---------------------------------------------------------------------------

static TArray<FString> GetStringArrayField(const TSharedPtr<FJsonObject>& Payload, const TCHAR* FieldName)
{
	TArray<FString> Values;
	if (Payload.IsValid())
	{
		const TArray<TSharedPtr<FJsonValue>>* Array = nullptr;
		if (Payload->TryGetArrayField(FieldName, Array))
		{
			for (const TSharedPtr<FJsonValue>& Item : *Array)
			{
				FString Value;
				if (Item.IsValid() && Item->TryGetString(Value) && !Value.IsEmpty())
				{
					Values.Add(MoveTemp(Value));
				}
			}
		}
	}
	return Values;
}

static int32 GetLimitField(const TSharedPtr<FJsonObject>& Payload, int32 DefaultLimit, int32 MaxLimit)
{
	double Value = DefaultLimit;
	if (Payload.IsValid())
	{
		Payload->TryGetNumberField(TEXT("limit"), Value);
	}
	return FMath::Clamp(static_cast<int32>(Value), 1, MaxLimit);
}

static bool TextMatchesKeywords(const FString& Text, const TArray<FString>& Keywords)
{
	if (Keywords.Num() == 0)
	{
		return true;
	}
	for (const FString& Keyword : Keywords)
	{
		if (Text.Contains(Keyword, ESearchCase::IgnoreCase))
		{
			return true;
		}
	}
	return false;
}

/** Keeps only the newest Limit matches (entries are in chronological order). */
static void TruncateToNewest(TArray<TSharedPtr<FJsonValue>>& Matches, int32 Limit)
{
	if (Matches.Num() > Limit)
	{
		Matches.RemoveAt(0, Matches.Num() - Limit);
	}
}

// ---------------------------------------------------------------------------
// get_debug_output - UE_LOG ring buffer
// ---------------------------------------------------------------------------

static FString VerbosityToName(ELogVerbosity::Type Verbosity)
{
	// Core's ToString covers Fatal..VeryVerbose.
	return ToString(Verbosity);
}

static bool VerbosityAllowed(ELogVerbosity::Type Verbosity, const TArray<FString>& Levels)
{
	if (Levels.Num() == 0)
	{
		return true;
	}
	const FString Name = VerbosityToName(Verbosity);
	for (const FString& Level : Levels)
	{
		if (Name.Equals(Level, ESearchCase::IgnoreCase))
		{
			return true;
		}
	}
	return false;
}

static bool CategoryAllowed(const FString& Category, const TArray<FString>& Categories)
{
	if (Categories.Num() == 0)
	{
		return true;
	}
	for (const FString& Wanted : Categories)
	{
		if (Category.Equals(Wanted, ESearchCase::IgnoreCase))
		{
			return true;
		}
	}
	return false;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleGetDebugOutput(const TSharedPtr<FJsonObject>& Payload)
{
	const TArray<FString> Keywords = GetStringArrayField(Payload, TEXT("keywords"));
	const TArray<FString> Categories = GetStringArrayField(Payload, TEXT("categories"));
	const TArray<FString> Levels = GetStringArrayField(Payload, TEXT("levels"));
	const int32 Limit = GetLimitField(Payload, 200, FForgeMindLogCapture::MaxEntries);

	FForgeMindLogCapture& Capture = GetForgeMindLogCapture();
	TArray<FForgeMindLogCapture::FEntry> Buffer;
	Capture.GetEntries(Buffer);

	TArray<TSharedPtr<FJsonValue>> Matches;
	for (const FForgeMindLogCapture::FEntry& Entry : Buffer)
	{
		const FString Category = Entry.Category.ToString();
		if (!CategoryAllowed(Category, Categories) ||
			!VerbosityAllowed(Entry.Verbosity, Levels) ||
			!TextMatchesKeywords(Entry.Text, Keywords))
		{
			continue;
		}

		TSharedRef<FJsonObject> JsonEntry = MakeShared<FJsonObject>();
		JsonEntry->SetStringField(TEXT("time"), Entry.Time.ToIso8601());
		JsonEntry->SetStringField(TEXT("category"), Category);
		JsonEntry->SetStringField(TEXT("level"), VerbosityToName(Entry.Verbosity));
		JsonEntry->SetStringField(TEXT("text"), Entry.Text);
		Matches.Add(MakeShared<FJsonValueObject>(JsonEntry));
	}

	const int32 Matched = Matches.Num();
	TruncateToNewest(Matches, Limit);

	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetArrayField(TEXT("entries"), Matches);
	Result->SetNumberField(TEXT("buffered"), Buffer.Num());
	Result->SetNumberField(TEXT("dropped"), Capture.GetDroppedCount());
	Result->SetNumberField(TEXT("matched"), Matched);
	Result->SetNumberField(TEXT("limit"), Limit);
	return Result;
}

// ---------------------------------------------------------------------------
// get_screen_output - on-screen debug message snapshots
// ---------------------------------------------------------------------------

static FString ColorToHex(FColor Color)
{
	return FString::Printf(TEXT("#%02X%02X%02X"), Color.R, Color.G, Color.B);
}

/** Normalizes one colors-filter entry to "#RRGGBB"; false when unparseable. */
static bool ColorFilterToHex(const FString& Raw, FString& OutHex)
{
	static const struct { const TCHAR* Name; FColor Color; } Names[] =
	{
		{ TEXT("red"),       FColor::Red },
		{ TEXT("green"),     FColor::Green },
		{ TEXT("blue"),      FColor::Blue },
		{ TEXT("yellow"),    FColor::Yellow },
		{ TEXT("cyan"),      FColor::Cyan },
		{ TEXT("magenta"),   FColor::Magenta },
		{ TEXT("orange"),    FColor::Orange },
		{ TEXT("purple"),    FColor::Purple },
		{ TEXT("white"),     FColor::White },
		{ TEXT("black"),     FColor::Black },
		{ TEXT("silver"),    FColor::Silver },
		{ TEXT("emerald"),   FColor::Emerald },
		{ TEXT("turquoise"), FColor::Turquoise },
	};
	for (const auto& Name : Names)
	{
		if (Raw.Equals(Name.Name, ESearchCase::IgnoreCase))
		{
			OutHex = ColorToHex(Name.Color);
			return true;
		}
	}

	// Hex form: optional '#' + 3/6/8 hex digits (FColor::FromHex accepts
	// exactly these; alpha digits are accepted but ignored by matching).
	FString Digits = Raw.StartsWith(TEXT("#")) ? Raw.Mid(1) : Raw;
	if (Digits.Len() != 3 && Digits.Len() != 6 && Digits.Len() != 8)
	{
		return false;
	}
	for (const TCHAR Character : Digits)
	{
		if (!FChar::IsHexDigit(Character))
		{
			return false;
		}
	}
	OutHex = ColorToHex(FColor::FromHex(Raw));
	return true;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleGetScreenOutput(const TSharedPtr<FJsonObject>& Payload)
{
	const TArray<FString> Keywords = GetStringArrayField(Payload, TEXT("keywords"));
	const TArray<FString> RawColors = GetStringArrayField(Payload, TEXT("colors"));
	const int32 Limit = GetLimitField(Payload, 200, FForgeMindScreenCapture::MaxEntries);

	TArray<FString> ColorHexes;
	for (const FString& Raw : RawColors)
	{
		FString Hex;
		if (ColorFilterToHex(Raw, Hex))
		{
			ColorHexes.Add(Hex);
		}
	}

	FForgeMindScreenCapture& Capture = GetForgeMindScreenCapture();
	TArray<FForgeMindScreenCapture::FEntry> Buffer;
	Capture.GetEntries(Buffer);

	TArray<TSharedPtr<FJsonValue>> Matches;
	for (const FForgeMindScreenCapture::FEntry& Entry : Buffer)
	{
		const FString Hex = ColorToHex(Entry.Color);
		// (5.6's TArray::Contains has no ESearchCase overload)
		const bool bColorAllowed = ColorHexes.Num() == 0 || ColorHexes.ContainsByPredicate(
			[&Hex](const FString& Wanted) { return Hex.Equals(Wanted, ESearchCase::IgnoreCase); });
		if (!bColorAllowed)
		{
			continue;
		}
		if (!TextMatchesKeywords(Entry.Text, Keywords))
		{
			continue;
		}

		TSharedRef<FJsonObject> JsonEntry = MakeShared<FJsonObject>();
		JsonEntry->SetStringField(TEXT("time"), Entry.Time.ToIso8601());
		// Keyless messages carry INDEX_NONE, like the engine's own Key.
		const int64 ReportedKey = Entry.Key == static_cast<uint64>(INDEX_NONE)
			? INDEX_NONE
			: static_cast<int64>(static_cast<int32>(Entry.Key));
		JsonEntry->SetNumberField(TEXT("key"), static_cast<double>(ReportedKey));
		JsonEntry->SetStringField(TEXT("color"), Hex);
		JsonEntry->SetStringField(TEXT("text"), Entry.Text);
		Matches.Add(MakeShared<FJsonValueObject>(JsonEntry));
	}

	const int32 Matched = Matches.Num();
	TruncateToNewest(Matches, Limit);

	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetArrayField(TEXT("entries"), Matches);
	Result->SetNumberField(TEXT("captured"), Buffer.Num());
	Result->SetNumberField(TEXT("dropped"), Capture.GetDroppedCount());
	Result->SetNumberField(TEXT("matched"), Matched);
	Result->SetNumberField(TEXT("limit"), Limit);
	return Result;
}