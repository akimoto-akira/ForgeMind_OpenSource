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
#include "Materials/MaterialExpressionExecBegin.h"
#include "Materials/MaterialExpressionExecEnd.h"
#include "Materials/MaterialExpressionFunctionInput.h"
#include "Materials/MaterialExpressionFunctionOutput.h"
#include "Materials/MaterialExpressionMaterialFunctionCall.h"
#include "Materials/MaterialExpressionCustom.h"
#include "Materials/MaterialExpressionLandscapeLayerBlend.h"
#include "Materials/MaterialExpressionSetMaterialAttributes.h"
#include "Materials/MaterialFunction.h"
#include "Materials/MaterialExpressionConstant.h"
#include "Materials/MaterialExpressionConstant2Vector.h"
#include "Materials/MaterialExpressionConstant3Vector.h"
#include "Materials/MaterialExpressionConstant4Vector.h"
#include "Materials/MaterialExpressionTextureBase.h"
#include "Materials/MaterialInterface.h"
#include "MaterialShared.h"
#include "SceneTypes.h"
#include "UObject/UnrealType.h"

namespace
{
	/**
	 * FExpressionInput's USTRUCT is a noexport mirror (MaterialExpression.h is wrapped
	 * in #if !CPP; the real C++ struct in MaterialExpressionIO.h carries no reflection),
	 * so there is no StaticStruct() to IsChildOf against. The mirror hierarchy is still
	 * recorded in the reflection data, so walk the super-struct chain by name:
	 * FMaterialAttributesInput -> FExpressionInput.
	 */
	bool IsExpressionInputStruct(const UScriptStruct* Struct)
	{
		// GetSuperStruct answers UStruct* in 5.0 - the walk only needs the name.
		for (const UStruct* Current = Struct; Current != nullptr; Current = Current->GetSuperStruct())
		{
			if (Current->GetName().Equals(TEXT("ExpressionInput")))
			{
				return true;
			}
		}
		return false;
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

			/**
			* The UPROPERTY that carries "the function this node calls". Only a
			* MaterialExpressionMaterialFunctionCall has one (MaterialFunction); every other
			* expression matches nothing here.
			*/
			FProperty* FindCalledFunctionProperty(UObject* Object)
			{
			if (Object == nullptr)
			{
			return nullptr;
			}
			return ForgeMindIR::FindProperty(Object, TEXT("MaterialFunction"));
			}

			/**
			* True for the keys the two channels name the called function with: "function" in
			* the material step commands (the requirement docs write Function=...),
			* "functionAsset" in an IR node, and the engine's own MaterialFunction spelling.
			*
			* Without this the step command's "function" key resolves to
			* UMaterialExpression::Function - the base class member that records which function
			* an expression belongs to while it is being compiled. That write succeeds, the
			* response reports no error, and the call node is left pointing at no function at
			* all, so it exposes no pins and compiles to nothing.
			*/
			bool IsCalledFunctionKey(const FString& Name)
			{
			return Name.Equals(TEXT("function"), ESearchCase::IgnoreCase)
			|| Name.Equals(TEXT("functionAsset"), ESearchCase::IgnoreCase)
			|| Name.Equals(TEXT("materialFunction"), ESearchCase::IgnoreCase);
			}

			/**
			* Rebuild a call node's pin arrays from the function it now references. The editor
			* does this when a function is picked in the details panel; a bridge-built node has
			* to get its FunctionInputs / FunctionOutputs the same way or there is nothing to
			* wire and nothing to read back.
			*/
			void RefreshFunctionCallPins(UMaterialExpression* Expression)
			{
			UMaterialExpressionMaterialFunctionCall* Call = Cast<UMaterialExpressionMaterialFunctionCall>(Expression);
			if (Call != nullptr && Call->MaterialFunction != nullptr)
			{
			Call->UpdateFromFunctionResource();
			}
			}

		/**
		 * A call node's inputs are not static FExpressionInput members - they are the
		 * per-function entries in FunctionInputs, named after the FunctionInput expression
		 * they mirror inside the function. Name matching is the only way a caller can
		 * address them, and it is the same name the requirement docs and the IR inputs[]
		 * entries carry.
		 */
		FExpressionInput* FindFunctionCallInput(UMaterialExpression* Target, const FString& Name)
		{
			UMaterialExpressionMaterialFunctionCall* Call = Cast<UMaterialExpressionMaterialFunctionCall>(Target);
			if (Call == nullptr)
			{
				return nullptr;
			}
			for (FFunctionExpressionInput& Entry : Call->FunctionInputs)
			{
				if (Entry.ExpressionInput != nullptr
					&& Entry.ExpressionInput->InputName.ToString().Equals(Name, ESearchCase::IgnoreCase))
				{
					return &Entry.Input;
				}
			}
			return nullptr;
		}

		/**
		 * A SetMaterialAttributes node keeps its pins in the dynamic Inputs array:
		 * Inputs[0] is the bundle input (addressed as "MaterialAttributes" - the name
		 * GetInputName(0) reports - and as "Input", the name the requirement docs use),
		 * and every later entry mirrors AttributeSetTypes[i-1] and is named after that
		 * attribute. The editor creates attribute pins on demand (its
		 * PostEditChangeProperty appends to both arrays when a type is added), so a
		 * name that is not listed yet creates the pin the same way here - a bridge-built
		 * node has no details panel to add it through. A freshly created node carries no
		 * entries at all while the compiler indexes Inputs[0] unconditionally, so the
		 * bundle input is ensured before anything is handed out.
		 */
		FExpressionInput* ResolveSetAttributesInput(UMaterialExpressionSetMaterialAttributes* SetNode, const FString& Name)
		{
			if (SetNode->Inputs.Num() == 0)
			{
				SetNode->Inputs.AddDefaulted();
			}
			if (Name.Equals(TEXT("MaterialAttributes"), ESearchCase::IgnoreCase)
				|| Name.Equals(TEXT("Input"), ESearchCase::IgnoreCase))
			{
				return &SetNode->Inputs[0];
			}

			auto MatchesAttribute = [SetNode, &Name](const FGuid& AttributeID) -> bool
			{
				return Name.Equals(FMaterialAttributeDefinitionMap::GetAttributeName(AttributeID), ESearchCase::IgnoreCase)
					|| Name.Equals(FMaterialAttributeDefinitionMap::GetDisplayNameForMaterial(AttributeID, SetNode->Material).ToString(), ESearchCase::IgnoreCase);
			};

			// A pin that already exists is only looked up (Inputs is kept in step with
			// AttributeSetTypes defensively: a hand-edited asset can lag by an entry).
			for (int32 Index = 0; Index < SetNode->AttributeSetTypes.Num(); ++Index)
			{
				if (MatchesAttribute(SetNode->AttributeSetTypes[Index]))
				{
					while (SetNode->Inputs.Num() < Index + 2)
					{
						SetNode->Inputs.AddDefaulted();
					}
					return &SetNode->Inputs[Index + 1];
				}
			}

			// Not listed yet: append the attribute id and a matching named input, the
			// exact pair the editor's PostEditChangeProperty adds on a type add.
			for (const FGuid& AttributeID : FMaterialAttributeDefinitionMap::GetOrderedVisibleAttributeList())
			{
				if (MatchesAttribute(AttributeID))
				{
					SetNode->AttributeSetTypes.Add(AttributeID);
					FExpressionInput& NewInput = SetNode->Inputs.AddDefaulted_GetRef();
					NewInput.InputName = FName(*FMaterialAttributeDefinitionMap::GetDisplayNameForMaterial(AttributeID, SetNode->Material).ToString());
					return &NewInput;
				}
			}
			return nullptr;
		}

		/**
		 * Write a fresh Guid into the "Id" member a function interface expression carries
		 * (UMaterialExpressionFunctionInput / UMaterialExpressionFunctionOutput both name it Id).
		 * The editor generates one the moment the node is placed; UpdateFromFunctionResource
		 * later restores a call node's wires by matching that Id after save / load. A
		 * bridge-built node that skips the generation keeps the all-zero default: the zero
		 * equals the struct default, so saving writes no Id at all, PostLoad then mints a
		 * fresh non-zero one on the next load, and every wire held by a caller's call node
		 * is silently dropped at the first UpdateFromFunctionResource - while every build
		 * command still reported success and the in-memory read-back still showed the wire.
		 */
		bool GenerateExpressionId(UMaterialExpression* Expression)
		{
			FStructProperty* IdProperty = CastField<FStructProperty>(ForgeMindIR::FindProperty(Expression, TEXT("Id")));
			if (IdProperty == nullptr || IdProperty->Struct == nullptr
				|| !IdProperty->Struct->GetName().Equals(TEXT("Guid")))
			{
				return false;
			}
			*static_cast<FGuid*>(IdProperty->ContainerPtrToValuePtr<void>(Expression)) = FGuid::NewGuid();
			return true;
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
			// Material functions keep the same list under a third name
			// (UMaterialFunction::FunctionExpressions); a function asset whose
			// expressions cannot be found here would be created, edited and read
			// back as an empty graph.
			for (const TCHAR* Candidate : { TEXT("ExpressionCollection"), TEXT("Expressions"), TEXT("FunctionExpressions") })
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
	// An abstract expression class must never be instantiated. NewObject does build the
	// object anyway and the engine only warns about it ("Class which was marked abstract was
	// trying to be loaded. It will be nulled out on save"), and that half-built instance then
	// aborts the editor from a pure virtual call once the material is compiled -
	// MaterialExpressionCustomOutput.h:18 GetFunctionName killed the session while No. 95's
	// third question added the base CustomOutput node (run 20261029_1910). This is the single
	// construction funnel for both channels, so refusing here turns that crash into an
	// ordinary reported failure.
	if (Class->HasAnyClassFlags(CLASS_Abstract))
	{
		return nullptr;
	}
	UMaterial* Material = Cast<UMaterial>(Asset);
	// When execution flow is enabled, already-created begin/end nodes must be reused; do not create an unreferenced pair.
	if (Material != nullptr)
	{
		if (Class == UMaterialExpressionExecBegin::StaticClass() && Material->ExpressionExecBegin)
		{
			return Material->ExpressionExecBegin;
		}
		if (Class == UMaterialExpressionExecEnd::StaticClass() && Material->ExpressionExecEnd)
		{
			return Material->ExpressionExecEnd;
		}
	}
	UMaterialExpression* Expression = NewObject<UMaterialExpression>(Asset, Class, NAME_None, RF_Transactional);
	if (Expression == nullptr)
	{
		return nullptr;
	}
	// The owning asset, whichever of the two carries the graph: a material links its
	// expressions through Material, a material function through Function. Both are
	// read by the compiler (and by UpdateFromFunctionResource, which rebuilds a call
	// node's pins from the function's own expression list), so an expression created
	// inside a function has to know its function.
	Expression->Material = Material;
	Expression->Function = Cast<UMaterialFunction>(Asset);
	if (Material != nullptr)
	{
		if (UMaterialExpressionExecBegin* Begin = Cast<UMaterialExpressionExecBegin>(Expression))
		{
			Material->ExpressionExecBegin = Begin;
		}
		if (UMaterialExpressionExecEnd* End = Cast<UMaterialExpressionExecEnd>(Expression))
		{
			Material->ExpressionExecEnd = End;
		}
	}

	if (!ParameterName.IsEmpty())
	{
		// Write the name before refreshing the GUID: the parameter identity is
		// derived from the name, so the other order hands out a GUID for an empty
		// parameter.
		FString Error;
		SetValue(Expression, TEXT("ParameterName"), MakeShared<FJsonValueString>(ParameterName), Error);
		Expression->UpdateParameterGuid(true, true);
	}

	// A function interface expression is identified by its Id across save / load (see
	// GenerateExpressionId). Creating it without one is the "silent wire loss on reload"
	// root cause, so both channels get it here - the single creation entry point.
	if (Expression->IsA(UMaterialExpressionFunctionInput::StaticClass())
		|| Expression->IsA(UMaterialExpressionFunctionOutput::StaticClass()))
	{
		GenerateExpressionId(Expression);
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

/**
 * Resolve a Custom node's input pin by the label the editor shows for it.
 *
 * A UMaterialExpressionCustom carries no named members for its arguments: each pin is one
 * FCustomInput entry whose InputName is the text the details panel displays and whose Input
 * member is the wire. A name that is already listed resolves to that entry; a name that is not
 * gets one created, which is what adding an input row in the editor does - a bridge-created node
 * has no details panel to add it through, and its Compile reads the names straight out of this
 * array (MaterialExpressions.cpp:12034-12061).
 *
 * Three rules, in this order, because the array and the class share one namespace of names:
 *   * An entry already carrying the requested name always wins. It is the one piece of evidence
 *     that the name was meant as an argument rather than a property, and it is what lets an
 *     argument be named like a static member ("Code", "Description") once its row exists.
 *   * Otherwise a name that matches a real static UPROPERTY is not a pin: the Custom node's own
 *     "Code"/"OutputType"/"Description" are strings and an enum, and treating them as pins handed
 *     the value to Inputs[i].Input.Expression, which left the node compiling with an empty "Code"
 *     plus a nameless argument - "Custom material Custom missing input 2 (Code)" (run
 *     20261029_2246). The caller falls through to the plain property lookup.
 *   * Otherwise a row is created, reusing the blank row the class constructor seeds Inputs with
 *     (MaterialExpressions.cpp:12025-12026) before appending - naming that row, not just wiring
 *     it, because an unnamed row is skipped by Compile and stays invisible to a read-back that
 *     keys its input entries by the row's InputName. That blank row is why a Custom node built in
 *     the editor shows an unnamed first pin and numbers the first real argument as
 *     "input 2"; filling the existing row instead of always appending keeps the bridge's first
 *     named argument in the first slot.
 */
static FExpressionInput* ResolveCustomInput(UMaterialExpression* Expression, const FString& Name)
{
	UMaterialExpressionCustom* CustomNode = Cast<UMaterialExpressionCustom>(Expression);
	if (CustomNode == nullptr || Name.IsEmpty())
	{
		return nullptr;
	}
	int32 UnnamedIndex = INDEX_NONE;
	for (int32 Index = 0; Index < CustomNode->Inputs.Num(); ++Index)
	{
		FCustomInput& Entry = CustomNode->Inputs[Index];
		if (Entry.InputName.ToString().Equals(Name, ESearchCase::IgnoreCase))
		{
			return &Entry.Input;
		}
		if (Entry.InputName.IsNone() && UnnamedIndex == INDEX_NONE)
		{
			UnnamedIndex = Index;
		}
	}
	if (CustomNode->GetClass()->FindPropertyByName(FName(*Name)) != nullptr)
	{
		return nullptr;
	}
	if (UnnamedIndex != INDEX_NONE)
	{
		// The name lives on the FCustomInput row, not on its FExpressionInput member: both
		// types carry an InputName and only the row's is what GetInputName reports and what
		// Compile matches the HLSL argument names against. Indexing rather than holding a
		// pointer keeps this valid no matter how the array is resized in between.
		// Stamping it is what adding a row in the details panel leaves behind, minus the
		// spaces it strips from a typed-in name (PostEditChangeProperty,
		// MaterialExpressions.cpp:12098-12112).
		CustomNode->Inputs[UnnamedIndex].InputName = FName(*Name);
		return &CustomNode->Inputs[UnnamedIndex].Input;
	}
	const int32 NewIndex = CustomNode->Inputs.AddDefaulted();
	FCustomInput& Added = CustomNode->Inputs[NewIndex];
	Added.InputName = FName(*Name);
	return &Added.Input;
}

/**
 * Resolve a LandscapeLayerBlend node's layer pin by the layer name the editor prints on it.
 *
 * Same shape as the Custom node's arguments handled just above: the layers live in the dynamic
 * "Layers" array, each FLayerBlendInput carrying LayerName plus the LayerInput wire (and the
 * optional HeightInput), so a requested name is matched against LayerName and, when it is not
 * listed yet, appended with that name - which is what adding a layer row in the details panel
 * does, and a bridge-created node has no panel to add it through. The bundle's own
 * "LayerName"/"Input" spellings are kept for the two static members of the same node.
 *
 * The class is looked up by name rather than through Cast<UMaterialExpressionLandscapeLayerBlend>:
 * UMaterialExpressionLandscapeLayerBlend is declared with no DLL export macro at all, so its
 * generated GetPrivateStaticClass is not exported from the Landscape module and a Cast would
 * compile here yet leave an unresolved external at link time. Only the layout is touched
 * directly, which the header provides; the type identity comes from reflection.
 */
static FExpressionInput* ResolveLayerBlendInput(UMaterialExpression* Expression, const FString& Name)
{
	if (Expression == nullptr || Name.IsEmpty())
	{
		return nullptr;
	}
	UClass* BlendClass = ForgeMindIR::ResolveClass(TEXT("MaterialExpressionLandscapeLayerBlend"));
	if (BlendClass == nullptr || !Expression->IsA(BlendClass))
	{
		return nullptr;
	}
	UMaterialExpressionLandscapeLayerBlend* BlendNode = static_cast<UMaterialExpressionLandscapeLayerBlend*>(Expression);
	for (FLayerBlendInput& Entry : BlendNode->Layers)
	{
		if (Entry.LayerName.ToString().Equals(Name, ESearchCase::IgnoreCase))
		{
			return &Entry.LayerInput;
		}
	}
	const int32 NewIndex = BlendNode->Layers.AddDefaulted();
	FLayerBlendInput& Added = BlendNode->Layers[NewIndex];
	Added.LayerName = FName(*Name);
	return &Added.LayerInput;
}

FExpressionInput* ForgeMindMaterial::GetExpressionInput(UObject* Owner, const FString& Name)
{
	if (Owner == nullptr || Name.IsEmpty())
	{
		return nullptr;
	}
	// A function call's inputs are the entries of its dynamic FunctionInputs array,
	// not members of the class, so they are matched by the name of the FunctionInput
	// they mirror. One lookup serves both channels - the step commands and the IR
	// input pass - so neither grows its own way of finding them.
	if (UMaterialExpression* Expression = Cast<UMaterialExpression>(Owner))
	{
		if (FExpressionInput* Dynamic = FindFunctionCallInput(Expression, Name))
		{
			return Dynamic;
		}
		// A SetMaterialAttributes node's pins live in its dynamic Inputs array, not in
		// named members - resolved (and created on demand, the way the editor's details
		// panel adds them) before the static member lookup below.
		if (UMaterialExpressionSetMaterialAttributes* SetNode = Cast<UMaterialExpressionSetMaterialAttributes>(Expression))
		{
			return ResolveSetAttributesInput(SetNode, Name);
		}
		// Same shape of problem in a Custom node: its arguments live in the dynamic Inputs
		// array, keyed by the InputName the editor prints on the pin.
		if (FExpressionInput* CustomPin = ResolveCustomInput(Expression, Name))
		{
			return CustomPin;
		}
		// ...and in a LandscapeLayerBlend node, whose layer pins are the dynamic Layers array,
		// keyed by the LayerName shown on each pin.
		if (FExpressionInput* LayerPin = ResolveLayerBlendInput(Expression, Name))
		{
			return LayerPin;
		}
	}

	FProperty* Property = ForgeMindIR::FindProperty(Owner, Name);
	FStructProperty* StructProperty = CastField<FStructProperty>(Property);
	// Any FExpressionInput-derived struct is an input pin, not only the base struct:
	// Break and Blend carry FMaterialAttributesInput members, and rejecting them by
	// exact struct name leaves their bundle inputs ("MaterialAttributes" / "A" / "B")
	// unresolvable while the pin visibly exists in the editor.
	if (StructProperty != nullptr && IsExpressionInputStruct(StructProperty->Struct))
	{
		return reinterpret_cast<FExpressionInput*>(StructProperty->ContainerPtrToValuePtr<void>(Owner));
	}

	// Pins that are not named members at all. QualitySwitch and FeatureLevelSwitch keep their
	// per-level wires in the static array "FExpressionInput Inputs[EMaterialQualityLevel::Num]",
	// so reflection never finds a property called "Low"/"Medium"/"High"/"Epic" even though the
	// editor prints exactly those names on the node - the engine answers for them through
	// GetInputName(Index), and GetInputs() lists the same entries in the same order
	// (MaterialExpressions.cpp, UMaterialExpressionQualitySwitch::GetInputs puts Default first and
	// then the array, matching its GetInputName). Asking the expression interface here means every
	// node that hides pins in an array becomes reachable through the one lookup both channels
	// share, instead of adding another per-class special case. It runs only after the member
	// lookup, so a class that has both keeps the member's answer.
	if (UMaterialExpression* Expression = Cast<UMaterialExpression>(Owner))
	{
		const TArray<FExpressionInput*> Inputs = Expression->GetInputs();
		for (int32 Index = 0; Index < Inputs.Num(); ++Index)
		{
			if (Inputs[Index] == nullptr)
			{
				continue;
			}
			const FName ResolvedName = Expression->GetInputName(Index);
			if (ResolvedName.IsNone())
			{
				// An unnamed pin (UMaterialExpressionFunctionOutput does not override GetInputName,
				// so the engine answers NAME_None) must not be claimed by any requested name,
				// including the literal "None" that NAME_None prints as.
				continue;
			}
			if (ResolvedName.ToString().Equals(Name, ESearchCase::IgnoreCase))
			{
				return Inputs[Index];
			}
		}
	}

	return nullptr;
}

FExpressionExecOutput* ForgeMindMaterial::GetExecutionOutput(UMaterialExpression* Owner, const FString& Name)
{
	if (Owner == nullptr || Name.IsEmpty()) return nullptr;
	TArray<FExpressionExecOutputEntry> Outputs;
	Owner->GetExecOutputs(Outputs);
	for (const FExpressionExecOutputEntry& Output : Outputs)
	{
		if (Output.Name.ToString().Equals(Name, ESearchCase::IgnoreCase)) return Output.Output;
	}
	return nullptr;
}

bool ForgeMindMaterial::ConnectExecutionOutput(UMaterialExpression* Source, const FString& Name,
	UMaterialExpression* Target, FString& OutError)
{
	FExpressionExecOutput* Output = GetExecutionOutput(Source, Name);
	if (Output == nullptr || Target == nullptr || !Target->HasExecInput())
	{
		OutError = TEXT("execution connection requires a declared exec output and a target with an exec input");
		return false;
	}
	UMaterial* Material = Source->Material;
	if (Material == nullptr || Target->Material != Material || !Material->IsCompiledWithExecutionFlow())
	{
		OutError = TEXT("execution nodes must belong to the same material with bEnableExecWire and r.MaterialEnableControlFlow enabled");
		return false;
	}
	// LoopBody is a nested execution domain and needs no back edge; reject back edges to avoid compiler recursion overflow.
	TSet<UMaterialExpression*> Visited;
	TArray<UMaterialExpression*> Pending;
	Pending.Add(Target);
	while (Pending.Num() > 0)
	{
		UMaterialExpression* Current = Pending.Pop(false);
		if (Current == Source)
		{
			OutError = TEXT("execution connection would create a cycle; use the loop's LoopBody scope without a back edge");
			return false;
		}
		if (Visited.Contains(Current)) continue;
		Visited.Add(Current);
		TArray<FExpressionExecOutputEntry> Next;
		Current->GetExecOutputs(Next);
		for (const FExpressionExecOutputEntry& Edge : Next)
		{
			if (UMaterialExpression* Connected = Edge.Output->GetExpression()) Pending.Add(Connected);
		}
	}
	Output->Connect(Target);
	Material->MarkPackageDirty();
	return Output->GetExpression() == Target;
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

bool ForgeMindMaterial::IsMaterialOutputActive(UObject* Asset, const FString& Name)
{
	UMaterial* Material = Cast<UMaterial>(Asset);
	if (Material == nullptr || Name.IsEmpty())
	{
		// Material functions and instances carry no domain-driven output mask to consult,
		// so there is nothing to veto here; the caller still fails on an unknown pin name.
		return true;
	}
	if (Material->IsCompiledWithExecutionFlow())
	{
		// Every property of an execution-flow material is emitted through ExecBegin and returned
		// from ExecEnd (Material.cpp:5011-5015, MaterialExpressions.cpp:22007-22011), so the
		// per-property active mask does not describe this material and must not veto its pins.
		return true;
	}
	const UEnum* PropertyEnum = StaticEnum<EMaterialProperty>();
	if (PropertyEnum == nullptr)
	{
		return true;
	}
	const int64 Value = ResolveEnumValue(PropertyEnum, TEXT("MP_") + Name);
	if (Value == INDEX_NONE)
	{
		// Not a known property at all: GetMaterialOutputInput reports the unknown pin name, which
		// is the right message for this case - inventing an inactivity verdict would mask it.
		return true;
	}
	return Material->IsPropertyActive(static_cast<EMaterialProperty>(Value));
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

bool ForgeMindMaterial::TryResolveNamedOutputIndex(UMaterialExpression* Source, const FString& PinName, int32& OutIndex)
{
	const FString Pin = PinName.TrimStartAndEnd();
	// Single letters are channel-extraction requests (R/G/B/A): the swizzle table's
	// ComponentMask path owns them, so they are not resolved against the output list
	// even where a same-named output exists (a TextureSample's "R"), keeping the
	// existing wire shape for every already-passing case.
	if (Source == nullptr || Pin.IsEmpty() || Pin.Len() == 1)
	{
		return false;
	}
	const TArray<FExpressionOutput>& Outputs = Source->GetOutputs();
	for (int32 Index = 0; Index < Outputs.Num(); ++Index)
	{
		if (Outputs[Index].OutputName.ToString().Equals(Pin, ESearchCase::IgnoreCase))
		{
			OutIndex = Index;
			return true;
		}
	}
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
	// A call node's called function is resolved before the plain lookup, not after it:
	// UMaterialExpression carries a base class member called Function ("the material
	// function this expression is being used with"), and the property lookup matches
	// case-insensitively, so "function" - the name both channels use - would land there
	// and leave the call node pointing at no function at all (see IsCalledFunctionKey).
	FProperty* Property = nullptr;
	if (Cast<UMaterialExpression>(Object) != nullptr && IsCalledFunctionKey(PropertyName))
	{
		Property = FindCalledFunctionProperty(Object);
	}
	if (Property == nullptr)
	{
		Property = ForgeMindIR::FindProperty(Object, PropertyName);
	}
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
			// A function reference has to be followed by the pin rebuild the editor
			// runs: without FunctionInputs the call node has no input to wire and no
			// output to read, yet every step around it still reports success.
			if (Property == FindCalledFunctionProperty(Object))
			{
				RefreshFunctionCallPins(Cast<UMaterialExpression>(Object));
			}
			return bImported;
		}
	}

	return ForgeMindIR::SetProperty(Object, Property->GetName(), Value, OutError);
}

bool ForgeMindMaterial::ApplyMaterialSetting(UMaterial* Material, const FString& Key, const TSharedPtr<FJsonValue>& Value, FString& OutError)
{
	if (Material == nullptr)
	{
		OutError = TEXT("no material to apply the setting to");
		return false;
	}
	if (SetValue(Material, Key, Value, OutError))
	{
		if (Key.Equals(TEXT("bEnableExecWire"), ESearchCase::IgnoreCase) && Material->bEnableExecWire)
		{
			if (!Material->IsCompiledWithExecutionFlow())
			{
				OutError = TEXT("bEnableExecWire requires r.MaterialEnableControlFlow=1 in startup configuration");
				return false;
			}
			// Create root nodes before any node edit triggers compilation; explicit node declarations go through the same entry point to reuse them.
			CreateExpression(Material, UMaterialExpressionExecBegin::StaticClass(), FString());
			CreateExpression(Material, UMaterialExpressionExecEnd::StaticClass(), FString());
		}
		return true;
	}
	// Retry on the editor-only data object, which is where later engine versions
	// moved some of these settings to.
	if (UObject* EditorData = GetEditorOnlyData(Material))
	{
		return SetValue(EditorData, Key, Value, OutError);
	}
	return false;
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
	// Enum settings and node properties are authored by both channels in the bare
	// form ("Surface", "Opaque", "FromMaterialExpression"); SetValue accepts exactly
	// that form, so the reader hands back the same form and a read value can be
	// written back unchanged. The engine's own name carries a scope and a prefix
	// ("EMaterialDomain::MD_Surface", "MSM_FromMaterialExpression"); what remains
	// after the first underscore is the name ResolveEnumValue matches on, which is
	// why stripping here and tolerating the prefix on the write side are one rule.
	auto AuthoredEnumName = [](const UEnum* Enum, int64 Value) -> FString
	{
		if (Enum == nullptr)
		{
			return FString::Printf(TEXT("%lld"), Value);
		}
		const FString FullName = Enum->GetNameStringByValue(Value);
		const int32 Scope = FullName.Find(TEXT("::"), ESearchCase::IgnoreCase, ESearchDir::FromEnd);
		const FString Bare = Scope != INDEX_NONE ? FullName.Mid(Scope + 2) : FullName;
		const int32 Underscore = Bare.Find(TEXT("_"));
		return Underscore != INDEX_NONE ? Bare.Mid(Underscore + 1) : Bare;
	};

	if (FEnumProperty* EnumProperty = CastField<FEnumProperty>(Property))
	{
		const int64 Value = static_cast<int64>(EnumProperty->GetUnderlyingProperty()->GetUnsignedIntPropertyValue(Data));
		return MakeShared<FJsonValueString>(AuthoredEnumName(EnumProperty->GetEnum(), Value));
	}
	if (FByteProperty* ByteProperty = CastField<FByteProperty>(Property))
	{
		if (ByteProperty->Enum != nullptr)
		{
			const uint8 Value = static_cast<uint8>(ByteProperty->GetUnsignedIntPropertyValue(Data));
			return MakeShared<FJsonValueString>(AuthoredEnumName(ByteProperty->Enum, Value));
		}
	}
	// FName members (a SetLocal/GetLocal node's LocalName, a Custom argument's InputName, a
	// parameter's name) have no branch of their own here, and whatever the generic reader makes
	// of one, it is not the authored name: an empty result is what left the read-back silently
	// without localName, which made the case's "both channels name the same local variable"
	// assertion check nothing at all.
	if (CastField<FNameProperty>(Property) != nullptr)
	{
		return MakeShared<FJsonValueString>(static_cast<const FName*>(Data)->ToString());
	}
	return ForgeMindIR::GetProperty(Object, Property->GetName());
}

/**
 * Struct names that are stored as an FLinearColor and written component-wise.
 *
 * The identity is the reflected struct name: UE 5.0.3 declares FLinearColor without a
 * usable StaticStruct() (Color.h carries no generated header for it), so asking the type
 * itself is a compile error. The step commands and this file share this one test, because
 * a colour recognised in one place and not the other would write a color-shaped value into
 * a property the writer then refuses.
 */
bool ForgeMindMaterial::IsLinearColorStruct(const UScriptStruct* Struct)
{
	return Struct != nullptr && Struct->GetName().Equals(TEXT("LinearColor"));
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

bool ForgeMindMaterial::ConnectOutputInput(UObject* Asset, FExpressionInput* Input, UMaterialExpression* Source, int32 OutputIndex, FString& OutError, bool bNamedOutput)
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

	// A pin name that matched one of the source's own outputs names a real pin,
	// whatever its index. DecalDerivative declares DDX at 0 and DDY at 1, so an
	// index inside the channel range still has to connect directly: reading DDY as
	// "the G channel of DDX" would build a different graph than the caller asked
	// for and still report success. A named request outside the declared list is
	// refused here instead of being reinterpreted as a channel.
	if (bNamedOutput)
	{
		if (OutputIndex < Source->GetOutputs().Num())
		{
			Input->Connect(OutputIndex, Source);
			return true;
		}
		OutError = FString::Printf(TEXT("output index %d is out of range: the source carries %d output(s)"), OutputIndex, Source->GetOutputs().Num());
		return false;
	}

	// Above the channel range an index can only be a named-output request (e.g. a
	// TextureSample's RGBA at 5): connect the engine's own named output directly
	// when the source declares that many, instead of rejecting the index.
	if (OutputIndex > 4)
	{
		if (OutputIndex < Source->GetOutputs().Num())
		{
			Input->Connect(OutputIndex, Source);
			return true;
		}
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
