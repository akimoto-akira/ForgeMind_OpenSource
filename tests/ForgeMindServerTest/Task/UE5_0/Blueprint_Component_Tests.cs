// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 73 - Blueprint Component (UE 5.0) - from Tasks/UE_5.0/Blueprint_Component.md
//
// Probe (collaboration spec point 6):
// - Design-time components are managed through the blueprint's SimpleConstructionScript:
//   add_blueprint_component takes path / class / name, plus two optional fields this file needs —
//     parent: parent component name for the attachment hierarchy (defaults to the root scene
//             node; USCS_Node::AddChildNode, ForgeMindBridgeClient_BlueprintComponent.cpp:190-239)
//     properties: initial values imported into the component template (FProperty::ImportText,
//             :241-287) — this is the only way to set UChildActorComponent::ChildActorClass.
//   ResolveComponentClass rejects abstract classes and anything not derived from UActorComponent,
//   so StaticMeshComponent / SceneComponent / ChildActorComponent all resolve.
// - BlueprintTestHelper.AddComponent covers the plain form only (path/class/name), so subtasks 2
//   and 3 send add_blueprint_component directly through BlueprintTestHelper.EditorCall (the same
//   raw-call form Blueprint_Variable / Blueprint_Bitmask use for fields the helper does not wrap).
//   Anonymous-object payloads are camel-cased on serialization; FProperty lookup by name is
//   case-insensitive, so ChildActorClass still lands on the right property.
// - DSL side: [Component] is implemented (Blueprint_ConstructionScript / Blueprint_Particle) and
//   the task doc's two extra parameters are now wired through (user visual bug 2026-10-23:
//   Mesh and Pivot showed as siblings in the Components panel). Both were being dropped silently
//   by the translator even though the IR layer and the bridge already supported them:
//     Attach = "Pivot"    → IR component parent → add_blueprint_component parent
//                           (USCS_Node::AddChildNode) — the same field subtask 2's MCP side sends;
//     ChildActor = typeof(X) → IR component properties { ChildActorClass: X } — the same payload
//                           the MCP side sends for subtask 3.
//   Attach names are validated against the components declared in the same block (an unknown name
//   used to fail the same silent way: component still created, still a sibling), and update mode
//   forwards parent too — the IR patch branch was missing it while create mode had it.
//
// Mis-touch comparison (VerifyIrSnapshot): inserted per user instruction (collaboration
// spec point 9). Each A-side (MCP) and DSL-side asset stores its own IR baseline on first run
// (no failure); later runs diff against it (per-asset self-comparison, not A/B cross-comparison).
// Making the baselines meaningful for this case required a read-back fix first: get_asset_ir
// emitted definition.components with name/type only, so the snapshot could not distinguish
// "Mesh under Pivot" from "Mesh beside Pivot" — the exact symptom reported. It now also emits
// parent, but only for a NON-DEFAULT parent: add_blueprint_component without an explicit
// parent hangs the node under the default scene root and reading it back re-applies with the
// same default, so that attachment is implicit and stays unreported (user ruling 2026-10-25:
// baselines of cases before No. 70 keep matching untouched; only an explicit attach to a
// non-default component carries information the IR cannot reconstruct — this case's
// Pivot→Mesh wire). Per that ruling this case's baselines were removed for re-seeding:
// the first run after the removal saves the new baselines (no failure), the run after
// that must match them.
// Boundary stated on purpose: ChildActorClass is a component-template property and is still not
// part of the graph-level read-back, so subtask 3's child class is covered by the bridge's own
// 'added' check and visual inspection, not by the snapshot.
// =========================================================================

public sealed class Blueprint_Component : ITestCase
{
    public string Name => "Blueprint Component (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 73;

    public async Task Run(TestContext ctx)
    {
        // -- Subtask 1: add a static mesh component --
        // MCP steps: Comp_Mesh_A (Extends Actor) + Add Component: StaticMeshComponent "Mesh"
        const string a1 = "/Game/Tests/UE5_0/Comp_Mesh_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        BlueprintTestHelper.AddComponent(ctx, a1, "Mesh", "StaticMeshComponent");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Comp_Mesh_B");

        // -- Subtask 2: add a scene component and attach a mesh under it --
        // MCP steps: Comp_Scene_A (Extends Actor) + SceneComponent "Pivot"
        //   + StaticMeshComponent "Mesh" attached to Pivot
        const string a2 = "/Game/Tests/UE5_0/Comp_Scene_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        BlueprintTestHelper.AddComponent(ctx, a2, "Pivot", "SceneComponent");
        // Attachment goes through add_blueprint_component's 'parent' field - the helper has no
        // parameter for it (see file header).
        var addMesh2 = BlueprintTestHelper.EditorCall(ctx, "add_blueprint_component", new
        {
            path = a2,
            @class = "StaticMeshComponent",
            name = "Mesh",
            parent = "Pivot",
        });
        ctx.AssertTrue(addMesh2["response"]?["added"]?.GetValue<bool>() ?? false,
            $"add_blueprint_component Mesh (parent Pivot): {addMesh2["response"]?["reason"]?.GetValue<string>() ?? ""}");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Comp_Scene_B");

        // -- Subtask 3: add a child actor component and set its child class --
        // MCP steps: Comp_Child_A (Extends Actor) + ChildActorComponent "Spawner"
        //   with Child Actor Class = Actor
        const string a3 = "/Game/Tests/UE5_0/Comp_Child_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        // ChildActorClass is a property of the component template, set through the 'properties'
        // object (see file header).
        var addChild3 = BlueprintTestHelper.EditorCall(ctx, "add_blueprint_component", new
        {
            path = a3,
            @class = "ChildActorComponent",
            name = "Spawner",
            properties = new { ChildActorClass = "Actor" },
        });
        ctx.AssertTrue(addChild3["response"]?["added"]?.GetValue<bool>() ?? false,
            $"add_blueprint_component Spawner: {addChild3["response"]?["reason"]?.GetValue<string>() ?? ""}");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        // DSL path (copied from task doc)
        BlueprintTestHelper.ApplyDsl(ctx, "");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/Comp_Child_B");

        await Task.CompletedTask;
    }
}
