// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#pragma once

#include "Engine/DeveloperSettings.h"

#include "ForgeMindWireStyleSettings.generated.h"

/** Wire rendering style for blueprint graph connections. */
UENUM(BlueprintType)
enum class EForgeMindWireStyle : uint8
{
	/** Engine default bezier spline (unchanged look). */
	Curve UMETA(DisplayName = "Curve (Default)"),

	/** Single straight segment between the two pins. */
	Straight UMETA(DisplayName = "Straight"),

	/** Orthogonal elbow connector with a vertical middle segment. */
	Elbow UMETA(DisplayName = "Elbow"),

	/** Circuit-board trace: symmetric horizontal runs joined by a 45-degree segment. */
	Circuit UMETA(DisplayName = "Circuit Board"),
};

/**
 * ForgeMind wire rendering preferences. Surfaced automatically under
 * Editor Preferences > General (EditorPerProjectUserSettings container),
 * no custom settings window is provided.
 */
UCLASS(Config = EditorPerProjectUserSettings, DefaultConfig, meta = (DisplayName = "ForgeMind Wire Style"))
class FORGEMINDFORUE_API UForgeMindWireStyleSettings : public UDeveloperSettings
{
	GENERATED_BODY()

public:
	/** How blueprint wires are drawn in every K2 graph panel. */
	UPROPERTY(Config, EditAnywhere, Category = "Rendering", meta = (DisplayName = "Blueprint Wire Style"))
	EForgeMindWireStyle WireStyle = EForgeMindWireStyle::Curve;

	virtual FName GetCategoryName() const override { return TEXT("General"); }

	virtual FText GetSectionText() const override
	{
		return NSLOCTEXT("ForgeMindWireStyle", "SectionText", "ForgeMind Wire Style");
	}

	virtual FText GetSectionDescription() const override
	{
		return NSLOCTEXT("ForgeMindWireStyle", "SectionDescription", "Rendering style for blueprint connection wires.");
	}
};