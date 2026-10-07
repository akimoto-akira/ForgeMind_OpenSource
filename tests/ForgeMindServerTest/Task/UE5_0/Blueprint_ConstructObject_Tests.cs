// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

namespace ForgeMindServer.Test.Task.UE5_0;

using Task = System.Threading.Tasks.Task;

// =========================================================================
// No. 18 - Blueprint Construct Object (UE 5.0) - from Tasks/UE_5.0/blueprint-construct-object.md
// Probe (spec point 6): Construct Object from Class (UK2Node_ConstructObjectFromClass)
// has no implemented lowering evidence in bridge/DSL translator (NewObject only seen in regex
// for other domain assets, blueprint domain not hit). MCP step node/pin names and DSL forms are
// **pending bridge confirmation** (requires prerequisite UObject subclass MyData). C# guarantees
// compilable; runtime node recognition needs backfill.
//
// 2026-10-03 Tested (TestRuns/20261003_0052) finalized:
//   1. Translator TryNewObjectCall lowers NewObject<T>(this) to ConstructObjectFromClass,
//      bridge expands to CallFunction:SpawnObject (UGameplayStatics) -- its ReturnValue is
//      **UObject(Object)**, not T.
//   2. Therefore Object -> MyData is base->derived, UE forbids implicit connect: original DSL `return
//      NewObject<MyData>(this);` directly connecting result.ReturnValue(MyData) reports
//      BP_PIN_TYPE_MISMATCH (node_002.ReturnValue -> node_003.ReturnValue).
//   3. Fix (aligning with SaveGame domain Save_Load_A/B verified form): add explicit Cast on both sides.
//      - B-side DSL: `MyData d = NewObject<MyData>(this) as MyData;` -- note `as` is only
//        supported in **local declarations**; `return X as T;` is not supported (return statement
//        case only handles Invocation/Identifier/binary ops, falls to default warning and the call
//        node is not built at all),
//        so assign local var first then `return d;`.
//      - A-side MCP: insert Cast<MyData> after ctor (following Cast<BP_Save> precedent).
//   4. Cast output pin name follows BP class precedent "ReturnValue" (Cast<APawn> engine class is "AsPawn");
//      MyData is a blueprint class, targetClass takes bare name "MyData" -- both pending this run's review.
// Note: diff comparison (VerifyIrSnapshot) covers all A-side and B-side assets.
//
// 2026-10-03 Retrospective (TestRuns/20261003_1336 + baseline 0107): previously changing B-side return type/
// array variable to Object/Object[] was a **workaround** not root cause fix -- the real root cause was bridge
// BaseTypeTextToPinType's blueprint class static cache built only once, under full group run the first cache
// build happened before MyData creation, and runtime-created blueprint classes always miss ->
// create_function_graph function shell was dropped (functionCount=0). Fixed: retry once on cache miss.
// DSL restored to MyData / MyData[] form (consistent with 0107 archived baseline, user decision follows baseline).
// =========================================================================

public sealed class Blueprint_ConstructObject : ITestCase
{
    public string Name => "Blueprint Construct Object (UE 5.0)";
    public string? Group => "UE5.0";
    public int Order => 18;

    public async Task Run(TestContext ctx)
    {
        // Prerequisite: UObject subclass MyData (with variable V : int). GroupLifecycle clears
        // Content/Tests on each run, so recreate every time.
        const string myData = "/Game/Tests/UE5_0/MyData";
        BlueprintTestHelper.CreateBlueprint(ctx, myData, "Object");
        BlueprintTestHelper.AddVariable(ctx, myData, "V", "int");
        BlueprintTestHelper.CompileAndSave(ctx, myData);

        // -- Subtask 1: Construct UObject --
        // MCP steps: NewObj_Basic_A (Extends Actor) + function Mk:
        //   ConstructObjectFromClass(MyData) -> Return
        const string a1 = "/Game/Tests/UE5_0/NewObj_Basic_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a1);
        var f1 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a1, "Mk",
            Array.Empty<object>(), outputs: new[] { new { name = "ReturnValue", type = "Object" } });
        // Bridge ConstructObjectFromClass expands to CallFunction:SpawnObject,
        // pin name is ObjectClass (not the original K2Node's Class).
        var ctor1 = BlueprintTestHelper.AddNode(ctx, a1, "Mk", "ConstructObjectFromClass", 300, 0);
        BlueprintTestHelper.SetPinDefault(ctx, a1, ctor1.nodeId, "ObjectClass", "/Game/Tests/UE5_0/MyData.MyData_C");
        // SpawnObject returns UObject: must Cast before connecting to MyData pin (Object->MyData
        // base->derived forbids implicit connect). Cast output pin name follows BP class precedent "ReturnValue".
        var cast1 = BlueprintTestHelper.AddNode(ctx, a1, "Mk", "Cast<MyData>", 500, 0);
        BlueprintTestHelper.Connect(ctx, a1, f1.entryId, "then", ctor1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, ctor1.nodeId, "then", cast1.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a1, ctor1.nodeId, "ReturnValue", cast1.nodeId, "Object");
        BlueprintTestHelper.Connect(ctx, a1, cast1.nodeId, "ReturnValue", f1.resultId, "ReturnValue");
        BlueprintTestHelper.Connect(ctx, a1, cast1.nodeId, "then", f1.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a1);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a1);

        // `as` is only supported in local declarations (return X as T is lowered to warning), so assign local var first.
        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 2: Construct and Set Property --
        // MCP steps: NewObj_Set_A + function Make: ConstructObjectFromClass(MyData)
        //   -> set V=10 -> Return
        const string a2 = "/Game/Tests/UE5_0/NewObj_Set_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a2);
        var f2 = BlueprintTestHelper.CreateFunctionWithInputs(ctx, a2, "Make",
            Array.Empty<object>(), outputs: new[] { new { name = "ReturnValue", type = "Object" } });
        var ctor2 = BlueprintTestHelper.AddNode(ctx, a2, "Make", "ConstructObjectFromClass", 300, 0);
        BlueprintTestHelper.SetPinDefault(ctx, a2, ctor2.nodeId, "ObjectClass", "/Game/Tests/UE5_0/MyData.MyData_C");
        // ctor's UObject result must be Cast to MyData first, to connect to MyData setter target pin.
        var cast2 = BlueprintTestHelper.AddNode(ctx, a2, "Make", "Cast<MyData>", 450, 0);
        BlueprintTestHelper.Connect(ctx, a2, f2.entryId, "then", ctor2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, ctor2.nodeId, "then", cast2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, ctor2.nodeId, "ReturnValue", cast2.nodeId, "Object");
        // Set V=10: external member setter (ownerClass=MyData, "__target" connects Cast result).
        var setV2 = BlueprintTestHelper.AddNode(ctx, a2, "Make", "V", 700, 0, "MyData", kind: "setter");
        BlueprintTestHelper.SetPinDefault(ctx, a2, setV2.nodeId, "V", "10");
        BlueprintTestHelper.Connect(ctx, a2, cast2.nodeId, "ReturnValue", setV2.nodeId, "__target");
        BlueprintTestHelper.Connect(ctx, a2, cast2.nodeId, "then", setV2.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, setV2.nodeId, "then", f2.resultId, "execute");
        BlueprintTestHelper.Connect(ctx, a2, cast2.nodeId, "ReturnValue", f2.resultId, "ReturnValue");
        BlueprintTestHelper.CompileAndSave(ctx, a2);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a2);

        BlueprintTestHelper.ApplyDsl(ctx, "");

        // -- Subtask 3: Construct and Add to Array --
        // MCP steps: NewObj_Arr_A (Extends Actor) + variable Pool: MyData[] + function Add:
        //   ConstructObjectFromClass(MyData) -> Add to Pool -> PrintString(Pool.Length)
        const string a3 = "/Game/Tests/UE5_0/NewObj_Arr_A";
        BlueprintTestHelper.CreateBlueprintActor(ctx, a3);
        BlueprintTestHelper.AddVariable(ctx, a3, "Pool", "Object[]");
        var f3 = BlueprintTestHelper.CreateFunction(ctx, a3, "Add");
        var ctor3 = BlueprintTestHelper.AddNode(ctx, a3, "Add", "ConstructObjectFromClass", 300, 0);
        BlueprintTestHelper.SetPinDefault(ctx, a3, ctor3.nodeId, "ObjectClass", "/Game/Tests/UE5_0/MyData.MyData_C");
        // Constructor result Cast to MyData then added to array (A-side Pool declared as Object[],
        // MyData->Object derived->base implicit connect allowed, so TargetArray unaffected).
        var cast3 = BlueprintTestHelper.AddNode(ctx, a3, "Add", "Cast<MyData>", 450, 0);
        BlueprintTestHelper.Connect(ctx, a3, f3.entryId, "then", ctor3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, ctor3.nodeId, "then", cast3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, ctor3.nodeId, "ReturnValue", cast3.nodeId, "Object");
        // Add to Pool (Array_Add: KismetArrayLibrary, TargetArray + NewItem pins);
        // then PrintString(Array_Length(Pool)).
        var add3 = BlueprintTestHelper.AddNode(ctx, a3, "Add", "Array_Add", 700, 0, "KismetArrayLibrary");
        var getPool3 = BlueprintTestHelper.AddNode(ctx, a3, "Add", "Pool", 550, 120, kind: "getter");
        BlueprintTestHelper.Connect(ctx, a3, getPool3.nodeId, "Pool", add3.nodeId, "TargetArray");
        BlueprintTestHelper.Connect(ctx, a3, cast3.nodeId, "ReturnValue", add3.nodeId, "NewItem");
        BlueprintTestHelper.Connect(ctx, a3, cast3.nodeId, "then", add3.nodeId, "execute");
        var len3 = BlueprintTestHelper.AddNode(ctx, a3, "Add", "Array_Length", 900, 0, "KismetArrayLibrary");
        BlueprintTestHelper.Connect(ctx, a3, getPool3.nodeId, "Pool", len3.nodeId, "TargetArray");
        var p3 = BlueprintTestHelper.AddNode(ctx, a3, "Add", "PrintString", 1200, 0, "KismetSystemLibrary");
        BlueprintTestHelper.Connect(ctx, a3, len3.nodeId, "ReturnValue", p3.nodeId, "InString");
        BlueprintTestHelper.Connect(ctx, a3, add3.nodeId, "then", p3.nodeId, "execute");
        BlueprintTestHelper.Connect(ctx, a3, p3.nodeId, "then", f3.resultId, "execute");
        BlueprintTestHelper.CompileAndSave(ctx, a3);
        BlueprintTestHelper.VerifyIrSnapshot(ctx, a3);

        BlueprintTestHelper.ApplyDsl(ctx, "");

        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/NewObj_Basic_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/NewObj_Set_B");
        BlueprintTestHelper.VerifyIrSnapshot(ctx, "/Game/Tests/UE5_0/NewObj_Arr_B");

        await Task.CompletedTask;
    }
}
