// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// AssetIR v2 batch operations — multi-asset topological-sorted batch
// creation/update from a single AssetIR JSON document.
//
// This is the evolution of GraphIR (which was blueprint-only). AssetIR
// wraps multiple asset types (blueprints, structs, enums, materials,
// animations, audio, input, etc.) into a single batch payload with
// automatic cross-asset dependency resolution via Kahn's algorithm.

#include "ForgeMindBridgeClient.h"

#include "ForgeMindBlueprintUtils.h"
#include "ForgeMindGraphLayout.h"

#include "AssetRegistry/AssetRegistryModule.h"
#include "AssetToolsModule.h"
#include "Dom/JsonObject.h"
#include "Dom/JsonValue.h"
#include "Editor.h"
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphNode.h"
#include "EdGraphSchema_K2.h"
#include "Engine/Blueprint.h"
#include "Engine/BlueprintGeneratedClass.h"
#include "IAssetTools.h"
#include "JsonObjectConverter.h"
#include "Kismet2/BlueprintEditorUtils.h"
#include "Kismet2/KismetEditorUtilities.h"
#include "Misc/PackageName.h"
#include "Misc/Paths.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "Serialization/JsonReader.h"
#include "UObject/Package.h"
#include "UObject/UObjectGlobals.h"
#include "UObject/UObjectIterator.h"
#include "UObject/UnrealType.h"
#include "WidgetBlueprint.h"
#include "Blueprint/UserWidget.h"

// Factory includes for the various asset types — all under UnrealEd/Classes/Factories/
#include "Factories/Factory.h"
#include "Factories/MaterialFactoryNew.h"
#include "Factories/MaterialInstanceConstantFactoryNew.h"
#include "Factories/MaterialFunctionFactoryNew.h"
#include "Factories/MaterialFunctionInstanceFactory.h"
#include "Factories/MaterialFunctionMaterialLayerFactory.h"
#include "Factories/MaterialFunctionMaterialLayerBlendFactory.h"
#include "Factories/MaterialParameterCollectionFactoryNew.h"
#include "Factories/PhysicalMaterialFactoryNew.h"
#include "Factories/PhysicalMaterialMaskFactory.h"
#include "Factories/DataAssetFactory.h"
#include "Factories/FontFactory.h"
#include "Factories/WorldFactory.h"
// CurveFactory.h contains UCurveFloatFactory, UCurveLinearColorFactory, UCurveVectorFactory
#include "Factories/CurveFactory.h"
#include "Factories/CurveTableFactory.h"
#include "Factories/CompositeCurveTableFactory.h"
#include "Factories/DataTableFactory.h"
#include "Factories/SlateBrushAssetFactory.h"
#include "Factories/SlateWidgetStyleAssetFactory.h"
#include "Factories/Texture2DArrayFactory.h"
#include "Factories/TextureCubeArrayFactory.h"
#include "Factories/VolumeTextureFactory.h"
#include "Factories/Texture2dFactoryNew.h"
// Note: class name is UTexture2DFactoryNew (capital D)
#include "Factories/AnimSequenceFactory.h"
#include "Factories/AnimStreamableFactory.h"
#include "Factories/AnimCompositeFactory.h"
#include "Factories/AnimMontageFactory.h"
#include "Factories/BlendSpaceFactory1D.h"
#include "Factories/BlendSpaceFactoryNew.h"
#include "Factories/PoseAssetFactory.h"
#include "Factories/SkeletonFactory.h"
#include "Factories/PhysicsAssetFactory.h"
#include "Factories/PreviewMeshCollectionFactory.h"
#include "Factories/ForceFeedbackEffectFactory.h"
#include "Factories/ForceFeedbackAttenuationFactory.h"
#include "Factories/ParticleSystemFactoryNew.h"
#include "Factories/SubUVAnimationFactory.h"
#include "Factories/TouchInterfaceFactory.h"
#include "Factories/ObjectLibraryFactory.h"
#include "Materials/Material.h"
#include "Materials/MaterialInstanceConstant.h"
#include "Materials/MaterialFunction.h"
#include "Materials/MaterialFunctionMaterialLayer.h"
#include "Materials/MaterialFunctionMaterialLayerBlend.h"
#include "Materials/MaterialFunctionInstance.h"
#include "Materials/MaterialParameterCollection.h"
#include "PhysicalMaterials/PhysicalMaterial.h"
#include "PhysicalMaterials/PhysicalMaterialMask.h"
#include "Slate/SlateBrushAsset.h"
#include "Styling/SlateWidgetStyleAsset.h"
#include "Engine/DataTable.h"
#include "Engine/Texture2DArray.h"
#include "Engine/TextureCubeArray.h"
#include "Engine/VolumeTexture.h"
#include "Engine/Texture.h"
#include "Curves/CurveFloat.h"
#include "Curves/CurveLinearColor.h"
#include "Curves/CurveVector.h"
#include "Engine/CurveTable.h"
#include "Engine/CompositeCurveTable.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimStreamable.h"
#include "Animation/AnimComposite.h"
#include "Animation/AnimMontage.h"
#include "Animation/BlendSpace.h"
#include "Animation/BlendSpace1D.h"
#include "Animation/PoseAsset.h"
#include "Animation/Skeleton.h"
#include "PhysicsEngine/PhysicsAsset.h"
#include "Engine/PreviewMeshCollection.h"
#include "GameFramework/ForceFeedbackEffect.h"
#include "GameFramework/ForceFeedbackAttenuation.h"
#include "Haptics/HapticFeedbackEffect_Curve.h"
#include "Haptics/HapticFeedbackEffect_Buffer.h"
#include "Haptics/HapticFeedbackEffect_SoundWave.h"
#include "Particles/ParticleSystem.h"
#include "Particles/SubUVAnimation.h"
#include "GameFramework/TouchInterface.h"
#include "InputCoreTypes.h"
#include "Engine/Font.h"
#include "Engine/ObjectLibrary.h"
#include "Engine/DataAsset.h"
// User-defined types
#include "Engine/UserDefinedStruct.h"
#include "Engine/UserDefinedEnum.h"
#include "Kismet2/StructureEditorUtils.h"
#include "UserDefinedStructure/UserDefinedStructEditorData.h"
// Behavior Tree
#include "BehaviorTree/BehaviorTree.h"
// Textures used in get_asset_ir identification
#include "Engine/Texture2D.h"

using namespace ForgeMindBlueprintUtils;

// ---------------------------------------------------------------------------
// Internal helpers — topological sort and dependency resolution
// ---------------------------------------------------------------------------

namespace
{
	/**
	 * Asset type categories that the topological sort understands.
	 * Assets in the same category share the same dependency weight.
	 */
	enum class EAssetIRCategory : uint8
	{
		Enum,               // No dependencies, must be first
		Struct,             // May depend on enums
		DataTable,          // May depend on structs
		Curve,              // No dependencies
		MaterialBase,       // Materials, material functions, etc.
		MaterialInstance,   // Depends on materials / material functions
		Texture,            // No dependencies
		AnimBase,           // Skeletons, etc.
		AnimDerived,        // Depends on skeletons
		AudioEffect,        // No dependencies
		Input,              // InputAction first, then IMC
		BlueprintFamily,    // May depend on structs, enums, materials
		World,              // May depend on anything
		DataAsset,          // May depend on anything
		Other,              // Catch-all
	};

	/** Maps AssetIR type string to a topological category. */
	EAssetIRCategory ResolveAssetCategory(const FString& AssetType)
	{
		if (AssetType == TEXT("enum"))                              return EAssetIRCategory::Enum;
		if (AssetType == TEXT("struct"))                            return EAssetIRCategory::Struct;
		if (AssetType == TEXT("dataTable") || AssetType == TEXT("mirrorDataTable"))
			return EAssetIRCategory::DataTable;
		if (AssetType == TEXT("curveTable") || AssetType == TEXT("compositeCurveTable")
			|| AssetType == TEXT("curveFloat") || AssetType == TEXT("curveLinearColor")
			|| AssetType == TEXT("curveVector"))
			return EAssetIRCategory::Curve;
		if (AssetType == TEXT("material") || AssetType == TEXT("materialFunction")
			|| AssetType == TEXT("materialFunctionMaterialLayer")
			|| AssetType == TEXT("materialFunctionMaterialLayerBlend")
			|| AssetType == TEXT("materialParameterCollection")
			|| AssetType == TEXT("physicalMaterial") || AssetType == TEXT("physicalMaterialMask")
			|| AssetType == TEXT("slateBrush") || AssetType == TEXT("slateWidgetStyle"))
			return EAssetIRCategory::MaterialBase;
		if (AssetType == TEXT("materialInstanceConstant")
			|| AssetType == TEXT("materialFunctionInstance"))
			return EAssetIRCategory::MaterialInstance;
		if (AssetType == TEXT("texture2DArray") || AssetType == TEXT("textureCubeArray")
			|| AssetType == TEXT("volumeTexture") || AssetType == TEXT("virtualTextureBuilder")
			|| AssetType == TEXT("runtimeVirtualTexture"))
			return EAssetIRCategory::Texture;
		if (AssetType == TEXT("skeleton") || AssetType == TEXT("physicsAsset")
			|| AssetType == TEXT("animBoneCompressionSettings")
			|| AssetType == TEXT("animCurveCompressionSettings"))
			return EAssetIRCategory::AnimBase;
		if (AssetType == TEXT("animSequence") || AssetType == TEXT("animStreamable")
			|| AssetType == TEXT("animComposite") || AssetType == TEXT("animMontage")
			|| AssetType == TEXT("animBank") || AssetType == TEXT("blendSpace1D")
			|| AssetType == TEXT("blendSpace") || AssetType == TEXT("poseAsset")
			|| AssetType == TEXT("previewMeshCollection") || AssetType == TEXT("meshDeformerCollection"))
			return EAssetIRCategory::AnimDerived;
		if (AssetType == TEXT("forceFeedbackEffect") || AssetType == TEXT("forceFeedbackAttenuation")
			|| AssetType == TEXT("hapticFeedbackCurve") || AssetType == TEXT("hapticFeedbackBuffer")
			|| AssetType == TEXT("hapticFeedbackSoundWave") || AssetType == TEXT("particleSystem")
			|| AssetType == TEXT("subUVAnimation"))
			return EAssetIRCategory::AudioEffect;
		if (AssetType == TEXT("inputAction"))                      return EAssetIRCategory::Input;
		if (AssetType == TEXT("inputMappingContext") || AssetType == TEXT("touchInterface"))
			return EAssetIRCategory::Input;
		if (AssetType == TEXT("behaviorTree"))                     return EAssetIRCategory::BlueprintFamily;
if (AssetType == TEXT("blueprint") || AssetType == TEXT("widgetBlueprint")
			|| AssetType == TEXT("animBlueprint") || AssetType == TEXT("blueprintFunctionLibrary")
			|| AssetType == TEXT("blueprintInterface") || AssetType == TEXT("blueprintMacroLibrary")
			|| AssetType == TEXT("gameplayAbility") || AssetType == TEXT("level_blueprint"))
			return EAssetIRCategory::BlueprintFamily;
		if (AssetType == TEXT("world"))                            return EAssetIRCategory::World;
		if (AssetType == TEXT("dataAsset") || AssetType == TEXT("objectLibrary"))
			return EAssetIRCategory::DataAsset;
		return EAssetIRCategory::Other;
	}

	/** Numeric priority for a category — lower = earlier in topological order. */
	int32 CategoryPriority(EAssetIRCategory Cat)
	{
		switch (Cat)
		{
		case EAssetIRCategory::Enum:            return 0;
		case EAssetIRCategory::Struct:          return 1;
		case EAssetIRCategory::DataTable:       return 2;
		case EAssetIRCategory::Curve:          return 0;
		case EAssetIRCategory::MaterialBase:   return 0;
		case EAssetIRCategory::MaterialInstance: return 5;
		case EAssetIRCategory::Texture:        return 0;
		case EAssetIRCategory::AnimBase:       return 0;
		case EAssetIRCategory::AnimDerived:    return 5;
		case EAssetIRCategory::AudioEffect:    return 0;
		case EAssetIRCategory::Input:         return 0;
		case EAssetIRCategory::BlueprintFamily: return 10;
		case EAssetIRCategory::World:         return 20;
		case EAssetIRCategory::DataAsset:    return 15;
		default:                               return 10;
		}
	}

	/**
	 * Extracts asset path from an asset definition JSON object.
	 * Returns the normalized package path (without the .Name suffix).
	 */
	FString ExtractAssetPath(const TSharedPtr<FJsonObject>& AssetDef)
	{
		FString Path;
		AssetDef->TryGetStringField(TEXT("path"), Path);
		if (Path.IsEmpty())
		{
			AssetDef->TryGetStringField(TEXT("assetPath"), Path);
		}
		// Strip the .Name suffix if present
		int32 DotIdx = INDEX_NONE;
		if (Path.FindChar(TEXT('.'), DotIdx))
		{
			FString AfterDot = Path.Mid(DotIdx + 1);
			// Only strip if the part after the dot is the asset name
			FString FileName = FPaths::GetCleanFilename(Path.Left(DotIdx));
			if (AfterDot == FileName)
			{
				Path = Path.Left(DotIdx);
			}
		}
		return Path;
	}

	/** Extracts the asset name (last path segment) from an asset definition. */
	FString ExtractAssetName(const TSharedPtr<FJsonObject>& AssetDef)
	{
		const FString Path = ExtractAssetPath(AssetDef);
		return FPaths::GetCleanFilename(Path);
	}

	/**
	 * Scans an asset definition for type references that create dependencies.
	 * For example, a struct member of type "EWeaponRarity" depends on the
	 * enum at /Game/Enums/EWeaponRarity. A blueprint variable of type "FItemData"
	 * depends on the struct at /Game/Structs/FItemData.
	 * Returns a set of asset paths this asset depends on.
	 */
	TSet<FString> ExtractDependencies(const TSharedPtr<FJsonObject>& AssetDef)
	{
		TSet<FString> Dependencies;
		if (!AssetDef.IsValid())
		{
			return Dependencies;
		}

		FString AssetType;
		AssetDef->TryGetStringField(TEXT("type"), AssetType);
		const FString MyPath = ExtractAssetPath(AssetDef);

		// Helper: looks at a type string and tries to match it to a known asset path
		auto TryResolveTypeReference = [&](const FString& TypeStr)
		{
			if (TypeStr.IsEmpty())
			{
				return;
			}
			// Remove array/map/set wrappers
			FString CleanType = TypeStr;
			if (CleanType.StartsWith(TEXT("TArray<"))) CleanType = CleanType.Mid(7, CleanType.Len() - 8);
			if (CleanType.StartsWith(TEXT("TSet<")))  CleanType = CleanType.Mid(5, CleanType.Len() - 6);
			if (CleanType.StartsWith(TEXT("TMap<")))
			{
				// Extract value type from TMap<K,V>
				int32 CommaIdx = INDEX_NONE;
				CleanType = CleanType.Mid(5, CleanType.Len() - 6);
				if (CleanType.FindChar(TEXT(','), CommaIdx))
				{
					CleanType = CleanType.Mid(CommaIdx + 1).TrimStartAndEnd();
				}
			}
			CleanType.TrimStartAndEndInline();

			// If it starts with E or F and looks like a user-defined type, try to resolve
			if (CleanType.StartsWith(TEXT("E")) || CleanType.StartsWith(TEXT("F")))
			{
				// Try to find a matching asset in the same batch
				// We'll resolve this later in the topological sort pass
				Dependencies.Add(CleanType);
			}
		};

		// Struct members
		const TArray<TSharedPtr<FJsonValue>>* MembersArray = nullptr;
		if (AssetDef->TryGetArrayField(TEXT("members"), MembersArray))
		{
			for (const TSharedPtr<FJsonValue>& MemberVal : *MembersArray)
			{
				const TSharedPtr<FJsonObject>* MemberObj = nullptr;
				if (MemberVal.IsValid() && MemberVal->TryGetObject(MemberObj) && MemberObj->IsValid())
				{
					FString MemberType;
					(*MemberObj)->TryGetStringField(TEXT("type"), MemberType);
					TryResolveTypeReference(MemberType);
				}
			}
		}

		// Blueprint variables
		const TArray<TSharedPtr<FJsonValue>>* VarsArray = nullptr;
		if (AssetDef->TryGetArrayField(TEXT("variables"), VarsArray))
		{
			for (const TSharedPtr<FJsonValue>& VarVal : *VarsArray)
			{
				const TSharedPtr<FJsonObject>* VarObj = nullptr;
				if (VarVal.IsValid() && VarVal->TryGetObject(VarObj) && VarObj->IsValid())
				{
					FString VarType;
					(*VarObj)->TryGetStringField(TEXT("type"), VarType);
					TryResolveTypeReference(VarType);
				}
			}
		}

		// Material instance parent
		FString ParentMaterial;
		if (AssetDef->TryGetStringField(TEXT("parent"), ParentMaterial) && !ParentMaterial.IsEmpty())
		{
			Dependencies.Add(ParentMaterial);
		}

		// InputMappingContext references to InputActions
		const TArray<TSharedPtr<FJsonValue>>* MappingsArray = nullptr;
		if (AssetDef->TryGetArrayField(TEXT("mappings"), MappingsArray))
		{
			for (const TSharedPtr<FJsonValue>& MapVal : *MappingsArray)
			{
				const TSharedPtr<FJsonObject>* MapObj = nullptr;
				if (MapVal.IsValid() && MapVal->TryGetObject(MapObj) && MapObj->IsValid())
				{
					FString ActionPath;
					(*MapObj)->TryGetStringField(TEXT("action"), ActionPath);
					if (!ActionPath.IsEmpty())
					{
						Dependencies.Add(ActionPath);
					}
				}
			}
		}

		// DataTable struct reference
		FString StructRef;
		if (AssetDef->TryGetStringField(TEXT("struct"), StructRef) && !StructRef.IsEmpty())
		{
			Dependencies.Add(StructRef);
		}

		// Blueprint parentClass (if it looks like a user-defined type)
		FString ParentClass;
		if (AssetDef->TryGetStringField(TEXT("parentClass"), ParentClass) && !ParentClass.IsEmpty())
		{
			// Only add dependency if it looks like a user-defined blueprint path
			if (ParentClass.StartsWith(TEXT("/Game/")) || ParentClass.StartsWith(TEXT("/Game")))
			{
				Dependencies.Add(ParentClass);
			}
		}

		// animBlueprint targetSkeleton
		FString SkeletonRef;
		if (AssetDef->TryGetStringField(TEXT("targetSkeleton"), SkeletonRef) && !SkeletonRef.StartsWith(TEXT("Anim")))
		{
			if (!SkeletonRef.IsEmpty() && SkeletonRef.StartsWith(TEXT("/Game/")))
			{
				Dependencies.Add(SkeletonRef);
			}
		}

		Dependencies.Remove(MyPath); // self-reference guard
		return Dependencies;
	}

	/**
	 * Performs topological sort on the asset list using a category-priority
	 * + dependency-aware Kahn's algorithm. Returns false on circular
	 * dependency, filling OutCycleChain with the cycle description.
	 */
	bool TopologicalSort(
		const TArray<TSharedPtr<FJsonObject>>& Assets,
		TArray<int32>& OutOrder,
		FString& OutCycleChain)
	{
		const int32 N = Assets.Num();
		OutOrder.Reset(N);

		// Build path → index map for dependency resolution
		TMap<FString, int32> PathToIndex;
		for (int32 i = 0; i < N; ++i)
		{
			const FString Path = ExtractAssetPath(Assets[i]);
			if (!Path.IsEmpty())
			{
				PathToIndex.Add(Path, i);
			}
			// Also map by asset name (for short type references like EWeaponRarity)
			const FString Name = ExtractAssetName(Assets[i]);
			if (!Name.IsEmpty())
			{
				PathToIndex.Add(Name, i);
			}
		}

		// Build dependency graph
		TArray<TSet<int32>> Dependents; // Dependents[i] = set of indices that depend on i
		TArray<TSet<int32>> Dependencies; // Dependencies[i] = set of indices that i depends on
		Dependents.SetNum(N);
		Dependencies.SetNum(N);

		for (int32 i = 0; i < N; ++i)
		{
			const TSet<FString> Deps = ExtractDependencies(Assets[i]);
			for (const FString& Dep : Deps)
			{
				// Try to resolve the dependency to an index in this batch
				const int32* Found = PathToIndex.Find(Dep);
				if (Found == nullptr)
				{
					// Try stripping the F/E prefix
					FString Stripped = Dep;
					if (Stripped.StartsWith(TEXT("F")) || Stripped.StartsWith(TEXT("E")))
					{
						Found = PathToIndex.Find(Stripped.RightChop(1));
					}
				}
				if (Found != nullptr && *Found != i)
				{
					Dependencies[i].Add(*Found);
					Dependents[*Found].Add(i);
				}
			}
		}

		// Kahn's algorithm with category-priority tie-breaking
		TArray<int32> InDegree;
		InDegree.SetNum(N);
		for (int32 i = 0; i < N; ++i)
		{
			InDegree[i] = Dependencies[i].Num();
		}

		// Collect all zero-in-degree nodes, sorted by category priority
		TArray<int32> Ready;
		for (int32 i = 0; i < N; ++i)
		{
			if (InDegree[i] == 0)
			{
				Ready.Add(i);
			}
		}
	auto SortReady = [&Ready, &Assets]()
	{
		Ready.Sort([&Assets](int32 A, int32 B)
			{
				FString TypeA, TypeB;
				Assets[A]->TryGetStringField(TEXT("type"), TypeA);
				Assets[B]->TryGetStringField(TEXT("type"), TypeB);
				const int32 PrioA = CategoryPriority(ResolveAssetCategory(TypeA));
				const int32 PrioB = CategoryPriority(ResolveAssetCategory(TypeB));
				if (PrioA != PrioB) return PrioA < PrioB;
				// Same priority — sort by path for deterministic output
				return ExtractAssetPath(Assets[A]) < ExtractAssetPath(Assets[B]);
			});
		};

		SortReady();
		while (Ready.Num() > 0)
		{
			const int32 Next = Ready[0];
			Ready.RemoveAt(0);
			OutOrder.Add(Next);

			for (int32 DependentIdx : Dependents[Next])
			{
				--InDegree[DependentIdx];
				if (InDegree[DependentIdx] == 0)
				{
					Ready.Add(DependentIdx);
				}
			}
			SortReady();
		}

		if (OutOrder.Num() != N)
		{
			// Circular dependency detected — find the cycle
			TArray<int32> Remaining;
			for (int32 i = 0; i < N; ++i)
			{
				if (!OutOrder.Contains(i))
				{
					Remaining.Add(i);
				}
			}
			// Build a chain description from the remaining nodes
			FString Chain;
			for (int32 i = 0; i < Remaining.Num(); ++i)
			{
				if (i > 0) Chain += TEXT(" → ");
				Chain += ExtractAssetPath(Assets[Remaining[i]]);
			}
			OutCycleChain = FString::Printf(TEXT("Circular dependency detected: %s"), *Chain);
			return false;
		}

		return true;
	}

	/**
	 * Creates a UObject asset via the given factory. Wraps FAssetToolsModule
	 * to create an asset at the given package path.
	 */
	UObject* CreateAssetWithFactory(UFactory* Factory, const FString& AssetPath, UClass* AssetClass)
	{
		FString PackagePath = AssetPath;
		int32 DotIdx = INDEX_NONE;
		if (PackagePath.FindChar(TEXT('.'), DotIdx))
		{
			PackagePath = PackagePath.Left(DotIdx);
		}
		if (!FPackageName::IsValidLongPackageName(PackagePath) || !PackagePath.StartsWith(TEXT("/Game/")))
		{
			return nullptr;
		}

		// Refuse to clobber an existing asset (use AssetRegistry, not FindPackage,
		// which can return stale pointers for deleted-but-not-GC'd packages)
		FAssetRegistryModule& AssetRegModule = FModuleManager::LoadModuleChecked<FAssetRegistryModule>(TEXT("AssetRegistry"));
		IAssetRegistry& AssetRegistry = AssetRegModule.Get();
		TArray<FAssetData> ExistingAssets;
		AssetRegistry.GetAssetsByPackageName(FName(*PackagePath), ExistingAssets, /*bIncludeOnlyOnDiskAssets=*/true);
		if (ExistingAssets.Num() > 0)
		{
			return nullptr;
		}

		const FString AssetName = FPaths::GetCleanFilename(PackagePath);
		const FString PackageNameStr = PackagePath;

		FAssetToolsModule& AssetToolsModule = FModuleManager::LoadModuleChecked<FAssetToolsModule>(TEXT("AssetTools"));
		IAssetTools& AssetTools = AssetToolsModule.Get();

		UObject* CreatedAsset = AssetTools.CreateAsset(AssetName, FPackageName::GetLongPackagePath(PackageNameStr), AssetClass, Factory);
		return CreatedAsset;
	}

	/** Checks if an asset already exists at the given path. */
	bool AssetExists(const FString& AssetPath)
	{
		FString PackagePath = AssetPath;
		int32 DotIdx = INDEX_NONE;
		if (PackagePath.FindChar(TEXT('.'), DotIdx))
		{
			PackagePath = PackagePath.Left(DotIdx);
		}
		// Use AssetRegistry as the sole authority. Disk-resident orphan
		// .uasset files (from crashed sessions, etc.) are invisible to
		// AssetRegistry after a scan and should not block recreation —
		// the asset creation flow will overwrite them.
		FAssetRegistryModule& AssetRegModule = FModuleManager::LoadModuleChecked<FAssetRegistryModule>(TEXT("AssetRegistry"));
		IAssetRegistry& AssetRegistry = AssetRegModule.Get();
		TArray<FAssetData> AssetDataList;
		AssetRegistry.GetAssetsByPackageName(FName(*PackagePath), AssetDataList, /*bIncludeOnlyOnDiskAssets=*/true);
		return AssetDataList.Num() > 0;
	}

	/** Loads an existing asset or returns nullptr. */
	UObject* LoadExistingAsset(const FString& AssetPath)
	{
		return LoadObject<UObject>(nullptr, *AssetPath);
	}

	/**
	 * Determines if a blueprint-family asset type should be created as a
	 * WidgetBlueprint (when the parent class is a UserWidget derivative).
	 */
	bool IsWidgetBlueprintType(const FString& ParentClassName)
	{
		UClass* ParentClass = ForgeMindFindFirstObject<UClass>(*ParentClassName);
		if (ParentClass == nullptr && ParentClassName.Len() > 1
			&& (ParentClassName[0] == 'U' || ParentClassName[0] == 'A' || ParentClassName[0] == 'F'))
		{
			ParentClass = ForgeMindFindFirstObject<UClass>(*ParentClassName.RightChop(1));
		}
		return ParentClass != nullptr && ParentClass->IsChildOf(UUserWidget::StaticClass());
	}
}

// ===========================================================================
// HandleApplyAssetIR
// ===========================================================================

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleApplyAssetIRLegacy(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	if (!Payload.IsValid())
	{
		Result->SetBoolField(TEXT("applied"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Missing payload"));
		return Result;
	}

	// --- Step 0: Unwrap "json" string field if present ---
	// The C# BlueprintDslTool may wrap the AssetIR JSON as a string inside
	// a { "json": "..." } envelope. If the top-level payload does not have
	// a "version" field but does have a "json" string field, parse it.
	TSharedPtr<FJsonObject> EffectivePayload = Payload;
	FString JsonString;
	if (!Payload->HasField(TEXT("version")) && Payload->TryGetStringField(TEXT("json"), JsonString))
	{
		TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(JsonString);
		TSharedPtr<FJsonObject> Parsed;
		if (FJsonSerializer::Deserialize(Reader, Parsed) && Parsed.IsValid())
		{
			EffectivePayload = Parsed;
		}
		else
		{
			Result->SetBoolField(TEXT("applied"), false);
			Result->SetStringField(TEXT("reason"), TEXT("Failed to parse 'json' field as valid JSON"));
			return Result;
		}
	}

	// --- Step 1: Parse and validate top-level fields ---

	int32 Version = 0;
	if (!EffectivePayload->TryGetNumberField(TEXT("version"), Version) || Version != 2)
	{
		Result->SetBoolField(TEXT("applied"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required field: version (must be 2)"));
		return Result;
	}

	const TArray<TSharedPtr<FJsonValue>>* AssetsArray = nullptr;
	if (!EffectivePayload->TryGetArrayField(TEXT("assets"), AssetsArray) || AssetsArray->Num() == 0)
	{
		Result->SetBoolField(TEXT("applied"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required field: assets[] (non-empty array)"));
		return Result;
	}

	FString Mode = TEXT("create");
	EffectivePayload->TryGetStringField(TEXT("mode"), Mode);
	Mode = Mode.ToLower();

	bool bCompile = true;
	EffectivePayload->TryGetBoolField(TEXT("compile"), bCompile);

	bool bSave = false;
	EffectivePayload->TryGetBoolField(TEXT("save"), bSave);

	TArray<FString> Warnings;

	// Collect asset definitions into an array for topological sort
	TArray<TSharedPtr<FJsonObject>> AssetDefs;
	for (const TSharedPtr<FJsonValue>& AssetVal : *AssetsArray)
	{
		const TSharedPtr<FJsonObject>* AssetObj = nullptr;
		if (AssetVal.IsValid() && AssetVal->TryGetObject(AssetObj) && AssetObj->IsValid())
		{
			AssetDefs.Add(*AssetObj);
		}
	}

	// --- Step 2: Topological sort ---

	TArray<int32> SortedOrder;
	FString CycleError;
	if (!TopologicalSort(AssetDefs, SortedOrder, CycleError))
	{
		Result->SetBoolField(TEXT("applied"), false);
		Result->SetStringField(TEXT("reason"), CycleError);
		return Result;
	}

	// --- Step 3: Process each asset in topological order ---

	TArray<TSharedPtr<FJsonValue>> AssetResults;
	TArray<FString> CreatedAssetPaths; // for rollback
	bool bFailed = false;
	FString FailureReason;
	FString FailedAsset;

	for (int32 AssetIdx : SortedOrder)
	{
		const TSharedPtr<FJsonObject>& AssetDef = AssetDefs[AssetIdx];

		FString AssetType;
		AssetDef->TryGetStringField(TEXT("type"), AssetType);
		const FString AssetPath = ExtractAssetPath(AssetDef);

		TSharedPtr<FJsonObject> AssetResult = MakeShared<FJsonObject>();
		AssetResult->SetStringField(TEXT("path"), AssetPath);
		AssetResult->SetStringField(TEXT("type"), AssetType);

		bool bAssetCreated = false;

// --- Blueprint family ---
		if (AssetType == TEXT("blueprint") || AssetType == TEXT("widgetBlueprint")
			|| AssetType == TEXT("animBlueprint") || AssetType == TEXT("blueprintFunctionLibrary")
			|| AssetType == TEXT("blueprintInterface") || AssetType == TEXT("blueprintMacroLibrary")
			|| AssetType == TEXT("gameplayAbility") || AssetType == TEXT("level_blueprint"))
		{
			// Inline blueprint creation using fine-grained handlers.
			// (Previously delegated to HandleApplyBlueprintGraph — GraphIR v1,
			//  now removed in favor of AssetIR-native processing.)

			// --- (a) Create the blueprint asset ---
			FString ParentClass;
			AssetDef->TryGetStringField(TEXT("parentClass"), ParentClass);

			if (Mode == TEXT("create") || (Mode != TEXT("merge") && !AssetExists(AssetPath)))
			{
				// Level blueprints live inside the level asset (.umap) — there is
				// no standalone .uasset to create. The target level must already
				// be open (create_level / decision 3=B injects the current level
				// path), so reuse the level blueprint instead of calling
				// HandleCreateBlueprint (which would refuse with
				// "asset already exists at ...").
				if (AssetType == TEXT("level_blueprint"))
				{
					UBlueprint* LevelBP = ResolveBlueprint(AssetPath);
					if (LevelBP == nullptr)
					{
						bFailed = true;
						FailureReason = TEXT("level blueprint requires an open level; call create_level (or open the level) first");
						FailedAsset = FString::Printf(TEXT("%s:%s"), *AssetType, *AssetPath);
						AssetResults.Add(MakeShared<FJsonValueObject>(AssetResult));
						break;
					}
					bAssetCreated = true;
				}
				else if (ParentClass.IsEmpty())
				{
					bFailed = true;
					FailureReason = TEXT("parentClass is required for blueprint creation");
					FailedAsset = FString::Printf(TEXT("%s:%s"), *AssetType, *AssetPath);
					AssetResults.Add(MakeShared<FJsonValueObject>(AssetResult));
					break;
				}
				else
				{
					TSharedPtr<FJsonObject> CreatePayload = MakeShared<FJsonObject>();
					CreatePayload->SetStringField(TEXT("path"), AssetPath);
					CreatePayload->SetStringField(TEXT("parentClass"), ParentClass);
					if (AssetType == TEXT("widgetBlueprint"))
					{
						CreatePayload->SetStringField(TEXT("blueprintType"), TEXT("widget"));
					}
					TSharedPtr<FJsonObject> CreateResult = HandleCreateBlueprint(CreatePayload);
					CreateResult->TryGetBoolField(TEXT("created"), bAssetCreated);
					if (!bAssetCreated)
					{
						FString Reason;
						CreateResult->TryGetStringField(TEXT("reason"), Reason);
						bFailed = true;
						FailureReason = Reason;
						FailedAsset = FString::Printf(TEXT("%s:%s"), *AssetType, *AssetPath);
						AssetResults.Add(MakeShared<FJsonValueObject>(AssetResult));
						break;
					}
				}
			}
			else
			{
				bAssetCreated = AssetExists(AssetPath);
			}

			// --- (b) Apply variables ---
			int32 VarCount = 0;
			const TArray<TSharedPtr<FJsonValue>>* VarsArray = nullptr;
			if (bAssetCreated && AssetDef->TryGetArrayField(TEXT("variables"), VarsArray))
			{
				for (const TSharedPtr<FJsonValue>& VarVal : *VarsArray)
				{
					const TSharedPtr<FJsonObject>* VarObjPtr = nullptr;
					if (!VarVal.IsValid() || !VarVal->TryGetObject(VarObjPtr) || !VarObjPtr->IsValid()) continue;
					const TSharedPtr<FJsonObject>& VarDef = *VarObjPtr;

					FString VarName, VarType;
					if (!VarDef->TryGetStringField(TEXT("name"), VarName) || !VarDef->TryGetStringField(TEXT("type"), VarType))
					{
						Warnings.Add(TEXT("Skipped variable: missing name or type"));
						continue;
					}

					TSharedPtr<FJsonObject> VarPayload = MakeShared<FJsonObject>();
					VarPayload->SetStringField(TEXT("path"), AssetPath);
					VarPayload->SetStringField(TEXT("name"), VarName);
					VarPayload->SetStringField(TEXT("type"), VarType);

					FString Category;
					if (VarDef->TryGetStringField(TEXT("category"), Category))
						VarPayload->SetStringField(TEXT("category"), Category);
					bool bExpose = false;
					if (VarDef->TryGetBoolField(TEXT("exposeOnSpawn"), bExpose))
						VarPayload->SetBoolField(TEXT("exposeOnSpawn"), bExpose);
					bool bInstanceEditable = false;
					if (VarDef->TryGetBoolField(TEXT("instanceEditable"), bInstanceEditable))
						VarPayload->SetBoolField(TEXT("instanceEditable"), bInstanceEditable);

					const TSharedPtr<FJsonValue> DefaultVal = VarDef->TryGetField(TEXT("defaultValue"));
					if (DefaultVal.IsValid())
						VarPayload->SetField(TEXT("default"), DefaultVal);

					TSharedPtr<FJsonObject> VarResult = HandleAddBlueprintVariable(VarPayload);
					bool bAdded = false;
					VarResult->TryGetBoolField(TEXT("added"), bAdded);
					if (bAdded) ++VarCount;
					else
					{
						// In merge mode the variable may already exist — try to update default
						if (Mode == TEXT("merge") && DefaultVal.IsValid())
						{
							TSharedPtr<FJsonObject> SetDefaultPayload = MakeShared<FJsonObject>();
							SetDefaultPayload->SetStringField(TEXT("path"), AssetPath);
							SetDefaultPayload->SetStringField(TEXT("name"), VarName);
							SetDefaultPayload->SetField(TEXT("default"), DefaultVal);
							HandleSetBlueprintVariableDefault(SetDefaultPayload);
							++VarCount;
						}
						else
						{
							FString Reason;
							VarResult->TryGetStringField(TEXT("reason"), Reason);
							Warnings.Add(FString::Printf(TEXT("Failed to add variable '%s': %s"), *VarName, *Reason));
						}
					}
				}
			}
			AssetResult->SetNumberField(TEXT("variableCount"), VarCount);

			// --- (c) Apply components ---
			int32 CompCount = 0;
			const TArray<TSharedPtr<FJsonValue>>* CompsArray = nullptr;
			if (bAssetCreated && AssetDef->TryGetArrayField(TEXT("components"), CompsArray))
			{
				for (const TSharedPtr<FJsonValue>& CompVal : *CompsArray)
				{
					const TSharedPtr<FJsonObject>* CompObjPtr = nullptr;
					if (!CompVal.IsValid() || !CompVal->TryGetObject(CompObjPtr) || !CompObjPtr->IsValid()) continue;
					const TSharedPtr<FJsonObject>& CompDef = *CompObjPtr;

					FString CompType, CompName;
					if (!CompDef->TryGetStringField(TEXT("type"), CompType) || !CompDef->TryGetStringField(TEXT("name"), CompName))
					{
						Warnings.Add(TEXT("Skipped component: missing type or name"));
						continue;
					}

				TSharedPtr<FJsonObject> CompPayload = MakeShared<FJsonObject>();
				CompPayload->SetStringField(TEXT("path"), AssetPath);
				CompPayload->SetStringField(TEXT("class"), CompType);
				CompPayload->SetStringField(TEXT("name"), CompName);

					FString CompParent, AttachSocket;
					if (CompDef->TryGetStringField(TEXT("parent"), CompParent) && !CompParent.IsEmpty())
						CompPayload->SetStringField(TEXT("parent"), CompParent);
					if (CompDef->TryGetStringField(TEXT("attachSocket"), AttachSocket) && !AttachSocket.IsEmpty())
						CompPayload->SetStringField(TEXT("attachSocket"), AttachSocket);

					const TSharedPtr<FJsonObject>* CompPropsPtr = nullptr;
					if (CompDef->TryGetObjectField(TEXT("properties"), CompPropsPtr) && CompPropsPtr->IsValid())
					{
						CompPayload->SetObjectField(TEXT("properties"), *CompPropsPtr);
					}

					TSharedPtr<FJsonObject> CompResult = HandleAddBlueprintComponent(CompPayload);
					bool bAdded = false;
					CompResult->TryGetBoolField(TEXT("added"), bAdded);
					if (bAdded) ++CompCount;
				}
			}
			AssetResult->SetNumberField(TEXT("componentCount"), CompCount);

			// --- (d) Apply widgets (for widget blueprints) ---
			int32 WidgetCount = 0;
			const TArray<TSharedPtr<FJsonValue>>* WidgetsArray = nullptr;
			if (bAssetCreated && AssetDef->TryGetArrayField(TEXT("widgets"), WidgetsArray))
			{
				for (const TSharedPtr<FJsonValue>& WidgetVal : *WidgetsArray)
				{
					const TSharedPtr<FJsonObject>* WidgetObjPtr = nullptr;
					if (!WidgetVal.IsValid() || !WidgetVal->TryGetObject(WidgetObjPtr) || !WidgetObjPtr->IsValid()) continue;
					const TSharedPtr<FJsonObject>& WidgetDef = *WidgetObjPtr;

					FString WidgetType, WidgetName;
					if (!WidgetDef->TryGetStringField(TEXT("type"), WidgetType) || !WidgetDef->TryGetStringField(TEXT("name"), WidgetName))
					{
						Warnings.Add(TEXT("Skipped widget: missing type or name"));
						continue;
					}

                TSharedPtr<FJsonObject> WidgetPayload = MakeShared<FJsonObject>();
					WidgetPayload->SetStringField(TEXT("path"), AssetPath);
					// HandleAddWidgetChild expects 'class' not 'type'
					WidgetPayload->SetStringField(TEXT("class"), WidgetType);
					WidgetPayload->SetStringField(TEXT("name"), WidgetName);

					FString WidgetParent;
					if (WidgetDef->TryGetStringField(TEXT("parent"), WidgetParent) && !WidgetParent.IsEmpty())
						WidgetPayload->SetStringField(TEXT("parent"), WidgetParent);

					// HandleAddWidgetChild uses 'promote' not 'isVariable'
					bool bIsVariable = true;
					if (WidgetDef->TryGetBoolField(TEXT("isVariable"), bIsVariable))
						WidgetPayload->SetBoolField(TEXT("promote"), bIsVariable);

					TSharedPtr<FJsonObject> WidgetResult = HandleAddWidgetChild(WidgetPayload);
					bool bAdded = false;
					WidgetResult->TryGetBoolField(TEXT("added"), bAdded);
					if (bAdded) ++WidgetCount;

					// Apply widget properties individually via set_component_property
					const TSharedPtr<FJsonObject>* WidgetPropsPtr = nullptr;
					if (bAdded && WidgetDef->TryGetObjectField(TEXT("properties"), WidgetPropsPtr) && WidgetPropsPtr->IsValid())
					{
						for (const TPair<FString, TSharedPtr<FJsonValue>>& PropPair : (*WidgetPropsPtr)->Values)
						{
							TSharedPtr<FJsonObject> PropPayload = MakeShared<FJsonObject>();
							PropPayload->SetStringField(TEXT("path"), AssetPath);
							PropPayload->SetStringField(TEXT("component"), WidgetName);
							PropPayload->SetStringField(TEXT("property"), PropPair.Key);
							PropPayload->SetField(TEXT("value"), PropPair.Value);
							HandleSetComponentProperty(PropPayload);
						}
					}
				}
			}
			AssetResult->SetNumberField(TEXT("widgetCount"), WidgetCount);

			// --- (e) Apply functions ---
			int32 FuncCount = 0;
			const TArray<TSharedPtr<FJsonValue>>* FuncsArray = nullptr;
			if (bAssetCreated && AssetDef->TryGetArrayField(TEXT("functions"), FuncsArray))
			{
				for (const TSharedPtr<FJsonValue>& FuncVal : *FuncsArray)
				{
					const TSharedPtr<FJsonObject>* FuncObjPtr = nullptr;
					if (!FuncVal.IsValid() || !FuncVal->TryGetObject(FuncObjPtr) || !FuncObjPtr->IsValid()) continue;
					const TSharedPtr<FJsonObject>& FuncDef = *FuncObjPtr;

					FString FuncName;
					if (!FuncDef->TryGetStringField(TEXT("name"), FuncName) || FuncName.IsEmpty())
					{
						Warnings.Add(TEXT("Skipped function: missing name"));
						continue;
					}

					TSharedPtr<FJsonObject> FuncPayload = MakeShared<FJsonObject>();
					FuncPayload->SetStringField(TEXT("path"), AssetPath);
					FuncPayload->SetStringField(TEXT("name"), FuncName);

					bool bPure = false;
					if (FuncDef->TryGetBoolField(TEXT("pure"), bPure))
						FuncPayload->SetBoolField(TEXT("pure"), bPure);

					// Forward inputs as params and outputs as return values
					const TArray<TSharedPtr<FJsonValue>>* InputsArray = nullptr;
					if (FuncDef->TryGetArrayField(TEXT("inputs"), InputsArray))
						FuncPayload->SetArrayField(TEXT("params"), *InputsArray);
					const TArray<TSharedPtr<FJsonValue>>* OutputsArray = nullptr;
					if (FuncDef->TryGetArrayField(TEXT("outputs"), OutputsArray))
						FuncPayload->SetArrayField(TEXT("outputs"), *OutputsArray);

					// Forward function-local variable declarations so the bridge can
					// create UE function-local variables (on the entry node).
					const TArray<TSharedPtr<FJsonValue>>* LocalsArray = nullptr;
					if (FuncDef->TryGetArrayField(TEXT("locals"), LocalsArray))
						FuncPayload->SetArrayField(TEXT("locals"), *LocalsArray);

					TSharedPtr<FJsonObject> FuncResult = HandleCreateFunctionGraph(FuncPayload);
					bool bCreated = false;
					FuncResult->TryGetBoolField(TEXT("created"), bCreated);
					if (bCreated) ++FuncCount;
				}
			}
			AssetResult->SetNumberField(TEXT("functionCount"), FuncCount);

			// --- (e2) Apply event graphs (nodes + connections) ---
			// Pre-compile so the SkeletonGeneratedClass is current: event nodes
			// (BeginPlay, ActorHit, ...) are found via FindFunctionByName on the
			// skeleton class, which must reflect any variables/components added
			// in steps (b)–(d) above.
			if (bAssetCreated && bCompile)
			{
				TSharedPtr<FJsonObject> PreCompilePayload = MakeShared<FJsonObject>();
				PreCompilePayload->SetStringField(TEXT("path"), AssetPath);
				HandleCompileBlueprint(PreCompilePayload);
			}

			int32 NodeCount = 0;
			int32 ConnCount = 0;
			const TArray<TSharedPtr<FJsonValue>>* GraphsArray = nullptr;
			if (bAssetCreated && AssetDef->TryGetArrayField(TEXT("graphs"), GraphsArray))
			{
				for (const TSharedPtr<FJsonValue>& GraphVal : *GraphsArray)
				{
					const TSharedPtr<FJsonObject>* GraphObjPtr = nullptr;
					if (!GraphVal.IsValid() || !GraphVal->TryGetObject(GraphObjPtr) || !GraphObjPtr->IsValid()) continue;
					const TSharedPtr<FJsonObject>& GraphDef = *GraphObjPtr;

					FString GraphName = TEXT("EventGraph");
					GraphDef->TryGetStringField(TEXT("name"), GraphName);

					// The default event graph always exists on blueprints.
					// We add nodes to it directly.

					// Track DSL node IDs → real UE node GUIDs for connections
					TMap<FString, FString> DslIdToUeGuid;

					// --- Add nodes ---
					const TArray<TSharedPtr<FJsonValue>>* GNodesArray = nullptr;
					if (GraphDef->TryGetArrayField(TEXT("nodes"), GNodesArray))
					{
						for (const TSharedPtr<FJsonValue>& NodeVal : *GNodesArray)
						{
							const TSharedPtr<FJsonObject>* NodeObjPtr = nullptr;
							if (!NodeVal.IsValid() || !NodeVal->TryGetObject(NodeObjPtr) || !NodeObjPtr->IsValid()) continue;
							const TSharedPtr<FJsonObject>& NodeDef = *NodeObjPtr;

							FString NodeId, NodeType;
							if (!NodeDef->TryGetStringField(TEXT("id"), NodeId) || !NodeDef->TryGetStringField(TEXT("type"), NodeType))
							{
								Warnings.Add(TEXT("Skipped graph node: missing id or type"));
								continue;
							}

							FString NodeName;
							NodeDef->TryGetStringField(TEXT("name"), NodeName);

							FString NodeClass;
							NodeDef->TryGetStringField(TEXT("class"), NodeClass);

							// Determine the kind from node type
						FString Kind;
					if (NodeType == TEXT("VariableGet")) Kind = TEXT("getter");
					else if (NodeType == TEXT("VariableSet")) Kind = TEXT("setter");
					else if (NodeType == TEXT("MacroInstance")) Kind = TEXT("macro");
					else if (NodeType == TEXT("Branch")) Kind = TEXT("function");
					else if (NodeType == TEXT("Self")) Kind = TEXT("self");
					// Timeline node (UK2Node_Timeline): the bridge creates the
					// blueprint's timeline template + node with a default float track.
					else if (NodeType == TEXT("Timeline")) Kind = TEXT("timeline");
					// Call parent function implementation in subclass (Blueprint "Parent: FunctionName" node).
					else if (NodeType == TEXT("CallParentFunction")) Kind = TEXT("callParentFunction");
					else Kind = TEXT("function"); // default: function/event

							// Map well-known node types to their event kind
							if (NodeType == TEXT("Event") || NodeClass == TEXT("BeginPlay") || NodeClass == TEXT("Tick"))
							{
								Kind = TEXT("event");
							}

							TSharedPtr<FJsonObject> NodePayload = MakeShared<FJsonObject>();
							NodePayload->SetStringField(TEXT("path"), AssetPath);
							NodePayload->SetStringField(TEXT("kind"), Kind);
							NodePayload->SetStringField(TEXT("name"), NodeName.IsEmpty() ? NodeClass : NodeName);
							NodePayload->SetStringField(TEXT("graphName"), GraphName);

							if (!NodeClass.IsEmpty())
							{
								NodePayload->SetStringField(TEXT("class"), NodeClass);
							}

							// Timeline length in seconds (DSL [Timeline("T", 1.0f)]):
							// forwarded as a top-level number so the timeline kind can
							// size the template. Properties alone cannot carry it -
							// they are applied as pin defaults after node creation.
							double TimelineLengthValue = 0.0;
							if (NodeDef->TryGetNumberField(TEXT("length"), TimelineLengthValue))
							{
								NodePayload->SetNumberField(TEXT("length"), TimelineLengthValue);
							}

							// Custom event parameters: forward the IR node's
							// 'params' array ([{name, type}, ...]) so
							// HandleAddBlueprintNode creates the event's
							// user-defined output pins (e.g. OnOverlap's
							// 'other'). Without this the entry node has no
							// parameter pins and entry→node data connections
							// fail.
							const TArray<TSharedPtr<FJsonValue>>* NodeParamsArray = nullptr;
							if (NodeDef->TryGetArrayField(TEXT("params"), NodeParamsArray) && NodeParamsArray->Num() > 0)
							{
								NodePayload->SetArrayField(TEXT("params"), *NodeParamsArray);
							}

							// Properties (pin defaults etc.)
							const TSharedPtr<FJsonObject>* PropsPtr = nullptr;
							if (NodeDef->TryGetObjectField(TEXT("properties"), PropsPtr) && PropsPtr->IsValid())
							{
								NodePayload->SetObjectField(TEXT("properties"), *PropsPtr);
							}

							TSharedPtr<FJsonObject> NodeResult = HandleAddBlueprintNode(NodePayload);
							bool bNodeAdded = false;
							FString RealNodeId;
							NodeResult->TryGetBoolField(TEXT("added"), bNodeAdded);
							NodeResult->TryGetStringField(TEXT("nodeId"), RealNodeId);
							if (bNodeAdded && !RealNodeId.IsEmpty())
							{
								DslIdToUeGuid.Add(NodeId, RealNodeId);
								++NodeCount;

								// Apply pin default values from the "properties" object.
								// Keys are positional names like __arg0, __arg1, ...
								// HandleSetPinDefaultValue resolves them to actual UE
								// pin names via FindPinByIndex.
								const TSharedPtr<FJsonObject>* NodePropsPtr = nullptr;
								if (NodeDef->TryGetObjectField(TEXT("properties"), NodePropsPtr) && NodePropsPtr->IsValid())
								{
									for (const TPair<FString, TSharedPtr<FJsonValue>>& PinProp : (*NodePropsPtr)->Values)
									{
										// Skip empty-string defaults (used as markers, not real values)
										if (PinProp.Value->Type == EJson::String && PinProp.Value->AsString().IsEmpty())
											continue;

										FString PinValueStr;
										switch (PinProp.Value->Type)
										{
										case EJson::String: PinValueStr = PinProp.Value->AsString(); break;
										case EJson::Number: PinValueStr = FString::Printf(TEXT("%g"), PinProp.Value->AsNumber()); break;
										case EJson::Boolean: PinValueStr = PinProp.Value->AsBool() ? TEXT("true") : TEXT("false"); break;
										default: continue;
										}

										TSharedPtr<FJsonObject> PinPayload = MakeShared<FJsonObject>();
										PinPayload->SetStringField(TEXT("path"), AssetPath);
										PinPayload->SetStringField(TEXT("node"), RealNodeId);
										PinPayload->SetStringField(TEXT("pin"), PinProp.Key);
										PinPayload->SetStringField(TEXT("value"), PinValueStr);
										HandleSetPinDefaultValue(PinPayload);
									}
								}
							}
							else
							{
								FString Reason;
								NodeResult->TryGetStringField(TEXT("reason"), Reason);
								Warnings.Add(FString::Printf(TEXT("Failed to add graph node '%s' (type=%s, name=%s): %s"), *NodeId, *NodeType, *NodeName, *Reason));
							}
						}
					}

					// --- Add connections ---
					const TArray<TSharedPtr<FJsonValue>>* ConnsArray = nullptr;
					if (GraphDef->TryGetArrayField(TEXT("connections"), ConnsArray))
					{
						for (const TSharedPtr<FJsonValue>& ConnVal : *ConnsArray)
						{
							const TSharedPtr<FJsonObject>* ConnObjPtr = nullptr;
							if (!ConnVal.IsValid() || !ConnVal->TryGetObject(ConnObjPtr) || !ConnObjPtr->IsValid()) continue;
							const TSharedPtr<FJsonObject>& ConnDef = *ConnObjPtr;

							FString FromDslId, FromPin, ToDslId, ToPin;
							if (!ConnDef->TryGetStringField(TEXT("from"), FromDslId) || !ConnDef->TryGetStringField(TEXT("fromPin"), FromPin)
								|| !ConnDef->TryGetStringField(TEXT("to"), ToDslId) || !ConnDef->TryGetStringField(TEXT("toPin"), ToPin))
							{
								Warnings.Add(TEXT("Skipped connection: missing from/fromPin/to/toPin"));
								continue;
							}

							// Resolve DSL IDs to real UE node GUIDs
							const FString* FromReal = DslIdToUeGuid.Find(FromDslId);
							const FString* ToReal = DslIdToUeGuid.Find(ToDslId);
							if (FromReal == nullptr || ToReal == nullptr)
							{
								Warnings.Add(FString::Printf(TEXT("Skipped connection: unresolved node id (from=%s, to=%s)"), *FromDslId, *ToDslId));
								continue;
							}

							TSharedPtr<FJsonObject> ConnPayload = MakeShared<FJsonObject>();
							ConnPayload->SetStringField(TEXT("path"), AssetPath);
							ConnPayload->SetStringField(TEXT("fromNode"), *FromReal);
							ConnPayload->SetStringField(TEXT("fromPin"), FromPin);
							ConnPayload->SetStringField(TEXT("toNode"), *ToReal);
							ConnPayload->SetStringField(TEXT("toPin"), ToPin);

						TSharedPtr<FJsonObject> ConnResult = HandleConnectBlueprintPins(ConnPayload);
						bool bConnLinked = false;
						ConnResult->TryGetBoolField(TEXT("connected"), bConnLinked);
						if (bConnLinked)
						{
							++ConnCount;
						}
						else
						{
							// Connection failure must hard-fail (not silent skip): when schema rejects, reason carries specifics (type mismatch and no auto-conversion node, etc).
							// Silently discarding creates a false pass where compiles succeed but variable is never actually wired.
							FString ConnReason;
							ConnResult->TryGetStringField(TEXT("reason"), ConnReason);
							bFailed = true;
							FailureReason = FString::Printf(TEXT("Connection failed (%s.%s -> %s.%s): %s"), *FromDslId, *FromPin, *ToDslId, *ToPin, *ConnReason);
							FailedAsset = FString::Printf(TEXT("%s:%s"), *AssetType, *AssetPath);
							break;
						}
						}
					}
				}
			}
			AssetResult->SetNumberField(TEXT("graphNodeCount"), NodeCount);
			AssetResult->SetNumberField(TEXT("graphConnectionCount"), ConnCount);

			// --- (e3) Apply function bodies (nodes + connections) ---
			// Step (e) only created the function shells: a graph holding the
			// FunctionEntry / FunctionResult nodes and the signature. The body
			// nodes and connections live in the "functions" array of the AssetIR
			// and are applied here, onto the graph named after each function.
			const TArray<TSharedPtr<FJsonValue>>* FuncBodyArray = nullptr;
			if (bAssetCreated && AssetDef->TryGetArrayField(TEXT("functions"), FuncBodyArray))
			{
				UBlueprint* FuncOwnerBP = LoadObject<UBlueprint>(nullptr, *AssetPath);

				for (const TSharedPtr<FJsonValue>& FuncBodyVal : *FuncBodyArray)
				{
					const TSharedPtr<FJsonObject>* FuncBodyObjPtr = nullptr;
					if (!FuncBodyVal.IsValid() || !FuncBodyVal->TryGetObject(FuncBodyObjPtr) || !FuncBodyObjPtr->IsValid()) continue;
					const TSharedPtr<FJsonObject>& FuncBodyDef = *FuncBodyObjPtr;

					FString FuncName;
					if (!FuncBodyDef->TryGetStringField(TEXT("name"), FuncName) || FuncName.IsEmpty()) continue;

					UEdGraph* FuncGraph = nullptr;
					if (FuncOwnerBP != nullptr)
					{
						for (UEdGraph* Candidate : FuncOwnerBP->FunctionGraphs)
						{
							if (Candidate != nullptr && Candidate->GetName() == FuncName)
							{
								FuncGraph = Candidate;
								break;
							}
						}
					}
					if (FuncGraph == nullptr)
					{
						Warnings.Add(FString::Printf(TEXT("Function graph '%s' not found - body nodes skipped"), *FuncName));
						continue;
					}

					UEdGraphNode* EntryNode = nullptr;
					UEdGraphNode* ResultNode = nullptr;
					for (UEdGraphNode* GraphNode : FuncGraph->Nodes)
					{
						if (GraphNode == nullptr) continue;
						const FString NodeClassName = GraphNode->GetClass()->GetName();
						if (NodeClassName == TEXT("K2Node_FunctionEntry")) EntryNode = GraphNode;
						else if (NodeClassName == TEXT("K2Node_FunctionResult")) ResultNode = GraphNode;
					}
					if (EntryNode == nullptr || ResultNode == nullptr)
					{
						Warnings.Add(FString::Printf(TEXT("Function graph '%s' is missing its entry/result node - body nodes skipped"), *FuncName));
						continue;
					}
					const FString EntryGuidStr = EntryNode->NodeGuid.ToString();
					const FString ResultGuidStr = ResultNode->NodeGuid.ToString();

					// Parameter names: inputs are read from the FunctionEntry node's
					// user-defined output pins; out parameters are written to the
					// FunctionResult node's input pins. ReturnValue has its own
					// dedicated pin and must not be treated as an out parameter.
					TSet<FString> InputParamNames;
					TSet<FString> OutputParamNames;
					auto CollectParamNames = [](const TSharedPtr<FJsonObject>& FuncObj, const TCHAR* Field, TSet<FString>& Out)
					{
						const TArray<TSharedPtr<FJsonValue>>* ParamArray = nullptr;
						if (!FuncObj->TryGetArrayField(Field, ParamArray)) return;
						for (const TSharedPtr<FJsonValue>& ParamVal : *ParamArray)
						{
							const TSharedPtr<FJsonObject>* ParamObjPtr = nullptr;
							if (!ParamVal.IsValid() || !ParamVal->TryGetObject(ParamObjPtr) || !ParamObjPtr->IsValid()) continue;
							FString ParamName;
							if ((*ParamObjPtr)->TryGetStringField(TEXT("name"), ParamName) && !ParamName.IsEmpty()) Out.Add(ParamName);
						}
					};
					CollectParamNames(FuncBodyDef, TEXT("inputs"), InputParamNames);
					CollectParamNames(FuncBodyDef, TEXT("outputs"), OutputParamNames);
					OutputParamNames.Remove(TEXT("ReturnValue"));

					TMap<FString, FString> DslIdToUeGuid;
					TMap<FString, TPair<FString, FString>> ParamAlias;

					FString EntryDslId, ResultDslId;
					FuncBodyDef->TryGetStringField(TEXT("entryNode"), EntryDslId);
					FuncBodyDef->TryGetStringField(TEXT("resultNode"), ResultDslId);
					if (!EntryDslId.IsEmpty()) DslIdToUeGuid.Add(EntryDslId, EntryGuidStr);
					if (!ResultDslId.IsEmpty()) DslIdToUeGuid.Add(ResultDslId, ResultGuidStr);

					// --- Body nodes ---
					const TArray<TSharedPtr<FJsonValue>>* BodyNodesArray = nullptr;
					if (FuncBodyDef->TryGetArrayField(TEXT("nodes"), BodyNodesArray))
					{
						for (const TSharedPtr<FJsonValue>& BodyNodeVal : *BodyNodesArray)
						{
							const TSharedPtr<FJsonObject>* BodyNodeObjPtr = nullptr;
							if (!BodyNodeVal.IsValid() || !BodyNodeVal->TryGetObject(BodyNodeObjPtr) || !BodyNodeObjPtr->IsValid()) continue;
							const TSharedPtr<FJsonObject>& BodyNode = *BodyNodeObjPtr;

							FString NodeId, NodeType, NodeName, NodeClass;
							if (!BodyNode->TryGetStringField(TEXT("id"), NodeId) || !BodyNode->TryGetStringField(TEXT("type"), NodeType)) continue;
							BodyNode->TryGetStringField(TEXT("name"), NodeName);
							BodyNode->TryGetStringField(TEXT("class"), NodeClass);

							// The function's own header nodes already exist.
							if (NodeType == TEXT("FunctionEntry") || NodeType == TEXT("FunctionResult")) continue;

							// Reading an input parameter is a pin on FunctionEntry and
							// writing an out parameter is a pin on FunctionResult - they
							// are not class variables, so no UE node is created. Alias
							// the DSL node id to the matching header node pin instead.
							if (NodeType == TEXT("VariableGet") && InputParamNames.Contains(NodeName))
							{
								ParamAlias.Add(NodeId, TPair<FString, FString>(EntryGuidStr, NodeName));
								continue;
							}
							if (NodeType == TEXT("VariableSet") && OutputParamNames.Contains(NodeName))
							{
								ParamAlias.Add(NodeId, TPair<FString, FString>(ResultGuidStr, NodeName));
								continue;
							}

							FString Kind;
							if (NodeType == TEXT("VariableGet")) Kind = TEXT("getter");
							else if (NodeType == TEXT("VariableSet")) Kind = TEXT("setter");
							else if (NodeType == TEXT("MacroInstance")) Kind = TEXT("macro");
							else if (NodeType == TEXT("Self")) Kind = TEXT("self");
							else Kind = TEXT("function");

							TSharedPtr<FJsonObject> NodePayload = MakeShared<FJsonObject>();
							NodePayload->SetStringField(TEXT("path"), AssetPath);
							NodePayload->SetStringField(TEXT("kind"), Kind);
							NodePayload->SetStringField(TEXT("name"), NodeName.IsEmpty() ? NodeClass : NodeName);
							NodePayload->SetStringField(TEXT("graphName"), FuncName);
							if (!NodeClass.IsEmpty()) NodePayload->SetStringField(TEXT("class"), NodeClass);

							const TSharedPtr<FJsonObject>* PropsPtr = nullptr;
							if (BodyNode->TryGetObjectField(TEXT("properties"), PropsPtr) && PropsPtr->IsValid())
							{
								NodePayload->SetObjectField(TEXT("properties"), *PropsPtr);
							}

							TSharedPtr<FJsonObject> NodeResult = HandleAddBlueprintNode(NodePayload);
							bool bNodeAdded = false;
							FString RealNodeId;
							NodeResult->TryGetBoolField(TEXT("added"), bNodeAdded);
							NodeResult->TryGetStringField(TEXT("nodeId"), RealNodeId);
							if (bNodeAdded && !RealNodeId.IsEmpty())
							{
								DslIdToUeGuid.Add(NodeId, RealNodeId);
								++NodeCount;

								// Apply literal pin defaults from the node's "properties"
								// object (e.g. the numeric operand of a binary expression
								// like Shield / 50.0f). This mirrors the identical handling
								// already done for top-level graph nodes above; without it,
								// function-body literals are silently dropped and the unlinked
								// input pins keep the engine's default (1.0 for divide, 0.0 for
								// multiply) instead of the intended constant.
								const TSharedPtr<FJsonObject>* BodyPropsPtr = nullptr;
								if (BodyNode->TryGetObjectField(TEXT("properties"), BodyPropsPtr) && BodyPropsPtr->IsValid())
								{
									for (const TPair<FString, TSharedPtr<FJsonValue>>& PinProp : (*BodyPropsPtr)->Values)
									{
										if (PinProp.Value->Type == EJson::String && PinProp.Value->AsString().IsEmpty())
											continue;
										FString PinValueStr;
										switch (PinProp.Value->Type)
										{
										case EJson::String: PinValueStr = PinProp.Value->AsString(); break;
										case EJson::Number: PinValueStr = FString::Printf(TEXT("%g"), PinProp.Value->AsNumber()); break;
										case EJson::Boolean: PinValueStr = PinProp.Value->AsBool() ? TEXT("true") : TEXT("false"); break;
										default: continue;
										}
										TSharedPtr<FJsonObject> PinPayload = MakeShared<FJsonObject>();
										PinPayload->SetStringField(TEXT("path"), AssetPath);
										PinPayload->SetStringField(TEXT("node"), RealNodeId);
										PinPayload->SetStringField(TEXT("pin"), PinProp.Key);
										PinPayload->SetStringField(TEXT("value"), PinValueStr);
										HandleSetPinDefaultValue(PinPayload);
									}
								}
							}
							else
							{
								FString Reason;
								NodeResult->TryGetStringField(TEXT("reason"), Reason);
								Warnings.Add(FString::Printf(TEXT("Failed to add function '%s' node '%s' (type=%s, name=%s): %s"), *FuncName, *NodeId, *NodeType, *NodeName, *Reason));
							}
						}
					}

					// --- Body connections ---
					const TArray<TSharedPtr<FJsonValue>>* BodyConnsArray = nullptr;
					if (FuncBodyDef->TryGetArrayField(TEXT("connections"), BodyConnsArray))
					{
						for (const TSharedPtr<FJsonValue>& BodyConnVal : *BodyConnsArray)
						{
							const TSharedPtr<FJsonObject>* BodyConnObjPtr = nullptr;
							if (!BodyConnVal.IsValid() || !BodyConnVal->TryGetObject(BodyConnObjPtr) || !BodyConnObjPtr->IsValid()) continue;
							const TSharedPtr<FJsonObject>& BodyConn = *BodyConnObjPtr;

							FString FromDslId, FromPin, ToDslId, ToPin;
							if (!BodyConn->TryGetStringField(TEXT("from"), FromDslId) || !BodyConn->TryGetStringField(TEXT("fromPin"), FromPin)
								|| !BodyConn->TryGetStringField(TEXT("to"), ToDslId) || !BodyConn->TryGetStringField(TEXT("toPin"), ToPin))
							{
								Warnings.Add(TEXT("Skipped function connection: missing from/fromPin/to/toPin"));
								continue;
							}

							FString FromReal, ToReal, FromRealPin = FromPin, ToRealPin = ToPin;
							if (const TPair<FString, FString>* FromAlias = ParamAlias.Find(FromDslId))
							{
								FromReal = FromAlias->Key;
								FromRealPin = FromAlias->Value;
							}
							else if (const FString* FoundFrom = DslIdToUeGuid.Find(FromDslId))
							{
								FromReal = *FoundFrom;
							}
							if (const TPair<FString, FString>* ToAlias = ParamAlias.Find(ToDslId))
							{
								ToReal = ToAlias->Key;
								ToRealPin = ToAlias->Value;
							}
							else if (const FString* FoundTo = DslIdToUeGuid.Find(ToDslId))
							{
								ToReal = *FoundTo;
							}

							if (FromReal.IsEmpty() || ToReal.IsEmpty())
							{
								Warnings.Add(FString::Printf(TEXT("Skipped function '%s' connection: unresolved node id (from=%s, to=%s)"), *FuncName, *FromDslId, *ToDslId));
								continue;
							}

							TSharedPtr<FJsonObject> ConnPayload = MakeShared<FJsonObject>();
							ConnPayload->SetStringField(TEXT("path"), AssetPath);
							ConnPayload->SetStringField(TEXT("fromNode"), FromReal);
							ConnPayload->SetStringField(TEXT("fromPin"), FromRealPin);
							ConnPayload->SetStringField(TEXT("toNode"), ToReal);
							ConnPayload->SetStringField(TEXT("toPin"), ToRealPin);

							TSharedPtr<FJsonObject> ConnResult = HandleConnectBlueprintPins(ConnPayload);
							bool bConnLinked = false;
							ConnResult->TryGetBoolField(TEXT("connected"), bConnLinked);
							if (bConnLinked)
							{
								++ConnCount;
							}
							else
							{
								FString Reason;
								ConnResult->TryGetStringField(TEXT("reason"), Reason);
								Warnings.Add(FString::Printf(TEXT("Function '%s' connection failed (%s -> %s): %s"), *FuncName, *FromRealPin, *ToRealPin, *Reason));
							}
						}
					}
				}

				AssetResult->SetNumberField(TEXT("graphNodeCount"), NodeCount);
				AssetResult->SetNumberField(TEXT("graphConnectionCount"), ConnCount);
			}

			// --- (f) Compile to make new variables/components visible ---
			if (bAssetCreated && bCompile)
			{
				TSharedPtr<FJsonObject> CompilePayload = MakeShared<FJsonObject>();
				CompilePayload->SetStringField(TEXT("path"), AssetPath);
				HandleCompileBlueprint(CompilePayload);
			}
		}
		// --- Struct ---
		else if (AssetType == TEXT("struct"))
		{
			if (Mode == TEXT("create"))
			{
				TSharedPtr<FJsonObject> CreatePayload = MakeShared<FJsonObject>();
				CreatePayload->SetStringField(TEXT("path"), AssetPath);
				TSharedPtr<FJsonObject> CreateResult = HandleCreateBlueprintStruct(CreatePayload);
				CreateResult->TryGetBoolField(TEXT("created"), bAssetCreated);
				if (!bAssetCreated)
				{
					FString Reason;
					CreateResult->TryGetStringField(TEXT("reason"), Reason);
					Warnings.Add(FString::Printf(TEXT("struct '%s' create failed: %s"), *AssetPath, *Reason));
				}
			}
			else
			{
				bAssetCreated = AssetExists(AssetPath);
			}

			// Add members
			if (bAssetCreated)
			{
				int32 MemberCount = 0;
				const TArray<TSharedPtr<FJsonValue>>* MembersArray = nullptr;
				if (AssetDef->TryGetArrayField(TEXT("members"), MembersArray))
				{
					for (const TSharedPtr<FJsonValue>& MemberVal : *MembersArray)
					{
						const TSharedPtr<FJsonObject>* MemberObj = nullptr;
						if (MemberVal.IsValid() && MemberVal->TryGetObject(MemberObj) && MemberObj->IsValid())
						{
							TSharedPtr<FJsonObject> MemberPayload = MakeShared<FJsonObject>();
					FJsonObject::Duplicate(*MemberObj, MemberPayload);
								MemberPayload->SetStringField(TEXT("path"), AssetPath);
								TSharedPtr<FJsonObject> MemberResult = HandleAddStructMember(MemberPayload);
								bool bAdded = false;
								MemberResult->TryGetBoolField(TEXT("added"), bAdded);
								if (bAdded) ++MemberCount;
						}
					}
				}
				AssetResult->SetNumberField(TEXT("memberCount"), MemberCount);

				// --- Secondary validation: remove leftover default members ---
				// CreateUserDefinedStruct always inserts a single default boolean
				// member. When AssetIR creates the struct (without passing members
				// to HandleCreateBlueprintStruct), that default member survives.
				// After adding the caller's members, scan for any member whose
				// internal VarName starts with "MemberVar_" and remove it.
				if (Mode == TEXT("create") && MembersArray != nullptr && MembersArray->Num() > 0)
				{
					UUserDefinedStruct* Struct = LoadObject<UUserDefinedStruct>(nullptr, *AssetPath);
					if (Struct != nullptr)
					{
						// Snapshot default member Guids before removing
						TArray<FGuid> DefaultGuids;
						{
							const TArray<FStructVariableDescription>& VarDescs = FStructureEditorUtils::GetVarDesc(Struct);
							for (const FStructVariableDescription& Desc : VarDescs)
							{
								if (Desc.VarName.ToString().StartsWith(TEXT("MemberVar_")))
								{
									DefaultGuids.Add(Desc.VarGuid);
								}
							}
						}
						for (const FGuid& DefaultGuid : DefaultGuids)
						{
							FStructureEditorUtils::RemoveVariable(Struct, DefaultGuid);
						}
						if (DefaultGuids.Num() > 0)
						{
							FStructureEditorUtils::OnStructureChanged(Struct, FStructureEditorUtils::EStructureEditorChangeInfo::RemovedVariable);
							Warnings.Add(FString::Printf(TEXT("Removed %d leftover default member(s) from struct '%s'"), DefaultGuids.Num(), *AssetPath));
						}
					}
				}
			}
		}
		// --- Enum ---
		else if (AssetType == TEXT("enum"))
		{
			if (Mode == TEXT("create"))
			{
				TSharedPtr<FJsonObject> CreatePayload = MakeShared<FJsonObject>();
				CreatePayload->SetStringField(TEXT("path"), AssetPath);
				TSharedPtr<FJsonObject> CreateResult = HandleCreateBlueprintEnum(CreatePayload);
				CreateResult->TryGetBoolField(TEXT("created"), bAssetCreated);
				if (!bAssetCreated)
				{
					FString Reason;
					CreateResult->TryGetStringField(TEXT("reason"), Reason);
					Warnings.Add(FString::Printf(TEXT("enum '%s' create failed: %s"), *AssetPath, *Reason));
				}
			}
			else
			{
				bAssetCreated = AssetExists(AssetPath);
			}

			if (bAssetCreated)
			{
				int32 EntryCount = 0;
				const TArray<TSharedPtr<FJsonValue>>* EntriesArray = nullptr;
				if (AssetDef->TryGetArrayField(TEXT("entries"), EntriesArray))
				{
					for (const TSharedPtr<FJsonValue>& EntryVal : *EntriesArray)
					{
						const TSharedPtr<FJsonObject>* EntryObj = nullptr;
						if (EntryVal.IsValid() && EntryVal->TryGetObject(EntryObj) && EntryObj->IsValid())
						{
							TSharedPtr<FJsonObject> EntryPayload = MakeShared<FJsonObject>();
					FJsonObject::Duplicate(*EntryObj, EntryPayload);
							EntryPayload->SetStringField(TEXT("path"), AssetPath);
							TSharedPtr<FJsonObject> EntryResult = HandleAddEnumEntry(EntryPayload);
							bool bAdded = false;
							EntryResult->TryGetBoolField(TEXT("added"), bAdded);
							if (bAdded) ++EntryCount;
						}
					}
				}
				AssetResult->SetNumberField(TEXT("entryCount"), EntryCount);
			}
		}
		// --- Factory-driven asset types ---
		//
		// For materials, curves, animations, audio, etc. we use the
		// corresponding UFactory subclass to create the asset, then
		// optionally apply properties via reflection.
		else
		{
			UFactory* Factory = nullptr;
			UClass* AssetClass = nullptr;

			if (AssetType == TEXT("material"))
			{
				Factory = NewObject<UMaterialFactoryNew>();
				AssetClass = UMaterial::StaticClass();
			}
			else if (AssetType == TEXT("materialInstanceConstant"))
			{
				Factory = NewObject<UMaterialInstanceConstantFactoryNew>();
				AssetClass = UMaterialInstanceConstant::StaticClass();
				// Set initial parent if specified
				FString ParentPath;
				if (AssetDef->TryGetStringField(TEXT("parent"), ParentPath) && !ParentPath.IsEmpty())
				{
					UMaterial* ParentMaterial = LoadObject<UMaterial>(nullptr, *ParentPath);
					if (ParentMaterial != nullptr)
					{
						Cast<UMaterialInstanceConstantFactoryNew>(Factory)->InitialParent = ParentMaterial;
					}
					else
					{
						Warnings.Add(FString::Printf(TEXT("Material parent '%s' not found for '%s'"), *ParentPath, *AssetPath));
					}
				}
			}
			else if (AssetType == TEXT("materialFunction"))
			{
				Factory = NewObject<UMaterialFunctionFactoryNew>();
				AssetClass = UMaterialFunction::StaticClass();
			}
			else if (AssetType == TEXT("materialFunctionMaterialLayer"))
			{
				Factory = NewObject<UMaterialFunctionMaterialLayerFactory>();
				AssetClass = UMaterialFunctionMaterialLayer::StaticClass();
			}
			else if (AssetType == TEXT("materialFunctionMaterialLayerBlend"))
			{
				Factory = NewObject<UMaterialFunctionMaterialLayerBlendFactory>();
				AssetClass = UMaterialFunctionMaterialLayerBlend::StaticClass();
			}
			else if (AssetType == TEXT("materialFunctionInstance"))
			{
				Factory = NewObject<UMaterialFunctionInstanceFactory>();
				AssetClass = UMaterialFunctionInstance::StaticClass();
			}
			else if (AssetType == TEXT("materialParameterCollection"))
			{
				Factory = NewObject<UMaterialParameterCollectionFactoryNew>();
				AssetClass = UMaterialParameterCollection::StaticClass();
			}
			else if (AssetType == TEXT("physicalMaterial"))
			{
				Factory = NewObject<UPhysicalMaterialFactoryNew>();
				AssetClass = UPhysicalMaterial::StaticClass();
			}
			else if (AssetType == TEXT("physicalMaterialMask"))
			{
				Factory = NewObject<UPhysicalMaterialMaskFactory>();
				AssetClass = UPhysicalMaterialMask::StaticClass();
			}
			else if (AssetType == TEXT("slateBrush"))
			{
				Factory = NewObject<USlateBrushAssetFactory>();
				AssetClass = USlateBrushAsset::StaticClass();
			}
			else if (AssetType == TEXT("slateWidgetStyle"))
			{
				Factory = NewObject<USlateWidgetStyleAssetFactory>();
				AssetClass = USlateWidgetStyleAsset::StaticClass();
			}
			else if (AssetType == TEXT("curveFloat"))
			{
				Factory = NewObject<UCurveFloatFactory>();
				AssetClass = UCurveFloat::StaticClass();
			}
			else if (AssetType == TEXT("curveLinearColor"))
			{
				Factory = NewObject<UCurveLinearColorFactory>();
				AssetClass = UCurveLinearColor::StaticClass();
			}
			else if (AssetType == TEXT("curveVector"))
			{
				Factory = NewObject<UCurveVectorFactory>();
				AssetClass = UCurveVector::StaticClass();
			}
			else if (AssetType == TEXT("curveTable"))
			{
				Factory = NewObject<UCurveTableFactory>();
				AssetClass = UCurveTable::StaticClass();
			}
			else if (AssetType == TEXT("compositeCurveTable"))
			{
				Factory = NewObject<UCompositeCurveTableFactory>();
				AssetClass = UCompositeCurveTable::StaticClass();
			}
			else if (AssetType == TEXT("dataTable"))
			{
				Factory = NewObject<UDataTableFactory>();
				AssetClass = UDataTable::StaticClass();
				// Set struct reference if specified. ResolveDataTableRowStruct
				// is the shared resolver (create_data_table uses the same one):
				// a bare LoadObject only accepts the /Game/Dir/S.S object form,
				// so an IR carrying the package form silently produced a
				// DataTable with no RowStruct - and the factory then refuses
				// to create the asset at all.
				FString StructPath;
				if (AssetDef->TryGetStringField(TEXT("struct"), StructPath) && !StructPath.IsEmpty())
				{
					FString StructReason;
					UScriptStruct* Struct = ResolveDataTableRowStruct(StructPath, StructReason);
					if (Struct != nullptr)
					{
						Cast<UDataTableFactory>(Factory)->Struct = Struct;
					}
					else
					{
						UE_LOG(LogForgeMindBridge, Warning, TEXT("[AssetIR] dataTable '%s': %s"), *StructPath, *StructReason);
					}
				}
			}
			else if (AssetType == TEXT("texture2DArray"))
			{
				Factory = NewObject<UTexture2DArrayFactory>();
				AssetClass = UTexture2DArray::StaticClass();
			}
			else if (AssetType == TEXT("textureCubeArray"))
			{
				Factory = NewObject<UTextureCubeArrayFactory>();
				AssetClass = UTextureCubeArray::StaticClass();
			}
			else if (AssetType == TEXT("volumeTexture"))
			{
				Factory = NewObject<UVolumeTextureFactory>();
				AssetClass = UVolumeTexture::StaticClass();
			}
			else if (AssetType == TEXT("virtualTextureBuilder")
				|| AssetType == TEXT("runtimeVirtualTexture"))
			{
				// These texture types don't have dedicated factories;
				// use the generic TextureFactory which creates UTexture2D by default,
				// then cast the result if needed.
				Factory = NewObject<UTexture2DFactoryNew>();
				AssetClass = UTexture2D::StaticClass();
			}
			else if (AssetType == TEXT("animSequence"))
			{
				Factory = NewObject<UAnimSequenceFactory>();
				AssetClass = UAnimSequence::StaticClass();
			}
			else if (AssetType == TEXT("animStreamable"))
			{
				Factory = NewObject<UAnimStreamableFactory>();
				AssetClass = UAnimStreamable::StaticClass();
			}
			else if (AssetType == TEXT("animComposite"))
			{
				Factory = NewObject<UAnimCompositeFactory>();
				AssetClass = UAnimComposite::StaticClass();
			}
			else if (AssetType == TEXT("animMontage"))
			{
				Factory = NewObject<UAnimMontageFactory>();
				AssetClass = UAnimMontage::StaticClass();
			}
			else if (AssetType == TEXT("blendSpace1D"))
			{
				Factory = NewObject<UBlendSpaceFactory1D>();
				AssetClass = UBlendSpace1D::StaticClass();
			}
			else if (AssetType == TEXT("blendSpace"))
			{
				Factory = NewObject<UBlendSpaceFactoryNew>();
				AssetClass = UBlendSpace::StaticClass();
			}
			else if (AssetType == TEXT("poseAsset"))
			{
				Factory = NewObject<UPoseAssetFactory>();
				AssetClass = UPoseAsset::StaticClass();
			}
			else if (AssetType == TEXT("skeleton"))
			{
				Factory = NewObject<USkeletonFactory>();
				AssetClass = USkeleton::StaticClass();
			}
			else if (AssetType == TEXT("physicsAsset"))
			{
				Factory = NewObject<UPhysicsAssetFactory>();
				AssetClass = UPhysicsAsset::StaticClass();
			}
			else if (AssetType == TEXT("previewMeshCollection"))
			{
				Factory = NewObject<UPreviewMeshCollectionFactory>();
				AssetClass = UPreviewMeshCollection::StaticClass();
			}
			else if (AssetType == TEXT("animBoneCompressionSettings"))
			{
				// No public factory in UE 5.6
				Warnings.Add(FString::Printf(TEXT("animBoneCompressionSettings type '%s' — no public factory available, skipped"), *AssetPath));
			}
			else if (AssetType == TEXT("animCurveCompressionSettings"))
			{
				// No public factory in UE 5.6
				Warnings.Add(FString::Printf(TEXT("animCurveCompressionSettings type '%s' — no public factory available, skipped"), *AssetPath));
			}
			else if (AssetType == TEXT("forceFeedbackEffect"))
			{
				Factory = NewObject<UForceFeedbackEffectFactory>();
				AssetClass = UForceFeedbackEffect::StaticClass();
			}
			else if (AssetType == TEXT("forceFeedbackAttenuation"))
			{
				Factory = NewObject<UForceFeedbackAttenuationFactory>();
				AssetClass = UForceFeedbackAttenuation::StaticClass();
			}
			else if (AssetType == TEXT("hapticFeedbackCurve"))
			{
				Factory = NewObject<UDataAssetFactory>();
				AssetClass = UHapticFeedbackEffect_Curve::StaticClass();
			}
			else if (AssetType == TEXT("hapticFeedbackBuffer"))
			{
				Factory = NewObject<UDataAssetFactory>();
				AssetClass = UHapticFeedbackEffect_Buffer::StaticClass();
			}
			else if (AssetType == TEXT("hapticFeedbackSoundWave"))
			{
				Factory = NewObject<UDataAssetFactory>();
				AssetClass = UHapticFeedbackEffect_SoundWave::StaticClass();
			}
			else if (AssetType == TEXT("particleSystem"))
			{
				Factory = NewObject<UParticleSystemFactoryNew>();
				AssetClass = UParticleSystem::StaticClass();
			}
			else if (AssetType == TEXT("subUVAnimation"))
			{
				Factory = NewObject<USubUVAnimationFactory>();
				AssetClass = USubUVAnimation::StaticClass();
			}
			else if (AssetType == TEXT("touchInterface"))
			{
				Factory = NewObject<UTouchInterfaceFactory>();
				AssetClass = UTouchInterface::StaticClass();
			}
			else if (AssetType == TEXT("world"))
			{
				Factory = NewObject<UWorldFactory>();
				AssetClass = UWorld::StaticClass();
			}
			else if (AssetType == TEXT("font"))
			{
				Factory = NewObject<UFontFactory>();
				AssetClass = UFont::StaticClass();
			}
			else if (AssetType == TEXT("vectorFieldStatic"))
			{
				// VectorFieldStatic factory may not be available in all UE versions
				Warnings.Add(FString::Printf(TEXT("vectorFieldStatic type '%s' — factory availability uncertain, skipped"), *AssetPath));
			}
			else if (AssetType == TEXT("dataAsset"))
			{
				Factory = NewObject<UDataAssetFactory>();
				AssetClass = UDataAsset::StaticClass();
			}
			else if (AssetType == TEXT("objectLibrary"))
			{
				Factory = NewObject<UObjectLibraryFactory>();
				AssetClass = UObjectLibrary::StaticClass();
			}
			else if (AssetType == TEXT("lightWeightInstance"))
			{
				// LightWeightInstance has no standard factory; skip with warning
				Warnings.Add(FString::Printf(TEXT("lightWeightInstance type '%s' — no public factory available, skipped"), *AssetPath));
			}
			else if (AssetType == TEXT("worldBookmark"))
			{
				// WorldBookmark doesn't have a public factory;
				// skip with a warning
				Warnings.Add(FString::Printf(TEXT("worldBookmark type '%s' — no public factory available, skipped"), *AssetPath));
			}
			else
			{
				Warnings.Add(FString::Printf(TEXT("Unsupported asset type '%s' for '%s' — skipped"), *AssetType, *AssetPath));
			}

			if (Factory != nullptr && AssetClass != nullptr)
			{
				if (Mode == TEXT("create") && !AssetExists(AssetPath))
				{
					UObject* CreatedAsset = CreateAssetWithFactory(Factory, AssetPath, AssetClass);
					if (CreatedAsset != nullptr)
					{
						bAssetCreated = true;
						CreatedAsset->MarkPackageDirty();
						FAssetRegistryModule::AssetCreated(CreatedAsset);

						// Apply properties from the "properties" field via reflection
						const TSharedPtr<FJsonObject>* PropsPtr = nullptr;
						if (AssetDef->TryGetObjectField(TEXT("properties"), PropsPtr) && PropsPtr->IsValid())
						{
							FJsonObjectConverter::JsonObjectToUStruct(PropsPtr->ToSharedRef(), CreatedAsset->GetClass(), CreatedAsset);
						}
					}
					else
					{
						Warnings.Add(FString::Printf(TEXT("Factory creation failed for '%s' (type=%s)"), *AssetPath, *AssetType));
					}
				}
				else if (Mode == TEXT("merge") || Mode == TEXT("replace"))
				{
					UObject* ExistingAsset = LoadExistingAsset(AssetPath);
					if (ExistingAsset != nullptr)
					{
						bAssetCreated = true;
						const TSharedPtr<FJsonObject>* PropsPtr = nullptr;
						if (AssetDef->TryGetObjectField(TEXT("properties"), PropsPtr) && PropsPtr->IsValid())
						{
							FJsonObjectConverter::JsonObjectToUStruct(PropsPtr->ToSharedRef(), ExistingAsset->GetClass(), ExistingAsset);
							ExistingAsset->MarkPackageDirty();
						}
					}
					else
					{
						// In merge/replace mode, if the asset doesn't exist, create it
						UObject* CreatedAsset = CreateAssetWithFactory(Factory, AssetPath, AssetClass);
						if (CreatedAsset != nullptr)
						{
							bAssetCreated = true;
							CreatedAsset->MarkPackageDirty();
							FAssetRegistryModule::AssetCreated(CreatedAsset);
							const TSharedPtr<FJsonObject>* PropsPtr = nullptr;
							if (AssetDef->TryGetObjectField(TEXT("properties"), PropsPtr) && PropsPtr->IsValid())
							{
								FJsonObjectConverter::JsonObjectToUStruct(PropsPtr->ToSharedRef(), CreatedAsset->GetClass(), CreatedAsset);
							}
						}
					}
				}
			}
		}

		if (bAssetCreated)
		{
			CreatedAssetPaths.Add(AssetPath);
			AssetResult->SetBoolField(TEXT("created"), true);
		}
		else
		{
			AssetResult->SetBoolField(TEXT("created"), false);
			if (!bFailed)
			{
				bFailed = true;
				FailureReason = FString::Printf(TEXT("Failed to create asset '%s' (type=%s)"), *AssetPath, *AssetType);
				FailedAsset = FString::Printf(TEXT("%s:%s"), *AssetType, *AssetPath);
			}
		}

		AssetResults.Add(MakeShared<FJsonValueObject>(AssetResult));

		if (bFailed)
		{
			break;
		}
	}

	// --- Step 4: Rollback on failure ---

	if (bFailed)
	{
		// Delete assets created during this batch (only in create mode)
		if (Mode == TEXT("create"))
		{
			for (const FString& Path : CreatedAssetPaths)
			{
				TSharedPtr<FJsonObject> DelPayload = MakeShared<FJsonObject>();
				DelPayload->SetStringField(TEXT("path"), Path);
				HandleDeleteAsset(DelPayload);
			}
		}

		Result->SetBoolField(TEXT("applied"), false);
		Result->SetStringField(TEXT("reason"), FailureReason);
		Result->SetBoolField(TEXT("partial"), true);
		Result->SetStringField(TEXT("failedAsset"), FailedAsset);
		Result->SetBoolField(TEXT("rolledBack"), Mode == TEXT("create"));
		Result->SetArrayField(TEXT("assets"), AssetResults);
		AppendWarningsToResult(Result, Warnings);
		return Result;
	}

	// --- Step 5: Compile (blueprint-family assets only) ---

	// Blueprint compilation is already handled by HandleApplyBlueprintGraph
	// when it processes each blueprint-family asset.

	// --- Step 6: Layout optimization for blueprint-family assets ---

	// Layout is already handled by HandleApplyBlueprintGraph.

	// --- Step 7: Save ---

	bool bSaved = false;
	if (bSave)
	{
		for (const FString& Path : CreatedAssetPaths)
		{
			TSharedPtr<FJsonObject> SavePayload = MakeShared<FJsonObject>();
			SavePayload->SetStringField(TEXT("path"), Path);
			TSharedPtr<FJsonObject> SaveResult = HandleSaveAsset(SavePayload);
			bool bSavedThis = false;
			SaveResult->TryGetBoolField(TEXT("saved"), bSavedThis);
			if (bSavedThis) bSaved = true;
		}
	}

	// --- Step 8: Build and return result ---

	Result->SetBoolField(TEXT("applied"), true);
	Result->SetNumberField(TEXT("assetCount"), AssetResults.Num());
	Result->SetArrayField(TEXT("assets"), AssetResults);
	if (bSaved)
	{
		Result->SetBoolField(TEXT("saved"), true);
	}
	AppendWarningsToResult(Result, Warnings);
	return Result;
}

// ===========================================================================
// HandleGetAssetIR
// ===========================================================================

namespace
{
	/**
	 * Identifies the AssetIR type string for a loaded UObject by
	 * checking its UClass hierarchy.
	 */
	FString IdentifyAssetType(UObject* Asset)
	{
		if (Asset == nullptr)
		{
			return TEXT("");
		}

		UClass* AssetClass = Asset->GetClass();

		// Blueprint family — check WidgetBlueprint before UBlueprint
		// since UWidgetBlueprint inherits from UBlueprint.
		if (AssetClass->IsChildOf(UWidgetBlueprint::StaticClass()))   return TEXT("widgetBlueprint");
		if (AssetClass->IsChildOf(UBlueprint::StaticClass()))         return TEXT("blueprint");

		// Data structures
		if (AssetClass->IsChildOf(UUserDefinedStruct::StaticClass())) return TEXT("struct");
		if (AssetClass->IsChildOf(UUserDefinedEnum::StaticClass()))   return TEXT("enum");
		if (AssetClass->IsChildOf(UDataTable::StaticClass()))         return TEXT("dataTable");
		if (AssetClass->IsChildOf(UCompositeCurveTable::StaticClass())) return TEXT("compositeCurveTable");
		if (AssetClass->IsChildOf(UCurveTable::StaticClass()))       return TEXT("curveTable");
		if (AssetClass->IsChildOf(UCurveFloat::StaticClass()))       return TEXT("curveFloat");
		if (AssetClass->IsChildOf(UCurveLinearColor::StaticClass())) return TEXT("curveLinearColor");
		if (AssetClass->IsChildOf(UCurveVector::StaticClass()))      return TEXT("curveVector");

		// Materials
		if (AssetClass->IsChildOf(UMaterialInstanceConstant::StaticClass())) return TEXT("materialInstanceConstant");
		if (AssetClass->IsChildOf(UMaterial::StaticClass()))         return TEXT("material");
		if (AssetClass->IsChildOf(UMaterialFunctionMaterialLayerBlend::StaticClass())) return TEXT("materialFunctionMaterialLayerBlend");
		if (AssetClass->IsChildOf(UMaterialFunctionMaterialLayer::StaticClass())) return TEXT("materialFunctionMaterialLayer");
		if (AssetClass->IsChildOf(UMaterialFunctionInstance::StaticClass())) return TEXT("materialFunctionInstance");
		if (AssetClass->IsChildOf(UMaterialFunction::StaticClass())) return TEXT("materialFunction");
		if (AssetClass->IsChildOf(UMaterialParameterCollection::StaticClass())) return TEXT("materialParameterCollection");
		if (AssetClass->IsChildOf(UPhysicalMaterialMask::StaticClass())) return TEXT("physicalMaterialMask");
		if (AssetClass->IsChildOf(UPhysicalMaterial::StaticClass())) return TEXT("physicalMaterial");

		// Textures
		if (AssetClass->IsChildOf(UTexture2DArray::StaticClass()))   return TEXT("texture2DArray");
		if (AssetClass->IsChildOf(UTextureCubeArray::StaticClass())) return TEXT("textureCubeArray");
		if (AssetClass->IsChildOf(UVolumeTexture::StaticClass()))   return TEXT("volumeTexture");

		// Animation
		if (AssetClass->IsChildOf(UAnimSequence::StaticClass()))     return TEXT("animSequence");
		if (AssetClass->IsChildOf(UAnimStreamable::StaticClass()))   return TEXT("animStreamable");
		if (AssetClass->IsChildOf(UAnimComposite::StaticClass()))    return TEXT("animComposite");
		if (AssetClass->IsChildOf(UAnimMontage::StaticClass()))      return TEXT("animMontage");
		if (AssetClass->IsChildOf(UBlendSpace1D::StaticClass()))    return TEXT("blendSpace1D");
		if (AssetClass->IsChildOf(UBlendSpace::StaticClass()))      return TEXT("blendSpace");
		if (AssetClass->IsChildOf(UPoseAsset::StaticClass()))       return TEXT("poseAsset");
		if (AssetClass->IsChildOf(USkeleton::StaticClass()))        return TEXT("skeleton");
		if (AssetClass->IsChildOf(UPhysicsAsset::StaticClass()))     return TEXT("physicsAsset");
		if (AssetClass->IsChildOf(UPreviewMeshCollection::StaticClass())) return TEXT("previewMeshCollection");

		// Audio/effects
		if (AssetClass->IsChildOf(UForceFeedbackEffect::StaticClass())) return TEXT("forceFeedbackEffect");
		if (AssetClass->IsChildOf(UForceFeedbackAttenuation::StaticClass())) return TEXT("forceFeedbackAttenuation");
		if (AssetClass->IsChildOf(UParticleSystem::StaticClass()))  return TEXT("particleSystem");
		if (AssetClass->IsChildOf(USubUVAnimation::StaticClass()))  return TEXT("subUVAnimation");

		// Input
		if (AssetClass->IsChildOf(UTouchInterface::StaticClass()))  return TEXT("touchInterface");

		// World
		if (AssetClass->IsChildOf(UWorld::StaticClass()))           return TEXT("world");

		// Fonts
		if (AssetClass->IsChildOf(UFont::StaticClass()))             return TEXT("font");

		// Data assets
		if (AssetClass->IsChildOf(UDataAsset::StaticClass()))        return TEXT("dataAsset");
		if (AssetClass->IsChildOf(UObjectLibrary::StaticClass()))    return TEXT("objectLibrary");

		// Fallback
		return TEXT("dataAsset");
	}

	/**
	 * Extracts a generic properties object from any UObject using
	 * reflection. This is the fallback extraction for asset types
	 * that don't have a specialized extractor.
	 */
	TSharedPtr<FJsonObject> ExtractGenericProperties(UObject* Asset)
	{
		TSharedPtr<FJsonObject> Properties = MakeShared<FJsonObject>();
		if (Asset == nullptr)
		{
			return Properties;
		}
		FJsonObjectConverter::UStructToJsonObject(Asset->GetClass(), Asset, Properties.ToSharedRef());
		return Properties;
	}
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleGetAssetIRLegacy(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString Path;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), Path)
			&& !Payload->TryGetStringField(TEXT("assetPath"), Path)) || Path.IsEmpty())
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required field: path (asset path or directory path)"));
		return Result;
	}

	bool bRecursive = false;
	Payload->TryGetBoolField(TEXT("recursive"), bRecursive);

	// --- Determine if this is a single asset or a directory ---

	// Try to load as a single asset first
	UObject* Asset = LoadObject<UObject>(nullptr, *Path);

	if (Asset != nullptr)
	{
		// --- Single asset mode ---

		const FString AssetType = IdentifyAssetType(Asset);

		TSharedRef<FJsonObject> AssetIR = MakeShared<FJsonObject>();
		AssetIR->SetStringField(TEXT("$schema"), TEXT("forgemind-assetir/v2"));
		AssetIR->SetNumberField(TEXT("version"), 2);

		TArray<TSharedPtr<FJsonValue>> AssetsArray;
		TSharedRef<FJsonObject> AssetEntry = MakeShared<FJsonObject>();
		AssetEntry->SetStringField(TEXT("type"), AssetType);
		AssetEntry->SetStringField(TEXT("path"), Path);

		// For blueprint-family assets, extract variables and components inline.
		if (AssetType == TEXT("blueprint") || AssetType == TEXT("widgetBlueprint"))
		{
			TSharedPtr<FJsonObject> ListPayload = MakeShared<FJsonObject>();
			ListPayload->SetStringField(TEXT("path"), Path);

			TSharedPtr<FJsonObject> VarResult = HandleListBlueprintVariables(ListPayload);
			const TArray<TSharedPtr<FJsonValue>>* VarsArray = nullptr;
			if (VarResult->TryGetArrayField(TEXT("variables"), VarsArray))
			{
				AssetEntry->SetArrayField(TEXT("variables"), *VarsArray);
			}

			TSharedPtr<FJsonObject> CompResult = HandleListBlueprintComponents(ListPayload);
			const TArray<TSharedPtr<FJsonValue>>* CompsArray = nullptr;
			if (CompResult->TryGetArrayField(TEXT("components"), CompsArray))
			{
				AssetEntry->SetArrayField(TEXT("components"), *CompsArray);
			}

			// Also include generic properties
			TSharedPtr<FJsonObject> Props = ExtractGenericProperties(Asset);
			if (Props->Values.Num() > 0)
			{
				AssetEntry->SetObjectField(TEXT("properties"), Props);
			}
		}
		else
		{
			// Generic properties extraction via reflection
			TSharedPtr<FJsonObject> Props = ExtractGenericProperties(Asset);
			if (Props->Values.Num() > 0)
			{
				AssetEntry->SetObjectField(TEXT("properties"), Props);
			}
		}

		AssetsArray.Add(MakeShared<FJsonValueObject>(AssetEntry));
		AssetIR->SetArrayField(TEXT("assets"), AssetsArray);

		Result->SetBoolField(TEXT("found"), true);
		Result->SetStringField(TEXT("path"), Asset->GetPathName());
		Result->SetObjectField(TEXT("assetir"), AssetIR);
		Result->SetNumberField(TEXT("assetCount"), 1);
		return Result;
	}

	// --- Directory scan mode ---

	// Check if the path is a content directory
	FString PackagePath = Path;
	int32 DotIdx = INDEX_NONE;
	if (PackagePath.FindChar(TEXT('.'), DotIdx))
	{
		PackagePath = PackagePath.Left(DotIdx);
	}

	// Use AssetRegistry to find all assets under the given path
	FAssetRegistryModule& AssetRegistryModule = FModuleManager::LoadModuleChecked<FAssetRegistryModule>("AssetRegistry");
	IAssetRegistry& AssetRegistry = AssetRegistryModule.Get();

	TArray<FAssetData> AssetDataList;
	FARFilter Filter;
	Filter.PackagePaths.Add(FName(*PackagePath));
	Filter.bRecursivePaths = bRecursive;
	AssetRegistry.GetAssets(Filter, AssetDataList);

	if (AssetDataList.Num() == 0)
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No assets found at '%s'"), *Path));
		return Result;
	}

	TArray<TSharedPtr<FJsonValue>> AssetsArray;
	for (const FAssetData& AssetData : AssetDataList)
	{
		UObject* AssetObj = AssetData.GetAsset();
		if (AssetObj == nullptr)
		{
			// Try synchronous load
			AssetObj = LoadObject<UObject>(nullptr, *AssetData.ObjectPath.ToString());
		}
		if (AssetObj == nullptr)
		{
			continue;
		}

		const FString AssetType = IdentifyAssetType(AssetObj);
		if (AssetType.IsEmpty())
		{
			continue;
		}

		TSharedRef<FJsonObject> AssetEntry = MakeShared<FJsonObject>();
		AssetEntry->SetStringField(TEXT("type"), AssetType);
		AssetEntry->SetStringField(TEXT("path"), AssetData.ObjectPath.ToString());

		// For blueprint-family, extract variables and components inline.
		if (AssetType == TEXT("blueprint") || AssetType == TEXT("widgetBlueprint"))
		{
			TSharedPtr<FJsonObject> ListPayload = MakeShared<FJsonObject>();
			ListPayload->SetStringField(TEXT("path"), AssetData.ObjectPath.ToString());

			TSharedPtr<FJsonObject> VarResult = HandleListBlueprintVariables(ListPayload);
			const TArray<TSharedPtr<FJsonValue>>* VarsArray = nullptr;
			if (VarResult->TryGetArrayField(TEXT("variables"), VarsArray))
			{
				AssetEntry->SetArrayField(TEXT("variables"), *VarsArray);
			}

			TSharedPtr<FJsonObject> CompResult = HandleListBlueprintComponents(ListPayload);
			const TArray<TSharedPtr<FJsonValue>>* CompsArray = nullptr;
			if (CompResult->TryGetArrayField(TEXT("components"), CompsArray))
			{
				AssetEntry->SetArrayField(TEXT("components"), *CompsArray);
			}

			TSharedPtr<FJsonObject> Props = ExtractGenericProperties(AssetObj);
			if (Props->Values.Num() > 0)
			{
				AssetEntry->SetObjectField(TEXT("properties"), Props);
			}
		}
		else
		{
			TSharedPtr<FJsonObject> Props = ExtractGenericProperties(AssetObj);
			if (Props->Values.Num() > 0)
			{
				AssetEntry->SetObjectField(TEXT("properties"), Props);
			}
		}

		AssetsArray.Add(MakeShared<FJsonValueObject>(AssetEntry));
	}

	TSharedRef<FJsonObject> AssetIR = MakeShared<FJsonObject>();
	AssetIR->SetStringField(TEXT("$schema"), TEXT("forgemind-assetir/v2"));
	AssetIR->SetNumberField(TEXT("version"), 2);
	AssetIR->SetArrayField(TEXT("assets"), AssetsArray);

	Result->SetBoolField(TEXT("found"), true);
	Result->SetStringField(TEXT("path"), Path);
	Result->SetObjectField(TEXT("assetir"), AssetIR);
	Result->SetNumberField(TEXT("assetCount"), AssetsArray.Num());
	return Result;
}