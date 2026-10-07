// Copyright Epic Games, Inc. All Rights Reserved.

using UnrealBuildTool;
using System.Collections.Generic;

public class ForgeMind_5_0Target : TargetRules
{
	public ForgeMind_5_0Target( TargetInfo Target) : base(Target)
	{
		Type = TargetType.Game;
		DefaultBuildSettings = BuildSettingsVersion.V2;
		ExtraModuleNames.AddRange( new string[] { "ForgeMind_5_0" } );
		// ForgeMindForUE IR_*.cpp files define same-name helper functions in anonymous namespaces
		// (Bridge/BridgeBool/MakePayload); unity build merge would cause redefinition conflicts --
		// this plugin is written assuming isolated compilation units, disable unity build
		bUseUnityBuild = false;
	}
}
