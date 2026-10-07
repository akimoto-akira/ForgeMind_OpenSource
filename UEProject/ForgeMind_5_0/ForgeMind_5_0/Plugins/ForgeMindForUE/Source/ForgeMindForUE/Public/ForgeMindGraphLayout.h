// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#pragma once

#include "Delegates/Delegate.h"
#include "Framework/Application/SlateApplication.h"

class UEdGraph;

/**
 * Blueprint-Assist-style node arrangement for any UEdGraph-based editor.
 *
 * One shared entry point (ArrangeGraph) serves both the interactive hotkey
 * and the headless bridge actions. Every chain is traced back to its
 * topmost node without incoming wires (usually an event or a dangling
 * head); those roots sit at X = 0 stacked in blank space and everything
 * downstream is laid out from them: each child block starts at its
 * parent's output pin and the next pin's block begins right below the
 * previous pin's whole subtree, so branches never overlap. Nodes are
 * never deleted and the stacking guarantees no overlaps.
 *
 * Works across all graph editors that use UEdGraph: blueprint event
 * graphs, functions, macros, animation blueprints, material graphs,
 * material functions, sound cues, behavior trees, and similar.
 *
 * The hotkey is configurable via Edit > Editor Preferences > Keyboard
 * Shortcuts (search for "Arrange Graph Nodes" under the ForgeMind
 * context). Default: Q.
 */
class FForgeMindGraphLayout
{
public:
	/** Hooks the global pre-input key listener (call once at module startup). */
	void Register();

	/** Removes the key listener (call at module shutdown). */
	void Unregister();

	/**
	 * Re-lays the whole graph out from its roots (see class comment).
	 * Shared by the hotkey and every bridge action that mutates the
	 * graph; pinless nodes such as comment boxes keep their positions.
	 *
	 * Works on any UEdGraph with a valid schema (blueprint event graphs,
	 * functions, macros, material graphs, sound cues, behavior trees, ...).
	 */
	static void ArrangeGraph(UEdGraph* Graph);

private:
	/** Material-graph-specific layout: data-flow tree from right to left. */
	static void ArrangeMaterialGraph(UEdGraph* Graph, const TArray<UEdGraphNode*>& Nodes);

	void AddKeyListener();
	void OnPreInputKeyDown(const FKeyEvent& KeyEvent);

	FDelegateHandle KeyListenerHandle;
	FDelegateHandle EngineInitHandle;
};