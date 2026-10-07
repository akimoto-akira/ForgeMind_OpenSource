// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// IR shell handling: version check, assetType routing, create/update/delete
// dispatch, compile/save post-processing and result reporting
// (IR rules.md section 2 / section 3 / section 4 / section 5 / section 6 / section 8).

#include "ForgeMindIRHandler.h"

#include "AssetRegistry/AssetRegistryModule.h"
#include "Engine/Blueprint.h"
#include "Engine/UserDefinedEnum.h"
#include "HAL/PlatformTime.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Engine/UserDefinedStruct.h"
#include "Engine/World.h"
#include "UObject/Package.h"
#include "WidgetBlueprint.h"

DEFINE_LOG_CATEGORY_STATIC(LogForgeMindIR, Log, All);

namespace
{
	/** Reads a version that may be written as a string ("5.6") or a number. */
	FString ReadVersionField(const TSharedPtr<FJsonObject>& Obj)
	{
		if (!Obj.IsValid())
		{
			return FString();
		}
		FString StringVersion;
		if (Obj->TryGetStringField(TEXT("version"), StringVersion) && !StringVersion.IsEmpty())
		{
			return StringVersion;
		}
		double NumberVersion = 0.0;
		if (Obj->TryGetNumberField(TEXT("version"), NumberVersion))
		{
			return FString::Printf(TEXT("%g"), NumberVersion);
		}
		return FString();
	}

	void AttachWarnings(const TSharedPtr<FJsonObject>& Result, const TArray<FIRWarning>& Warnings)
	{
		if (Warnings.Num() == 0)
		{
			return;
		}
		TArray<TSharedPtr<FJsonValue>> Entries;
		for (const FIRWarning& Warning : Warnings)
		{
			TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
			Entry->SetStringField(TEXT("code"), Warning.Code);
			Entry->SetStringField(TEXT("message"), Warning.Message);
			Entries.Add(MakeShared<FJsonValueObject>(Entry));
		}
		Result->SetArrayField(TEXT("warnings"), Entries);
	}

	/** The C# layer may wrap the IR document inside a { "json": "..." } envelope. */
	TSharedPtr<FJsonObject> UnwrapPayload(const TSharedPtr<FJsonObject>& Payload)
	{
		if (!Payload.IsValid())
		{
			return nullptr;
		}
		if (Payload->HasField(TEXT("version")) || Payload->HasField(TEXT("assets")) || Payload->HasField(TEXT("assetType")))
		{
			return Payload;
		}
		FString JsonString;
		if (Payload->TryGetStringField(TEXT("json"), JsonString))
		{
			TSharedPtr<FJsonObject> Parsed;
			const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(JsonString);
			if (FJsonSerializer::Deserialize(Reader, Parsed) && Parsed.IsValid())
			{
				return Parsed;
			}
		}
		return Payload;
	}

	/**
	 * Fallback assetType identification from the UObject class, used by
	 * get_asset_ir when the caller did not state the domain.
	 */
	FString IdentifyAssetTypeFor(UObject* Asset)
	{
		if (Asset == nullptr)
		{
			return FString();
		}
		UClass* Class = Asset->GetClass();

		// Level asset: level blueprint is a level **embedded** asset (PersistentLevel.LevelScriptActor's
		// ClassGeneratedBy), not a standalone .uasset -- LoadAsset on level path returns UWorld or a
		// map-containing UPackage. Previously there was no level branch here -> returns empty ->
		// ReadIR walks to "AssetType.IsEmpty()" and increments Failed directly (**no error info**)
		// -> get_asset_ir always returns success=false for level assets, incorrectly triggering
		// VerifyIrSnapshot comparisons unusable for level test cases. Recognized as level_blueprint,
		// delegated to FBlueprintIRHandler::Read's LevelScriptActor fallback resolution.
		if (Class->IsChildOf(UWorld::StaticClass())) return TEXT("level_blueprint");
		if (UPackage* Package = Cast<UPackage>(Asset))
		{
			if (Package->ContainsMap()) return TEXT("level_blueprint");
		}

		if (Class->IsChildOf(UWidgetBlueprint::StaticClass())) return TEXT("widget_blueprint");
		if (Class->IsChildOf(UBlueprint::StaticClass()))       return TEXT("blueprint");
		if (Class->IsChildOf(UUserDefinedStruct::StaticClass())) return TEXT("user_defined_struct");
		if (Class->IsChildOf(UUserDefinedEnum::StaticClass()))  return TEXT("user_defined_enum");

		// Everything else: let the handlers claim it by class name so new
		// domains can be added without touching this table.
		static const TPair<FString, FString> NameMap[] = {
			TPair<FString, FString>(TEXT("MaterialInstanceConstant"), TEXT("material_instance_constant")),
			TPair<FString, FString>(TEXT("MaterialFunctionInstance"), TEXT("material_function_instance")),
			TPair<FString, FString>(TEXT("MaterialFunction"),        TEXT("material_function")),
			TPair<FString, FString>(TEXT("Material"),                TEXT("material")),
			TPair<FString, FString>(TEXT("AnimBlueprint"),           TEXT("anim_blueprint")),
			TPair<FString, FString>(TEXT("BehaviorTree"),            TEXT("behavior_tree")),
			TPair<FString, FString>(TEXT("BlackboardData"),          TEXT("blackboard")),
			TPair<FString, FString>(TEXT("NiagaraSystem"),           TEXT("niagara_system")),
			TPair<FString, FString>(TEXT("NiagaraEmitter"),          TEXT("niagara_emitter")),
			TPair<FString, FString>(TEXT("PhysicsAsset"),            TEXT("physics_asset")),
			TPair<FString, FString>(TEXT("GeometryCollection"),      TEXT("geometry_collection")),
			TPair<FString, FString>(TEXT("GroomBindingAsset"),       TEXT("groom_binding_asset")),
			TPair<FString, FString>(TEXT("GroomAsset"),              TEXT("groom_asset")),
			TPair<FString, FString>(TEXT("PaperFlipbook"),           TEXT("paper_flipbook")),
			TPair<FString, FString>(TEXT("PaperTileMap"),            TEXT("paper_tile_map")),
			TPair<FString, FString>(TEXT("PaperSprite"),             TEXT("paper_sprite")),
			TPair<FString, FString>(TEXT("LevelSequence"),           TEXT("level_sequence")),
			TPair<FString, FString>(TEXT("FoliageType"),             TEXT("foliage_type")),
			TPair<FString, FString>(TEXT("Landscape"),               TEXT("landscape")),
		};

		const FString ClassName = Class->GetName();
		for (const TPair<FString, FString>& Pair : NameMap)
		{
			if (ClassName.Contains(Pair.Key))
			{
				return Pair.Value;
			}
		}
		return FString();
	}
}

// ---------------------------------------------------------------------------
// Registry
// ---------------------------------------------------------------------------

FForgeMindIRRegistry& FForgeMindIRRegistry::Get()
{
	static FForgeMindIRRegistry Instance;
	return Instance;
}

void FForgeMindIRRegistry::Register(const TSharedRef<IIRHandler>& Handler)
{
	TArray<FString> Types;
	Handler->GetAssetTypes(Types);
	for (const FString& Type : Types)
	{
		Handlers.Add(Type, Handler);
	}
}

void FForgeMindIRRegistry::RegisterBuiltinHandlers()
{
	if (bBuiltinRegistered)
	{
		return;
	}
	bBuiltinRegistered = true;

	Register(MakeBlueprintIRHandler());
	Register(MakeWidgetIRHandler());
	Register(MakeMaterialIRHandler());
	Register(MakeAnimBlueprintIRHandler());
	Register(MakeBehaviorTreeIRHandler());
	Register(MakeNiagaraIRHandler());
	Register(MakePhysicsIRHandler());
	Register(MakeChaosIRHandler());
	Register(MakeLandscapeIRHandler());
	Register(MakeFoliageIRHandler());
	Register(MakeGroomIRHandler());
	Register(MakePaper2DIRHandler());
	Register(MakeSequencerIRHandler());

	UE_LOG(LogForgeMindIR, Log, TEXT("Registered %d asset IR handlers"), Handlers.Num());
}

IIRHandler* FForgeMindIRRegistry::Find(const FString& AssetType) const
{
	const TSharedRef<IIRHandler>* Found = Handlers.Find(AssetType);
	return Found != nullptr ? &Found->Get() : nullptr;
}

TArray<FString> FForgeMindIRRegistry::SupportedAssetTypes() const
{
	TArray<FString> Keys;
	Handlers.GetKeys(Keys);
	Keys.Sort();
	return Keys;
}

bool FForgeMindIRRegistry::IsNewFormat(const TSharedPtr<FJsonObject>& Payload)
{
	if (!Payload.IsValid())
	{
		return false;
	}
	// Batch form: any asset entry carrying the snake_case assetType key.
	const TArray<TSharedPtr<FJsonValue>>* Assets = nullptr;
	if (Payload->TryGetArrayField(TEXT("assets"), Assets) && Assets->Num() > 0)
	{
		for (const TSharedPtr<FJsonValue>& Value : *Assets)
		{
			const TSharedPtr<FJsonObject> Asset = Value->AsObject();
			if (Asset.IsValid() && Asset->HasField(TEXT("assetType")))
			{
				return true;
			}
		}
		return false;
	}
	// Single form: the payload itself carries assetType (snake_case).
	return Payload->HasField(TEXT("assetType"));
}

TSharedPtr<FJsonObject> FForgeMindIRRegistry::BuildAssetResult(const FString& AssetType, const FString& Path,
	const FString& Mode, const FIRApplyContext& Ctx)
{
	TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetBoolField(TEXT("success"), !Ctx.HasError());
	Result->SetStringField(TEXT("assetType"), AssetType);
	Result->SetStringField(TEXT("path"), Path);
	Result->SetStringField(TEXT("mode"), Mode);

	if (Ctx.HasError())
	{
		Result->SetStringField(TEXT("error"), Ctx.Error);
		Result->SetStringField(TEXT("errorCode"), Ctx.ErrorCode);
	}

	if (Ctx.PatchResults.Num() > 0)
	{
		Result->SetArrayField(TEXT("patchResults"), Ctx.PatchResults);
	}
	if (Mode != TEXT("delete"))
	{
		Result->SetNumberField(TEXT("compileTimeMs"), Ctx.CompileTimeMs);
	}
	AttachWarnings(Result, Ctx.Warnings);

	if (Ctx.Extra.IsValid())
	{
		for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Ctx.Extra->Values)
		{
			Result->SetField(Pair.Key, Pair.Value);
		}
	}
	return Result;
}

// ---------------------------------------------------------------------------
// ApplySingle — one asset IR object
// ---------------------------------------------------------------------------

TSharedPtr<FJsonObject> FForgeMindIRRegistry::ApplySingle(const TSharedPtr<FJsonObject>& AssetIR, const FString& FallbackVersion)
{
	TSharedRef<FJsonObject> Failure = MakeShared<FJsonObject>();

	if (!AssetIR.IsValid())
	{
		Failure->SetBoolField(TEXT("success"), false);
		Failure->SetStringField(TEXT("error"), TEXT("empty asset IR"));
		Failure->SetStringField(TEXT("errorCode"), TEXT("INVALID_PAYLOAD"));
		return Failure;
	}

	FIRApplyContext Ctx;
	AssetIR->TryGetStringField(TEXT("assetType"), Ctx.AssetType);
	FString RawPath;
	AssetIR->TryGetStringField(TEXT("path"), RawPath);
	if (RawPath.IsEmpty())
	{
		AssetIR->TryGetStringField(TEXT("assetPath"), RawPath);
	}
	Ctx.Path = ForgeMindIR::NormalizePath(RawPath);

	// --- version (IR rules.md section 2.1) ---
	FString Version = ReadVersionField(AssetIR);
	if (Version.IsEmpty())
	{
		Version = FallbackVersion;
	}
	FString VersionError;
	if (!ForgeMindIR::CheckVersion(Version, VersionError))
	{
		Failure->SetBoolField(TEXT("success"), false);
		Failure->SetStringField(TEXT("assetType"), Ctx.AssetType);
		Failure->SetStringField(TEXT("path"), Ctx.Path);
		Failure->SetStringField(TEXT("error"), VersionError);
		Failure->SetStringField(TEXT("errorCode"), TEXT("INVALID_VERSION"));
		return Failure;
	}

	// --- path (IR rules.md section 2.3) ---
	FString PathError;
	if (!ForgeMindIR::IsValidAssetPath(Ctx.Path, PathError))
	{
		Failure->SetBoolField(TEXT("success"), false);
		Failure->SetStringField(TEXT("assetType"), Ctx.AssetType);
		Failure->SetStringField(TEXT("path"), Ctx.Path);
		Failure->SetStringField(TEXT("error"), PathError);
		Failure->SetStringField(TEXT("errorCode"), TEXT("INVALID_PATH"));
		return Failure;
	}

	// --- assetType routing (IR rules.md section 2.2) ---
	IIRHandler* Handler = Find(Ctx.AssetType);
	if (Handler == nullptr)
	{
		Failure->SetBoolField(TEXT("success"), false);
		Failure->SetStringField(TEXT("assetType"), Ctx.AssetType);
		Failure->SetStringField(TEXT("path"), Ctx.Path);
		Failure->SetStringField(TEXT("error"), FString::Printf(TEXT("unsupported assetType %s"), *Ctx.AssetType));
		Failure->SetStringField(TEXT("errorCode"), TEXT("UNSUPPORTED_ASSET_TYPE"));
		return Failure;
	}

	// --- mode / flags ---
	AssetIR->TryGetStringField(TEXT("mode"), Ctx.Mode);
	Ctx.Mode = Ctx.Mode.ToLower();
	if (Ctx.Mode.IsEmpty())
	{
		Ctx.Mode = TEXT("create");
	}
	if (!AssetIR->TryGetBoolField(TEXT("compile"), Ctx.bCompile))
	{
		Ctx.bCompile = true;
	}
	if (!AssetIR->TryGetBoolField(TEXT("save"), Ctx.bSave))
	{
		Ctx.bSave = false;
	}
	AssetIR->TryGetBoolField(TEXT("overwrite"), Ctx.bOverwrite);

	const TSharedPtr<FJsonObject>* DefPtr = nullptr;
	if (AssetIR->TryGetObjectField(TEXT("definition"), DefPtr) && DefPtr != nullptr)
	{
		Ctx.Definition = *DefPtr;
	}
	const TArray<TSharedPtr<FJsonValue>>* PatchesPtr = nullptr;
	if (AssetIR->TryGetArrayField(TEXT("patches"), PatchesPtr) && PatchesPtr != nullptr)
	{
		Ctx.Patches = *PatchesPtr;
	}

	// --- mode / payload shape contract (Core Syntax.md section 5.2) ---
	if (Ctx.Mode == TEXT("create") && !Ctx.Definition.IsValid())
	{
		Failure->SetBoolField(TEXT("success"), false);
		Failure->SetStringField(TEXT("assetType"), Ctx.AssetType);
		Failure->SetStringField(TEXT("path"), Ctx.Path);
		Failure->SetStringField(TEXT("error"), TEXT("mode 'create' requires a definition object"));
		Failure->SetStringField(TEXT("errorCode"), TEXT("INVALID_PAYLOAD"));
		return Failure;
	}
	if (Ctx.Mode == TEXT("update") && Ctx.Patches.Num() == 0)
	{
		Failure->SetBoolField(TEXT("success"), false);
		Failure->SetStringField(TEXT("assetType"), Ctx.AssetType);
		Failure->SetStringField(TEXT("path"), Ctx.Path);
		Failure->SetStringField(TEXT("error"), TEXT("mode 'update' requires a non-empty patches array"));
		Failure->SetStringField(TEXT("errorCode"), TEXT("INVALID_PAYLOAD"));
		return Failure;
	}

	// --- DELETE (IR rules.md section 5) ---
	if (Ctx.Mode == TEXT("delete"))
	{
		if (!ForgeMindIR::AssetExists(Ctx.Path))
		{
			Ctx.Fail(TEXT("ASSET_NOT_FOUND"), FString::Printf(TEXT("asset not found at %s"), *Ctx.Path));
			return BuildAssetResult(Ctx.AssetType, Ctx.Path, Ctx.Mode, Ctx);
		}
		const int32 ReferencerCount = ForgeMindIR::CountReferencers(Ctx.Path);
		if (ReferencerCount > 0)
		{
			Ctx.Warn(TEXT("ASSET_REFERENCED"),
				FString::Printf(TEXT("asset is referenced by %d other asset(s)"), ReferencerCount));
		}
		if (!Handler->DeleteAsset(Ctx))
		{
			Ctx.Fail(TEXT("DELETE_FAILED"), FString::Printf(TEXT("failed to delete asset at %s"), *Ctx.Path));
		}
		return BuildAssetResult(Ctx.AssetType, Ctx.Path, Ctx.Mode, Ctx);
	}

	// --- CREATE / UPDATE ---
	UObject* Asset = nullptr;
	const bool bExists = ForgeMindIR::AssetExists(Ctx.Path);

	if (Ctx.Mode == TEXT("create"))
	{
		// Level blueprints live inside the level asset (.umap), so after
		// create_level the target path already exists; "create" means reuse
		// the level blueprint and apply the definition (handler-owned).
		const bool bLevelBlueprint = Ctx.AssetType == TEXT("level_blueprint");
		if (bExists && !Ctx.bOverwrite && !bLevelBlueprint)
		{
			Ctx.Fail(TEXT("ASSET_ALREADY_EXISTS"),
				FString::Printf(TEXT("asset already exists at %s"), *Ctx.Path));
			return BuildAssetResult(Ctx.AssetType, Ctx.Path, Ctx.Mode, Ctx);
		}
		if (bExists && Ctx.bOverwrite)
		{
			// Recreate from scratch so the definition stays authoritative.
			FString DeleteError;
			ForgeMindIR::DeleteAssetObject(Ctx.Path, DeleteError);
		}
		Asset = Handler->Create(Ctx);
	}
	else
	{
		if (!bExists)
		{
			Ctx.Fail(TEXT("ASSET_NOT_FOUND"), FString::Printf(TEXT("asset not found at %s"), *Ctx.Path));
			return BuildAssetResult(Ctx.AssetType, Ctx.Path, Ctx.Mode, Ctx);
		}
		Asset = ForgeMindIR::LoadAsset(Ctx.Path);
		if (Asset == nullptr)
		{
			Ctx.Fail(TEXT("ASSET_NOT_FOUND"), FString::Printf(TEXT("asset not found at %s"), *Ctx.Path));
			return BuildAssetResult(Ctx.AssetType, Ctx.Path, Ctx.Mode, Ctx);
		}
		if (!Handler->Update(Asset, Ctx))
		{
			Ctx.Fail(TEXT("UPDATE_FAILED"), Ctx.Error.IsEmpty()
				? FString::Printf(TEXT("failed to apply patches to %s"), *Ctx.Path)
				: Ctx.Error);
		}
	}

	if (Ctx.HasError() || Asset == nullptr)
	{
		if (!Ctx.HasError())
		{
			Ctx.Fail(TEXT("CREATE_FAILED"), FString::Printf(TEXT("failed to create asset at %s"), *Ctx.Path));
		}
		return BuildAssetResult(Ctx.AssetType, Ctx.Path, Ctx.Mode, Ctx);
	}

	// --- compile (IR rules.md section 7.1) ---
	const double CompileStart = FPlatformTime::Seconds();
	if (Ctx.bCompile)
	{
		if (!Handler->CompileAsset(Asset, Ctx))
		{
			Ctx.Warn(TEXT("COMPILATION_FAILED"), Ctx.Error.IsEmpty()
				? FString::Printf(TEXT("compilation of %s reported errors"), *Ctx.Path)
				: Ctx.Error);
		}
	}
	Ctx.CompileTimeMs = (FPlatformTime::Seconds() - CompileStart) * 1000.0;

	ForgeMindIR::FinalizeAsset(Asset, Ctx.bSave);
	return BuildAssetResult(Ctx.AssetType, Ctx.Path, Ctx.Mode, Ctx);
}

// ---------------------------------------------------------------------------
// ApplyBatch
// ---------------------------------------------------------------------------

TSharedPtr<FJsonObject> FForgeMindIRRegistry::ApplyBatch(const TSharedPtr<FJsonObject>& InPayload)
{
	RegisterBuiltinHandlers();

	const TSharedPtr<FJsonObject> Payload = UnwrapPayload(InPayload);
	TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();

	if (!Payload.IsValid())
	{
		Result->SetBoolField(TEXT("success"), false);
		Result->SetStringField(TEXT("error"), TEXT("missing payload"));
		Result->SetStringField(TEXT("errorCode"), TEXT("INVALID_PAYLOAD"));
		return Result;
	}

	const FString BatchVersion = ReadVersionField(Payload);

	// Single asset IR (no assets[] wrapper).
	if (!Payload->HasField(TEXT("assets")))
	{
		TSharedPtr<FJsonObject> Single = ApplySingle(Payload, BatchVersion);
		Single->SetBoolField(TEXT("applied"), Single->GetBoolField(TEXT("success")));
		return Single;
	}

	const TArray<TSharedPtr<FJsonValue>>* AssetsArray = nullptr;
	Payload->TryGetArrayField(TEXT("assets"), AssetsArray);
	if (AssetsArray == nullptr || AssetsArray->Num() == 0)
	{
		Result->SetBoolField(TEXT("success"), false);
		Result->SetStringField(TEXT("error"), TEXT("assets[] is empty"));
		Result->SetStringField(TEXT("errorCode"), TEXT("INVALID_PAYLOAD"));
		return Result;
	}

	TArray<TSharedPtr<FJsonValue>> AssetResults;
	int32 CompletedCount = 0;
	int32 FailedCount = 0;

	// IR rules.md section 6.2 -- sequential, order preserving, no rollback on failure.
	TSet<FString> CreatedPaths;
	for (const TSharedPtr<FJsonValue>& Value : *AssetsArray)
	{
		const TSharedPtr<FJsonObject> AssetIR = Value->AsObject();
		if (!AssetIR.IsValid())
		{
			TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
			Entry->SetBoolField(TEXT("success"), false);
			Entry->SetStringField(TEXT("error"), TEXT("asset IR entry is not an object"));
			Entry->SetStringField(TEXT("errorCode"), TEXT("INVALID_PAYLOAD"));
			AssetResults.Add(MakeShared<FJsonValueObject>(Entry));
			++FailedCount;
			continue;
		}

		FString Mode;
		AssetIR->TryGetStringField(TEXT("mode"), Mode);
		FString Path;
		AssetIR->TryGetStringField(TEXT("path"), Path);
		Path = ForgeMindIR::NormalizePath(Path);

		const bool bIsCreate = Mode.Equals(TEXT("create"), ESearchCase::IgnoreCase);
		if (bIsCreate && CreatedPaths.Contains(Path))
		{
			// IR rules.md section 6.5 -- a second create on the same path in one batch fails.
			TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
			Entry->SetBoolField(TEXT("success"), false);
			Entry->SetStringField(TEXT("path"), Path);
			Entry->SetStringField(TEXT("assetType"), AssetIR->GetStringField(TEXT("assetType")));
			Entry->SetStringField(TEXT("error"),
				FString::Printf(TEXT("asset already exists at %s (created earlier in same batch)"), *Path));
			Entry->SetStringField(TEXT("errorCode"), TEXT("ASSET_ALREADY_EXISTS"));
			AssetResults.Add(MakeShared<FJsonValueObject>(Entry));
			++FailedCount;
			continue;
		}

		TSharedPtr<FJsonObject> AssetResult = ApplySingle(AssetIR, BatchVersion);
		if (AssetResult->GetBoolField(TEXT("success")))
		{
			++CompletedCount;
			if (bIsCreate)
			{
				CreatedPaths.Add(Path);
			}
		}
		else
		{
			++FailedCount;
		}
		AssetResults.Add(MakeShared<FJsonValueObject>(AssetResult));
	}

	Result->SetBoolField(TEXT("success"), FailedCount == 0);
	Result->SetBoolField(TEXT("applied"), FailedCount == 0);
	Result->SetBoolField(TEXT("batch"), true);
	Result->SetArrayField(TEXT("assetResults"), AssetResults);
	Result->SetNumberField(TEXT("completedCount"), CompletedCount);
	Result->SetNumberField(TEXT("failedCount"), FailedCount);
	return Result;
}

// ---------------------------------------------------------------------------
// ReadIR -- get_asset_ir (mode = "read", IR rules.md section 1.4)
// ---------------------------------------------------------------------------

TSharedPtr<FJsonObject> FForgeMindIRRegistry::ReadIR(const TSharedPtr<FJsonObject>& InPayload)
{
	RegisterBuiltinHandlers();

	const TSharedPtr<FJsonObject> Payload = UnwrapPayload(InPayload);
	TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();

	if (!Payload.IsValid())
	{
		Result->SetBoolField(TEXT("success"), false);
		Result->SetStringField(TEXT("error"), TEXT("missing payload"));
		Result->SetStringField(TEXT("errorCode"), TEXT("INVALID_PAYLOAD"));
		return Result;
	}

	const TArray<TSharedPtr<FJsonValue>>* PathsArray = nullptr;
	const bool bMulti = Payload->TryGetArrayField(TEXT("paths"), PathsArray) && PathsArray->Num() > 0;

	TArray<FString> Paths;
	if (bMulti)
	{
		for (const TSharedPtr<FJsonValue>& Value : *PathsArray)
		{
			Paths.Add(ForgeMindIR::NormalizePath(Value->AsString()));
		}
	}
	else
	{
		FString Single;
		Payload->TryGetStringField(TEXT("path"), Single);
		if (Single.IsEmpty())
		{
			Payload->TryGetStringField(TEXT("assetPath"), Single);
		}
		Paths.Add(ForgeMindIR::NormalizePath(Single));
	}

	TArray<TSharedPtr<FJsonValue>> Assets;
	int32 Failed = 0;

	for (const FString& Path : Paths)
	{
		FIRApplyContext Ctx;
		Ctx.Path = Path;
		Ctx.Mode = TEXT("read");

		FString RequestedLevel;
		Payload->TryGetStringField(TEXT("level"), RequestedLevel);
		if (RequestedLevel == TEXT("graph") || RequestedLevel == TEXT("full"))
		{
			Ctx.Level = RequestedLevel;
		}
		else
		{
			Ctx.Level = TEXT("summary");
		}

		FString PathError;
		if (!ForgeMindIR::IsValidAssetPath(Path, PathError))
		{
			++Failed;
			continue;
		}

		UObject* Asset = ForgeMindIR::LoadAsset(Path);
		if (Asset == nullptr)
		{
			++Failed;
			continue;
		}

		FString AssetType;
		if (!Payload->TryGetStringField(TEXT("assetType"), AssetType) || AssetType.IsEmpty() || bMulti)
		{
			AssetType = IdentifyAssetTypeFor(Asset);
		}

		// LoadAsset may return a UPackage or UBlueprintGeneratedClass instead
		// of the UBlueprint source object. When the automatic type
		// identification fails, try loading the blueprint source object
		// via the canonical "Package.AssetName" convention.
		if (AssetType.IsEmpty())
		{
			const FString AssetName = ForgeMindIR::AssetNameOf(Path);
			const FString ObjectForm = Path + TEXT(".") + AssetName;
			if (UObject* Reloaded = LoadObject<UObject>(nullptr, *ObjectForm))
			{
				Asset = Reloaded;
				AssetType = IdentifyAssetTypeFor(Asset);
			}
		}
		if (AssetType.IsEmpty())
		{
			++Failed;
			continue;
		}
		Ctx.AssetType = AssetType;

		IIRHandler* Handler = Find(AssetType);
		if (Handler == nullptr)
		{
			++Failed;
			continue;
		}

		TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
		Entry->SetStringField(TEXT("version"), FString::Printf(TEXT("%d.%d"),
			FEngineVersion::Current().GetMajor(), FEngineVersion::Current().GetMinor()));
		Entry->SetStringField(TEXT("assetType"), AssetType);
		Entry->SetStringField(TEXT("path"), Path);
		Entry->SetStringField(TEXT("mode"), TEXT("read"));
		Entry->SetStringField(TEXT("level"), Ctx.Level);

		const TSharedPtr<FJsonObject> Definition = Handler->Read(Asset, Ctx);
		Entry->SetObjectField(TEXT("definition"),
			Definition.IsValid() ? Definition.ToSharedRef() : MakeShared<FJsonObject>());
		AttachWarnings(Entry, Ctx.Warnings);
		Assets.Add(MakeShared<FJsonValueObject>(Entry));
	}

	if (bMulti || Assets.Num() != 1)
	{
		Result->SetBoolField(TEXT("success"), Failed == 0);
		Result->SetBoolField(TEXT("batch"), true);
		Result->SetArrayField(TEXT("assets"), Assets);
		Result->SetNumberField(TEXT("completedCount"), Assets.Num());
		Result->SetNumberField(TEXT("failedCount"), Failed);
		return Result;
	}

	// Single asset: return the IR object itself (IR rules.md section 1.4 symmetry).
	TSharedRef<FJsonObject> Single = Assets[0]->AsObject().ToSharedRef();
	Single->SetBoolField(TEXT("success"), true);
	return Single;
}