// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#pragma once

#include "CoreMinimal.h"
#include "Dom/JsonObject.h"

class UClass;
/**
 * UE 5.0 specialization: no FindFirstObject (introduced in 5.1), uniformly delegates
 * to FindObject(ANY_PACKAGE, ...) (semantically equivalent: find first among loaded objects).
 */
template <typename T>
T* ForgeMindFindFirstObject(const TCHAR* Name)
{
	return FindObject<T>(ANY_PACKAGE, Name);
}

/**
 * Resolve the generated UClass by bare blueprint name (e.g. "BP_Base"): first check memory for "BP_Base_C",
 * then load blueprint of same name from known asset roots (/Game/Tests, /Game) to get GeneratedClass.
 * create_blueprint's parentClass and add_blueprint_node's class field share the same resolution
 * chain (defined in ForgeMindBridgeClient.cpp).
 */
UClass* ForgeMindResolveBlueprintClassByName(const FString& BlueprintName);

/** Shared by every bridge implementation file (defined in ForgeMindBridgeClient.cpp). */
DECLARE_LOG_CATEGORY_EXTERN(LogForgeMindBridge, Log, All);

class FSocket;

/**
 * TCP bridge client connecting this editor to the SiliconLife host's
 * ForgeMind bridge server.
 *
 * Discovery: reads %USERPROFILE%/.forgemind/bridge.json ({port, token, pid}).
 * Protocol: 4-byte little-endian length prefix + UTF-8 JSON envelope
 * ({v, type, id, name, payload, error}). Flow: connect -> auth -> handshake
 * -> message loop. Host-originated requests are dispatched to command
 * handlers on the game thread; responses come back synchronously.
 *
 * Heartbeat is host-driven: the host pings periodically and this client
 * answers; a dead host closes our socket via its own sweep, triggering
 * reconnection with exponential backoff.
 */
class FForgeMindBridgeClient
{
public:
	FForgeMindBridgeClient();
	~FForgeMindBridgeClient();

	/** Starts the connect retry timer (call from StartupModule). */
	void Start();

	/** Stops all threads and closes the socket (call from ShutdownModule). */
	void Stop();

	/** Drains game-thread work: queued host messages + connect retries. */
	void Tick(float DeltaTime);

	/** FEditorDelegates::OnEditorPreExit callback - best-effort closing notice, then teardown. */
	void OnPreExit();

	/**
	 * The live bridge client, exposed so the Asset IR domain handlers
	 * (Private/IR) can reuse the per-command implementations instead of
	 * duplicating editor logic. Null until the module starts the client.
	 */
	static FForgeMindBridgeClient*& ActiveInstance();

private:
	/** Values parsed from bridge.json. */
	struct FDiscovery
	{
		int32 Port = 0;
		FString Token;
		FString Reserve2; // AES-encrypted Base64 of 20 request tokens
		FString Reserve3; // AES key (hex string) for decrypting reserve2
		bool bValid = false;
	};

	void TryConnect();
	bool ConnectOnce();
	FDiscovery ReadDiscovery() const;
	/** Decrypts reserve2 using the AES key in reserve3, populating RequestTokenSet. */
	void DecryptRequestTokens(const FString& Reserve2, const FString& Reserve3Hex);
	void StartMessageThreads();
	/**
	 * Sole channel teardown entry: first Shutdown makes in-flight socket calls return immediately,
	 * then join worker threads, finally Close+DestroySocket. Shared by Stop (editor exit) and
	 * HandleDisconnect (MCP closes first).
	 */
	void TeardownConnection();
	void HandleDisconnect();
	uint32 ReceiveLoop();
	uint32 SendLoop();
	bool RecvFull(uint8* Buffer, int32 Size);
	bool SendFrame(const FString& Json);
	void ProcessRequest(TSharedRef<FJsonObject> Message);
	void ProcessEvent(TSharedRef<FJsonObject> Message);
	void QueueSend(const FString& Json);

	// Command handlers - always executed on the game thread.
	// Public so the Asset IR domain handlers (Private/IR) can reuse them.
public:
	TSharedPtr<FJsonObject> HandlePing(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleGetStatus(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleOpenAsset(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleExecConsole(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleQuitEditor(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleListClassHierarchy(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleGetCurrentLevel(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleCreateLevel(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleSetPie(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleListLevelActors(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleGetActorDetails(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleDeleteActor(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleSpawnActor(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleSetActorProperty(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleCreateBlueprint(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleCreateBlueprintStruct(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleCreateBlueprintEnum(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleCreateBlueprintInterface(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleCreateDataTable(const TSharedPtr<FJsonObject>& Payload);
	// Creates a UInputAction asset (Enhanced Input plugin) - the prerequisite the
	// UK2Node_EnhancedInputAction event node binds.
	TSharedPtr<FJsonObject> HandleCreateInputAction(const TSharedPtr<FJsonObject>& Payload);
	// Creates a UInputMappingContext asset (Enhanced Input plugin) - the asset
	// "Add Mapping Context" feeds to the enhanced-input LocalPlayer subsystem.
	TSharedPtr<FJsonObject> HandleCreateInputMappingContext(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleAddStructMember(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleUpdateStructMember(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleRemoveStructMember(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleAddEnumEntry(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleUpdateEnumEntry(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleRemoveEnumEntry(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleScanAssets(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleListAssets(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleGetAssetInfo(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleAddBlueprintVariable(const TSharedPtr<FJsonObject>& Payload);
	// Event dispatcher management (add / bind / unbind / broadcast)
	TSharedPtr<FJsonObject> HandleAddEventDispatcher(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleSetBlueprintVariableDefault(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleUpdateBlueprintVariable(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleListBlueprintVariables(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleRemoveBlueprintVariable(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleListBlueprintGraph(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleListBlueprintCatalog(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleAddBlueprintNode(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleConnectBlueprintPins(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleSetPinDefaultValue(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleDeleteBlueprintNode(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleBreakBlueprintPins(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleSaveAsset(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleDeleteAsset(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleCreateFunctionGraph(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleRemoveFunctionGraph(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleCreateMacroGraph(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleCompileBlueprint(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleAddCommentBox(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleMoveNodesToComment(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleRemoveNodesFromComment(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleGetDebugOutput(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleGetScreenOutput(const TSharedPtr<FJsonObject>& Payload);

	// Blueprint component management
	TSharedPtr<FJsonObject> HandleAddBlueprintComponent(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleListBlueprintComponents(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleSetComponentProperty(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleRemoveBlueprintComponent(const TSharedPtr<FJsonObject>& Payload);

	// Editor settings
	TSharedPtr<FJsonObject> HandleListEditorSettings(const TSharedPtr<FJsonObject>& Payload);

	// Input Settings (action / axis key mappings registration)
	TSharedPtr<FJsonObject> HandleAddActionMapping(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleAddAxisMapping(const TSharedPtr<FJsonObject>& Payload);

	// UMG Widget child management (Widget Blueprint designer hierarchy)
	TSharedPtr<FJsonObject> HandleAddWidgetChild(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleListWidgetChildren(const TSharedPtr<FJsonObject>& Payload);

	// Material step commands (requirement docs 'one command per step' A side) - they build
	// the same expression graph as the Asset IR material handler, see Private/IR.
	TSharedPtr<FJsonObject> HandleCreateMaterial(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleAddMaterialExpression(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleConnectMaterialPins(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleCompileMaterial(const TSharedPtr<FJsonObject>& Payload);

	// --- AssetIR v2 batch operations ---

	/** Creates or updates multiple assets (blueprints, structs, enums, materials, etc.) from an AssetIR JSON document. */
	TSharedPtr<FJsonObject> HandleApplyAssetIR(const TSharedPtr<FJsonObject>& Payload);

	/** Extracts AssetIR JSON from one or more existing assets (single asset or directory scan). */
	TSharedPtr<FJsonObject> HandleGetAssetIR(const TSharedPtr<FJsonObject>& Payload);

	// Screenshot capture
	TSharedPtr<FJsonObject> HandleTakeScreenshot(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleListWindows(const TSharedPtr<FJsonObject>& Payload);

	/**
	 * Legacy AssetIR v2 handler (v2 envelope, `type` key). Kept for
	 * compatibility with older hosts; the current pipeline uses the
	 * versioned Asset IR handled by FForgeMindIRRegistry.
	 */
	TSharedPtr<FJsonObject> HandleApplyAssetIRLegacy(const TSharedPtr<FJsonObject>& Payload);
	TSharedPtr<FJsonObject> HandleGetAssetIRLegacy(const TSharedPtr<FJsonObject>& Payload);

private:
	TMap<FString, TFunction<TSharedPtr<FJsonObject>(const TSharedPtr<FJsonObject>&)>> RequestHandlers;
	TArray<FString> AdvertisedCommands;

	FSocket* Socket = nullptr;
	bool bConnected = false;
	bool bStopping = false;
	bool bStopped = false;
	FString Token;

	/** 20 request tokens decrypted from bridge.json reserve2. Requests whose
	 *  "rt" field is not in this set are rejected. */
	TSet<FString> RequestTokenSet;

	/** Set by a worker thread when the connection drops; drained in Tick. */
	FThreadSafeBool bConnectionLost;

	/** Set by quit_editor; drained in Tick so the acknowledgement is flushed first. */
	FThreadSafeBool bQuitRequested;

	// Exponential backoff between connect attempts (1s .. 60s).
	double NextAttemptTime = 0.0;
	double BackoffSeconds = 1.0;

	// Keepalive: host sweeps sessions silent for 90s, ping every 30s.
	double LastKeepaliveTime = 0.0;

	TSharedPtr<FRunnableThread> ReceiveThread;
	TSharedPtr<FRunnableThread> SendThread;

	// Outbound queue (any thread -> send thread).
	FCriticalSection SendQueueLock;
	FThreadSafeBool SendQueueSignal;
	TArray<FString> SendQueue;

	// Inbound queue (receive thread -> game thread).
	FCriticalSection GameQueueLock;
	TArray<TSharedRef<FJsonObject>> GameQueue;

	FDelegateHandle PreExitHandle;
};