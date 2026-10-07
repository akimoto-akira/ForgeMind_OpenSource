// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// "About" window for the ForgeMindForUE companion plugin.
//
// Opened from the editor main menu (Help ▸ About ForgeMind). Shows the
// plugin descriptor (friendly name / version / description), the engine
// version it was compiled against, and the current bridge connection state.

#include "ForgeMindAboutWindow.h"

#include "Framework/Application/SlateApplication.h"
#include "Interfaces/IPluginManager.h"
#include "Misc/EngineVersion.h"
#include "Widgets/Input/SButton.h"
#include "Widgets/Layout/SBorder.h"
#include "Widgets/Layout/SSpacer.h"
#include "Widgets/SBoxPanel.h"
#include "Widgets/SWindow.h"
#include "Widgets/Text/STextBlock.h"

#define LOCTEXT_NAMESPACE "ForgeMindAboutWindow"

namespace
{
	/** The single About window instance (cleared when the user closes it). */
	TWeakPtr<SWindow> GAboutWindow;
}

void ShowForgeMindAboutWindow()
{
	if (TSharedPtr<SWindow> Existing = GAboutWindow.Pin())
	{
		Existing->BringToFront();
		Existing->FlashWindow();
		return;
	}

	const TSharedPtr<IPlugin> Plugin = IPluginManager::Get().FindPlugin(TEXT("ForgeMindForUE"));

	const FString FriendlyName = Plugin.IsValid() ? Plugin->GetDescriptor().FriendlyName : TEXT("ForgeMindForUE");
	const FString Version = Plugin.IsValid()
		? FString::Printf(TEXT("%s (%s)"), *Plugin->GetDescriptor().VersionName, *FString::FromInt(Plugin->GetDescriptor().Version))
		: TEXT("-");
	const FString Description = Plugin.IsValid() ? Plugin->GetDescriptor().Description : FString();
	const FString CreatedBy = Plugin.IsValid() ? Plugin->GetDescriptor().CreatedBy : FString();

	TSharedRef<SWindow> Window = SNew(SWindow)
		.Title(FText::FromString(FString::Printf(TEXT("About %s"), *FriendlyName)))
		.ClientSize(FVector2D(520.f, 260.f))
		.SupportsMinimize(false)
		.SupportsMaximize(false)
		[
			SNew(SBorder)
			.Padding(20.f)
			[
				SNew(SVerticalBox)

				// Plugin name
				+ SVerticalBox::Slot()
				.AutoHeight()
				[
					SNew(STextBlock)
					.Text(FText::FromString(FriendlyName))
					.TextStyle(&FCoreStyle::Get().GetWidgetStyle<FTextBlockStyle>(TEXT("TextBlock")))
				]

				// Description
				+ SVerticalBox::Slot()
				.AutoHeight()
				.Padding(0.f, 10.f, 0.f, 0.f)
				[
					SNew(STextBlock)
					.Text(FText::FromString(Description))
					.AutoWrapText(true)
				]

				+ SVerticalBox::Slot()
				.AutoHeight()
				.Padding(0.f, 16.f, 0.f, 0.f)
				[
					SNew(SVerticalBox)

					+ SVerticalBox::Slot()
					.AutoHeight()
					[
						SNew(STextBlock)
						.Text(FText::FromString(FString::Printf(TEXT("Version: %s"), *Version)))
					]

					+ SVerticalBox::Slot()
					.AutoHeight()
					[
						SNew(STextBlock)
						.Text(FText::FromString(
							FString::Printf(TEXT("Engine: %s"), *FEngineVersion::Current().ToString(EVersionComponent::Patch))))
					]

					+ SVerticalBox::Slot()
					.AutoHeight()
					[
						SNew(STextBlock)
						.Text(FText::FromString(FString::Printf(TEXT("Author: %s"), *CreatedBy)))
					]
				]

				+ SVerticalBox::Slot()
				.FillHeight(1.f)
				[
					SNew(SSpacer)
				]

				+ SVerticalBox::Slot()
				.AutoHeight()
				.HAlign(HAlign_Right)
				[
					SNew(SButton)
					.Content()
					[
						SNew(STextBlock).Text(FText::FromString(TEXT("Close")))
					]
					.OnClicked_Lambda([]()
					{
						if (TSharedPtr<SWindow> Open = GAboutWindow.Pin())
						{
							Open->RequestDestroyWindow();
						}
						return FReply::Handled();
					})
				]
			]
		];

	GAboutWindow = Window;
	FSlateApplication::Get().AddWindow(Window);
}

#undef LOCTEXT_NAMESPACE