// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 69 - Blueprint Runtime Component (UE 5.0) - from Tasks/UE_5.0/Blueprint_RuntimeComponent.md
//
// Probe (collaboration spec point 6):
// - "AddComponent" is one of the bridge's native flow nodes (IsNativeFlowNodeName /
//   CreateNativeFlowNode), created by name with the component class passed through the payload
//   **targetClass** field — there is no Class pin (verified on Ctor_Loop_A, see
//   Blueprint_ConstructionScript). Its RelativeTransform pin has no default, so the A side feeds
//   it a MakeTransform identity transform — the same thing the DSL translator emits when a
//   AddComponent call omits the transform ("Default transform (FTransform=0) falls back to default identity transform").
// - The node's component output pin is "ReturnValue" (UK2Node_AddComponent), and member calls on
//   the resulting component use "__target" (the external-member form used by
//   Blueprint_Particle / Blueprint_ConstructionScript).
// - Real UFUNCTION names (engine index 5.0):
//     AActor::AddComponentByClass(TSubclassOf<UActorComponent> Class, bool bManualAttachment,
//       const FTransform& RelativeTransform, bool bDeferredFinish) : UActorComponent*  @Actor.h:1556
//     UStaticMeshComponent::SetStaticMesh(UStaticMesh* NewMesh) : bool  @StaticMeshComponent.h:395
//     USceneComponent::K2_AttachToComponent(Parent, SocketName, LocationRule, RotationRule,
//       ScaleRule, bWeldSimulatedBodies) : bool  @SceneComponent.h:745
//       (AActor has a same-named overload @Actor.h:1586 — passing class "SceneComponent"
//        selects the component one.)
// - **TSubclassOf variable gap**: add_blueprint_variable's type parser has no class-reference
//   branch (BaseTypeTextToPinType only has primitives / structs / soft refs; anything else
//   becomes PC_Object), so `Cls : Class` cannot be declared as the doc asks. Subtask 2 declares
//   it as an object-reference variable and still wires it to the Class pin — the resulting type
//   mismatch is the pending requirement (a PC_Class branch + a class-reference type text).
// - DSL side: `AddComponent<T>()` is implemented (LowerAddComponent, typeof / generic forms), but
//   binding its return value (`StaticMeshComponent c = AddComponent<StaticMeshComponent>();`) and
//   `AddComponentByClass(Cls)` are not — kept verbatim as the requirement spec.
// =========================================================================

public sealed class Blueprint_RuntimeComponent : ITestCase
{
    public string Name => "Blueprint Runtime Component (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 69;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: add a component of a static class and configure it --
        // MCP steps: AddComp_A (Extends Actor) + SM : StaticMesh
        //   function AddMesh -> AddComponent(StaticMeshComponent) -> SetStaticMesh(SM)
        const string a1 = "/Game/Tests/UE5_0/AddComp_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        BlueprintTestHelper.AddVariable(ctx, a1, "SM", "StaticMesh");
        BlueprintTestHelper.CompileAndSave(ctx, a1);

        var f1 = BlueprintTestHelper.CreateFunction(ctx, a1, "AddMesh");
        var addC1 = BlueprintTestHelper.AddNode(ctx, a1, "AddMesh", "AddComponent", 300, 0,
            targetClass: "/Script/Engine.StaticMeshComponent");
        // RelativeTransform has no default: identity transform (same as Ctor_Loop_A and as the
        // DSL translator's implicit default).
        var mkTf1 = BlueprintTestHelper.AddNode(ctx, a1, "AddMesh", "MakeTransform", 150, 120,
            "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a1, mkTf1.nodeId, "Location", "0,0,0");
        var getSm1 = BlueprintTestHelper.AddNode(ctx, a1, "AddMesh", "SM", 150, 240, kind: "getter");
        var setMesh1 = BlueprintTestHelper.AddNode(ctx, a1, "AddMesh", "SetStaticMesh", 550, 0,
            "StaticMeshComponent");
        BlueprintTestHelper.Connect(ctx, a1, mkTf1.nodeId, "ReturnValue", addC1.nodeId,
            "RelativeTransform");
        BlueprintTestHelper.Connect(ctx, a1, addC1.nodeId, "ReturnValue", setMesh1.nodeId,
            "__target");
        BlueprintTestHelper.Connect(ctx, a1, getSm1.nodeId, "SM", setMesh1.nodeId, "NewMesh");
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", addC1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, addC1.nodeId, "then", setMesh1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, setMesh1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 2: add a component whose class comes from a variable --
        // MCP steps: AddCompBy_A (Extends Actor) + Cls : ActorComponent
        //   function Add -> AddComponentByClass(Cls) -> PrintString(GetDisplayName(c))
        const string a2 = "/Game/Tests/UE5_0/AddCompBy_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        // Type text falls back to an object reference — no class-reference branch exists yet
        // (see file header).
        BlueprintTestHelper.AddVariable(ctx, a2, "Cls", "ActorComponent");
        BlueprintTestHelper.CompileAndSave(ctx, a2);

        var f2 = BlueprintTestHelper.CreateFunction(ctx, a2, "Add");
        var getCls2 = BlueprintTestHelper.AddNode(ctx, a2, "Add", "Cls", 100, -100, kind: "getter");
        var addBy2 = BlueprintTestHelper.AddNode(ctx, a2, "Add", "AddComponentByClass", 350, 0);
        var name2 = BlueprintTestHelper.AddNode(ctx, a2, "Add", "GetDisplayName", 550, -100,
            "KismetSystemLibrary");
        var print2 = BlueprintTestHelper.AddNode(ctx, a2, "Add", "PrintString", 750, 0,
            "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a2, getCls2.nodeId, "Cls", addBy2.nodeId, "Class");
        BlueprintTestHelper.Connect(ctx, a2, addBy2.nodeId, "ReturnValue", name2.nodeId, "Object");
        BlueprintTestHelper.Connect(ctx, a2, name2.nodeId, "Return Value", print2.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", addBy2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, addBy2.nodeId, "then", print2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, print2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a2);

        // DSL path (copied from task doc; c.GetName() is not a UFUNCTION — GetDisplayName, same
        // as Blueprint_SoftRef)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 3: add a component at runtime and attach it --
        // MCP steps: AddCompAttach_A (Extends Actor, Root : SceneComponent)
        //   function Add -> AddComponent(SceneComponent) -> K2_AttachToComponent(Root)
        const string a3 = "/Game/Tests/UE5_0/AddCompAttach_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddComponent(ctx, a3, "Root", "SceneComponent");
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Add");
        var addC3 = BlueprintTestHelper.AddNode(ctx, a3, "Add", "AddComponent", 300, 0,
            targetClass: "/Script/Engine.SceneComponent");
        var mkTf3 = BlueprintTestHelper.AddNode(ctx, a3, "Add", "MakeTransform", 150, 120,
            "KismetMathLibrary");
        BlueprintTestHelper.SetPinDefault(ctx, a3, mkTf3.nodeId, "Location", "0,0,0");
        var getRoot3 = BlueprintTestHelper.AddNode(ctx, a3, "Add", "Root", 150, 240, kind: "getter");
        // "Attach Component To Component": USceneComponent::K2_AttachToComponent.
        var attach3 = BlueprintTestHelper.AddNode(ctx, a3, "Add", "K2_AttachToComponent", 550, 0,
            "SceneComponent");
        BlueprintTestHelper.Connect(ctx, a3, mkTf3.nodeId, "ReturnValue", addC3.nodeId,
            "RelativeTransform");
        BlueprintTestHelper.Connect(ctx, a3, addC3.nodeId, "ReturnValue", attach3.nodeId,
            "__target");
        BlueprintTestHelper.Connect(ctx, a3, getRoot3.nodeId, "Root", attach3.nodeId, "Parent");
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", addC3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, addC3.nodeId, "then", attach3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, attach3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");

        await Task.CompletedTask;
    }
}
