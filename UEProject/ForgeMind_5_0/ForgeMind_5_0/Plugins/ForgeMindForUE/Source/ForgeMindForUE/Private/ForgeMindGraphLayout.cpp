// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#include "ForgeMindGraphLayout.h"

#include "Dom/JsonObject.h"
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphNode.h"
#include "EdGraph/EdGraphPin.h"
#include "EdGraphSchema_K2.h"
#include "EdGraphNode_Comment.h"
#include "Fonts/FontMeasure.h"
#include "Framework/Application/SlateApplication.h"
#include "Framework/Commands/UICommandInfo.h"
#include "HAL/FileManager.h"
#include "HAL/PlatformTime.h"
#include "InputCoreTypes.h"
#include "K2Node_CallFunction.h"
#include "K2Node_CustomEvent.h"
#include "K2Node_Event.h"
#include "K2Node_FunctionEntry.h"
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
#include "Misc/Crc.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Rendering/SlateRenderer.h"
#include "SGraphNode.h"
#include "SGraphPanel.h"
#include "ScopedTransaction.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "Styling/AppStyle.h"
#include "Styling/CoreStyle.h"
#include "Widgets/InvalidateWidgetReason.h"
#include "Widgets/SWidget.h"
#include "Widgets/SWindow.h"

#include "ForgeMindGraphCommands.h"
#include "ForgeMindWireStyleSettings.h"

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
	const int32 DefaultGapX = 64;
	/** Vertical gap between stacked siblings. */
	const int32 DefaultGapY = 32;
	/** Vertical gap between independent root blocks. */
	const int32 DefaultBlockGap = 64;
	/**
	 * Fixed distance from a node's LEFT edge to the right edge of its data
	 * column.  It is deliberately its own value rather than a reuse of
	 * GapX: it is not "the gap to the next column", it is the offset every
	 * data node of one consumer is right-aligned against, so tuning one
	 * must not move the other.
	 */
	const int32 DefaultDataColumnGap = 64;

	/**
	 * Spacing the placement math runs on, resolved from the active wire style.
	 *
	 * The style is read once per arrangement and decides how nodes are spaced:
	 * a bezier curve absorbs any vertical offset, while an orthogonal or 45-degree
	 * trace only reads cleanly when both ends line up horizontally. Each style
	 * therefore owns its own spacing, resolved here as the single entry point so
	 * a new rule lands in one place instead of at every use site.
	 */
	struct FLayoutParams
	{
		int32 GapX = 0;
		int32 GapY = 0;
		int32 BlockGap = 0;
		int32 DataColumnGap = 0;
	};

	FLayoutParams ResolveLayoutParams(EForgeMindWireStyle WireStyle)
	{
		FLayoutParams Params;
		Params.GapX = DefaultGapX;
		Params.GapY = DefaultGapY;
		Params.BlockGap = DefaultBlockGap;
		Params.DataColumnGap = DefaultDataColumnGap;

		switch (WireStyle)
		{
		case EForgeMindWireStyle::Curve:
			// Default spacing: the curve hides vertical offsets, so the tree
			// layout needs no correction.
			break;

		case EForgeMindWireStyle::Straight:
		case EForgeMindWireStyle::Elbow:
		case EForgeMindWireStyle::Circuit:
			// Not defined yet: these reuse the Curve spacing until their own
			// placement rules are specified.
			break;
		}

		return Params;
	}

	/** -------------------------------------------------------------------
	 * Node size acquisition
	 *
	 * A node only has a true size once Slate has laid it out, and that size
	 * moves with the literal a pin carries: a longer default value widens the
	 * inline edit box, an extra pin adds a row.  Every placement path asks for
	 * a size through one resolver that tries, in order:
	 *
	 *   1. the rendered SGraphNode widget      - exact and current
	 *   2. UEdGraphNode::NodeWidth/NodeHeight  - persisted on the asset
	 *   3. UMaterialExpression::GetWidth/GetHeight - material expressions
	 *   4. the .forgemind cache beside the project - measured in an earlier run
	 *   5. an estimate from font metrics plus the pin layout
	 *
	 * Only 1-3 are measurements.  Whatever they return is written back to the
	 * cache, so the next arrangement of the same node - possibly headless, with
	 * no widget at all - is exact instead of guessed.  Estimates are never
	 * cached: a guess must not be promoted to a fact.
	 */

	/** Geometry used when nothing better is known (no widget, no cache). */
	const int32 FallbackWidth = 200;
	const int32 EstimatedTitleHeight = 40;
	const int32 EstimatedPinRow = 20;
	/**
	 * Event nodes draw a taller title block than a regular node, so their pin
	 * rows - and therefore their exec wire - start lower.  Carrying the
	 * difference here (rather than as a nudge in the placement code) keeps
	 * every alignment calculation working off one pin model.
	 */
	const int32 EventTitleExtraHeight = 18;

	/** Horizontal padding of a node body, both sides. */
	const int32 EstimatedPaddingX = 32;
	/** Gap between the input pin column and the output pin column. */
	const int32 EstimatedColumnGap = 24;
	const int32 MinEstimatedWidth = 140;
	const int32 MaxEstimatedWidth = 640;
	/** Average glyph width of the 9pt UI font - only used when the font
	 *  measurement service is unavailable (commandlet, no renderer). */
	const float FallbackCharWidth = 6.5f;
	/** Clamp for an inline default-value edit box, in pixels. */
	const float MinDefaultValueBox = 40.0f;
	const float MaxDefaultValueBox = 200.0f;
	/** Font size of a pin label, one step below the node title. */
	const int32 PinFontSize = 9;
	const int32 TitleFontSize = 10;

	/** Minimum seconds between two cache writes. */
	const double CacheFlushIntervalSeconds = 2.0;

	/**
	 * Where measured sizes are kept: a ".forgemind" folder sitting next to the
	 * project folder, not inside it.  It therefore never lands in Saved/ (which
	 * is wiped by a clean), never gets cooked into the project, and one folder
	 * per project keeps different engine versions from sharing font metrics.
	 */
	FString GetNodeSizeCachePath()
	{
		const FString ProjectDir = FPaths::ConvertRelativePathToFull(FPaths::ProjectDir());
		FString Path = FPaths::Combine(ProjectDir, TEXT(".."), TEXT(".forgemind"), TEXT("node_sizes.json"));
		FPaths::CollapseRelativeDirectories(Path);
		return Path;
	}

	/** Engine major.minor - sizes are only reusable across identical metrics. */
	FString GetEngineVersionKey()
	{
		const FEngineVersion& Version = FEngineVersion::Current();
		return FString::Printf(TEXT("%d.%d"), Version.GetMajor(), Version.GetMinor());
	}

	/**
	 * Durable record of measured node sizes, keyed by node class plus a hash of
	 * everything that can change the geometry: title, pin layout and - the part
	 * that is easy to miss - the literal each pin carries.  A different default
	 * value produces a different key, so a widened node is measured again
	 * instead of reusing the size of its narrower twin.
	 */
	class FNodeSizeCache
	{
	public:
		static FNodeSizeCache& Get()
		{
			static FNodeSizeCache Instance;
			return Instance;
		}

		bool TryGet(const FString& Key, FIntPoint& OutSize)
		{
			EnsureLoaded();
			const FIntPoint* Found = Sizes.Find(Key);
			if (Found == nullptr)
			{
				return false;
			}
			OutSize = *Found;
			return true;
		}

		/** Records a measured size.  Called only from the measurement paths. */
		void Put(const FString& Key, const FIntPoint& Size)
		{
			if (Key.IsEmpty() || Size.X <= 0 || Size.Y <= 0)
			{
				return;
			}
			EnsureLoaded();

			FIntPoint& Slot = Sizes.FindOrAdd(Key);
			if (Slot == Size)
			{
				return;
			}
			Slot = Size;
			bDirty = true;

			// Throttled: a bridge run arranges once per mutation, and a few
			// hundred mutations must not each hit the disk.
			const double Now = FPlatformTime::Seconds();
			if (Now - LastFlushSeconds >= CacheFlushIntervalSeconds)
			{
				Flush(Now);
			}
		}

		/** Writes the table out.  Called on shutdown so the last batch is kept. */
		void FlushAll()
		{
			if (bDirty)
			{
				Flush(FPlatformTime::Seconds());
			}
		}

	private:
		void EnsureLoaded()
		{
			if (bLoaded)
			{
				return;
			}
			bLoaded = true;
			Load();
		}

		void Load()
		{
			const FString Path = GetNodeSizeCachePath();
			FString Contents;
			if (!FFileHelper::LoadFileToString(Contents, *Path))
			{
				// First run, or the folder was cleaned: nothing to reuse yet.
				return;
			}

			TSharedPtr<FJsonObject> Root;
			const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Contents);
			if (!FJsonSerializer::Deserialize(Reader, Root) || !Root.IsValid())
			{
				return;
			}

			// A cache written by another engine version is dropped wholesale:
			// its sizes were measured under different slate metrics, and mixing
			// them would silently distort every later layout.
			FString FileEngine;
			Root->TryGetStringField(TEXT("engine"), FileEngine);
			if (FileEngine != GetEngineVersionKey())
			{
				// Metrics belong to another engine: drop them wholesale rather
				// than mixing sizes measured under different slate styles.
				Sizes.Empty();
				return;
			}

			// UE5.0's TryGetField returns the value by value; the out-parameter
			// overload only exists in later engine versions.
			const TSharedPtr<FJsonValue> TableField = Root->TryGetField(TEXT("sizes"));
			if (!TableField.IsValid())
			{
				return;
			}
			const TSharedPtr<FJsonObject> Table = TableField->AsObject();
			if (!Table.IsValid())
			{
				return;
			}

			for (const TPair<FString, TSharedPtr<FJsonValue>>& Entry : Table->Values)
			{
				const TSharedPtr<FJsonObject> Record = Entry.Value.IsValid() ? Entry.Value->AsObject() : nullptr;
				if (!Record.IsValid())
				{
					continue;
				}
				double W = 0.0;
				double H = 0.0;
				if (!Record->TryGetNumberField(TEXT("w"), W) || !Record->TryGetNumberField(TEXT("h"), H))
				{
					continue;
				}
				if (W > 0.0 && H > 0.0)
				{
					Sizes.Add(Entry.Key, FIntPoint(FMath::RoundToInt(W), FMath::RoundToInt(H)));
				}
			}
		}

		void Flush(const double Now)
		{
			if (!bDirty)
			{
				return;
			}

			const FString Path = GetNodeSizeCachePath();
			IFileManager::Get().MakeDirectory(*FPaths::GetPath(Path), true);

			const TSharedRef<FJsonObject> Root = MakeShared<FJsonObject>();
			Root->SetStringField(TEXT("engine"), GetEngineVersionKey());
			Root->SetNumberField(TEXT("version"), 1.0);

			const TSharedRef<FJsonObject> Table = MakeShared<FJsonObject>();
			for (const TPair<FString, FIntPoint>& Entry : Sizes)
			{
				const TSharedRef<FJsonObject> Record = MakeShared<FJsonObject>();
				Record->SetNumberField(TEXT("w"), static_cast<double>(Entry.Value.X));
				Record->SetNumberField(TEXT("h"), static_cast<double>(Entry.Value.Y));
				Table->SetObjectField(Entry.Key, Record);
			}
			Root->SetObjectField(TEXT("sizes"), Table);

			FString Serialized;
			const TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&Serialized);
			if (!FJsonSerializer::Serialize(Root, Writer))
			{
				return;
			}
			if (!FFileHelper::SaveStringToFile(Serialized, *Path))
			{
				return;
			}

			bDirty = false;
			LastFlushSeconds = Now;
		}

		TMap<FString, FIntPoint> Sizes;
		bool bLoaded = false;
		bool bDirty = false;
		double LastFlushSeconds = 0.0;
	};

	/**
	 * Cache key: node class plus a hash of every input that can move the
	 * geometry.  The pin signature covers direction, name, type and container,
	 * and the literal held by the pin - the value typed into a pin is what
	 * silently widens a node after it has already been measured.
	 */
	FString BuildNodeSizeKey(const UEdGraphNode* Node)
	{
		FString Signature;
		Signature.Reserve(256);
		Signature += Node->GetClass()->GetName();
		Signature += TEXT("|");
		Signature += Node->GetNodeTitle(ENodeTitleType::FullTitle).ToString();
		Signature += TEXT("|");
		Signature += FString::FromInt(static_cast<int32>(Node->AdvancedPinDisplay));
		Signature += TEXT("|");

		for (const UEdGraphPin* Pin : Node->Pins)
		{
			if (Pin == nullptr)
			{
				continue;
			}
			Signature += (Pin->Direction == EGPD_Input) ? TEXT("I") : TEXT("O");
			Signature += Pin->PinName.ToString();
			Signature += TEXT(":");
			Signature += Pin->GetDisplayName().ToString();
			Signature += TEXT(":");
			Signature += Pin->PinType.PinCategory.ToString();
			Signature += Pin->PinType.PinSubCategory.ToString();
			if (Pin->PinType.PinSubCategoryObject.Get() != nullptr)
			{
				Signature += Pin->PinType.PinSubCategoryObject->GetName();
			}
			Signature += TEXT("@");
			Signature += FString::FromInt(static_cast<int32>(Pin->PinType.ContainerType));
			Signature += TEXT("=");
			Signature += Pin->DefaultValue;
			Signature += Pin->DefaultTextValue.ToString();
			if (Pin->DefaultObject != nullptr)
			{
				Signature += Pin->DefaultObject->GetName();
			}
			Signature += TEXT(";");
		}

		const uint32 Hash = FCrc::StrCrc32(*Signature);
		return FString::Printf(TEXT("%s#%08X"), *Node->GetClass()->GetName(), Hash);
	}

	/**
	 * Text width in pixels.  Returns -1 when no renderer is available, which
	 * tells the caller to fall back to a per-character estimate.
	 */
	float MeasureText(const FString& Text, const FSlateFontInfo& Font)
	{
		if (Text.IsEmpty())
		{
			return 0.0f;
		}
		if (!FSlateApplication::IsInitialized())
		{
			// No renderer (commandlet/headless): the caller falls back to an
			// average glyph width instead of a real measurement.
			return -1.0f;
		}

		const TSharedRef<FSlateFontMeasure> FontMeasure =
			FSlateApplication::Get().GetRenderer()->GetFontMeasureService();
		return FontMeasure->Measure(Text, Font).X;
	}

	/** Font used by graph node titles, with a plain fallback for headless runs. */
	FSlateFontInfo GetTitleFont()
	{
		const FSlateFontInfo& Styled = FAppStyle::Get().GetFontStyle(TEXT("Graph.Node.NodeTitle"));
		if (Styled.HasValidFont())
		{
			return Styled;
		}
		return FCoreStyle::GetDefaultFontStyle(TEXT("Regular"), TitleFontSize);
	}

	/** Font used by pin labels. */
	FSlateFontInfo GetPinFont()
	{
		return FCoreStyle::GetDefaultFontStyle(TEXT("Regular"), PinFontSize);
	}

	/**
	 * Last-resort geometry: title text plus one row per pin, measured with the
	 * real fonts when a renderer exists and with an average glyph width when it
	 * does not.  Inputs and outputs share a row, so the row count is the larger
	 * of the two columns rather than the total pin count.
	 */
	FIntPoint EstimateNodeSize(const UEdGraphNode* Node)
	{
		const FString Title = Node->GetNodeTitle(ENodeTitleType::FullTitle).ToString();
		const FSlateFontInfo TitleFont = GetTitleFont();
		const FSlateFontInfo PinFont = GetPinFont();

		float TitleWidth = MeasureText(Title, TitleFont);
		if (TitleWidth < 0.0f)
		{
			TitleWidth = static_cast<float>(Title.Len()) * FallbackCharWidth;
		}

		float InputColumn = 0.0f;
		float OutputColumn = 0.0f;
		int32 InputRows = 0;
		int32 OutputRows = 0;

		for (const UEdGraphPin* Pin : Node->Pins)
		{
			if (Pin == nullptr)
			{
				continue;
			}

			float LabelWidth = MeasureText(Pin->GetDisplayName().ToString(), PinFont);
			if (LabelWidth < 0.0f)
			{
				LabelWidth = static_cast<float>(Pin->GetDisplayName().ToString().Len()) * FallbackCharWidth;
			}

			// Only input pins carry an inline edit box, and only the literal
			// currently held by the pin decides how wide that box grows.
			FString Literal = Pin->DefaultValue;
			if (Literal.IsEmpty())
			{
				Literal = Pin->DefaultTextValue.ToString();
			}
			if (Literal.IsEmpty() && Pin->DefaultObject != nullptr)
			{
				Literal = Pin->DefaultObject->GetName();
			}

			float RowWidth = LabelWidth;
			if (Pin->Direction == EGPD_Input && !Literal.IsEmpty())
			{
				float BoxWidth = MeasureText(Literal, PinFont);
				if (BoxWidth < 0.0f)
				{
					BoxWidth = static_cast<float>(Literal.Len()) * FallbackCharWidth;
				}
				RowWidth += FMath::Clamp(BoxWidth, MinDefaultValueBox, MaxDefaultValueBox);
			}

			if (Pin->Direction == EGPD_Input)
			{
				InputColumn = FMath::Max(InputColumn, RowWidth);
				++InputRows;
			}
			else
			{
				OutputColumn = FMath::Max(OutputColumn, RowWidth);
				++OutputRows;
			}
		}

		const float BodyWidth = InputColumn + EstimatedColumnGap + OutputColumn;
		const int32 Width = FMath::Clamp(
			FMath::RoundToInt(FMath::Max(BodyWidth, TitleWidth) + EstimatedPaddingX),
			MinEstimatedWidth, MaxEstimatedWidth);
		const int32 Rows = FMath::Max(1, FMath::Max(InputRows, OutputRows));
		const int32 Height = EstimatedTitleHeight + Rows * EstimatedPinRow;

		return FIntPoint(Width, Height);
	}

	/**
	 * Finds the graph panel displaying this graph, if one is open.  The search
	 * covers every top-level window rather than only the focused panel: an
	 * arrangement can be requested from the bridge while the graph is open but
	 * unfocused, and a focused-panel search would silently miss it.
	 */
	SGraphPanel* FindGraphPanelForGraph(const UEdGraph* Graph)
	{
		if (Graph == nullptr || !FSlateApplication::IsInitialized())
		{
			return nullptr;
		}

		static const FName GraphPanelTypeName(TEXT("SGraphPanel"));

		const TArray<TSharedRef<SWindow>>& Windows = FSlateApplication::Get().GetTopLevelWindows();
		for (const TSharedRef<SWindow>& Window : Windows)
		{
			TArray<TSharedPtr<SWidget>> Pending;
			Pending.Push(Window);
			int32 Visited = 0;
			while (Pending.Num() > 0 && Visited < 5000)
			{
				const TSharedPtr<SWidget> Current = Pending.Pop();
				++Visited;
				if (!Current.IsValid())
				{
					continue;
				}

				const FVector2D PanelSize = Current->GetPaintSpaceGeometry().GetLocalSize();
				if (Current->GetType() == GraphPanelTypeName && PanelSize.X > 0.0f && PanelSize.Y > 0.0f)
				{
					SGraphPanel* Panel = StaticCastSharedPtr<SGraphPanel>(Current).Get();
					if (Panel != nullptr && Panel->GetGraphObj() == Graph)
					{
						return Panel;
					}
				}

				if (const FChildren* Children = Current->GetChildren())
				{
					for (int32 ChildIndex = 0; ChildIndex < Children->Num(); ++ChildIndex)
					{
						// GetChildAt hands back a const ref; the traversal only
						// reads type and geometry, so dropping const is safe.
						Pending.Push(ConstCastSharedRef<SWidget>(Children->GetChildAt(ChildIndex)));
					}
				}
			}
		}

		return nullptr;
	}

	/**
	 * Panel lookup memoised per graph.  An arrangement resolves the size of
	 * every node, and walking the whole window tree once per node would
	 * dominate its cost.  The panel is held weakly: a graph panel closed
	 * between two arrangements is detected rather than followed into freed
	 * memory, and a graph with no open panel is remembered as "no panel"
	 * instead of being searched again for every node.
	 */
	SGraphPanel* GetPanelForGraph(const UEdGraph* Graph)
	{
		static const UEdGraph* CachedGraph = nullptr;
		static TWeakPtr<SGraphPanel> CachedPanel;
		static bool bCacheValid = false;

		if (bCacheValid && CachedGraph == Graph)
		{
			if (!CachedPanel.IsValid())
			{
				return nullptr;
			}
			return CachedPanel.Pin().Get();
		}

		SGraphPanel* Panel = FindGraphPanelForGraph(Graph);
		CachedGraph = Graph;
		bCacheValid = true;
		// SWidget::AsShared() yields a TSharedRef (Slate widgets are shared with
		// ESPMode::ThreadSafe), so the downcast has to go through
		// StaticCastSharedRef - StaticCastSharedPtr only accepts a TSharedPtr.
		CachedPanel = Panel != nullptr
			? TWeakPtr<SGraphPanel>(StaticCastSharedRef<SGraphPanel>(Panel->AsShared()))
			: TWeakPtr<SGraphPanel>();
		return Panel;
	}

	/**
	 * Resolves a node's size through the full chain above.  The result of the
	 * last call is remembered because the placement math asks for width and
	 * height of the same node in immediate succession.
	 */
	FIntPoint GetNodeSize(const UEdGraphNode* Node)
	{
		static const UEdGraphNode* LastNode = nullptr;
		static FIntPoint LastSize(0, 0);

		if (Node == nullptr)
		{
			return FIntPoint(FallbackWidth, EstimatedTitleHeight);
		}
		if (Node == LastNode && LastSize.X > 0 && LastSize.Y > 0)
		{
			return LastSize;
		}

		FIntPoint Result(0, 0);
		bool bMeasured = false;

		// 1. The rendered widget, when the graph happens to be open.
		if (Node->NodeGuid.IsValid())
		{
			if (SGraphPanel* Panel = GetPanelForGraph(Node->GetGraph()))
			{
				const TSharedPtr<SGraphNode> Widget = Panel->GetNodeWidgetFromGuid(Node->NodeGuid);
				if (Widget.IsValid())
				{
					const FVector2D Desired = Widget->GetDesiredSize();
					if (Desired.X > 0.0f && Desired.Y > 0.0f)
					{
						Result = FIntPoint(FMath::RoundToInt(Desired.X), FMath::RoundToInt(Desired.Y));
						bMeasured = true;
					}
				}
			}
		}

		// 2. Size persisted on the asset itself (resizable nodes, or a graph
		//    that was rendered and saved in an earlier session).
		if (!bMeasured && Node->NodeWidth > 0 && Node->NodeHeight > 0)
		{
			Result = FIntPoint(Node->NodeWidth, Node->NodeHeight);
			bMeasured = true;
		}

		// 3. Material expressions carry their own geometry, no widget needed.
		if (!bMeasured)
		{
			if (const UMaterialGraphNode* MaterialNode = Cast<UMaterialGraphNode>(Node))
			{
				const UMaterialExpression* Expression = MaterialNode->MaterialExpression.Get();
				if (Expression != nullptr && Expression->GetWidth() > 0 && Expression->GetHeight() > 0)
				{
					Result = FIntPoint(Expression->GetWidth(), Expression->GetHeight());
					bMeasured = true;
				}
			}
		}

		const FString Key = BuildNodeSizeKey(Node);

		if (bMeasured)
		{
			FNodeSizeCache::Get().Put(Key, Result);
		}
		else if (!FNodeSizeCache::Get().TryGet(Key, Result))
		{
			// 5. Nothing measured, nothing cached: estimate, and do not store it.
			Result = EstimateNodeSize(Node);
		}

		LastNode = Node;
		LastSize = Result;
		return Result;
	}

	/** Resolved width - measured, cached, or estimated. */
	int32 GetNodeWidth(const UEdGraphNode* Node)
	{
		return GetNodeSize(Node).X;
	}

	/** Resolved height - measured, cached, or estimated. */
	int32 GetNodeHeight(const UEdGraphNode* Node)
	{
		return GetNodeSize(Node).Y;
	}

	/**
	 * TERMINOLOGY (user-defined): a DATA NODE is any node that provides
	 * data - every node that has a return value or, more generally, any
	 * output.  Nothing else about it matters: not its class, not how it
	 * was created, not whether some other rule happens to consume it.
	 * A "+" operator, a Break struct, a Self reference, a variable
	 * getter and a function call with a Return Value are all data nodes
	 * by this definition, and so is a SpawnActor that carries both an
	 * exec pair and a Return Value.
	 *
	 * The test is therefore structural - does the node own at least one
	 * non-exec output pin - and is deliberately NOT a list of known
	 * node classes: such a list silently excludes every class that was
	 * not enumerated, and any node missing from it stops being treated
	 * as data and gets misplaced.
	 */
	bool ProvidesData(const UEdGraphNode* Node)
	{
		if (Node == nullptr)
		{
			return false;
		}
		for (const UEdGraphPin* Pin : Node->Pins)
		{
			if (Pin != nullptr && Pin->Direction == EGPD_Output && !IsExecPin(Pin))
			{
				return true;
			}
		}
		return false;
	}

	/**
	 * Returns true if the node is a data node that is NOT driven by an
	 * exec wire: it provides data (see ProvidesData) and it has no exec
	 * pin at all, so it cannot be a step of the execution chain.  These
	 * are the nodes the left-column rule applies to.
	 *
	 * A node that carries an exec pin is a flow node first: it is placed
	 * by the exec chain even when it also provides data (SpawnActor's
	 * Return Value is data, but the node belongs to the chain).
	 * In material graphs every expression node qualifies (the only
	 * non-data node is the Material Root output sink).
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
		// Any exec pin means the node is part of the flow, not a free
		// data provider, even when it also has data outputs.
		for (const UEdGraphPin* Pin : Node->Pins)
		{
			if (Pin != nullptr && IsExecPin(Pin))
			{
				return false;
			}
		}
		return ProvidesData(Node);
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
	 * Returns true if the node is the entry point of a function graph:
	 * UK2Node_FunctionEntry, the "head" the graph starts from.  It is the
	 * anchor of the whole layout (see the rule documented at ArrangeGraph).
	 */
	bool IsFunctionEntryNode(const UEdGraphNode* Node)
	{
		return Node != nullptr && Cast<UK2Node_FunctionEntry>(Node) != nullptr;
	}

	/**
	 * Returns true if the graph is a function graph - one that owns a
	 * function entry node.  Event graphs (no entry node, event nodes
	 * instead) return false.
	 */
	bool GraphHasFunctionEntry(const TArray<UEdGraphNode*>& Nodes)
	{
		for (const UEdGraphNode* Node : Nodes)
		{
			if (IsFunctionEntryNode(Node))
			{
				return true;
			}
		}
		return false;
	}

	/**
	 * Total horizontal room the left-side data columns of a node need, or 0
	 * if it has none.  Used to reserve space in the exec chain so the data
	 * columns don't overlap the preceding exec node.
	 *
	 * The measurement is recursive: a provider that is itself fed by data
	 * nodes pushes its own column one step further left, so the reservation
	 * has to cover the whole chain, not just the first column.  Depth is
	 * capped because a malformed graph can link data nodes in a cycle.
	 */
	int32 GetProviderChainWidth(UEdGraphNode* Node, const FLayoutParams& Params,
		const TMap<UEdGraphNode*, TArray<UEdGraphNode*>>& DataProvidersByConsumer,
		int32 Depth = 0)
	{
		if (Node == nullptr || Depth > 16)
		{
			return 0;
		}

		const TArray<UEdGraphNode*>* Providers = DataProvidersByConsumer.Find(Node);
		if (Providers == nullptr)
		{
			return 0;
		}
		int32 MaxW = 0;
		for (UEdGraphNode* Provider : *Providers)
		{
			if (Provider == nullptr)
			{
				continue;
			}
			int32 Width = GetNodeWidth(Provider);
			const int32 Nested = GetProviderChainWidth(Provider, Params, DataProvidersByConsumer, Depth + 1);
			if (Nested > 0)
			{
				Width += Params.DataColumnGap + Nested;
			}
			MaxW = FMath::Max(MaxW, Width);
		}
		return MaxW;
	}

	/**
	 * Vertical offset of a pin relative to its node top. Always estimated:
	 * pins stack row by row under the title bar in declaration order.
	 */
	int32 GetPinOffsetY(const UEdGraphNode* Node, const UEdGraphPin* Pin)
	{
		if (Node == nullptr || Pin == nullptr)
		{
			return EstimatedTitleHeight;
		}

		// A node draws its pins in two columns under the title bar - inputs
		// on the left, outputs on the right - and each column numbers its own
		// rows starting at the title.  The row is therefore the pin's index
		// within its own direction, not its index in the flat Pins array: an
		// exec input and an exec output are both the first row of their own
		// column, which is what makes a horizontal exec wire possible.
		int32 Row = 0;
		for (const UEdGraphPin* Candidate : Node->Pins)
		{
			if (Candidate == Pin)
			{
				break;
			}
			if (Candidate != nullptr && Candidate->Direction == Pin->Direction)
			{
				++Row;
			}
		}

		const int32 TitleExtra = IsEventNode(Node) ? EventTitleExtraHeight : 0;
		return EstimatedTitleHeight + TitleExtra + Row * EstimatedPinRow;
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
		const FLayoutParams& Params,
		const TMap<UEdGraphNode*, TArray<FChildSlot>>& ChildrenByParent,
		const TMap<UEdGraphNode*, TArray<UEdGraphNode*>>& DataProvidersByConsumer,
		const TMap<UEdGraphNode*, TArray<UEdGraphNode*>>& DataFollowersBySource,
		TSet<UEdGraphNode*>& Placed,
		TMap<UEdGraphNode*, FIntPoint>& OutPositions)
	{
		OutPositions.Add(Node, FIntPoint(X, Y));
		Placed.Add(Node);

		// ------------------------------------------------------------------
		// Layout rule 3 - data nodes on the LEFT of a node
		//
		// Of the data nodes hanging off this node's input pins, every one
		// that is not driven by an exec wire (i.e. it is not a preceding
		// step in the exec chain - IsPureDataNode requires that the node
		// provides data AND carries no exec pin; anything already placed
		// belongs to the exec flow and is skipped anyway) is stacked to
		// the LEFT of this node, ordered TOP
		// TO BOTTOM BY THIS NODE'S PIN ORDER.  "This node" is the node
		// currently being laid out: the order comes from its own input pin
		// declaration sequence, never from the providers' own previous
		// coordinates, so the result does not depend on where the graph
		// happened to sit before the arrangement ran.
		//
		// Providers are measured, not assumed: each one is pushed down to
		// clear the actual bottom of the one above it, and a provider that
		// is itself fed by data nodes recurses, which puts its own
		// providers one column further left instead of on top of it.
		//
		// NOT SPECIFIED YET: the RIGHT side of a node (rule for data nodes
		// hanging off output pins).  The existing behaviour - dangling pure
		// nodes stacked behind their source - is kept for now and will be
		// replaced when that side is specified.
		// ------------------------------------------------------------------
		int32 MaxProviderY = Y;
		{
			const TArray<UEdGraphNode*>* ClaimedProviders = DataProvidersByConsumer.Find(Node);

			struct FDataProviderEntry
			{
				UEdGraphNode* Node;
				int32 PinTargetY;
			};
			TArray<FDataProviderEntry> Entries;
			TSet<UEdGraphNode*> Taken;

			// Walk this node's data input pins in declaration order: the
			// pin sequence IS the top-to-bottom order.
			for (const UEdGraphPin* InPin : Node->Pins)
			{
				if (InPin == nullptr || InPin->Direction != EGPD_Input
					|| IsExecPin(InPin))
				{
					continue;
				}
				const int32 PinTargetY = Y + GetPinOffsetY(Node, InPin);
				for (const UEdGraphPin* Link : InPin->LinkedTo)
				{
					UEdGraphNode* Provider = Link != nullptr ? Link->GetOwningNode() : nullptr;
					if (Provider == nullptr || Provider == Node
						|| Provider->GetGraph() != Node->GetGraph())
					{
						continue;
					}
					// Exec-driven nodes belong to the exec chain, not to
					// this stack; already-placed nodes are skipped too.
					if (!IsPureDataNode(Provider) || Placed.Contains(Provider)
						|| Taken.Contains(Provider))
					{
						continue;
					}
					// Only place the providers this node owns: a pure node
					// feeding two consumers is claimed by exactly one of
					// them, and the other one must not steal it.
					if (ClaimedProviders == nullptr || !ClaimedProviders->Contains(Provider))
					{
						continue;
					}
					Taken.Add(Provider);
					Entries.Add({ Provider, PinTargetY });
				}
			}

			// Right edge shared by every node in this column: they are all
			// right-aligned at one fixed distance from this node's left
			// edge, so a wider provider simply reaches further left instead
			// of covering a neighbour or drifting closer to the consumer.
			const int32 ColumnRight = X - Params.DataColumnGap;
			int32 PrevBottom = INT32_MIN;
			for (const FDataProviderEntry& Entry : Entries)
			{
				const int32 DataW = GetNodeWidth(Entry.Node);
				const int32 DataH = GetNodeHeight(Entry.Node);
				// Align to the pin the data arrives at, then never overlap
				// the previous provider: push down past its real bottom.
				const int32 TargetY = PrevBottom != INT32_MIN
					? FMath::Max(Entry.PinTargetY, PrevBottom + Params.GapY)
					: Entry.PinTargetY;

				// Recurse so a provider's own providers land one column
				// further left, and so their vertical extent is reserved.
				const int32 ChildSpan = LayoutSubtree(Entry.Node, ColumnRight - DataW, TargetY, Params,
					ChildrenByParent, DataProvidersByConsumer, DataFollowersBySource, Placed, OutPositions);
				PrevBottom = TargetY + FMath::Max(DataH, ChildSpan);
				MaxProviderY = FMath::Max(MaxProviderY, PrevBottom);
			}
		}

		// Place pure data nodes whose output is dangling but whose input
		// comes from this node, stacked to the RIGHT (behind the source).
		// NOT SPECIFIED YET - this is the right side of a node, which rule 3
		// deliberately leaves alone for now; the current ordering is simply
		// the order they were collected in.  These followers DO inflate the
		// returned span so the next root node starts below this node's full
		// visual extent (including its right-side data children).
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

			int32 FollowerX = X + GetNodeWidth(Node) + Params.GapX;
			int32 FollowerY = Y;
			for (const FDataFollowerEntry& Entry : FollowerEntries)
			{
				OutPositions.Add(Entry.Node, FIntPoint(FollowerX, FollowerY));
				Placed.Add(Entry.Node);
				FollowerY += GetNodeHeight(Entry.Node) + Params.GapY;
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
			// Layout rule 2: the wire running out of a node's FIRST exec
			// output pin has to be horizontal.  Aligning node tops is not
			// enough - the wire connects two pins, so the child is shifted
			// by the difference between the two pin offsets: its exec input
			// pin ends up at exactly the Y of the output pin it is wired to.
			//
			// For a regular node both pins are the first row of their own
			// column, the difference is zero and the child stays on the
			// parent's line.  An event node reports a lower output pin
			// (taller title block) and the child drops by exactly that much.
			const int32 ParentPinOffset = GetPinOffsetY(Node, Pin);
			int32 ChainX = X + GetNodeWidth(Node) + Params.GapX;
			for (const FChildSlot& Slot : PinSlots)
			{
				if (Placed.Contains(Slot.Child))
				{
					continue;
				}
				// Reserve space BEFORE placing this child so its
				// left-side data providers don't overlap the previous node.
				// The data column sits one DataColumnGap left of the child,
				// so that distance is part of the reservation.
				const int32 ProviderW = GetProviderChainWidth(Slot.Child, Params, DataProvidersByConsumer);
				ChainX += ProviderW + (ProviderW > 0 ? Params.DataColumnGap : 0);
				const int32 ChainY = Y + ParentPinOffset - GetPinOffsetY(Slot.Child, Slot.ChildPin);
				const int32 ChildSpan = LayoutSubtree(Slot.Child, ChainX, ChainY, Params,
					ChildrenByParent, DataProvidersByConsumer, DataFollowersBySource, Placed, OutPositions);
				ChainX += GetNodeWidth(Slot.Child) + Params.GapX;
				Bottom = FMath::Max(Bottom, ChainY + ChildSpan);
			}
			bFirstExecBranch = false;
		}
		else
		{
			// Subsequent exec branch: start on a new line below the previous
			// branch's bottom, then chain horizontally.  Rule 2 only pins the
			// first exec output to a horizontal wire; these branches get their
			// own line, but the same pin-offset correction is applied so the
			// branch's own wire leaves its pin level.
			const int32 ParentPinOffset = GetPinOffsetY(Node, Pin);
			BranchTop = Bottom + Params.GapY;
			int32 ChainX = X + GetNodeWidth(Node) + Params.GapX;
			for (const FChildSlot& Slot : PinSlots)
			{
				if (Placed.Contains(Slot.Child))
				{
					continue;
				}
				// Reserve space BEFORE placing this child so its
				// left-side data providers don't overlap the previous node.
				// The data column sits one DataColumnGap left of the child,
				// so that distance is part of the reservation.
				const int32 ProviderW = GetProviderChainWidth(Slot.Child, Params, DataProvidersByConsumer);
				ChainX += ProviderW + (ProviderW > 0 ? Params.DataColumnGap : 0);
				const int32 ChainY = BranchTop + ParentPinOffset - GetPinOffsetY(Slot.Child, Slot.ChildPin);
				const int32 ChildSpan = LayoutSubtree(Slot.Child, ChainX, ChainY, Params,
					ChildrenByParent, DataProvidersByConsumer, DataFollowersBySource, Placed, OutPositions);
				ChainX += GetNodeWidth(Slot.Child) + Params.GapX;
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
		const FLayoutParams& Params,
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
			const int32 ProviderLeftX = LeftX - Params.GapX - Items[Child].Width;
			const int32 Span = LayoutMaterialItems(Child, ProviderLeftX, TargetY, Params, Items, Placed, OutPositions);
			CursorY = TargetY + Span + Params.GapY;
			Bottom = FMath::Max(Bottom, TargetY + Span);
		}

		return Bottom - Y;
	}
}

void FForgeMindGraphLayout::ArrangeMaterialGraph(UEdGraph* Graph, const TArray<UEdGraphNode*>& Nodes)
{
	// Same style-driven spacing as the blueprint path.
	const FLayoutParams Params = ResolveLayoutParams(GetDefault<UForgeMindWireStyleSettings>()->WireStyle);

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
		const int32 Span = LayoutMaterialItems(Root, 0, CursorY, Params, Items, Placed, Positions);
		CursorY += Span + Params.BlockGap;
	}

	// Catch-all: anything not reached (cycles, orphans).
	for (int32 Index = 0; Index < Ordered.Num(); ++Index)
	{
		if (Placed.Contains(Index))
		{
			continue;
		}
		const int32 Span = LayoutMaterialItems(Index, 0, CursorY, Params, Items, Placed, Positions);
		CursorY += Span + Params.BlockGap;
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

	// Same style-driven spacing as the blueprint path.
	const FLayoutParams Params = ResolveLayoutParams(GetDefault<UForgeMindWireStyleSettings>()->WireStyle);

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
		// Expressions carry their own geometry (thumbnail plus one tab per
		// output), so this path is measured, not estimated - it stays correct
		// even when nothing has been rendered.  The estimate below is only the
		// fallback for an expression that reports no size.
		const int32 ExpressionWidth = Expression->GetWidth();
		const int32 ExpressionHeight = Expression->GetHeight();
		Item.Width = ExpressionWidth > 0 ? ExpressionWidth : FallbackWidth;
		Item.Height = ExpressionHeight > 0
			? ExpressionHeight
			: EstimatedTitleHeight + FMath::Max(1, Expression->GetInputs().Num()) * EstimatedPinRow;
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
		const int32 Span = LayoutMaterialItems(Root, 0, CursorY, Params, Items, Placed, Positions);
		CursorY += Span + Params.BlockGap;
	}
	for (int32 Index = 0; Index < Ordered.Num(); ++Index)
	{
		if (Placed.Contains(Index))
		{
			continue;
		}
		const int32 Span = LayoutMaterialItems(Index, 0, CursorY, Params, Items, Placed, Positions);
		CursorY += Span + Params.BlockGap;
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

	// Placement is wire-style aware: resolve the spacing for the active style
	// once, then run the shared tree layout with it.
	const FLayoutParams Params = ResolveLayoutParams(GetDefault<UForgeMindWireStyleSettings>()->WireStyle);

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
	//
	// Two passes.  The first one gives every non-pure (exec-driven) node
	// its direct data nodes.  The second one extends the same relation
	// into pure chains - a data node that feeds another data node instead
	// of an exec node - so a chain like
	//     GetActorLocation -> Multiply -> PredictProjectilePath
	// nests one column per level instead of dropping its upstream nodes
	// into the catch-all.  It repeats until stable because the node array
	// order does not follow the data flow, so a deep chain is only fully
	// claimed one level per iteration.
	TMap<UEdGraphNode*, TArray<UEdGraphNode*>> DataProvidersByConsumer;
	TSet<UEdGraphNode*> DataNodeClaimed;

	auto ClaimProviders = [&Graph, &DataProvidersByConsumer, &DataNodeClaimed](UEdGraphNode* Consumer) -> bool
	{
		bool bClaimedAny = false;
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
				bClaimedAny = true;
			}
		}
		return bClaimedAny;
	};

	// Pass 1: exec-driven consumers take their direct data nodes first.
	for (UEdGraphNode* Consumer : Nodes)
	{
		if (IsPureDataNode(Consumer))
		{
			continue;
		}
		ClaimProviders(Consumer);
	}

	// Pass 2: extend the same relation into pure chains, repeating until
	// stable.  A pure node only claims its own providers once it has a
	// home itself - otherwise an unclaimed node would collect a column
	// of providers that nothing ever places.
	bool bProgress = true;
	while (bProgress)
	{
		bProgress = false;
		for (UEdGraphNode* Consumer : Nodes)
		{
			if (!IsPureDataNode(Consumer) || !DataNodeClaimed.Contains(Consumer))
			{
				continue;
			}
			if (ClaimProviders(Consumer))
			{
				bProgress = true;
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

	// ------------------------------------------------------------------
	// Layout rule 1 - where a graph starts
	//
	// Every graph has a head, and the head is anchored at X = 0: it is the
	// origin the rest of the graph is laid out from.
	//
	//   * Function graph - the head is the function entry node
	//     (UK2Node_FunctionEntry).  Rule: it sits at X = 0, Y = 0, so a
	//     function always starts at the top-left corner no matter where its
	//     nodes were before.  Every other root block follows below it.
	//
	//   * Event graph - the head is the event node (UK2Node_Event,
	//     UK2Node_CustomEvent, input events, ...).  NOT SPECIFIED YET:
	//     event graphs are out of scope while the function layout is being
	//     tuned.  The current behaviour keeps each root block in its
	//     existing vertical order (sorted by NodePosY) and starts at the
	//     first root's current Y - it does not pin anything to Y = 0.  When
	//     event graphs are specified, the intended rule is: heads stacked
	//     top to bottom in graph order, which will replace this fallback.
	//
	//   * Macro / tunnel graphs (UK2Node_Tunnel entry) are also unspecified.
	//
	// Roots are ordered heads-first, and the vertical cursor starts at 0 for
	// a function graph instead of at whatever Y the nodes happened to have.
	// ------------------------------------------------------------------
	TArray<UEdGraphNode*> Roots;
	for (UEdGraphNode* Node : Nodes)
	{
		if (!HasParent.Contains(Node) && !IsPureDataNode(Node))
		{
			Roots.Add(Node);
		}
	}
	const bool bFunctionGraph = GraphHasFunctionEntry(Nodes);
	Roots.Sort([](const UEdGraphNode& Lhs, const UEdGraphNode& Rhs)
	{
		// Heads first: a function entry outranks any other root.
		const bool bLhsHead = IsFunctionEntryNode(&Lhs);
		const bool bRhsHead = IsFunctionEntryNode(&Rhs);
		if (bLhsHead != bRhsHead)
		{
			return bLhsHead;
		}
		return Lhs.NodePosY < Rhs.NodePosY;
	});

	TMap<UEdGraphNode*, FIntPoint> Positions;
	TSet<UEdGraphNode*> Placed;
	// Function graph: the head is pinned to (0, 0) and every other block is
	// stacked below it, so the cursor starts at 0.  Event graph: see the
	// note above - order is preserved, no pinning yet.
	int32 CursorY = bFunctionGraph ? 0 : (Roots.Num() > 0 ? Roots[0]->NodePosY : 0);
	for (UEdGraphNode* Root : Roots)
	{
		if (Placed.Contains(Root))
		{
			continue;
		}
		const int32 Span = LayoutSubtree(Root, 0, CursorY, Params, ChildrenByParent, DataProvidersByConsumer, DataFollowersBySource, Placed, Positions);
		CursorY += Span + Params.BlockGap;
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
		const int32 Span = LayoutSubtree(Node, 0, CursorY, Params, ChildrenByParent, DataProvidersByConsumer, DataFollowersBySource, Placed, Positions);
		CursorY += Span + Params.BlockGap;
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

	// Sizes measured during this pass are worth more than the throttled write:
	// flushing here means even a single arrangement persists them.
	FNodeSizeCache::Get().FlushAll();
}

void FForgeMindGraphLayout::FlushNodeSizeCache()
{
	FNodeSizeCache::Get().FlushAll();
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