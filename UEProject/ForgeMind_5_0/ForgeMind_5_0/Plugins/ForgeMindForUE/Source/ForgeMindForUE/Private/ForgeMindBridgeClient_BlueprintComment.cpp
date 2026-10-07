// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// P9: Comment box commands (add_comment_box, move_nodes_to_comment,
// remove_nodes_from_comment) - split out of BlueprintGraph.cpp.
//
// Design: NodesUnderComment is the source of truth for which nodes belong
// to a comment box. After any membership change (add/remove), ArrangeGraph
// is called to re-layout the pinned nodes; its tail pass re-fits every
// comment box to snugly wrap its members (or shrink to a minimum default
// size when empty). This keeps the box's geometry in sync with its member
// list without fighting the engine's own geometry-based selection rebuild.

#include "ForgeMindBridgeClient.h"

#include "ForgeMindBlueprintUtils.h"
#include "ForgeMindGraphLayout.h"

#include "Dom/JsonObject.h"
#include "Dom/JsonValue.h"
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphNode.h"
#include "EdGraphNode_Comment.h"
#include "EdGraphSchema_K2.h"
#include "Engine/Blueprint.h"
#include "Kismet2/BlueprintEditorUtils.h"

using namespace ForgeMindBlueprintUtils;

namespace
{
	/** Finds a comment box by its node id string across the blueprint. */
	UEdGraphNode_Comment* FindCommentBox(UBlueprint* Blueprint, const FString& NodeIdText)
	{
		FGuid Guid;
		if (!FGuid::Parse(NodeIdText, Guid))
		{
			return nullptr;
		}
		UEdGraphNode* Node = FindNodeByGuid(Blueprint, Guid);
		return Cast<UEdGraphNode_Comment>(Node);
	}
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleAddCommentBox(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString Title;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("title"), Title) || Title.IsEmpty())
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path), title (comment box text); optional nodes (array of node ids to wrap), graphName (target graph), x/y (position, defaults to 0,0), width/height (defaults to 400x300)"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s'"), *AssetPath));
		return Result;
	}

	FString GraphName;
	Payload->TryGetStringField(TEXT("graphName"), GraphName);
	UEdGraph* Graph = nullptr;
	{
		TArray<FString> Available;
		auto Match = [&GraphName, &Available](const TArray<TObjectPtr<UEdGraph>>& Graphs) -> UEdGraph*
		{
			for (UEdGraph* G : Graphs)
			{
				if (G == nullptr) { continue; }
				Available.Add(G->GetName());
				if (!GraphName.IsEmpty() && G->GetName() == GraphName) { return G; }
			}
			return nullptr;
		};
		Graph = Match(Blueprint->UbergraphPages);
		if (Graph == nullptr) { Graph = Match(Blueprint->FunctionGraphs); }
		if (Graph == nullptr) { Graph = Match(Blueprint->MacroGraphs); }
		if (Graph == nullptr && GraphName.IsEmpty())
		{
			for (UEdGraph* G : Blueprint->UbergraphPages)
			{
				if (G != nullptr) { Graph = G; break; }
			}
		}
		if (Graph == nullptr)
		{
			Result->SetBoolField(TEXT("created"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No graph found '%s'; available: %s"), *GraphName, *FString::Join(Available, TEXT(", "))));
			return Result;
		}
	}

	int32 PosX = 0;
	int32 PosY = 0;
	int32 Width = 400;
	int32 Height = 300;
	double NumberValue = 0.0;
	if (Payload->TryGetNumberField(TEXT("x"), NumberValue)) { PosX = static_cast<int32>(NumberValue); }
	if (Payload->TryGetNumberField(TEXT("y"), NumberValue)) { PosY = static_cast<int32>(NumberValue); }
	if (Payload->TryGetNumberField(TEXT("width"), NumberValue)) { Width = static_cast<int32>(NumberValue); }
	if (Payload->TryGetNumberField(TEXT("height"), NumberValue)) { Height = static_cast<int32>(NumberValue); }

	UEdGraphNode_Comment* CommentNode = NewObject<UEdGraphNode_Comment>(Graph);
	CommentNode->CreateNewGuid();
	CommentNode->PostPlacedNewNode();
	CommentNode->MoveMode = ECommentBoxMode::GroupMovement;
	CommentNode->OnRenameNode(Title);
	CommentNode->NodePosX = PosX;
	CommentNode->NodePosY = PosY;
	CommentNode->NodeWidth = Width;
	CommentNode->NodeHeight = Height;
	Graph->AddNode(CommentNode);

	// Optionally wrap specified nodes under the comment box.
	TArray<FString> WrappedNodeIds;
	const TArray<TSharedPtr<FJsonValue>>* NodesArray = nullptr;
	if (Payload->TryGetArrayField(TEXT("nodes"), NodesArray))
	{
		for (const TSharedPtr<FJsonValue>& NodeValue : *NodesArray)
		{
			FString NodeIdText;
			if (!NodeValue.IsValid() || !NodeValue->TryGetString(NodeIdText) || NodeIdText.IsEmpty())
			{
				continue;
			}
			FGuid Guid;
			if (!FGuid::Parse(NodeIdText, Guid))
			{
				continue;
			}
			UEdGraphNode* TargetNode = FindNodeByGuid(Blueprint, Guid);
			if (TargetNode == nullptr)
			{
				continue;
			}
			CommentNode->AddNodeUnderComment(TargetNode);
			WrappedNodeIds.Add(NodeIdText);
		}
		// Trigger a full layout pass: ArrangeGraph re-positions pinned
		// nodes and its tail pass re-fits every comment box (including
		// this one) to snugly wrap its members.
		FForgeMindGraphLayout::ArrangeGraph(Graph);
	}

	FBlueprintEditorUtils::MarkBlueprintAsStructurallyModified(Blueprint);
	CompileAndCollectDiagnostics(Blueprint, Result);

	Result->SetBoolField(TEXT("created"), true);
	Result->SetStringField(TEXT("nodeId"), CommentNode->NodeGuid.ToString());
	Result->SetStringField(TEXT("nodeClass"), CommentNode->GetClass()->GetName());
	Result->SetStringField(TEXT("graph"), Graph->GetName());
	Result->SetNumberField(TEXT("posX"), CommentNode->NodePosX);
	Result->SetNumberField(TEXT("posY"), CommentNode->NodePosY);
	Result->SetNumberField(TEXT("width"), CommentNode->NodeWidth);
	Result->SetNumberField(TEXT("height"), CommentNode->NodeHeight);
	if (WrappedNodeIds.Num() > 0)
	{
		TArray<TSharedPtr<FJsonValue>> WrappedValues;
		for (const FString& Id : WrappedNodeIds)
		{
			WrappedValues.Add(MakeShared<FJsonValueString>(Id));
		}
		Result->SetArrayField(TEXT("wrappedNodes"), WrappedValues);
	}
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleMoveNodesToComment(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString CommentIdText;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("comment"), CommentIdText) || CommentIdText.IsEmpty())
	{
		Result->SetBoolField(TEXT("moved"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path), comment (comment box node id), nodes (array of node ids to add under the comment)"));
		return Result;
	}

	const TArray<TSharedPtr<FJsonValue>>* NodesArray = nullptr;
	if (!Payload->TryGetArrayField(TEXT("nodes"), NodesArray) || NodesArray->Num() == 0)
	{
		Result->SetBoolField(TEXT("moved"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required field: nodes (array of node ids to add under the comment)"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("moved"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s'"), *AssetPath));
		return Result;
	}

	UEdGraphNode_Comment* Comment = FindCommentBox(Blueprint, CommentIdText);
	if (Comment == nullptr)
	{
		Result->SetBoolField(TEXT("moved"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Comment box '%s' not found - re-read the graph via list_blueprint_graph; it must be an EdGraphNode_Comment"), *CommentIdText));
		return Result;
	}

	TArray<FString> AddedNodeIds;
	for (const TSharedPtr<FJsonValue>& NodeValue : *NodesArray)
	{
		FString NodeIdText;
		if (!NodeValue.IsValid() || !NodeValue->TryGetString(NodeIdText) || NodeIdText.IsEmpty())
		{
			continue;
		}
		FGuid Guid;
		if (!FGuid::Parse(NodeIdText, Guid))
		{
			continue;
		}
		UEdGraphNode* TargetNode = FindNodeByGuid(Blueprint, Guid);
		if (TargetNode == nullptr)
		{
			continue;
		}
		Comment->AddNodeUnderComment(TargetNode);
		AddedNodeIds.Add(NodeIdText);
	}

	// Membership changed: trigger ArrangeGraph which re-layouts pinned nodes
	// and re-fits all comment boxes to their member set. The comment box
	// will snugly wrap its new members.
	FForgeMindGraphLayout::ArrangeGraph(Comment->GetGraph());

	Blueprint->Modify();
	CompileAndCollectDiagnostics(Blueprint, Result);

	Result->SetBoolField(TEXT("moved"), true);
	Result->SetNumberField(TEXT("addedCount"), AddedNodeIds.Num());
	if (AddedNodeIds.Num() > 0)
	{
		TArray<TSharedPtr<FJsonValue>> AddedValues;
		for (const FString& Id : AddedNodeIds)
		{
			AddedValues.Add(MakeShared<FJsonValueString>(Id));
		}
		Result->SetArrayField(TEXT("addedNodes"), AddedValues);
	}
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleRemoveNodesFromComment(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString AssetPath;
	FString CommentIdText;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("comment"), CommentIdText) || CommentIdText.IsEmpty())
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: path (blueprint asset path), comment (comment box node id), nodes (array of node ids to remove from the comment)"));
		return Result;
	}

	const TArray<TSharedPtr<FJsonValue>>* NodesArray = nullptr;
	if (!Payload->TryGetArrayField(TEXT("nodes"), NodesArray) || NodesArray->Num() == 0)
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required field: nodes (array of node ids to remove from the comment)"));
		return Result;
	}

	UBlueprint* Blueprint = ResolveBlueprint(AssetPath);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No blueprint found at '%s'"), *AssetPath));
		return Result;
	}

	UEdGraphNode_Comment* Comment = FindCommentBox(Blueprint, CommentIdText);
	if (Comment == nullptr)
	{
		Result->SetBoolField(TEXT("removed"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Comment box '%s' not found - re-read the graph via list_blueprint_graph"), *CommentIdText));
		return Result;
	}

	// Collect the nodes to remove, then rebuild the NodesUnderComment list
	// without them (UEdGraphNode_Comment has no single-remove API; clear +
	// re-add is the clean path).
	TSet<UObject*> ToRemove;
	TArray<FString> RemovedNodeIds;
	for (const TSharedPtr<FJsonValue>& NodeValue : *NodesArray)
	{
		FString NodeIdText;
		if (!NodeValue.IsValid() || !NodeValue->TryGetString(NodeIdText) || NodeIdText.IsEmpty())
		{
			continue;
		}
		FGuid Guid;
		if (!FGuid::Parse(NodeIdText, Guid))
		{
			continue;
		}
		UEdGraphNode* TargetNode = FindNodeByGuid(Blueprint, Guid);
		if (TargetNode == nullptr)
		{
			continue;
		}
		ToRemove.Add(TargetNode);
		RemovedNodeIds.Add(NodeIdText);
	}

	const FCommentNodeSet& CurrentMembers = Comment->GetNodesUnderComment();
	TArray<UObject*> Remaining;
	for (UObject* Member : CurrentMembers)
	{
		if (!ToRemove.Contains(Member))
		{
			Remaining.Add(Member);
		}
	}
	Comment->ClearNodesUnderComment();
	for (UObject* Member : Remaining)
	{
		Comment->AddNodeUnderComment(Member);
	}

	// Membership changed: trigger ArrangeGraph which re-layouts pinned nodes
	// and re-fits all comment boxes. If the box is now empty, ArrangeGraph's
	// tail pass gives it a minimum visible size (200x100).
	FForgeMindGraphLayout::ArrangeGraph(Comment->GetGraph());

	Blueprint->Modify();
	CompileAndCollectDiagnostics(Blueprint, Result);

	Result->SetBoolField(TEXT("removed"), true);
	Result->SetNumberField(TEXT("removedCount"), RemovedNodeIds.Num());
	if (RemovedNodeIds.Num() > 0)
	{
		TArray<TSharedPtr<FJsonValue>> RemovedValues;
		for (const FString& Id : RemovedNodeIds)
		{
			RemovedValues.Add(MakeShared<FJsonValueString>(Id));
		}
		Result->SetArrayField(TEXT("removedNodes"), RemovedValues);
	}
	return Result;
}