// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

using UnrealBuildTool;

public class ForgeMindForUE : ModuleRules
{
    public ForgeMindForUE(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = ModuleRules.PCHUsageMode.UseExplicitOrSharedPCHs;

        PublicIncludePaths.AddRange(
            new string[] {
				// ... add public include paths required here ...
			}
            );


        PrivateIncludePaths.AddRange(
    new string[] {
    }
    );


        PublicDependencyModuleNames.AddRange(
            new string[]
            {
                "Core",
				// ... add other public dependencies that you statically link with here ...
			}
            );


        PrivateDependencyModuleNames.AddRange(
            new string[]
            {
                "CoreUObject",
                "Engine",
                "RenderCore",
                "RHI",
                "Slate",
                "SlateCore",
			// Configurable hotkey graph layout: FInputChord / EKeys key matching
			"InputCore",
				// ForgeMind bridge: TCP channel, JSON envelope, .uproject path
				"Networking",
                "Sockets",
                "Json",
				// get_actor_details: FJsonObjectConverter reflection-based property dump
				"JsonUtilities",
                "Projects",
				// Editor state + command handlers (open_asset / exec_console)
				"UnrealEd",
				// quit_editor: IMainFrameModule::RequestCloseEditor graceful shutdown
				"MainFrame",
				// create_blueprint: FAssetRegistryModule::AssetCreated registration
				"AssetRegistry",
				// add_blueprint_variable: UEdGraphSchema_K2 pin category constants
				"BlueprintGraph",
			// Wire style: FKismetConnectionDrawingPolicy + FNodeFactory injection
			"GraphEditor",
			// Material graph wire style: FSubstrateWidget, UMaterialGraphSchema
			"MaterialEditor",
				// Wire style settings surfaced in Editor Preferences
			"DeveloperSettings",
			// create_blueprint: UWidgetBlueprint (WidgetBlueprint.h lives in UMGEditor)
			"UMGEditor",
			// create_blueprint: UWidgetBlueprintGeneratedClass / UUserWidget
			"UMG",
			// apply_asset_ir: IAssetTools for factory-driven asset creation
			"AssetTools",
			// apply_asset_ir: PhysicalMaterials/PhysicalMaterial.h
			"PhysicsCore",
			// apply_asset_ir: UBehaviorTree (BehaviorTree.h)
			"AIModule",
			// apply_asset_ir: UBehaviorTreeFactory (BehaviorTreeFactory.h)
			"BehaviorTreeEditor",
			// main menu entry + About window (UToolMenus / SWindow)
			"ToolMenus",
				// ... add private dependencies that your module statically link with here --
			}
            );


        DynamicallyLoadedModuleNames.AddRange(
            new string[]
            {
				// ... add any modules that your module loads dynamically here ...
			}
            );
    }
}