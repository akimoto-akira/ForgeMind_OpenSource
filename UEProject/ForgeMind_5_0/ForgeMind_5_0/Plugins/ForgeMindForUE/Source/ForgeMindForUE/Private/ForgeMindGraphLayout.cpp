// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#include "ForgeMindGraphLayout.h"

#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphNode.h"
#include "EdGraph/EdGraphPin.h"
#include "EdGraphSchema_K2.h"
#include "EdGraphNode_Comment.h"
#include "Framework/Commands/UICommandInfo.h"
#include "InputCoreTypes.h"
#include "K2Node_CallFunction.h"
#include "K2Node_CustomEvent.h"
#include "K2Node_Event.h"
#include "K2Node_InputAction.h"
#include "K2Node_InputAxisEvent.h"
#include "K2Node_MakeArray.h"
#include "K2Node_MakeMap.h"
#include "K2Node_MakeSet.h"
#include "K2Node_MakeStruct.h"
#include "K2Node_VariableGet.h"
#include "Layout/ChildrenBase.h"
#include "Materials/MaterialExpression.h"
#include "Materials/MaterialExpressionComment.h"
#include "MaterialGraph/MaterialGraph.h"
#include "MaterialGraph/MaterialGraphSchema.h"
#include "MaterialGraph/MaterialGraphNode.h"
#include "MaterialGraph/MaterialGraphNode_Root.h"
#include "SGraphPanel.h"
#include "ScopedTransaction.h"
#include "Widgets/InvalidateWidgetReason.h"
#include "Widgets/SWidget.h"

#include "ForgeMindGraphCommands.h"

namespace
{
	/**
	 * The feature applies to any graph that is backed by a concrete schema.
	 * This covers all blueprint-based graphs (event graphs, functions,
	 * macros, animation blueprints) as well as non-blueprint graph editors
	 * that share the same UEdGraph infrastructure: material graphs,
	 * material functions, sound cues, behavior trees, gameplay abilities,
	 * and similar. The ArrangeGraph algorithm is purely structural - it
	 * only reads pins, wires, and node positions - so it works identically
	 * across every graph type as long as a schema is set.
	 */
	bool IsEditableGraph(const UEdGraph* Graph)
	{
		if (Graph == nullptr)
		{
			return false;
		}
		// A graph without a schema cannot be edited - this filters out
		// transient or internal scratch graphs that have no editor UI.
		return Graph->GetSchema() != nullptr;
	}

	/**
	 * Returns true when the graph is a material graph (or material function
	 * graph).  Material graphs have no execution wires - every connection is
	 * a data wire - so the layout algorithm must use data-dependency edges
	 * instead of exec chains to build the node hierarchy.
	 */
	bool IsMaterialGraph(const UEdGraph* Graph)
	{
		if (Graph == nullptr)
		{
			return false;
		}
		const UEdGraphSchema* Schema = Graph->GetSchema();
		return Schema != nullptr && Schema->IsA(UMaterialGraphSchema::StaticClass());
	}

	/**
	 * Returns true if the node is any kind of material graph node
	 * (UMaterialGraphNode, UMaterialGraphNode_Root, UMaterialGraphNode_Composite,
	 * UMaterialGraphNode_Custom, UMaterialGraphNode_PinBase).  These all derive
	 * from UMaterialGraphNode_Base.
	 */
	bool IsMaterialNode(const UEdGraphNode* Node)
	{
		return Cast<UMaterialGraphNode_Base>(Node) != nullptr;
	}

	/**
	 * Returns true if the node is the material output root node - the
	 * rightmost sink in a material graph that collects all material inputs.
	 */
	bool IsMaterialRootNode(const UEdGraphNode* Node)
	{
		return Cast<UMaterialGraphNode_Root>(Node) != nullptr;
	}

	/**
	 * Returns true if the pin is an exec pin, regardless of whether the
	 * graph is a blueprint graph (UEdGraphSchema_K2::PC_Exec) or a material
	 * graph (UMaterialGraphSchema::PC_Exec).  Both resolve to FName("exec"),
	 * but comparing against the literal avoids depending on that equality.
	 */
	bool IsExecPin(const UEdGraphPin* Pin)
	{
		return Pin != nullptr
			&& (Pin->PinType.PinCategory == UEdGraphSchema_K2::PC_Exec
				|| Pin->PinType.PinCategory == UMaterialGraphSchema::PC_Exec);
	}

	/**
	 * Locate the graph panel whose keyboard focus matches the pressed key.
	 * First walks up from the focused widget (covers focus on a node or the
	 * panel itself), then searches the focused subtree (covers focus on the
	 * wrapping graph editor widget). Widgets are matched by their Slate type
	 * name since Slate has no RTTI.
	 */
	SGraphPanel* FindFocusedGraphPanel()
	{
		static const FName GraphPanelTypeName(TEXT("SGraphPanel"));

		TSharedPtr<SWidget> Focused = FSlateApplication::Get().GetKeyboardFocusedWidget();
		if (!Focused.IsValid())
		{
			return nullptr;
		}

		// Upwards: the focused widget may sit inside the panel.
		TArray<TSharedPtr<SWidget>> SearchRoots;
		for (TSharedPtr<SWidget> Walk = Focused; Walk.IsValid(); Walk = Walk->GetParentWidget())
		{
			if (Walk->GetType() == GraphPanelTypeName)
			{
				return StaticCastSharedPtr<SGraphPanel>(Walk).Get();
			}
			SearchRoots.Add(Walk);
		}

		// Downwards: the panel is a child of the focused editor widget.
		// Search from the shallowest root first (closest to the focus).
		for (int32 RootIndex = SearchRoots.Num() - 1; RootIndex >= 0; --RootIndex)
		{
			TArray<TSharedPtr<SWidget>> Pending;
			Pending.Push(SearchRoots[RootIndex]);
			int32 Visited = 0;
			while (Pending.Num() > 0 && Visited < 5000)
			{
				TSharedPtr<SWidget> Current = Pending.Pop();
				++Visited;
				const FVector2D PanelSize = Current->GetPaintSpaceGeometry().GetLocalSize();
				if (Current->GetType() == GraphPanelTypeName && PanelSize.X > 0.0f && PanelSize.Y > 0.0f)
				{
					return StaticCastSharedPtr<SGraphPanel>(Current).Get();
				}
				if (const FChildren* Children = Current->GetChildren())
				{
					for (int32 ChildIndex = 0; ChildIndex < Children->Num(); ++ChildIndex)
					{
						// GetChildAt hands back a const ref; the traversal
						// only reads geometry/type, so dropping const is safe.
						Pending.Push(ConstCastSharedRef<SWidget>(Children->GetChildAt(ChildIndex)));
					}
				}
			}
		}

		return nullptr;
	}

	/** Horizontal gap between a node and its downstream column. */
	const int32 GapX = 64;
	/** Vertical gap between stacked siblings. */
	const int32 GapY = 32;
	/** Vertical gap between independent root blocks. */
	const int32 BlockGap = 64;

	/** Size estimates for nodes that were never rendered (width/height 0). */
	const int32 FallbackWidth = 200;
	const int32 EstimatedTitleHeight = 40;
	const int32 EstimatedPinRow = 20;

	/** Measured width when rendered, else a conservative fallback. */
	int32 GetNodeWidth(const UEdGraphNode* Node)
	{
		return Node->NodeWidth > 0 ? Node->NodeWidth : FallbackWidth;
	}

	/** Measured height when rendered, else title bar + one row per pin. */
	int32 GetNodeHeight(const UEdGraphNode* Node)
	{
		return Node->NodeHeight > 0
			? Node->NodeHeight
			: EstimatedTitleHeight + Node->Pins.Num() * EstimatedPinRow;
	}

	/**
	 * Returns true if the node is a pure data node - one that has no
	 * exec pins and only produces data.  In blueprint graphs these are
	 * pure function calls, Make array / map / set / struct, and variable
	 * getters.  In material graphs every expression node is a pure data
	 * node (the only non-data node is the Material Root output sink).
	 * These nodes are laid out to the LEFT of the node that consumes
	 * their output, stacked vertically in declaration order.
	 */
	bool IsPureDataNode(const UEdGraphNode* Node)
	{
		if (Node == nullptr)
		{
			return false;
		}
		// Material graph: every expression node is pure data.  The Root
		// node (output sink) is NOT pure - it is the layout root.
		if (IsMaterialNode(Node))
		{
			return !IsMaterialRootNode(Node);
		}
		// Quick filter: any exec pin means it is not pure.
		for (const UEdGraphPin* Pin : Node->Pins)
		{
			if (Pin != nullptr && IsExecPin(Pin))
			{
				return false;
			}
		}
		return Cast<UK2Node_CallFunction>(Node) != nullptr
			|| Cast<UK2Node_MakeArray>(Node) != nullptr
			|| Cast<UK2Node_MakeMap>(Node) != nullptr
			|| Cast<UK2Node_MakeSet>(Node) != nullptr
			|| Cast<UK2Node_MakeStruct>(Node) != nullptr
			|| Cast<UK2Node_VariableGet>(Node) != nullptr;
	}

	/**
	 * Returns true if the node is any kind of event node - the entry
	 * point of an execution chain (delegate events, custom events, input
	 * events, Enhanced Input action events).  Children of event nodes get
	 * a small downward nudge so the exec wire visually clears the event
	 * node's title bar.
	 */
	bool IsEventNode(const UEdGraphNode* Node)
	{
		return Cast<UK2Node_Event>(Node) != nullptr
			|| Cast<UK2Node_CustomEvent>(Node) != nullptr
			|| Cast<UK2Node_InputAxisEvent>(Node) != nullptr
			|| Cast<UK2Node_InputAction>(Node) != nullptr;
	}

	/**
	 * Returns the widest left-side data provider width for a node, or 0
	 * if it has none.  Used to reserve horizontal space in the exec chain
	 * so left-side data nodes don't overlap the preceding exec node.
	 */
	int32 GetMaxDataProviderWidth(UEdGraphNode* Node,
		const TMap<UEdGraphNode*, TArray<UEdGraphNode*>>& DataProvidersByConsumer)
	{
		const TArray<UEdGraphNode*>* Providers = DataProvidersByConsumer.Find(Node);
		if (Providers == nullptr)
		{
			return 0;
		}
		int32 MaxW = 0;
		for (UEdGraphNode* Provider : *Providers)
		{
			if (Provider != nullptr)
			{
				MaxW = FMath::Max(MaxW, GetNodeWidth(Provider));
			}
		}
		return MaxW;
	}

	/**
	 * Vertical offset of a pin relative to its node top. Always estimated:
	 * pins stack row by row under the title bar in declaration order.
	 */
	int32 GetPinOffsetY(const UEdGraphNode* Node, const UEdGraphPin* Pin)
	{
		int32 Index = 0;
		for (const UEdGraphPin* Candidate : Node->Pins)
		{
			if (Candidate == Pin)
			{
				break;
			}
			++Index;
		}
		return EstimatedTitleHeight + Index * EstimatedPinRow;
	}

	/** Anchor record: which parent output pin owns a child's position, and
	 * the child input pin that wire lands in (needed to align the two ends). */
	struct FChildSlot
	{
		UEdGraphPin* Pin;
		UEdGraphPin* ChildPin;
		UEdGraphNode* Child;
	};

	/**
	 * Snapshot of each comment box's member list, captured BEFORE node
	 * positions are written.  UEdGraphNode_Comment::GetNodesUnderComment()
	 * returns a list that is rebuilt from geometric containment whenever
	 * the comment box is selected in the UI.  In blueprint graphs this
	 * list is reliably populated, but in material graphs the list may be
	 * empty if the comment box was never selected after creation.  As a
	 * fallback we detect members by geometric containment — the same
	 * test SGraphNodeComment::IsNodeUnderComment uses — so the snapshot
	 * is correct regardless of graph type.
	 */
	struct FCommentSnapshot
	{
		/** The comment boxes that were present, in graph order. */
		TArray<UEdGraphNode_Comment*> Comments;
		/** For each comment box (same index), the member nodes at snapshot
		 *  time.  Stored separately so we can iterate after positions change. */
		TArray<TArray<UEdGraphNode*>> Members;
	};

	/**
	 * Returns true when Node's bounding rectangle is fully contained
	 * within Comment's bounding rectangle.  This mirrors the logic in
	 * SGraphNodeComment::IsNodeUnderComment so we get the same membership
	 * that the UI would compute.
	 */
	bool IsNodeGeometricallyInsideComment(const UEdGraphNode* Node, const UEdGraphNode_Comment* Comment)
	{
		if (Node == nullptr || Comment == nullptr)
		{
			return false;
		}
		// Comment box rectangle (NodePosX/Y is top-left, size is width/height).
		const int32 CLeft = Comment->NodePosX;
		const int32 CTop = Comment->NodePosY;
		const int32 CRight = CLeft + (Comment->NodeWidth > 0 ? Comment->NodeWidth : 200);
		const int32 CBottom = CTop + (Comment->NodeHeight > 0 ? Comment->NodeHeight : 100);

		// Node rectangle.
		const int32 NLeft = Node->NodePosX;
		const int32 NTop = Node->NodePosY;
		const int32 NRight = NLeft + (Node->NodeWidth > 0 ? Node->NodeWidth : FallbackWidth);
		const int32 NBottom = NTop + (Node->NodeHeight > 0 ? Node->NodeHeight : GetNodeHeight(Node));

		// Containment test: node rect must be fully inside comment rect.
		return NLeft >= CLeft && NTop >= CTop && NRight <= CRight && NBottom <= CBottom;
	}

	/**
	 * Capture the current member list of every comment box in the graph.
	 * Must be called BEFORE any node positions are modified.
	 *
	 * Two strategies are combined:
	 *  1. If GetNodesUnderComment() returns a non-empty list, use it
	 *     (reliable in blueprint graphs where the UI populates the list).
	 *  2. Otherwise, fall back to geometric containment detection —
	 *     scan every pinned node and test whether its rectangle is fully
	 *     inside the comment box.  This handles material graphs where
	 *     the member list may not have been populated yet.
	 */
	FCommentSnapshot SnapshotCommentMembers(UEdGraph* Graph)
	{
		FCommentSnapshot Snap;
		for (UEdGraphNode* Node : Graph->Nodes)
		{
			UEdGraphNode_Comment* Comment = Cast<UEdGraphNode_Comment>(Node);
			if (Comment == nullptr)
			{
				continue;
			}

			TArray<UEdGraphNode*> MemberNodes;

			// Strategy 1: use the existing member list if populated.
			const FCommentNodeSet& Existing = Comment->GetNodesUnderComment();
			for (UObject* Member : Existing)
			{
				if (UEdGraphNode* MemberNode = Cast<UEdGraphNode>(Member))
				{
					MemberNodes.Add(MemberNode);
				}
			}

			// Strategy 2: if the list is empty, detect members by geometry.
			if (MemberNodes.Num() == 0)
			{
				for (UEdGraphNode* Candidate : Graph->Nodes)
				{
					// Skip null, self, and other comment boxes.
					if (Candidate == nullptr || Candidate == Comment)
					{
						continue;
					}
					// Skip comment boxes (they don't have pins and
					// shouldn't be tracked as members of another comment).
					if (Cast<UEdGraphNode_Comment>(Candidate) != nullptr)
					{
						continue;
					}
					if (IsNodeGeometricallyInsideComment(Candidate, Comment))
					{
						MemberNodes.Add(Candidate);
					}
				}
			}

			Snap.Comments.Add(Comment);
			Snap.Members.Add(MoveTemp(MemberNodes));
		}
		return Snap;
	}

	/**
	 * Re-fit every comment box to the new positions of its snapshot
	 * members.  Uses the snapshot taken before the layout move so that
	 * membership is preserved exactly - nodes that were inside stay
	 * inside, nodes that were outside stay outside.
	 */
	void RefitCommentBoxes(const FCommentSnapshot& Snap)
	{
		const int32 Padding = 32;
		const int32 TitleBar = 30;
		// UE's SGraphNodeComment uses geometric containment to decide which
		// nodes are "under" the box for drag-grouping. The bottom-most node's
		// interactive area (including its output pins) extends below
		// NodePosY + NodeHeight, so the box needs extra bottom clearance or
		// the last node won't be picked up when dragging the comment box.
		// Two pin-row heights (~28px each) is empirically sufficient.
		const int32 BottomPinAllowance = 56;
		const int32 MinBoxWidth = 200;
		const int32 MinBoxHeight = 100;

		for (int32 i = 0; i < Snap.Comments.Num(); ++i)
		{
			UEdGraphNode_Comment* Comment = Snap.Comments[i];
			const TArray<UEdGraphNode*>& MemberNodes = Snap.Members[i];

			int32 MinX = INT32_MAX;
			int32 MinY = INT32_MAX;
			int32 MaxX = INT32_MIN;
			int32 MaxY = INT32_MIN;
			for (UEdGraphNode* MemberNode : MemberNodes)
			{
				if (MemberNode == nullptr)
				{
					continue;
				}
				MinX = FMath::Min(MinX, MemberNode->NodePosX);
				MinY = FMath::Min(MinY, MemberNode->NodePosY);
				MaxX = FMath::Max(MaxX, MemberNode->NodePosX + (MemberNode->NodeWidth > 0 ? MemberNode->NodeWidth : 200));
				MaxY = FMath::Max(MaxY, MemberNode->NodePosY + (MemberNode->NodeHeight > 0 ? MemberNode->NodeHeight : 60));
			}

			Comment->Modify();
			if (MinX != INT32_MAX)
			{
				// Has members: wrap them snugly.
				Comment->NodePosX = MinX - Padding;
				Comment->NodePosY = MinY - Padding - TitleBar;
				Comment->NodeWidth = FMath::Max((MaxX - MinX) + 2 * Padding, MinBoxWidth);
				Comment->NodeHeight = FMath::Max((MaxY - MinY) + 2 * Padding + TitleBar + BottomPinAllowance, MinBoxHeight);
			}
			else
			{
				// No members: ensure at least a minimum visible size, keep position.
				Comment->NodeWidth = FMath::Max(Comment->NodeWidth, MinBoxWidth);
				Comment->NodeHeight = FMath::Max(Comment->NodeHeight, MinBoxHeight);
			}
		}
	}

	/**
	 * Follows the exec chain from a node and lays every downstream exec
	 * node out in a single horizontal line at the same Y, one column per
	 * node.  When a node has multiple exec output pins (e.g. IfThenElse's
	 * Then / Else), the first branch continues on the same line and each
	 * subsequent branch starts on a new line below the previous branch's
	 * full span.  After placing the node itself:
	 *  - Pure data nodes whose output feeds this node's inputs go LEFT.
	 *  - Pure data nodes whose output is dangling but whose input comes
	 *    from this node go RIGHT (behind the source).
	 *
	 * Returns the vertical span consumed below the node top.
	 */
	int32 LayoutSubtree(UEdGraphNode* Node, int32 X, int32 Y,
		const TMap<UEdGraphNode*, TArray<FChildSlot>>& ChildrenByParent,
		const TMap<UEdGraphNode*, TArray<UEdGraphNode*>>& DataProvidersByConsumer,
		const TMap<UEdGraphNode*, TArray<UEdGraphNode*>>& DataFollowersBySource,
		TSet<UEdGraphNode*>& Placed,
		TMap<UEdGraphNode*, FIntPoint>& OutPositions)
	{
		OutPositions.Add(Node, FIntPoint(X, Y));
		Placed.Add(Node);

		// Place pure data nodes that feed this node's data input pins,
		// stacked to the LEFT.  Sort order depends on the node type:
		//  - Two-sided data node (has both input source AND output
		//    consumer): ordered by the consumer's (this node's) input
		//    pin Y offset, so it aligns with where the data arrives.
		//  - Pure provider (input only, no output consumer): ordered by
		//    its own input source Y, so it follows the flow of data.
		int32 MaxProviderY = Y;
		if (const TArray<UEdGraphNode*>* DataProviders = DataProvidersByConsumer.Find(Node))
		{
			// Gather unplaced providers with their sort key.
			struct FDataProviderEntry
			{
				UEdGraphNode* Node;
				int32 SortKey;
			};
			TArray<FDataProviderEntry> Entries;
			for (UEdGraphNode* DataNode : *DataProviders)
			{
				if (DataNode == nullptr || Placed.Contains(DataNode))
				{
					continue;
				}
				// Check if this data node has an output consumer (two-sided).
				bool bHasOutputConsumer = false;
				for (const UEdGraphPin* OutPin : DataNode->Pins)
				{
					if (OutPin == nullptr || OutPin->Direction != EGPD_Output
						|| IsExecPin(OutPin))
					{
						continue;
					}
					for (const UEdGraphPin* Link : OutPin->LinkedTo)
					{
						UEdGraphNode* Consumer = Link != nullptr ? Link->GetOwningNode() : nullptr;
						if (Consumer != nullptr && Consumer != DataNode)
						{
							bHasOutputConsumer = true;
							break;
						}
					}
					if (bHasOutputConsumer)
					{
						break;
					}
				}

				int32 SortKey;
				if (bHasOutputConsumer)
				{
					// Two-sided: sort by the consumer's input pin Y offset
					// (the pin on this Node that the data feeds into).
					SortKey = Y;
					for (const UEdGraphPin* InPin : Node->Pins)
					{
						if (InPin == nullptr || InPin->Direction != EGPD_Input
							|| IsExecPin(InPin))
						{
							continue;
						}
						for (const UEdGraphPin* Link : InPin->LinkedTo)
						{
							UEdGraphNode* Provider = Link != nullptr ? Link->GetOwningNode() : nullptr;
							if (Provider == DataNode)
							{
								SortKey = Y + GetPinOffsetY(Node, InPin);
								break;
							}
						}
					}
				}
				else
				{
					// Pure provider: sort by input source Y.
					SortKey = DataNode->NodePosY;
					for (const UEdGraphPin* InPin : DataNode->Pins)
					{
						if (InPin == nullptr || InPin->Direction != EGPD_Input
							|| IsExecPin(InPin))
						{
							continue;
						}
						for (const UEdGraphPin* Link : InPin->LinkedTo)
						{
							UEdGraphNode* Source = Link != nullptr ? Link->GetOwningNode() : nullptr;
							if (Source != nullptr && Source != DataNode)
							{
								SortKey = Source->NodePosY;
								break;
							}
						}
						if (SortKey != DataNode->NodePosY)
						{
							break;
						}
					}
				}
				Entries.Add({ DataNode, SortKey });
			}
			Entries.Sort([](const FDataProviderEntry& Lhs, const FDataProviderEntry& Rhs)
			{
				return Lhs.SortKey < Rhs.SortKey;
			});

			int32 DataX = X - GapX;
			int32 DataY = Y;
			int32 PrevBottom = INT32_MIN;
			for (const FDataProviderEntry& Entry : Entries)
			{
				const int32 DataW = GetNodeWidth(Entry.Node);
				const int32 DataH = GetNodeHeight(Entry.Node);
				// Target Y: align to the consumer pin (SortKey) so the
				// wire stays near-horizontal and avoids crossing other
				// nodes.  Never overlap the previous data node.
				int32 TargetY = FMath::Max(Entry.SortKey, PrevBottom + (PrevBottom != INT32_MIN ? GapY : 0));
				OutPositions.Add(Entry.Node, FIntPoint(DataX - DataW, TargetY));
				Placed.Add(Entry.Node);
				PrevBottom = TargetY + DataH;
				DataY = FMath::Max(DataY, PrevBottom + GapY);
			}
			MaxProviderY = FMath::Max(DataY, PrevBottom);
		}

		// Place pure data nodes whose output is dangling but whose input
		// comes from this node, stacked to the RIGHT (behind the source).
		// Same sort-by-input-source-Y ordering as the left-side placement.
		// These followers DO inflate the returned span so the next root
		// node starts below this node's full visual extent (including
		// its right-side data children).
		int32 MaxFollowerY = Y;
		if (const TArray<UEdGraphNode*>* DataFollowers = DataFollowersBySource.Find(Node))
		{
			struct FDataFollowerEntry
			{
				UEdGraphNode* Node;
				int32 SortKey;
			};
			TArray<FDataFollowerEntry> FollowerEntries;
			for (UEdGraphNode* DataNode : *DataFollowers)
			{
				if (DataNode == nullptr || Placed.Contains(DataNode))
				{
					continue;
				}
				// Sort key: the source Y that feeds this data node (which
				// is this node itself, so use the node's Y).
				int32 SortKey = Y;
				FollowerEntries.Add({ DataNode, SortKey });
			}
			FollowerEntries.Sort([](const FDataFollowerEntry& Lhs, const FDataFollowerEntry& Rhs)
			{
				return Lhs.SortKey < Rhs.SortKey;
			});

			int32 FollowerX = X + GetNodeWidth(Node) + GapX;
			int32 FollowerY = Y;
			for (const FDataFollowerEntry& Entry : FollowerEntries)
			{
				OutPositions.Add(Entry.Node, FIntPoint(FollowerX, FollowerY));
				Placed.Add(Entry.Node);
				FollowerY += GetNodeHeight(Entry.Node) + GapY;
			}
			MaxFollowerY = FollowerY;
		}

		const TArray<FChildSlot>* Slots = ChildrenByParent.Find(Node);
		// Bottom accounts for the node height, left-side data providers,
		// AND right-side data followers - so the next root node starts
		// below this node's full visual extent.
		int32 Bottom = FMath::Max3(Y + GetNodeHeight(Node), MaxProviderY, MaxFollowerY);

		if (Slots == nullptr)
		{
			return Bottom - Y;
		}

		// Walk exec output pins in declaration order.  The first exec branch
		// continues on the same horizontal line; each additional exec branch
		// drops to a new line below the previous branch's span.
		bool bFirstExecBranch = true;
		int32 BranchTop = Y;
		for (const UEdGraphPin* Pin : Node->Pins)
		{
			if (Pin == nullptr || Pin->Direction != EGPD_Output)
			{
				continue;
			}
			// Only follow exec pins - data pins are not part of the
			// execution flow and are ignored for linear layout.
			if (!IsExecPin(Pin))
			{
				continue;
			}

			// Collect exec children anchored through this pin.
			TArray<FChildSlot> PinSlots;
			for (const FChildSlot& Slot : *Slots)
			{
				if (Slot.Pin == Pin && !Placed.Contains(Slot.Child))
				{
					PinSlots.Add(Slot);
				}
			}
			if (PinSlots.Num() == 0)
			{
				continue;
			}
			PinSlots.Sort([](const FChildSlot& Lhs, const FChildSlot& Rhs)
			{
				return Lhs.Child->NodePosY < Rhs.Child->NodePosY;
			});

			if (bFirstExecBranch)
			{
				// First exec branch: continue on the same horizontal line,
				// chaining each node to the right of the previous one.
				// Children of event nodes get a 18px downward nudge so the
				// exec wire visually clears the event's title bar.
				const int32 EventNudge = IsEventNode(Node) ? 18 : 0;
				int32 ChainX = X + GetNodeWidth(Node) + GapX;
				int32 ChainY = Y + EventNudge;
				for (const FChildSlot& Slot : PinSlots)
				{
					if (Placed.Contains(Slot.Child))
					{
						continue;
					}
					// Reserve space BEFORE placing this child so its
					// left-side data providers don't overlap the previous node.
					const int32 ProviderW = GetMaxDataProviderWidth(Slot.Child, DataProvidersByConsumer);
					ChainX += ProviderW + (ProviderW > 0 ? GapX : 0);
					const int32 ChildSpan = LayoutSubtree(Slot.Child, ChainX, ChainY,
						ChildrenByParent, DataProvidersByConsumer, DataFollowersBySource, Placed, OutPositions);
					ChainX += GetNodeWidth(Slot.Child) + GapX;
					Bottom = FMath::Max(Bottom, ChainY + ChildSpan);
				}
				bFirstExecBranch = false;
			}
			else
			{
				// Subsequent exec branch: start on a new line below the
				// previous branch's bottom, then chain horizontally.
				// Same 18px event nudge applies to branch children.
				const int32 EventNudge = IsEventNode(Node) ? 18 : 0;
				BranchTop = Bottom + GapY;
				int32 ChainX = X + GetNodeWidth(Node) + GapX;
				int32 ChainY = BranchTop + EventNudge;
				for (const FChildSlot& Slot : PinSlots)
				{
					if (Placed.Contains(Slot.Child))
					{
						continue;
					}
					// Reserve space BEFORE placing this child so its
					// left-side data providers don't overlap the previous node.
					const int32 ProviderW = GetMaxDataProviderWidth(Slot.Child, DataProvidersByConsumer);
					ChainX += ProviderW + (ProviderW > 0 ? GapX : 0);
					const int32 ChildSpan = LayoutSubtree(Slot.Child, ChainX, ChainY,
						ChildrenByParent, DataProvidersByConsumer, DataFollowersBySource, Placed, OutPositions);
					ChainX += GetNodeWidth(Slot.Child) + GapX;
					Bottom = FMath::Max(Bottom, ChainY + ChildSpan);
				}
			}
		}

		return Bottom - Y;
	}

	/**
	 * One item of the identity-agnostic material layout model.
	 *
	 * The material placement rule is the same whether the graph is the material
	 * editor's transient UEdGraph or a plain UMaterialExpression list read straight
	 * off the asset (bridge / Asset IR built assets have no editor graph at all),
	 * so both front-ends reduce to this: an item with a size and a list of data
	 * providers, each tagged with the y-offset of the input pin it feeds.
	 */
	struct FLayoutItem
	{
		int32 Width = FallbackWidth;
		int32 Height = EstimatedTitleHeight;
		/** <provider item index, y-offset of this item's input pin that wire lands in> */
		TArray<TPair<int32, int32>> Providers;
	};

	/**
	 * Material layout core: data-flow tree from right to left.
	 *
	 * Material graphs have no execution wires - every connection is a data wire - so
	 * the exec-chain logic used for blueprint graphs would treat every node as an
	 * independent root and effectively do nothing. Instead the item being placed keeps
	 * its left edge at LeftX and each of its providers is placed one column further
	 * left, stacked vertically in consumer-pin order, the whole subtree span being
	 * consumed before the next provider starts so branches never overlap.
	 *
	 * Returns the vertical span consumed by this subtree.
	 */
	int32 LayoutMaterialItems(int32 Index, int32 LeftX, int32 Y,
		const TArray<FLayoutItem>& Items, TSet<int32>& Placed, TArray<FIntPoint>& OutPositions)
	{
		OutPositions[Index] = FIntPoint(LeftX, Y);
		Placed.Add(Index);

		const FLayoutItem& Item = Items[Index];
		int32 Bottom = Y + Item.Height;

		// Cursor tracks the bottom of the previously placed provider subtree so the
		// next one can never overlap it, however deep its own subtree turns out.
		int32 CursorY = Y;
		for (const TPair<int32, int32>& Provider : Item.Providers)
		{
			const int32 Child = Provider.Key;
			if (Child == Index || !Items.IsValidIndex(Child) || Placed.Contains(Child))
			{
				continue;
			}
			// Align to the consumer pin so the wire stays near-horizontal, but never
			// climb back over an already placed sibling.
			const int32 TargetY = FMath::Max(Y + Provider.Value, CursorY);
			const int32 ProviderLeftX = LeftX - GapX - Items[Child].Width;
			const int32 Span = LayoutMaterialItems(Child, ProviderLeftX, TargetY, Items, Placed, OutPositions);
			CursorY = TargetY + Span + GapY;
			Bottom = FMath::Max(Bottom, TargetY + Span);
		}

		return Bottom - Y;
	}
}

void FForgeMindGraphLayout::ArrangeMaterialGraph(UEdGraph* Graph, const TArray<UEdGraphNode*>& Nodes)
{
	// Front-end over the material editor's graph: nodes and pins are reduced to the
	// shared layout model, the placement math itself lives in LayoutMaterialItems only.
	TMap<UEdGraphNode*, int32> IndexOf;
	TArray<UEdGraphNode*> Ordered;
	TArray<FLayoutItem> Items;
	for (UEdGraphNode* Node : Nodes)
	{
		if (Node == nullptr || IndexOf.Contains(Node))
		{
			continue;
		}
		IndexOf.Add(Node, Ordered.Num());
		Ordered.Add(Node);

		FLayoutItem Item;
		Item.Width = GetNodeWidth(Node);
		Item.Height = GetNodeHeight(Node);
		Items.Add(MoveTemp(Item));
	}
	if (Ordered.Num() == 0)
	{
		return;
	}

	// Data wires: each provider is anchored to exactly one consumer (first one found in
	// node list order), so it appears in exactly one place in the layout.
	TSet<int32> Claimed;
	for (UEdGraphNode* Consumer : Ordered)
	{
		const int32 ConsumerIndex = IndexOf[Consumer];
		// Pins iterate in declaration order, which keeps the provider list sorted by the
		// consumer pin offset it lands in - wires stay near-horizontal and never cross
		// their own siblings.
		for (const UEdGraphPin* InputPin : Consumer->Pins)
		{
			if (InputPin == nullptr || InputPin->Direction != EGPD_Input || IsExecPin(InputPin))
			{
				continue;
			}
			for (const UEdGraphPin* Link : InputPin->LinkedTo)
			{
				UEdGraphNode* Provider = Link != nullptr ? Link->GetOwningNode() : nullptr;
				const int32* ProviderIndex = Provider != nullptr ? IndexOf.Find(Provider) : nullptr;
				if (ProviderIndex == nullptr || *ProviderIndex == ConsumerIndex || Claimed.Contains(*ProviderIndex))
				{
					continue;
				}
				Claimed.Add(*ProviderIndex);
				Items[ConsumerIndex].Providers.Add(TPair<int32, int32>(*ProviderIndex, GetPinOffsetY(Consumer, InputPin)));
			}
		}
	}

	// Roots: the Material Root node (the output sink, no output pins) when the editor
	// graph has one, otherwise every node nothing consumes (material functions have no
	// root node). Unclaimed nodes are laid out in ascending Y order.
	TArray<int32> Roots;
	for (int32 Index = 0; Index < Ordered.Num(); ++Index)
	{
		if (IsMaterialRootNode(Ordered[Index]))
		{
			Roots.Add(Index);
		}
	}
	if (Roots.Num() == 0)
	{
		for (int32 Index = 0; Index < Ordered.Num(); ++Index)
		{
			if (!Claimed.Contains(Index))
			{
				Roots.Add(Index);
			}
		}
		Roots.Sort([&Ordered](const int32& Lhs, const int32& Rhs)
		{
			return Ordered[Lhs]->NodePosY < Ordered[Rhs]->NodePosY;
		});
	}

	TArray<FIntPoint> Positions;
	Positions.SetNumUninitialized(Ordered.Num());
	TSet<int32> Placed;
	int32 CursorY = Roots.Num() > 0 ? Ordered[Roots[0]]->NodePosY : 0;
	for (const int32 Root : Roots)
	{
		if (Placed.Contains(Root))
		{
			continue;
		}
		// The root stays at X = 0; its providers expand to the LEFT (negative X).
		const int32 Span = LayoutMaterialItems(Root, 0, CursorY, Items, Placed, Positions);
		CursorY += Span + BlockGap;
	}

	// Catch-all: anything not reached (cycles, orphans).
	for (int32 Index = 0; Index < Ordered.Num(); ++Index)
	{
		if (Placed.Contains(Index))
		{
			continue;
		}
		const int32 Span = LayoutMaterialItems(Index, 0, CursorY, Items, Placed, Positions);
		CursorY += Span + BlockGap;
	}

	// Snapshot comment box membership BEFORE moving nodes so that the
	// refit after the move uses the exact set of members at layout time.
	const FCommentSnapshot CommentSnap = SnapshotCommentMembers(Graph);

	const FScopedTransaction Transaction(NSLOCTEXT("ForgeMindGraphLayout", "ArrangeNodes", "Arrange Graph Nodes"));
	for (int32 Index = 0; Index < Ordered.Num(); ++Index)
	{
		if (!Placed.Contains(Index))
		{
			continue;
		}
		Ordered[Index]->Modify();
		Ordered[Index]->NodePosX = Positions[Index].X;
		Ordered[Index]->NodePosY = Positions[Index].Y;
	}

	// Re-fit comment boxes to their original members' new positions.
	RefitCommentBoxes(CommentSnap);

	Graph->Modify();
	Graph->NotifyGraphChanged();
}

void FForgeMindGraphLayout::ArrangeMaterialExpressions(UObject* Asset, const TArray<UMaterialExpression*>& Expressions)
{
	if (Asset == nullptr)
	{
		return;
	}

	// Same layout as the editor-graph front-end above, over the durable expression data:
	// the comment boxes are not part of the data flow and keep their own positions,
	// exactly like the pinless nodes ArrangeGraph leaves alone.
	TMap<UMaterialExpression*, int32> IndexOf;
	TArray<UMaterialExpression*> Ordered;
	TArray<FLayoutItem> Items;
	for (UMaterialExpression* Expression : Expressions)
	{
		if (Expression == nullptr || Cast<UMaterialExpressionComment>(Expression) != nullptr
			|| IndexOf.Contains(Expression))
		{
			continue;
		}
		IndexOf.Add(Expression, Ordered.Num());
		Ordered.Add(Expression);

		FLayoutItem Item;
		// Nothing has been rendered headless, so the geometry is estimated the same way
		// the editor front-end estimates an unmeasured node: title bar plus one row per pin.
		Item.Height = EstimatedTitleHeight
			+ FMath::Max(1, Expression->GetInputs().Num()) * EstimatedPinRow;
		Items.Add(MoveTemp(Item));
	}
	if (Ordered.Num() == 0)
	{
		return;
	}

	// Data wires, now that every expression has an index: a provider is anchored to its
	// first consumer only, matching the editor-graph behaviour. Input order is the pin
	// declaration order, so the provider list comes out sorted by consumer pin offset.
	TSet<int32> Claimed;
	for (int32 ConsumerIndex = 0; ConsumerIndex < Ordered.Num(); ++ConsumerIndex)
	{
		TArray<TPair<int32, int32>>& Providers = Items[ConsumerIndex].Providers;
		const TArray<FExpressionInput*> Inputs = Ordered[ConsumerIndex]->GetInputs();
		Providers.Reserve(Inputs.Num());
		for (int32 InputIndex = 0; InputIndex < Inputs.Num(); ++InputIndex)
		{
			const FExpressionInput* Input = Inputs[InputIndex];
			const int32* ProviderIndex = (Input != nullptr && Input->Expression != nullptr)
				? IndexOf.Find(Input->Expression)
				: nullptr;
			if (ProviderIndex == nullptr || *ProviderIndex == ConsumerIndex || Claimed.Contains(*ProviderIndex))
			{
				continue;
			}
			Claimed.Add(*ProviderIndex);
			Providers.Add(TPair<int32, int32>(*ProviderIndex, EstimatedTitleHeight + InputIndex * EstimatedPinRow));
		}
	}

	// Roots are the expressions nothing consumes - the ones feeding the material output
	// pins, or a material function's output expression. Ascending Y keeps blocks stable
	// across repeated tidy passes.
	TArray<int32> Roots;
	for (int32 Index = 0; Index < Ordered.Num(); ++Index)
	{
		if (!Claimed.Contains(Index))
		{
			Roots.Add(Index);
		}
	}
	Roots.Sort([&Ordered](const int32& Lhs, const int32& Rhs)
	{
		return Ordered[Lhs]->MaterialExpressionEditorY < Ordered[Rhs]->MaterialExpressionEditorY;
	});
	if (Roots.Num() == 0)
	{
		// Fully cyclic leftovers: start somewhere deterministic.
		Roots.Add(0);
	}

	TArray<FIntPoint> Positions;
	Positions.SetNumUninitialized(Ordered.Num());
	TSet<int32> Placed;
	int32 CursorY = Ordered[Roots[0]]->MaterialExpressionEditorY;
	for (const int32 Root : Roots)
	{
		if (Placed.Contains(Root))
		{
			continue;
		}
		const int32 Span = LayoutMaterialItems(Root, 0, CursorY, Items, Placed, Positions);
		CursorY += Span + BlockGap;
	}
	for (int32 Index = 0; Index < Ordered.Num(); ++Index)
	{
		if (Placed.Contains(Index))
		{
			continue;
		}
		const int32 Span = LayoutMaterialItems(Index, 0, CursorY, Items, Placed, Positions);
		CursorY += Span + BlockGap;
	}

	bool bChanged = false;
	for (int32 Index = 0; Index < Ordered.Num(); ++Index)
	{
		if (!Placed.Contains(Index))
		{
			continue;
		}
		UMaterialExpression* Expression = Ordered[Index];
		const FIntPoint& Position = Positions[Index];
		if (Expression->MaterialExpressionEditorX == Position.X && Expression->MaterialExpressionEditorY == Position.Y)
		{
			continue;
		}
		Expression->Modify();
		Expression->MaterialExpressionEditorX = Position.X;
		Expression->MaterialExpressionEditorY = Position.Y;
		bChanged = true;
	}
	if (!bChanged)
	{
		return;
	}

	Asset->MarkPackageDirty();

	// No touch of a material editor's live graph here on purpose: UMaterial::MaterialGraph
	// is a raw non-UPROPERTY pointer that the editor sets when it opens and that outlives
	// the graph's collection afterwards, so it cannot be trusted as "an editor is open".
	// An already-open editor keeps its own node layout and is tidied by the hotkey, which
	// runs the node-side front-end above on that graph.
}

void FForgeMindGraphLayout::ArrangeGraph(UEdGraph* Graph)
{
	if (Graph == nullptr)
	{
		return;
	}

	// Every pinned node takes part; pinless nodes (comment boxes) stay put.
	TArray<UEdGraphNode*> Nodes;
	for (UEdGraphNode* Node : Graph->Nodes)
	{
		if (Node != nullptr && Node->Pins.Num() > 0)
		{
			Nodes.Add(Node);
		}
	}
	if (Nodes.Num() == 0)
	{
		return;
	}

	// Material graphs use a separate layout path: they have no execution
	// wires, so the exec-chain logic below would treat every node as an
	// independent root and effectively do nothing.  Instead, build a
	// data-dependency tree rooted at the Material Root node (or the
	// leftmost unconnected node if there is no root) and expand providers
	// leftward from the sink.
	if (IsMaterialGraph(Graph))
	{
		ArrangeMaterialGraph(Graph, Nodes);
		return;
	}

	// Each node with an incoming exec wire is anchored to its topmost exec
	// parent (lowest current Y); that one parent pin decides its position.
	// Only exec pins (PC_Exec) are followed - data-only nodes have no exec
	// parent and will be treated as roots or catch-all leftovers.
	TMap<UEdGraphNode*, TArray<FChildSlot>> ChildrenByParent;
	TSet<UEdGraphNode*> HasParent;
	for (UEdGraphNode* Node : Nodes)
	{
		UEdGraphNode* BestParent = nullptr;
		UEdGraphPin* BestPin = nullptr;
		UEdGraphPin* BestChildPin = nullptr;
		for (const UEdGraphPin* Pin : Node->Pins)
		{
			if (Pin == nullptr || Pin->Direction != EGPD_Input)
			{
				continue;
			}
			// Only exec input pins participate in the execution chain.
			if (!IsExecPin(Pin))
			{
				continue;
			}
			for (const UEdGraphPin* Link : Pin->LinkedTo)
			{
				UEdGraphNode* Other = Link != nullptr ? Link->GetOwningNode() : nullptr;
				if (Other == nullptr || Other == Node || Other->GetGraph() != Graph || Other->Pins.Num() == 0)
				{
					continue;
				}
				if (BestParent == nullptr
					|| Other->NodePosY < BestParent->NodePosY
					|| (Other->NodePosY == BestParent->NodePosY && Other->NodePosX < BestParent->NodePosX))
				{
					BestParent = Other;
					BestPin = const_cast<UEdGraphPin*>(Link);
					BestChildPin = const_cast<UEdGraphPin*>(Pin);
				}
			}
		}
		if (BestParent != nullptr)
		{
			HasParent.Add(Node);
			ChildrenByParent.FindOrAdd(BestParent).Add(FChildSlot{ BestPin, BestChildPin, Node });
		}
	}

	// Build the reverse map: for each consumer node, which pure data
	// nodes feed its data input pins (in pin declaration order).  A
	// pure data node is anchored to its first consumer found in the
	// node list; if it feeds multiple consumers, the earliest one wins.
	TMap<UEdGraphNode*, TArray<UEdGraphNode*>> DataProvidersByConsumer;
	TSet<UEdGraphNode*> DataNodeClaimed;
	for (UEdGraphNode* Consumer : Nodes)
	{
		if (IsPureDataNode(Consumer))
		{
			continue;
		}
		for (const UEdGraphPin* InputPin : Consumer->Pins)
		{
			if (InputPin == nullptr || InputPin->Direction != EGPD_Input)
			{
				continue;
			}
			// Exec input pins are not data - skip.
			if (IsExecPin(InputPin))
			{
				continue;
			}
			for (const UEdGraphPin* Link : InputPin->LinkedTo)
			{
				UEdGraphNode* Provider = Link != nullptr ? Link->GetOwningNode() : nullptr;
				if (Provider == nullptr || Provider == Consumer || Provider->GetGraph() != Graph)
				{
					continue;
				}
				if (!IsPureDataNode(Provider) || DataNodeClaimed.Contains(Provider))
				{
					continue;
				}
				DataNodeClaimed.Add(Provider);
				DataProvidersByConsumer.FindOrAdd(Consumer).Add(Provider);
			}
		}
	}

	// Unclaimed pure data nodes: their output has no consumer (dangling
	// or feeds a node that is itself a leftover).  Per "no output, look
	// at input": anchor them to the node that feeds their data input,
	// so they follow their input source instead of falling to catch-all.
	// These are placed to the RIGHT of their source (behind it), unlike
	// consumer-claimed data nodes which go to the left.
	TMap<UEdGraphNode*, TArray<UEdGraphNode*>> DataFollowersBySource;
	for (UEdGraphNode* Node : Nodes)
	{
		if (!IsPureDataNode(Node) || DataNodeClaimed.Contains(Node))
		{
			continue;
		}
		// Find the topmost (lowest Y) source node feeding this node's
		// data input pins.  Same "best parent" logic as exec anchoring.
		UEdGraphNode* BestSource = nullptr;
		for (const UEdGraphPin* InPin : Node->Pins)
		{
			if (InPin == nullptr || InPin->Direction != EGPD_Input
				|| IsExecPin(InPin))
			{
				continue;
			}
			for (const UEdGraphPin* Link : InPin->LinkedTo)
			{
				UEdGraphNode* Source = Link != nullptr ? Link->GetOwningNode() : nullptr;
				if (Source == nullptr || Source == Node || Source->GetGraph() != Graph)
				{
					continue;
				}
				if (BestSource == nullptr
					|| Source->NodePosY < BestSource->NodePosY
					|| (Source->NodePosY == BestSource->NodePosY && Source->NodePosX < BestSource->NodePosX))
				{
					BestSource = Source;
				}
			}
		}
		if (BestSource != nullptr)
		{
			DataNodeClaimed.Add(Node);
			DataFollowersBySource.FindOrAdd(BestSource).Add(Node);
		}
	}

	// Roots: nodes with no exec parent.  Pure data nodes are excluded -
	// they are placed to the left of their consumer by LayoutSubtree.
	TArray<UEdGraphNode*> Roots;
	for (UEdGraphNode* Node : Nodes)
	{
		if (!HasParent.Contains(Node) && !IsPureDataNode(Node))
		{
			Roots.Add(Node);
		}
	}
	Roots.Sort([](const UEdGraphNode& Lhs, const UEdGraphNode& Rhs)
	{
		return Lhs.NodePosY < Rhs.NodePosY;
	});

	TMap<UEdGraphNode*, FIntPoint> Positions;
	TSet<UEdGraphNode*> Placed;
	int32 CursorY = Roots.Num() > 0 ? Roots[0]->NodePosY : 0;
	for (UEdGraphNode* Root : Roots)
	{
		if (Placed.Contains(Root))
		{
			continue;
		}
		const int32 Span = LayoutSubtree(Root, 0, CursorY, ChildrenByParent, DataProvidersByConsumer, DataFollowersBySource, Placed, Positions);
		CursorY += Span + BlockGap;
	}

	// Catch-all: a cycle leaves every member with a parent, so none ever
	// surfaces as a root; lay each leftover chain out as its own block.
	// Also picks up pure data nodes whose consumer was never placed
	// (e.g. the consumer is itself a leftover) and unclaimed data nodes.
	for (UEdGraphNode* Node : Nodes)
	{
		if (Placed.Contains(Node))
		{
			continue;
		}
		const int32 Span = LayoutSubtree(Node, 0, CursorY, ChildrenByParent, DataProvidersByConsumer, DataFollowersBySource, Placed, Positions);
		CursorY += Span + BlockGap;
	}

	if (Positions.Num() == 0)
	{
		return;
	}

	// Snapshot comment box membership BEFORE moving nodes so that the
	// refit after the move uses the exact set of members at layout time.
	const FCommentSnapshot CommentSnap = SnapshotCommentMembers(Graph);

	const FScopedTransaction Transaction(NSLOCTEXT("ForgeMindGraphLayout", "ArrangeNodes", "Arrange Graph Nodes"));
	for (const TPair<UEdGraphNode*, FIntPoint>& Pair : Positions)
	{
		Pair.Key->Modify();
		Pair.Key->NodePosX = Pair.Value.X;
		Pair.Key->NodePosY = Pair.Value.Y;
	}

	// Re-fit comment boxes to their original members' new positions.
	// The snapshot preserves the exact membership from before the layout
	// move: nodes that were inside the comment stay inside (the box
	// follows them), nodes that were outside stay outside (the box never
	// expands to include newly-adjacent nodes).
	RefitCommentBoxes(CommentSnap);

	Graph->Modify();
	Graph->NotifyGraphChanged();
}

void FForgeMindGraphLayout::Register()
{
	// Register the configurable command (default Q) so it shows up in
	// Edit > Editor Preferences > Keyboard Shortcuts under "ForgeMind".
	FForgeMindGraphCommands::Register();

	// The module may load before Slate exists; fall back to engine init.
	if (FSlateApplication::IsInitialized())
	{
		AddKeyListener();
	}
	else
	{
		EngineInitHandle = FCoreDelegates::OnPostEngineInit.AddRaw(this, &FForgeMindGraphLayout::AddKeyListener);
	}
}

void FForgeMindGraphLayout::Unregister()
{
	if (EngineInitHandle.IsValid())
	{
		FCoreDelegates::OnPostEngineInit.Remove(EngineInitHandle);
		EngineInitHandle.Reset();
	}
	if (KeyListenerHandle.IsValid() && FSlateApplication::IsInitialized())
	{
		FSlateApplication::Get().OnApplicationPreInputKeyDownListener().Remove(KeyListenerHandle);
		KeyListenerHandle.Reset();
	}
}

void FForgeMindGraphLayout::AddKeyListener()
{
	if (!KeyListenerHandle.IsValid())
	{
		KeyListenerHandle = FSlateApplication::Get().OnApplicationPreInputKeyDownListener().AddRaw(this, &FForgeMindGraphLayout::OnPreInputKeyDown);
	}
}

void FForgeMindGraphLayout::OnPreInputKeyDown(const FKeyEvent& KeyEvent)
{
	// Read the configured hotkey from the command system (default Q).
	// The user can remap it via Edit > Editor Preferences > Keyboard Shortcuts.
	const FUICommandInfo* ArrangeCmd = FForgeMindGraphCommands::Get().ArrangeNodes.Get();
	if (ArrangeCmd == nullptr)
	{
		return;
	}

	// Build an FInputChord from the incoming key event so we can test it
	// against every active chord the user has configured. The chord uses
	// the boolean-modifier constructor matching FInputChord's API.
	const FInputChord PressedChord(
		KeyEvent.GetKey(),
		KeyEvent.IsShiftDown(),
		KeyEvent.IsControlDown(),
		KeyEvent.IsAltDown(),
		KeyEvent.IsCommandDown()
	);

	// HasActiveChord checks both primary and secondary bindings; if the
	// user cleared the binding, neither matches and the key is ignored.
	if (!ArrangeCmd->HasActiveChord(PressedChord))
	{
		return;
	}

	SGraphPanel* Panel = FindFocusedGraphPanel();
	if (Panel == nullptr)
	{
		return;
	}
	UEdGraph* Graph = Panel->GetGraphObj();
	if (Graph == nullptr || !IsEditableGraph(Graph))
	{
		return;
	}
	ArrangeGraph(Graph);
	Panel->Invalidate(EInvalidateWidget::Layout);
}