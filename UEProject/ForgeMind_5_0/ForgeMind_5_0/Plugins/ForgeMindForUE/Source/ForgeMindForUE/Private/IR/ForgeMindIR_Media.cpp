// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Groom, Paper2D and Sequencer domain IR handlers.

#include "ForgeMindIRGeneric.h"

#include "UObject/UnrealType.h"

namespace
{
	/** Adds one element to a USTRUCT array property and returns its memory. */
	void* AddStructSlot(UObject* Owner, const FString& ArrayPropertyName, const UStruct*& OutStructType)
	{
		OutStructType = nullptr;
		FArrayProperty* ArrayProperty = CastField<FArrayProperty>(ForgeMindIR::FindProperty(Owner, ArrayPropertyName));
		if (ArrayProperty == nullptr)
		{
			return nullptr;
		}
		FStructProperty* Inner = CastField<FStructProperty>(ArrayProperty->Inner);
		if (Inner == nullptr || Inner->Struct == nullptr)
		{
			return nullptr;
		}
		FScriptArrayHelper Helper(ArrayProperty, ArrayProperty->ContainerPtrToValuePtr<void>(Owner));
		void* Slot = Helper.GetRawPtr(Helper.AddValue());
		OutStructType = Inner->Struct;
		return Slot;
	}

	void SetStructMember(void* StructMemory, const UStruct* StructType, const FString& MemberName,
		const TSharedPtr<FJsonValue>& Value)
	{
		FProperty* Member = ForgeMindIR::FindPropertyOnStruct(StructType, MemberName);
		if (Member == nullptr)
		{
			return;
		}
		void* Data = Member->ContainerPtrToValuePtr<void>(StructMemory);
		const FString ImportText = ForgeMindIR::JsonValueToImportText(Value);
		Member->ImportText(*ImportText, Data, PPF_PropertyWindow, nullptr);
	}

	void SetStructObjectMember(void* StructMemory, const UStruct* StructType, const FString& MemberName, UObject* Value)
	{
		FProperty* Member = ForgeMindIR::FindPropertyOnStruct(StructType, MemberName);
		FObjectProperty* ObjectMember = CastField<FObjectProperty>(Member);
		if (ObjectMember == nullptr)
		{
			return;
		}
		ObjectMember->SetObjectPropertyValue(ObjectMember->ContainerPtrToValuePtr<void>(StructMemory), Value);
	}
}

// ---------------------------------------------------------------------------
// Groom
// ---------------------------------------------------------------------------

class FGroomIRHandler : public FGenericAssetIRHandler
{
public:
	FGroomIRHandler()
		: FGenericAssetIRHandler({ { TEXT("groom_asset"), TEXT("groom_binding_asset"), TEXT("groom_cache") },
			TEXT("GroomBindingAsset"),
			{ { TEXT("bindingType"), TEXT("BindingType") }, { TEXT("bindingMeshType"), TEXT("BindingMeshType") },
			  { TEXT("hairWidth"), TEXT("HairWidth") }, { TEXT("hairRootScale"), TEXT("HairRootScale") },
			  { TEXT("hairTipScale"), TEXT("HairTipScale") }, { TEXT("lodBias"), TEXT("LODBias") },
			  { TEXT("hairLengthScale"), TEXT("HairLengthScale") }, { TEXT("density"), TEXT("Density") } },
			{ TEXT("BindingType"), TEXT("BindingMeshType"), TEXT("HairWidth"), TEXT("HairRootScale"),
			  TEXT("HairTipScale"), TEXT("LODBias"), TEXT("HairLengthScale"), TEXT("Density") },
			false, TEXT("GROOM_BINDING_TYPE_INVALID") })
	{
	}

protected:
	UObject* CreateAssetObject(FIRApplyContext& Ctx) override
	{
		FString ClassName = TEXT("GroomBindingAsset");
		if (Ctx.AssetType == TEXT("groom_asset")) ClassName = TEXT("GroomAsset");
		else if (Ctx.AssetType == TEXT("groom_cache")) ClassName = TEXT("GroomCache");

		UClass* Class = ForgeMindIR::ResolveClass(ClassName);
		if (Class == nullptr)
		{
			Ctx.Fail(TEXT("UNSUPPORTED_ASSET_TYPE"),
				FString::Printf(TEXT("class '%s' is not available in this editor (is the HairStrands module loaded?)"), *ClassName));
			return nullptr;
		}
		UObject* Asset = ForgeMindIR::CreateAssetInstance(Class, Ctx.Path, true);
		if (Asset == nullptr)
		{
			Ctx.Fail(TEXT("CREATE_FAILED"), FString::Printf(TEXT("failed to create asset at %s"), *Ctx.Path));
			return nullptr;
		}

		if (!Ctx.Definition.IsValid())
		{
			return Asset;
		}
		FString Path;
		if (Ctx.Definition->TryGetStringField(TEXT("targetSkeletalMesh"), Path) && !Path.IsEmpty())
		{
			FString Error;
			ForgeMindIR::SetProperty(Asset, TEXT("TargetSkeletalMesh"), MakeShared<FJsonValueString>(Path), Error);
		}
		if (Ctx.Definition->TryGetStringField(TEXT("sourceSkeletalMesh"), Path) && !Path.IsEmpty())
		{
			FString Error;
			ForgeMindIR::SetProperty(Asset, TEXT("SourceSkeletalMesh"), MakeShared<FJsonValueString>(Path), Error);
		}
		return Asset;
	}

	void ApplyDefinitionExtras(UObject* Asset, FIRApplyContext& Ctx) override
	{
		if (!Ctx.Definition.IsValid())
		{
			return;
		}
		if (Ctx.Definition->HasField(TEXT("hairGroupsInterpolation"))
			|| Ctx.Definition->HasField(TEXT("hairGroupsLOD"))
			|| Ctx.Definition->HasField(TEXT("hairGroupsPhysics")))
		{
			Ctx.Warn(TEXT("GROOM_INCOMPLETE_HAIRGROUP"),
				TEXT("per-hair-group configuration is authored in the Groom editor - the asset level defaults were applied"));
		}
	}
};

TSharedRef<IIRHandler> MakeGroomIRHandler()
{
	return MakeShared<FGroomIRHandler>();
}

// ---------------------------------------------------------------------------
// Paper2D
// ---------------------------------------------------------------------------

class FPaper2DIRHandler : public FGenericAssetIRHandler
{
public:
	FPaper2DIRHandler()
		: FGenericAssetIRHandler({ { TEXT("paper_sprite"), TEXT("paper_flipbook"), TEXT("paper_tile_map"),
				TEXT("paper_sprite_atlas"), TEXT("paper_tile_set") },
			TEXT("PaperSprite"),
			{ { TEXT("sourceTexture"), TEXT("SourceTexture") }, { TEXT("pivotMode"), TEXT("PivotMode") },
			  { TEXT("alternateMaterial"), TEXT("AlternateMaterial") }, { TEXT("framesPerSecond"), TEXT("FramesPerSecond") },
			  { TEXT("defaultMaterial"), TEXT("DefaultMaterial") }, { TEXT("collisionSource"), TEXT("CollisionSource") },
			  { TEXT("mapWidth"), TEXT("MapWidth") }, { TEXT("mapHeight"), TEXT("MapHeight") },
			  { TEXT("tileWidth"), TEXT("TileWidth") }, { TEXT("tileHeight"), TEXT("TileHeight") },
			  { TEXT("projectionMode"), TEXT("ProjectionMode") } },
			{ TEXT("SourceTexture"), TEXT("PivotMode"), TEXT("FramesPerSecond"), TEXT("MapWidth"),
			  TEXT("MapHeight"), TEXT("TileWidth"), TEXT("TileHeight"), TEXT("ProjectionMode") },
			false, TEXT("INVALID_SOURCE_TEXTURE") })
	{
	}

protected:
	UObject* CreateAssetObject(FIRApplyContext& Ctx) override
	{
		FString ClassName = TEXT("PaperSprite");
		if (Ctx.AssetType == TEXT("paper_flipbook"))      ClassName = TEXT("PaperFlipbook");
		else if (Ctx.AssetType == TEXT("paper_tile_map")) ClassName = TEXT("PaperTileMap");
		else if (Ctx.AssetType == TEXT("paper_sprite_atlas")) ClassName = TEXT("PaperSpriteAtlas");
		else if (Ctx.AssetType == TEXT("paper_tile_set")) ClassName = TEXT("PaperTileSet");

		UClass* Class = ForgeMindIR::ResolveClass(ClassName);
		if (Class == nullptr)
		{
			Ctx.Fail(TEXT("UNSUPPORTED_ASSET_TYPE"),
				FString::Printf(TEXT("class '%s' is not available in this editor (is the Paper2D module loaded?)"), *ClassName));
			return nullptr;
		}
		UObject* Asset = ForgeMindIR::CreateAssetInstance(Class, Ctx.Path, true);
		if (Asset == nullptr)
		{
			Ctx.Fail(TEXT("CREATE_FAILED"), FString::Printf(TEXT("failed to create asset at %s"), *Ctx.Path));
		}
		return Asset;
	}

	void ApplyDefinitionExtras(UObject* Asset, FIRApplyContext& Ctx) override
	{
		if (!Ctx.Definition.IsValid())
		{
			return;
		}

		// Flipbook frames.
		const TArray<TSharedPtr<FJsonValue>>* KeyFrames = nullptr;
		if (Ctx.Definition->TryGetArrayField(TEXT("keyFrames"), KeyFrames))
		{
			int32 FrameCount = 0;
			for (const TSharedPtr<FJsonValue>& Value : *KeyFrames)
			{
				const TSharedPtr<FJsonObject> Frame = Value->AsObject();
				if (!Frame.IsValid())
				{
					continue;
				}
				const UStruct* SlotType = nullptr;
				void* Slot = AddStructSlot(Asset, TEXT("KeyFrames"), SlotType);
				if (Slot == nullptr)
				{
					break;
				}
				FString SpritePath = ForgeMindIR::FirstString(Frame, { TEXT("sprite"), TEXT("asset") });
				UObject* Sprite = ForgeMindIR::ResolveObjectPath(SpritePath);
				if (Sprite == nullptr)
				{
					Ctx.Warn(TEXT("SPRITE_REFERENCE_NOT_FOUND"),
						FString::Printf(TEXT("flipbook frame sprite '%s' was not found"), *SpritePath));
				}
				else
				{
					SetStructObjectMember(Slot, SlotType, TEXT("Sprite"), Sprite);
				}
				double FrameRun = 1.0;
				Frame->TryGetNumberField(TEXT("frameRun"), FrameRun);
				SetStructMember(Slot, SlotType, TEXT("FrameRun"), MakeShared<FJsonValueNumber>(FrameRun));
				++FrameCount;
			}
			Ctx.Extra->SetNumberField(TEXT("keyFrameCount"), FrameCount);
		}

		if (Ctx.Definition->HasField(TEXT("tileLayers")))
		{
			Ctx.Warn(TEXT("TILEMAP_LAYER_DIMENSION_MISMATCH"),
				TEXT("tile map layers are authored in the Paper2D tile map editor"));
		}
		if (Ctx.Definition->HasField(TEXT("bodySetup")))
		{
			Ctx.Warn(TEXT("COLLISION_DATA_INVALID"),
				TEXT("sprite collision geometry is edited in the Sprite editor"));
		}
	}
};

TSharedRef<IIRHandler> MakePaper2DIRHandler()
{
	return MakeShared<FPaper2DIRHandler>();
}


// ---------------------------------------------------------------------------
// Sequencer
// ---------------------------------------------------------------------------

class FSequencerIRHandler : public FGenericAssetIRHandler
{
public:
	FSequencerIRHandler()
		: FGenericAssetIRHandler({ { TEXT("level_sequence"), TEXT("movie_scene_sequence") }, TEXT("LevelSequence"),
			{ { TEXT("lengthInSeconds"), TEXT("MovieScene_LengthInSeconds") } },
			{ TEXT("MovieScene"), }, false, TEXT("SECTIONS_INVALID_FRAME_RANGE") })
	{
	}

protected:
	UObject* CreateAssetObject(FIRApplyContext& Ctx) override
	{
		UClass* Class = ForgeMindIR::ResolveClass(TEXT("LevelSequence"));
		if (Class == nullptr)
		{
			Ctx.Fail(TEXT("UNSUPPORTED_ASSET_TYPE"),
				TEXT("ULevelSequence is not available in this editor (is the LevelSequence module loaded?)"));
			return nullptr;
		}
		UObject* Asset = ForgeMindIR::CreateAssetInstance(Class, Ctx.Path, true);
		if (Asset == nullptr)
		{
			Ctx.Fail(TEXT("CREATE_FAILED"), FString::Printf(TEXT("failed to create asset at %s"), *Ctx.Path));
			return nullptr;
		}

		if (!Ctx.Definition.IsValid())
		{
			return Asset;
		}

		// displayRate / playbackSettings live on the MovieScene sub-object.
		UObject* MovieScene = nullptr;
		{
			FProperty* Property = ForgeMindIR::FindProperty(Asset, TEXT("MovieScene"));
			if (FObjectProperty* ObjectProperty = CastField<FObjectProperty>(Property))
			{
				MovieScene = ObjectProperty->GetObjectPropertyValue(ObjectProperty->ContainerPtrToValuePtr<void>(Asset));
			}
		}

		const TSharedPtr<FJsonObject>* DisplayRate = nullptr;
		if (MovieScene != nullptr && Ctx.Definition->TryGetObjectField(TEXT("displayRate"), DisplayRate) && DisplayRate->IsValid())
		{
			const TSharedPtr<FJsonObject>* Rate = DisplayRate;
			FString Error;
			if (TSharedPtr<FJsonValue> Numerator = (*Rate)->TryGetField(TEXT("numerator")))
			{
				ForgeMindIR::SetProperty(MovieScene, TEXT("DisplayRate_Numerator"), Numerator, Error);
				ForgeMindIR::SetProperty(MovieScene, TEXT("DisplayRate"), Numerator, Error);
			}
			if (TSharedPtr<FJsonValue> Denominator = (*Rate)->TryGetField(TEXT("denominator")))
			{
				ForgeMindIR::SetProperty(MovieScene, TEXT("DisplayRate_Denominator"), Denominator, Error);
			}
		}

		const TSharedPtr<FJsonObject>* Playback = nullptr;
		if (Ctx.Definition->TryGetObjectField(TEXT("playbackSettings"), Playback) && Playback->IsValid())
		{
			for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : (*Playback)->Values)
			{
				FString Error;
				if (!ForgeMindIR::SetProperty(Asset, Pair.Key, Pair.Value, Error) && MovieScene != nullptr)
				{
					ForgeMindIR::SetProperty(MovieScene, Pair.Key, Pair.Value, Error);
				}
			}
		}

		const TArray<TSharedPtr<FJsonValue>>* Tracks = nullptr;
		if (Ctx.Definition->TryGetArrayField(TEXT("tracks"), Tracks) && Tracks->Num() > 0)
		{
			Ctx.Warn(TEXT("SECTIONS_INVALID_TRACK_TYPE"),
				TEXT("track/section construction uses the Sequencer editor API - add tracks in the Sequencer UI"));
			Ctx.Extra->SetNumberField(TEXT("trackCount"), Tracks->Num());
		}
		return Asset;
	}
};

TSharedRef<IIRHandler> MakeSequencerIRHandler()
{
	return MakeShared<FSequencerIRHandler>();
}