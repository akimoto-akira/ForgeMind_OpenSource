// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// UMG Widget child management commands (add_widget_child,
// list_widget_children) - for Widget Blueprint designer hierarchy.
//
// Widget Blueprint stores its designer-time widget hierarchy in a
// UWidgetTree (accessible via UWidgetBlueprint::WidgetTree).  The root
// widget is a UCanvasPanel by default.  Child widgets are created via
// WidgetTree->ConstructWidget<T>() and attached to a parent panel via
// ParentPanel->AddChild(ChildWidget).

#include "ForgeMindBridgeClient.h"

#include "ForgeMindBlueprintUtils.h"

#include "Dom/JsonObject.h"
#include "Dom/JsonValue.h"
#include "Engine/Blueprint.h"
#include "Kismet2/BlueprintEditorUtils.h"
#include "Kismet2/KismetEditorUtilities.h"
#include "WidgetBlueprint.h"
#include "Blueprint/WidgetBlueprintGeneratedClass.h"
#include "Blueprint/UserWidget.h"
#include "Components/Widget.h"
#include "Components/CanvasPanel.h"
#include "Components/PanelWidget.h"
#include "Components/ScrollBox.h"
#include "Components/TextBlock.h"
#include "Components/VerticalBox.h"
#include "Components/Button.h"
#include "Components/SizeBox.h"
#include "Components/Border.h"
#include "Components/Image.h"
#include "Components/Spacer.h"
#include "Blueprint/WidgetTree.h"
#include "Misc/Guid.h"

using namespace ForgeMindBlueprintUtils;

namespace
{
	/**
	 * Resolve a UWidget subclass by short name or full path.
	 * Supports common UMG widget classes: TextBlock, ScrollBox, VerticalBox,
	 * Button, SizeBox, Border, Image, Spacer, CanvasPanel, etc.
	 */
	UClass* ResolveWidgetClass(const FString& ClassName)
	{
		// Try as a full asset path first (e.g. /Script/UMG.TextBlock)
		if (ClassName.StartsWith(TEXT("/")))
		{
			UClass* Found = LoadObject<UClass>(nullptr, *ClassName);
			if (Found != nullptr && Found->IsChildOf(UWidget::StaticClass()))
			{
				return Found;
			}
		}

		// Try a loaded-object name search, then a U-prefixed variant
		// (e.g. "TextBlock" -> "UTextBlock").
		UClass* Found = ForgeMindFindFirstObject<UClass>(*ClassName);
		if (Found != nullptr && Found->IsChildOf(UWidget::StaticClass()))
		{
			return Found;
		}

		const FString Prefixed = TEXT("U") + ClassName;
		Found = ForgeMindFindFirstObject<UClass>(*Prefixed);
		if (Found != nullptr && Found->IsChildOf(UWidget::StaticClass()))
		{
			return Found;
		}

		return nullptr;
	}

	/**
	 * Recursively collect all widgets in the tree rooted at the given widget.
	 */
	void CollectWidgets(UWidget* Widget, TArray<UWidget*>& OutWidgets)
	{
		if (Widget == nullptr)
		{
			return;
		}
		OutWidgets.Add(Widget);

		// If this is a panel widget, recurse into its children
		if (UPanelWidget* Panel = Cast<UPanelWidget>(Widget))
		{
			for (int32 i = 0; i < Panel->GetChildrenCount(); ++i)
			{
				CollectWidgets(Panel->GetChildAt(i), OutWidgets);
			}
		}
	}

	/**
	 * Build a JSON description of a widget node.
	 */
	TSharedRef<FJsonObject> WidgetToJson(UWidget* Widget)
	{
		TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
		Entry->SetStringField(TEXT("name"), Widget->GetFName().ToString());
		Entry->SetStringField(TEXT("class"), Widget->GetClass()->GetName());

		// Check if this widget has children (panel widget)
		if (UPanelWidget* Panel = Cast<UPanelWidget>(Widget))
		{
			Entry->SetNumberField(TEXT("childCount"), Panel->GetChildrenCount());
		}
		else
		{
			Entry->SetNumberField(TEXT("childCount"), 0);
		}

		return Entry;
	}
}

// ---- add_widget_child ----

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleAddWidgetChild(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString ClassName;
	FString WidgetName;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("class"), ClassName) || ClassName.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("name"), WidgetName) || WidgetName.IsEmpty())
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (widget blueprint asset path), class (widget class name, e.g. TextBlock, ScrollBox, VerticalBox), name (widget name); optional: parent (parent widget name, defaults to root)"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Blueprint '%s' not found"), *AssetPath));
		return Result;
	}

	// Must be a Widget Blueprint
	UWidgetBlueprint* WidgetBP = Cast<UWidgetBlueprint>(Blueprint);
	if (WidgetBP == nullptr || WidgetBP->WidgetTree == nullptr)
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), TEXT("This blueprint is not a Widget Blueprint (must have a Widget Tree)"));
		return Result;
	}

	UClass* WidgetClass = ResolveWidgetClass(ClassName);
	if (WidgetClass == nullptr)
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("'%s' is not a valid widget class (must derive from UWidget, e.g. TextBlock, ScrollBox, VerticalBox)"), *ClassName));
		return Result;
	}

	// Check for duplicate name
	UWidget* Existing = WidgetBP->WidgetTree->FindWidget(FName(*WidgetName));
	if (Existing != nullptr)
	{
		// If force flag is set, just promote the existing widget to variable
		// and return success instead of erroring.
		bool bForce = false;
		Payload->TryGetBoolField(TEXT("force"), bForce);
		if (bForce)
		{
			bool bPromote2 = true;
			Payload->TryGetBoolField(TEXT("promote"), bPromote2);
			if (bPromote2)
			{
				Existing->bIsVariable = true;
			}
			Blueprint->MarkPackageDirty();
			FBlueprintEditorUtils::MarkBlueprintAsStructurallyModified(Blueprint);
			FKismetEditorUtilities::CompileBlueprint(Blueprint);
			Result->SetBoolField(TEXT("added"), true);
			Result->SetBoolField(TEXT("dirty"), Blueprint->GetPackage()->IsDirty());
			Result->SetStringField(TEXT("name"), WidgetName);
			Result->SetStringField(TEXT("class"), Existing->GetClass()->GetName());
			Result->SetStringField(TEXT("note"), TEXT("Widget already existed; bIsVariable was set to true (force mode)"));
			return Result;
		}
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("A widget named '%s' already exists in this Widget Blueprint"), *WidgetName));
		return Result;
	}

	// Create the widget via the WidgetTree
	UWidget* NewWidget = WidgetBP->WidgetTree->ConstructWidget<UWidget>(WidgetClass, FName(*WidgetName));
	if (NewWidget == nullptr)
	{
		Result->SetBoolField(TEXT("added"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Failed to create widget of class '%s'"), *ClassName));
		return Result;
	}

	// Promote to variable so the widget is accessible from the Blueprint
	// graph via a Get node (like dragging from the Hierarchy panel with
	// "Promote to Variable" checked). When bIsVariable is true, the compiler
	// creates an FObjectProperty on the generated class named after the widget.
	bool bPromote = true;
	if (Payload->TryGetBoolField(TEXT("promote"), bPromote))
	{
		// explicit override
	}
	NewWidget->bIsVariable = bPromote;

	// Resolve the parent widget. If not specified, attach to the root widget.
	FString ParentName;
	UWidget* ParentWidget = nullptr;

	// Get the root widget
	UWidget* RootWidget = WidgetBP->WidgetTree->RootWidget.Get();

	if (Payload->TryGetStringField(TEXT("parent"), ParentName) && !ParentName.IsEmpty())
	{
		// Find the named parent widget in the tree
		ParentWidget = WidgetBP->WidgetTree->FindWidget(FName(*ParentName));
		if (ParentWidget == nullptr)
		{
			Result->SetBoolField(TEXT("added"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Parent widget '%s' not found in the widget tree"), *ParentName));
			return Result;
		}
	}
	else
	{
		// Default to the root widget
		ParentWidget = RootWidget;
	}

	// If there is no root widget yet, set this widget as the root.
	if (RootWidget == nullptr)
	{
		WidgetBP->WidgetTree->RootWidget = NewWidget;
	}
	// Attach to the parent. The parent must be a panel widget.
	else if (UPanelWidget* ParentPanel = Cast<UPanelWidget>(ParentWidget))
	{
		ParentPanel->AddChild(NewWidget);
	}
	else
	{
		// Parent is not a panel — try the root widget as fallback
		if (UPanelWidget* RootPanel = Cast<UPanelWidget>(RootWidget))
		{
			RootPanel->AddChild(NewWidget);
		}
		else
		{
			// Root is not a panel widget (e.g. Image, TextBlock).
			// Auto-create a CanvasPanel as the new root, move the old
			// root under it, then add the new widget as a sibling.
			UCanvasPanel* NewRoot = WidgetBP->WidgetTree->ConstructWidget<UCanvasPanel>(UCanvasPanel::StaticClass(), TEXT("RootCanvasPanel"));
			if (NewRoot != nullptr)
			{
				WidgetBP->WidgetTree->RootWidget = NewRoot;
				NewRoot->AddChild(RootWidget);
				NewRoot->AddChild(NewWidget);
			}
			else
			{
				Result->SetBoolField(TEXT("added"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Parent widget '%s' is not a panel widget and cannot hold children"), *ParentName));
				return Result;
			}
		}
	}

	// Mark the blueprint as modified and compile
	Blueprint->MarkPackageDirty();
	FBlueprintEditorUtils::MarkBlueprintAsStructurallyModified(Blueprint);
	FKismetEditorUtilities::CompileBlueprint(Blueprint);

	Result->SetBoolField(TEXT("added"), true);
	Result->SetBoolField(TEXT("dirty"), Blueprint->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("name"), WidgetName);
	Result->SetStringField(TEXT("class"), WidgetClass->GetName());
	Result->SetStringField(TEXT("parent"), ParentWidget != nullptr ? ParentWidget->GetFName().ToString() : TEXT(""));
	return Result;
}

// ---- list_widget_children ----

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleListWidgetChildren(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty())
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required field: path (widget blueprint asset path)"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Blueprint '%s' not found"), *AssetPath));
		return Result;
	}

	UWidgetBlueprint* WidgetBP = Cast<UWidgetBlueprint>(Blueprint);
	if (WidgetBP == nullptr || WidgetBP->WidgetTree == nullptr)
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), TEXT("This blueprint is not a Widget Blueprint"));
		return Result;
	}

	// Collect all widgets in the tree
	TArray<UWidget*> AllWidgets;
	WidgetBP->WidgetTree->GetAllWidgets(AllWidgets);

	// Also get the root widget separately
	UWidget* RootWidget = WidgetBP->WidgetTree->RootWidget.Get();

	Result->SetBoolField(TEXT("found"), true);
	Result->SetStringField(TEXT("path"), AssetPath);
	Result->SetStringField(TEXT("rootWidget"), RootWidget != nullptr ? RootWidget->GetFName().ToString() : TEXT(""));
	Result->SetNumberField(TEXT("count"), AllWidgets.Num());

	TArray<TSharedPtr<FJsonValue>> WidgetsArray;
	for (UWidget* Widget : AllWidgets)
	{
		if (Widget != nullptr)
		{
			WidgetsArray.Add(MakeShareable(new FJsonValueObject(WidgetToJson(Widget))));
		}
	}
	Result->SetArrayField(TEXT("widgets"), WidgetsArray);

	return Result;
}