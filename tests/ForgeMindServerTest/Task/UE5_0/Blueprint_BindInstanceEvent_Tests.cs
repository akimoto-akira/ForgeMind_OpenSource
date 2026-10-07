// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 9 - Blueprint Bind Instance Event (UE 5.0) - from Tasks/UE_5.0/blueprint-bind-instance-event.md
// =========================================================================

public sealed class Blueprint_BindInstanceEvent : ITestCase
{
    public string Name => "Blueprint Bind Instance Event (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 9;

    public async Task Run(TestContext ctx)
    {
        // -- Support status probe conclusion (collaboration spec point 6) --
        // Engine semantics review (UE 5.0.3 source):
        // - UK2Node_ActorBoundEvent is a level blueprint dedicated node (EventOwner is serialized
        //   level Actor instance, no connectable pins, CanPasteHere limited to level blueprints) --
        //   The correct engine form for variable binding is AddDelegate + CreateDelegate combo
        //   (in regular blueprints, right-click Target -> Bind Event to OnActorBeginOverlap expansion);
        // - UK2Node_ComponentBoundEvent locates by this blueprint's SCS component name (no connectable pins)
        //   -> bridge needs new componentBoundEvent kind (already implemented);
        // - DSL `Target.OnActorBeginOverlap.Add(...)` instance event binding form is implemented
        //   (TryLowerDispatcherBinding form 2).

        // -- Subtask 1: Bind to Actor's BeginOverlap --
        // MCP steps: BindActor_A (Extends Actor) + variable Target: Actor
        //         -> actorBoundEvent node (binds Target's OnActorBeginOverlap,
        //           carries custom event OnOverlap, params other) -> print overlapping Actor's name
        const string a1 = "/Game/Tests/UE5_0/BindActor_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        BlueprintTestHelper.AddVariable(ctx, a1, "Target", "Actor");
        BlueprintTestHelper.CompileAndSave(ctx, a1);

        var ev1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "ReceiveBeginPlay", 0, 0);
        var getTarget1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "Target", 100, 120, kind: "getter");
        // "Bind Event to OnActorBeginOverlap" (Target=variable) engine expansion form:
        // CustomEvent OnOverlap(AActor other) -> CreateDelegate wrapping ->
        // AddDelegate OnActorBeginOverlap (Target pin connects variable, binds at BeginPlay).
        // "__target" resolved by bridge FindPinToIndex AddDelegate's self/Target pin.
        var onOverlap1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "OnOverlap", 600, 0,
            kind: "customevent", eventParams: new[]
            {
                new { name = "OverlappedActor", type = "Actor" },
                new { name = "OtherActor", type = "Actor" },
            });
        var bind1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "OnActorBeginOverlap", 300, -100, kind: "addDelegate");
        var create1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "CreateDelegate", 150, 120, function: "OnOverlap");
        BlueprintTestHelper.Connect(ctx, a1, create1.nodeId, "ReturnValue", bind1.nodeId, "Delegate");
        BlueprintTestHelper.Connect(ctx, a1, getTarget1.nodeId, "Target", bind1.nodeId, "__target");
        BlueprintTestHelper.Connect(ctx, a1, ev1.nodeId, "then", bind1.nodeId, "execute");

        // OnOverlap body: print overlapping Actor's name. GetName is not BlueprintCallable,
        // decompose by engine equivalent form: GetDisplayName (returns FString, KismetSystemLibrary.h
        // L116-117) -> PrintString(InString)
        var getName1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "GetDisplayName", 900, -100, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a1, onOverlap1.nodeId, "OtherActor", getName1.nodeId, "Object");
        var p1 = BlueprintTestHelper.AddNode(ctx, a1, "EventGraph", "PrintString", 1200, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a1, onOverlap1.nodeId, "then", p1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, getName1.nodeId, "ReturnValue", p1.nodeId, "InString");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (from task doc; instance event binding form pending -- test is spec.
        // Event signature corrected to dual param by engine real name: doc's single param OnOverlap(AActor other)
        // doesn't match FActorBeginOverlapSignature)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/BindActor_B");

        // -- Subtask 2: Bind to Component's BeginOverlap --
        // MCP steps: BindComp_A (Extends Actor) + Trigger component
        //         -> componentBoundEvent node (binds Trigger's OnComponentBeginOverlap,
        //           carries custom event OnHit) -> print overlapping Actor's name
        const string a2 = "/Game/Tests/UE5_0/BindComp_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        // add_blueprint_component class name field is 'class' (not 'type'); component class
        // must be a concrete class (editor SCS cannot instantiate abstract class), choose PrimitiveComponent's
        // concrete subclass BoxComponent -- OnComponentBeginOverlap is declared in parent class
        // UPrimitiveComponent, componentBoundEvent resolves along parent chain by signature real name.
        BlueprintTestHelper.AddComponent(ctx, a2, "Trigger", "BoxComponent");
        BlueprintTestHelper.CompileAndSave(ctx, a2);

        // UK2Node_ComponentBoundEvent: "Bind Event to OnComponentBeginOverlap".
        // Component located by SCS component variable name ('component' field, engine has no component pin);
        // Event signature output pins generated by engine from real name (OverlappedComponent / OtherActor /
        // OtherComp / ...), node event name is engine-generated BndEvt__ internal name.
        var bind2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "OnHit", 300, 0,
            kind: "componentBoundEvent", function: "OnComponentBeginOverlap",
            component: "Trigger");

        var getName2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "GetDisplayName", 600, -100, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, bind2.nodeId, "OtherActor", getName2.nodeId, "Object");
        var p2 = BlueprintTestHelper.AddNode(ctx, a2, "EventGraph", "PrintString", 900, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, bind2.nodeId, "then", p2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, getName2.nodeId, "ReturnValue", p2.nodeId, "InString");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (from task doc; signature corrected to six params by FComponentBeginOverlapSignature:
        // OverlappedComponent/OtherActor/OtherComp/
        // OtherBodyIndex/bFromSweep/SweepResult -- doc's dual params are draft notation)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/BindComp_B");

        // -- Subtask 3: Unbind --
        // MCP steps: BindUnbind_A (Extends Actor) + variable Target: Actor
        //         -> Function Stop: "Unbind Event from OnActorBeginOverlap" (Target=Target,
        //           event=OnOverlap). Isomorphic with Disp_Unbind_A: CustomEvent OnOverlap
        //           wrapped into delegate value via CreateDelegate, connects to RemoveDelegate "Delegate" pin.
        const string a3 = "/Game/Tests/UE5_0/BindUnbind_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "Target", "Actor");
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        var onOverlap3 = BlueprintTestHelper.AddNode(ctx, a3, "EventGraph", "OnOverlap", 600, 0,
            kind: "customevent", eventParams: new[]
            {
                new { name = "OverlappedActor", type = "Actor" },
                new { name = "OtherActor", type = "Actor" },
            });
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Stop");
        var getTarget3 = BlueprintTestHelper.AddNode(ctx, a3, "Stop", "Target", 100, 240, kind: "getter");
        // "Unbind Event from OnActorBeginOverlap" (UK2Node_RemoveDelegate acts on
        // instance): removeDelegate resolves inherited FMulticastDelegateProperty by name
        // (AActor.OnActorBeginOverlap, UClass::FindPropertyByName looks up along parent chain);
        // "__target" connection points to the unbound instance (bridge FindPinToIndex -> self/Target pin).
        var unbind3 = BlueprintTestHelper.AddNode(ctx, a3, "Stop", "OnActorBeginOverlap", 300, 0, kind: "removeDelegate");
        var create3 = BlueprintTestHelper.AddNode(ctx, a3, "Stop", "CreateDelegate", 150, 120, function: "OnOverlap");
        BlueprintTestHelper.Connect(ctx, a3, create3.nodeId, "ReturnValue", unbind3.nodeId, "Delegate");
        BlueprintTestHelper.Connect(ctx, a3, getTarget3.nodeId, "Target", unbind3.nodeId, "__target");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", unbind3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, unbind3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (from task doc; signature corrected to dual params by engine real name -- same as subtask 1)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/BindUnbind_B");

        await Task.CompletedTask;
    }
}
