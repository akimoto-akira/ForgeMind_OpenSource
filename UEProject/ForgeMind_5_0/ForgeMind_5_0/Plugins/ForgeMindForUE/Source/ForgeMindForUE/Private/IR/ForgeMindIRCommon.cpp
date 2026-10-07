// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Shared plumbing for every domain IR handler: path/version validation,
// asset lifetime (create / save / delete) and reflection-based property
// read/write.

#include "ForgeMindIRHandler.h"

#include "Misc/OutputDeviceDebug.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "AssetToolsModule.h"
#include "Factories/Factory.h"
#include "IAssetTools.h"
#include "Misc/PackageName.h"
#include "Misc/Paths.h"
#include "ObjectTools.h"
#include "UObject/Package.h"
#include "UObject/SavePackage.h"
#include "UObject/UObjectGlobals.h"
#include "UObject/UObjectIterator.h"
#include "UObject/UnrealType.h"

namespace
{
	FString EngineMajorMinor()
	{
		static FString Cached;
		if (Cached.IsEmpty())
		{
			Cached = FString::Printf(TEXT("%d.%d"),
				FEngineVersion::Current().GetMajor(),
				FEngineVersion::Current().GetMinor());
		}
		return Cached;
	}

	bool SavePackageToDisk(UPackage* Package, FString& OutError)
	{
		const FString PackageName = Package->GetName();
		FString Filename;
		if (!FPackageName::TryConvertLongPackageNameToFilename(PackageName, Filename, FPackageName::GetAssetPackageExtension()) || Filename.IsEmpty())
		{
			const FString RelativePath = PackageName.RightChop(FString(TEXT("/Game")).Len());
			Filename = FPaths::ProjectContentDir() / (RelativePath + FPackageName::GetAssetPackageExtension());
		}

		FStringOutputDevice ErrorDevice;
		FSavePackageArgs SaveArgs;
		SaveArgs.TopLevelFlags = RF_Public | RF_Standalone;
		SaveArgs.bSlowTask = false;
		SaveArgs.SaveFlags = SAVE_NoError;
		SaveArgs.Error = &ErrorDevice;

		if (!UPackage::SavePackage(Package, nullptr, *Filename, SaveArgs))
		{
			OutError = ErrorDevice;
			if (OutError.IsEmpty())
			{
				OutError = TEXT("SavePackage failed");
			}
			return false;
		}
		return true;
	}
}

// ---------------------------------------------------------------------------
// IIRHandler default
// ---------------------------------------------------------------------------

bool IIRHandler::CompileAsset(UObject* Asset, FIRApplyContext& Ctx)
{
	// Domains without a real compiler still need the editor to notice the
	// edit so previews and the content browser refresh.
	if (Asset != nullptr)
	{
		Asset->PostEditChange();
		Asset->MarkPackageDirty();
	}
	Ctx.CompileTimeMs = 0.0;
	return true;
}

// ---------------------------------------------------------------------------
// Shell validation (IR rules.md section 2)
// ---------------------------------------------------------------------------

bool ForgeMindIR::CheckVersion(const FString& Version, FString& OutError)
{
	if (Version.IsEmpty())
	{
		OutError = TEXT("missing version");
		return false;
	}
	const FString EngineVersion = EngineMajorMinor();
	if (!Version.Equals(EngineVersion, ESearchCase::IgnoreCase))
	{
		OutError = FString::Printf(TEXT("IR version %s incompatible with engine %s"), *Version, *EngineVersion);
		return false;
	}
	return true;
}

bool ForgeMindIR::IsValidAssetPath(const FString& Path, FString& OutError)
{
	if (Path.IsEmpty())
	{
		OutError = TEXT("missing path");
		return false;
	}
	if (Path.Contains(TEXT("//")) || Path.Contains(TEXT("\\")))
	{
		OutError = FString::Printf(TEXT("invalid path %s"), *Path);
		return false;
	}
	if (!FPackageName::IsValidLongPackageName(NormalizePath(Path)))
	{
		OutError = FString::Printf(TEXT("invalid path %s"), *Path);
		return false;
	}
	return true;
}

FString ForgeMindIR::NormalizePath(const FString& Path)
{
	FString Result = Path;
	Result.ReplaceInline(TEXT("\\"), TEXT("/"));
	// "/Game/A/B.B" → "/Game/A/B"
	int32 DotIdx = INDEX_NONE;
	if (Result.FindChar(TEXT('.'), DotIdx))
	{
		const FString BeforeDot = Result.Left(DotIdx);
		const FString AfterDot = Result.Mid(DotIdx + 1);
		if (AfterDot == FPaths::GetCleanFilename(BeforeDot))
		{
			Result = BeforeDot;
		}
	}
	while (Result.EndsWith(TEXT("/")))
	{
		Result.LeftChopInline(1);
	}
	return Result;
}

FString ForgeMindIR::PackagePathOf(const FString& Path)
{
	return FPackageName::GetLongPackagePath(NormalizePath(Path));
}

FString ForgeMindIR::AssetNameOf(const FString& Path)
{
	return FPaths::GetCleanFilename(NormalizePath(Path));
}

bool ForgeMindIR::AssetExists(const FString& Path)
{
	const FString PackagePath = NormalizePath(Path);
	FAssetRegistryModule& Module = FModuleManager::LoadModuleChecked<FAssetRegistryModule>(TEXT("AssetRegistry"));
	TArray<FAssetData> Assets;
	Module.Get().GetAssetsByPackageName(FName(*PackagePath), Assets, true);
	return Assets.Num() > 0;
}

UObject* ForgeMindIR::LoadAsset(const FString& Path)
{
	const FString Normalized = NormalizePath(Path);
	if (UObject* Found = FindObject<UObject>(nullptr, *Normalized))
	{
		return Found;
	}
	if (UObject* Loaded = LoadObject<UObject>(nullptr, *Normalized))
	{
		return Loaded;
	}
	// Tolerate the "Package.Object" spelling.
	const FString ObjectForm = Normalized + TEXT(".") + AssetNameOf(Normalized);
	return LoadObject<UObject>(nullptr, *ObjectForm);
}

UObject* ForgeMindIR::CreateAssetInstance(UClass* AssetClass, const FString& Path, bool bOverwrite)
{
	if (AssetClass == nullptr)
	{
		return nullptr;
	}
	const FString PackageName = NormalizePath(Path);
	if (!FPackageName::IsValidLongPackageName(PackageName))
	{
		return nullptr;
	}
	if (AssetExists(PackageName) && !bOverwrite)
	{
		return nullptr;
	}

	UPackage* Package = CreatePackage(*PackageName);
	if (Package == nullptr)
	{
		return nullptr;
	}
	Package->FullyLoad();

	UObject* Asset = NewObject<UObject>(Package, AssetClass, FName(*AssetNameOf(PackageName)),
		RF_Public | RF_Standalone | RF_Transactional);
	if (Asset == nullptr)
	{
		return nullptr;
	}

	FAssetRegistryModule::AssetCreated(Asset);
	Package->MarkPackageDirty();
	return Asset;
}

UObject* ForgeMindIR::CreateAssetWithFactory(UFactory* Factory, UClass* AssetClass, const FString& Path)
{
	if (Factory == nullptr || AssetClass == nullptr)
	{
		return nullptr;
	}
	FAssetToolsModule& Module = FModuleManager::LoadModuleChecked<FAssetToolsModule>(TEXT("AssetTools"));
	return Module.Get().CreateAsset(AssetNameOf(Path), PackagePathOf(Path), AssetClass, Factory);
}

UClass* ForgeMindIR::ResolveClass(const FString& Name)
{
	if (Name.IsEmpty())
	{
		return nullptr;
	}

	static TMap<FString, TWeakObjectPtr<UClass>> Cache;
	if (const TWeakObjectPtr<UClass>* Cached = Cache.Find(Name))
	{
		if (Cached->IsValid())
		{
			return Cached->Get();
		}
	}

	auto StripPrefix = [](const FString& In) -> FString
	{
		if (In.Len() > 1 && (In[0] == TEXT('U') || In[0] == TEXT('A') || In[0] == TEXT('F'))
			&& FChar::IsUpper(In[1]))
		{
			return In.RightChop(1);
		}
		return In;
	};

	const FString Bare = StripPrefix(Name);
	const FString WithU = TEXT("U") + Bare;

	for (UClass* Class : TObjectRange<UClass>())
	{
		if (Class == nullptr)
		{
			continue;
		}
		const FString& ClassName = Class->GetName();
		if (ClassName.Equals(Name, ESearchCase::IgnoreCase)
			|| ClassName.Equals(Bare, ESearchCase::IgnoreCase)
			|| ClassName.Equals(WithU, ESearchCase::IgnoreCase))
		{
			Cache.Add(Name, Class);
			return Class;
		}
	}

	return nullptr;
}

UObject* ForgeMindIR::ResolveObjectPath(const FString& Path, UClass* ExpectedClass)
{
	if (Path.IsEmpty())
	{
		return nullptr;
	}
	FString Clean = Path;
	Clean.ReplaceInline(TEXT("\\"), TEXT("/"));
	Clean.RemoveFromEnd(TEXT(".uasset"));

	TArray<FString> Candidates;
	Candidates.Add(Clean);
	if (!Clean.Contains(TEXT(".")))
	{
		Candidates.Add(Clean + TEXT(".") + FPaths::GetCleanFilename(Clean));
	}

	for (const FString& Candidate : Candidates)
	{
		UObject* Loaded = LoadObject<UObject>(nullptr, *Candidate);
		if (Loaded != nullptr && (ExpectedClass == nullptr || Loaded->IsA(ExpectedClass)))
		{
			return Loaded;
		}
	}
	return nullptr;
}

bool ForgeMindIR::SaveAssetObject(UObject* Asset)
{
	if (Asset == nullptr)
	{
		return false;
	}
	UPackage* Package = Asset->GetPackage();
	if (Package == nullptr)
	{
		return false;
	}
	FString Error;
	return SavePackageToDisk(Package, Error);
}

int32 ForgeMindIR::CountReferencers(const FString& Path)
{
	const FString PackageName = NormalizePath(Path);
	IAssetRegistry& Registry = FModuleManager::LoadModuleChecked<FAssetRegistryModule>(TEXT("AssetRegistry")).Get();
	TArray<FName> Referencers;
	UE::AssetRegistry::FDependencyQuery Query(UE::AssetRegistry::EDependencyQuery::Hard);
	Registry.GetReferencers(FName(*PackageName), Referencers, UE::AssetRegistry::EDependencyCategory::Package, Query);
	return Referencers.Num();
}

bool ForgeMindIR::DeleteAssetObject(const FString& Path, FString& OutError)
{
	const FString PackageName = NormalizePath(Path);
	if (!PackageName.StartsWith(TEXT("/Game/")))
	{
		OutError = TEXT("Only assets under /Game/ can be deleted");
		return false;
	}

	UObject* Asset = LoadAsset(PackageName);
	if (Asset == nullptr)
	{
		OutError = FString::Printf(TEXT("asset not found at %s"), *PackageName);
		return false;
	}

	TArray<UObject*> ObjectsToDelete = { Asset };
	const int32 Deleted = ObjectTools::DeleteObjects(ObjectsToDelete, false, ObjectTools::EAllowCancelDuringDelete::CancelNotAllowed);
	if (Deleted == 0)
	{
		OutError = FString::Printf(TEXT("deletion of '%s' was refused by the editor"), *PackageName);
		return false;
	}
	return true;
}

// ---------------------------------------------------------------------------
// Reflection
// ---------------------------------------------------------------------------

FProperty* ForgeMindIR::FindPropertyOnStruct(const UStruct* Struct, const FString& Name)
{
	if (Struct == nullptr)
	{
		return nullptr;
	}
	for (TFieldIterator<FProperty> It(Struct, EFieldIteratorFlags::IncludeSuper); It; ++It)
	{
		if ((*It)->GetName().Equals(Name, ESearchCase::IgnoreCase))
		{
			return *It;
		}
	}
	return nullptr;
}

FProperty* ForgeMindIR::FindProperty(UObject* Object, const FString& Name)
{
	if (Object == nullptr)
	{
		return nullptr;
	}
	return FindPropertyOnStruct(Object->GetClass(), Name);
}

FString ForgeMindIR::JsonValueToImportText(const TSharedPtr<FJsonValue>& Value)
{
	if (!Value.IsValid())
	{
		return FString();
	}

	switch (Value->Type)
	{
	case EJson::None:
	case EJson::Null:
		return FString();
	case EJson::String:
		return Value->AsString();
	case EJson::Number:
		return FString::SanitizeFloat(Value->AsNumber());
	case EJson::Boolean:
		return Value->AsBool() ? TEXT("true") : TEXT("false");
	case EJson::Array:
	{
		const TArray<TSharedPtr<FJsonValue>>& Items = Value->AsArray();
		FString Out = TEXT("(");
		for (int32 i = 0; i < Items.Num(); ++i)
		{
			if (i > 0)
			{
				Out += TEXT(",");
			}
			Out += JsonValueToImportText(Items[i]);
		}
		Out += TEXT(")");
		return Out;
	}
	case EJson::Object:
	{
		const TSharedPtr<FJsonObject>& Obj = Value->AsObject();
		FString Out = TEXT("(");
		bool bFirst = true;
		for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Obj->Values)
		{
			if (!bFirst)
			{
				Out += TEXT(",");
			}
			bFirst = false;
			FString Key = Pair.Key;
			if (Key.Len() > 0)
			{
				Key[0] = FChar::ToUpper(Key[0]);
			}
			Out += Key + TEXT("=") + JsonValueToImportText(Pair.Value);
		}
		Out += TEXT(")");
		return Out;
	}
	default:
		break;
	}
	return FString();
}

bool ForgeMindIR::SetProperty(UObject* Object, const FString& PropertyName, const TSharedPtr<FJsonValue>& Value, FString& OutError)
{
	FProperty* Property = FindProperty(Object, PropertyName);
	if (Property == nullptr)
	{
		OutError = FString::Printf(TEXT("property '%s' not found on %s"), *PropertyName, *Object->GetName());
		return false;
	}
	if (!Value.IsValid())
	{
		return true;
	}

	void* Data = Property->ContainerPtrToValuePtr<void>(Object);

	const bool bObjectLike = Property->IsA<FObjectProperty>() || Property->IsA<FClassProperty>()
		|| Property->IsA<FSoftObjectProperty>() || Property->IsA<FSoftClassProperty>()
		|| Property->IsA<FWeakObjectProperty>() || Property->IsA<FInterfaceProperty>();

	TArray<FString> Candidates;
	if (bObjectLike && Value->Type == EJson::String)
	{
		FString Path = Value->AsString();
		Path.ReplaceInline(TEXT("\\"), TEXT("/"));
		Path.RemoveFromEnd(TEXT(".uasset"));
		Candidates.Add(Path);
		if (!Path.Contains(TEXT(".")))
		{
			Candidates.Add(Path + TEXT(".") + FPaths::GetCleanFilename(Path));
		}
	}
	else
	{
		Candidates.Add(JsonValueToImportText(Value));
	}

	for (const FString& Candidate : Candidates)
	{
		if (Property->ImportText(*Candidate, Data, PPF_PropertyWindow, Object) != nullptr)
		{
			return true;
		}
	}

	OutError = FString::Printf(TEXT("value for property '%s' could not be imported (%s)"),
		*PropertyName, Candidates.Num() > 0 ? *Candidates[0] : TEXT(""));
	return false;
}

void ForgeMindIR::SetProperties(UObject* Object, const TSharedPtr<FJsonObject>& Values, TArray<FIRWarning>& OutWarnings)
{
	if (Object == nullptr || !Values.IsValid())
	{
		return;
	}
	for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Values->Values)
	{
		FString Error;
		if (!SetProperty(Object, Pair.Key, Pair.Value, Error))
		{
			OutWarnings.Add(FIRWarning{ TEXT("PROPERTY_NOT_FOUND"), Error });
		}
	}
}

TSharedPtr<FJsonValue> ForgeMindIR::GetProperty(UObject* Object, const FString& PropertyName)
{
	FProperty* Property = FindProperty(Object, PropertyName);
	if (Property == nullptr || Object == nullptr)
	{
		return nullptr;
	}
	const void* Data = Property->ContainerPtrToValuePtr<void>(Object);

	auto AsText = [Property, Data]() -> TSharedPtr<FJsonValue>
	{
		FString Text;
		Property->ExportTextItem(Text, Data, nullptr, nullptr, PPF_None);
		return MakeShared<FJsonValueString>(Text);
	};

	if (const FBoolProperty* BoolProperty = CastField<FBoolProperty>(Property))
	{
		return MakeShared<FJsonValueBoolean>(BoolProperty->GetPropertyValue(Data));
	}
	if (const FNumericProperty* Numeric = CastField<FNumericProperty>(Property))
	{
		if (Numeric->IsFloatingPoint())
		{
			return MakeShared<FJsonValueNumber>(Numeric->GetFloatingPointPropertyValue(Data));
		}
		return MakeShared<FJsonValueNumber>(static_cast<double>(Numeric->GetSignedIntPropertyValue(Data)));
	}
	if (const FStructProperty* StructProperty = CastField<FStructProperty>(Property))
	{
		// Emit the component form AI-authored IR uses ({x,y,z} / {r,g,b,a} / {min,max}).
		TSharedRef<FJsonObject> Out = MakeShared<FJsonObject>();
		for (TFieldIterator<FProperty> It(StructProperty->Struct); It; ++It)
		{
			const FProperty* Member = *It;
			const void* MemberData = Member->ContainerPtrToValuePtr<void>(Data);
			if (const FNumericProperty* MemberNumeric = CastField<FNumericProperty>(Member))
			{
				if (MemberNumeric->IsFloatingPoint())
				{
					Out->SetNumberField(Member->GetName(), MemberNumeric->GetFloatingPointPropertyValue(MemberData));
				}
				else
				{
					Out->SetNumberField(Member->GetName(), static_cast<double>(MemberNumeric->GetSignedIntPropertyValue(MemberData)));
				}
			}
			else if (const FBoolProperty* MemberBool = CastField<FBoolProperty>(Member))
			{
				Out->SetBoolField(Member->GetName(), MemberBool->GetPropertyValue(MemberData));
			}
			else
			{
				FString Text;
				Member->ExportTextItem(Text, MemberData, nullptr, nullptr, PPF_None);
				Out->SetStringField(Member->GetName(), Text);
			}
		}
		return MakeShared<FJsonValueObject>(Out);
	}
	if (const FObjectProperty* ObjectProperty = CastField<FObjectProperty>(Property))
	{
		const UObject* Value = ObjectProperty->GetObjectPropertyValue(Data);
		return MakeShared<FJsonValueString>(Value != nullptr ? Value->GetPathName() : FString());
	}
	return AsText();
}

TSharedPtr<FJsonObject> ForgeMindIR::GetProperties(UObject* Object, const TArray<FString>& Names)
{
	TSharedRef<FJsonObject> Out = MakeShared<FJsonObject>();
	if (Object == nullptr)
	{
		return Out;
	}
	for (const FString& Name : Names)
	{
		if (TSharedPtr<FJsonValue> Value = GetProperty(Object, Name))
		{
			Out->SetField(Name, Value);
		}
	}
	return Out;
}

// ---------------------------------------------------------------------------
// Misc helpers
// ---------------------------------------------------------------------------

FString ForgeMindIR::FirstString(const TSharedPtr<FJsonObject>& Obj, const TArray<FString>& Keys)
{
	if (!Obj.IsValid())
	{
		return FString();
	}
	for (const FString& Key : Keys)
	{
		FString Value;
		if (Obj->TryGetStringField(Key, Value) && !Value.IsEmpty())
		{
			return Value;
		}
	}
	return FString();
}

bool ForgeMindIR::IsDeletePatch(const TSharedPtr<FJsonObject>& Patch)
{
	if (!Patch.IsValid())
	{
		return false;
	}
	bool bDelete = false;
	if (Patch->TryGetBoolField(TEXT("delete"), bDelete) && bDelete)
	{
		return true;
	}
	// Some translators still spell deletion as the legacy op field.
	FString Op;
	if (Patch->TryGetStringField(TEXT("op"), Op) && Op.Equals(TEXT("delete"), ESearchCase::IgnoreCase))
	{
		return true;
	}
	return false;
}

void ForgeMindIR::FinalizeAsset(UObject* Asset, bool bSave)
{
	if (Asset == nullptr)
	{
		return;
	}
	Asset->MarkPackageDirty();
	if (UPackage* Package = Asset->GetPackage())
	{
		Package->MarkPackageDirty();
	}
	if (bSave)
	{
		SaveAssetObject(Asset);
	}
}