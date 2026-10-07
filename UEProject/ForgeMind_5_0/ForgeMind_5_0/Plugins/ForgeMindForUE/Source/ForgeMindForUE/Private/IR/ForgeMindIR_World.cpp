// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Niagara, Physics, Chaos, Landscape and Foliage domain IR handlers.
//
// These asset classes live in modules this plugin does not link, so the
// UClass is resolved by name at runtime and the definition is applied
// through UPROPERTY reflection. Each domain keeps a small hook for the
// structures its IR actually carries (body setups, wheel configs, ...).

#include "ForgeMindIRGeneric.h"

#include "UObject/UnrealType.h"

namespace
{
	/** Appends a freshly created sub-object to an array property. */
	bool AppendObjectToArray(UObject* Owner, const FString& ArrayPropertyName, UObject* Element)
	{
		if (Owner == nullptr || Element == nullptr)
		{
			return false;
		}
		FArrayProperty* ArrayProperty = CastField<FArrayProperty>(ForgeMindIR::FindProperty(Owner, ArrayPropertyName));
		if (ArrayProperty == nullptr)
		{
			return false;
		}
		FObjectProperty* Inner = CastField<FObjectProperty>(ArrayProperty->Inner);
		if (Inner != nullptr)
		{
			FScriptArrayHelper Helper(ArrayProperty, ArrayProperty->ContainerPtrToValuePtr<void>(Owner));
			Inner->SetObjectPropertyValue(Helper.GetRawPtr(Helper.AddValue()), Element);
			return true;
		}
		if (FStructProperty* StructInner = CastField<FStructProperty>(ArrayProperty->Inner))
		{
			// Arrays of structs that hold exactly one object member
			// (e.g. children slots) get the element assigned to that member.
			FScriptArrayHelper Helper(ArrayProperty, ArrayProperty->ContainerPtrToValuePtr<void>(Owner));
			void* Slot = Helper.GetRawPtr(Helper.AddValue());
			for (TFieldIterator<FObjectProperty> It(StructInner->Struct); It; ++It)
			{
				(*It)->SetObjectPropertyValue((*It)->ContainerPtrToValuePtr<void>(Slot), Element);
				return true;
			}
		}
		return false;
	}

	UObject* NewChildOf(UObject* Outer, const FString& ClassName)
	{
		UClass* Class = ForgeMindIR::ResolveClass(ClassName);
		if (Class == nullptr)
		{
			return nullptr;
		}
		return NewObject<UObject>(Outer, Class, NAME_None, RF_Transactional);
	}
}

// ---------------------------------------------------------------------------
// Niagara
// ---------------------------------------------------------------------------

class FNiagaraIRHandler : public FGenericAssetIRHandler
{
public:
	FNiagaraIRHandler()
		: FGenericAssetIRHandler({ { TEXT("niagara_system"), TEXT("niagara_emitter") }, TEXT("NiagaraSystem"),
			{ { TEXT("warmupTime"), TEXT("WarmupTime") }, { TEXT("warmupTickCount"), TEXT("WarmupTickCount") },
			  { TEXT("fixedTickDeltaTime"), TEXT("FixedTickDeltaTime") }, { TEXT("randomSeed"), TEXT("RandomSeed") } },
			{ TEXT("WarmupTime"), TEXT("WarmupTickCount"), TEXT("FixedTickDeltaTime"), TEXT("RandomSeed") },
			true, TEXT("NAMESPACE_VIOLATION") })
	{
	}

protected:
	UObject* CreateAssetObject(FIRApplyContext& Ctx) override
	{
		const FString ClassName = Ctx.AssetType == TEXT("niagara_emitter") ? TEXT("NiagaraEmitter") : TEXT("NiagaraSystem");
		UClass* Class = ForgeMindIR::ResolveClass(ClassName);
		if (Class == nullptr)
		{
			Ctx.Fail(TEXT("UNSUPPORTED_ASSET_TYPE"),
				FString::Printf(TEXT("class '%s' is not available in this editor"), *ClassName));
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
		const TArray<TSharedPtr<FJsonValue>>* Emitters = nullptr;
		if (Ctx.Definition->TryGetArrayField(TEXT("emitters"), Emitters) && Emitters->Num() > 0)
		{
			Ctx.Warn(TEXT("DATA_INTERFACE_NOT_FOUND"),
				TEXT("emitter references need the Niagara editor graph API - wire them in the Niagara editor"));
		}
		const TArray<TSharedPtr<FJsonValue>>* Parameters = nullptr;
		if (Ctx.Definition->TryGetArrayField(TEXT("userParameters"), Parameters) && Parameters->Num() > 0)
		{
			Ctx.Warn(TEXT("NAMESPACE_VIOLATION"),
				TEXT("user parameters are declared through the Niagara parameter panel"));
		}
	}
};

TSharedRef<IIRHandler> MakeNiagaraIRHandler()
{
	return MakeShared<FNiagaraIRHandler>();
}

// ---------------------------------------------------------------------------
// Physics
// ---------------------------------------------------------------------------

class FPhysicsIRHandler : public FGenericAssetIRHandler
{
public:
	FPhysicsIRHandler()
		: FGenericAssetIRHandler({ { TEXT("physics_asset") }, TEXT("PhysicsAsset"), {}, {}, false, TEXT("INVALID_GEOMETRY") })
	{
	}

protected:
	void ApplyDefinitionExtras(UObject* Asset, FIRApplyContext& Ctx) override
	{
		if (!Ctx.Definition.IsValid())
		{
			return;
		}

		int32 BodyCount = 0;
		const TArray<TSharedPtr<FJsonValue>>* BodySetups = nullptr;
		if (Ctx.Definition->TryGetArrayField(TEXT("bodySetups"), BodySetups))
		{
			for (const TSharedPtr<FJsonValue>& Value : *BodySetups)
			{
				const TSharedPtr<FJsonObject> Body = Value->AsObject();
				if (!Body.IsValid())
				{
					continue;
				}
				UObject* BodySetup = NewChildOf(Asset, TEXT("SkeletalBodySetup"));
				if (BodySetup == nullptr)
				{
					BodySetup = NewChildOf(Asset, TEXT("BodySetup"));
				}
				if (BodySetup == nullptr)
				{
					Ctx.Warn(TEXT("INVALID_GEOMETRY"), TEXT("USkeletalBodySetup is not available in this editor"));
					return;
				}
				FString Bone;
				Body->TryGetStringField(TEXT("bone"), Bone);
				for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Body->Values)
				{
					if (Pair.Value->Type == EJson::Object || Pair.Value->Type == EJson::Array)
					{
						continue;
					}
					FString Error;
					ForgeMindIR::SetProperty(BodySetup, Pair.Key.Equals(TEXT("bone")) ? TEXT("BoneName") : Pair.Key,
						Pair.Value, Error);
				}
				if (AppendObjectToArray(Asset, TEXT("BodySetup"), BodySetup))
				{
					++BodyCount;
				}
				if (Body->HasField(TEXT("aggGeom")))
				{
					Ctx.Warn(TEXT("INVALID_GEOMETRY"),
						FString::Printf(TEXT("aggGeom for bone '%s' must be authored in the Physics Asset editor"), *Bone));
				}
			}
		}

		int32 ConstraintCount = 0;
		const TArray<TSharedPtr<FJsonValue>>* Constraints = nullptr;
		if (Ctx.Definition->TryGetArrayField(TEXT("constraints"), Constraints))
		{
			for (const TSharedPtr<FJsonValue>& Value : *Constraints)
			{
				const TSharedPtr<FJsonObject> Constraint = Value->AsObject();
				if (!Constraint.IsValid())
				{
					continue;
				}
				UObject* Template = NewChildOf(Asset, TEXT("PhysicsConstraintTemplate"));
				if (Template == nullptr)
				{
					continue;
				}
				for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Constraint->Values)
				{
					if (Pair.Value->Type == EJson::Object || Pair.Value->Type == EJson::Array)
					{
						continue;
					}
					FString Error;
					ForgeMindIR::SetProperty(Template, Pair.Key, Pair.Value, Error);
				}
				if (AppendObjectToArray(Asset, TEXT("ConstraintSetup"), Template))
				{
					++ConstraintCount;
				}
			}
		}

		Ctx.Extra->SetNumberField(TEXT("bodyCount"), BodyCount);
		Ctx.Extra->SetNumberField(TEXT("constraintCount"), ConstraintCount);
	}
};

TSharedRef<IIRHandler> MakePhysicsIRHandler()
{
	return MakeShared<FPhysicsIRHandler>();
}

// ---------------------------------------------------------------------------
// Chaos
// ---------------------------------------------------------------------------

class FChaosIRHandler : public FGenericAssetIRHandler
{
public:
	FChaosIRHandler()
		: FGenericAssetIRHandler({ { TEXT("geometry_collection"), TEXT("chaos_cloth_config"), TEXT("chaos_cloth_asset"),
				TEXT("chaos_vehicle_movement_config"), TEXT("physics_field"), TEXT("chaos_solver_actor") },
			TEXT("GeometryCollection"), {}, {}, false, TEXT("CHAOS_FRAGMENT_BUILD_FAILED") })
	{
	}

protected:
	UObject* CreateAssetObject(FIRApplyContext& Ctx) override
	{
		FString ClassName;
		if (Ctx.AssetType == TEXT("geometry_collection"))           ClassName = TEXT("GeometryCollection");
		else if (Ctx.AssetType == TEXT("chaos_cloth_config"))       ClassName = TEXT("ChaosClothConfig");
		else if (Ctx.AssetType == TEXT("chaos_cloth_asset"))        ClassName = TEXT("ChaosClothAsset");
		else if (Ctx.AssetType == TEXT("chaos_vehicle_movement_config")) ClassName = TEXT("ChaosVehicleMovementComponent");
		else if (Ctx.AssetType == TEXT("physics_field"))            ClassName = TEXT("PhysicsField");
		else if (Ctx.AssetType == TEXT("chaos_solver_actor"))       ClassName = TEXT("ChaosSolverActor");
		else
		{
			Ctx.Fail(TEXT("UNSUPPORTED_ASSET_TYPE"), FString::Printf(TEXT("unsupported chaos assetType %s"), *Ctx.AssetType));
			return nullptr;
		}

		UClass* Class = ForgeMindIR::ResolveClass(ClassName);
		if (Class == nullptr)
		{
			Ctx.Fail(TEXT("UNSUPPORTED_ASSET_TYPE"),
				FString::Printf(TEXT("class '%s' for assetType '%s' is not available in this editor"),
					*ClassName, *Ctx.AssetType));
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
		if (Ctx.Definition->HasField(TEXT("sources")))
		{
			Ctx.Warn(TEXT("CHAOS_FRAGMENT_BUILD_FAILED"),
				TEXT("geometry collection fracturing runs through the Chaos editor - the source list was stored for review"));
		}
		if (Ctx.Definition->HasField(TEXT("nodeGraph")))
		{
			Ctx.Warn(TEXT("CHAOS_CLOTH_COMPILE_FAILED"),
				TEXT("cloth node graphs are authored in the Chaos Cloth editor"));
		}
	}

	void ReadExtras(UObject* Asset, const TSharedPtr<FJsonObject>& Definition, FIRApplyContext& Ctx) override
	{
		if (TSharedPtr<FJsonValue> Value = ForgeMindIR::GetProperty(Asset, TEXT("EnableClustering")))
		{
			Definition->SetField(TEXT("enableClustering"), Value);
		}
	}
};

TSharedRef<IIRHandler> MakeChaosIRHandler()
{
	return MakeShared<FChaosIRHandler>();
}

// ---------------------------------------------------------------------------
// Landscape
// ---------------------------------------------------------------------------

class FLandscapeIRHandler : public FGenericAssetIRHandler
{
public:
	FLandscapeIRHandler()
		: FGenericAssetIRHandler({ { TEXT("landscape") }, TEXT("LandscapeLayerInfoObject"),
			{ { TEXT("layerName"), TEXT("LayerName") }, { TEXT("hardness"), TEXT("Hardness") },
			  { TEXT("physMaterial"), TEXT("PhysMaterial") } },
			{ TEXT("LayerName"), TEXT("Hardness"), TEXT("PhysMaterial") },
			false, TEXT("LANDSCAPE_INVALID_COMPONENT_CONFIG") })
	{
	}

protected:
	UObject* CreateAssetObject(FIRApplyContext& Ctx) override
	{
		// Landscapes are level actors; the IR handler owns the serializable
		// companion asset (landscape layer info) and reports the actor-side
		// configuration as warnings so nothing is silently dropped.
		if (Ctx.Definition.IsValid() && Ctx.Definition->HasField(TEXT("componentSize")))
		{
			Ctx.Warn(TEXT("LANDSCAPE_INVALID_COMPONENT_CONFIG"),
				TEXT("landscape geometry is a level actor - apply componentSize/componentCount through the level, "
					 "the IR handler stores the layer configuration only"));
		}

		UClass* Class = ForgeMindIR::ResolveClass(TEXT("LandscapeLayerInfoObject"));
		if (Class == nullptr)
		{
			Ctx.Fail(TEXT("UNSUPPORTED_ASSET_TYPE"),
				TEXT("ULandscapeLayerInfoObject is not available in this editor (the Landscape module is not loaded)"));
			return nullptr;
		}
		UObject* Asset = ForgeMindIR::CreateAssetInstance(Class, Ctx.Path, true);
		if (Asset == nullptr)
		{
			Ctx.Fail(TEXT("CREATE_FAILED"), FString::Printf(TEXT("failed to create asset at %s"), *Ctx.Path));
			return nullptr;
		}

		const TArray<TSharedPtr<FJsonValue>>* LayerInfos = nullptr;
		if (Ctx.Definition.IsValid() && Ctx.Definition->TryGetArrayField(TEXT("layerInfos"), LayerInfos) && LayerInfos->Num() > 0)
		{
			const TSharedPtr<FJsonObject> First = (*LayerInfos)[0]->AsObject();
			if (First.IsValid())
			{
				for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : First->Values)
				{
					FString Error;
					ForgeMindIR::SetProperty(Asset, Pair.Key, Pair.Value, Error);
				}
			}
		}
		return Asset;
	}
};

TSharedRef<IIRHandler> MakeLandscapeIRHandler()
{
	return MakeShared<FLandscapeIRHandler>();
}

// ---------------------------------------------------------------------------
// Foliage
// ---------------------------------------------------------------------------

class FFoliageIRHandler : public FGenericAssetIRHandler
{
public:
	FFoliageIRHandler()
		: FGenericAssetIRHandler({ { TEXT("foliage_type"), TEXT("foliage_type_instanced_static_mesh"), TEXT("foliage_type_actor") },
			TEXT("FoliageType_InstancedStaticMesh"),
			{ { TEXT("mesh"), TEXT("Mesh") }, { TEXT("density"), TEXT("Density") }, { TEXT("radius"), TEXT("Radius") },
			  { TEXT("scaling"), TEXT("Scaling") }, { TEXT("alignToNormal"), TEXT("AlignToNormal") },
			  { TEXT("castShadow"), TEXT("CastShadow") }, { TEXT("collisionRadius"), TEXT("CollisionRadius") },
			  { TEXT("actorClass"), TEXT("ActorClass") }, { TEXT("componentClass"), TEXT("ComponentClass") } },
			{ TEXT("Mesh"), TEXT("Density"), TEXT("Radius"), TEXT("Scaling"), TEXT("AlignToNormal"), TEXT("CastShadow") },
			false, TEXT("FOLIAGE_INVALID_MESH") })
	{
	}

protected:
	UObject* CreateAssetObject(FIRApplyContext& Ctx) override
	{
		FString ClassName = TEXT("FoliageType_InstancedStaticMesh");
		if (Ctx.AssetType == TEXT("foliage_type_actor"))
		{
			ClassName = TEXT("FoliageType_Actor");
		}
		else if (Ctx.AssetType == TEXT("foliage_type"))
		{
			ClassName = TEXT("FoliageType");
		}
		UClass* Class = ForgeMindIR::ResolveClass(ClassName);
		if (Class == nullptr)
		{
			Class = ForgeMindIR::ResolveClass(TEXT("FoliageType_InstancedStaticMesh"));
		}
		if (Class == nullptr)
		{
			Ctx.Fail(TEXT("UNSUPPORTED_ASSET_TYPE"),
				FString::Printf(TEXT("class '%s' is not available in this editor"), *ClassName));
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
		const TArray<TSharedPtr<FJsonValue>>* Overrides = nullptr;
		if (!Ctx.Definition->TryGetArrayField(TEXT("overrideMaterials"), Overrides))
		{
			return;
		}
		for (const TSharedPtr<FJsonValue>& Value : *Overrides)
		{
			UObject* Material = ForgeMindIR::ResolveObjectPath(Value->AsString());
			if (Material == nullptr)
			{
				Ctx.Warn(TEXT("FOLIAGE_INVALID_MESH"),
					FString::Printf(TEXT("override material '%s' was not found"), *Value->AsString()));
				continue;
			}
			AppendObjectToArray(Asset, TEXT("OverrideMaterials"), Material);
		}
	}
};

TSharedRef<IIRHandler> MakeFoliageIRHandler()
{
	return MakeShared<FFoliageIRHandler>();
}