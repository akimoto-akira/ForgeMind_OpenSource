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
class UMaterial;
class UMaterialExpression;
struct FExpressionInput;
struct FExpressionExecOutput;

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

	/**
	 * FExpressionInput of Owner matched case-insensitively by name: the member of that
	 * name for a static input, and - on a MaterialExpressionMaterialFunctionCall, whose
	 * inputs are the per-function entries of FunctionInputs rather than members - the
	 * entry mirroring a function input of that name.
	 */
	FExpressionInput* GetExpressionInput(UObject* Owner, const FString& Name);

	/** Locate execution output by engine-declared property name, handled separately from data inputs. */
	FExpressionExecOutput* GetExecutionOutput(UMaterialExpression* Owner, const FString& Name);

	/** Shared by both construction channels; maintains execution in-degree and rejects cross-material connections and execution cycles. */
	bool ConnectExecutionOutput(UMaterialExpression* Source, const FString& Name, UMaterialExpression* Target, FString& OutError);

	/**
	 * Material output pins (BaseColor / EmissiveColor / ...) resolved through EMaterialProperty,
	 * falling back to the plain struct-property lookup for material functions and later engine
	 * versions.
	 */
	FExpressionInput* GetMaterialOutputInput(UObject* Asset, const FString& Name);

	/**
	 * False when the named material output is one the engine will not read for this material's
	 * current domain / blend mode / shader frequency. UMaterial::IsPropertyActive is the engine's
	 * own answer (IsPropertyActive_Internal, Material.cpp:5286-5289) and it is what turned a
	 * post-process material wired to Opacity into a silently dead wire: every node, step flag and
	 * IR comparison still passed while the shader never looked at that output.
	 * Materials compiled with the execution flow are exempt: their properties are emitted from
	 * the exec chain, so the per-property active mask does not describe them.
	 */
	bool IsMaterialOutputActive(UObject* Asset, const FString& Name);

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
	 * Resolve a pin name against the outputs the source expression actually declares,
	 * by output name, ignoring case. Single-letter channel names (R/G/B/A) are excluded:
	 * they are channel-extraction requests and keep going through the swizzle table's
	 * ComponentMask path. Returns false when the source declares no output with that
	 * name, leaving the caller to fall back to TryResolveOutputIndex.
	 *
	 * This is what makes "RGBA" reach a TextureSample's real fourth-channel output
	 * (engine output list: RGB=0, R=1, G=2, B=3, A=4, RGBA=5) instead of output 0:
	 * output 0 is RGB - a float3 - and wiring a float4-requested pin to it is exactly
	 * how a "Cannot cast from smaller type float3 to larger type float4" compile error
	 * happens while every connect step still reports success.
	 */
	bool TryResolveNamedOutputIndex(UMaterialExpression* Source, const FString& PinName, int32& OutIndex);

	/**
	 * Write one UPROPERTY from JSON. Numbers, booleans and strings go through the
	 * editor import path; a color-valued property additionally accepts the numeric
	 * array form ([r,g,b] / [r,g,b,a]) and the "(R=..,G=..)" text form, enum
	 * properties accept the bare display name ("Surface" for MD_Surface,
	 * "Opaque" for BLEND_Opaque) and object properties a project asset path.
	 * Unknown shapes fall back to the shared reflection importer.
	 */
	bool SetValue(UObject* Object, const FString& PropertyName, const TSharedPtr<FJsonValue>& Value, FString& OutError);

	/**
	 * Apply one material-settings key to a material, falling back to the editor-only
	 * data object (where later engine versions moved some settings) when the material
	 * itself does not carry the property. Single shared entry for the IR channel's
	 * ApplyMaterialSettings and the create_material step command's extra payload keys,
	 * so a key one channel accepts cannot be silently dropped by the other.
	 */
	bool ApplyMaterialSetting(UMaterial* Material, const FString& Key, const TSharedPtr<FJsonValue>& Value, FString& OutError);

	/** Read one UPROPERTY back as JSON; colors are emitted as [r,g,b,a] so both channels compare equal. */
	TSharedPtr<FJsonValue> GetValue(UObject* Object, const FString& PropertyName);

	/**
	 * True when a struct property is stored as an FLinearColor and has to be written
	 * component-wise. Identified by the reflected struct name, because UE 5.0.3 gives
	 * FLinearColor no usable StaticStruct(). Shared so the step commands and the IR channel
	 * cannot disagree about what counts as a colour.
	 */
	bool IsLinearColorStruct(const UScriptStruct* Struct);

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
	 * An index above 4 can only be a named-output request (the channel swizzles
	 * end at 4) - e.g. a TextureSample's "RGBA" at index 5 - and connects
	 * directly when the source declares that many outputs, so the wire lands on
	 * the engine's own named pin instead of a mask reconstruction.
	 *
	 * bNamedOutput marks a request that was resolved against the outputs the
	 * source actually declares (a pin name match, e.g. DecalDerivative's "DDY"
	 * at index 1). Those indexes are real pins, not channels, so they connect
	 * directly even inside the 1..4 range and never get a mask reconstruction;
	 * an index the source does not declare is an error rather than a guess.
	 * Numeric swizzle requests keep bNamedOutput false, which is what preserves
	 * the channel-extraction behaviour every existing graph was built with.
	 */
	bool ConnectOutputInput(UObject* Asset, FExpressionInput* Input, UMaterialExpression* Source, int32 OutputIndex, FString& OutError, bool bNamedOutput = false);

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
