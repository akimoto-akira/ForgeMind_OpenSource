// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// Implementation of the shared material-expression operations declared in
// ForgeMindMaterialOps.h. See that header for the reason this code exists once
// instead of being duplicated per channel.

#include "ForgeMindMaterialOps.h"
#include "ForgeMindIRHandler.h"
#include "ForgeMindGraphLayout.h"

#include "Materials/Material.h"
#include "Materials/MaterialExpression.h"
#include "Materials/MaterialExpressionConstant.h"
#include "Materials/MaterialExpressionConstant2Vector.h"
#include "Materials/MaterialExpressionConstant3Vector.h"
#include "Materials/MaterialExpressionConstant4Vector.h"
#include "Materials/MaterialExpressionTextureBase.h"
#include "Materials/MaterialInterface.h"
#include "SceneTypes.h"
#include "UObject/UnrealType.h"

namespace
{
	/** Struct names that are stored as an FLinearColor and written component-wise. */
	bool IsLinearColorStruct(const UScriptStruct* Struct)
	{
		return Struct != nullptr && Struct->GetName().Equals(TEXT("LinearColor"));
	}

	/** Pull the numbers of a JSON value in every shape the two channels produce. */
	bool JsonToNumbers(const TSharedPtr<FJsonValue>& Value, TArray<double>& OutNumbers)
	{
		OutNumbers.Reset();
		if (!Value.IsValid())
		{
			return false;
		}
		switch (Value->Type)
		{
		case EJson::Number:
			OutNumbers.Add(Value->AsNumber());
			return true;
		case EJson::Boolean:
			OutNumbers.Add(Value->AsBool() ? 1.0 : 0.0);
			return true;
		case EJson::Array:
			for (const TSharedPtr<FJsonValue>& Item : Value->AsArray())
			{
				if (Item.IsValid() && Item->Type == EJson::Number)
				{
					OutNumbers.Add(Item->AsNumber());
					continue;
				}
				TArray<double> Parsed;
				if (!Item.IsValid() || Item->Type != EJson::String
					|| !ForgeMindMaterial::ParseNumericList(Item->AsString(), Parsed) || Parsed.Num() == 0)
				{
					return false;
				}
				OutNumbers.Append(Parsed);
			}
			return OutNumbers.Num() > 0;
		case EJson::String:
			return ForgeMindMaterial::ParseNumericList(Value->AsString(), OutNumbers) && OutNumbers.Num() > 0;
		case EJson::Object:
		{
			// { "r": .., "g": .., "b": .., "a": .. } - the shape get_asset_ir emits for structs.
			const TSharedPtr<FJsonObject> Obj = Value->AsObject();
			if (!Obj.IsValid())
			{
				return false;
			}
			static const TCHAR* Keys[] = { TEXT("R"), TEXT("G"), TEXT("B"), TEXT("A") };
			for (const TCHAR* Key : Keys)
			{
				double Number = 0.0;
				if (Obj->TryGetNumberField(Key, Number))
				{
					OutNumbers.Add(Number);
				}
				else if (Obj->TryGetNumberField(FString(Key).ToLower(), Number))
				{
					OutNumbers.Add(Number);
				}
			}
			return OutNumbers.Num() == 3 || OutNumbers.Num() == 4;
		}
		default:
			return false;
		}
	}

	/**
	 * Write the node-level "constant" field onto a constant-family expression that
	 * carries its value in scalar members instead of one named property: Constant
	 * (R) / Constant2Vector (R, G) / StaticBool (Value bitfield). The classes with
	 * an FLinearColor "Constant" member resolve by name before reaching here. The
	 * value shapes are the ones both channels emit: number, bool, "[r,g]" /
	 * "[r,g,b,a]" arrays and color text.
	 */
	bool SetExpressionConstant(UObject* Object, const TSharedPtr<FJsonValue>& Value, FString& OutError)
	{
		if (Value.IsValid() && Value->Type == EJson::Boolean)
		{
			FBoolProperty* BoolProperty = CastField<FBoolProperty>(ForgeMindIR::FindProperty(Object, TEXT("Value")));
			if (BoolProperty == nullptr)
			{
				OutError = FString::Printf(TEXT("constant bool has no 'Value' member on %s"), *Object->GetName());
				return false;
			}
			BoolProperty->SetPropertyValue(BoolProperty->ContainerPtrToValuePtr<void>(Object), Value->AsBool());
			return true;
		}

		TArray<double> Numbers;
		if (!JsonToNumbers(Value, Numbers) || Numbers.Num() == 0)
		{
			OutError = FString::Printf(TEXT("value for 'constant' is not a constant on %s"), *Object->GetName());
			return false;
		}
		FFloatProperty* R = CastField<FFloatProperty>(ForgeMindIR::FindProperty(Object, TEXT("R")));
		if (R == nullptr)
		{
			OutError = FString::Printf(TEXT("constant has no scalar member to write on %s"), *Object->GetName());
			return false;
		}
		R->SetPropertyValue(R->ContainerPtrToValuePtr<void>(Object), static_cast<float>(Numbers[0]));
		if (FFloatProperty* G = CastField<FFloatProperty>(ForgeMindIR::FindProperty(Object, TEXT("G"))))
		{
			if (Numbers.Num() > 1)
			{
				G->SetPropertyValue(G->ContainerPtrToValuePtr<void>(Object), static_cast<float>(Numbers[1]));
			}
		}
		return true;
	}

	/** Resolve an enum value from a display name, tolerating the engine prefix ("Surface" -> MD_Surface). */
	int64 ResolveEnumValue(const UEnum* Enum, const FString& Name)
	{
		if (Enum == nullptr || Name.IsEmpty())
		{
			return INDEX_NONE;
		}
		const int64 Direct = Enum->GetValueByNameString(Name);
		if (Direct != INDEX_NONE)
		{
			return Direct;
		}
		for (int32 i = 0; i < Enum->NumEnums(); ++i)
		{
			const FString FullName = Enum->GetNameByIndex(i).ToString();
			const int32 Scope = FullName.Find(TEXT("::"), ESearchCase::IgnoreCase, ESearchDir::FromEnd);
			const FString Bare = Scope != INDEX_NONE ? FullName.Mid(Scope + 2) : FullName;
			if (Bare.Equals(Name, ESearchCase::IgnoreCase))
			{
				return Enum->GetValueByIndex(i);
			}
			const int32 Underscore = Bare.Find(TEXT("_"));
			const FString Tail = Underscore != INDEX_NONE ? Bare.Mid(Underscore + 1) : Bare;
			if (Tail.Equals(Name, ESearchCase::IgnoreCase))
			{
				return Enum->GetValueByIndex(i);
			}
		}
		return INDEX_NONE;
	}
}

FString ForgeMindMaterial::ExpressionClassName(const FString& Type)
{
	if (Type.IsEmpty())
	{
		return Type;
	}
	// ResolveClass tolerates the U prefix either way, so keep the bare
	// "MaterialExpression..." spelling the Asset IR schema uses.
	// The engine's MaterialExpressionLess (MaterialExpressionBinaryOp.h) only
	// compiles through the new HLSL generator (r.MaterialEnableNewHLSLGenerator,
	// off by default in 5.0), so route the Less op to the plugin's If-based
	// compare family (UMaterialExpressionLessMask) before the generic lookup.
	if (Type.Equals(TEXT("Less"), ESearchCase::IgnoreCase)
		|| Type.Equals(TEXT("MaterialExpressionLess"), ESearchCase::IgnoreCase))
	{
		return TEXT("MaterialExpressionLessMask");
	}
	if (Type.StartsWith(TEXT("MaterialExpression"), ESearchCase::IgnoreCase))
	{
		return Type;
	}
	if (Type.StartsWith(TEXT("UMaterialExpression"), ESearchCase::IgnoreCase))
	{
		return Type.RightChop(1);
	}
	if (Type == TEXT("LinearInterpolate") || Type == TEXT("Lerp")) return TEXT("MaterialExpressionLinearInterpolate");
	if (Type == TEXT("Fractional") || Type == TEXT("Frac"))        return TEXT("MaterialExpressionFrac");
	return TEXT("MaterialExpression") + Type;
}

UObject* ForgeMindMaterial::GetEditorOnlyData(UObject* Asset)
{
	if (Asset == nullptr)
	{
		return nullptr;
	}
	FProperty* Property = FindFProperty<FProperty>(Asset->GetClass(), TEXT("EditorOnlyData"));
	FObjectProperty* ObjectProperty = CastField<FObjectProperty>(Property);
	if (ObjectProperty == nullptr)
	{
		return nullptr;
	}
	return ObjectProperty->GetObjectPropertyValue(ObjectProperty->ContainerPtrToValuePtr<void>(Asset));
}

FString ForgeMindMaterial::ExpressionTypeName(const UMaterialExpression* Expression)
{
	if (Expression == nullptr)
	{
		return FString();
	}
	const FString ClassName = Expression->GetClass()->GetName();
	return ClassName.StartsWith(TEXT("U")) ? ClassName.RightChop(1) : ClassName;
}

namespace
{
	/** Locate the expression list of an asset: the editor-only data first, then the asset itself (UE 5.0). */
	FArrayProperty* FindExpressionList(UObject* Asset, void*& OutArrayData, UObject*& OutContainer)
	{
		OutArrayData = nullptr;
		OutContainer = nullptr;
		if (Asset == nullptr)
		{
			return nullptr;
		}
		UObject* Containers[] = { ForgeMindMaterial::GetEditorOnlyData(Asset), Asset };
		for (UObject* Container : Containers)
		{
			if (Container == nullptr)
			{
				continue;
			}
			for (const TCHAR* Candidate : { TEXT("ExpressionCollection"), TEXT("Expressions") })
			{
				FArrayProperty* ArrayProperty = CastField<FArrayProperty>(ForgeMindIR::FindProperty(Container, Candidate));
				if (ArrayProperty == nullptr)
				{
					continue;
				}
				OutArrayData = ArrayProperty->ContainerPtrToValuePtr<void>(Container);
				OutContainer = Container;
				return ArrayProperty;
			}
		}
		return nullptr;
	}
}

void ForgeMindMaterial::GetExpressions(UObject* Asset, TArray<UMaterialExpression*>& OutExpressions)
{
	OutExpressions.Reset();
	void* ArrayData = nullptr;
	UObject* Container = nullptr;
	FArrayProperty* ArrayProperty = FindExpressionList(Asset, ArrayData, Container);
	if (ArrayProperty == nullptr)
	{
		return;
	}
	FObjectProperty* Inner = CastField<FObjectProperty>(ArrayProperty->Inner);
	if (Inner == nullptr)
	{
		return;
	}
	FScriptArrayHelper Helper(ArrayProperty, ArrayData);
	for (int32 i = 0; i < Helper.Num(); ++i)
	{
		UObject* Element = Inner->GetObjectPropertyValue(Helper.GetRawPtr(i));
		if (UMaterialExpression* Expression = Cast<UMaterialExpression>(Element))
		{
			OutExpressions.Add(Expression);
		}
	}
}

bool ForgeMindMaterial::HasExpressionList(UObject* Asset)
{
	void* ArrayData = nullptr;
	UObject* Container = nullptr;
	return FindExpressionList(Asset, ArrayData, Container) != nullptr;
}

bool ForgeMindMaterial::AddExpression(UObject* Asset, UMaterialExpression* Expression)
{
	if (Asset == nullptr || Expression == nullptr)
	{
		return false;
	}
	TArray<UMaterialExpression*> Existing;
	GetExpressions(Asset, Existing);
	if (Existing.Contains(Expression))
	{
		return true;
	}

	void* ArrayData = nullptr;
	UObject* Container = nullptr;
	FArrayProperty* ArrayProperty = FindExpressionList(Asset, ArrayData, Container);
	if (ArrayProperty == nullptr)
	{
		return false;
	}
	FObjectProperty* Inner = CastField<FObjectProperty>(ArrayProperty->Inner);
	if (Inner == nullptr)
	{
		return false;
	}
	FScriptArrayHelper Helper(ArrayProperty, ArrayData);
	Inner->SetObjectPropertyValue(Helper.GetRawPtr(Helper.AddValue()), Expression);
	return true;
}

UMaterialExpression* ForgeMindMaterial::CreateExpression(UObject* Asset, UClass* Class, const FString& ParameterName)
{
	if (Asset == nullptr || Class == nullptr || !Class->IsChildOf(UMaterialExpression::StaticClass()))
	{
		return nullptr;
	}
	UMaterialExpression* Expression = NewObject<UMaterialExpression>(Asset, Class, NAME_None, RF_Transactional);
	if (Expression == nullptr)
	{
		return nullptr;
	}
	Expression->Material = Cast<UMaterial>(Asset);

	if (!ParameterName.IsEmpty())
	{
		// Write the name before refreshing the GUID: the parameter identity is
		// derived from the name, so the other order hands out a GUID for an empty
		// parameter.
		FString Error;
		SetValue(Expression, TEXT("ParameterName"), MakeShared<FJsonValueString>(ParameterName), Error);
		Expression->UpdateParameterGuid(true, true);
	}

	AddExpression(Asset, Expression);
	return Expression;
}

UMaterialExpression* ForgeMindMaterial::FindExpression(UObject* Asset, const FString& Id)
{
	if (Asset == nullptr || Id.IsEmpty())
	{
		return nullptr;
	}
	TArray<UMaterialExpression*> Expressions;
	GetExpressions(Asset, Expressions);
	for (UMaterialExpression* Expression : Expressions)
	{
		if (Expression != nullptr && Expression->GetName() == Id)
		{
			return Expression;
		}
	}
	return nullptr;
}

FExpressionInput* ForgeMindMaterial::GetExpressionInput(UObject* Owner, const FString& Name)
{
	if (Owner == nullptr || Name.IsEmpty())
	{
		return nullptr;
	}
	FProperty* Property = ForgeMindIR::FindProperty(Owner, Name);
	FStructProperty* StructProperty = CastField<FStructProperty>(Property);
	if (StructProperty == nullptr || StructProperty->Struct == nullptr
		|| !StructProperty->Struct->GetName().Equals(TEXT("ExpressionInput")))
	{
		return nullptr;
	}
	return reinterpret_cast<FExpressionInput*>(StructProperty->ContainerPtrToValuePtr<void>(Owner));
}

FExpressionInput* ForgeMindMaterial::GetMaterialOutputInput(UObject* Asset, const FString& Name)
{
	if (Asset == nullptr || Name.IsEmpty())
	{
		return nullptr;
	}
	// UE 5.0 keeps the material outputs as typed F*MaterialInput members that are
	// only reachable through UMaterial::GetExpressionInputForProperty.
	if (UMaterial* Material = Cast<UMaterial>(Asset))
	{
		const UEnum* PropertyEnum = StaticEnum<EMaterialProperty>();
		const int64 Value = ResolveEnumValue(PropertyEnum, TEXT("MP_") + Name);
		if (Value != INDEX_NONE)
		{
			if (FExpressionInput* Input = Material->GetExpressionInputForProperty(static_cast<EMaterialProperty>(Value)))
			{
				return Input;
			}
		}
	}
	if (UObject* EditorData = ForgeMindMaterial::GetEditorOnlyData(Asset))
	{
		if (FExpressionInput* Input = ForgeMindMaterial::GetExpressionInput(EditorData, Name))
		{
			return Input;
		}
	}
	return GetExpressionInput(Asset, Name);
}

void ForgeMindMaterial::GetConnectedMaterialOutputs(UObject* Asset, TArray<TPair<FString, FExpressionInput*>>& OutOutputs)
{
	OutOutputs.Reset();
	if (Asset == nullptr)
	{
		return;
	}
	const UEnum* PropertyEnum = StaticEnum<EMaterialProperty>();
	if (PropertyEnum == nullptr)
	{
		return;
	}
	UMaterial* Material = Cast<UMaterial>(Asset);
	for (int32 i = 0; i < PropertyEnum->NumEnums(); ++i)
	{
		const FString FullName = PropertyEnum->GetNameByIndex(i).ToString();
		const int32 Scope = FullName.Find(TEXT("::"), ESearchCase::IgnoreCase, ESearchDir::FromEnd);
		const FString Bare = Scope != INDEX_NONE ? FullName.Mid(Scope + 2) : FullName;
		if (!Bare.StartsWith(TEXT("MP_")))
		{
			continue;
		}
		FExpressionInput* Input = nullptr;
		if (Material != nullptr)
		{
			Input = Material->GetExpressionInputForProperty(static_cast<EMaterialProperty>(PropertyEnum->GetValueByIndex(i)));
		}
		else
		{
			Input = GetMaterialOutputInput(Asset, Bare.RightChop(3));
		}
		if (Input == nullptr || Input->Expression == nullptr)
		{
			continue;
		}
		OutOutputs.Add(TPair<FString, FExpressionInput*>(Bare.RightChop(3), Input));
	}
}

int32 ForgeMindMaterial::ResolveOutputIndex(const FString& PinName)
{
	int32 Index = 0;
	TryResolveOutputIndex(PinName, Index);
	return Index;
}

bool ForgeMindMaterial::TryResolveOutputIndex(const FString& PinName, int32& OutIndex)
{
	const FString Pin = PinName.TrimStartAndEnd();
	if (Pin.IsEmpty() || Pin == TEXT("Out") || Pin == TEXT("Result") || Pin == TEXT("Color")
		|| Pin == TEXT("RGB") || Pin == TEXT("RGBA") || Pin == TEXT("Material")
		|| Pin == TEXT("MaterialAttributes"))
	{
		OutIndex = 0;
		return true;
	}
	if (Pin == TEXT("R")) { OutIndex = 1; return true; }
	if (Pin == TEXT("G")) { OutIndex = 2; return true; }
	if (Pin == TEXT("B")) { OutIndex = 3; return true; }
	if (Pin == TEXT("A")) { OutIndex = 4; return true; }
	OutIndex = 0;
	// An unrecognised pin name is reported by the caller instead of quietly being
	// wired to output 0, so a typo cannot turn into a silent wrong connection.
	return false;
}

bool ForgeMindMaterial::ParseNumericList(const FString& Text, TArray<double>& OutValues)
{
	OutValues.Reset();
	FString Work = Text.TrimStartAndEnd();
	// Wrapping brackets of either kind are decoration around the component list.
	while (Work.StartsWith(TEXT("(")) || Work.StartsWith(TEXT("[")))
	{
		Work = Work.RightChop(1);
	}
	while (Work.EndsWith(TEXT(")")) || Work.EndsWith(TEXT("]")))
	{
		Work = Work.LeftChop(1);
	}
	Work = Work.TrimStartAndEnd();
	if (Work.IsEmpty())
	{
		return false;
	}

	TArray<FString> Parts;
	Work.ParseIntoArray(Parts, TEXT(","), true);
	for (const FString& Part : Parts)
	{
		FString Item = Part.TrimStartAndEnd();
		const int32 Equals = Item.Find(TEXT("="), ESearchCase::CaseSensitive);
		if (Equals != INDEX_NONE)
		{
			Item = Item.Mid(Equals + 1).TrimStartAndEnd();
		}
		if (Item.IsEmpty())
		{
			return false;
		}
		// A literal suffix ("0.5f", "1d") is dropped only when what remains parses
		// as a number, so a stray word is still reported instead of half-read.
		if (!Item.IsNumeric())
		{
			const FString Shorter = Item.Left(Item.Len() - 1);
			if (!Shorter.IsNumeric())
			{
				return false;
			}
			Item = Shorter;
		}
		OutValues.Add(FCString::Atod(*Item));
	}
	return OutValues.Num() > 0;
}

bool ForgeMindMaterial::SetValue(UObject* Object, const FString& PropertyName, const TSharedPtr<FJsonValue>& Value, FString& OutError)
{
	if (Object == nullptr || PropertyName.IsEmpty())
	{
		OutError = TEXT("SetValue called without an object or property name");
		return false;
	}
	FProperty* Property = ForgeMindIR::FindProperty(Object, PropertyName);
	if (Property == nullptr)
	{
		// The IR schema carries a constant-family value as one node-level "constant"
		// field, while the engine stores it in the member each class actually owns
		// (R / R,G / Value bitfield). The read side emits the same field for the whole
		// family, so the write side resolves it here once instead of reporting
		// "property not found" and silently leaving the default value in place.
		if (PropertyName.Equals(TEXT("constant"), ESearchCase::IgnoreCase)
			&& Cast<UMaterialExpression>(Object) != nullptr)
		{
			return SetExpressionConstant(Object, Value, OutError);
		}
		OutError = FString::Printf(TEXT("property '%s' not found on %s"), *PropertyName, *Object->GetName());
		return false;
	}
	if (!Value.IsValid() || Value->Type == EJson::Null)
	{
		return true;
	}

	void* Data = Property->ContainerPtrToValuePtr<void>(Object);

	// Colors: written component-wise so the "[1,0,0]" array form, the "(R=1,G=0,B=0)"
	// text form and the {"R":..} object form all land on the same value.
	if (FStructProperty* StructProperty = CastField<FStructProperty>(Property))
	{
		if (IsLinearColorStruct(StructProperty->Struct))
		{
			TArray<double> Numbers;
			if (!JsonToNumbers(Value, Numbers))
			{
				OutError = FString::Printf(TEXT("value for color property '%s' is not a color"), *PropertyName);
				return false;
			}
			const FLinearColor Color(
				static_cast<float>(Numbers.Num() > 0 ? Numbers[0] : 0.0),
				static_cast<float>(Numbers.Num() > 1 ? Numbers[1] : 0.0),
				static_cast<float>(Numbers.Num() > 2 ? Numbers[2] : 0.0),
				static_cast<float>(Numbers.Num() > 3 ? Numbers[3] : 1.0));
			*static_cast<FLinearColor*>(Data) = Color;
			return true;
		}
	}

	// Enums: bare display names are what both the DSL and the step commands carry.
	if (FEnumProperty* EnumProperty = CastField<FEnumProperty>(Property))
	{
		const UEnum* Enum = EnumProperty->GetEnum();
		if (Value->Type == EJson::String)
		{
			const int64 EnumValue = ResolveEnumValue(Enum, Value->AsString());
			if (EnumValue == INDEX_NONE)
			{
				OutError = FString::Printf(TEXT("'%s' is not a value of enum %s"), *Value->AsString(), *GetNameSafe(Enum));
				return false;
			}
			EnumProperty->GetUnderlyingProperty()->SetIntPropertyValue(Data, EnumValue);
			return true;
		}
		if (Value->Type == EJson::Number)
		{
			EnumProperty->GetUnderlyingProperty()->SetIntPropertyValue(Data, static_cast<int64>(Value->AsNumber()));
			return true;
		}
	}
	if (FByteProperty* ByteProperty = CastField<FByteProperty>(Property))
	{
		if (ByteProperty->Enum != nullptr && Value->Type == EJson::String)
		{
			const int64 EnumValue = ResolveEnumValue(ByteProperty->Enum, Value->AsString());
			if (EnumValue == INDEX_NONE)
			{
				OutError = FString::Printf(TEXT("'%s' is not a value of enum %s"), *Value->AsString(), *GetNameSafe(ByteProperty->Enum));
				return false;
			}
			ByteProperty->SetIntPropertyValue(Data, static_cast<uint64>(EnumValue));
			return true;
		}
	}

	// Asset references must actually resolve. FObjectProperty::ImportText accepts a path
	// it cannot load - it returns success with a null pointer - and a silently null
	// texture is exactly how a bad reference turns into a material compile error further
	// down the graph (the missing /Game/T_Brick made the If node report an error while
	// every step command claimed success). So verify the property afterwards and report.
	if (FObjectProperty* ObjectProperty = CastField<FObjectProperty>(Property))
	{
		if (Value->Type == EJson::String && !Value->AsString().IsEmpty())
		{
			FString ImportError;
			const bool bImported = ForgeMindIR::SetProperty(Object, Property->GetName(), Value, ImportError);
			if (ObjectProperty->GetObjectPropertyValue(Data) == nullptr)
			{
				OutError = FString::Printf(TEXT("asset reference '%s' did not resolve to an object (%s.%s): %s"),
					*Value->AsString(), *Object->GetName(), *Property->GetName(),
					ImportError.IsEmpty() ? TEXT("the property is still null after the import") : *ImportError);
				return false;
			}
			// The editor UI syncs the sampler type whenever a texture is assigned
			// (UMaterialExpressionTextureSample::PostEditChangeProperty ->
			// AutoSetSampleType); without the same sync a bridge-assigned mask
			// texture keeps the class-default Color sampler and the material fails
			// to compile with "Sampler type is Color, should be Masks".
			if (Object->IsA(UMaterialExpressionTextureBase::StaticClass())
				&& Property->GetFName() == TEXT("Texture"))
			{
				Cast<UMaterialExpressionTextureBase>(Object)->AutoSetSampleType();
			}
			return bImported;
		}
	}

	return ForgeMindIR::SetProperty(Object, Property->GetName(), Value, OutError);
}

TSharedPtr<FJsonValue> ForgeMindMaterial::GetValue(UObject* Object, const FString& PropertyName)
{
	if (Object == nullptr || PropertyName.IsEmpty())
	{
		return nullptr;
	}
	FProperty* Property = ForgeMindIR::FindProperty(Object, PropertyName);
	if (Property == nullptr)
	{
		return nullptr;
	}
	void* Data = Property->ContainerPtrToValuePtr<void>(Object);

	if (FStructProperty* StructProperty = CastField<FStructProperty>(Property))
	{
		if (IsLinearColorStruct(StructProperty->Struct))
		{
			const FLinearColor& Color = *static_cast<const FLinearColor*>(Data);
			return MakeShared<FJsonValueArray>(TArray<TSharedPtr<FJsonValue>>{
				MakeShared<FJsonValueNumber>(Color.R),
				MakeShared<FJsonValueNumber>(Color.G),
				MakeShared<FJsonValueNumber>(Color.B),
				MakeShared<FJsonValueNumber>(Color.A),
			});
		}
	}
	if (FObjectProperty* ObjectProperty = CastField<FObjectProperty>(Property))
	{
		const UObject* Referenced = ObjectProperty->GetObjectPropertyValue(Data);
		if (Referenced == nullptr)
		{
			return MakeShared<FJsonValueNull>();
		}
		return MakeShared<FJsonValueString>(Referenced->GetPathName());
	}
	if (FEnumProperty* EnumProperty = CastField<FEnumProperty>(Property))
	{
		const UEnum* Enum = EnumProperty->GetEnum();
		const int64 Value = static_cast<int64>(EnumProperty->GetUnderlyingProperty()->GetUnsignedIntPropertyValue(Data));
		return MakeShared<FJsonValueString>(Enum != nullptr ? Enum->GetNameStringByValue(Value) : FString::Printf(TEXT("%lld"), Value));
	}
	if (FByteProperty* ByteProperty = CastField<FByteProperty>(Property))
	{
		if (ByteProperty->Enum != nullptr)
		{
			const uint8 Value = static_cast<uint8>(ByteProperty->GetUnsignedIntPropertyValue(Data));
			return MakeShared<FJsonValueString>(ByteProperty->Enum->GetNameStringByValue(Value));
		}
	}
	return ForgeMindIR::GetProperty(Object, Property->GetName());
}

UMaterialExpression* ForgeMindMaterial::CreateConstantExpression(UObject* Asset, const TSharedPtr<FJsonValue>& Value, FString& OutError)
{
	TArray<double> Numbers;
	if (Value.IsValid() && Value->Type == EJson::Array)
	{
		if (!JsonToNumbers(Value, Numbers))
		{
			OutError = TEXT("constant value carries non-numeric components");
			return nullptr;
		}
	}
	else if (!JsonToNumbers(Value, Numbers))
	{
		OutError = FString::Printf(TEXT("a JSON value of type %d is not a constant"),
			Value.IsValid() ? static_cast<int32>(Value->Type) : -1);
		return nullptr;
	}

	UClass* Class = nullptr;
	if (Numbers.Num() == 1)
	{
		Class = UMaterialExpressionConstant::StaticClass();
	}
	else if (Numbers.Num() == 2)
	{
		Class = UMaterialExpressionConstant2Vector::StaticClass();
	}
	else if (Numbers.Num() == 3)
	{
		Class = UMaterialExpressionConstant3Vector::StaticClass();
	}
	else if (Numbers.Num() == 4)
	{
		Class = UMaterialExpressionConstant4Vector::StaticClass();
	}
	else
	{
		OutError = FString::Printf(TEXT("a constant of %d components has no material expression class"), Numbers.Num());
		return nullptr;
	}

	UMaterialExpression* Expression = CreateExpression(Asset, Class, FString());
	if (Expression == nullptr)
	{
		OutError = TEXT("failed to create the constant expression");
		return nullptr;
	}

	const TSharedPtr<FJsonValue> First = MakeShared<FJsonValueNumber>(Numbers[0]);
	if (Numbers.Num() == 1)
	{
		SetValue(Expression, TEXT("R"), First, OutError);
	}
	else if (Numbers.Num() == 2)
	{
		SetValue(Expression, TEXT("R"), First, OutError);
		SetValue(Expression, TEXT("G"), MakeShared<FJsonValueNumber>(Numbers[1]), OutError);
	}
	else
	{
		// Constant3Vector / Constant4Vector store an FLinearColor; the 3-component
		// form keeps alpha at 1, which is what the editor writes too.
		const TArray<TSharedPtr<FJsonValue>> Components = {
			MakeShared<FJsonValueNumber>(Numbers[0]),
			MakeShared<FJsonValueNumber>(Numbers[1]),
			MakeShared<FJsonValueNumber>(Numbers[2]),
			MakeShared<FJsonValueNumber>(Numbers.Num() > 3 ? Numbers[3] : 1.0),
		};
		SetValue(Expression, TEXT("Constant"), MakeShared<FJsonValueArray>(Components), OutError);
	}
	return Expression;
}

bool ForgeMindMaterial::ConnectConstant(FExpressionInput* Input, UObject* Asset, const TSharedPtr<FJsonValue>& Value, FString& OutError)
{
	if (Input == nullptr || Asset == nullptr)
	{
		OutError = TEXT("no input to connect a constant to");
		return false;
	}
	UMaterialExpression* Constant = CreateConstantExpression(Asset, Value, OutError);
	if (Constant == nullptr)
	{
		return false;
	}
	Input->Connect(0, Constant);
	return true;
}

bool ForgeMindMaterial::ConnectOutputInput(UObject* Asset, FExpressionInput* Input, UMaterialExpression* Source, int32 OutputIndex, FString& OutError)
{
	if (Asset == nullptr || Input == nullptr || Source == nullptr || OutputIndex < 0)
	{
		OutError = TEXT("ConnectOutputInput needs an asset, an input, a source and a non-negative output index");
		return false;
	}

	// The default output connects directly. Everything else the swizzle tables
	// produce (R/G/B/A = 1..4) is a CHANNEL request and is realized the way the
	// editor builds it: a ComponentMask extracting exactly that channel from the
	// source's default output. Wiring the raw index instead would address an
	// output pin the source does not declare - ConnectExpression silently drops
	// such a connection and the input stays unwired - and where an output does
	// exist at that index its name does not mean the requested channel.
	if (OutputIndex == 0)
	{
		Input->Connect(0, Source);
		return true;
	}

	if (OutputIndex > 4)
	{
		OutError = FString::Printf(TEXT("output index %d is out of range: the source carries %d output(s)"), OutputIndex, Source->GetOutputs().Num());
		return false;
	}

	UClass* MaskClass = ForgeMindIR::ResolveClass(TEXT("MaterialExpressionComponentMask"));
	if (MaskClass == nullptr)
	{
		OutError = TEXT("MaterialExpressionComponentMask is not available in this editor");
		return false;
	}
	UMaterialExpression* Mask = CreateExpression(Asset, MaskClass, FString());
	if (Mask == nullptr)
	{
		OutError = TEXT("failed to create the channel-extraction ComponentMask");
		return false;
	}
	// The constructor enables R and G by default, so all four channels are written
	// explicitly and exactly the requested one stays set.
	static const TCHAR* Channels[] = { TEXT("R"), TEXT("G"), TEXT("B"), TEXT("A") };
	for (int32 Channel = 0; Channel < 4; ++Channel)
	{
		FString Error;
		if (!SetValue(Mask, Channels[Channel], MakeShared<FJsonValueBoolean>(Channel == OutputIndex - 1), Error))
		{
			OutError = FString::Printf(TEXT("%s: %s"), Channels[Channel], *Error);
			return false;
		}
	}
	FExpressionInput* MaskInput = GetExpressionInput(Mask, TEXT("Input"));
	if (MaskInput == nullptr)
	{
		OutError = TEXT("the ComponentMask carries no 'Input' member on this engine version");
		return false;
	}
	MaskInput->Connect(0, Source);
	Input->Connect(0, Mask);
	return true;
}

UObject* ForgeMindMaterial::ResolveMaterial(const FString& Path)
{
	if (Path.IsEmpty())
	{
		return nullptr;
	}
	return ForgeMindIR::LoadAsset(ForgeMindIR::NormalizePath(Path));
}

void ForgeMindMaterial::ArrangeLayout(UObject* Asset)
{
	if (Asset == nullptr)
	{
		return;
	}
	TArray<UMaterialExpression*> Expressions;
	GetExpressions(Asset, Expressions);
	FForgeMindGraphLayout::ArrangeMaterialExpressions(Asset, Expressions);
}
