// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 70 - Blueprint Enhanced Input (UE 5.0) - from Tasks/UE_5.0/Blueprint_EnhancedInput.md
//
// Probe (collaboration spec point 6):
// - The plugin IS enabled in the test project: ForgeMind_5_0.uproject lists
//   { "Name": "EnhancedInput", "Enabled": true } (its .uplugin has EnabledByDefault: false, so the
//   project entry is what turns it on). UInputAction / UInputMappingContext /
//   UEnhancedInputComponent therefore load, and the "InputAction" / "InputMappingContext" type
//   texts resolve. (This paragraph previously claimed the plugin was off — that was stale,
//   corrected 2026-10-22 after run 20261022_1955.)
// - Binding: `UEnhancedInputComponent::BindAction` is **not** a UFUNCTION (SpkTool
//   overloads BindAction -> 0 declaration sites; verified against EnhancedInputComponent.h in
//   5.0.3, where the class's only UFUNCTION is the BlueprintInternalUseOnly GetBoundActionValue
//   at :432). In a blueprint the action is bound with the specialized event node
//   UK2Node_EnhancedInputAction (module InputBlueprintNodes), which add_blueprint_node now
//   creates through kind "enhancedInputAction" (payload targetClass = the UInputAction asset).
//   The node's real pin set — read from K2Node_EnhancedInputAction::AllocateDefaultPins — is:
//   exec OUTPUT pins named after ETriggerEvent (started / ongoing / triggered / completed), plus
//   data outputs ActionValue / ElapsedSeconds / TriggeredSeconds / InputAction. It has NO input
//   exec pin and no "Action" / "TriggerEvent" pins, so a BeginPlay -> BindAction(action, event)
//   wiring does not exist in a blueprint at all. Subtask 1 was written as that plain function
//   call (designed before the plugin form was known); it is rewritten 2026-10-22 to the event
//   node, per the user's ruling that the requirement stands but the shape must be the plugin's.
// - Prerequisite asset: the node binds a real UInputAction **asset** (doc step 1), not a typed
//   variable. create_input_action was long listed in the Companion's advertised commands but had
//   no handler, and the asset-IR path classified "inputAction" for ordering without a creation
//   branch; the handler is added in this round (FForgeMindBridgeClient::HandleCreateInputAction),
//   which also gives the action a "Down" trigger — a trigger-less action supports no trigger
//   event and every exec pin would be labelled "(Unsupported)".
// - Value: the event node does NOT hand out an FInputActionValue. Its "ActionValue" pin is
//   typed straight from the action's ValueType through UK2Node_GetInputActionValue's
//   ValueLookups table (K2Node_GetInputActionValue.cpp): Boolean -> PC_Bool, Axis1D ->
//   PC_Real/float, **Axis2D -> PC_Struct FVector2D**, Axis3D -> PC_Struct FVector. Wiring that
//   pin into UEnhancedInputLibrary::BreakInputActionValue (whose InActionValue is
//   FInputActionValue) is refused by the schema - "only fully matching structs are
//   compatible" (run 20261023_1141) - so an Axis2D action is read with
//   UKismetMathLibrary::BreakVector2D(FVector2D InVec, out double& X, out double& Y) : void
//   [Pure] @KismetMathLibrary.h:992, which is the blueprint form of the doc's
//   "Get Value as Vector 2D -> Break". The X component is printed through
//   UKismetStringLibrary::Conv_DoubleToString(double InDouble) : FString
//   (KismetStringLibrary.h:20, DisplayName "To String (Float)"; a Conv_FloatToString does
//   not exist in 5.0.3 — the float spelling is this one).
// - Mapping context: AddMappingContext lives on the enhanced-input LocalPlayer subsystem
//   (EnhancedInputSubsystemInterface.h:116), reached through add_blueprint_node kind "subsystem"
//   (UK2Node_GetSubsystem, which resolves the class by name with a U-prefix retry). The context
//   argument must be a real UInputMappingContext **asset** (doc Question 3 Step 1); like the action it
//   had no creation path — create_input_mapping_context was advertised but undispatched, and the
//   asset-IR reader only treats an IMC's "mappings" as ordering dependencies — so the handler is
//   added in this round (HandleCreateInputMappingContext) and the blueprint reaches the asset
//   through a variable defaulted to it.
// - Struct type text: the bridge emits and accepts "struct<Name>" for non-base struct types
//   (ForgeMindBlueprintUtils.cpp PinTypeToText :1429); the DSL side spells the value parameter
//   with the engine name Vector2D, matching the pin the event node actually allocates.
// - DSL side: [EnhancedInputAction("<action asset path>", Event = "Triggered")] lowers to the
//   same UK2Node_EnhancedInputAction event node the A side builds (type "EnhancedInputAction"
//   → kind "enhancedInputAction", asset through targetClass). Event selects which exec output
//   pin (started / ongoing / triggered / completed) the body chains from, and the handler's
//   value parameter resolves to the node's fixed "ActionValue" output pin — deliberately NOT
//   registered as an entry pin, because no such-named pin exists on that node.
//   The legacy [InputAction("X")] attribute stays what it always was: K2Node_InputActionEvent
//   keyed by a project ActionMappings name (Blueprint_InputEvents), a different engine node.
// - DSL style (user ruling 2026-10-23): a struct value is read by **member access** — `v.X`
//   / `d.Y` — never by calling the break function as a statement with out-parameters
//   (`BreakVector2D(v, out x, out y)` is not DSL syntax). The translator lowers the member
//   access through the engine's native break node (BreakVector2D for FVector2D, cached per
//   receiver so X and Y share one node) — the same graph the MCP side builds by hand.
//   `OperandTypeKind` gained a Vector2D kind so those members type as doubles, which is what
//   lets `v.X.ToString()` pick Conv_DoubleToString (same "enum missing a kind → silent
//   fallback" family as Vector / Text / bool).
// - Engine-name discipline (spec §6, unchanged): `BindEnhancedAction` and
//   `v.GetValueAsVector2D()` from the task doc are NOT used — neither exists in the index
//   (BindAction is native C++ with no UFUNCTION; the Vector2D accessor came later). The value
//   is read through the native break node reached by member access, as above.
//   `AddMappingContext` is kept because it is a genuine UFUNCTION(BlueprintCallable, Category="Input")
//   (EnhancedInputSubsystemInterface.h:116) — but it must be reached as a **subsystem-qualified
//   call** (user ruling 2026-10-23): the function is declared on
//   `UEnhancedInputSubsystemInterface`, and a blueprint has no interface instance to be self,
//   so the bare form is now a translation error (run 20261023_1443:
//   "Could not call 'AddMappingContext': it is declared on the interface …, which has no
//   instance in a blueprint"). The B side therefore writes
//   `EnhancedInputLocalPlayerSubsystem.AddMappingContext(IMC_Default, 0)`, which the
//   translator lowers to the same graph as the A side: a Subsystem node (UK2Node_GetSubsystem,
//   bridge kind "subsystem") feeding the call's __target.
//
// Mis-touch comparison (VerifyIrSnapshot): inserted per user instruction (collaboration
// spec point 9) after the case was visually confirmed. Each A-side (MCP) and DSL-side
// asset stores its own graph-level IR baseline on first run (no failure); later runs diff
// against it, catching accidental damage to already-tested structures (per-asset
// self-comparison, not A/B cross-comparison). Every A-side blueprint here is compiled twice
// (once right after creation, once after the nodes are wired), so the snapshot sits after the
// FINAL compile only — baselining the half-built graph would make the finished one diff
// against its own skeleton. The two plugin assets (IA_Move / IMC_Default) are deliberately
// not snapshotted: they are not blueprint graphs, so get_asset_ir level=graph has nothing to
// read for them.
// =========================================================================

public sealed class Blueprint_EnhancedInput : ITestCase
{
    public string Name => "Blueprint Enhanced Input (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 70;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: bind an enhanced input action --
        // MCP steps: IA_Move : InputAction ASSET (doc step 1; the event node binds an asset,
        //   not a typed variable) + EInput_Bind_A (Extends Actor) + the EnhancedInputAction
        //   event node -> its "triggered" exec out reads the action value and prints one
        //   component. The node is itself the flow entry: there is no BeginPlay -> call wiring
        //   and no OnMove custom event in the A-side graph (see file header).
        const string ia1 = "/Game/Tests/UE5_0/IA_Move";
        BlueprintTestHelper.CreateInputAction(ctx, ia1);

        const string a1 = "/Game/Tests/UE5_0/EInput_Bind_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        BlueprintTestHelper.CompileAndSave(ctx, a1);

        var bind1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "IA_Move", 0, 0,
            kind: "enhancedInputAction", targetClass: ia1);
        var break1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "BreakVector2D",
            250, -150, "KismetMathLibrary");
        var conv1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "Conv_DoubleToString",
            450, -150, "KismetStringLibrary");
        var print1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "PrintString", 650, 0,
            "KismetSystemLibrary");
        // The event node's ActionValue pin is already an FVector2D (the action's Axis2D
        // ValueType), so it is broken apart with BreakVector2D - feeding it to
        // BreakInputActionValue (FInputActionValue) is refused by the schema (see header).
        // "X" / "Y" are that node's outputs; the value itself has no string conversion, so
        // subtask 1 prints one component.
        BlueprintTestHelper.Connect(ctx, a1, bind1.nodeId, "ActionValue", break1.nodeId, "InVec");
        BlueprintTestHelper.Connect(ctx, a1, break1.nodeId, "X", conv1.nodeId, "InDouble");
        BlueprintTestHelper.Connect(ctx, a1, conv1.nodeId, "ReturnValue", print1.nodeId, "InString");
        // Exec flow starts at the trigger pin (the node has no input exec pin).
        BlueprintTestHelper.Connect(ctx, a1, bind1.nodeId, "triggered", print1.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (same enhanced-input event node as the A side)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/EInput_Bind_B");

        // -- Subtask 2: read the input value and feed movement --
        // MCP steps: reuse the IA_Move ASSET from subtask 1 (doc step 1 "Reuse IA_Move (value type
        //   Vector2D)"; Axis2D is what CreateInputAction writes) + EInput_Value_A
        //   (Extends Character) + the EnhancedInputAction event node -> ActionValue
        //   -> BreakInputActionValue -> MakeVector(X, Y, 0) -> AddMovementInput(ScaleValue = 1.0),
        //   chained from the "triggered" exec output (the node has no input exec pin).
        const string a2 = "/Game/Tests/UE5_0/EInput_Value_A";
        BlueprintTestHelper.CreateBlueprint(ctx, a2, "Character");
        BlueprintTestHelper.CompileAndSave(ctx, a2);

        var bind2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "IA_Move", 0, 0,
            kind: "enhancedInputAction", targetClass: ia1);
        var break2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "BreakVector2D",
            250, -150, "KismetMathLibrary");
        var mkVec2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "MakeVector", 500, -150,
            "KismetMathLibrary");
        var move2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "AddMovementInput", 750, 0);
        BlueprintTestHelper.Connect(ctx, a2, bind2.nodeId, "ActionValue", break2.nodeId, "InVec");
        BlueprintTestHelper.Connect(ctx, a2, break2.nodeId, "X", mkVec2.nodeId, "X");
        BlueprintTestHelper.Connect(ctx, a2, break2.nodeId, "Y", mkVec2.nodeId, "Y");
        BlueprintTestHelper.SetPinDefault(ctx, a2, mkVec2.nodeId, "Z", "0.0");
        BlueprintTestHelper.Connect(ctx, a2, mkVec2.nodeId, "ReturnValue", move2.nodeId,
            "WorldDirection");
        BlueprintTestHelper.SetPinDefault(ctx, a2, move2.nodeId, "ScaleValue", "1.0");
        BlueprintTestHelper.Connect(ctx, a2, bind2.nodeId, "triggered", move2.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (same enhanced-input event node as the A side)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/EInput_Value_B");

        // -- Subtask 3: add an input mapping context --
        // MCP steps: IMC_Default : InputMappingContext ASSET (doc step 1) + EInput_CTX_A
        //   (Extends Actor, variable IMC_Default defaulted to that asset) +
        //   BeginPlay -> EnhancedInputLocalPlayerSubsystem -> Add Mapping Context(Priority = 0)
        const string imc3 = "/Game/Tests/UE5_0/IMC_Default";
        BlueprintTestHelper.CreateInputMappingContext(ctx, imc3);

        const string a3 = "/Game/Tests/UE5_0/EInput_CTX_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        // The context reaches AddMappingContext through a variable whose default is the asset —
        // the blueprint-facing form of "use this IMC" (the node's MappingContext pin is a plain
        // object reference, so an unwired variable would hand the call None at runtime). The
        // default is written in object form ("<package>.<asset>"): the pin's default-value parser
        // only accepts a valid object path, not a bare package path.
        BlueprintTestHelper.AddVariable(ctx, a3, "IMC_Default", "InputMappingContext",
            defaultValue: $"{imc3}.IMC_Default");
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        var begin3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "ReceiveBeginPlay", 0, 0);
        var getImc3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "IMC_Default", 100, 150,
            kind: "getter");
        var sub3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "EnhancedInputLocalPlayerSubsystem",
            250, -150, kind: "subsystem");
        var addCtx3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "AddMappingContext", 500, 0,
            "EnhancedInputLocalPlayerSubsystem");
        BlueprintTestHelper.Connect(ctx, a3, sub3.nodeId, "ReturnValue", addCtx3.nodeId, "__target");
        BlueprintTestHelper.Connect(ctx, a3, getImc3.nodeId, "IMC_Default", addCtx3.nodeId,
            "MappingContext");
        BlueprintTestHelper.SetPinDefault(ctx, a3, addCtx3.nodeId, "Priority", "0");
        BlueprintTestHelper.Connect(ctx, a3, begin3.nodeId, "then", addCtx3.nodeId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/EInput_CTX_B");

        await Task.CompletedTask;
    }
}
