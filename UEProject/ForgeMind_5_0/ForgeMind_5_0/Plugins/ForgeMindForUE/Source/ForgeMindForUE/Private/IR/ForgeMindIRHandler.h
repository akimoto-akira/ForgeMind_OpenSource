// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Asset IR Handler framework (IR rules.md section 2).
//
// The C# translator layer emits a versioned "Asset IR" document
// ({ version, batch, assets:[{ version, assetType, path, mode, compile,
// save, definition|patches }] }). This framework is the Companion-side
// counterpart: a registry of IIRHandler implementations, one per DSL
// domain, plus the shell handling (version check, assetType routing,
// path validation, create/update/delete dispatch, result reporting)
// that section 2 / section 3 / section 4 / section 5 / section 8 of IR rules.md mandate.

#pragma once

#include "CoreMinimal.h"
#include "Dom/JsonObject.h"
#include "Dom/JsonValue.h"

class UObject;
class UClass;

// ---------------------------------------------------------------------------
// Context / result carrier
// ---------------------------------------------------------------------------

/** Non-fatal diagnostic attached to an apply result. */
struct FIRWarning
{
	FString Code;
	FString Message;
};

/**
 * Everything a handler needs to apply or read one asset IR, plus the
 * output slots it fills in. Handlers never talk JSON envelopes — the
 * registry owns those.
 */
struct FIRApplyContext
{
	// ---- request ----
	FString AssetType;
	FString Path;
	FString Mode;              // create | update | delete | read
	FString Level;             // summary | graph | full  (IR detail level, default: summary)
	bool bCompile = true;
	bool bSave = false;
	bool bOverwrite = false;

	TSharedPtr<FJsonObject> Definition;
	TArray<TSharedPtr<FJsonValue>> Patches;

	// ---- response ----
	TArray<TSharedPtr<FJsonValue>> PatchResults;
	TArray<FIRWarning> Warnings;
	FString ErrorCode;
	FString Error;
	double CompileTimeMs = 0.0;

	/** Extra domain specific fields merged into the per-asset result. */
	TSharedPtr<FJsonObject> Extra = MakeShared<FJsonObject>();

	bool HasError() const { return !Error.IsEmpty(); }

	void Warn(const FString& Code, const FString& Message)
	{
		Warnings.Add(FIRWarning{ Code, Message });
	}

	void Fail(const FString& Code, const FString& Message)
	{
		if (Error.IsEmpty())
		{
			ErrorCode = Code;
			Error = Message;
		}
	}

	/**
	 * Records one patch outcome. Per IR rules.md section 4.6 the Companion reports
	 * the op it actually inferred so the AI can confirm its diff.
	 */
	void AddPatchResult(const FString& ActualOp, const FString& PatchType, const FString& Name,
		bool bOk, const FString& Message = FString())
	{
		TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
		Entry->SetStringField(TEXT("actualOp"), ActualOp);
		Entry->SetStringField(TEXT("type"), PatchType);
		if (!Name.IsEmpty())
		{
			Entry->SetStringField(TEXT("name"), Name);
		}
		Entry->SetBoolField(TEXT("success"), bOk);
		if (!Message.IsEmpty())
		{
			Entry->SetStringField(TEXT("message"), Message);
		}
		PatchResults.Add(MakeShared<FJsonValueObject>(Entry));
	}
};

// ---------------------------------------------------------------------------
// Handler interface
// ---------------------------------------------------------------------------

/**
 * One implementation per DSL domain. Handlers are stateless and
 * registry-owned; everything mutable travels through FIRApplyContext.
 */
class IIRHandler
{
public:
	virtual ~IIRHandler() = default;

	/** assetType strings (snake_case) this handler claims. */
	virtual void GetAssetTypes(TArray<FString>& OutAssetTypes) const = 0;

	/**
	 * Creates the asset at Ctx.Path from Ctx.Definition.
	 * Returns the new object, or nullptr after calling Ctx.Fail().
	 */
	virtual UObject* Create(FIRApplyContext& Ctx) = 0;

	/** Applies Ctx.Patches onto an existing asset. Returns false on error. */
	virtual bool Update(UObject* Asset, FIRApplyContext& Ctx) = 0;

	/** Removes the asset at Ctx.Path (disk files included, IR rules.md section 5.3). */
	virtual bool DeleteAsset(FIRApplyContext& Ctx) = 0;

	/** Serializes an existing asset back into a definition object (mode=read). */
	virtual TSharedPtr<FJsonObject> Read(UObject* Asset, FIRApplyContext& Ctx) = 0;

	/** Domain specific "compile". Default implementation is a no-op + note. */
	virtual bool CompileAsset(UObject* Asset, FIRApplyContext& Ctx);
};

// ---------------------------------------------------------------------------
// Handler factories (one per translation unit under Private/IR)
// ---------------------------------------------------------------------------

TSharedRef<IIRHandler> MakeBlueprintIRHandler();      // blueprint / widget_blueprint
TSharedRef<IIRHandler> MakeWidgetIRHandler();         // widget_blueprint / widget_animation
TSharedRef<IIRHandler> MakeMaterialIRHandler();       // material / material_function / material_instance_constant
TSharedRef<IIRHandler> MakeAnimBlueprintIRHandler();  // anim_blueprint
TSharedRef<IIRHandler> MakeBehaviorTreeIRHandler();   // behavior_tree / blackboard
TSharedRef<IIRHandler> MakeNiagaraIRHandler();        // niagara_system / niagara_emitter
TSharedRef<IIRHandler> MakePhysicsIRHandler();        // physics_asset
TSharedRef<IIRHandler> MakeChaosIRHandler();          // geometry_collection / chaos_* / physics_field
TSharedRef<IIRHandler> MakeLandscapeIRHandler();      // landscape
TSharedRef<IIRHandler> MakeFoliageIRHandler();        // foliage_type*
TSharedRef<IIRHandler> MakeGroomIRHandler();          // groom_asset / groom_binding_asset / groom_cache
TSharedRef<IIRHandler> MakePaper2DIRHandler();        // paper_sprite / paper_flipbook / paper_tile_map
TSharedRef<IIRHandler> MakeSequencerIRHandler();      // level_sequence / movie_scene_sequence

// ---------------------------------------------------------------------------
// Shared helpers used by every handler
// ---------------------------------------------------------------------------

namespace ForgeMindIR
{
	/** "5.6" vs engine 5.6 -- IR rules.md section 2.1. */
	bool CheckVersion(const FString& Version, FString& OutError);

	/** IR rules.md section 2.3. */
	bool IsValidAssetPath(const FString& Path, FString& OutError);

	/** Strips a trailing ".Name" duplicate and normalizes separators. */
	FString NormalizePath(const FString& Path);

	/** Package path (everything before the last '/'). */
	FString PackagePathOf(const FString& Path);

	/** Asset name (last segment). */
	FString AssetNameOf(const FString& Path);

	bool AssetExists(const FString& Path);
	UObject* LoadAsset(const FString& Path);

	/**
	 * Creates a package + asset instance without needing a domain factory.
	 * Used by domains whose factories live in editor modules this plugin
	 * does not link; the class is resolved by name at runtime instead.
	 */
	UObject* CreateAssetInstance(UClass* AssetClass, const FString& Path, bool bOverwrite);

	/** Creates the asset through a UFactory, when one is available. */
	UObject* CreateAssetWithFactory(UFactory* Factory, UClass* AssetClass, const FString& Path);

	/**
	 * Resolves a UClass by short name, tolerating the U/A/F prefix and
	 * "MaterialExpressionFoo"/"Foo" style spellings. Returns nullptr when
	 * the class belongs to a module that is not loaded.
	 */
	UClass* ResolveClass(const FString& Name);

	/** Loads an asset from a path that may omit the ".ObjectName" suffix. */
	UObject* ResolveObjectPath(const FString& Path, UClass* ExpectedClass = nullptr);

	bool SaveAssetObject(UObject* Asset);
	bool DeleteAssetObject(const FString& Path, FString& OutError);
	int32 CountReferencers(const FString& Path);

	// ---- reflection plumbing ----

	/** Case-insensitive UPROPERTY lookup on an instance's class. */
	FProperty* FindProperty(UObject* Object, const FString& Name);
	FProperty* FindPropertyOnStruct(const UStruct* Struct, const FString& Name);

	/** JSON → UE import text ("true", "12.5", "(X=1,Y=2,Z=3)"). */
	FString JsonValueToImportText(const TSharedPtr<FJsonValue>& Value);

	/** Writes one UPROPERTY through ImportText_Direct (Details-panel path). */
	bool SetProperty(UObject* Object, const FString& PropertyName, const TSharedPtr<FJsonValue>& Value, FString& OutError);

	/** Bulk setter; unknown keys are collected as warnings, not errors. */
	void SetProperties(UObject* Object, const TSharedPtr<FJsonObject>& Values, TArray<FIRWarning>& OutWarnings);

	/** Reads one UPROPERTY back into JSON. */
	TSharedPtr<FJsonValue> GetProperty(UObject* Object, const FString& PropertyName);

	/** Serializes all UPROPERTYs whose names appear in Names. */
	TSharedPtr<FJsonObject> GetProperties(UObject* Object, const TArray<FString>& Names);

	// ---- array helpers used by domain patch appliers ----

	/**
	 * Finds an element in an object array by a string field value.
	 * OutIndex receives the match position.
	 */
	template <typename Predicate>
	int32 IndexOf(const TArray<TSharedPtr<FJsonValue>>& Array, Predicate Pred)
	{
		for (int32 i = 0; i < Array.Num(); ++i)
		{
			const TSharedPtr<FJsonObject> Obj = Array[i]->AsObject();
			if (Obj.IsValid() && Pred(Obj))
			{
				return i;
			}
		}
		return INDEX_NONE;
	}

	/** First non-empty string among several candidate keys. */
	FString FirstString(const TSharedPtr<FJsonObject>& Obj, const TArray<FString>& Keys);

	/** IR rules.md patch delete flag: "delete": true. */
	bool IsDeletePatch(const TSharedPtr<FJsonObject>& Patch);

	/** Marks the asset modified and (optionally) persists it. */
	void FinalizeAsset(UObject* Asset, bool bSave);
}

// ---------------------------------------------------------------------------
// Registry
// ---------------------------------------------------------------------------

class FForgeMindIRRegistry
{
public:
	static FForgeMindIRRegistry& Get();

	/** Registers every built-in domain handler (idempotent). */
	void RegisterBuiltinHandlers();

	void Register(const TSharedRef<IIRHandler>& Handler);

	IIRHandler* Find(const FString& AssetType) const;

	TArray<FString> SupportedAssetTypes() const;

	/** True when the payload's assets[] carry the new snake_case assetType key. */
	static bool IsNewFormat(const TSharedPtr<FJsonObject>& Payload);

	/** Entry point for apply_asset_ir — handles both batch and single IR. */
	TSharedPtr<FJsonObject> ApplyBatch(const TSharedPtr<FJsonObject>& Payload);

	/** Applies exactly one asset IR object. */
	TSharedPtr<FJsonObject> ApplySingle(const TSharedPtr<FJsonObject>& AssetIR, const FString& FallbackVersion);

	/** Entry point for get_asset_ir (mode = "read", IR rules.md section 1.4). */
	TSharedPtr<FJsonObject> ReadIR(const TSharedPtr<FJsonObject>& Payload);

private:
	TSharedPtr<FJsonObject> BuildAssetResult(const FString& AssetType, const FString& Path,
		const FString& Mode, const FIRApplyContext& Ctx);

	TMap<FString, TSharedRef<IIRHandler>> Handlers;
	bool bBuiltinRegistered = false;
};