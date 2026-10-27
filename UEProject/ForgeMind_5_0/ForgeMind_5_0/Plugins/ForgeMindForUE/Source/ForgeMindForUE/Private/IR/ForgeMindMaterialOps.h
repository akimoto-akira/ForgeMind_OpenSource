// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Shared material-expression operations.
//
// Two channels build and read material graphs through the Companion:
//   * the Asset IR material handler (apply_asset_ir / get_asset_ir), and
//   * the material step commands (create_material / add_material_expression /
//     connect_material_pins / compile_material) that mirror the MCP one-command
//     flow of the requirement docs.
// Both must end with byte-identical editor state or the cross-method IR
// comparison of a task doc's A/B asset pair reports a difference that is not a
// behaviour difference but an implementation difference. That is why expression
// creation, expression-list registration, pin resolution, value writing and
// constant wiring live here once instead of in each channel.
//
// UE 5.0 specifics this layer hides:
//   * the expression list sits on UMaterial as "Expressions" (there is no
//     UMaterialEditorOnlyData yet - that shape only arrives in later engine
//     versions, so every lookup falls back from the editor-only data object to
//     the asset itself),
//   * the material output pins are not UPROPERTY struct members reachable by
//     name; they are F*MaterialInput members read through
//     UMaterial::GetExpressionInputForProperty(EMaterialProperty).

#pragma once

#include "CoreMinimal.h"
#include "Dom/JsonValue.h"

class UObject;
class UClass;
class UMaterialExpression;
struct FExpressionInput;

namespace ForgeMindMaterial
{
	/** "TextureSample" -> "MaterialExpressionTextureSample"; already-prefixed names pass through. */
	FString ExpressionClassName(const FString& Type);

	/** Editor-only data object of a material (null on engine versions that keep everything on UMaterial). */
	UObject* GetEditorOnlyData(UObject* Asset);

	/** Ordered expression list of a material / material function, as it is stored on the asset. */
	void GetExpressions(UObject* Asset, TArray<UMaterialExpression*>& OutExpressions);

	/** True when the asset carries an expression list at all (an empty graph still counts). */
	bool HasExpressionList(UObject* Asset);

	/** Append an expression to the asset's expression list; a no-op when it is already listed. */
	bool AddExpression(UObject* Asset, UMaterialExpression* Expression);

	/**
	 * Create an expression of Class outerred to Asset, link it to the owning
	 * material and register it in the expression list. When ParameterName is
	 * non-empty the expression is treated as a parameter (ParameterName written,
	 * parameter GUID refreshed) exactly the way the Asset IR parameters[] route
	 * does, so both channels produce the same object.
	 */
	UMaterialExpression* CreateExpression(UObject* Asset, UClass* Class, const FString& ParameterName);

	/** Look an expression up by the object name CreateExpression reports. */
	UMaterialExpression* FindExpression(UObject* Asset, const FString& Id);

	/** FExpressionInput member of Owner matched case-insensitively by property name. */
	FExpressionInput* GetExpressionInput(UObject* Owner, const FString& Name);

	/**
	 * Material output pins (BaseColor / EmissiveColor / ...) resolved through EMaterialProperty,
	 * falling back to the plain struct-property lookup for material functions and later engine
	 * versions.
	 */
	FExpressionInput* GetMaterialOutputInput(UObject* Asset, const FString& Name);

	/**
	 * Every material output pin that currently has something wired, in engine property order
	 * and with the "MP_" prefix stripped from the name. The IR read side enumerates connected
	 * pins through this so the EMaterialProperty knowledge stays in one place.
	 */
	void GetConnectedMaterialOutputs(UObject* Asset, TArray<TPair<FString, FExpressionInput*>>& OutOutputs);

	/**
	 * Output index of a source pin name: "" / Out / RGB / RGBA / Result / Color -> 0,
	 * R / G / B / A -> 1 / 2 / 3 / 4. The DSL side pins its swizzles to the same
	 * numbers, so the two channels wire the same output.
	 */
	int32 ResolveOutputIndex(const FString& PinName);

	/** Same as ResolveOutputIndex but tells the caller when the name is not a known pin. */
	bool TryResolveOutputIndex(const FString& PinName, int32& OutIndex);

	/**
	 * Write one UPROPERTY from JSON. Numbers, booleans and strings go through the
	 * editor import path; a color-valued property additionally accepts the numeric
	 * array form ([r,g,b] / [r,g,b,a]) and the "(R=..,G=..)" text form, enum
	 * properties accept the bare display name ("Surface" for MD_Surface,
	 * "Opaque" for BLEND_Opaque) and object properties a project asset path.
	 * Unknown shapes fall back to the shared reflection importer.
	 */
	bool SetValue(UObject* Object, const FString& PropertyName, const TSharedPtr<FJsonValue>& Value, FString& OutError);

	/** Read one UPROPERTY back as JSON; colors are emitted as [r,g,b,a] so both channels compare equal. */
	TSharedPtr<FJsonValue> GetValue(UObject* Object, const FString& PropertyName);

	/** Parse "(R=1,G=0,B=0)", "[1,0,0]", "0.7", "1, 0, 0" into the numbers they carry. */
	bool ParseNumericList(const FString& Text, TArray<double>& OutValues);

	/**
	 * Create the constant expression that carries Value: a single number becomes
	 * MaterialExpressionConstant, three components MaterialExpressionConstant3Vector,
	 * four components MaterialExpressionConstant4Vector.
	 */
	UMaterialExpression* CreateConstantExpression(UObject* Asset, const TSharedPtr<FJsonValue>& Value, FString& OutError);

	/** Connect a constant to an input, creating the constant expression next to it. */
	bool ConnectConstant(FExpressionInput* Input, UObject* Asset, const TSharedPtr<FJsonValue>& Value, FString& OutError);

	/**
	 * Connect Source's output into Input. The default output (index 0) connects
	 * directly. A channel request (R/G/B/A = 1..4, what the swizzle tables map
	 * to) is realized the way the editor builds it: a ComponentMask node
	 * extracting exactly that channel is inserted between Source's default
	 * output and the target input - the engine's ConnectExpression silently
	 * drops any index beyond the source's declared outputs (the pin stays
	 * unwired), and where an output does exist at that index its name does not
	 * mean the requested channel. Asset owns the inserted node.
	 */
	bool ConnectOutputInput(UObject* Asset, FExpressionInput* Input, UMaterialExpression* Source, int32 OutputIndex, FString& OutError);

	/** Load a material-family asset from a project path ("/Game/Tests/M_X"). */
	UObject* ResolveMaterial(const FString& Path);

	/**
	 * Tidy the asset's expression graph. Every channel that mutates a material graph calls
	 * this once when it is done - the material step commands as well as the Asset IR
	 * handler - which is the same trigger the blueprint graph commands use for
	 * FForgeMindGraphLayout::ArrangeGraph, so an asset assembled one command at a time and
	 * one assembled from an IR definition both open laid out instead of stacked at 0,0.
	 */
	void ArrangeLayout(UObject* Asset);

	/** Class short name without the U prefix, e.g. "UMaterialExpressionIf" -> "MaterialExpressionIf". */
	FString ExpressionTypeName(const UMaterialExpression* Expression);
}
