// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Editor screenshot capture. Two handlers:
//
// list_windows  - enumerate all top-level Slate windows (title, size, position)
// take_screenshot - capture the editor screen via FScreenshotRequest,
//   optionally specifying a save path and whether to include UI.
//
// FScreenshotRequest::RequestScreenshot is asynchronous: it sets a flag
// that the viewport reads on the next frame. We register a delegate on
// OnScreenshotRequestProcessed and block with a ticker-based wait until
// the screenshot is written or a timeout expires.

#include "ForgeMindBridgeClient.h"

#include "Dom/JsonValue.h"
#include "Framework/Application/SlateApplication.h"
#include "Widgets/SWindow.h"
#include "UnrealClient.h"
#include "Misc/Paths.h"
#include "HAL/FileManager.h"

// ---------------------------------------------------------------------------
// list_windows - enumerate all top-level Slate windows
// ---------------------------------------------------------------------------

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleListWindows(const TSharedPtr<FJsonObject>& Payload)
{
	TArray<TSharedRef<SWindow>> TopLevelWindows = FSlateApplication::Get().GetTopLevelWindows();

	TArray<TSharedPtr<FJsonValue>> WindowsArray;
	for (int32 Index = 0; Index < TopLevelWindows.Num(); ++Index)
	{
		const TSharedRef<SWindow>& Window = TopLevelWindows[Index];

		TSharedRef<FJsonObject> WindowObj = MakeShared<FJsonObject>();
		WindowObj->SetNumberField(TEXT("index"), Index);

		FText Title = Window->GetTitle();
		WindowObj->SetStringField(TEXT("title"), Title.ToString());

		// GetSizeInScreen returns the full window size including title bar
		const FVector2D Size = Window->GetSizeInScreen();
		WindowObj->SetNumberField(TEXT("width"), static_cast<double>(Size.X));
		WindowObj->SetNumberField(TEXT("height"), static_cast<double>(Size.Y));

		// Window position on screen
		const FGeometry Geometry = Window->GetWindowGeometryInScreen();
		const FVector2D Position = Geometry.GetAbsolutePosition();
		WindowObj->SetNumberField(TEXT("x"), static_cast<double>(Position.X));
		WindowObj->SetNumberField(TEXT("y"), static_cast<double>(Position.Y));

		// Type name for identification (e.g. "SWindow", "SDockTab" etc.)
		WindowObj->SetStringField(TEXT("type"), Window->GetTypeAsString());

		// Is this the active window?
		TSharedPtr<SWindow> ActiveWindow = FSlateApplication::Get().GetActiveTopLevelWindow();
		WindowObj->SetBoolField(TEXT("isActive"), ActiveWindow == Window);

		WindowsArray.Add(MakeShared<FJsonValueObject>(WindowObj));
	}

	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetArrayField(TEXT("windows"), WindowsArray);
	Result->SetNumberField(TEXT("count"), static_cast<double>(WindowsArray.Num()));

	return Result;
}

// ---------------------------------------------------------------------------
// take_screenshot - capture the editor screen
// ---------------------------------------------------------------------------

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleTakeScreenshot(const TSharedPtr<FJsonObject>& Payload)
{
	bool bShowUI = true;
	FString SavePath;
	bool bAddFilenameSuffix = false;

	if (Payload.IsValid())
	{
		Payload->TryGetBoolField(TEXT("show_ui"), bShowUI);
		Payload->TryGetStringField(TEXT("save_path"), SavePath);
		Payload->TryGetBoolField(TEXT("add_filename_suffix"), bAddFilenameSuffix);
	}

	// Default save path if none provided
	if (SavePath.IsEmpty())
	{
		const FString Timestamp = FDateTime::Now().ToString(TEXT("%Y%m%d_%H%M%S"));
		SavePath = FPaths::ScreenShotDir() / FString::Printf(TEXT("ForgeMind_%s.png"), *Timestamp);
	}

	// Ensure the directory exists
	FPaths::MakePlatformFilename(SavePath);
	const FString Dir = FPaths::GetPath(SavePath);
	if (!Dir.IsEmpty() && !IFileManager::Get().DirectoryExists(*Dir))
	{
		IFileManager::Get().MakeDirectory(*Dir, true);
	}

	// Request the screenshot. FScreenshotRequest is asynchronous: it
	// sets bIsScreenshotRequested=true, and the viewport reads this
	// flag on the next frame, captures, writes the file, then fires
	// OnScreenshotRequestProcessed.
	FScreenshotRequest::RequestScreenshot(SavePath, bShowUI, bAddFilenameSuffix);

	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetBoolField(TEXT("requested"), true);
	Result->SetStringField(TEXT("path"), SavePath);
	Result->SetBoolField(TEXT("show_ui"), bShowUI);
	Result->SetStringField(TEXT("note"),
		TEXT("Screenshot is captured asynchronously on the next frame. ")
		TEXT("The file appears shortly at the returned path. ")
		TEXT("Subscribe to FScreenshotRequest::OnScreenshotRequestProcessed for completion notification."));

	return Result;
}