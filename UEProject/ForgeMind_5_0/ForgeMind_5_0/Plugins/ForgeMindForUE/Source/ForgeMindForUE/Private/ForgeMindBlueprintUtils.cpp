// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#include "ForgeMindBlueprintUtils.h"

#include "AssetRegistry/AssetRegistryModule.h"

#include "ForgeMindBridgeClient.h"

#include "Dom/JsonObject.h"
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphNode.h"
#include "EdGraphSchema_K2.h"
#include "Engine/Blueprint.h"
#include "Engine/BlueprintGeneratedClass.h"
#include "Engine/Level.h"
#include "Engine/LevelScriptActor.h"
#include "Engine/LevelScriptBlueprint.h"
#include "Engine/World.h"
#include "Editor.h"
#include "Kismet2/BlueprintEditorUtils.h"
#include "Kismet2/KismetEditorUtilities.h"
#include "Misc/Paths.h"
#include "UObject/UObjectGlobals.h"
#include "UObject/UnrealType.h"
#include "Engine/UserDefinedEnum.h"
#include "Math/Color.h"
#include "Engine/UserDefinedStruct.h"
#include "Kismet2/StructureEditorUtils.h"
#include "Engine/MemberReference.h"
#include "K2Node_CreateDelegate.h"
#include "K2Node_CustomEvent.h"
#include "K2Node_CallFunction.h"
#include "UserDefinedStructure/UserDefinedStructEditorData.h"

namespace ForgeMindBlueprintUtils
{
	// JSON numbers can arrive as strings ("1.5"); accept both forms.
	static bool TryGetNumberLoose(const TSharedPtr<FJsonObject>& Object, const FString& Key, double& OutValue)
	{
		if (Object->TryGetNumberField(Key, OutValue))
		{
			return true;
		}
		FString AsText;
		if (Object->TryGetStringField(Key, AsText) && AsText.IsNumeric())
		{
			OutValue = FCString::Atod(*AsText);
			return true;
		}
		return false;
	}

	// JSON field lookup tolerant to casing (x/X, pitch/Pitch, ...).
	static bool TryGetNumberCaseInsensitive(const TSharedPtr<FJsonObject>& Object, const TArray<FString>& Casings, double& OutValue)
	{
		for (const FString& Key : Casings)
		{
			if (TryGetNumberLoose(Object, Key, OutValue))
			{
				return true;
			}
		}
		return false;
	}

	// Three components from {named fields}, [n, n, n] or a bare "a,b,c" string.
	static bool JsonToComponents3(const TSharedPtr<FJsonValue>& JsonValue, const TArray<FString>& KeyA, const TArray<FString>& KeyB, const TArray<FString>& KeyC, double* Out)
	{
		if (JsonValue->Type == EJson::Object)
		{
			const TSharedPtr<FJsonObject>& Object = JsonValue->AsObject();
			return TryGetNumberCaseInsensitive(Object, KeyA, Out[0])
				&& TryGetNumberCaseInsensitive(Object, KeyB, Out[1])
				&& TryGetNumberCaseInsensitive(Object, KeyC, Out[2]);
		}
		if (JsonValue->Type == EJson::Array)
		{
			const TArray<TSharedPtr<FJsonValue>>& Items = JsonValue->AsArray();
			if (Items.Num() != 3)
			{
				return false;
			}
			for (int32 Index = 0; Index < 3; ++Index)
			{
				if (Items[Index]->Type == EJson::Number)
				{
					Out[Index] = Items[Index]->AsNumber();
				}
				else if (Items[Index]->Type == EJson::String && Items[Index]->AsString().IsNumeric())
				{
					Out[Index] = FCString::Atod(*Items[Index]->AsString());
				}
				else
				{
					return false;
				}
			}
			return true;
		}
		if (JsonValue->Type == EJson::String)
		{
			TArray<FString> Parts;
			JsonValue->AsString().ParseIntoArray(Parts, TEXT(","), true);
			if (Parts.Num() != 3)
			{
				return false;
			}
			for (int32 Index = 0; Index < 3; ++Index)
			{
				Parts[Index].TrimStartAndEndInline();
				if (!Parts[Index].IsNumeric())
				{
					return false;
				}
				Out[Index] = FCString::Atod(*Parts[Index]);
			}
			return true;
		}
		return false;
	}

	// Delimited join - FString::Join does not exist in 5.6.
	static FString JoinStrings(const TArray<FString>& Items, const TCHAR* Delim)
	{
		FString Out;
		for (int32 Index = 0; Index < Items.Num(); ++Index)
		{
			if (Index > 0)
			{
				Out += Delim;
			}
			Out += Items[Index];
		}
		return Out;
	}

	// Warns about object keys that match no known struct member - models
	// occasionally hallucinate member names, and silently dropping them would
	// hide the mistake. Non-object inputs (arrays, engine text strings) carry
	// no member names to check.
	static void WarnUnknownMemberKeys(const TSharedPtr<FJsonValue>& JsonValue, const TArray<FString>& KnownKeys, const FString& Context, TArray<FString>& OutWarnings)
	{
		if (!JsonValue.IsValid() || JsonValue->Type != EJson::Object)
		{
			return;
		}
		for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : JsonValue->AsObject()->Values)
		{
			if (!KnownKeys.Contains(Pair.Key))
			{
				OutWarnings.Add(FString::Printf(TEXT("%s: unknown struct member '%s' ignored (valid: %s)"),
					*Context, *Pair.Key, *JoinStrings(KnownKeys, TEXT(", "))));
			}
		}
	}

	// Scalar (non-struct, non-container) default text from a JSON value, in
	// the engine's importable text form.
	static bool ScalarDefaultFromJsonValue(const TSharedPtr<FJsonValue>& DefaultJson, const FEdGraphPinType& PinType, FString& OutDefault, FString& OutReason)
	{
		switch (DefaultJson->Type)
		{
		case EJson::String:
			OutDefault = DefaultJson->AsString();
			// Integer pins need a plain integer literal - models often quote
			// numbers, and values like "1e9" or "5.0" would fail ParseInt64.
			if ((PinType.PinCategory == UEdGraphSchema_K2::PC_Int
				|| PinType.PinCategory == UEdGraphSchema_K2::PC_Int64
				|| PinType.PinCategory == UEdGraphSchema_K2::PC_Byte)
				&& OutDefault.IsNumeric())
			{
				// Plain integer literals round-trip exactly through Atoi64 (needed
				// for int64 values beyond double precision); anything else is
				// rounded via double since ParseInt64 would reject it outright.
				const bool bPlainInteger = !OutDefault.Contains(TEXT(".")) && !OutDefault.Contains(TEXT("e")) && !OutDefault.Contains(TEXT("E"));
				OutDefault = bPlainInteger
					? FString::Printf(TEXT("%lld"), FCString::Atoi64(*OutDefault))
					: FString::Printf(TEXT("%lld"), static_cast<int64>(FCString::Atod(*OutDefault)));
			}
			return true;
		case EJson::Boolean:
			OutDefault = DefaultJson->AsBool() ? TEXT("true") : TEXT("false");
			return true;
		case EJson::Number:
			// Integer pins need a plain integer literal: FDefaultValueHelper::
			// ParseInt64 rejects scientific notation, and %g switches to it (and
			// loses precision) for large int64 values.
			if (PinType.PinCategory == UEdGraphSchema_K2::PC_Int
				|| PinType.PinCategory == UEdGraphSchema_K2::PC_Int64
				|| PinType.PinCategory == UEdGraphSchema_K2::PC_Byte)
			{
				OutDefault = FString::Printf(TEXT("%lld"), static_cast<int64>(DefaultJson->AsNumber()));
			}
			else if (PinType.PinCategory == UEdGraphSchema_K2::PC_Real
				&& PinType.PinSubCategory == UEdGraphSchema_K2::PC_Double)
			{
				// doubles need 17 significant digits to survive the JSON round trip.
				OutDefault = FString::Printf(TEXT("%.17g"), DefaultJson->AsNumber());
			}
			else
			{
				OutDefault = FString::Printf(TEXT("%g"), DefaultJson->AsNumber());
			}
			return true;
		default:
			OutReason = TEXT("'default' must be a scalar value (string, number or boolean) for this variable type");
			return false;
		}
	}

	// Quotes a value for the delimited container import grammar
	// (FArrayProperty/FMapProperty::ImportText parses elements with
	// PPF_Delimited): needed whenever the text could contain delimiters.
	static FString QuoteForDelimitedImport(const FString& Text)
	{
		FString Escaped = Text.Replace(TEXT("\\"), TEXT("\\\\")).Replace(TEXT("\""), TEXT("\\\""));
		return FString::Printf(TEXT("\"%s\""), *Escaped);
	}

	// Three named components out of an object/array/string, formatted as
	// (NameA=v,NameB=v,NameC=v) - the property text form that
	// FStructProperty::ImportText parses for struct elements inside containers.
	static bool StructElementComponents3(const TSharedPtr<FJsonValue>& JsonValue, const TArray<FString>& CasingsA, const TArray<FString>& CasingsB, const TArray<FString>& CasingsC, const TCHAR* NameA, const TCHAR* NameB, const TCHAR* NameC, FString& OutText, FString& OutReason)
	{
		double Components[3];
		if (!JsonToComponents3(JsonValue, CasingsA, CasingsB, CasingsC, Components))
		{
			OutReason = FString::Printf(TEXT("a %s/%s/%s struct element default must be {named fields}, [a,b,c] or \"a,b,c\""), NameA, NameB, NameC);
			return false;
		}
		OutText = FString::Printf(TEXT("(%s=%f,%s=%f,%s=%f)"), NameA, Components[0], NameB, Components[1], NameC, Components[2]);
		return true;
	}

	// Text for one container entry in the property import-text form that
	// FArrayProperty/FSetProperty/FMapProperty::ImportText parses. Elements
	// are imported through the inner property's ImportText, so struct elements
	// use the (Member=Value,...) form rather than the top-level "x,y,z"
	// shortcut that PropertyValueFromString applies to bare struct variables.
	static bool ContainerElementTextFromJsonValue(const TSharedPtr<FJsonValue>& Value, const FEdGraphPinType& ElemType, FString& OutText, FString& OutReason, TArray<FString>& OutWarnings)
	{
		if (!Value.IsValid() || Value->Type == EJson::Null)
		{
			OutReason = TEXT("container default entries must not be null");
			return false;
		}

		if (ElemType.PinCategory == UEdGraphSchema_K2::PC_Struct)
		{
			const UScriptStruct* Struct = Cast<UScriptStruct>(ElemType.PinSubCategoryObject.Get());
			if (Struct == nullptr)
			{
				OutReason = TEXT("the container element struct type is missing");
				return false;
			}
			// Strings pass through as ready-made property text.
			if (Value->Type == EJson::String)
			{
				OutText = Value->AsString();
				return true;
			}
			if (Value->Type != EJson::Object)
			{
				OutReason = FString::Printf(TEXT("a '%s' container element default must be an object or a property-text string"), *Struct->GetName());
				return false;
			}
			if (Struct == TBaseStructure<FVector>::Get())
			{
				WarnUnknownMemberKeys(Value, { TEXT("X"), TEXT("x"), TEXT("Y"), TEXT("y"), TEXT("Z"), TEXT("z") }, TEXT("vector container element"), OutWarnings);
				return StructElementComponents3(Value, { TEXT("X"), TEXT("x") }, { TEXT("Y"), TEXT("y") }, { TEXT("Z"), TEXT("z") }, TEXT("X"), TEXT("Y"), TEXT("Z"), OutText, OutReason);
			}
			if (Struct == TBaseStructure<FRotator>::Get())
			{
				WarnUnknownMemberKeys(Value, { TEXT("Pitch"), TEXT("pitch"), TEXT("Yaw"), TEXT("yaw"), TEXT("Roll"), TEXT("roll") }, TEXT("rotator container element"), OutWarnings);
				return StructElementComponents3(Value, { TEXT("Pitch"), TEXT("pitch") }, { TEXT("Yaw"), TEXT("yaw") }, { TEXT("Roll"), TEXT("roll") }, TEXT("Pitch"), TEXT("Yaw"), TEXT("Roll"), OutText, OutReason);
			}
			if (Struct == TBaseStructure<FTransform>::Get())
			{
				WarnUnknownMemberKeys(Value, { TEXT("Translation"), TEXT("translation"), TEXT("Rotation"), TEXT("rotation"), TEXT("Scale3D"), TEXT("scale3d"), TEXT("Scale"), TEXT("scale") }, TEXT("transform container element"), OutWarnings);
				FString Translation = TEXT("(X=0.000000,Y=0.000000,Z=0.000000)");
				FString Rotation = TEXT("(Pitch=0.000000,Yaw=0.000000,Roll=0.000000)");
				FString Scale = TEXT("(X=1.000000,Y=1.000000,Z=1.000000)");
				const TSharedPtr<FJsonObject>& Object = Value->AsObject();
				TSharedPtr<FJsonValue> Sub = Object->Values.FindRef(TEXT("Translation"));
				if (!Sub.IsValid()) { Sub = Object->Values.FindRef(TEXT("translation")); }
				if (Sub.IsValid() && !StructElementComponents3(Sub, { TEXT("X"), TEXT("x") }, { TEXT("Y"), TEXT("y") }, { TEXT("Z"), TEXT("z") }, TEXT("X"), TEXT("Y"), TEXT("Z"), Translation, OutReason))
				{
					return false;
				}
				Sub = Object->Values.FindRef(TEXT("Rotation"));
				if (!Sub.IsValid()) { Sub = Object->Values.FindRef(TEXT("rotation")); }
				if (Sub.IsValid() && !StructElementComponents3(Sub, { TEXT("Pitch"), TEXT("pitch") }, { TEXT("Yaw"), TEXT("yaw") }, { TEXT("Roll"), TEXT("roll") }, TEXT("Pitch"), TEXT("Yaw"), TEXT("Roll"), Rotation, OutReason))
				{
					return false;
				}
				Sub = Object->Values.FindRef(TEXT("Scale3D"));
				if (!Sub.IsValid()) { Sub = Object->Values.FindRef(TEXT("scale3d")); }
				if (!Sub.IsValid()) { Sub = Object->Values.FindRef(TEXT("Scale")); }
				if (!Sub.IsValid()) { Sub = Object->Values.FindRef(TEXT("scale")); }
				if (Sub.IsValid() && !StructElementComponents3(Sub, { TEXT("X"), TEXT("x") }, { TEXT("Y"), TEXT("y") }, { TEXT("Z"), TEXT("z") }, TEXT("X"), TEXT("Y"), TEXT("Z"), Scale, OutReason))
				{
					return false;
				}
				OutText = FString::Printf(TEXT("(Translation=%s,Rotation=%s,Scale3D=%s)"), *Translation, *Rotation, *Scale);
				return true;
			}
			// Generic struct: (Member=Value,...) with scalar members, same
			// member validation as the top-level generic struct branch.
			const TSharedPtr<FJsonObject>& Object = Value->AsObject();
			FString Text = TEXT("(");
			bool bFirst = true;
			for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : Object->Values)
			{
				if (Struct->FindPropertyByName(FName(*Pair.Key)) == nullptr)
				{
					TArray<FString> ValidNames;
					for (TFieldIterator<FProperty> MemberIt(Struct); MemberIt; ++MemberIt)
					{
						ValidNames.Add(MemberIt->GetName());
					}
					OutWarnings.Add(FString::Printf(TEXT("'%s' container element default: unknown member '%s' ignored (valid: %s)"),
						*Struct->GetName(), *Pair.Key, *JoinStrings(ValidNames, TEXT(", "))));
					continue;
				}
				FString Member;
				switch (Pair.Value.IsValid() ? Pair.Value->Type : EJson::None)
				{
				case EJson::Number:
					Member = FString::Printf(TEXT("%g"), Pair.Value->AsNumber());
					break;
				case EJson::Boolean:
					Member = Pair.Value->AsBool() ? TEXT("true") : TEXT("false");
					break;
				case EJson::String:
					Member = Pair.Value->AsString();
					break;
				default:
					OutReason = FString::Printf(TEXT("struct member '%s' must be a scalar value"), *Pair.Key);
					return false;
				}
				Text += FString::Printf(TEXT("%s%s=%s"), bFirst ? TEXT("") : TEXT(","), *Pair.Key, *Member);
				bFirst = false;
			}
			OutText = Text + TEXT(")");
			return true;
		}

		if (!ScalarDefaultFromJsonValue(Value, ElemType, OutText, OutReason))
		{
			return false;
		}
		// Quote whatever the delimited grammar might choke on; names with plain
		// identifiers stay bare. (UE 5.6's FindChar only has the two-argument
		// overload, so one dummy index serves the whole check.)
		int32 CharIndex;
		const bool bNeedsQuotes = ElemType.PinCategory == UEdGraphSchema_K2::PC_String
			|| ElemType.PinCategory == UEdGraphSchema_K2::PC_Text
			|| ElemType.PinCategory == UEdGraphSchema_K2::PC_Object
			|| ElemType.PinCategory == UEdGraphSchema_K2::PC_SoftObject
			|| OutText.FindChar(TEXT(' '), CharIndex) || OutText.FindChar(TEXT(','), CharIndex) || OutText.FindChar(TEXT('('), CharIndex)
			|| OutText.FindChar(TEXT(')'), CharIndex) || OutText.FindChar(TEXT('='), CharIndex) || OutText.FindChar(TEXT('"'), CharIndex);
		if (bNeedsQuotes)
		{
			OutText = QuoteForDelimitedImport(OutText);
		}
		return true;
	}

	// Default text for array/set/map variables. Containers import through
	// FArrayProperty/FSetProperty/FMapProperty::ImportText: (e1,e2,...) for
	// arrays/sets, ((k1,v1),(k2,v2),...) for maps. A plain string passes
	// through as ready-made engine import text.
	static bool ContainerDefaultFromJsonValue(const TSharedPtr<FJsonValue>& DefaultJson, const FEdGraphPinType& PinType, FString& OutDefault, FString& OutReason, TArray<FString>& OutWarnings)
	{
		if (DefaultJson->Type == EJson::String)
		{
			OutDefault = DefaultJson->AsString();
			return true;
		}

		FEdGraphPinType ElemType = PinType;
		ElemType.ContainerType = EPinContainerType::None;

		if (PinType.ContainerType == EPinContainerType::Map)
		{
			if (DefaultJson->Type != EJson::Object)
			{
				OutReason = TEXT("a map default must be a JSON object of key -> value pairs (or an engine import-text string)");
				return false;
			}
			// Compiler semantics (ConvertPropertyToPinType / CreatePropertyOnScope):
			// the base pin type is the map's KEY and PinValueType the VALUE
			// terminal. ElemType above is therefore the key type.
			const FEdGraphPinType& KeyType = ElemType;
			FEdGraphPinType ValueType;
			ValueType.PinCategory = PinType.PinValueType.TerminalCategory;
			ValueType.PinSubCategory = PinType.PinValueType.TerminalSubCategory;
			ValueType.PinSubCategoryObject = PinType.PinValueType.TerminalSubCategoryObject;
			const bool bNumericKey = KeyType.PinCategory == UEdGraphSchema_K2::PC_Int
				|| KeyType.PinCategory == UEdGraphSchema_K2::PC_Int64
				|| KeyType.PinCategory == UEdGraphSchema_K2::PC_Byte;

			FString Text = TEXT("(");
			bool bFirst = true;
			for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : DefaultJson->AsObject()->Values)
			{
				if (bNumericKey && !Pair.Key.IsNumeric())
				{
					OutReason = FString::Printf(TEXT("map key '%s' is not numeric but the key type is an integer type"), *Pair.Key);
					return false;
				}
				FString KeyText;
				if (!ContainerElementTextFromJsonValue(MakeShared<FJsonValueString>(Pair.Key), KeyType, KeyText, OutReason, OutWarnings))
				{
					return false;
				}
				FString ValueText;
				if (!ContainerElementTextFromJsonValue(Pair.Value, ValueType, ValueText, OutReason, OutWarnings))
				{
					return false;
				}
				// Entry form the engine's FMapProperty::ImportText parses: each
				// entry is "(key,value)" with key and value imported by their own
				// properties (PropertyMap.cpp) - no "Key=" prefix.
				Text += FString::Printf(TEXT("%s(%s,%s)"), bFirst ? TEXT("") : TEXT(","), *KeyText, *ValueText);
				bFirst = false;
			}
			OutDefault = Text + TEXT(")");
			return true;
		}

		// Array / set.
		if (DefaultJson->Type != EJson::Array)
		{
			OutReason = TEXT("an array or set default must be a JSON array of element values (or an engine import-text string)");
			return false;
		}
		FString Text = TEXT("(");
		bool bFirst = true;
		for (const TSharedPtr<FJsonValue>& Element : DefaultJson->AsArray())
		{
			FString ElementText;
			if (!ContainerElementTextFromJsonValue(Element, ElemType, ElementText, OutReason, OutWarnings))
			{
				return false;
			}
			Text += FString::Printf(TEXT("%s%s"), bFirst ? TEXT("") : TEXT(","), *ElementText);
			bFirst = false;
		}
		OutDefault = Text + TEXT(")");
		return true;
	}

	bool EnsureCompiledVariableDefault(UBlueprint* Blueprint, const FString& VarName, const FString& ExpectedDefault, FString& OutReason)
	{
		UBlueprintGeneratedClass* GeneratedClass = Cast<UBlueprintGeneratedClass>(Blueprint->GeneratedClass);
		if (GeneratedClass == nullptr)
		{
			OutReason = TEXT("the blueprint has no compiled class yet, so the default value could not be applied");
			return false;
		}
		UObject* CDO = GeneratedClass->GetDefaultObject(false);
		if (CDO == nullptr)
		{
			OutReason = TEXT("the compiled class has no default object yet, so the default value could not be applied");
			return false;
		}
		FProperty* Property = FindFProperty<FProperty>(GeneratedClass, FName(*VarName));
		if (Property == nullptr)
		{
			OutReason = FString::Printf(TEXT("compilation produced no property named '%s' on the generated class"), *VarName);
			return false;
		}

		// Same parser the compiler uses; parse the expected text into scratch
		// memory and compare it against what the CDO actually holds.
		const int32 PropertySize = Property->GetSize();
		uint8* ParsedValue = static_cast<uint8*>(FMemory::Malloc(PropertySize));
		uint8* ActualValue = static_cast<uint8*>(FMemory::Malloc(PropertySize));
		Property->InitializeValue(ParsedValue);
		Property->InitializeValue(ActualValue);
		const bool bParsed = FBlueprintEditorUtils::PropertyValueFromString_Direct(Property, ExpectedDefault, ParsedValue, CDO);
		if (!bParsed)
		{
			FMemory::Free(ParsedValue);
			FMemory::Free(ActualValue);
			OutReason = FString::Printf(TEXT("default '%s' could not be parsed for variable '%s' of type '%s'"), *ExpectedDefault, *VarName, *Property->GetClass()->GetName());
			return false;
		}
		Property->CopyCompleteValue(ActualValue, Property->ContainerPtrToValuePtr<const void>(CDO));
		const bool bAlreadyApplied = Property->Identical(ParsedValue, ActualValue);
		if (!bAlreadyApplied)
		{
			// The compiler left the property untouched - write the value into the
			// CDO ourselves so the default still takes effect.
			Property->CopyCompleteValue(Property->ContainerPtrToValuePtr<void>(CDO), ParsedValue);
			Blueprint->MarkPackageDirty();
		}
		FMemory::Free(ParsedValue);
		FMemory::Free(ActualValue);
		return true;
	}

	FBlueprintVariableLookup LookupBlueprintVariable(UBlueprint* Blueprint, const FString& VarName)
	{
		FBlueprintVariableLookup Lookup;
		if (Blueprint == nullptr || VarName.IsEmpty())
		{
			return Lookup;
		}
		const FName Name(*VarName);

		const int32 LocalIndex = FBlueprintEditorUtils::FindNewVariableIndex(Blueprint, Name);
		if (LocalIndex != INDEX_NONE)
		{
			Lookup.LocalDesc = &Blueprint->NewVariables[LocalIndex];
		}

		// Existence follows the engine's own visibility rule, so this is the
		// same answer AddMemberVariable acts on. SCS component variables and
		// timeline templates count as class members too and never show up as
		// a plain property lookup.
		TSet<FName> ClassVariables;
		FBlueprintEditorUtils::GetClassVariableList(Blueprint, ClassVariables);
		Lookup.bNameTaken = ClassVariables.Contains(Name);

		// FindFProperty walks the super chain, so one lookup answers both
		// "is it mine" and "whose is it".
		UClass* Class = Blueprint->GeneratedClass != nullptr
			? Blueprint->GeneratedClass
			: Blueprint->ParentClass;
		if (Class != nullptr)
		{
			Lookup.Property = FindFProperty<FProperty>(Class, Name);
			Lookup.OwnerClass = Lookup.Property != nullptr ? Lookup.Property->GetOwnerClass() : nullptr;
		}

		Lookup.bInherited = Lookup.LocalDesc == nullptr && Lookup.bNameTaken;
		return Lookup;
	}

	bool WritePropertyDefaultToClassCDO(UBlueprint* Blueprint, const FBlueprintVariableLookup& Lookup,
		const TSharedPtr<FJsonValue>& DefaultJson, FString& OutAppliedText, FString& OutReason,
		TArray<FString>& OutWarnings)
	{
		OutAppliedText.Reset();
		OutReason.Reset();

		if (Blueprint == nullptr)
		{
			OutReason = TEXT("no blueprint to write the default on");
			return false;
		}
		if (Lookup.Property == nullptr)
		{
			OutReason = TEXT("the variable has no compiled property to write the default on");
			return false;
		}

		UClass* Class = Blueprint->GeneratedClass;
		if (Class == nullptr)
		{
			// The CDO to write is always this blueprint's own class - writing
			// into the parent's would change the default for every sibling.
			// An asset with no generated class yet is simply uncompiled.
			FKismetEditorUtilities::CompileBlueprint(Blueprint);
			Class = Blueprint->GeneratedClass;
		}
		if (Class == nullptr)
		{
			OutReason = TEXT("the blueprint has neither a generated class nor a parent class");
			return false;
		}

		// Normalize through the property's own pin type, the same conversion
		// add_blueprint_variable does for a name it gets to declare.
		FEdGraphPinType PinType;
		const UEdGraphSchema_K2* K2Schema = GetDefault<UEdGraphSchema_K2>();
		if (K2Schema == nullptr || !K2Schema->ConvertPropertyToPinType(Lookup.Property, PinType))
		{
			OutReason = FString::Printf(TEXT("property '%s' of type '%s' cannot be expressed as a blueprint variable type"),
				*Lookup.Property->GetName(), *PropertyTypeToText(Lookup.Property));
			return false;
		}
		FString ValueText;
		if (!BlueprintDefaultFromJsonValue(DefaultJson, PinType, ValueText, OutReason, OutWarnings))
		{
			return false;
		}

		UObject* CDO = Class->GetDefaultObject(false);
		if (CDO == nullptr)
		{
			OutReason = TEXT("the class has no default object to write into");
			return false;
		}

		// ImportText is the Details panel's own write path.
		void* ValueData = Lookup.Property->ContainerPtrToValuePtr<void>(CDO);
		const TCHAR* TextPtr = *ValueText;
		if (Lookup.Property->ImportText(TextPtr, ValueData, PPF_PropertyWindow, CDO) == nullptr)
		{
			OutReason = FString::Printf(TEXT("value '%s' cannot be assigned to property '%s'"),
				*ValueText, *Lookup.Property->GetName());
			return false;
		}

		FPropertyChangedEvent ChangedEvent(Lookup.Property);
		CDO->PreEditChange(Lookup.Property);
		CDO->PostEditChangeProperty(ChangedEvent);
		Blueprint->MarkPackageDirty();

		OutAppliedText = MoveTemp(ValueText);
		return true;
	}

	void CollectInheritedSettingConflicts(const FBlueprintVariableLookup& Lookup,
		const TSharedPtr<FJsonObject>& Payload, TArray<FString>& OutConflicts)
	{
		OutConflicts.Reset();
		const FProperty* Property = Lookup.Property;
		if (Property == nullptr || !Payload.IsValid())
		{
			return;
		}
		const uint64 Flags = Property->PropertyFlags;

		// Boolean settings: honored only when the declaring class already put
		// the variable in the requested state. instanceEditable is the one
		// inverted mapping - it is the absence of CPF_DisableEditOnInstance.
		struct FBoolRule
		{
			const TCHAR* Key;
			uint64 Flag;
			bool bInverted;
		};
		static const FBoolRule BoolRules[] =
		{
			{ TEXT("instanceEditable"),      CPF_DisableEditOnInstance, true },
			{ TEXT("exposeOnSpawn"),        CPF_ExposeOnSpawn,       false },
			{ TEXT("exposeToCinematics"),   CPF_Interp,             false },
			{ TEXT("config"),               CPF_Config,             false },
			{ TEXT("transient"),            CPF_Transient,          false },
			{ TEXT("saveGame"),             CPF_SaveGame,           false },
			{ TEXT("advancedDisplay"),      CPF_AdvancedDisplay,    false },
			{ TEXT("deprecated"),           CPF_Deprecated,         false },
		};
		for (const FBoolRule& Rule : BoolRules)
		{
			bool bRequested = false;
			if (!Payload->TryGetBoolField(Rule.Key, bRequested))
			{
				continue;
			}
			const bool bFlagSet = (Flags & Rule.Flag) != 0;
			if (bFlagSet != (Rule.bInverted ? !bRequested : bRequested))
			{
				OutConflicts.Add(FString::Printf(TEXT("%s=%s"), Rule.Key, bRequested ? TEXT("true") : TEXT("false")));
			}
		}

		// 'replication' mirrors CPF_Net, mirroring OnChangeReplication.
		FString Replication;
		if (Payload->TryGetStringField(TEXT("replication"), Replication))
		{
			const bool bWantsNet = Replication.Equals(TEXT("replicated"), ESearchCase::IgnoreCase);
			const bool bWantsNone = Replication.Equals(TEXT("none"), ESearchCase::IgnoreCase);
			if ((bWantsNet || bWantsNone) && bWantsNet != ((Flags & CPF_Net) != 0))
			{
				OutConflicts.Add(FString::Printf(TEXT("replication=%s"), *Replication));
			}
		}

		// 'category' and the metadata-backed settings compare against the
		// compiled property's metadata: the compiler copies the variable's
		// Category onto the property (KismetCompiler.cpp).
		FString Category;
		if (Payload->TryGetStringField(TEXT("category"), Category)
			&& !Property->GetMetaData(TEXT("Category")).Equals(Category, ESearchCase::IgnoreCase))
		{
			OutConflicts.Add(FString::Printf(TEXT("category=%s"), *Category));
		}
		bool bPrivate = false;
		if (Payload->TryGetBoolField(TEXT("private"), bPrivate)
			&& Property->GetBoolMetaData(FBlueprintMetadata::MD_Private) != bPrivate)
		{
			OutConflicts.Add(FString::Printf(TEXT("private=%s"), bPrivate ? TEXT("true") : TEXT("false")));
		}
		FString DeprecatedMessage;
		if (Payload->TryGetStringField(TEXT("deprecatedMessage"), DeprecatedMessage)
			&& !DeprecatedMessage.IsEmpty()
			&& !Property->GetMetaData(FBlueprintMetadata::MD_DeprecationMessage).Equals(DeprecatedMessage, ESearchCase::IgnoreCase))
		{
			OutConflicts.Add(FString::Printf(TEXT("deprecatedMessage=%s"), *DeprecatedMessage));
		}

		// The replication condition lives on the variable declaration, not on
		// the compiled property, so there is nothing to compare against - any
		// request for it on an inherited variable is unsatisfiable.
		FString Condition;
		if (Payload->TryGetStringField(TEXT("replicationCondition"), Condition) && !Condition.IsEmpty())
		{
			OutConflicts.Add(FString::Printf(TEXT("replicationCondition=%s"), *Condition));
		}
	}

	bool BlueprintDefaultFromJsonValue(const TSharedPtr<FJsonValue>& DefaultJson, const FEdGraphPinType& PinType, FString& OutDefault, FString& OutReason, TArray<FString>& OutWarnings)
	{
		if (!DefaultJson.IsValid())
		{
			OutReason = TEXT("'default' must not be null");
			return false;
		}

		const UScriptStruct* Struct = (PinType.PinCategory == UEdGraphSchema_K2::PC_Struct)
			? Cast<UScriptStruct>(PinType.PinSubCategoryObject.Get()) : nullptr;

		if (PinType.ContainerType != EPinContainerType::None)
		{
			return ContainerDefaultFromJsonValue(DefaultJson, PinType, OutDefault, OutReason, OutWarnings);
		}

		if (Struct != nullptr)
		{
			// String defaults pass through untouched - they may already use an
			// engine text form like "1,2,3" or "tx,ty,tz|p,y,r|sx,sy,sz".
			if (DefaultJson->Type == EJson::String)
			{
				OutDefault = DefaultJson->AsString();
				return true;
			}

			double Components[3];
			if (Struct == TBaseStructure<FVector>::Get())
			{
				WarnUnknownMemberKeys(DefaultJson, { TEXT("X"), TEXT("x"), TEXT("Y"), TEXT("y"), TEXT("Z"), TEXT("z") }, TEXT("vector default"), OutWarnings);
				if (!JsonToComponents3(DefaultJson, { TEXT("X"), TEXT("x") }, { TEXT("Y"), TEXT("y") }, { TEXT("Z"), TEXT("z") }, Components))
				{
					OutReason = TEXT("a vector default must be {\"X\":x,\"Y\":y,\"Z\":z}, [x,y,z] or \"x,y,z\"");
					return false;
				}
				OutDefault = FString::Printf(TEXT("%f,%f,%f"), Components[0], Components[1], Components[2]);
				return true;
			}
			if (Struct == TBaseStructure<FLinearColor>::Get())
		{
			// FLinearColor has 4 components: R, G, B, A.
			// String defaults pass through untouched (may already be "(R=1,G=0,B=0,A=1)").
			if (DefaultJson->Type == EJson::String)
			{
				OutDefault = DefaultJson->AsString();
				return true;
			}
			WarnUnknownMemberKeys(DefaultJson, { TEXT("R"), TEXT("r"), TEXT("G"), TEXT("g"), TEXT("B"), TEXT("b"), TEXT("A"), TEXT("a") }, TEXT("linearcolor default"), OutWarnings);
			double R = 0, G = 0, B = 0, A = 1;
			if (DefaultJson->Type == EJson::Object)
			{
				const TSharedPtr<FJsonObject>& Obj = DefaultJson->AsObject();
				TSharedPtr<FJsonValue> RVal = Obj->TryGetField(TEXT("R"));
				if (!RVal.IsValid()) RVal = Obj->TryGetField(TEXT("r"));
				TSharedPtr<FJsonValue> GVal = Obj->TryGetField(TEXT("G"));
				if (!GVal.IsValid()) GVal = Obj->TryGetField(TEXT("g"));
				TSharedPtr<FJsonValue> BVal = Obj->TryGetField(TEXT("B"));
				if (!BVal.IsValid()) BVal = Obj->TryGetField(TEXT("b"));
				TSharedPtr<FJsonValue> AVal = Obj->TryGetField(TEXT("A"));
				if (!AVal.IsValid()) AVal = Obj->TryGetField(TEXT("a"));
				auto GetDouble = [](const TSharedPtr<FJsonValue>& V) -> double
				{
					if (!V.IsValid()) return 0.0;
					if (V->Type == EJson::Number) return V->AsNumber();
					if (V->Type == EJson::String && V->AsString().IsNumeric()) return FCString::Atod(*V->AsString());
					return 0.0;
				};
				R = GetDouble(RVal);
				G = GetDouble(GVal);
				B = GetDouble(BVal);
				A = AVal.IsValid() ? GetDouble(AVal) : 1.0;
			}
			else if (DefaultJson->Type == EJson::Array)
			{
				const TArray<TSharedPtr<FJsonValue>>& Arr = DefaultJson->AsArray();
				if (Arr.Num() < 3)
				{
					OutReason = TEXT("a linearcolor default must be {\"R\":r,\"G\":g,\"B\":b,\"A\":a}, [r,g,b,a] or \"(R=r,G=g,B=b,A=a)\"");
					return false;
				}
				auto GetDouble = [](const TSharedPtr<FJsonValue>& V) -> double
				{
					if (!V.IsValid()) return 0.0;
					if (V->Type == EJson::Number) return V->AsNumber();
					if (V->Type == EJson::String && V->AsString().IsNumeric()) return FCString::Atod(*V->AsString());
					return 0.0;
				};
				R = GetDouble(Arr[0]);
				G = GetDouble(Arr[1]);
				B = GetDouble(Arr[2]);
				A = (Arr.Num() >= 4) ? GetDouble(Arr[3]) : 1.0;
			}
			else
			{
				OutReason = TEXT("a linearcolor default must be {\"R\":r,\"G\":g,\"B\":b,\"A\":a}, [r,g,b,a] or \"(R=r,G=g,B=b,A=a)\"");
				return false;
			}
			OutDefault = FString::Printf(TEXT("(R=%g,G=%g,B=%g,A=%g)"), R, G, B, A);
			return true;
		}
		if (Struct == TBaseStructure<FRotator>::Get())
		{
			// ParseRotator maps the three comma components onto Pitch,Yaw,Roll.
			WarnUnknownMemberKeys(DefaultJson, { TEXT("Pitch"), TEXT("pitch"), TEXT("Yaw"), TEXT("yaw"), TEXT("Roll"), TEXT("roll") }, TEXT("rotator default"), OutWarnings);
			if (!JsonToComponents3(DefaultJson, { TEXT("Pitch"), TEXT("pitch") }, { TEXT("Yaw"), TEXT("yaw") }, { TEXT("Roll"), TEXT("roll") }, Components))
			{
				OutReason = TEXT("a rotator default must be {\"Pitch\":p,\"Yaw\":y,\"Roll\":r}, [p,y,r] or \"p,y,r\"");
				return false;
			}
			OutDefault = FString::Printf(TEXT("%f,%f,%f"), Components[0], Components[1], Components[2]);
			return true;
		}
		if (Struct == TBaseStructure<FTransform>::Get())
			{
				// FTransform::InitFromString wants "translation|rotation|scale".
				if (DefaultJson->Type != EJson::Object)
				{
					OutReason = TEXT("a transform default must be {\"Translation\":{X,Y,Z},\"Rotation\":{Pitch,Yaw,Roll},\"Scale\":{X,Y,Z}} or the \"tx,ty,tz|p,y,r|sx,sy,sz\" string form");
					return false;
				}
				const TSharedPtr<FJsonObject>& Object = DefaultJson->AsObject();
				WarnUnknownMemberKeys(DefaultJson, { TEXT("Translation"), TEXT("translation"), TEXT("Rotation"), TEXT("rotation"), TEXT("Scale"), TEXT("scale") }, TEXT("transform default"), OutWarnings);
				FVector Translation = FVector::ZeroVector;
				FRotator Rotation = FRotator::ZeroRotator;
				FVector Scale = FVector::OneVector;
				// FindRef returns a value copy (invalid share ptr when absent).
				TSharedPtr<FJsonValue> SubValue = Object->Values.FindRef(TEXT("Translation"));
				if (!SubValue.IsValid())
				{
					SubValue = Object->Values.FindRef(TEXT("translation"));
				}
				if (SubValue.IsValid())
				{
					WarnUnknownMemberKeys(SubValue, { TEXT("X"), TEXT("x"), TEXT("Y"), TEXT("y"), TEXT("Z"), TEXT("z") }, TEXT("transform Translation"), OutWarnings);
					if (!JsonToComponents3(SubValue, { TEXT("X"), TEXT("x") }, { TEXT("Y"), TEXT("y") }, { TEXT("Z"), TEXT("z") }, Components))
					{
						OutReason = TEXT("transform 'Translation' must be {X,Y,Z}, [x,y,z] or \"x,y,z\"");
						return false;
					}
					Translation.Set(Components[0], Components[1], Components[2]);
				}
				SubValue = Object->Values.FindRef(TEXT("Rotation"));
				if (!SubValue.IsValid())
				{
					SubValue = Object->Values.FindRef(TEXT("rotation"));
				}
				if (SubValue.IsValid())
				{
					WarnUnknownMemberKeys(SubValue, { TEXT("Pitch"), TEXT("pitch"), TEXT("Yaw"), TEXT("yaw"), TEXT("Roll"), TEXT("roll") }, TEXT("transform Rotation"), OutWarnings);
					if (!JsonToComponents3(SubValue, { TEXT("Pitch"), TEXT("pitch") }, { TEXT("Yaw"), TEXT("yaw") }, { TEXT("Roll"), TEXT("roll") }, Components))
					{
						OutReason = TEXT("transform 'Rotation' must be {Pitch,Yaw,Roll}, [p,y,r] or \"p,y,r\"");
						return false;
					}
					Rotation = FRotator(Components[0], Components[1], Components[2]);
				}
				SubValue = Object->Values.FindRef(TEXT("Scale"));
				if (!SubValue.IsValid())
				{
					SubValue = Object->Values.FindRef(TEXT("scale"));
				}
				if (SubValue.IsValid())
				{
					WarnUnknownMemberKeys(SubValue, { TEXT("X"), TEXT("x"), TEXT("Y"), TEXT("y"), TEXT("Z"), TEXT("z") }, TEXT("transform Scale"), OutWarnings);
					if (!JsonToComponents3(SubValue, { TEXT("X"), TEXT("x") }, { TEXT("Y"), TEXT("y") }, { TEXT("Z"), TEXT("z") }, Components))
					{
						OutReason = TEXT("transform 'Scale' must be {X,Y,Z}, [x,y,z] or \"x,y,z\"");
						return false;
					}
					Scale.Set(Components[0], Components[1], Components[2]);
				}
				OutDefault = FTransform(Rotation, Translation, Scale).ToString();
				return true;
			}

			// Generic struct: build the property text form (Prop=Value,...),
			// which is what UScriptStruct::ImportText parses.
			if (DefaultJson->Type != EJson::Object)
			{
				OutReason = FString::Printf(TEXT("a '%s' struct default must be an object with one numeric field per struct member, or an engine property text string"), *Struct->GetName());
				return false;
			}
			FString Text = TEXT("(");
			bool bFirst = true;
			for (const TPair<FString, TSharedPtr<FJsonValue>>& Pair : DefaultJson->AsObject()->Values)
			{
				// Skip members the struct does not have (case-sensitive, like the
				// engine's ImportText matching) - warned, not silently dropped.
				if (Struct->FindPropertyByName(FName(*Pair.Key)) == nullptr)
				{
					TArray<FString> ValidNames;
					for (TFieldIterator<FProperty> MemberIt(Struct); MemberIt; ++MemberIt)
					{
						ValidNames.Add(MemberIt->GetName());
					}
					OutWarnings.Add(FString::Printf(TEXT("'%s' struct default: unknown member '%s' ignored (valid: %s)"),
						*Struct->GetName(), *Pair.Key, *JoinStrings(ValidNames, TEXT(", "))));
					continue;
				}
				FString Member;
				switch (Pair.Value.IsValid() ? Pair.Value->Type : EJson::None)
				{
				case EJson::Number:
					Member = FString::Printf(TEXT("%g"), Pair.Value->AsNumber());
					break;
				case EJson::Boolean:
					Member = Pair.Value->AsBool() ? TEXT("true") : TEXT("false");
					break;
				case EJson::String:
					Member = Pair.Value->AsString();
					break;
				default:
					OutReason = FString::Printf(TEXT("struct member '%s' must be a scalar value"), *Pair.Key);
					return false;
				}
				Text += FString::Printf(TEXT("%s%s=%s"), bFirst ? TEXT("") : TEXT(","), *Pair.Key, *Member);
				bFirst = false;
			}
			OutDefault = Text + TEXT(")");
			return true;
		}

		// Non-struct, non-container variables take scalar defaults only.
		return ScalarDefaultFromJsonValue(DefaultJson, PinType, OutDefault, OutReason);
	}

	// Surfaces non-fatal warnings (unknown struct members) in the response and
	// the editor log so hallucinated member names get corrected instead of
	// silently dropped.
	void AppendWarningsToResult(const TSharedPtr<FJsonObject>& Result, const TArray<FString>& Warnings)
	{
		if (Warnings.Num() == 0)
		{
			return;
		}
		TArray<TSharedPtr<FJsonValue>> WarningValues;
		WarningValues.Reserve(Warnings.Num());
		for (const FString& Warning : Warnings)
		{
			UE_LOG(LogForgeMindBridge, Warning, TEXT("%s"), *Warning);
			WarningValues.Add(MakeShared<FJsonValueString>(Warning));
		}
		Result->SetArrayField(TEXT("warnings"), WarningValues);
	}

	// Optional key readers for update_blueprint_variable: presence of the key
	// decides whether the setting changes; a wrong-typed value is rejected.
	bool TryGetOptionalBool(const TSharedPtr<FJsonObject>& Payload, const TCHAR* Key, bool& OutValue, bool& bPresent, FString& OutReason)
	{
		bPresent = Payload->HasField(Key);
		if (bPresent && !Payload->TryGetBoolField(Key, OutValue))
		{
			OutReason = FString::Printf(TEXT("'%s' must be a boolean"), Key);
			return false;
		}
		return true;
	}

	bool TryGetOptionalString(const TSharedPtr<FJsonObject>& Payload, const TCHAR* Key, FString& OutValue, bool& bPresent, FString& OutReason)
	{
		bPresent = Payload->HasField(Key);
		if (bPresent && !Payload->TryGetStringField(Key, OutValue))
		{
			OutReason = FString::Printf(TEXT("'%s' must be a string"), Key);
			return false;
		}
		return true;
	}

	// Replication condition text -> enum. Accepts the engine identifiers with or
	// without the COND_ prefix (case-insensitive); hidden conditions (Dynamic,
	// Never, NetGroup) and COND_Max are refused.
	bool ParseReplicationCondition(const FString& InText, ELifetimeCondition& OutCondition, FString& OutReason)
	{
		FString Name = InText.TrimStartAndEnd().ToLower();
		if (Name.StartsWith(TEXT("cond_")))
		{
			Name = Name.RightChop(5);
		}

		static const struct { const TCHAR* Name; ELifetimeCondition Condition; } Conditions[] =
		{
			{ TEXT("none"), COND_None },
			{ TEXT("always"), COND_None },
			{ TEXT("initialonly"), COND_InitialOnly },
			{ TEXT("owneronly"), COND_OwnerOnly },
			{ TEXT("skipowner"), COND_SkipOwner },
			{ TEXT("simulatedonly"), COND_SimulatedOnly },
			{ TEXT("autonomousonly"), COND_AutonomousOnly },
			{ TEXT("simulatedorphysics"), COND_SimulatedOrPhysics },
			{ TEXT("initialorowner"), COND_InitialOrOwner },
			{ TEXT("custom"), COND_Custom },
			{ TEXT("replayorowner"), COND_ReplayOrOwner },
			{ TEXT("replayonly"), COND_ReplayOnly },
			{ TEXT("simulatedonlynoreplay"), COND_SimulatedOnlyNoReplay },
			{ TEXT("simulatedorphysicsnoreplay"), COND_SimulatedOrPhysicsNoReplay },
			{ TEXT("skipreplay"), COND_SkipReplay },
		};
		for (const auto& Entry : Conditions)
		{
			if (Name == Entry.Name)
			{
				OutCondition = Entry.Condition;
				return true;
			}
		}
		OutReason = FString::Printf(TEXT("unknown replicationCondition '%s' (expected e.g. none, initialOnly, ownerOnly, skipOwner, simulatedOnly, autonomousOnly, replayOnly)"), *InText);
		return false;
	}

	// Inverse of ParseReplicationCondition: enum -> the same camelCase names
	// update_blueprint_variable accepts, so reported values round-trip.
	const TCHAR* ReplicationConditionToText(const ELifetimeCondition Condition)
	{
		switch (Condition)
		{
		case COND_InitialOnly: return TEXT("initialOnly");
		case COND_OwnerOnly: return TEXT("ownerOnly");
		case COND_SkipOwner: return TEXT("skipOwner");
		case COND_SimulatedOnly: return TEXT("simulatedOnly");
		case COND_AutonomousOnly: return TEXT("autonomousOnly");
		case COND_SimulatedOrPhysics: return TEXT("simulatedOrPhysics");
		case COND_InitialOrOwner: return TEXT("initialOrOwner");
		case COND_Custom: return TEXT("custom");
		case COND_ReplayOrOwner: return TEXT("replayOrOwner");
		case COND_ReplayOnly: return TEXT("replayOnly");
		case COND_SimulatedOnlyNoReplay: return TEXT("simulatedOnlyNoReplay");
		case COND_SimulatedOrPhysicsNoReplay: return TEXT("simulatedOrPhysicsNoReplay");
		case COND_SkipReplay: return TEXT("skipReplay");
		case COND_None:
		default: return TEXT("none");
		}
	}

	// Sets or clears a single bit when the corresponding payload key was
	// present; returns whether the key was applied.
	bool ApplyBoolFlag(uint64& PropertyFlags, const uint64 Flag, const bool bEnable, const bool bPresent)
	{
		if (!bPresent)
		{
			return false;
		}
		if (bEnable)
		{
			PropertyFlags |= Flag;
		}
		else
		{
			PropertyFlags &= ~Flag;
		}
		return true;
	}

	// Renders an FEdGraphPinType with the same vocabulary add_blueprint_variable
	// accepts ('float', 'double', 'vector', ... or a class name for object
	// references), so a reported type can be fed straight back into add.
	FString PinTypeToText(const FEdGraphPinType& PinType)
	{
		FString Base;
		if (PinType.PinCategory == UEdGraphSchema_K2::PC_Boolean)
		{
			Base = TEXT("bool");
		}
		else if (PinType.PinCategory == UEdGraphSchema_K2::PC_Byte)
		{
			const UEnum* Enum = Cast<UEnum>(PinType.PinSubCategoryObject.Get());
			Base = (Enum != nullptr) ? FString::Printf(TEXT("byte<%s>"), *Enum->GetName()) : TEXT("byte");
		}
		else if (PinType.PinCategory == UEdGraphSchema_K2::PC_Int)
		{
			Base = TEXT("int");
		}
		else if (PinType.PinCategory == UEdGraphSchema_K2::PC_Int64)
		{
			Base = TEXT("int64");
		}
		else if (PinType.PinCategory == UEdGraphSchema_K2::PC_Real)
		{
			// PC_Real carries the precision in the subcategory (see the
			// CreatePrimitiveProperty note in HandleAddBlueprintVariable).
			Base = (PinType.PinSubCategory == UEdGraphSchema_K2::PC_Float) ? TEXT("float") : TEXT("double");
		}
		else if (PinType.PinCategory == UEdGraphSchema_K2::PC_Name)
		{
			Base = TEXT("name");
		}
		else if (PinType.PinCategory == UEdGraphSchema_K2::PC_String)
		{
			Base = TEXT("string");
		}
		else if (PinType.PinCategory == UEdGraphSchema_K2::PC_Text)
		{
			Base = TEXT("text");
		}
		else if (PinType.PinCategory == UEdGraphSchema_K2::PC_Struct)
		{
			const UScriptStruct* Struct = Cast<UScriptStruct>(PinType.PinSubCategoryObject.Get());
		if (Struct == TBaseStructure<FVector>::Get()) { Base = TEXT("vector"); }
		else if (Struct == TBaseStructure<FRotator>::Get()) { Base = TEXT("rotator"); }
		else if (Struct == TBaseStructure<FTransform>::Get()) { Base = TEXT("transform"); }
		else if (Struct == TBaseStructure<FLinearColor>::Get()) { Base = TEXT("linearcolor"); }
		else { Base = FString::Printf(TEXT("struct<%s>"), (Struct != nullptr) ? *Struct->GetName() : TEXT("?")); }
		}
		else if (PinType.PinCategory == UEdGraphSchema_K2::PC_Object)
		{
			const UClass* ObjectClass = Cast<UClass>(PinType.PinSubCategoryObject.Get());
			Base = (ObjectClass != nullptr) ? ObjectClass->GetName() : TEXT("object");
		}
		else
		{
			Base = PinType.PinCategory.ToString();
		}

		switch (PinType.ContainerType)
		{
		case EPinContainerType::Array: return Base + TEXT("[]");
		case EPinContainerType::Set: return FString::Printf(TEXT("set<%s>"), *Base);
		case EPinContainerType::Map:
		{
			// Compiler semantics (EdGraphSchema_K2::ConvertPropertyToPinType /
			// FKismetCompilerUtilities::CreatePropertyOnScope): the base pin
			// type is the map's KEY and PinValueType the VALUE terminal.
			FEdGraphPinType ValueType;
			ValueType.PinCategory = PinType.PinValueType.TerminalCategory;
			ValueType.PinSubCategory = PinType.PinValueType.TerminalSubCategory;
			ValueType.PinSubCategoryObject = PinType.PinValueType.TerminalSubCategoryObject;
			return FString::Printf(TEXT("map<%s,%s>"), *Base, *PinTypeToText(ValueType));
		}
		case EPinContainerType::None:
		default: return Base;
		}
	}

	// Type text for an already-compiled or native property (inherited variables),
	// using the same vocabulary as PinTypeToText.
	FString PropertyTypeToText(const FProperty* Property)
	{
		// Unwrap containers first so the suffixes match PinTypeToText.
		const FProperty* Value = Property;
		int32 Container = 0; // 0 = none, 1 = array, 2 = set, 3 = map
		FString MapKeyText;
		if (const FArrayProperty* ArrayProp = CastField<FArrayProperty>(Property)) { Value = ArrayProp->Inner; Container = 1; }
		else if (const FSetProperty* SetProp = CastField<FSetProperty>(Property)) { Value = SetProp->ElementProp; Container = 2; }
		else if (const FMapProperty* MapProp = CastField<FMapProperty>(Property)) { Value = MapProp->ValueProp; MapKeyText = PropertyTypeToText(MapProp->KeyProp); Container = 3; }

		FString Base;
		if (CastField<FBoolProperty>(Value) != nullptr)
		{
			Base = TEXT("bool");
		}
		else if (const FByteProperty* ByteProp = CastField<FByteProperty>(Value))
		{
			Base = (ByteProp->Enum != nullptr) ? FString::Printf(TEXT("byte<%s>"), *ByteProp->Enum->GetName()) : TEXT("byte");
		}
		else if (CastField<FIntProperty>(Value) != nullptr)
		{
			Base = TEXT("int");
		}
		else if (CastField<FInt64Property>(Value) != nullptr)
		{
			Base = TEXT("int64");
		}
		else if (CastField<FFloatProperty>(Value) != nullptr)
		{
			Base = TEXT("float");
		}
		else if (CastField<FDoubleProperty>(Value) != nullptr)
		{
			Base = TEXT("double");
		}
		else if (CastField<FNameProperty>(Value) != nullptr)
		{
			Base = TEXT("name");
		}
		else if (CastField<FStrProperty>(Value) != nullptr)
		{
			Base = TEXT("string");
		}
		else if (CastField<FTextProperty>(Value) != nullptr)
		{
			Base = TEXT("text");
		}
		else if (const FEnumProperty* EnumProp = CastField<FEnumProperty>(Value))
		{
			Base = FString::Printf(TEXT("byte<%s>"), (EnumProp->GetEnum() != nullptr) ? *EnumProp->GetEnum()->GetName() : TEXT("?"));
		}
		else if (const FStructProperty* StructProp = CastField<FStructProperty>(Value))
		{
			const UScriptStruct* Struct = StructProp->Struct;
			if (Struct == TBaseStructure<FVector>::Get()) { Base = TEXT("vector"); }
			else if (Struct == TBaseStructure<FRotator>::Get()) { Base = TEXT("rotator"); }
			else if (Struct == TBaseStructure<FTransform>::Get()) { Base = TEXT("transform"); }
			else if (Struct == TBaseStructure<FLinearColor>::Get()) { Base = TEXT("linearcolor"); }
			else { Base = FString::Printf(TEXT("struct<%s>"), (Struct != nullptr) ? *Struct->GetName() : TEXT("?")); }
		}
		else if (const FSoftObjectProperty* SoftProp = CastField<FSoftObjectProperty>(Value))
		{
			Base = FString::Printf(TEXT("soft<%s>"), (SoftProp->PropertyClass != nullptr) ? *SoftProp->PropertyClass->GetName() : TEXT("object"));
		}
		else if (const FObjectPropertyBase* ObjProp = CastField<FObjectPropertyBase>(Value))
		{
			Base = (ObjProp->PropertyClass != nullptr) ? ObjProp->PropertyClass->GetName() : TEXT("object");
		}
		else
		{
			Base = Value->GetClass()->GetName();
		}

		switch (Container)
		{
		case 1: return Base + TEXT("[]");
		case 2: return FString::Printf(TEXT("set<%s>"), *Base);
		case 3: return FString::Printf(TEXT("map<%s,%s>"), *MapKeyText, *Base);
		default: return Base;
		}
	}

	// -- DSL user enum aliases (session-level) --
	// See RegisterUserTypeEnumAlias comments in ForgeMindBlueprintUtils.h.
	static TMap<FString, FString>& GetUserTypeEnumAliases()
	{
		static TMap<FString, FString> Aliases;
		return Aliases;
	}

	void RegisterUserTypeEnumAlias(const FString& DeclaredName, const FString& AssetPath)
	{
		if (DeclaredName.IsEmpty() || AssetPath.IsEmpty())
		{
			return;
		}
		GetUserTypeEnumAliases().Add(DeclaredName, AssetPath);
	}

	UEnum* ResolveUserTypeEnumAlias(const FString& DeclaredName)
	{
		const FString* AliasPath = GetUserTypeEnumAliases().Find(DeclaredName);
		if (AliasPath == nullptr)
		{
			return nullptr;
		}
		return LoadObject<UEnum>(nullptr, **AliasPath);
	}

	// One base type of the vocabulary PinTypeToText emits. Keywords cover the
	// common primitives and math structs; anything else is treated as a UClass
	// name and becomes an object reference. Containers never appear here -
	// PinTypeTextToPinType wraps this for the array/set/map forms.
	static bool BaseTypeTextToPinType(const FString& InText, FEdGraphPinType& OutPinType, FString& OutReason)
	{
		const FString TypeLower = InText.ToLower();
		if (TypeLower == TEXT("bool") || TypeLower == TEXT("boolean"))
		{
			OutPinType.PinCategory = UEdGraphSchema_K2::PC_Boolean;
			return true;
		}
		if (TypeLower == TEXT("byte") || TypeLower == TEXT("uint8"))
		{
			OutPinType.PinCategory = UEdGraphSchema_K2::PC_Byte;
			return true;
		}
		if (TypeLower == TEXT("int") || TypeLower == TEXT("int32") || TypeLower == TEXT("integer"))
		{
			OutPinType.PinCategory = UEdGraphSchema_K2::PC_Int;
			return true;
		}
		if (TypeLower == TEXT("int64") || TypeLower == TEXT("long"))
		{
			OutPinType.PinCategory = UEdGraphSchema_K2::PC_Int64;
			return true;
		}
		if (TypeLower == TEXT("float") || TypeLower == TEXT("real"))
		{
			// UE 5.6's compiler (FKismetCompilerUtilities::CreatePrimitiveProperty)
			// has no PinCategory == PC_Float branch: floating-point variables must
			// be declared as PC_Real with a PC_Float/PC_Double subcategory, anything
			// else silently falls back to an FIntProperty (which then rejects '1.5').
			OutPinType.PinCategory = UEdGraphSchema_K2::PC_Real;
			OutPinType.PinSubCategory = UEdGraphSchema_K2::PC_Float;
			return true;
		}
		if (TypeLower == TEXT("double"))
		{
			OutPinType.PinCategory = UEdGraphSchema_K2::PC_Real;
			OutPinType.PinSubCategory = UEdGraphSchema_K2::PC_Double;
			return true;
		}
		if (TypeLower == TEXT("name"))
		{
			OutPinType.PinCategory = UEdGraphSchema_K2::PC_Name;
			return true;
		}
		if (TypeLower == TEXT("string"))
		{
			OutPinType.PinCategory = UEdGraphSchema_K2::PC_String;
			return true;
		}
		if (TypeLower == TEXT("text"))
		{
			OutPinType.PinCategory = UEdGraphSchema_K2::PC_Text;
			return true;
		}
		if (TypeLower == TEXT("vector") || TypeLower == TEXT("rotator") || TypeLower == TEXT("transform"))
		{
			UScriptStruct* Struct = (TypeLower == TEXT("vector")) ? TBaseStructure<FVector>::Get()
				: (TypeLower == TEXT("rotator")) ? TBaseStructure<FRotator>::Get()
				: TBaseStructure<FTransform>::Get();
			OutPinType.PinCategory = UEdGraphSchema_K2::PC_Struct;
			OutPinType.PinSubCategoryObject = Struct;
			return true;
		}
		if (TypeLower == TEXT("linearcolor") || TypeLower == TEXT("color"))
		{
			OutPinType.PinCategory = UEdGraphSchema_K2::PC_Struct;
			OutPinType.PinSubCategoryObject = TBaseStructure<FLinearColor>::Get();
			return true;
		}

	// User-defined enums: resolve by asset path (e.g. /Game/Blueprints/EItemType)
	// or short name (e.g. EItemType). This covers UUserDefinedEnum assets that
	// have not been loaded as a class member.
	if (InText.StartsWith(TEXT("/")))
	{
		UEnum* EnumAsset = LoadObject<UEnum>(nullptr, *InText);
		if (EnumAsset != nullptr)
		{
			OutPinType.PinCategory = UEdGraphSchema_K2::PC_Byte;
			OutPinType.PinSubCategoryObject = EnumAsset;
			return true;
		}
		UScriptStruct* StructAsset = LoadObject<UScriptStruct>(nullptr, *InText);
		if (StructAsset != nullptr)
		{
			OutPinType.PinCategory = UEdGraphSchema_K2::PC_Struct;
			OutPinType.PinSubCategoryObject = StructAsset;
			return true;
		}
		UClass* ClassAsset = LoadObject<UClass>(nullptr, *InText);
		if (ClassAsset != nullptr)
		{
			OutPinType.PinCategory = UEdGraphSchema_K2::PC_Object;
			OutPinType.PinSubCategoryObject = ClassAsset;
			return true;
		}
	}

	// User-defined enums by short name (e.g. EItemType)
	// Try FindFirstObject first (searches loaded objects), then fall back
	// to a cached AssetRegistry name->path map built on first use.
	UEnum* EnumObj = ForgeMindFindFirstObject<UEnum>(*InText);
	if (EnumObj == nullptr)
	{
		// Static cache: built once per session, maps short asset names to
		// object paths so we avoid calling GetAssetsByClass on every pin
		// type resolution (which would scan thousands of assets each time).
		struct FEnumNameCache
		{
			TMap<FString, FString> NameToPath;
			bool bBuilt = false;
		};
		static FEnumNameCache Cache;
		if (!Cache.bBuilt)
		{
			FAssetRegistryModule& AssetRegModule = FModuleManager::LoadModuleChecked<FAssetRegistryModule>(TEXT("AssetRegistry"));
			IAssetRegistry& AssetRegistry = AssetRegModule.Get();
			TArray<FAssetData> EnumAssets;
			AssetRegistry.GetAssetsByClass(UEnum::StaticClass()->GetFName(), EnumAssets, /*bSearchSubClasses=*/true);
			for (const FAssetData& AssetData : EnumAssets)
			{
				Cache.NameToPath.Add(AssetData.AssetName.ToString(), AssetData.ObjectPath.ToString());
			}
			Cache.bBuilt = true;
		}
		const FString* FoundPath = Cache.NameToPath.Find(InText);
		if (FoundPath != nullptr)
		{
			EnumObj = LoadObject<UEnum>(nullptr, **FoundPath);
		}
	}
	if (EnumObj == nullptr)
	{
// DSL user enum alias fallback: Blueprint DSL references enum types using DSL-side
	// C# declaration name (e.g., EState), which may differ from the enum asset name (EState_B).
		// After CreateEnumAsset creates the enum in IR, it registers the "declaration name -> asset path" alias.
		const FString* AliasPath = GetUserTypeEnumAliases().Find(InText);
		if (AliasPath != nullptr)
		{
			EnumObj = LoadObject<UEnum>(nullptr, **AliasPath);
		}
	}
	if (EnumObj != nullptr)
	{
		OutPinType.PinCategory = UEdGraphSchema_K2::PC_Byte;
		OutPinType.PinSubCategoryObject = EnumObj;
		return true;
	}

	// User-defined structs by short name (e.g. FItemData)
	// Same cached fallback strategy as enums above.
	// Engine script struct real names have no F prefix (FHitResult -> HitResult, FTransform ->
	// Transform), aligned with UClass prefix tolerance rules below.
	UScriptStruct* StructObj = ForgeMindFindFirstObject<UScriptStruct>(*InText);
	if (StructObj == nullptr && InText.Len() > 1 && InText[0] == 'F')
	{
		StructObj = ForgeMindFindFirstObject<UScriptStruct>(*InText.RightChop(1));
	}
	if (StructObj == nullptr)
	{
		struct FStructNameCache
		{
			TMap<FString, FString> NameToPath;
			bool bBuilt = false;
		};
		static FStructNameCache Cache;
		if (!Cache.bBuilt)
		{
			FAssetRegistryModule& AssetRegModule2 = FModuleManager::LoadModuleChecked<FAssetRegistryModule>(TEXT("AssetRegistry"));
			IAssetRegistry& AssetRegistry2 = AssetRegModule2.Get();
			TArray<FAssetData> StructAssets;
			AssetRegistry2.GetAssetsByClass(UScriptStruct::StaticClass()->GetFName(), StructAssets, /*bSearchSubClasses=*/true);
			for (const FAssetData& AssetData : StructAssets)
			{
				Cache.NameToPath.Add(AssetData.AssetName.ToString(), AssetData.ObjectPath.ToString());
			}
			Cache.bBuilt = true;
		}
		const FString* FoundPath = Cache.NameToPath.Find(InText);
		if (FoundPath == nullptr && InText.Len() > 1 && InText[0] == 'F')
		{
			FoundPath = Cache.NameToPath.Find(InText.RightChop(1));
		}
		if (FoundPath != nullptr)
		{
			StructObj = LoadObject<UScriptStruct>(nullptr, **FoundPath);
		}
	}
	if (StructObj != nullptr)
	{
		OutPinType.PinCategory = UEdGraphSchema_K2::PC_Struct;
		OutPinType.PinSubCategoryObject = StructObj;
		return true;
	}

	// Same resolution rules as list_class_hierarchy: tolerate the C++
	// type prefix (UStaticMesh -> StaticMesh).
	UClass* ObjectClass = ForgeMindFindFirstObject<UClass>(*InText);
	if (ObjectClass == nullptr && InText.Len() > 1 && (InText[0] == 'U' || InText[0] == 'A' || InText[0] == 'F'))
	{
		ObjectClass = ForgeMindFindFirstObject<UClass>(*InText.RightChop(1));
	}
	// Blueprint-generated classes (e.g. BP_PickupItem_C, WBP_Inventory_C)
	// may not be loaded in memory. Use a cached AssetRegistry name->path
	// map to avoid scanning all blueprint assets on every call.
	if (ObjectClass == nullptr)
	{
		struct FBpNameCache
		{
			TMap<FString, FString> NameToPath; // key: "BP_Foo" and "BP_Foo_C" -> object path
			bool bBuilt = false;
		};
		static FBpNameCache BpCache;
		// BpCache has static storage duration: MSVC rejects capturing it in a
		// lambda (C3495), so the lambda accesses it directly (no capture).
		auto BuildBpCache = []()
		{
			FAssetRegistryModule& AssetRegModule3 = FModuleManager::LoadModuleChecked<FAssetRegistryModule>(TEXT("AssetRegistry"));
			IAssetRegistry& AssetRegistry3 = AssetRegModule3.Get();
			TArray<FAssetData> BpAssets;
			AssetRegistry3.GetAssetsByClass(UBlueprint::StaticClass()->GetFName(), BpAssets, /*bSearchSubClasses=*/true);
			for (const FAssetData& AssetData : BpAssets)
			{
				FString AssetNameStr = AssetData.AssetName.ToString();
				BpCache.NameToPath.Add(AssetNameStr, AssetData.ObjectPath.ToString());
				BpCache.NameToPath.Add(AssetNameStr + TEXT("_C"), AssetData.ObjectPath.ToString());
			}
			BpCache.bBuilt = true;
		};
		if (!BpCache.bBuilt)
		{
			BuildBpCache();
		}
		// Runtime-created blueprints (e.g. a test-built asset created after this
		// cache was first populated) are absent from the one-shot map - rebuild
		// once on a miss so late-created assets resolve instead of silently
		// dropping the whole function shell (create_function_graph outputs).
		const FString* FoundBpPath = BpCache.NameToPath.Find(InText);
		if (FoundBpPath == nullptr)
		{
			BpCache.NameToPath.Empty();
			BuildBpCache();
			FoundBpPath = BpCache.NameToPath.Find(InText);
		}
		if (FoundBpPath != nullptr)
		{
			UBlueprint* Bp = LoadObject<UBlueprint>(nullptr, **FoundBpPath);
			if (Bp != nullptr && Bp->GeneratedClass != nullptr)
			{
				ObjectClass = Bp->GeneratedClass;
			}
		}
	}
	if (ObjectClass == nullptr)
	{
		OutReason = FString::Printf(TEXT("'%s' is neither a known variable type keyword nor a loaded UClass/UEnum/UScriptStruct name"), *InText);
		return false;
	}
	OutPinType.PinCategory = UEdGraphSchema_K2::PC_Object;
	OutPinType.PinSubCategoryObject = ObjectClass;
	return true;
}

	bool PinTypeTextToPinType(const FString& TypeText, FEdGraphPinType& OutPinType, FString& OutReason)
	{
		OutPinType = FEdGraphPinType();
		OutPinType.ContainerType = EPinContainerType::None;

		FString Trimmed = TypeText.TrimStartAndEnd();
		if (Trimmed.IsEmpty())
		{
			OutReason = TEXT("type must not be empty");
			return false;
		}

		const FString Lower = Trimmed.ToLower();

		// "X[]" - the form PinTypeToText reports for array variables.
		if (Trimmed.EndsWith(TEXT("[]")))
		{
			OutPinType.ContainerType = EPinContainerType::Array;
			return BaseTypeTextToPinType(Trimmed.LeftChop(2), OutPinType, OutReason);
		}

		// "array<X>" / "tarray<X>", "set<X>" / "tset<X>", "map<K,V>" / "tmap<K,V>".
		// Accept the C++ template spellings too since callers see them in C++
		// headers. The inner text is taken verbatim (not lowercased) so UClass
		// names keep their case.
		struct FContainerForm { const TCHAR* Prefix; EPinContainerType Kind; };
		static const FContainerForm Forms[] = {
			{ TEXT("array<"), EPinContainerType::Array }, { TEXT("tarray<"), EPinContainerType::Array },
			{ TEXT("set<"), EPinContainerType::Set }, { TEXT("tset<"), EPinContainerType::Set },
			{ TEXT("map<"), EPinContainerType::Map }, { TEXT("tmap<"), EPinContainerType::Map }
		};
		for (const FContainerForm& Form : Forms)
		{
			const FString PrefixLower = FString(Form.Prefix);
			if (!Lower.StartsWith(PrefixLower) || !Lower.EndsWith(TEXT(">")))
			{
				continue;
			}
			if (Trimmed.Len() <= PrefixLower.Len() + 1)
			{
				OutReason = FString::Printf(TEXT("'%s' is missing its element type"), *Trimmed);
				return false;
			}
			const FString Inner = Trimmed.Mid(PrefixLower.Len(), Trimmed.Len() - PrefixLower.Len() - 1);

			if (Form.Kind != EPinContainerType::Map)
			{
				OutPinType.ContainerType = Form.Kind;
				return BaseTypeTextToPinType(Inner, OutPinType, OutReason);
			}

			// "K,V" - split at the first top-level comma. Nesting is rejected
			// beforehand: FEdGraphPinType models one container level only.
			int32 CommaIndex = INDEX_NONE;
			int32 Depth = 0;
			for (int32 i = 0; i < Inner.Len(); ++i)
			{
				if (Inner[i] == '<' || Inner[i] == '(')
				{
					++Depth;
				}
				else if (Inner[i] == '>' || Inner[i] == ')')
				{
					--Depth;
				}
				else if (Inner[i] == ',' && Depth == 0)
				{
					CommaIndex = i;
					break;
				}
			}
			if (CommaIndex == INDEX_NONE)
			{
				OutReason = FString::Printf(TEXT("a map type needs both a key and a value type: map<KeyType,ValueType>"));
				return false;
			}
			const FString KeyText = Inner.Left(CommaIndex).TrimStartAndEnd();
			const FString ValueText = Inner.Mid(CommaIndex + 1).TrimStartAndEnd();

			// Compiler semantics (EdGraphSchema_K2::ConvertPropertyToPinType /
			// FKismetCompilerUtilities::CreatePropertyOnScope): the base pin
			// type is the map's KEY and PinValueType the VALUE terminal.
			FString KeyReason;
			if (!BaseTypeTextToPinType(KeyText, OutPinType, KeyReason))
			{
				OutReason = FString::Printf(TEXT("map key: %s"), *KeyReason);
				return false;
			}
			// The compiler rejects key types without a value hash (FText has
			// none) - refuse upfront with a clear reason instead of a bare
			// compile error.
			if (OutPinType.PinCategory == UEdGraphSchema_K2::PC_Text)
			{
				OutReason = TEXT("text cannot be a map key (it has no hash); use a name or string key instead");
				return false;
			}

			FEdGraphPinType ValueType;
			FString ValueReason;
			if (!BaseTypeTextToPinType(ValueText, ValueType, ValueReason))
			{
				OutReason = FString::Printf(TEXT("map value: %s"), *ValueReason);
				return false;
			}
			OutPinType.ContainerType = EPinContainerType::Map;
			OutPinType.PinValueType.TerminalCategory = ValueType.PinCategory;
			OutPinType.PinValueType.TerminalSubCategory = ValueType.PinSubCategory;
			OutPinType.PinValueType.TerminalSubCategoryObject = ValueType.PinSubCategoryObject;
			return true;
		}

		return BaseTypeTextToPinType(Trimmed, OutPinType, OutReason);
	}

	// The settings block shared by every listed variable - it mirrors the
	// update_blueprint_variable key set, so reported values can be fed straight
	// back into update (own variables only, inherited ones are read-only).
	void AddVariableSettingsToJson(FJsonObject& Entry, const uint64 PropertyFlags, const ELifetimeCondition Condition,
		const bool bPrivate, const bool bExposeOnSpawn, const FString& DeprecatedMessage, const bool bHasDeprecatedMessage,
		const FName RepNotifyFunc)
	{
		Entry.SetBoolField(TEXT("instanceEditable"), (PropertyFlags & CPF_DisableEditOnInstance) == 0);
		Entry.SetBoolField(TEXT("private"), bPrivate);
		Entry.SetBoolField(TEXT("config"), (PropertyFlags & CPF_Config) != 0);
		Entry.SetBoolField(TEXT("transient"), (PropertyFlags & CPF_Transient) != 0);
		Entry.SetBoolField(TEXT("saveGame"), (PropertyFlags & CPF_SaveGame) != 0);
		Entry.SetBoolField(TEXT("advancedDisplay"), (PropertyFlags & CPF_AdvancedDisplay) != 0);
		Entry.SetBoolField(TEXT("deprecated"), (PropertyFlags & CPF_Deprecated) != 0);
		if (bHasDeprecatedMessage)
		{
			Entry.SetStringField(TEXT("deprecatedMessage"), DeprecatedMessage);
		}
		Entry.SetBoolField(TEXT("exposeOnSpawn"), bExposeOnSpawn);
		Entry.SetBoolField(TEXT("exposeToCinematics"), (PropertyFlags & CPF_Interp) != 0);

		if (PropertyFlags & CPF_RepNotify)
		{
			Entry.SetStringField(TEXT("replication"), TEXT("repNotify"));
		}
		else if (PropertyFlags & CPF_Net)
		{
			Entry.SetStringField(TEXT("replication"), TEXT("replicated"));
		}
		else
		{
			Entry.SetStringField(TEXT("replication"), TEXT("none"));
		}
		if (RepNotifyFunc != NAME_None)
		{
			Entry.SetStringField(TEXT("repNotifyFunc"), RepNotifyFunc.ToString());
		}
		Entry.SetStringField(TEXT("replicationCondition"), ReplicationConditionToText(Condition));
	}

	// Entry built from a variable description (the blueprint's own variables and
	// those of blueprint parents - the descriptions are what the editor's
	// variable details panel edits). The default however is read off the
	// generated class CDO: a full compile moves the staged default onto the
	// CDO and empties FBPVariableDescription::DefaultValue (KismetCompiler.cpp),
	// so the CDO is the source of truth once compiled; the staged text only
	// survives for variables that haven't seen a full compile yet.
	TSharedRef<FJsonObject> BlueprintVariableToJson(const FBPVariableDescription& Variable, const UClass* GeneratedClass)
	{
		TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
		Entry->SetStringField(TEXT("name"), Variable.VarName.ToString());
		Entry->SetStringField(TEXT("type"), PinTypeToText(Variable.VarType));

		FString DefaultText = Variable.DefaultValue;
		if (GeneratedClass != nullptr)
		{
			if (const FProperty* Property = FindFProperty<FProperty>(GeneratedClass, Variable.VarName))
			{
				FBlueprintEditorUtils::PropertyValueToString(Property, reinterpret_cast<const uint8*>(GeneratedClass->GetDefaultObject()), DefaultText);
			}
		}
		Entry->SetStringField(TEXT("defaultValue"), DefaultText);
		Entry->SetStringField(TEXT("category"), Variable.Category.ToString());

		FString DeprecatedMessage;
		const bool bHasDeprecatedMessage = Variable.HasMetaData(FBlueprintMetadata::MD_DeprecationMessage);
		if (bHasDeprecatedMessage)
		{
			DeprecatedMessage = Variable.GetMetaData(FBlueprintMetadata::MD_DeprecationMessage);
		}
		AddVariableSettingsToJson(*Entry, Variable.PropertyFlags, Variable.ReplicationCondition,
			Variable.HasMetaData(FBlueprintMetadata::MD_Private),
			Variable.HasMetaData(FBlueprintMetadata::MD_ExposeOnSpawn),
			DeprecatedMessage, bHasDeprecatedMessage, Variable.RepNotifyFunc);
		return Entry;
	}

	// Entry built from a compiled property (variables inherited from C++ parent
	// classes); the default is read off the owner's CDO in the same text form
	// PropertyValueFromString parses.
	TSharedRef<FJsonObject> NativePropertyToJson(const FProperty* Property, const UClass* OwnerClass)
	{
		TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
		Entry->SetStringField(TEXT("name"), Property->GetName());
		Entry->SetStringField(TEXT("type"), PropertyTypeToText(Property));

		FString DefaultText;
		if (OwnerClass != nullptr)
		{
			FBlueprintEditorUtils::PropertyValueToString(Property, reinterpret_cast<const uint8*>(OwnerClass->GetDefaultObject()), DefaultText);
		}
		Entry->SetStringField(TEXT("defaultValue"), DefaultText);

		if (Property->HasMetaData(TEXT("Category")))
		{
			Entry->SetStringField(TEXT("category"), Property->GetMetaData(TEXT("Category")));
		}

		FString DeprecatedMessage;
		const bool bHasDeprecatedMessage = Property->HasMetaData(FBlueprintMetadata::MD_DeprecationMessage);
		if (bHasDeprecatedMessage)
		{
			DeprecatedMessage = Property->GetMetaData(FBlueprintMetadata::MD_DeprecationMessage);
		}
		AddVariableSettingsToJson(*Entry, Property->PropertyFlags, Property->GetBlueprintReplicationCondition(),
			Property->HasMetaData(FBlueprintMetadata::MD_Private),
			Property->HasMetaData(FBlueprintMetadata::MD_ExposeOnSpawn) || Property->HasAnyPropertyFlags(CPF_ExposeOnSpawn),
			DeprecatedMessage, bHasDeprecatedMessage, Property->RepNotifyFunc);
		return Entry;
	}

	// ---- Blueprint graph helpers (moved from BlueprintGraph.cpp) ----

	UBlueprint* ResolveBlueprint(const FString& AssetPath)
	{
		FString PackagePath = AssetPath;
		int32 DotIndex = INDEX_NONE;
		if (PackagePath.FindChar(TEXT('.'), DotIndex))
		{
			PackagePath = PackagePath.Left(DotIndex);
		}
		const FString ObjectPath = FString::Printf(TEXT("%s.%s"), *PackagePath, *FPaths::GetCleanFilename(PackagePath));
		if (UBlueprint* BP = LoadObject<UBlueprint>(nullptr, *ObjectPath))
		{
			return BP;
		}
// Level blueprint: when normal UBlueprint asset resolution fails, resolve by map path -
	// The UBlueprint of a level blueprint is not a standalone .uasset, but the level itself
	// (PersistentLevel.LevelScriptActor's ClassGeneratedBy).
	// (e.g., /Game/Tests/MyLevel -> that level's level blueprint UBlueprint)
	// Prefer matching the current editor world (create_level loads the target level;
	// NewMap world name may differ from package name, LoadObject by object name would miss).
		UWorld* World = nullptr;
		if (GEditor != nullptr)
		{
			UWorld* CurWorld = GEditor->GetEditorWorldContext(false).World();
			if (CurWorld != nullptr && CurWorld->GetOutermost() != nullptr
				&& CurWorld->GetOutermost()->GetPathName() == PackagePath)
			{
				World = CurWorld;
			}
		}
		if (World == nullptr)
		{
			World = LoadObject<UWorld>(nullptr, *ObjectPath);
		}
// GetLevelScriptBlueprint(false) ensures LevelScriptActor exists and returns its
	// level blueprint (for newly created empty levels, LevelScriptActor may not be generated yet, direct pointer access returns null).
		if (World != nullptr && World->PersistentLevel != nullptr)
		{
			if (UBlueprint* LevelBP = World->PersistentLevel->GetLevelScriptBlueprint(false))
			{
				return LevelBP;
			}
		}
		return nullptr;
	}

	UObject* ResolveAsset(const FString& AssetPath)
	{
		// Strip any ".subname" suffix — the caller may pass either
		// /Game/Folder/MyAsset  or  /Game/Folder/MyAsset.MyAsset
		FString PackagePath = AssetPath;
		int32 DotIndex = INDEX_NONE;
		if (PackagePath.FindChar(TEXT('.'), DotIndex))
		{
			PackagePath = PackagePath.Left(DotIndex);
		}
		const FString ObjectPath = FString::Printf(TEXT("%s.%s"), *PackagePath, *FPaths::GetCleanFilename(PackagePath));
		return LoadObject<UObject>(nullptr, *ObjectPath);
	}

	/** Walks graph + SubGraphs depth-first, visiting every node once. */
	template <typename FVisitor>
	void ForEachNodeInGraph(const UEdGraph* Graph, FVisitor Visitor)
	{
		TSet<const UEdGraph*> Visited;
		TArray<const UEdGraph*> Pending;
		Pending.Push(Graph);
		while (Pending.Num() > 0)
		{
			const UEdGraph* Current = Pending.Pop();
			if (Visited.Contains(Current))
			{
				continue;
			}
			Visited.Add(Current);
			for (const UEdGraphNode* Node : Current->Nodes)
			{
				if (Node != nullptr)
				{
					Visitor(Node);
				}
			}
			for (const UEdGraph* Sub : Current->SubGraphs)
			{
				if (Sub != nullptr)
				{
					Pending.Push(Sub);
				}
			}
		}
	}

UEdGraphNode* FindNodeByGuid(UBlueprint* Blueprint, const FGuid& Guid)
{
UEdGraphNode* Found = nullptr;
auto Visit = [&Guid, &Found](const UEdGraphNode* Node)
{
if (Found == nullptr && Node->NodeGuid == Guid)
{
Found = const_cast<UEdGraphNode*>(Node);
}
};
for (UEdGraph* Graph : Blueprint->UbergraphPages)
{
if (Graph == nullptr) { continue; }
ForEachNodeInGraph(Graph, Visit);
}
for (UEdGraph* Graph : Blueprint->FunctionGraphs)
{
if (Graph == nullptr) { continue; }
ForEachNodeInGraph(Graph, Visit);
}
for (UEdGraph* Graph : Blueprint->MacroGraphs)
{
if (Graph == nullptr) { continue; }
ForEachNodeInGraph(Graph, Visit);
}
return Found;
}

UEdGraphPin* FindPinByIndex(UEdGraphNode* Node, const FString& PinName)
{
if (Node == nullptr) { return nullptr; }

// Two pin naming conventions are supported:
//
// 1. Legacy generic names: "self" → first input data pin,
//    "A"→1, "B"→2, ..., "G"→7 (input data pins by index).
//    "ReturnValue"/"Value" → first output data pin.
//
// 2. Positional names from the DSL: "__argN" maps to the Nth
//    non-exec, non-self, non-WorldContextObject input data pin.
//    "__target" maps to the self/target input pin.
//    This lets the DSL pass arguments by position without knowing
//    the actual UE pin names, and the bridge resolves them here.

// --- Positional __argN / __target ---
static const FString ArgPrefix = TEXT("__arg");
static const FString TargetName = TEXT("__target");

if (PinName == TargetName)
{
	// Find the first input pin named self/Self/Target/__Target
	for (UEdGraphPin* Pin : Node->Pins)
	{
		if (Pin == nullptr) { continue; }
		if (Pin->Direction != EGPD_Input) { continue; }
		if (Pin->PinType.PinCategory == UEdGraphSchema_K2::PC_Exec) { continue; }
		FString NameStr = Pin->PinName.ToString();
		if (NameStr == TEXT("self") || NameStr == TEXT("Self")
			|| NameStr == TEXT("Target") || NameStr == TEXT("__Target"))
		{
			return Pin;
		}
	}
	return nullptr; // No target pin on this node (e.g. static call)
}

if (PinName.StartsWith(ArgPrefix))
{
	FString IndexStr = PinName.RightChop(ArgPrefix.Len());
	if (!IndexStr.IsNumeric())
	{
		return nullptr;
	}
	int32 ArgIdx = FCString::Atoi(*IndexStr);
	if (ArgIdx < 0)
	{
		return nullptr;
	}

	// Collect non-exec, non-self, non-WorldContextObject input pins.
	//
	// "Target" semantics change with call form: the target pin of non-static calls (interface
	// Message node's receiver is named "Target") must be excluded from the position table; while static
	// functions have no target -- a pin named "Target" is a real UFUNCTION parameter
	// (VInterpTo / FInterpTo's Current/Target/DeltaTime/InterpSpeed),
	// excluding it would collapse the position table and shift all subsequent parameters (Lerp_V_B:
	// Target parameter dangling, DeltaSeconds misconnected to InterpSpeed).
	// UK2Node_CallFunction's target pin is always named "self" (PN_Self), therefore
	// "Target" on CallFunction is always kept as a parameter; for non-CallFunction nodes
	// (Message / macro / async delegate target forms) maintain old exclusion (conservative).
	const bool bIsCallFunctionNode = Node->IsA<UK2Node_CallFunction>();
	TArray<UEdGraphPin*> ParamPins;
	for (UEdGraphPin* Pin : Node->Pins)
	{
		if (Pin == nullptr) { continue; }
		if (Pin->Direction != EGPD_Input) { continue; }
		if (Pin->PinType.PinCategory == UEdGraphSchema_K2::PC_Exec) { continue; }
		FString NameStr = Pin->PinName.ToString();
		if (NameStr == TEXT("self") || NameStr == TEXT("Self")
			|| NameStr == TEXT("WorldContextObject") || NameStr == TEXT("__WorldContext"))
		{
			continue;
		}
		if ((NameStr == TEXT("Target") || NameStr == TEXT("__Target"))
			&& !bIsCallFunctionNode)
		{
			continue;
		}
		ParamPins.Add(Pin);
	}
	if (ArgIdx < ParamPins.Num())
	{
		return ParamPins[ArgIdx];
	}
	return nullptr;
}

// --- Legacy generic names ---
int32 TargetInputIndex = INDEX_NONE;
int32 TargetOutputIndex = INDEX_NONE;

if (PinName == TEXT("self"))
{
TargetInputIndex = 0;
}
else if (PinName == TEXT("A")) { TargetInputIndex = 1; }
else if (PinName == TEXT("B")) { TargetInputIndex = 2; }
else if (PinName == TEXT("C")) { TargetInputIndex = 3; }
else if (PinName == TEXT("D")) { TargetInputIndex = 4; }
else if (PinName == TEXT("E")) { TargetInputIndex = 5; }
else if (PinName == TEXT("F")) { TargetInputIndex = 6; }
else if (PinName == TEXT("G")) { TargetInputIndex = 7; }
else if (PinName == TEXT("ReturnValue") || PinName == TEXT("Value"))
{
TargetOutputIndex = 0;
}
else
{
// Not a generic name — let the caller handle it
return nullptr;
}

// Collect data pins (non-exec) by direction
TArray<UEdGraphPin*> InputDataPins;
TArray<UEdGraphPin*> OutputDataPins;
for (UEdGraphPin* Pin : Node->Pins)
{
if (Pin == nullptr) { continue; }
if (Pin->PinType.PinCategory == UEdGraphSchema_K2::PC_Exec) { continue; }
if (Pin->Direction == EGPD_Input)
{
InputDataPins.Add(Pin);
}
else if (Pin->Direction == EGPD_Output)
{
OutputDataPins.Add(Pin);
}
}

if (TargetInputIndex != INDEX_NONE && TargetInputIndex < InputDataPins.Num())
{
return InputDataPins[TargetInputIndex];
}
if (TargetOutputIndex != INDEX_NONE && TargetOutputIndex < OutputDataPins.Num())
{
return OutputDataPins[TargetOutputIndex];
}
// "Value" on a setter (e.g. UK2Node_VariableSet) has no output data pin: its
// value pin is the (only) input data pin, named after the variable. The generic
// "Value" name therefore resolves to the output pin above for getters, but must
// fall back to the input pin here, otherwise the setter connection is silently
// dropped (task 21 #5: "OverlapCount += 1" lost the Add->Set link).
if (PinName == TEXT("Value") && InputDataPins.Num() > 0)
{
return InputDataPins[0];
}
return nullptr;
}

	// Faithful replica of UK2Node_CreateDelegate::IsValid() with editor
	// semantics (bDontUseSkeletalClassForSelf=false). The original is NOT
	// exported from UnrealEditor-BlueprintGraph.dll (IsValid and
	// UEdGraphSchema_K2::FunctionCanBeUsedInDelegate lack BLUEPRINTGRAPH_API,
	// linking them caused LNK2019), so this rebuilds the check from exported
	// primitives: GetFunctionName / GetDelegateOutPin / GetDelegateSignature /
	// GetScopeClass + FMemberReference. Branch order and messages mirror
	// K2Node_CreateDelegate.cpp L57-185 (UE 5.0).
	static bool ValidateCreateDelegateEditorSemantics(const UK2Node_CreateDelegate* Node, FString* OutMsg)
	{
		const FName FunctionName = Node->GetFunctionName();
		if (FunctionName.IsNone())
		{
			if (OutMsg) { *OutMsg = TEXT("No function/event specified."); }
			return false;
		}

		const UEdGraphPin* DelegatePin = Node->GetDelegateOutPin();
		if (DelegatePin == nullptr)
		{
			if (OutMsg) { *OutMsg = TEXT("Malformed node - there's no delegate output pin."); }
			return false;
		}

		const UFunction* Signature = Node->GetDelegateSignature();
		if (Signature == nullptr)
		{
			if (OutMsg) { *OutMsg = TEXT("Unable to determine expected signature - is the delegate pin connected?"); }
			return false;
		}

		// Secondary connections must all match the primary signature.
		for (int32 PinIter = 1; PinIter < DelegatePin->LinkedTo.Num(); ++PinIter)
		{
			const UEdGraphPin* OtherPin = DelegatePin->LinkedTo[PinIter];
			const UFunction* OtherSignature = OtherPin
				? FMemberReference::ResolveSimpleMemberReference<UFunction>(OtherPin->PinType.PinSubCategoryMemberReference)
				: nullptr;
			if (OtherSignature == nullptr || !Signature->IsSignatureCompatibleWith(OtherSignature))
			{
				if (OutMsg) { *OutMsg = TEXT("A connected delegate's signature is incompatible - has that delegate changed?"); }
				return false;
			}
		}

		UClass* ScopeClass = Node->GetScopeClass(false);
		if (ScopeClass == nullptr)
		{
			if (OutMsg)
			{
				*OutMsg = FString::Printf(TEXT("Unable to determine context for the selected function/event: '%s' - make sure the target 'Self' pin is properly set up."), *FunctionName.ToString());
			}
			return false;
		}

		FMemberReference MemberReference;
		MemberReference.SetDirect(FunctionName, FGuid(), TSubclassOf<UObject>(ScopeClass), false);
		const UFunction* FoundFunction = MemberReference.ResolveMember<UFunction>();
		if (FoundFunction == nullptr)
		{
			if (OutMsg)
			{
				*OutMsg = FString::Printf(TEXT("Unable to find the selected function/event: '%s' - has it been deleted?"), *FunctionName.ToString());
			}
			return false;
		}
		if (!Signature->IsSignatureCompatibleWith(FoundFunction))
		{
			if (OutMsg)
			{
				*OutMsg = FString::Printf(TEXT("The function/event '%s' does not match the necessary signature - has the delegate or function/event changed?"), *FunctionName.ToString());
			}
			return false;
		}
		// Subset of UEdGraphSchema_K2::FunctionCanBeUsedInDelegate (unexported):
		// must be BlueprintCallable, not latent, not pure. The deprecated /
		// CanUserKismetCallFunction nuances are omitted - not reachable for the
		// CustomEvent targets this validation runs against.
		if (!FoundFunction->HasAllFunctionFlags(FUNC_BlueprintCallable)
			|| FoundFunction->HasMetaData(FBlueprintMetadata::MD_Latent)
			|| FoundFunction->HasAllFunctionFlags(FUNC_BlueprintPure))
		{
			if (OutMsg)
			{
				*OutMsg = FString::Printf(TEXT("The selected function/event '%s' is not bindable - is the function/event deprecated, pure or latent?"), *FunctionName.ToString());
			}
			return false;
		}
		return true;
	}

	// Session-level registry of intended CreateDelegate function names, see
	// ForgeMindBlueprintUtils.h for why this exists.
	static TMap<FGuid, FName> GCreateDelegateIntendedNames;

	void RememberCreateDelegateName(const FGuid& NodeGuid, const FName& FunctionName)
	{
		if (!FunctionName.IsNone())
		{
			GCreateDelegateIntendedNames.Add(NodeGuid, FunctionName);
		}
	}

	void RestoreCreateDelegateNames(UBlueprint* Blueprint)
	{
		auto Restore = [](const UEdGraphNode* NodeObj)
		{
			// Graphs come from a mutable Blueprint; the walker just hands out
			// const pointers, so unwrap once here.
			UK2Node_CreateDelegate* CreateNode = Cast<UK2Node_CreateDelegate>(const_cast<UEdGraphNode*>(NodeObj));
			if (CreateNode == nullptr)
			{
				return;
			}
			const FName* Intended = GCreateDelegateIntendedNames.Find(CreateNode->NodeGuid);
			if (Intended == nullptr || CreateNode->GetFunctionName() == *Intended)
			{
				return;
			}

			// Same strategy as add-time: when the target function resolves in
			// the current scope, go through HandleAnyChange(true) so UE fixes
			// up dependent state; if it was still cleared afterwards, force
			// the name back. When the target does not resolve yet (e.g. the
			// CustomEvent graph node has not been through a compile), assign
			// directly - mirroring UE's own bypass in K2Node_Event::ExpandNode.
			UClass* ScopeClass = CreateNode->GetScopeClass();
			bool bResolvable = false;
			if (ScopeClass != nullptr)
			{
				FMemberReference ProbeRef;
				ProbeRef.SetDirect(*Intended, FGuid(), TSubclassOf<UObject>(ScopeClass), false);
				bResolvable = (ProbeRef.ResolveMember<UFunction>() != nullptr);
			}
			if (bResolvable)
			{
				CreateNode->HandleAnyChange(true);
				if (CreateNode->GetFunctionName().IsNone())
				{
					CreateNode->SelectedFunctionName = *Intended;
				}
			}
			else
			{
				CreateNode->SelectedFunctionName = *Intended;
				if (UEdGraph* EventGraph = CreateNode->GetGraph())
				{
					EventGraph->NotifyGraphChanged();
				}
			}
		};
		for (UEdGraph* Graph : Blueprint->UbergraphPages)
		{
			if (Graph != nullptr) { ForEachNodeInGraph(Graph, Restore); }
		}
		for (UEdGraph* Graph : Blueprint->FunctionGraphs)
		{
			if (Graph != nullptr) { ForEachNodeInGraph(Graph, Restore); }
		}
		for (UEdGraph* Graph : Blueprint->MacroGraphs)
		{
			if (Graph != nullptr) { ForEachNodeInGraph(Graph, Restore); }
		}
	}

	/**
	 * Aligns CustomEvent handler parameter pins with the delegate signature
	 * reached through this blueprint's CreateDelegate nodes.
	 *
	 * The editor derives a delegate-bound custom event's parameters FROM the
	 * delegate signature (right-click the delegate pin -> "Create Event"), so
	 * its UFunction parameters carry the signature's exact PropertyFlags -
	 * including CPF_ReferenceParm on `const FHitResult&`-style params.
	 * Programmatically-created CustomEvents declare their params from the
	 * MCP/DSL payload instead (by value). UFunction::IsSignatureCompatibleWith
	 * (UObject/Class.cpp) XORs per-param PropertyFlags against an ignore list
	 * that covers CPF_ConstParm but NOT CPF_ReferenceParm, so a by-value
	 * SweepResult makes UK2Node_CreateDelegate::IsValid fail with "does not
	 * match the necessary signature" even when every pin type matches.
	 *
	 * Mirrors the editor's derive-from-signature behavior: copy the signature's
	 * per-param const/reference flags onto the CustomEvent's param output pins.
	 * KismetCompiler's CreatePropertiesFromList maps PinType.bIsConst ->
	 * CPF_ConstParm and Term.bPassedByReference (PinType.bIsReference) ->
	 * CPF_ReferenceParm|CPF_OutParm on the next compile, making the handler
	 * signature-compatible with the delegate.
	 */
	void AlignCustomEventParamsWithDelegates(UBlueprint* Blueprint)
	{
		// CreateDelegate nodes can sit in any graph (the handler event always
		// lives in the Ubergraph, but the CreateDelegate itself may be inside
		// a function graph - e.g. an unbind helper).
		TArray<UK2Node_CreateDelegate*> CreateNodes;
		auto Gather = [&CreateNodes](const UEdGraphNode* NodeObj)
		{
			if (UK2Node_CreateDelegate* Create = Cast<UK2Node_CreateDelegate>(const_cast<UEdGraphNode*>(NodeObj)))
			{
				CreateNodes.Add(Create);
			}
		};
		for (UEdGraph* Graph : Blueprint->UbergraphPages)
		{
			if (Graph != nullptr) { ForEachNodeInGraph(Graph, Gather); }
		}
		for (UEdGraph* Graph : Blueprint->FunctionGraphs)
		{
			if (Graph != nullptr) { ForEachNodeInGraph(Graph, Gather); }
		}
		for (UEdGraph* Graph : Blueprint->MacroGraphs)
		{
			if (Graph != nullptr) { ForEachNodeInGraph(Graph, Gather); }
		}

		auto FindCustomEvent = [Blueprint](const FName& FunctionName) -> UK2Node_CustomEvent*
		{
			UK2Node_CustomEvent* Found = nullptr;
			auto Search = [&Found, &FunctionName](const UEdGraphNode* NodeObj)
			{
				if (Found != nullptr) { return; }
				if (UK2Node_CustomEvent* Custom = Cast<UK2Node_CustomEvent>(const_cast<UEdGraphNode*>(NodeObj)))
				{
					if (Custom->CustomFunctionName == FunctionName)
					{
						Found = Custom;
					}
				}
			};
			for (UEdGraph* Graph : Blueprint->UbergraphPages)
			{
				if (Graph == nullptr || Found != nullptr) { continue; }
				ForEachNodeInGraph(Graph, Search);
			}
			return Found;
		};

		for (UK2Node_CreateDelegate* CreateNode : CreateNodes)
		{
			const FName TargetFn = CreateNode->GetFunctionName();
			if (TargetFn.IsNone()) { continue; }
			const UFunction* Signature = CreateNode->GetDelegateSignature();
			if (Signature == nullptr) { continue; }

			UK2Node_CustomEvent* EventNode = FindCustomEvent(TargetFn);
			if (EventNode == nullptr) { continue; }

			// Signature param const/reference flags keyed by param name. The
			// handler's UFunction is compiled from the CustomEvent's STUB
			// function graph, whose UK2Node_FunctionEntry receives
			// SrcCustomEventNode->UserDefinedPins (KismetCompiler.cpp
			// CreateFunctionStubForEvent) - so the flags must be written to
			// UserDefinedPins; the graph pins are updated as well to keep the
			// node self-consistent until its next AllocateDefaultPins.
			struct FParamFlagOverride
			{
				bool bConst = false;
				bool bReference = false;
			};
			TMap<FName, FParamFlagOverride> Overrides;
			// Same (non-const) iterator form as UFunction::IsSignatureCompatibleWith
			TFieldIterator<FProperty> SigIt(const_cast<UFunction*>(Signature));
			for (; SigIt && (SigIt->PropertyFlags & CPF_Parm); ++SigIt)
			{
				FParamFlagOverride Flags;
				Flags.bConst = SigIt->HasAnyPropertyFlags(CPF_ConstParm);
				Flags.bReference = SigIt->HasAnyPropertyFlags(CPF_ReferenceParm);
				Overrides.Add(SigIt->GetFName(), Flags);
			}
			if (Overrides.Num() == 0) { continue; }

			// Type guard: only align flags over the same underlying type (a
			// genuine type mismatch fails signature checks anyway and must
			// not be papered over here).
			auto MatchesPropertyType = [](const FEdGraphPinType& PinType, const FProperty* Prop) -> bool
			{
				if (const FStructProperty* StructProp = CastField<FStructProperty>(Prop))
				{
					return PinType.PinCategory == UEdGraphSchema_K2::PC_Struct
						&& PinType.PinSubCategoryObject.Get() == StructProp->Struct;
				}
				if (const FObjectPropertyBase* ObjProp = CastField<FObjectPropertyBase>(Prop))
				{
					return PinType.PinCategory == UEdGraphSchema_K2::PC_Object
						&& PinType.PinSubCategoryObject.Get() == ObjProp->PropertyClass;
				}
				return true;
			};

			for (const TPair<FName, FParamFlagOverride>& Override : Overrides)
			{
				const FProperty* SigProp = nullptr;
				for (TFieldIterator<FProperty> It(const_cast<UFunction*>(Signature)); It && (It->PropertyFlags & CPF_Parm); ++It)
				{
					if (It->GetFName() == Override.Key) { SigProp = *It; break; }
				}
				if (SigProp == nullptr) { continue; }

				// 1) UserDefinedPins - the authoritative store the compiler
				//    derives the stub function signature from.
				for (TSharedPtr<FUserPinInfo>& UserPin : EventNode->UserDefinedPins)
				{
					if (UserPin.IsValid() && UserPin->PinName == Override.Key
						&& MatchesPropertyType(UserPin->PinType, SigProp))
					{
						UserPin->PinType.bIsConst = Override.Value.bConst;
						UserPin->PinType.bIsReference = Override.Value.bReference;
					}
				}

				// 2) Graph pins - same-name param output pin, kept in sync.
				if (UEdGraphPin* ParamPin = EventNode->FindPin(Override.Key, EGPD_Output))
				{
					if (MatchesPropertyType(ParamPin->PinType, SigProp))
					{
						ParamPin->PinType.bIsConst = Override.Value.bConst;
						ParamPin->PinType.bIsReference = Override.Value.bReference;
					}
				}
			}
		}
	}

	void CompileAndCollectDiagnostics(UBlueprint* Blueprint, TSharedPtr<FJsonObject>& Result)
	{
		FBlueprintEditorUtils::MarkBlueprintAsStructurallyModified(Blueprint);

		// Recreate any CreateDelegate function name that HandleAnyChange
		// cleared while the node's delegate pin was unconnected. This runs
		// AFTER MarkBlueprintAsStructurallyModified because that mark itself
		// can trigger source-node reconstruction (and thus another clearing
		// pass) - restoring any earlier would be undone before the compile.
		RestoreCreateDelegateNames(Blueprint);

		// Align CustomEvent handler params with the delegate signature they
		// are bound to (same timing rationale as RestoreCreateDelegateNames).
		AlignCustomEventParamsWithDelegates(Blueprint);

		// UE 5.0: FKismetEditorUtilities::CompileBlueprint returns void (the bool
		// return only landed in later versions), so there is no return value to
		// inspect - error detection relies on Blueprint->Status below.
		FKismetEditorUtilities::CompileBlueprint(Blueprint);
		Blueprint->MarkPackageDirty();

		// CompileBlueprint reports nothing itself, so Blueprint->Status
		// (EBlueprintStatus) is the authoritative check: BS_Error means the
		// compile produced hard errors, BS_UpToDate means it compiled cleanly.
		// Some versions also use BS_Unknown as an error state.
		const bool bHasErrors = Blueprint->Status == BS_Error
			|| Blueprint->Status == BS_Unknown;

		TArray<TSharedPtr<FJsonValue>> Messages;
		auto Collect = [&Messages](const UEdGraphNode* Node)
		{
			if (!Node->bHasCompilerMessage || Node->ErrorMsg.IsEmpty())
			{
				return;
			}
			TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
			Entry->SetNumberField(TEXT("severity"), Node->ErrorType);
			Entry->SetStringField(TEXT("node"), Node->NodeGuid.ToString());
			Entry->SetStringField(TEXT("message"), Node->ErrorMsg);
			Messages.Add(MakeShared<FJsonValueObject>(Entry));
		};
		for (const UEdGraph* Graph : Blueprint->UbergraphPages)
		{
			if (Graph != nullptr) { ForEachNodeInGraph(Graph, Collect); }
		}
		for (const UEdGraph* Graph : Blueprint->FunctionGraphs)
		{
			if (Graph != nullptr) { ForEachNodeInGraph(Graph, Collect); }
		}
		for (const UEdGraph* Graph : Blueprint->MacroGraphs)
		{
			if (Graph != nullptr) { ForEachNodeInGraph(Graph, Collect); }
		}

		// Editor-mode validation pass.
		//
		// Full-compile late validation (KismetCompiler.cpp
		// "Late validation for Delegates") calls
		// UK2Node_CreateDelegate::ValidationAfterFunctionsAreCreated with
		// bFullCompile=true, which resolves the target function against the
		// freshly built GeneratedClass. The editor UI however validates the
		// same node against the SkeletonGeneratedClass (IsValid() default
		// args). The two checks can therefore disagree: the node carries the
		// generic delegate signature from its connection pin, so a full
		// compile can succeed while the editor still flags the node as
		// invalid. Reproduce the editor's exact check here so compile_blueprint
		// reports what the user would see, including the concrete failure
		// reason (OutMsg names the failed branch of IsValid).
		int32 EditorValidationErrors = 0;
		auto ValidateCreateDelegateEditors = [&Messages, &EditorValidationErrors](const UEdGraphNode* NodeObj)
		{
			const UK2Node_CreateDelegate* CreateNode = Cast<UK2Node_CreateDelegate>(NodeObj);
			if (CreateNode == nullptr)
			{
				return;
			}
			FString Why;
			if (!ValidateCreateDelegateEditorSemantics(CreateNode, &Why))
			{
				++EditorValidationErrors;
				TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
				Entry->SetNumberField(TEXT("severity"), 0);
				Entry->SetStringField(TEXT("node"), NodeObj->NodeGuid.ToString());
				Entry->SetStringField(TEXT("message"),
					FString::Printf(TEXT("[editor validation] CreateDelegate node is invalid: %s"), *Why));
				Messages.Add(MakeShared<FJsonValueObject>(Entry));
			}
		};
		for (const UEdGraph* Graph : Blueprint->UbergraphPages)
		{
			if (Graph != nullptr) { ForEachNodeInGraph(Graph, ValidateCreateDelegateEditors); }
		}
		for (const UEdGraph* Graph : Blueprint->FunctionGraphs)
		{
			if (Graph != nullptr) { ForEachNodeInGraph(Graph, ValidateCreateDelegateEditors); }
		}
		for (const UEdGraph* Graph : Blueprint->MacroGraphs)
		{
			if (Graph != nullptr) { ForEachNodeInGraph(Graph, ValidateCreateDelegateEditors); }
		}
		if (EditorValidationErrors > 0)
		{
			UE_LOG(LogTemp, Warning,
				TEXT("CompileAndCollectDiagnostics: %d CreateDelegate node(s) failed editor-mode validation in %s"),
				EditorValidationErrors, *Blueprint->GetPathName());
		}

		// If CompileBlueprint failed or Blueprint->Status indicates errors
		// but no node-level messages were collected, add a synthetic error
		// entry so the caller's ErrorCount > 0 check catches it.
		if (bHasErrors && Messages.Num() == 0)
		{
			TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
			Entry->SetNumberField(TEXT("severity"), 0);
			Entry->SetStringField(TEXT("node"), TEXT(""));
			Entry->SetStringField(TEXT("message"),
				FString::Printf(TEXT("Blueprint compile failed (Status=%d)"),
					static_cast<int32>(Blueprint->Status)));
			Messages.Add(MakeShared<FJsonValueObject>(Entry));
		}
		Result->SetArrayField(TEXT("compileMessages"), Messages);
		Result->SetBoolField(TEXT("dirty"), Blueprint->GetPackage()->IsDirty());
	}

	void MarkBlueprintModified(UBlueprint* Blueprint)
	{
		if (Blueprint != nullptr)
		{
			Blueprint->Modify();
			Blueprint->MarkPackageDirty();
		}
	}

	bool GraphNameExists(UBlueprint* Blueprint, const FString& Name)
	{
		for (const UEdGraph* G : Blueprint->UbergraphPages)
		{
			if (G != nullptr && G->GetName() == Name) { return true; }
		}
		for (const UEdGraph* G : Blueprint->FunctionGraphs)
		{
			if (G != nullptr && G->GetName() == Name) { return true; }
		}
		for (const UEdGraph* G : Blueprint->MacroGraphs)
		{
			if (G != nullptr && G->GetName() == Name) { return true; }
		}
		return false;
	}

	void CheckForDefaultMemberVars(UUserDefinedStruct* Struct, TArray<FString>& Warnings)
	{
		if (Struct == nullptr)
		{
			return;
		}
		const TArray<FStructVariableDescription>& VarDescs = FStructureEditorUtils::GetVarDesc(Struct);
		TArray<FString> DefaultNames;
		for (const FStructVariableDescription& Desc : VarDescs)
		{
			if (Desc.VarName.ToString().StartsWith(TEXT("MemberVar_")))
			{
				DefaultNames.Add(Desc.VarName.ToString());
			}
		}
		if (DefaultNames.Num() > 0)
		{
            Warnings.Add(TEXT("Struct may contain unused default members; consider removing them."));
		}
	}
}