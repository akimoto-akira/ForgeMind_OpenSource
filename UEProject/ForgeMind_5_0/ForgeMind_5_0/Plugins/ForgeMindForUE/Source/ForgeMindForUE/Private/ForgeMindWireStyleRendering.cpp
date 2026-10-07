// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#include "ForgeMindWireStyleRendering.h"

#include "ConnectionDrawingPolicy.h"
#include "Containers/Array.h"
#include "Containers/Set.h"
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphPin.h"
#include "EdGraph/EdGraphNode.h"
#include "EdGraphSchema_K2.h"
#include "ForgeMindWireStyleSettings.h"
#include "GraphEditorSettings.h"
#include "GraphSplineOverlapResult.h"
#include "Layout/ArrangedWidget.h"
#include "MaterialGraph/MaterialGraph.h"
#include "MaterialGraph/MaterialGraphSchema.h"
#include "MaterialGraphNode_Knot.h"
#include "Math/Box2D.h"
#include "Math/Color.h"
#include "Math/Vector2D.h"
#include "Rendering/DrawElements.h"
#include "SGraphPin.h"
#include "Shader/ShaderTypes.h"
#include "Templates/Casts.h"
#include "Templates/SharedPointer.h"
#include "UObject/NameTypes.h"

// ============================================================
// FForgeMindConnectionDrawingPolicy (blueprint / non-material)
// ============================================================

FForgeMindConnectionDrawingPolicy::FForgeMindConnectionDrawingPolicy(int32 InBackLayerID, int32 InFrontLayerID, float InZoomFactor, const FSlateRect& InClippingRect, FSlateWindowElementList& InDrawElements, UEdGraph* InGraphObj)
	: FConnectionDrawingPolicy(InBackLayerID, InFrontLayerID, InZoomFactor, InClippingRect, InDrawElements)
	, GraphObj(InGraphObj)
{
}

void FForgeMindConnectionDrawingPolicy::DetermineWiringStyle(UEdGraphPin* OutputPin, UEdGraphPin* InputPin, /*inout*/ FConnectionParams& Params)
{
	// Re-implements FKismetConnectionDrawingPolicy::DetermineWiringStyle
	// using only public APIs so the ForgeMind wire style can coexist with
	// per-pin-type wire colors (exec = white, bool = red, float = green,
	// etc.), exec-wire thickness from UGraphEditorSettings, orphaned-pin
	// highlighting, knot tangent reversal, and hover deemphasis.
	//
	// The base FConnectionDrawingPolicy::DetermineWiringStyle only sets
	// AssociatedPin1/2 and leaves WireColor/WireThickness at their defaults
	// (white / 1.0).  Without this override all wires would render in the
	// same default white color regardless of pin type.
	Params.WireThickness = Settings->DefaultDataWireThickness;
	Params.AssociatedPin1 = OutputPin;
	Params.AssociatedPin2 = InputPin;

	// Get the schema and grab the default color from it
	check(OutputPin);
	check(GraphObj);
	const UEdGraphSchema* Schema = GraphObj->GetSchema();

	if (OutputPin->bOrphanedPin || (InputPin && InputPin->bOrphanedPin))
	{
		Params.WireColor = FLinearColor::Red;
	}
	else
	{
		Params.WireColor = Schema->GetPinTypeColor(OutputPin->PinType);
	}

	// If this is a K2 graph, handle exec wires with distinct color/thickness.
	if (const UEdGraphSchema_K2* K2Schema = Cast<const UEdGraphSchema_K2>(Schema))
	{
		// Exec wires get the exec color and thicker wire.
		if (OutputPin->PinType.PinCategory == UEdGraphSchema_K2::PC_Exec
			|| (InputPin && InputPin->PinType.PinCategory == UEdGraphSchema_K2::PC_Exec))
		{
			Params.WireColor = Settings->ExecutionPinTypeColor;
			Params.WireThickness = Settings->DefaultExecutionWireThickness;
		}
	}

	// Hover deemphasis (darkens non-hovered wires when a pin is hovered).
	const bool bDeemphasizeUnhoveredPins = HoveredPins.Num() > 0;
	if (bDeemphasizeUnhoveredPins)
	{
		ApplyHoverDeemphasis(OutputPin, InputPin, /*inout*/ Params.WireThickness, /*inout*/ Params.WireColor);
	}
}

void FForgeMindConnectionDrawingPolicy::DrawConnection(int32 LayerId, const FVector2D& Start, const FVector2D& End, const FConnectionParams& Params)
{
	// Curve keeps the full engine behavior (spline, hover, exec bubbles).
	if (GetDefault<UForgeMindWireStyleSettings>()->WireStyle == EForgeMindWireStyle::Curve)
	{
		FConnectionDrawingPolicy::DrawConnection(LayerId, Start, End, Params);
		return;
	}

	DrawStyledConnection(LayerId, Start, End, Params);
}

void FForgeMindConnectionDrawingPolicy::DrawStyledConnection(int32 LayerId, const FVector2D& Start, const FVector2D& End, const FConnectionParams& Params)
{
	const EForgeMindWireStyle WireStyle = GetDefault<UForgeMindWireStyleSettings>()->WireStyle;

	// Wire geometry in the same draw space the engine spline lives in.
	TArray<FVector2D> Points;
	if (WireStyle == EForgeMindWireStyle::Circuit && End.X - Start.X >= FMath::Abs(End.Y - Start.Y))
	{
		// PCB trace: symmetric horizontal runs joined by one 45-degree bridge.
		const float Dy = End.Y - Start.Y;
		const float Run = ((End.X - Start.X) - FMath::Abs(Dy)) * 0.5f;
		Points.Add(Start);
		Points.Add(FVector2D(Start.X + Run, Start.Y));
		Points.Add(FVector2D(End.X - Run, End.Y));
		Points.Add(End);
	}
	else if (WireStyle == EForgeMindWireStyle::Elbow || WireStyle == EForgeMindWireStyle::Circuit)
	{
		// Elbow; also the circuit fallback when the pins sit too steep or
		// backwards for a 45-degree trace.
		const float MidX = (Start.X + End.X) * 0.5f;
		Points.Add(Start);
		Points.Add(FVector2D(MidX, Start.Y));
		Points.Add(FVector2D(MidX, End.Y));
		Points.Add(End);
	}
	else
	{
		Points.Add(Start);
		Points.Add(End);
	}

	// Hover hit-testing: mirror the engine's bTreatSplinesLikePins block but
	// against the visible polyline, so right-click split / relink matches what
	// the user sees instead of the hidden spline.
	if (Settings->bTreatSplinesLikePins)
	{
		const float QueryDistanceTriggerThresholdSquared = FMath::Square(Settings->SplineHoverTolerance + Params.WireThickness * 0.5f);
		const float QueryDistanceForCloseSquared = FMath::Square(FMath::Sqrt(QueryDistanceTriggerThresholdSquared) + Settings->SplineCloseTolerance);

		FBox2D Bounds(ForceInit);
		for (const FVector2D& Point : Points)
		{
			Bounds += Point;
		}

		if (Bounds.ComputeSquaredDistanceToPoint(LocalMousePosition) < QueryDistanceForCloseSquared)
		{
			FVector2D ClosestPoint(ForceInit);
			float ClosestDistanceSquared = FLT_MAX;

			for (int32 Index = 0; Index < Points.Num() - 1; ++Index)
			{
				const FVector2D Candidate = FMath::ClosestPointOnSegment2D(LocalMousePosition, Points[Index], Points[Index + 1]);
				const float DistanceSquared = (LocalMousePosition - Candidate).SizeSquared();
				if (DistanceSquared < ClosestDistanceSquared)
				{
					ClosestDistanceSquared = DistanceSquared;
					ClosestPoint = Candidate;
				}
			}

			if (ClosestDistanceSquared < QueryDistanceTriggerThresholdSquared)
			{
				if (ClosestDistanceSquared < SplineOverlapResult.GetDistanceSquared())
				{
					const float SquaredDistToPin1 = (Params.AssociatedPin1 != nullptr) ? (Start - ClosestPoint).SizeSquared() : FLT_MAX;
					const float SquaredDistToPin2 = (Params.AssociatedPin2 != nullptr) ? (End - ClosestPoint).SizeSquared() : FLT_MAX;

					SplineOverlapResult = FGraphSplineOverlapResult(Params.AssociatedPin1, Params.AssociatedPin2, ClosestDistanceSquared, SquaredDistToPin1, SquaredDistToPin2, true);
				}
			}
			else if (ClosestDistanceSquared < QueryDistanceForCloseSquared)
			{
				SplineOverlapResult.SetCloseToSpline(true);
			}
		}
	}

	// Draw the polyline; points are expressed relative to the bounding-box
	// corner so the legacy paint geometry maps them back to draw space 1:1.
	FVector2D MinPoint = Points[0];
	FVector2D MaxPoint = Points[0];
	for (const FVector2D& Point : Points)
	{
		MinPoint = FVector2D(FMath::Min(MinPoint.X, Point.X), FMath::Min(MinPoint.Y, Point.Y));
		MaxPoint = FVector2D(FMath::Max(MaxPoint.X, Point.X), FMath::Max(MaxPoint.Y, Point.Y));
	}

	TArray<FVector2D> LocalPoints;
	LocalPoints.Reserve(Points.Num());
	for (const FVector2D& Point : Points)
	{
		LocalPoints.Add(Point - MinPoint);
	}

	FSlateDrawElement::MakeLines(
		DrawElementsList,
		LayerId,
		FPaintGeometry(MinPoint, MaxPoint - MinPoint, 1.0f),
		LocalPoints,
		ESlateDrawEffect::None,
		Params.WireColor,
		true,
		Params.WireThickness
	);
}

// ============================================================
// FForgeMindMaterialConnectionDrawingPolicy
// ============================================================

FForgeMindMaterialConnectionDrawingPolicy::FForgeMindMaterialConnectionDrawingPolicy(int32 InBackLayerID, int32 InFrontLayerID, float ZoomFactor, const FSlateRect& InClippingRect, FSlateWindowElementList& InDrawElements, UEdGraph* InGraphObj)
	: FConnectionDrawingPolicy(InBackLayerID, InFrontLayerID, ZoomFactor, InClippingRect, InDrawElements)
	, MaterialGraph(CastChecked<UMaterialGraph>(InGraphObj))
	, MaterialGraphSchema(CastChecked<UMaterialGraphSchema>(InGraphObj->GetSchema()))
{
	// Don't want to draw ending arrowheads (same as FMaterialGraphConnectionDrawingPolicy)
	ArrowImage = nullptr;
	ArrowRadius = FVector2D::ZeroVector;

	// Still need to be able to perceive the graph while dragging connectors,
	// esp over comment boxes (same as FMaterialGraphConnectionDrawingPolicy)
	HoverDeemphasisDarkFraction = 0.4f;
}

bool FForgeMindMaterialConnectionDrawingPolicy::FindPinCenter(UEdGraphPin* Pin, FVector2D& OutCenter) const
{
	if (const TSharedPtr<SGraphPin>* pPinWidget = PinToPinWidgetMap.Find(Pin))
	{
		if (FArrangedWidget* pPinEntry = PinGeometries->Find((*pPinWidget).ToSharedRef()))
		{
			OutCenter = FGeometryHelper::CenterOf(pPinEntry->Geometry);
			return true;
		}
	}
	return false;
}

bool FForgeMindMaterialConnectionDrawingPolicy::GetAverageConnectedPosition(UMaterialGraphNode_Knot* Knot, EEdGraphPinDirection Direction, FVector2D& OutPos) const
{
	FVector2D Result = FVector2D::ZeroVector;
	int32 ResultCount = 0;

	UEdGraphPin* Pin = (Direction == EGPD_Input) ? Knot->GetInputPin() : Knot->GetOutputPin();
	for (UEdGraphPin* LinkedPin : Pin->LinkedTo)
	{
		FVector2D CenterPoint;
		if (FindPinCenter(LinkedPin, /*out*/ CenterPoint))
		{
			Result += CenterPoint;
			ResultCount++;
		}
	}

	if (ResultCount > 0)
	{
		OutPos = Result * (1.0f / static_cast<float>(ResultCount));
		return true;
	}
	return false;
}

bool FForgeMindMaterialConnectionDrawingPolicy::ShouldChangeTangentForKnot(UMaterialGraphNode_Knot* Knot)
{
	if (bool* pResult = KnotToReversedDirectionMap.Find(Knot))
	{
		return *pResult;
	}

	bool bPinReversed = false;

	FVector2D AverageLeftPin;
	FVector2D AverageRightPin;
	FVector2D CenterPin;
	bool bCenterValid = FindPinCenter(Knot->GetOutputPin(), /*out*/ CenterPin);
	bool bLeftValid = GetAverageConnectedPosition(Knot, EGPD_Input, /*out*/ AverageLeftPin);
	bool bRightValid = GetAverageConnectedPosition(Knot, EGPD_Output, /*out*/ AverageRightPin);

	if (bLeftValid && bRightValid)
	{
		bPinReversed = AverageRightPin.X < AverageLeftPin.X;
	}
	else if (bCenterValid)
	{
		if (bLeftValid)
		{
			bPinReversed = CenterPin.X < AverageLeftPin.X;
		}
		else if (bRightValid)
		{
			bPinReversed = AverageRightPin.X < CenterPin.X;
		}
	}

	KnotToReversedDirectionMap.Add(Knot, bPinReversed);
	return bPinReversed;
}

void FForgeMindMaterialConnectionDrawingPolicy::DetermineWiringStyle(UEdGraphPin* OutputPin, UEdGraphPin* InputPin, /*inout*/ FConnectionParams& Params)
{
	// Re-implements FMaterialGraphConnectionDrawingPolicy::DetermineWiringStyle
	// using only public APIs so the ForgeMind wire style can coexist with
	// material-specific wire colors, Substrate, knot tangents, and hover
	// deemphasis.
	Params.AssociatedPin1 = OutputPin;
	Params.AssociatedPin2 = InputPin;
	Params.WireColor = MaterialGraphSchema->ActivePinColor;

	bool bInactivePin = false;
	bool bExecPin = false;
	bool bValueTypePin = false;

	if (OutputPin)
	{
		if (!MaterialGraph->IsInputActive(OutputPin))
		{
			bInactivePin = true;
		}

		if (OutputPin->PinType.PinCategory == UMaterialGraphSchema::PC_Exec)
		{
			bExecPin = true;
		}

		UEdGraphNode* OutputNode = OutputPin->GetOwningNode();
		if (UMaterialGraphNode_Knot* OutputKnotNode = Cast<UMaterialGraphNode_Knot>(OutputNode))
		{
			if (ShouldChangeTangentForKnot(OutputKnotNode))
			{
				Params.StartDirection = EGPD_Input;
			}
		}
		else if (!OutputNode->IsNodeEnabled() || OutputNode->IsDisplayAsDisabledForced() || OutputNode->IsNodeUnrelated())
		{
			bInactivePin = true;
		}
	}
	if (InputPin)
	{
		if (!MaterialGraph->IsInputActive(InputPin))
		{
			bInactivePin = true;
		}

		if (InputPin->PinType.PinCategory == UMaterialGraphSchema::PC_Exec)
		{
			bExecPin = true;
		}

		UEdGraphNode* InputNode = InputPin->GetOwningNode();
		if (UMaterialGraphNode_Knot* InputKnotNode = Cast<UMaterialGraphNode_Knot>(InputNode))
		{
			if (ShouldChangeTangentForKnot(InputKnotNode))
			{
				Params.EndDirection = EGPD_Output;
			}
		}
		else if (!InputNode->IsNodeEnabled() || InputNode->IsDisplayAsDisabledForced() || InputNode->IsNodeUnrelated())
		{
			bInactivePin = true;
		}
	}

	if (bInactivePin)
	{
		Params.WireColor = MaterialGraphSchema->InactivePinColor;
	}
	else if (bExecPin)
	{
		Params.WireColor = Settings->ExecutionPinTypeColor;
		Params.WireThickness = Settings->DefaultExecutionWireThickness;
	}
	else if (bValueTypePin)
	{
		if (InputPin)
		{
			Params.WireColor = MaterialGraphSchema->GetPinTypeColor(InputPin->PinType);
		}
	}

	const bool bDeemphasizeUnhoveredPins = HoveredPins.Num() > 0;
	if (bDeemphasizeUnhoveredPins)
	{
		ApplyHoverDeemphasis(OutputPin, InputPin, /*inout*/ Params.WireThickness, /*inout*/ Params.WireColor);
	}
}

void FForgeMindMaterialConnectionDrawingPolicy::DrawConnection(int32 LayerId, const FVector2D& Start, const FVector2D& End, const FConnectionParams& Params)
{
	// Curve keeps the full engine behavior (material spline, hover, knots).
	if (GetDefault<UForgeMindWireStyleSettings>()->WireStyle == EForgeMindWireStyle::Curve)
	{
		FConnectionDrawingPolicy::DrawConnection(LayerId, Start, End, Params);
		return;
	}

	// Reuse the same geometry + hover hit-testing as the blueprint policy.
	// Inline the logic here since DrawStyledConnection is protected on
	// FForgeMindConnectionDrawingPolicy and we inherit from FConnectionDrawingPolicy.
	const EForgeMindWireStyle WireStyle = GetDefault<UForgeMindWireStyleSettings>()->WireStyle;

	TArray<FVector2D> Points;
	if (WireStyle == EForgeMindWireStyle::Circuit && End.X - Start.X >= FMath::Abs(End.Y - Start.Y))
	{
		const float Dy = End.Y - Start.Y;
		const float Run = ((End.X - Start.X) - FMath::Abs(Dy)) * 0.5f;
		Points.Add(Start);
		Points.Add(FVector2D(Start.X + Run, Start.Y));
		Points.Add(FVector2D(End.X - Run, End.Y));
		Points.Add(End);
	}
	else if (WireStyle == EForgeMindWireStyle::Elbow || WireStyle == EForgeMindWireStyle::Circuit)
	{
		const float MidX = (Start.X + End.X) * 0.5f;
		Points.Add(Start);
		Points.Add(FVector2D(MidX, Start.Y));
		Points.Add(FVector2D(MidX, End.Y));
		Points.Add(End);
	}
	else
	{
		Points.Add(Start);
		Points.Add(End);
	}

	// Hover hit-testing (same as FForgeMindConnectionDrawingPolicy).
	if (Settings->bTreatSplinesLikePins)
	{
		const float QueryDistanceTriggerThresholdSquared = FMath::Square(Settings->SplineHoverTolerance + Params.WireThickness * 0.5f);
		const float QueryDistanceForCloseSquared = FMath::Square(FMath::Sqrt(QueryDistanceTriggerThresholdSquared) + Settings->SplineCloseTolerance);

		FBox2D Bounds(ForceInit);
		for (const FVector2D& Point : Points)
		{
			Bounds += Point;
		}

		if (Bounds.ComputeSquaredDistanceToPoint(LocalMousePosition) < QueryDistanceForCloseSquared)
		{
			FVector2D ClosestPoint(ForceInit);
			float ClosestDistanceSquared = FLT_MAX;

			for (int32 Index = 0; Index < Points.Num() - 1; ++Index)
			{
				const FVector2D Candidate = FMath::ClosestPointOnSegment2D(LocalMousePosition, Points[Index], Points[Index + 1]);
				const float DistanceSquared = (LocalMousePosition - Candidate).SizeSquared();
				if (DistanceSquared < ClosestDistanceSquared)
				{
					ClosestDistanceSquared = DistanceSquared;
					ClosestPoint = Candidate;
				}
			}

			if (ClosestDistanceSquared < QueryDistanceTriggerThresholdSquared)
			{
				if (ClosestDistanceSquared < SplineOverlapResult.GetDistanceSquared())
				{
					const float SquaredDistToPin1 = (Params.AssociatedPin1 != nullptr) ? (Start - ClosestPoint).SizeSquared() : FLT_MAX;
					const float SquaredDistToPin2 = (Params.AssociatedPin2 != nullptr) ? (End - ClosestPoint).SizeSquared() : FLT_MAX;

					SplineOverlapResult = FGraphSplineOverlapResult(Params.AssociatedPin1, Params.AssociatedPin2, ClosestDistanceSquared, SquaredDistToPin1, SquaredDistToPin2, true);
				}
			}
			else if (ClosestDistanceSquared < QueryDistanceForCloseSquared)
			{
				SplineOverlapResult.SetCloseToSpline(true);
			}
		}
	}

	FVector2D MinPoint = Points[0];
	FVector2D MaxPoint = Points[0];
	for (const FVector2D& Point : Points)
	{
		MinPoint = FVector2D(FMath::Min(MinPoint.X, Point.X), FMath::Min(MinPoint.Y, Point.Y));
		MaxPoint = FVector2D(FMath::Max(MaxPoint.X, Point.X), FMath::Max(MaxPoint.Y, Point.Y));
	}

	TArray<FVector2D> LocalPoints;
	LocalPoints.Reserve(Points.Num());
	for (const FVector2D& Point : Points)
	{
		LocalPoints.Add(Point - MinPoint);
	}

	FSlateDrawElement::MakeLines(
		DrawElementsList,
		LayerId,
		FPaintGeometry(MinPoint, MaxPoint - MinPoint, 1.0f),
		LocalPoints,
		ESlateDrawEffect::None,
		Params.WireColor,
		true,
		Params.WireThickness
	);
}

// ============================================================
// FForgeMindPinConnectionFactory
// ============================================================

FConnectionDrawingPolicy* FForgeMindPinConnectionFactory::CreateConnectionPolicy(const UEdGraphSchema* Schema, int32 InBackLayerID, int32 InFrontLayerID, float ZoomFactor, const FSlateRect& InClippingRect, FSlateWindowElementList& InDrawElements, UEdGraph* InGraphObj) const
{
	if (Schema == nullptr)
	{
		return nullptr;
	}

	UE_LOG(LogTemp, Warning, TEXT("FForgeMind factory: schema=%s graph=%s"),
		*Schema->GetClass()->GetName(),
		InGraphObj ? *InGraphObj->GetClass()->GetName() : TEXT("null"));

	// Material graphs: use the material-specific policy so DetermineWiringStyle
	// (wire colors, Substrate, inactive pins, knot tangents) is preserved
	// while the ForgeMind DrawConnection override applies the chosen geometry.
	if (Schema->IsA(UMaterialGraphSchema::StaticClass()) && InGraphObj != nullptr)
	{
		return new FForgeMindMaterialConnectionDrawingPolicy(InBackLayerID, InFrontLayerID, ZoomFactor, InClippingRect, InDrawElements, InGraphObj);
	}

	// All other graph types (blueprint, animation, sound cue, behavior tree, ...).
	return new FForgeMindConnectionDrawingPolicy(InBackLayerID, InFrontLayerID, ZoomFactor, InClippingRect, InDrawElements, InGraphObj);
}