// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#pragma once

#include "ConnectionDrawingPolicy.h"
#include "EdGraphUtilities.h"

class UMaterialGraph;
class UMaterialGraphSchema;
class UEdGraphSchema_K2;

/**
 * Universal connection drawing policy that honors the ForgeMind wire style
 * preference across graph editors that use UEdGraph (blueprint event
 * graphs, functions, macros, animation blueprints, sound cues, behavior
 * trees, and similar).
 *
 * Curve keeps the engine default spline; Straight, Elbow and Circuit
 * replace the wire geometry while preserving the engine's hover
 * hit-testing (so right-click split / relink keeps working on the visible
 * wire shape).
 *
 * The scope matches the Q-key auto-arrange feature (any UEdGraph with a
 * valid schema).
 */
class FForgeMindConnectionDrawingPolicy : public FConnectionDrawingPolicy
{
public:
	FForgeMindConnectionDrawingPolicy(int32 InBackLayerID, int32 InFrontLayerID, float ZoomFactor, const FSlateRect& InClippingRect, FSlateWindowElementList& InDrawElements, UEdGraph* InGraphObj);

	// FConnectionDrawingPolicy interface
	virtual void DetermineWiringStyle(UEdGraphPin* OutputPin, UEdGraphPin* InputPin, /*inout*/ FConnectionParams& Params) override;
	virtual void DrawConnection(int32 LayerId, const FVector2D& Start, const FVector2D& End, const FConnectionParams& Params) override;
	// End of FConnectionDrawingPolicy interface

protected:
	/** Shared geometry + hover hit-testing for Straight/Elbow/Circuit. */
	void DrawStyledConnection(int32 LayerId, const FVector2D& Start, const FVector2D& End, const FConnectionParams& Params);

	/** Cached graph object (needed to query the schema for pin-type colors). */
	UEdGraph* GraphObj;
};

/**
 * Material-graph-specific connection drawing policy that honors the
 * ForgeMind wire style.  Inherits directly from FConnectionDrawingPolicy
 * (not FMaterialGraphConnectionDrawingPolicy, whose header is private)
 * and re-implements DetermineWiringStyle to preserve material-specific
 * wire colors (active/inactive, exec, value-type, Substrate, hover
 * deemphasis) and knot tangent reversal.
 *
 * When the wire style is Curve, DrawConnection delegates to the base
 * FConnectionDrawingPolicy so the engine spline is used unchanged.
 * For Straight/Elbow/Circuit the same geometry + hover hit-testing as
 * the blueprint policy is applied.
 */
class FForgeMindMaterialConnectionDrawingPolicy : public FConnectionDrawingPolicy
{
public:
	FForgeMindMaterialConnectionDrawingPolicy(int32 InBackLayerID, int32 InFrontLayerID, float ZoomFactor, const FSlateRect& InClippingRect, FSlateWindowElementList& InDrawElements, UEdGraph* InGraphObj);

	// FConnectionDrawingPolicy interface
	virtual void DetermineWiringStyle(UEdGraphPin* OutputPin, UEdGraphPin* InputPin, /*inout*/ FConnectionParams& Params) override;
	virtual void DrawConnection(int32 LayerId, const FVector2D& Start, const FVector2D& End, const FConnectionParams& Params) override;
	// End of FConnectionDrawingPolicy interface

private:
	/** Cached material graph (CastChecked in constructor, safe afterwards). */
	UMaterialGraph* MaterialGraph;
	/** Cached material graph schema. */
	const UMaterialGraphSchema* MaterialGraphSchema;

	/** Knot tangent reversal cache (mirrors FMaterialGraphConnectionDrawingPolicy). */
	TMap<class UMaterialGraphNode_Knot*, bool> KnotToReversedDirectionMap;

	bool ShouldChangeTangentForKnot(class UMaterialGraphNode_Knot* Knot);
	bool GetAverageConnectedPosition(class UMaterialGraphNode_Knot* Knot, EEdGraphPinDirection Direction, FVector2D& OutPos) const;
	bool FindPinCenter(UEdGraphPin* Pin, FVector2D& OutCenter) const;
};

/**
 * Factory handing the ForgeMind policy to every graph panel via
 * FEdGraphUtilities::RegisterVisualPinConnectionFactory.  Works for all
 * schemas - the ForgeMind wire style applies to any UEdGraph-based editor,
 * matching the Q-key auto-arrange scope.
 */
class FForgeMindPinConnectionFactory : public FGraphPanelPinConnectionFactory
{
public:
	virtual FConnectionDrawingPolicy* CreateConnectionPolicy(const UEdGraphSchema* Schema, int32 InBackLayerID, int32 InFrontLayerID, float ZoomFactor, const FSlateRect& InClippingRect, FSlateWindowElementList& InDrawElements, UEdGraph* InGraphObj) const override;
};