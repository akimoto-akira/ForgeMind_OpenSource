// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

#include "ForgeMindBridgeClient.h"

#include "AssetRegistry/AssetRegistryModule.h"
#include "Dom/JsonValue.h"
#include "Editor.h"
#include "Misc/CoreDelegates.h"
#include "EdGraph/EdGraphPin.h"
#include "EdGraph/EdGraph.h"
#include "K2Node_FunctionEntry.h"
#include "K2Node_FunctionResult.h"
#include "EdGraphSchema_K2.h"
#include "Engine/Blueprint.h"
#include "Engine/BlueprintGeneratedClass.h"
#include "FileHelpers.h"
#include "Kismet2/BlueprintEditorUtils.h"
#include "Kismet2/KismetEditorUtilities.h"
// create_blueprint: UBlueprintFunctionLibrary (abstract) is a valid blueprint
// parent for Blueprint Function Library assets — see HandleCreateBlueprint.
#include "Kismet/BlueprintFunctionLibrary.h"
#include "Misc/PackageName.h"
#include "Components/ActorComponent.h"
#include "Components/SceneComponent.h"
#include "GameFramework/Actor.h"
#include "GameFramework/GameModeBase.h"
#include "HAL/PlatformProcess.h"
#include "HAL/PlatformMisc.h"
#include "HAL/Runnable.h"
#include "HAL/RunnableThread.h"
#include "JsonObjectConverter.h"
#include "PlayInEditorDataTypes.h"
#include "Settings/LevelEditorPlaySettings.h"
#include "Subsystems/EditorActorSubsystem.h"
#include "Interfaces/IMainFrameModule.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Guid.h"
#include "Misc/Paths.h"
#include "Misc/Base64.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "SocketSubsystem.h"
#include "Sockets.h"
#include "Subsystems/AssetEditorSubsystem.h"
#include "UObject/UObjectGlobals.h"
#include "UObject/UObjectIterator.h"
#include "UObject/Package.h"
#include "UObject/UnrealType.h"
// list_editor_settings: UDeveloperSettings traversal + FColor/FLinearColor value extraction
#include "Engine/DeveloperSettings.h"
#include "Math/Color.h"
// create_blueprint: UWidgetBlueprint / UWidgetBlueprintGeneratedClass for Widget Blueprint creation
#include "WidgetBlueprint.h"
#include "Blueprint/WidgetBlueprintGeneratedClass.h"
#include "Blueprint/UserWidget.h"
#include "ForgeMindBlueprintUtils.h"

#if PLATFORM_WINDOWS
#include "Windows/AllowWindowsPlatformTypes.h"
#include <bcrypt.h>
#pragma comment(lib, "bcrypt.lib")
#include "Windows/HideWindowsPlatformTypes.h"
#endif

// Defined here, declared extern in ForgeMindBlueprintUtils.h so the split
// implementation files share one category.
DEFINE_LOG_CATEGORY(LogForgeMindBridge);

namespace
{
	/** Hard cap for a single frame payload (guards against corrupt headers). */
	constexpr int32 MaxFrameBytes = 16 * 1024 * 1024;

	/** Keepalive interval - well under the host's 90s silence sweep
	 *  and well under RecvFull's 30s idle timeout so the pong response
	 *  arrives long before RecvFull gives up. */
	constexpr double KeepaliveSeconds = 10.0;

	/** Upper bound of the exponential connect backoff. */
	constexpr double MaxBackoffSeconds = 60.0;

	/** Wraps a member loop as an FRunnable for FRunnableThread::Create. */
	class FBridgeThreadTask : public FRunnable
	{
		TFunction<uint32()> Body;

	public:
		explicit FBridgeThreadTask(TFunction<uint32()> InBody)
			: Body(MoveTemp(InBody))
		{
		}

		virtual uint32 Run() override
		{
			return Body ? Body() : 0u;
		}
	};

	FString SerializeJsonObject(const TSharedRef<FJsonObject>& Object)
	{
		FString Output;
		TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&Output);
		FJsonSerializer::Serialize(Object, Writer);
		return Output;
	}

	TSharedRef<FJsonObject> MakeEnvelope(const FString& Type, const FString& Name, const FString& Id = FString())
	{
		TSharedRef<FJsonObject> Envelope = MakeShared<FJsonObject>();
		Envelope->SetNumberField(TEXT("v"), 1);
		Envelope->SetStringField(TEXT("type"), Type);
		if (!Id.IsEmpty())
		{
			Envelope->SetStringField(TEXT("id"), Id);
		}
		Envelope->SetStringField(TEXT("name"), Name);
		return Envelope;
	}

	/**
	 * Converts a frame payload (raw UTF-8 bytes, NOT null-terminated) to an FString.
	 * The bytes are copied with an explicit trailing zero first: with a non-terminated
	 * buffer the string converter reads one byte past the end (StringConv.h Init passes
	 * SourceLen + 1 to Convert), which emits one spurious trailing character and breaks
	 * strict JSON parsing.
	 */
	FString PayloadToString(const TArray<uint8>& Payload, const int32 Length)
	{
		TArray<uint8> Terminated;
		Terminated.SetNumUninitialized(Length + 1);
		FMemory::Memcpy(Terminated.GetData(), Payload.GetData(), Length);
		Terminated[Length] = 0;
		return FString(FUTF8ToTCHAR(reinterpret_cast<const ANSICHAR*>(Terminated.GetData()), Length + 1).Get());
	}

	/**
	 * Builds a complete FText from host-provided information. The host only
	 * transmits the raw ingredients; the actual FText assembly happens here so
	 * the result carries proper localization identity:
	 *   - JSON string                              -> culture-invariant literal
	 *   - { stringTable, key }                     -> string table reference
	 *   - { format, args:{name:value} }            -> FText::Format result
	 *   - { text, [namespace], [key] }             -> text with gatherable identity
	 * Returns false when the payload cannot describe a text at all.
	 */
	bool TryBuildTextFromJson(const TSharedPtr<FJsonValue>& JsonValue, FText& OutText, FString& OutError)
	{
		if (!JsonValue.IsValid())
		{
			OutError = TEXT("Missing value for the FText property");
			return false;
		}

		// Plain string: a culture-invariant literal without localization identity.
		if (JsonValue->Type == EJson::String)
		{
			OutText = FText::FromString(JsonValue->AsString());
			return true;
		}

		const TSharedPtr<FJsonObject>* ObjectPtr = nullptr;
		if (JsonValue->Type != EJson::Object
			|| !JsonValue->TryGetObject(ObjectPtr) || !ObjectPtr->IsValid())
		{
			OutError = TEXT("FText value must be a string or an object "
				"({ text, [namespace], [key] }, { stringTable, key } or { format, args })");
			return false;
		}
		const TSharedRef<FJsonObject> Object = ObjectPtr->ToSharedRef();

		// String table reference - resolves against the loaded tables and
		// follows the table's translations at display time.
		FString TableId;
		FString TableKey;
		if (Object->TryGetStringField(TEXT("stringTable"), TableId))
		{
			if (!Object->TryGetStringField(TEXT("key"), TableKey) || TableKey.IsEmpty())
			{
				OutError = TEXT("FText string table value needs both 'stringTable' and 'key'");
				return false;
			}
			OutText = FText::FromStringTable(FName(*TableId), TableKey);
			return true;
		}

		// Format pattern with named arguments - each argument is itself built
		// through this converter, so nested descriptions work too.
		FString FormatPattern;
		if (Object->TryGetStringField(TEXT("format"), FormatPattern))
		{
			FFormatNamedArguments Arguments;
			const TSharedPtr<FJsonObject>* ArgsObject = nullptr;
			if (Object->TryGetObjectField(TEXT("args"), ArgsObject) && ArgsObject->IsValid())
			{
				for (const TPair<FString, TSharedPtr<FJsonValue>>& Arg : (*ArgsObject)->Values)
				{
					FText ArgumentText;
					if (!TryBuildTextFromJson(Arg.Value, ArgumentText, OutError))
					{
						return false;
					}
					Arguments.Add(Arg.Key, ArgumentText);
				}
			}
			OutText = FText::Format(FText::FromString(FormatPattern), Arguments);
			return true;
		}

		// Source text with optional localization identity. ChangeKey stamps
		// namespace/key onto the text so the localization pipeline can gather
		// and replace it; without them the result stays a literal.
		FString SourceText;
		if (!Object->TryGetStringField(TEXT("text"), SourceText))
		{
			OutError = TEXT("FText object value needs 'text', or 'stringTable'+'key', or 'format'");
			return false;
		}

		FString Namespace;
		FString Key;
		const bool bHasNamespace = Object->TryGetStringField(TEXT("namespace"), Namespace);
		const bool bHasKey = Object->TryGetStringField(TEXT("key"), Key);
		OutText = (bHasNamespace || bHasKey)
			? FText::ChangeKey(Namespace, Key, FText::FromString(SourceText))
			: FText::FromString(SourceText);
		return true;
	}
}

FForgeMindBridgeClient::FForgeMindBridgeClient()
{
	ActiveInstance() = this;

	AdvertisedCommands = { TEXT("ping"), TEXT("get_status"), TEXT("open_asset"), TEXT("exec_console"), TEXT("quit_editor"), TEXT("list_class_hierarchy"), TEXT("get_current_level"), TEXT("create_level"), TEXT("set_pie"), TEXT("list_level_actors"), TEXT("get_actor_details"), TEXT("delete_actor"), TEXT("spawn_actor"), TEXT("set_actor_property"), TEXT("create_blueprint"), TEXT("create_blueprint_struct"), TEXT("create_blueprint_enum"), TEXT("create_blueprint_interface"), TEXT("add_struct_member"), TEXT("update_struct_member"), TEXT("remove_struct_member"), TEXT("add_enum_entry"), TEXT("update_enum_entry"), TEXT("remove_enum_entry"), TEXT("scan_assets"), TEXT("list_assets"), TEXT("get_asset_info"), TEXT("add_blueprint_variable"), TEXT("add_event_dispatcher"), TEXT("set_blueprint_variable_default"), TEXT("update_blueprint_variable"), TEXT("list_blueprint_variables"), TEXT("remove_blueprint_variable"), TEXT("list_blueprint_graph"), TEXT("list_blueprint_catalog"), TEXT("add_blueprint_node"), TEXT("connect_blueprint_pins"), TEXT("set_pin_default_value"), TEXT("delete_blueprint_node"), TEXT("break_blueprint_pins"), TEXT("save_asset"), TEXT("delete_asset"), TEXT("create_function_graph"), TEXT("remove_function_graph"), TEXT("create_macro_graph"), TEXT("compile_blueprint"), TEXT("add_comment_box"), TEXT("move_nodes_to_comment"), TEXT("remove_nodes_from_comment"), TEXT("get_debug_output"), TEXT("get_screen_output"), TEXT("add_blueprint_component"), TEXT("list_blueprint_components"), TEXT("set_component_property"), TEXT("remove_blueprint_component"), TEXT("create_input_action"), TEXT("create_input_mapping_context"), TEXT("add_input_mapping"), TEXT("list_editor_settings"), TEXT("add_widget_child"), TEXT("list_widget_children"), TEXT("apply_asset_ir"), TEXT("get_asset_ir"), TEXT("take_screenshot"), TEXT("list_windows") };

	RequestHandlers.Add(TEXT("ping"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandlePing(Payload); });
	RequestHandlers.Add(TEXT("get_status"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleGetStatus(Payload); });
	RequestHandlers.Add(TEXT("open_asset"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleOpenAsset(Payload); });
	RequestHandlers.Add(TEXT("exec_console"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleExecConsole(Payload); });
	RequestHandlers.Add(TEXT("quit_editor"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleQuitEditor(Payload); });
	RequestHandlers.Add(TEXT("list_class_hierarchy"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleListClassHierarchy(Payload); });
	RequestHandlers.Add(TEXT("get_current_level"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleGetCurrentLevel(Payload); });
	RequestHandlers.Add(TEXT("create_level"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleCreateLevel(Payload); });
	RequestHandlers.Add(TEXT("set_pie"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleSetPie(Payload); });
	RequestHandlers.Add(TEXT("list_level_actors"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleListLevelActors(Payload); });
	RequestHandlers.Add(TEXT("get_actor_details"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleGetActorDetails(Payload); });
	RequestHandlers.Add(TEXT("delete_actor"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleDeleteActor(Payload); });
	RequestHandlers.Add(TEXT("spawn_actor"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleSpawnActor(Payload); });
	RequestHandlers.Add(TEXT("set_actor_property"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleSetActorProperty(Payload); });
	RequestHandlers.Add(TEXT("create_blueprint"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleCreateBlueprint(Payload); });
	RequestHandlers.Add(TEXT("create_blueprint_struct"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleCreateBlueprintStruct(Payload); });
	RequestHandlers.Add(TEXT("create_blueprint_enum"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleCreateBlueprintEnum(Payload); });
	RequestHandlers.Add(TEXT("create_blueprint_interface"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleCreateBlueprintInterface(Payload); });
	RequestHandlers.Add(TEXT("add_struct_member"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleAddStructMember(Payload); });
	RequestHandlers.Add(TEXT("update_struct_member"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleUpdateStructMember(Payload); });
	RequestHandlers.Add(TEXT("remove_struct_member"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleRemoveStructMember(Payload); });
	RequestHandlers.Add(TEXT("add_enum_entry"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleAddEnumEntry(Payload); });
	RequestHandlers.Add(TEXT("update_enum_entry"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleUpdateEnumEntry(Payload); });
	RequestHandlers.Add(TEXT("remove_enum_entry"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleRemoveEnumEntry(Payload); });
	RequestHandlers.Add(TEXT("scan_assets"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleScanAssets(Payload); });
	RequestHandlers.Add(TEXT("list_assets"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleListAssets(Payload); });
	RequestHandlers.Add(TEXT("get_asset_info"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleGetAssetInfo(Payload); });
	RequestHandlers.Add(TEXT("add_blueprint_variable"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleAddBlueprintVariable(Payload); });
	RequestHandlers.Add(TEXT("add_event_dispatcher"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleAddEventDispatcher(Payload); });
	RequestHandlers.Add(TEXT("set_blueprint_variable_default"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleSetBlueprintVariableDefault(Payload); });
	RequestHandlers.Add(TEXT("update_blueprint_variable"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleUpdateBlueprintVariable(Payload); });
	RequestHandlers.Add(TEXT("list_blueprint_variables"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleListBlueprintVariables(Payload); });
	RequestHandlers.Add(TEXT("remove_blueprint_variable"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleRemoveBlueprintVariable(Payload); });
	RequestHandlers.Add(TEXT("list_blueprint_graph"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleListBlueprintGraph(Payload); });
	RequestHandlers.Add(TEXT("list_blueprint_catalog"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleListBlueprintCatalog(Payload); });
	RequestHandlers.Add(TEXT("add_blueprint_node"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleAddBlueprintNode(Payload); });
	RequestHandlers.Add(TEXT("connect_blueprint_pins"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleConnectBlueprintPins(Payload); });
	RequestHandlers.Add(TEXT("set_pin_default_value"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleSetPinDefaultValue(Payload); });
	RequestHandlers.Add(TEXT("delete_blueprint_node"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleDeleteBlueprintNode(Payload); });
	RequestHandlers.Add(TEXT("break_blueprint_pins"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleBreakBlueprintPins(Payload); });
	RequestHandlers.Add(TEXT("save_asset"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleSaveAsset(Payload); });
	RequestHandlers.Add(TEXT("delete_asset"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleDeleteAsset(Payload); });
	RequestHandlers.Add(TEXT("create_function_graph"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleCreateFunctionGraph(Payload); });
	RequestHandlers.Add(TEXT("remove_function_graph"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleRemoveFunctionGraph(Payload); });
	RequestHandlers.Add(TEXT("create_macro_graph"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleCreateMacroGraph(Payload); });
	RequestHandlers.Add(TEXT("compile_blueprint"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleCompileBlueprint(Payload); });
	RequestHandlers.Add(TEXT("add_comment_box"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleAddCommentBox(Payload); });
	RequestHandlers.Add(TEXT("move_nodes_to_comment"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleMoveNodesToComment(Payload); });
	RequestHandlers.Add(TEXT("remove_nodes_from_comment"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleRemoveNodesFromComment(Payload); });
	RequestHandlers.Add(TEXT("get_debug_output"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleGetDebugOutput(Payload); });
	RequestHandlers.Add(TEXT("get_screen_output"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleGetScreenOutput(Payload); });
	RequestHandlers.Add(TEXT("add_blueprint_component"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleAddBlueprintComponent(Payload); });
	RequestHandlers.Add(TEXT("list_blueprint_components"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleListBlueprintComponents(Payload); });
	RequestHandlers.Add(TEXT("set_component_property"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleSetComponentProperty(Payload); });
	RequestHandlers.Add(TEXT("remove_blueprint_component"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleRemoveBlueprintComponent(Payload); });
	RequestHandlers.Add(TEXT("list_editor_settings"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleListEditorSettings(Payload); });
	RequestHandlers.Add(TEXT("add_widget_child"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleAddWidgetChild(Payload); });
	RequestHandlers.Add(TEXT("list_widget_children"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleListWidgetChildren(Payload); });
	RequestHandlers.Add(TEXT("apply_asset_ir"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleApplyAssetIR(Payload); });
	RequestHandlers.Add(TEXT("get_asset_ir"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleGetAssetIR(Payload); });
	RequestHandlers.Add(TEXT("take_screenshot"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleTakeScreenshot(Payload); });
	RequestHandlers.Add(TEXT("list_windows"), [this](const TSharedPtr<FJsonObject>& Payload) { return HandleListWindows(Payload); });
}

FForgeMindBridgeClient*& FForgeMindBridgeClient::ActiveInstance()
{
	static FForgeMindBridgeClient* Instance = nullptr;
	return Instance;
}

FForgeMindBridgeClient::~FForgeMindBridgeClient()
{
	if (ActiveInstance() == this)
	{
		ActiveInstance() = nullptr;
	}
	Stop();
}

void FForgeMindBridgeClient::Start()
{
	bStopping = false;
	bStopped = false;
	bConnectionLost = false;
	NextAttemptTime = FPlatformTime::Seconds(); // first attempt immediately
	PreExitHandle = FCoreDelegates::OnPreExit.AddRaw(this, &FForgeMindBridgeClient::OnPreExit);
	UE_LOG(LogForgeMindBridge, Log, TEXT("Bridge client started - will connect to the ForgeMind host when it publishes bridge.json"));
}

void FForgeMindBridgeClient::Stop()
{
	if (bStopped)
	{
		return;
	}
	bStopping = true;

	// Short grace period so a pending editor_closing frame can still flush
	const double Deadline = FPlatformTime::Seconds() + 1.0;
	while (FPlatformTime::Seconds() < Deadline)
	{
		{
			FScopeLock Lock(&SendQueueLock);
			if (SendQueue.Num() == 0)
			{
				break;
			}
		}
		FPlatformProcess::Sleep(0.02f);
	}

	TeardownConnection();
	bStopped = true;

	if (PreExitHandle.IsValid())
	{
		FCoreDelegates::OnPreExit.RemoveAll(this);
		PreExitHandle.Reset();
	}
	UE_LOG(LogForgeMindBridge, Log, TEXT("Bridge client stopped"));
}

void FForgeMindBridgeClient::OnPreExit()
{
	// Best-effort closing notice; Stop() gives it a short flush window
	QueueSend(SerializeJsonObject(MakeEnvelope(TEXT("event"), TEXT("editor_closing"))));
	Stop();
}

void FForgeMindBridgeClient::Tick(float DeltaTime)
{
	if (bStopping)
	{
		return;
	}

	// Deferred editor shutdown requested via quit_editor - the acknowledgement
	// response was queued last tick and has been flushed by the send thread.
	// RequestCloseEditor() runs the unsaved-packages prompt and may be refused
	// (save/GC/slow-task in flight, or the user cancels the save dialog).
	if (bQuitRequested)
	{
		bQuitRequested = false;
		UE_LOG(LogForgeMindBridge, Log, TEXT("quit_editor received - requesting editor close"));
		IMainFrameModule& MainFrame = FModuleManager::LoadModuleChecked<IMainFrameModule>(TEXT("MainFrame"));
		MainFrame.RequestCloseEditor();
	}

	// Connection lost reported by a worker thread - tear down and back off
	if (bConnectionLost)
	{
		bConnectionLost = false;
		HandleDisconnect();
	}

	if (!bConnected && FPlatformTime::Seconds() >= NextAttemptTime)
	{
		TryConnect();
	}

	if (!bConnected)
	{
		return;
	}

	// Keepalive keeps this session out of the host's silent-session sweep
	const double Now = FPlatformTime::Seconds();
	if (Now - LastKeepaliveTime >= KeepaliveSeconds)
	{
		LastKeepaliveTime = Now;
		QueueSend(SerializeJsonObject(MakeEnvelope(TEXT("request"), TEXT("ping"), FGuid::NewGuid().ToString(EGuidFormats::Digits))));
	}

	// Drain inbound messages on the game thread
	TArray<TSharedRef<FJsonObject>> Pending;
	{
		FScopeLock Lock(&GameQueueLock);
		Pending = MoveTemp(GameQueue);
	}
	for (const TSharedRef<FJsonObject>& Message : Pending)
	{
		FString Type = Message->GetStringField(TEXT("type"));
		if (Type == TEXT("request"))
		{
			ProcessRequest(Message);
		}
		else if (Type == TEXT("event"))
		{
			ProcessEvent(Message);
		}
		// responses - replies to our keepalive pings; nothing to do
	}
}

void FForgeMindBridgeClient::TryConnect()
{
	if (ConnectOnce())
	{
		bConnected = true;
		BackoffSeconds = 1.0;
		LastKeepaliveTime = FPlatformTime::Seconds();
		StartMessageThreads();

		// Announce ourselves once the channel is live
		QueueSend(SerializeJsonObject(MakeEnvelope(TEXT("event"), TEXT("editor_ready"))));
	}
	else
	{
		BackoffSeconds = FMath::Min(BackoffSeconds * 2.0, MaxBackoffSeconds);
	}
	NextAttemptTime = FPlatformTime::Seconds() + BackoffSeconds;
}

bool FForgeMindBridgeClient::ConnectOnce()
{
	const FDiscovery Discovery = ReadDiscovery();
	if (!Discovery.bValid)
	{
		return false; // no host published yet (or stale/corrupt file) - logged inside ReadDiscovery
	}

	ISocketSubsystem* Subsystem = ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM);
	Socket = Subsystem->CreateSocket(NAME_Stream, TEXT("ForgeMindBridge"), false);
	if (Socket == nullptr)
	{
		return false;
	}

	TSharedRef<FInternetAddr> Address = Subsystem->CreateInternetAddr();
	// SetLoopbackAddress() handles byte order internally - SetIp(uint32) applies
	// htonl itself despite its header comment, so a pre-converted constant gets
	// double-swapped and silently targets a bogus external address.
	Address->SetLoopbackAddress();
	Address->SetPort(Discovery.Port);

	if (!Socket->Connect(*Address))
	{
		const ESocketErrors SocketError = Subsystem->GetLastErrorCode();
		UE_LOG(LogForgeMindBridge, Warning, TEXT("Connect to 127.0.0.1:%d failed - error %d (%s)"),
			Discovery.Port, static_cast<int32>(SocketError), Subsystem->GetSocketError(SocketError));
		Subsystem->DestroySocket(Socket);
		Socket = nullptr;
		return false;
	}

	// Non-blocking from here on, BEFORE the handshake. The handshake runs on the
	// game thread, and RecvFull's probe Recv would block there forever on a
	// blocking socket - which is what happens when the host dies mid-frame after
	// the 4-byte length header (the 10s deadline is never reached because we
	// never come back from the syscall).
	Socket->SetNonBlocking(true);

	Token = Discovery.Token;

	// Decrypt the 20 request tokens from reserve2 so ProcessRequest can
	// validate the "rt" field on every incoming request.
	if (!Discovery.Reserve2.IsEmpty() && !Discovery.Reserve3.IsEmpty())
	{
		DecryptRequestTokens(Discovery.Reserve2, Discovery.Reserve3);
	}

	// Auth + handshake happen synchronously before the worker threads exist.
	// The host answers only the handshake; a dropped connection means bad auth.
	// UE 5.6 has no FSocket receive timeout - poll for the reply with a deadline.
	const double HandshakeDeadline = FPlatformTime::Seconds() + 10.0;

	const FString AuthJson = [&]
	{
		TSharedRef<FJsonObject> Envelope = MakeEnvelope(TEXT("request"), TEXT("auth"), TEXT("auth"));
		TSharedRef<FJsonObject> AuthPayload = MakeShared<FJsonObject>();
		AuthPayload->SetStringField(TEXT("token"), Token);
		Envelope->SetObjectField(TEXT("payload"), AuthPayload);
		return SerializeJsonObject(Envelope);
	}();

	const FString HandshakeJson = [&]
	{
		TSharedRef<FJsonObject> Envelope = MakeEnvelope(TEXT("request"), TEXT("handshake"), TEXT("handshake"));
		TSharedRef<FJsonObject> Info = MakeShared<FJsonObject>();
		Info->SetStringField(TEXT("projectFile"), FPaths::GetProjectFilePath());
		const FEngineVersion& Version = FEngineVersion::Current();
		Info->SetStringField(TEXT("engineVersion"), FString::Printf(TEXT("%u.%u.%u"), Version.GetMajor(), Version.GetMinor(), Version.GetPatch()));
		Info->SetNumberField(TEXT("pid"), FPlatformProcess::GetCurrentProcessId());
		TArray<TSharedPtr<FJsonValue>> Commands;
		for (const FString& Command : AdvertisedCommands)
		{
			Commands.Add(MakeShared<FJsonValueString>(Command));
		}
		Info->SetArrayField(TEXT("commands"), Commands);
		Envelope->SetObjectField(TEXT("payload"), Info);
		return SerializeJsonObject(Envelope);
	}();

	if (!SendFrame(AuthJson))
	{
		UE_LOG(LogForgeMindBridge, Warning, TEXT("Handshake failed - could not send auth frame"));
		Subsystem->DestroySocket(Socket);
		Socket = nullptr;
		return false;
	}
	if (!SendFrame(HandshakeJson))
	{
		UE_LOG(LogForgeMindBridge, Warning, TEXT("Handshake failed - could not send handshake frame"));
		Subsystem->DestroySocket(Socket);
		Socket = nullptr;
		return false;
	}

	// Await the handshake response frame
	uint32 Pending = 0;
	while (!Socket->HasPendingData(Pending) || Pending < 4)
	{
		if (FPlatformTime::Seconds() >= HandshakeDeadline)
		{
			UE_LOG(LogForgeMindBridge, Warning, TEXT("Handshake failed - no reply from host within 10s"));
			Subsystem->DestroySocket(Socket);
			Socket = nullptr;
			return false;
		}
		FPlatformProcess::Sleep(0.02f);
	}

	uint8 Header[4];
	if (!RecvFull(Header, sizeof(Header)))
	{
		UE_LOG(LogForgeMindBridge, Warning, TEXT("Handshake failed - host rejected auth or timed out"));
		Subsystem->DestroySocket(Socket);
		Socket = nullptr;
		return false;
	}
	const int32 Length = Header[0] | (Header[1] << 8) | (Header[2] << 16) | (Header[3] << 24);
	if (Length <= 0 || Length > MaxFrameBytes)
	{
		UE_LOG(LogForgeMindBridge, Warning, TEXT("Handshake failed - bogus reply length %d"), Length);
		Subsystem->DestroySocket(Socket);
		Socket = nullptr;
		return false;
	}
	TArray<uint8> Payload;
	Payload.SetNumUninitialized(Length);
	if (!RecvFull(Payload.GetData(), Length))
	{
		UE_LOG(LogForgeMindBridge, Warning, TEXT("Handshake failed - reply truncated at %d bytes"), Length);
		Subsystem->DestroySocket(Socket);
		Socket = nullptr;
		return false;
	}

	const FString ResponseJson = PayloadToString(Payload, Length);
	TSharedPtr<FJsonObject> Response;
	if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(ResponseJson), Response) || !Response.IsValid())
	{
		UE_LOG(LogForgeMindBridge, Warning, TEXT("Handshake failed - reply is not JSON: %s"), *ResponseJson.Left(200));
		Subsystem->DestroySocket(Socket);
		Socket = nullptr;
		return false;
	}

	bool bOk = Response->GetStringField(TEXT("type")) == TEXT("response")
		&& Response->GetStringField(TEXT("name")) == TEXT("handshake")
		&& !Response->HasTypedField<EJson::String>(TEXT("error"));
	if (!bOk)
	{
		UE_LOG(LogForgeMindBridge, Warning, TEXT("Handshake refused by host: %s"), *ResponseJson);
		Subsystem->DestroySocket(Socket);
		Socket = nullptr;
		return false;
	}

	UE_LOG(LogForgeMindBridge, Log, TEXT("Connected to ForgeMind host on 127.0.0.1:%d"), Discovery.Port);
	return true;
}

FForgeMindBridgeClient::FDiscovery FForgeMindBridgeClient::ReadDiscovery() const
{
	FDiscovery Result;

	// Use %USERPROFILE% directly .FPlatformProcess::UserHomeDir() returns the
	// engine directory (not the user profile) on some UE 5.0 configurations,
	// causing a path mismatch with the C# host which uses
	// Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).
	const FString UserProfile = FPlatformMisc::GetEnvironmentVariable(TEXT("USERPROFILE"));
	const FString Path = FPaths::Combine(UserProfile, TEXT(".forgemind"), TEXT("bridge.json"));
	FString Contents;
	if (!FFileHelper::LoadFileToString(Contents, *Path))
	{
		UE_LOG(LogForgeMindBridge, Warning, TEXT("Discovery failed - cannot read %s"), *Path);
		return Result;
	}

	TSharedPtr<FJsonObject> Json;
	if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Contents), Json) || !Json.IsValid())
	{
		UE_LOG(LogForgeMindBridge, Warning, TEXT("Discovery failed - %s is not valid JSON"), *Path);
		return Result;
	}

	int32 Port = 0;
	FString DiscoveredToken;
	if (!Json->TryGetNumberField(TEXT("port"), Port) || !Json->TryGetStringField(TEXT("token"), DiscoveredToken))
	{
		UE_LOG(LogForgeMindBridge, Warning, TEXT("Discovery failed - %s misses 'port' or 'token'"), *Path);
		return Result;
	}

	Result.Port = Port;
	Result.Token = DiscoveredToken;
	// Read reserve2 (encrypted token set) and reserve3 (AES key hex) .
	// may be absent in older bridge.json files.
	Json->TryGetStringField(TEXT("reserve2"), Result.Reserve2);
	Json->TryGetStringField(TEXT("reserve3"), Result.Reserve3);
	Result.bValid = Port > 0 && !DiscoveredToken.IsEmpty();
	return Result;
}

void FForgeMindBridgeClient::DecryptRequestTokens(const FString& Reserve2, const FString& Reserve3Hex)
{
	RequestTokenSet.Reset();

#if PLATFORM_WINDOWS
	// 1. Hex-decode the AES key (32 hex chars .16 bytes for AES-128)
	const FString HexKey = Reserve3Hex.ToLower();
	TArray<uint8> KeyBytes;
	KeyBytes.Reserve(16);
	for (int32 i = 0; i + 1 < HexKey.Len(); i += 2)
	{
		KeyBytes.Add(static_cast<uint8>(FCString::Strtoi(*HexKey.Mid(i, 2), nullptr, 16)));
	}
	if (KeyBytes.Num() < 16)
	{
		UE_LOG(LogForgeMindBridge, Warning, TEXT("reserve3 hex too short (%d bytes) .token validation disabled"), KeyBytes.Num());
		return;
	}

	// 2. Base64-decode reserve2 .cipher bytes
	TArray<uint8> CipherBytes;
	if (!FBase64::Decode(Reserve2, CipherBytes) || CipherBytes.Num() == 0)
	{
		UE_LOG(LogForgeMindBridge, Warning, TEXT("reserve2 base64 decode failed .token validation disabled"));
		return;
	}

	// 3. AES-128-CBC decrypt with zero IV using Windows BCrypt
	BCRYPT_ALG_HANDLE hAlg = nullptr;
	if (BCryptOpenAlgorithmProvider(&hAlg, BCRYPT_AES_ALGORITHM, nullptr, 0) != 0)
	{
		UE_LOG(LogForgeMindBridge, Warning, TEXT("BCryptOpenAlgorithmProvider failed .token validation disabled"));
		return;
	}

	BCryptSetProperty(hAlg, BCRYPT_CHAINING_MODE,
		(PUCHAR)BCRYPT_CHAIN_MODE_CBC, sizeof(BCRYPT_CHAIN_MODE_CBC), 0);

	BCRYPT_KEY_HANDLE hKey = nullptr;
	NTSTATUS status = BCryptGenerateSymmetricKey(hAlg, &hKey, nullptr, 0,
		KeyBytes.GetData(), 16, 0);
	if (status != 0)
	{
		BCryptCloseAlgorithmProvider(hAlg, 0);
		UE_LOG(LogForgeMindBridge, Warning, TEXT("BCryptGenerateSymmetricKey failed (0x%08X) .token validation disabled"), status);
		return;
	}

	// Zero IV (16 bytes) .BCryptDecrypt consumes the IV, so use a copy
	uint8 IV[16] = {0};

	// First call to get the plaintext size
	DWORD PlainLen = 0;
	status = BCryptDecrypt(hKey, CipherBytes.GetData(), static_cast<ULONG>(CipherBytes.Num()),
		nullptr, IV, sizeof(IV), nullptr, 0, &PlainLen, BCRYPT_BLOCK_PADDING);
	if (status != 0)
	{
		BCryptDestroyKey(hKey);
		BCryptCloseAlgorithmProvider(hAlg, 0);
		UE_LOG(LogForgeMindBridge, Warning, TEXT("BCryptDecrypt size query failed (0x%08X) .token validation disabled"), status);
		return;
	}

	// Second call with output buffer (reset IV since it was consumed)
	TArray<uint8> PlainBytes;
	PlainBytes.SetNumUninitialized(PlainLen);
	FMemory::Memzero(IV, sizeof(IV));
	status = BCryptDecrypt(hKey, CipherBytes.GetData(), static_cast<ULONG>(CipherBytes.Num()),
		nullptr, IV, sizeof(IV), PlainBytes.GetData(), PlainLen, &PlainLen, BCRYPT_BLOCK_PADDING);

	BCryptDestroyKey(hKey);
	BCryptCloseAlgorithmProvider(hAlg, 0);

	if (status != 0)
	{
		UE_LOG(LogForgeMindBridge, Warning, TEXT("BCryptDecrypt failed (0x%08X) .token validation disabled"), status);
		return;
	}

	// 4. Parse JSON array of token strings
	// Trim trailing nulls from PKCS7 padding
	int32 ActualLen = PlainLen;
	while (ActualLen > 0 && PlainBytes[ActualLen - 1] == 0)
	{
		--ActualLen;
	}
	PlainBytes.SetNum(ActualLen, false);

	const FString TokensJson = FString(UTF8_TO_TCHAR(PlainBytes.GetData()), ActualLen);
	TArray<TSharedPtr<FJsonValue>> TokenArray;
	TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(TokensJson);
	if (!FJsonSerializer::Deserialize(Reader, TokenArray) || TokenArray.Num() == 0)
	{
		UE_LOG(LogForgeMindBridge, Warning, TEXT("Failed to parse token array from decrypted reserve2 .token validation disabled"));
		return;
	}

	for (const TSharedPtr<FJsonValue>& TokenValue : TokenArray)
	{
		if (TokenValue.IsValid() && TokenValue->Type == EJson::String)
		{
			RequestTokenSet.Add(TokenValue->AsString());
		}
	}

	UE_LOG(LogForgeMindBridge, Log, TEXT("Loaded %d request tokens from reserve2"), RequestTokenSet.Num());
#else
	UE_LOG(LogForgeMindBridge, Warning, TEXT("Request token decryption not implemented on this platform .validation disabled"));
#endif
}

void FForgeMindBridgeClient::StartMessageThreads()
{
	ReceiveThread = TSharedPtr<FRunnableThread>(FRunnableThread::Create(
		new FBridgeThreadTask([this] { return ReceiveLoop(); }), TEXT("ForgeMindBridgeRecv")));
	SendThread = TSharedPtr<FRunnableThread>(FRunnableThread::Create(
		new FBridgeThreadTask([this] { return SendLoop(); }), TEXT("ForgeMindBridgeSend")));
}

void FForgeMindBridgeClient::TeardownConnection()
{
	// Teardown order: Shutdown -> Join threads -> Close/DestroySocket.
	// FRunnableThread::Kill() defaults to bShouldWait=true, so the caller (game thread) waits until
	// the runnable returns; but the runnable may be blocked on a socket read -- the peer is dead but FIN
	// hasn't been consumed yet, RecvFull's 30s idle timeout is its only exit (observed after
	// TestRuns/20261004_1817: when MCP server closes first, the entire UE editor freezes). Calling
	// Shutdown(ReadWrite) first makes in-flight socket calls return errors immediately, so both worker
	// threads exit on the next 20ms poll cycle, and Kill() no longer blocks the game thread. The FSocket
	// object must stay alive until both threads have exited -- DestroySocket will free it.
	if (Socket != nullptr)
	{
		Socket->Shutdown(ESocketShutdownMode::ReadWrite);
	}

	if (ReceiveThread.IsValid())
	{
		ReceiveThread->Kill();
		ReceiveThread.Reset();
	}
	if (SendThread.IsValid())
	{
		SendThread->Kill();
		SendThread.Reset();
	}

	if (Socket != nullptr)
	{
		Socket->Close();
		ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM)->DestroySocket(Socket);
		Socket = nullptr;
	}
	bConnected = false;
}

void FForgeMindBridgeClient::HandleDisconnect()
{
	TeardownConnection();
	BackoffSeconds = FMath::Min(BackoffSeconds * 2.0, MaxBackoffSeconds);
	NextAttemptTime = FPlatformTime::Seconds() + BackoffSeconds;

	UE_LOG(LogForgeMindBridge, Log, TEXT("Connection lost - retrying in %.0fs"), BackoffSeconds);
}

uint32 FForgeMindBridgeClient::ReceiveLoop()
{
	while (!bStopping && !bConnectionLost)
	{
		uint8 Header[4];
		if (!RecvFull(Header, sizeof(Header)))
		{
			break;
		}

		const int32 Length = Header[0] | (Header[1] << 8) | (Header[2] << 16) | (Header[3] << 24);
		if (Length <= 0 || Length > MaxFrameBytes)
		{
			UE_LOG(LogForgeMindBridge, Warning, TEXT("Received corrupt frame length %d - dropping connection"), Length);
			break;
		}

		TArray<uint8> Payload;
		Payload.SetNumUninitialized(Length);
		if (!RecvFull(Payload.GetData(), Length))
		{
			break;
		}

		const FString Json = PayloadToString(Payload, Length);
		TSharedPtr<FJsonObject> Message;
		if (FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Json), Message) && Message.IsValid())
		{
			FScopeLock Lock(&GameQueueLock);
			GameQueue.Add(Message.ToSharedRef());
		}
		else
		{
			UE_LOG(LogForgeMindBridge, Verbose, TEXT("Ignoring non-JSON frame (%d bytes)"), Length);
		}
	}

	if (!bStopping)
	{
		bConnectionLost.AtomicSet(true);
	}
	return 0u;
}

uint32 FForgeMindBridgeClient::SendLoop()
{
	while (!bStopping && !bConnectionLost)
	{
		FString Json;
		{
			FScopeLock Lock(&SendQueueLock);
			if (SendQueue.Num() > 0)
			{
				Json = MoveTemp(SendQueue[0]);
				SendQueue.RemoveAt(0);
				if (SendQueue.Num() == 0)
				{
					SendQueueSignal = false;
				}
			}
		}

		if (Json.IsEmpty())
		{
			FPlatformProcess::Sleep(0.05f);
			continue;
		}

		if (!SendFrame(Json))
		{
			break;
		}
	}

	if (!bStopping)
	{
		bConnectionLost.AtomicSet(true);
	}
	return 0u;
}

bool FForgeMindBridgeClient::RecvFull(uint8* Buffer, int32 Size)
{
	// Non-blocking socket: when HasPendingData returns 0 the connection
	// may still have been closed by the peer. We must periodically call
	// Recv to detect a graceful close (Recv returns false on a closed
	// non-blocking socket). Without this probe, RecvFull would spin
	// forever in the Sleep+continue loop after the host dies.
	constexpr double RecvIdleTimeoutSeconds = 30.0;
	double IdleDeadline = FPlatformTime::Seconds() + RecvIdleTimeoutSeconds;
	int32 Received = 0;

	while (Received < Size)
	{
		if (bStopping)
		{
			return false;
		}

		uint32 Pending = 0;
		const bool bHasData = Socket->HasPendingData(Pending);

		if (bHasData && Pending == 0)
		{
			// select() reports the socket readable while FIONREAD reports 0 bytes
			// = the peer performed an orderly shutdown (FIN). This is the only
			// reliable EOF signal on UE sockets: FSocketBSD::Recv returns
			// (true, 0) for "no data yet" on a non-blocking socket (it swallows
			// WSAEWOULDBLOCK), and (false, 0) for EOF - so neither the boolean
			// nor the byte count alone can tell them apart. Without this the
			// receive thread spun until the 30s idle timeout after the MCP server
			// closed, and HandleDisconnect's ReceiveThread->Kill() (bShouldWait
			// = true) parked the game thread for exactly that long.
			UE_LOG(LogForgeMindBridge, Verbose,
				TEXT("RecvFull saw EOF (readable, 0 bytes pending) - peer closed the connection"));
			return false;
		}

		if (bHasData && Pending > 0)
		{
			int32 Read = 0;
			if (!Socket->Recv(Buffer + Received, Size - Received, Read) || Read <= 0)
			{
				return false;
			}
			Received += Read;
			IdleDeadline = FPlatformTime::Seconds() + RecvIdleTimeoutSeconds;
			continue;
		}

		// No pending data .probe the socket to detect a closed peer.
		// On a non-blocking socket with a dead connection Recv returns
		// false (SE_ConnectionClosed / SE_EWOULDBLOCK). RecvFull must
		// not loop forever waiting for data that will never arrive.
		int32 Probe = 0;
		const bool bRecvOk = Socket->Recv(Buffer + Received, Size - Received, Probe);
		if (bRecvOk && Probe > 0)
		{
			Received += Probe;
			IdleDeadline = FPlatformTime::Seconds() + RecvIdleTimeoutSeconds;
			continue;
		}
		if (!bRecvOk)
		{
			// Recv failed .could be EWOULDBLOCK (transient, keep waiting)
			// or a real error (connection reset / closed). Distinguish via
			// the socket subsystem error code.
			const ESocketErrors Err = ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM)->GetLastErrorCode();
			if (Err != SE_EWOULDBLOCK && Err != SE_TRY_AGAIN)
			{
				UE_LOG(LogForgeMindBridge, Verbose,
					TEXT("RecvFull detected closed connection (error %d: %s) .aborting receive"),
					static_cast<int32>(Err),
					ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM)->GetSocketError(Err));
				return false;
			}
			// EWOULDBLOCK: genuinely no data yet, keep waiting
		}

		// Check the idle timeout .if no data for 30s, the host is gone.
		if (FPlatformTime::Seconds() >= IdleDeadline)
		{
			UE_LOG(LogForgeMindBridge, Warning,
				TEXT("RecvFull timed out after %.0fs with no data .connection assumed dead"),
				RecvIdleTimeoutSeconds);
			return false;
		}

		FPlatformProcess::Sleep(0.02f);
	}
	return true;
}

bool FForgeMindBridgeClient::SendFrame(const FString& Json)
{
	if (Socket == nullptr)
	{
		return false;
	}

	const FTCHARToUTF8 Converter(*Json);
	const int32 PayloadLength = Converter.Length();
	if (PayloadLength > MaxFrameBytes)
	{
		return false;
	}

	TArray<uint8> Frame;
	Frame.SetNumUninitialized(sizeof(int32) + PayloadLength);
	Frame[0] = static_cast<uint8>(PayloadLength & 0xFF);
	Frame[1] = static_cast<uint8>((PayloadLength >> 8) & 0xFF);
	Frame[2] = static_cast<uint8>((PayloadLength >> 16) & 0xFF);
	Frame[3] = static_cast<uint8>((PayloadLength >> 24) & 0xFF);
	FMemory::Memcpy(Frame.GetData() + sizeof(int32), Converter.Get(), PayloadLength);

	// Non-blocking socket: Send may return 0 bytes (transient buffer full)
	// when the peer's receive window is closed. Distinguish transient
	// back-pressure from a dead connection by checking the socket error.
	// A hard failure (SE_NotConnected / SE_ConnectionReset / SE_EPIPE)
	// breaks the send loop immediately.
	int32 Sent = 0;
	int32 ConsecutiveZeroSends = 0;
	constexpr int32 MaxConsecutiveZeroSends = 1500; // ~30s at 0.02s per retry

	while (Sent < Frame.Num())
	{
		if (bStopping)
		{
			return false;
		}

		int32 Chunk = 0;
		const bool bSendOk = Socket->Send(Frame.GetData() + Sent, Frame.Num() - Sent, Chunk);

		if (bSendOk && Chunk > 0)
		{
			Sent += Chunk;
			ConsecutiveZeroSends = 0;
			continue;
		}

		if (!bSendOk || Chunk <= 0)
		{
			const ESocketErrors Err = ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM)->GetLastErrorCode();
			if (Err == SE_EWOULDBLOCK || Err == SE_TRY_AGAIN)
			{
				// Transient: peer's receive buffer is full, retry after brief sleep
				if (++ConsecutiveZeroSends > MaxConsecutiveZeroSends)
				{
					UE_LOG(LogForgeMindBridge, Warning,
						TEXT("SendFrame timed out after %d retries -- connection assumed dead"),
						MaxConsecutiveZeroSends);
					return false;
				}
				FPlatformProcess::Sleep(0.02f);
				continue;
			}
			// Hard error: connection reset / closed / pipe broken
			UE_LOG(LogForgeMindBridge, Verbose,
				TEXT("SendFrame failed (error %d: %s) -- connection lost"),
				static_cast<int32>(Err),
				ISocketSubsystem::Get(PLATFORM_SOCKETSUBSYSTEM)->GetSocketError(Err));
			return false;
		}
	}
	return true;
}

void FForgeMindBridgeClient::QueueSend(const FString& Json)
{
	FScopeLock Lock(&SendQueueLock);
	SendQueue.Add(Json);
	SendQueueSignal.AtomicSet(true);
}

void FForgeMindBridgeClient::ProcessRequest(TSharedRef<FJsonObject> Message)
{
	const FString Id = Message->GetStringField(TEXT("id"));
	const FString Name = Message->GetStringField(TEXT("name"));

#if !UE_BUILD_SHIPPING
	// DEBUG: log incoming request
	// Dump the full payload so we can see exactly what the host sent.
	{
		FString PayloadDump;
		const TSharedPtr<FJsonObject>* PayloadPtrForDump = nullptr;
		if (Message->TryGetObjectField(TEXT("payload"), PayloadPtrForDump) && PayloadPtrForDump != nullptr)
		{
			TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&PayloadDump);
			FJsonSerializer::Serialize((*PayloadPtrForDump).ToSharedRef(), Writer);
		}
		if (PayloadDump.IsEmpty())
		{
			PayloadDump = TEXT("(none)");
		}
		UE_LOG(LogForgeMindBridge, Log, TEXT("[DEBUG] Request '%s' (id=%s) payload=%s"), *Name, *Id, *PayloadDump);
	}
#endif

	// Validate request token ("rt" field) if the token set is populated.
	// Requests with missing or unknown tokens are rejected.
	if (RequestTokenSet.Num() > 0)
	{
		FString RequestToken;
		Message->TryGetStringField(TEXT("rt"), RequestToken);
		if (RequestToken.IsEmpty() || !RequestTokenSet.Contains(RequestToken))
		{
			UE_LOG(LogForgeMindBridge, Warning, TEXT("Rejecting request '%s' -- invalid request token"), *Name);
#if !UE_BUILD_SHIPPING
			UE_LOG(LogForgeMindBridge, Log, TEXT("[DEBUG] Request '%s' rejected -- token='%s'"), *Name, *RequestToken);
#endif
			TSharedRef<FJsonObject> Rejection = MakeEnvelope(TEXT("response"), Name, Id);
			Rejection->SetStringField(TEXT("error"), TEXT("Invalid request token"));
			QueueSend(SerializeJsonObject(Rejection));
			return;
		}
#if !UE_BUILD_SHIPPING
		else
		{
			UE_LOG(LogForgeMindBridge, Log, TEXT("[DEBUG] Token validated for '%s'"), *Name);
		}
#endif
	}

	// Extract payload for handler dispatch
	TSharedPtr<FJsonObject> Payload;
	const TSharedPtr<FJsonObject>* PayloadPtr = nullptr;
	if (Message->TryGetObjectField(TEXT("payload"), PayloadPtr) && PayloadPtr != nullptr)
	{
		Payload = *PayloadPtr;
	}

#if !UE_BUILD_SHIPPING
	// Log handler lookup
	const bool bHasHandler = RequestHandlers.Contains(Name);
	UE_LOG(LogForgeMindBridge, Log, TEXT("[DEBUG] Dispatching '%s' -- handler %s"), *Name, bHasHandler ? TEXT("found") : TEXT("NOT FOUND"));
	const double HandlerStartTime = FPlatformTime::Seconds();
#endif

	// Dispatch to registered handler
	TSharedPtr<FJsonObject> Result;
	FString Error;
	if (const TFunction<TSharedPtr<FJsonObject>(const TSharedPtr<FJsonObject>&)>* Handler = RequestHandlers.Find(Name))
	{
		Result = (*Handler)(Payload);
	}
	else
	{
		Error = FString::Printf(TEXT("Unknown command '%s'"), *Name);
#if !UE_BUILD_SHIPPING
		UE_LOG(LogForgeMindBridge, Log, TEXT("[DEBUG] No handler registered for '%s'"), *Name);
#endif
	}

#if !UE_BUILD_SHIPPING
	const double HandlerElapsed = FPlatformTime::Seconds() - HandlerStartTime;
#endif

	// Build response envelope
	TSharedRef<FJsonObject> Response = MakeEnvelope(TEXT("response"), Name, Id);
	if (Result.IsValid())
	{
		Response->SetObjectField(TEXT("payload"), Result);
	}
	if (!Error.IsEmpty())
	{
		Response->SetStringField(TEXT("error"), Error);
	}

#if !UE_BUILD_SHIPPING
	// DEBUG: log outgoing response
	{
		FString ResultDump;
		if (Result.IsValid())
		{

			TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&ResultDump);
			FJsonSerializer::Serialize(Result.ToSharedRef(), Writer);
			// Truncate huge payloads to avoid flooding the log
			if (ResultDump.Len() > 2048)
			{
				ResultDump = ResultDump.Left(2048) + TEXT("...(truncated)");
			}
		}
		else
		{
			ResultDump = TEXT("(null)");
		}
		UE_LOG(LogForgeMindBridge, Log, TEXT("[DEBUG] Response '%s' (id=%s) took=%.3fs result=%s%s"),
			*Name, *Id, HandlerElapsed,
			*ResultDump,
			Error.IsEmpty() ? TEXT("") : *FString::Printf(TEXT(" error='%s'"), *Error));
	}
#endif

	QueueSend(SerializeJsonObject(Response));
}

void FForgeMindBridgeClient::ProcessEvent(TSharedRef<FJsonObject> Message)
{
	// Host-originated events are not part of the initial command set - log only
	UE_LOG(LogForgeMindBridge, Verbose, TEXT("Host event '%s' received"), *Message->GetStringField(TEXT("name")));
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandlePing(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetBoolField(TEXT("pong"), true);
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleGetStatus(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetStringField(TEXT("projectFile"), FPaths::GetProjectFilePath());
	const FEngineVersion& Version = FEngineVersion::Current();
	Result->SetStringField(TEXT("engineVersion"), FString::Printf(TEXT("%u.%u.%u"), Version.GetMajor(), Version.GetMinor(), Version.GetPatch()));
	Result->SetNumberField(TEXT("pid"), FPlatformProcess::GetCurrentProcessId());
	Result->SetBoolField(TEXT("isPlaying"), GEditor != nullptr && GEditor->PlayWorld != nullptr);
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleGetCurrentLevel(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	UWorld* World = GEditor != nullptr ? GEditor->GetEditorWorldContext().World() : nullptr;
	if (World == nullptr)
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), TEXT("No editor world is loaded"));
		return Result;
	}

	Result->SetBoolField(TEXT("found"), true);
	// Package path such as /Game/Maps/MyLevel; a fresh unsaved map lives in /Temp
	Result->SetStringField(TEXT("levelPath"), World->GetOutermost()->GetName());
	// Map name with any PIE prefix already stripped
	Result->SetStringField(TEXT("levelName"), World->GetMapName());
	Result->SetBoolField(TEXT("isDirty"), World->GetOutermost()->IsDirty());
	// The editor world's own WorldType is always Editor - PIE runs in a
	// separate world, so ask GEditor for it (same check as get_status).
	Result->SetBoolField(TEXT("isPlayInEditor"), GEditor->PlayWorld != nullptr);
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleCreateLevel(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString LevelPath;
	bool bOverwrite = false;
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("path"), LevelPath)
			&& !Payload->TryGetStringField(TEXT("levelPath"), LevelPath))
		|| LevelPath.IsEmpty())
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required field: path (level package path, e.g. /Game/Tests/Level_Lvl)"));
		return Result;
	}
	Payload->TryGetBoolField(TEXT("overwrite"), bOverwrite);

	// Normalize: strip .umap suffix and object suffix to get package path /Game/...
	FString PackagePath = LevelPath;
	if (PackagePath.EndsWith(TEXT(".umap"), ESearchCase::IgnoreCase))
	{
		PackagePath.LeftChopInline(5);
	}
	const int32 DotIdx = PackagePath.Find(TEXT("."), ESearchCase::CaseSensitive, ESearchDir::FromEnd);
	if (DotIdx != INDEX_NONE)
	{
		PackagePath.LeftChopInline(PackagePath.Len() - DotIdx);
	}

	const FString LevelName = FPackageName::GetLongPackageAssetName(PackagePath);

	// Target level already exists -> open and reuse (idempotent); delete old asset and recreate when bOverwrite.
	if (!bOverwrite)
	{
		const FString ExistingObjectPath = PackagePath + TEXT(".") + LevelName;
		if (UWorld* ExistingWorld = LoadObject<UWorld>(nullptr, *ExistingObjectPath))
		{
			// Idempotent reuse: load as current editor level
			UEditorLoadingAndSavingUtils::LoadMap(PackagePath);
			Result->SetBoolField(TEXT("created"), false);
			Result->SetBoolField(TEXT("loaded"), true);
			Result->SetStringField(TEXT("levelPath"), PackagePath);
			Result->SetStringField(TEXT("levelName"), LevelName);
			Result->SetStringField(TEXT("reason"), TEXT("Level already exists - loaded as current level"));
			return Result;
		}
	}

	// Create empty level, save to target path, then load as current editor level.
	UWorld* NewWorld = GEditor->NewMap();
	if (NewWorld == nullptr)
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), TEXT("GEditor->NewMap() failed"));
		return Result;
	}
	if (!UEditorLoadingAndSavingUtils::SaveMap(NewWorld, PackagePath))
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("SaveMap failed for '%s'"), *PackagePath));
		return Result;
	}
	// SaveMap already changed the package name to the target path; load once to ensure the editor world points to this level.
	UEditorLoadingAndSavingUtils::LoadMap(PackagePath);

	Result->SetBoolField(TEXT("created"), true);
	Result->SetBoolField(TEXT("loaded"), true);
	Result->SetStringField(TEXT("levelPath"), PackagePath);
	Result->SetStringField(TEXT("levelName"), LevelName);
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleSetPie(const TSharedPtr<FJsonObject>& Payload)
{
	// Every field is optional: an empty payload starts PIE with the user's
	// configured settings, matching the toolbar Play button.
	bool bWantRunning = true;
	if (Payload.IsValid())
	{
		// 'action' is the canonical key ("start"/"stop"); 'running' and
		// 'enabled' are accepted as boolean aliases. Without any key the
		// request defaults to start.
		FString Action;
		if (Payload->TryGetStringField(TEXT("action"), Action))
		{
			Action.ToLowerInline();
			if (Action == TEXT("stop") || Action == TEXT("end") || Action == TEXT("exit") || Action == TEXT("quit"))
			{
				bWantRunning = false;
			}
		}
		else if (!Payload->TryGetBoolField(TEXT("running"), bWantRunning))
		{
			Payload->TryGetBoolField(TEXT("enabled"), bWantRunning);
		}
	}

	const bool bIsPlaying = GEditor != nullptr && GEditor->PlayWorld != nullptr;

	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
	if (bWantRunning == bIsPlaying)
	{
		Result->SetBoolField(TEXT("requested"), false);
		Result->SetBoolField(TEXT("isPlaying"), bIsPlaying);
		Result->SetStringField(TEXT("note"), bIsPlaying ? TEXT("PIE is already running") : TEXT("PIE is not running"));
		return Result;
	}

	if (!bWantRunning)
	{
		GEditor->RequestEndPlayMap();
		Result->SetBoolField(TEXT("requested"), true);
		Result->SetBoolField(TEXT("isPlaying"), bIsPlaying);
		Result->SetStringField(TEXT("note"), TEXT("PIE stop was requested; the state changes over the next frames - poll get_current_level to confirm"));
		return Result;
	}

	FRequestPlaySessionParams Params;
	if (Payload.IsValid())
	{
		FString Value;
		if (Payload->TryGetStringField(TEXT("destination"), Value))
		{
			if (Value.Equals(TEXT("inProcess"), ESearchCase::IgnoreCase))
			{
				Params.SessionDestination = EPlaySessionDestinationType::InProcess;
			}
			else if (Value.Equals(TEXT("newProcess"), ESearchCase::IgnoreCase))
			{
				Params.SessionDestination = EPlaySessionDestinationType::NewProcess;
			}
			else if (Value.Equals(TEXT("launcher"), ESearchCase::IgnoreCase))
			{
				Params.SessionDestination = EPlaySessionDestinationType::Launcher;
			}
			else
			{
				Result->SetBoolField(TEXT("requested"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Unknown destination '%s' (valid: inProcess, newProcess, launcher)"), *Value));
				return Result;
			}
		}

		if (Payload->TryGetStringField(TEXT("worldType"), Value))
		{
			if (Value.Equals(TEXT("pie"), ESearchCase::IgnoreCase) || Value.Equals(TEXT("playInEditor"), ESearchCase::IgnoreCase))
			{
				Params.WorldType = EPlaySessionWorldType::PlayInEditor;
			}
			else if (Value.Equals(TEXT("sie"), ESearchCase::IgnoreCase) || Value.Equals(TEXT("simulateInEditor"), ESearchCase::IgnoreCase))
			{
				Params.WorldType = EPlaySessionWorldType::SimulateInEditor;
			}
			else
			{
				Result->SetBoolField(TEXT("requested"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Unknown worldType '%s' (valid: pie, sie)"), *Value));
				return Result;
			}
		}

		if (Payload->TryGetStringField(TEXT("preview"), Value))
		{
			EPlaySessionPreviewType Preview;
			if (Value.Equals(TEXT("none"), ESearchCase::IgnoreCase))
			{
				Preview = EPlaySessionPreviewType::NoPreview;
			}
			else if (Value.Equals(TEXT("mobile"), ESearchCase::IgnoreCase))
			{
				Preview = EPlaySessionPreviewType::MobilePreview;
			}
			else if (Value.Equals(TEXT("vulkan"), ESearchCase::IgnoreCase))
			{
				Preview = EPlaySessionPreviewType::VulkanPreview;
			}
			else if (Value.Equals(TEXT("vr"), ESearchCase::IgnoreCase))
			{
				Preview = EPlaySessionPreviewType::VRPreview;
			}
			else
			{
				Result->SetBoolField(TEXT("requested"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Unknown preview '%s' (valid: none, mobile, vulkan, vr)"), *Value));
				return Result;
			}
			Params.SessionPreviewTypeOverride = Preview;
		}

		if (Payload->TryGetStringField(TEXT("map"), Value) && !Value.IsEmpty())
		{
			Params.GlobalMapOverride = Value;
		}

		if (Payload->TryGetStringField(TEXT("gameMode"), Value) && !Value.IsEmpty())
		{
			// GameMode classes carry the C++ prefix in reflection (AGameModeBase),
			// so tolerate both spellings like list_class_hierarchy does.
			UClass* GameModeClass = ForgeMindFindFirstObject<UClass>(*Value);
			if (GameModeClass == nullptr && Value.StartsWith(TEXT("GameMode")))
			{
				GameModeClass = ForgeMindFindFirstObject<UClass>(*(TEXT("A") + Value));
			}
			if (GameModeClass == nullptr || !GameModeClass->IsChildOf(AGameModeBase::StaticClass()))
			{
				Result->SetBoolField(TEXT("requested"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("'%s' is not a GameMode class"), *Value));
				return Result;
			}
			Params.GameModeOverride = GameModeClass;
		}

		// Reads {x, y, z} / {pitch, yaw, roll} numeric fields; any other
		// representation is ignored so the GameMode PlayerStart is used.
		auto TryGetVector = [&Payload](const TCHAR* Field, FVector& Out) -> bool
		{
			const TSharedPtr<FJsonObject>* Obj = nullptr;
			if (!Payload.IsValid() || !Payload->TryGetObjectField(Field, Obj) || !Obj->IsValid())
			{
				return false;
			}
			double X, Y, Z;
			if (!(*Obj)->TryGetNumberField(TEXT("x"), X) || !(*Obj)->TryGetNumberField(TEXT("y"), Y) || !(*Obj)->TryGetNumberField(TEXT("z"), Z))
			{
				return false;
			}
			Out = FVector(X, Y, Z);
			return true;
		};
		FVector Location;
		if (TryGetVector(TEXT("startLocation"), Location))
		{
			Params.StartLocation = Location;
			const TSharedPtr<FJsonObject>* RotObj;
			if (Payload->TryGetObjectField(TEXT("startRotation"), RotObj) && RotObj->IsValid())
			{
				double Pitch = 0.0, Yaw = 0.0, Roll = 0.0;
				(*RotObj)->TryGetNumberField(TEXT("pitch"), Pitch);
				(*RotObj)->TryGetNumberField(TEXT("yaw"), Yaw);
				(*RotObj)->TryGetNumberField(TEXT("roll"), Roll);
				Params.StartRotation = FRotator(Pitch, Yaw, Roll);
			}
		}

		if (Payload->TryGetStringField(TEXT("standaloneArgs"), Value) && !Value.IsEmpty())
		{
			// Only consumed when destination is newProcess.
			Params.AdditionalStandaloneCommandLineParameters = Value;
		}

		double NumPlayers;
		if (Payload->TryGetNumberField(TEXT("numPlayers"), NumPlayers))
		{
			const int32 Clamped = FMath::Clamp(FMath::RoundToInt32(NumPlayers), 1, 4); // 4 == MAX_LOCAL_PLAYERS
			// Same object the PIE preferences UI edits; persisted with user settings.
			GetMutableDefault<ULevelEditorPlaySettings>()->SetPlayNumberOfClients(Clamped);
		}
	}

	// The session starts over the following frames.
	GEditor->RequestPlaySession(Params);

	Result->SetBoolField(TEXT("requested"), true);
	Result->SetBoolField(TEXT("isPlaying"), bIsPlaying);
	Result->SetStringField(TEXT("note"), TEXT("PIE start was requested; the state changes over the next frames - poll get_current_level to confirm"));
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleOpenAsset(const TSharedPtr<FJsonObject>& Payload)
{
	FString AssetPath;
	if (!Payload.IsValid() || !Payload->TryGetStringField(TEXT("path"), AssetPath) || AssetPath.IsEmpty())
	{
		TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
		Result->SetBoolField(TEXT("opened"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Missing 'path' payload field"));
		return Result;
	}

	UObject* Asset = LoadObject<UObject>(nullptr, *AssetPath);
	bool bOpened = false;
	if (Asset != nullptr && GEditor != nullptr)
	{
		UAssetEditorSubsystem* AssetEditor = GEditor->GetEditorSubsystem<UAssetEditorSubsystem>();
		bOpened = AssetEditor != nullptr && AssetEditor->OpenEditorForAsset(Asset);
	}

	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetBoolField(TEXT("opened"), bOpened);
	Result->SetStringField(TEXT("path"), AssetPath);
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleExecConsole(const TSharedPtr<FJsonObject>& Payload)
{
	FString Command;
	if (!Payload.IsValid() || !Payload->TryGetStringField(TEXT("command"), Command) || Command.IsEmpty())
	{
		TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
		Result->SetBoolField(TEXT("executed"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Missing 'command' payload field"));
		return Result;
	}

	if (GEditor != nullptr)
	{
		GEditor->Exec(nullptr, *Command, *GLog);
	}

	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetBoolField(TEXT("executed"), true);
	Result->SetStringField(TEXT("command"), Command);
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleQuitEditor(const TSharedPtr<FJsonObject>& Payload)
{
	// Plugin-initiated graceful shutdown: same path as closing the main window
	// (unsaved-package prompt, debugger/GC/slow-task guards). Never a host-side
	// process kill. The close itself is deferred to the next tick so this
	// acknowledgement reaches the host before the editor starts shutting down.
	bQuitRequested.AtomicSet(true);

	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetBoolField(TEXT("exitRequested"), true);
	Result->SetStringField(TEXT("note"), TEXT("Editor close starts on the next frame; if unsaved assets exist a prompt appears and the editor only proceeds to unload after the user decides (cancelling keeps it running), otherwise it unloads directly"));
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleListClassHierarchy(const TSharedPtr<FJsonObject>& Payload)
{
	FString ClassName;
	if (!Payload.IsValid() || !Payload->TryGetStringField(TEXT("class"), ClassName) || ClassName.IsEmpty())
	{
		TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Missing 'class' payload field"));
		return Result;
	}

	double ParentDepth = 0.0;
	double ChildDepth = 0.0;
	// Absent depth fields fall back to useful defaults: parents walk up to
	// the UObject cap (chains are short anyway), children get one level.
	// An explicitly passed 0 still means "skip that direction".
	const bool bHasParentLevels = Payload->TryGetNumberField(TEXT("parentLevels"), ParentDepth);
	const bool bHasChildLevels = Payload->TryGetNumberField(TEXT("childLevels"), ChildDepth);
	const int32 ParentLevels = bHasParentLevels ? FMath::Max(0, FMath::RoundToInt32(ParentDepth)) : INT32_MAX;
	const int32 ChildLevels = bHasChildLevels ? FMath::Max(0, FMath::RoundToInt32(ChildDepth)) : 1;

	// Reflection classes carry no namespace; tolerate the C++ type prefix
	// (AActor -> Actor) since class objects are registered without it.
	bool bPrefixStripped = false;
	UClass* Class = ForgeMindFindFirstObject<UClass>(*ClassName);
	if (Class == nullptr && ClassName.Len() > 1 && (ClassName[0] == 'U' || ClassName[0] == 'A' || ClassName[0] == 'F'))
	{
		Class = ForgeMindFindFirstObject<UClass>(*ClassName.RightChop(1));
		bPrefixStripped = Class != nullptr;
	}

	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
	if (Class == nullptr)
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("class"), ClassName);
		Result->SetStringField(TEXT("reason"), TEXT("No UClass with that reflection name is loaded"));
		return Result;
	}

	// Parents: nearest first. UObject is the top of the world relevant to
	// Blueprint - even if C++ reflection could expose anything above it, it is
	// never reported.
	TArray<TSharedPtr<FJsonValue>> Parents;
	UClass* Super = Class->GetSuperClass();
	for (int32 Depth = 0; Super != nullptr && Depth < ParentLevels; ++Depth)
	{
		Parents.Add(MakeShared<FJsonValueString>(Super->GetName()));
		if (Super == UObject::StaticClass())
		{
			break;
		}
		Super = Super->GetSuperClass();
	}

	// Children: breadth-first over every loaded UClass (native and Blueprint
	// generated alike), level by level, names sorted for stable output.
	TArray<TSharedPtr<FJsonValue>> Children;
	TArray<UClass*> CurrentLevel;
	CurrentLevel.Add(Class);
	for (int32 Depth = 1; Depth <= ChildLevels && CurrentLevel.Num() > 0; ++Depth)
	{
		TArray<UClass*> NextLevel;
		for (TObjectIterator<UClass> It; It; ++It)
		{
			UClass* Candidate = *It;
			if (Candidate->GetSuperClass() != nullptr && CurrentLevel.Contains(Candidate->GetSuperClass()))
			{
				NextLevel.Add(Candidate);
			}
		}
		NextLevel.Sort([](UClass& Lhs, UClass& Rhs) { return Lhs.GetName() < Rhs.GetName(); });
		for (UClass* Child : NextLevel)
		{
			TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
			Entry->SetNumberField(TEXT("depth"), Depth);
			Entry->SetStringField(TEXT("name"), Child->GetName());
			Children.Add(MakeShared<FJsonValueObject>(Entry));
		}
		CurrentLevel = MoveTemp(NextLevel);
	}

	Result->SetBoolField(TEXT("found"), true);
	Result->SetStringField(TEXT("class"), ClassName);
	Result->SetStringField(TEXT("reflectionName"), Class->GetName());
	if (bPrefixStripped)
	{
		Result->SetStringField(TEXT("note"), FString::Printf(
			TEXT("UE reflection names drop the C++ type prefix - '%s' is registered as '%s'"),
			*ClassName, *Class->GetName()));
	}
	Result->SetArrayField(TEXT("parents"), Parents);
	Result->SetArrayField(TEXT("children"), Children);
	Result->SetNumberField(TEXT("childCount"), Children.Num());
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleListLevelActors(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	UWorld* World = GEditor != nullptr ? GEditor->GetEditorWorldContext().World() : nullptr;
	ULevel* Level = World != nullptr ? World->GetCurrentLevel() : nullptr;
	if (Level == nullptr)
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), TEXT("No editor world is loaded"));
		return Result;
	}

	TArray<TSharedPtr<FJsonValue>> Actors;
	Actors.Reserve(Level->Actors.Num());
	for (const TObjectPtr<AActor>& Actor : Level->Actors)
	{
		if (!IsValid(Actor))
		{
			continue;
		}
		TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
		Entry->SetStringField(TEXT("name"), Actor->GetName());
		Entry->SetStringField(TEXT("type"), Actor->GetClass()->GetName());
		Actors.Add(MakeShared<FJsonValueObject>(Entry));
	}

	Result->SetBoolField(TEXT("found"), true);
	Result->SetNumberField(TEXT("count"), Actors.Num());
	Result->SetArrayField(TEXT("actors"), Actors);
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleGetActorDetails(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString ActorName;
	if (!Payload.IsValid() || !Payload->TryGetStringField(TEXT("name"), ActorName) || ActorName.IsEmpty())
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Missing required field: name (get exact names via list_level_actors)"));
		return Result;
	}

	UWorld* World = GEditor != nullptr ? GEditor->GetEditorWorldContext().World() : nullptr;
	ULevel* Level = World != nullptr ? World->GetCurrentLevel() : nullptr;

	AActor* Actor = nullptr;
	if (Level != nullptr)
	{
		for (const TObjectPtr<AActor>& Candidate : Level->Actors)
		{
			if (IsValid(Candidate) && Candidate->GetName() == ActorName)
			{
				Actor = Candidate;
				break;
			}
		}
	}

	if (Actor == nullptr)
	{
		Result->SetBoolField(TEXT("found"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No actor named '%s' in the current level"), *ActorName));
		return Result;
	}

	Result->SetBoolField(TEXT("found"), true);
	Result->SetStringField(TEXT("name"), Actor->GetName());
	Result->SetStringField(TEXT("type"), Actor->GetClass()->GetName());

	// Properties = actor-layer properties + root component properties.
	// Dump the root component first, then the actor on top so that on any
	// duplicate key the actor layer wins.
	TSharedRef<FJsonObject> Properties = MakeShared<FJsonObject>();
	if (USceneComponent* Root = Actor->GetRootComponent())
	{
		FJsonObjectConverter::UStructToJsonObject(Root->GetClass(), Root, Properties);
	}
	FJsonObjectConverter::UStructToJsonObject(Actor->GetClass(), Actor, Properties);
	Result->SetObjectField(TEXT("properties"), Properties);

	// Component list (name + reflection type), sorted by name for stable output.
	TArray<TPair<FString, FString>> ComponentList;
	for (const UActorComponent* Component : Actor->GetComponents())
	{
		if (IsValid(Component))
		{
			ComponentList.Add(MakeTuple(Component->GetName(), Component->GetClass()->GetName()));
		}
	}
	ComponentList.Sort([](const TPair<FString, FString>& Lhs, const TPair<FString, FString>& Rhs) { return Lhs.Key < Rhs.Key; });

	TArray<TSharedPtr<FJsonValue>> Components;
	Components.Reserve(ComponentList.Num());
	for (const TPair<FString, FString>& Item : ComponentList)
	{
		TSharedRef<FJsonObject> Entry = MakeShared<FJsonObject>();
		Entry->SetStringField(TEXT("name"), Item.Key);
		Entry->SetStringField(TEXT("type"), Item.Value);
		Components.Add(MakeShared<FJsonValueObject>(Entry));
	}
	Result->SetArrayField(TEXT("components"), Components);
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleDeleteActor(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString ActorName;
	if (!Payload.IsValid() || !Payload->TryGetStringField(TEXT("name"), ActorName) || ActorName.IsEmpty())
	{
		Result->SetBoolField(TEXT("deleted"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Missing required field: name (get exact names via list_level_actors)"));
		return Result;
	}

	UWorld* World = GEditor != nullptr ? GEditor->GetEditorWorldContext().World() : nullptr;
	ULevel* Level = World != nullptr ? World->GetCurrentLevel() : nullptr;

	AActor* Actor = nullptr;
	if (Level != nullptr)
	{
		for (const TObjectPtr<AActor>& Candidate : Level->Actors)
		{
			if (IsValid(Candidate) && Candidate->GetName() == ActorName)
			{
				Actor = Candidate;
				break;
			}
		}
	}

	if (Actor == nullptr)
	{
		Result->SetBoolField(TEXT("deleted"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No actor named '%s' in the current level"), *ActorName));
		return Result;
	}

	// Official editor path: notifies editor subsystems and participates in undo.
	UEditorActorSubsystem* ActorSubsystem = GEditor->GetEditorSubsystem<UEditorActorSubsystem>();
	const bool bDeleted = ActorSubsystem != nullptr && ActorSubsystem->DestroyActor(Actor);

	Result->SetBoolField(TEXT("deleted"), bDeleted);
	Result->SetStringField(TEXT("name"), ActorName);
	if (!bDeleted)
	{
		Result->SetStringField(TEXT("reason"), TEXT("EditorActorSubsystem::DestroyActor rejected the actor"));
	}
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleSpawnActor(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString ClassName;
	if (!Payload.IsValid() || !Payload->TryGetStringField(TEXT("type"), ClassName) || ClassName.IsEmpty())
	{
		Result->SetBoolField(TEXT("spawned"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Missing required field: type (find classes via list_class_hierarchy)"));
		return Result;
	}

	// Reflection names usually drop the C++ 'A' prefix (Actor, PointLight),
	// so tolerate both spellings like the set_pie gameMode lookup does.
	UClass* ActorClass = ForgeMindFindFirstObject<UClass>(*ClassName);
	if (ActorClass == nullptr && ClassName.StartsWith(TEXT("A")))
	{
		ActorClass = ForgeMindFindFirstObject<UClass>(*(ClassName.RightChop(1)));
	}
	if (ActorClass == nullptr || !ActorClass->IsChildOf(AActor::StaticClass()) || ActorClass->HasAnyClassFlags(CLASS_Abstract))
	{
		Result->SetBoolField(TEXT("spawned"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("'%s' is not a spawnable (non-abstract) Actor class"), *ClassName));
		return Result;
	}

	// Read location and rotation from the payload (optional, default to zero).
	FVector SpawnLocation = FVector::ZeroVector;
	FRotator SpawnRotation = FRotator::ZeroRotator;

	const TArray<TSharedPtr<FJsonValue>>* LocArray = nullptr;
	if (Payload->TryGetArrayField(TEXT("location"), LocArray) && LocArray->Num() >= 3)
	{
		double X = 0, Y = 0, Z = 0;
		(*LocArray)[0]->TryGetNumber(X);
		(*LocArray)[1]->TryGetNumber(Y);
		(*LocArray)[2]->TryGetNumber(Z);
		SpawnLocation = FVector(X, Y, Z);
	}

const TArray<TSharedPtr<FJsonValue>>* RotArray = nullptr;
	if (Payload->TryGetArrayField(TEXT("rotation"), RotArray) && RotArray->Num() >= 3)
	{
		double P = 0, Yaw = 0, R = 0;
		(*RotArray)[0]->TryGetNumber(P);   // Pitch
		(*RotArray)[1]->TryGetNumber(Yaw); // Yaw
		(*RotArray)[2]->TryGetNumber(R);   // Roll
		SpawnRotation = FRotator(P, Yaw, R);
	}

	// Optional 'name': rename the spawned actor so later commands (e.g. the
	// level_blueprint levelLiteral node, which matches placed actors by
	// Actor->GetName()) can address it deterministically. If an actor with
	// that name already exists in the current level we reuse it and report
	// spawned=true (idempotent re-runs), instead of stacking duplicates.
	FString RequestedName;
	Payload->TryGetStringField(TEXT("name"), RequestedName);

	if (!RequestedName.IsEmpty())
	{
		UWorld* WorldNow = GEditor != nullptr ? GEditor->GetEditorWorldContext().World() : nullptr;
		ULevel* LevelNow = WorldNow != nullptr ? WorldNow->GetCurrentLevel() : nullptr;
		if (LevelNow != nullptr)
		{
			for (const TObjectPtr<AActor>& Candidate : LevelNow->Actors)
			{
				if (IsValid(Candidate) && Candidate->GetName() == RequestedName)
				{
					Result->SetBoolField(TEXT("spawned"), true);
					Result->SetStringField(TEXT("name"), Candidate->GetName());
					Result->SetStringField(TEXT("type"), Candidate->GetClass()->GetName());
					Result->SetBoolField(TEXT("alreadyExists"), true);
					return Result;
				}
			}
		}
	}

	// Official editor path: registers the actor with the current level and
	// notifies editor subsystems; bTransient = false keeps it saveable.
	UEditorActorSubsystem* ActorSubsystem = GEditor->GetEditorSubsystem<UEditorActorSubsystem>();
	AActor* NewActor = ActorSubsystem != nullptr
		? ActorSubsystem->SpawnActorFromClass(ActorClass, SpawnLocation, SpawnRotation)
		: nullptr;

	if (NewActor == nullptr)
	{
		Result->SetBoolField(TEXT("spawned"), false);
		Result->SetStringField(TEXT("reason"), TEXT("EditorActorSubsystem::SpawnActorFromClass returned no actor"));
		return Result;
	}

	if (!RequestedName.IsEmpty())
	{
		NewActor->Rename(*RequestedName, NewActor->GetOuter(),
			REN_DontCreateRedirectors | REN_ForceNoResetLoaders);
	}

	Result->SetBoolField(TEXT("spawned"), true);
	Result->SetStringField(TEXT("name"), NewActor->GetName());
	Result->SetStringField(TEXT("type"), NewActor->GetClass()->GetName());
	return Result;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleSetActorProperty(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString ActorName;
	FString PropertyName;
	// 'name' is the canonical actor key; accept 'actor' as an alias because
	// that is the term the command name itself uses and models tend to emit it.
	if (!Payload.IsValid()
		|| (!Payload->TryGetStringField(TEXT("name"), ActorName)
			&& !Payload->TryGetStringField(TEXT("actor"), ActorName)) || ActorName.IsEmpty()
		|| !Payload->TryGetStringField(TEXT("property"), PropertyName) || PropertyName.IsEmpty()
		|| !Payload->HasField(TEXT("value")))
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: name (the actor name from list_level_actors), property, value (property names via get_actor_details)"));
		return Result;
	}

	UWorld* World = GEditor != nullptr ? GEditor->GetEditorWorldContext().World() : nullptr;
	ULevel* Level = World != nullptr ? World->GetCurrentLevel() : nullptr;

	AActor* Actor = nullptr;
	if (Level != nullptr)
	{
		for (const TObjectPtr<AActor>& Candidate : Level->Actors)
		{
			if (IsValid(Candidate) && Candidate->GetName() == ActorName)
			{
				Actor = Candidate;
				break;
			}
		}
	}

	if (Actor == nullptr)
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No actor named '%s' in the current level"), *ActorName));
		return Result;
	}

	// Same merge scope as get_actor_details: actor layer first, root component
	// as fallback - so any property the details dump shows is settable here.
	UObject* Owner = Actor;
	FProperty* Property = FindFProperty<FProperty>(Actor->GetClass(), *PropertyName);
	if (Property == nullptr)
	{
		if (USceneComponent* Root = Actor->GetRootComponent())
		{
			Property = FindFProperty<FProperty>(Root->GetClass(), *PropertyName);
			Owner = Root;
		}
	}
	if (Property == nullptr)
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Property '%s' not found on actor '%s' or its root component"), *PropertyName, *ActorName));
		return Result;
	}

	const TSharedPtr<FJsonValue> JsonValue = Payload->TryGetField(TEXT("value"));
	if (!JsonValue.IsValid())
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Missing required field: value"));
		return Result;
	}

	void* ValueData = Property->ContainerPtrToValuePtr<void>(Owner);
	bool bImported = false;

	// FText properties get a dedicated conversion: the host only passes the
	// describing information (source text / namespace / key / string table /
	// format arguments) and the full FText - including its localization
	// identity - is assembled here. ImportText_Direct would only ever produce
	// identity-less literals, so it is bypassed for text properties.
	if (FTextProperty* TextProperty = CastField<FTextProperty>(Property))
	{
		FText NewText;
		FString TextError;
		if (!TryBuildTextFromJson(JsonValue, NewText, TextError))
		{
			Result->SetBoolField(TEXT("set"), false);
			Result->SetStringField(TEXT("reason"), TextError);
			return Result;
		}
		TextProperty->SetPropertyValue(ValueData, NewText);
		bImported = true;
	}
	else if (JsonValue->Type == EJson::Object || JsonValue->Type == EJson::Array)
	{
		// Struct/container properties don't accept JSON through ImportText, so
		// write them via the JsonUtilities converter instead.
		bImported = FJsonObjectConverter::JsonValueToUProperty(JsonValue, Property, ValueData);
	}
	else
	{
		// Scalar values go through the property text import path so that
		// strings get full property parsing. Object properties accept both a
		// bare object path (/Game/Props/SM_Chair.SM_Chair) and the exported
		// wrapper form (/Script/Engine.StaticMesh'/Game/.../SM_Chair...');
		// unloaded assets are resolved/loaded by the import machinery.
		FString ImportText;
		if (JsonValue->Type == EJson::String)
		{
			ImportText = JsonValue->AsString();
		}
		else if (JsonValue->Type == EJson::Number)
		{
			ImportText = FString::SanitizeFloat(JsonValue->AsNumber());
		}
		else if (JsonValue->Type == EJson::Boolean)
		{
			ImportText = JsonValue->AsBool() ? TEXT("true") : TEXT("false");
		}
		else
		{
			Result->SetBoolField(TEXT("set"), false);
			Result->SetStringField(TEXT("reason"), TEXT("Unsupported JSON value type for property import"));
			return Result;
		}

		const TCHAR* TextPtr = *ImportText;
		bImported = Property->ImportText(TextPtr, ValueData, PPF_PropertyWindow, Owner) != nullptr;

		if (!bImported && CastField<FObjectPropertyBase>(Property) != nullptr)
		{
			// Give asset references a clearer diagnosis than the generic
			// "cannot be assigned" - the usual culprits are a bad path, a
			// missing asset or a class that does not match the property.
			Result->SetBoolField(TEXT("set"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(
				TEXT("Object reference '%s' could not be resolved for property '%s' - check the asset path (%s) exists and its class matches"),
				*ImportText, *PropertyName, *CastField<FObjectPropertyBase>(Property)->PropertyClass->GetName()));
			return Result;
		}
	}

	if (!bImported)
	{
		Result->SetBoolField(TEXT("set"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Value cannot be assigned to property '%s'"), *PropertyName));
		return Result;
	}

	// Editor-visible change: refresh details panel and feed the undo buffer.
	FPropertyChangedEvent ChangedEvent(Property);
	Owner->PreEditChange(Property);
	Owner->PostEditChangeProperty(ChangedEvent);

	Result->SetBoolField(TEXT("set"), true);
	Result->SetStringField(TEXT("name"), ActorName);
	Result->SetStringField(TEXT("property"), PropertyName);
	Result->SetStringField(TEXT("layer"), Owner == Actor ? TEXT("actor") : TEXT("rootComponent"));
	return Result;
}

UClass* ForgeMindResolveBlueprintClassByName(const FString& BlueprintName)
{
	// Blueprint generated class name is BlueprintName + "_C"; sibling blueprints in the same group may not be loaded into memory yet.
	if (UClass* Generated = ForgeMindFindFirstObject<UClass>(*(BlueprintName + TEXT("_C"))))
	{
		return Generated;
	}
	// Full asset path (e.g. /Game/Tests/UE5_0/Inherit_Base_B from [Extends] after cross-block
	// alias injection): load directly, do not prepend a root directory — that produced
	// "/Game/Tests//Game/Tests/..." double-slash package names and a Fatal at
	// UObjectGlobals.cpp:614 (2026-10-08, Inherit_Child_B crash).
	if (BlueprintName.StartsWith(TEXT("/")))
	{
		FString BpAssetPath = BlueprintName;
		if (!BpAssetPath.Contains(TEXT(".")))
		{
			BpAssetPath = FString::Printf(TEXT("%s.%s"), *BlueprintName, *FPaths::GetBaseFilename(BlueprintName));
		}
		if (UBlueprint* Bp = LoadObject<UBlueprint>(nullptr, *BpAssetPath))
		{
			if (Bp->GeneratedClass != nullptr)
			{
				return Bp->GeneratedClass;
			}
		}
		return nullptr;
	}
	// If still not found, load by known asset root directory and get its GeneratedClass.
	for (const FString& Root : { FString(TEXT("/Game/Tests")), FString(TEXT("/Game")) })
	{
		const FString BpAssetPath = FString::Printf(TEXT("%s/%s.%s"), *Root, *BlueprintName, *BlueprintName);
		if (UBlueprint* Bp = LoadObject<UBlueprint>(nullptr, *BpAssetPath))
		{
			if (Bp->GeneratedClass != nullptr)
			{
				return Bp->GeneratedClass;
			}
		}
	}
	return nullptr;
}

TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleCreateBlueprint(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString ClassName;
	FString AssetPath;
	// 'path' is the canonical asset key; accept 'assetPath' as an alias since
	// the tool's own top-level 'path' carries the .uproject instead.
	if (!Payload.IsValid()
		|| !Payload->TryGetStringField(TEXT("parentClass"), ClassName) || ClassName.IsEmpty()
		|| (!Payload->TryGetStringField(TEXT("path"), AssetPath)
			&& !Payload->TryGetStringField(TEXT("assetPath"), AssetPath)) || AssetPath.IsEmpty())
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), TEXT("Required fields: parentClass (reflection class name, e.g. Actor), path (project asset path, e.g. /Game/Blueprints/BP_Thing)"));
		return Result;
	}

	// A Blueprint Macro Library is a UBlueprint asset whose parent class is
	// UBlueprintFunctionLibrary and whose BlueprintType is BPTYPE_MacroLibrary.
	// It has no dedicated C++ class, so "BlueprintMacroLibrary" is an asset-type
	// alias rather than a reflectable class name. Resolve it to the function
	// library base class and flag it for BPTYPE_MacroLibrary below.
	const bool bIsMacroLibrary = ClassName.Equals(TEXT("BlueprintMacroLibrary"), ESearchCase::IgnoreCase);

	// Same resolution rules as list_class_hierarchy: reflection classes carry
		// no namespace, tolerate the C++ type prefix (AActor -> Actor).
		UClass* ParentClass = ForgeMindFindFirstObject<UClass>(*ClassName);
		if (ParentClass == nullptr && ClassName.Len() > 1 && (ClassName[0] == 'U' || ClassName[0] == 'A' || ClassName[0] == 'F'))
		{
			ParentClass = ForgeMindFindFirstObject<UClass>(*ClassName.RightChop(1));
		}
		// Blueprint class (e.g. [Extends(typeof(BP_Base))]): named resolution generates class (_C suffix +
		// asset root directory load). Linked to the class field of add_blueprint_node.
		if (ParentClass == nullptr)
		{
			ParentClass = ForgeMindResolveBlueprintClassByName(ClassName);
		}
		if (ParentClass == nullptr)
	{
		if (bIsMacroLibrary)
		{
			ParentClass = UBlueprintFunctionLibrary::StaticClass();
		}
		else
		{
			Result->SetBoolField(TEXT("created"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("No UClass named '%s' is loaded (valid names via list_class_hierarchy)"), *ClassName));
			return Result;
		}
	}
	// A Blueprint Function Library is a UBlueprint whose parent is the abstract
	// UBlueprintFunctionLibrary base. The editor's own "New Blueprint Function
	// Library" action creates exactly that, bypassing CanCreateBlueprintOfClass
	// (that gate only filters the parent-class picker dialog and refuses abstract
	// classes). Permit it explicitly so create_blueprint can build function
	// libraries from the MCP/DSL side.
	const bool bIsFunctionLibrary = ParentClass->IsChildOf(UBlueprintFunctionLibrary::StaticClass());
	if (!bIsFunctionLibrary && !FKismetEditorUtilities::CanCreateBlueprintOfClass(ParentClass))
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Class '%s' cannot be used as a blueprint parent"), *ParentClass->GetName()));
		return Result;
	}

	// Path may arrive as /Game/Dir/BP_Name or /Game/Dir/BP_Name.BP_Name; keep
	// the package part and derive the asset name from its last element.
	FString PackagePath = AssetPath;
	int32 DotIndex = INDEX_NONE;
	if (PackagePath.FindChar(TEXT('.'), DotIndex))
	{
		PackagePath = PackagePath.Left(DotIndex);
	}
	if (!FPackageName::IsValidLongPackageName(PackagePath) || !PackagePath.StartsWith(TEXT("/Game/")))
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), TEXT("path must be a project content path like /Game/Blueprints/BP_Thing"));
		return Result;
	}
	const FString AssetName = FPaths::GetCleanFilename(PackagePath);

	// Refuse to clobber an existing asset.
	// Use AssetRegistry (not FindPackage) to avoid stale in-memory packages
	// from deleted-but-not-GC'd assets.
	{
		FAssetRegistryModule& AssetRegModule = FModuleManager::LoadModuleChecked<FAssetRegistryModule>(TEXT("AssetRegistry"));
		IAssetRegistry& AssetRegistry = AssetRegModule.Get();
		TArray<FAssetData> ExistingAssets;
		AssetRegistry.GetAssetsByPackageName(FName(*PackagePath), ExistingAssets, /*bIncludeOnlyOnDiskAssets=*/true);
		if (ExistingAssets.Num() > 0)
		{
			Result->SetBoolField(TEXT("created"), false);
			Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("An asset already exists at '%s'"), *PackagePath));
			return Result;
		}
	}

	// Guard against stale in-memory packages: a previous creation attempt
	// that crashed or a failed deletion can leave a UPackage in memory that
	// AssetRegistry (disk-only check above) doesn't see. CreateBlueprint
	// asserts if FindObject<UBlueprint>(Outer, Name) finds a remnant.
	UPackage* Package = nullptr;
	if (UPackage* StalePackage = FindObject<UPackage>(nullptr, *PackagePath))
	{
		// Check if the package actually contains a blueprint
		if (UBlueprint* StaleBP = FindObject<UBlueprint>(StalePackage, *AssetName))
		{
// Close any open asset editor for this blueprint, the editor
				// subsystem holds a strong reference that prevents GC.
			if (GEditor)
			{
				UAssetEditorSubsystem* AssetEditorSys = GEditor->GetEditorSubsystem<UAssetEditorSubsystem>();
				if (AssetEditorSys)
				{
					AssetEditorSys->CloseAllEditorsForAsset(StaleBP);
				}
			}

			// Mark the stale blueprint AND its generated classes as garbage.
			// A UBlueprint has child UBlueprintGeneratedClass objects that
			// also need to be marked, otherwise they keep the package alive.
			StaleBP->MarkAsGarbage();
			if (UClass* GenClass = StaleBP->GeneratedClass)
			{
				GenClass->MarkAsGarbage();
			}
			if (UClass* SkeletonClass = StaleBP->ParentClass)
			{
				// Skeleton generated class may also hold references
			}
			// Also clear the package's link to the blueprint so the
			// subsequent FindObject in CreateBlueprint won't find it.
			StalePackage->MarkAsGarbage();
			// Force a garbage collection pass to clear the stale objects.
			CollectGarbage(GARBAGE_COLLECTION_KEEPFLAGS, true);
		}
		// Re-check: if the stale blueprint is gone after GC, create a fresh package
		if (FindObject<UBlueprint>(nullptr, *FString::Printf(TEXT("%s.%s"), *PackagePath, *AssetName)))
		{
			// Still there, try renaming it out of the way so a fresh
			// package can be created. This is the nuclear option: we
			// rename the stale object to a unique temporary name, then
			// let GC eventually collect it.
			if (UBlueprint* StillStaleBP = FindObject<UBlueprint>(nullptr, *FString::Printf(TEXT("%s.%s"), *PackagePath, *AssetName)))
			{
				const FString TempName = FString::Printf(TEXT("Stale_%s_%s"), *AssetName, *FGuid::NewGuid().ToString(EGuidFormats::Digits));
				StillStaleBP->Rename(*TempName, StalePackage, REN_DontCreateRedirectors | REN_ForceNoResetLoaders);
				StillStaleBP->MarkAsGarbage();
				CollectGarbage(GARBAGE_COLLECTION_KEEPFLAGS, true);
			}
			// Final check
			if (FindObject<UBlueprint>(nullptr, *FString::Printf(TEXT("%s.%s"), *PackagePath, *AssetName)))
			{
				// Still there, refuse rather than crash
				Result->SetBoolField(TEXT("created"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("A stale blueprint '%s' exists in memory but not on disk. Clear the editor's transient data or restart the editor."), *PackagePath));
				return Result;
			}
		}
		// The stale object is gone, create a fresh package
		Package = CreatePackage(*PackagePath);
	}
	else
	{
		Package = CreatePackage(*PackagePath);
	}

	// When the parent class is UUserWidget (or a subclass of it), the asset
	// must be created as a UWidgetBlueprint with a UWidgetBlueprintGeneratedClass
	// so the editor opens the Widget Blueprint designer (with its hierarchy
	// panel, palette, and default root Canvas Panel) instead of a bare event
	// graph.  Using UBlueprint for a UserWidget parent produces a broken asset
	// that has no designer tab and cannot hold widget child slots.
	UClass* BlueprintClass = UBlueprint::StaticClass();
	UClass* GeneratedClass = UBlueprintGeneratedClass::StaticClass();
	if (ParentClass->IsChildOf(UUserWidget::StaticClass()))
	{
		BlueprintClass = UWidgetBlueprint::StaticClass();
		GeneratedClass = UWidgetBlueprintGeneratedClass::StaticClass();
	}

	// Argument order follows the engine's own usage (Kismet2.cpp): blueprint
	// class first, generated class second - swapping them feeds nullptr into
	// the TSubclassOf conversion and crashes NewObject with a null class.
	// BlueprintType must match the editor's factory (EditorFactories.cpp:6972):
	// BPTYPE_FunctionLibrary for UBlueprintFunctionLibrary parents, else
	// BPTYPE_Normal. Using BPTYPE_Normal for a function library breaks
	// __WorldContext handling — IsStaticFunctionGraph (EdGraphSchema_K2.cpp:5678)
	// checks BlueprintType first, and bIsStaticFunction
	// (BlueprintCompilationManager.cpp:2866) gates both __WorldContext
	// injection and MD_WorldContext metadata; a mismatch between the entry
	// node's FUNC_Static-driven __WorldContext pin and the missing metadata
	// causes "Could not find a pin for the parameter __WorldContext" on load.
		// BPTYPE_MacroLibrary for blueprint macro libraries (their macros expand
	// inline at call sites via tunnel nodes). BPTYPE_FunctionLibrary for ordinary
	// static function libraries. BPTYPE_Normal otherwise.
	const EBlueprintType BPType = bIsMacroLibrary ? BPTYPE_MacroLibrary : (bIsFunctionLibrary ? BPTYPE_FunctionLibrary : BPTYPE_Normal);
	UBlueprint* Blueprint = FKismetEditorUtilities::CreateBlueprint(
		ParentClass, Package, FName(*AssetName), BPType,
		BlueprintClass, GeneratedClass);
	if (Blueprint == nullptr)
	{
		Result->SetBoolField(TEXT("created"), false);
		Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("Blueprint creation failed for parent '%s' at '%s'"), *ParentClass->GetName(), *PackagePath));
		return Result;
	}

	// Make it immediately usable and visible, then mark it modified and keep
	// it dirty: the dirty flag is what drives the editor's persistence - it
	// shows the modified marker in the content browser and triggers the save
	// prompt on close, which is what actually writes the .uasset to disk.
	// Without it nothing gets persisted.
	FKismetEditorUtilities::CompileBlueprint(Blueprint);
	FAssetRegistryModule::AssetCreated(Blueprint);
	Blueprint->MarkPackageDirty();

	// Optional 'implements' field: array of interface asset paths to implement.
	// FBlueprintEditorUtils::ImplementNewInterface attaches the interface and
	// auto-builds an implementation function graph for each interface message
	// (signature only — the caller fills in the body via list_blueprint_graph
	// + add_blueprint_node, since create_function_graph would collide with
	// the auto-built graph).
	const TArray<TSharedPtr<FJsonValue>>* ImplementsArray = nullptr;
	if (Payload->TryGetArrayField(TEXT("implements"), ImplementsArray) && ImplementsArray != nullptr && ImplementsArray->Num() > 0)
	{
		for (const TSharedPtr<FJsonValue>& ImplVal : *ImplementsArray)
		{
			FString InterfacePath;
			if (!ImplVal.IsValid() || !ImplVal->TryGetString(InterfacePath) || InterfacePath.IsEmpty())
			{
				continue;
			}
			UBlueprint* InterfaceBP = ForgeMindBlueprintUtils::ResolveBlueprint(InterfacePath);
			if (InterfaceBP == nullptr || InterfaceBP->GeneratedClass == nullptr)
			{
				Result->SetBoolField(TEXT("created"), false);
				Result->SetStringField(TEXT("reason"), FString::Printf(TEXT("implements: no interface blueprint found at '%s'"), *InterfacePath));
				return Result;
			}
			FBlueprintEditorUtils::ImplementNewInterface(Blueprint, InterfaceBP->GeneratedClass->GetFName());

			// ImplementNewInterface skips void interface functions: its condition
			// CanKismetOverrideFunction && !FunctionCanBePlacedAsEvent is false for
			// void + FUNC_BlueprintEvent (FunctionCanBePlacedAsEvent returns true
			// when there are no output params). UE expects those to be implemented
			// via event nodes, but the test expects a function graph with
			// entry/result nodes. Manually create implementation graphs for the
			// skipped (void) functions, mirroring ImplementNewInterface's own path
			// (CreateNewGraph + AddInterfaceGraph) and registering them on the
			// FBPInterfaceDescription so list_blueprint_graph discovers them.
			UClass* InterfaceClass = InterfaceBP->GeneratedClass;
			UE_LOG(LogForgeMindBridge, Warning, TEXT("implements: InterfaceClass=%s, ImplementedInterfaces.Num=%d"), InterfaceClass != nullptr ? *InterfaceClass->GetName() : TEXT("null"), Blueprint->ImplementedInterfaces.Num());
			FBPInterfaceDescription* ImplDesc = nullptr;
			for (FBPInterfaceDescription& Desc : Blueprint->ImplementedInterfaces)
			{
				UE_LOG(LogForgeMindBridge, Warning, TEXT("implements: Desc.Interface=%s"), Desc.Interface != nullptr ? *Desc.Interface->GetName() : TEXT("null"));
				if (Desc.Interface == InterfaceClass)
				{
					ImplDesc = &Desc;
					break;
				}
			}
			UE_LOG(LogForgeMindBridge, Warning, TEXT("implements: ImplDesc=%s"), ImplDesc != nullptr ? TEXT("found") : TEXT("null"));
			for (TFieldIterator<UFunction> FuncIt(InterfaceClass, EFieldIteratorFlags::IncludeSuper); FuncIt; ++FuncIt)
			{
				UFunction* InterfaceFunc = *FuncIt;
				const bool bCanOverride = UEdGraphSchema_K2::CanKismetOverrideFunction(InterfaceFunc);
				UE_LOG(LogForgeMindBridge, Warning, TEXT("implements: Func=%s, CanOverride=%d, Flags=0x%X"), *InterfaceFunc->GetName(), bCanOverride ? 1 : 0, InterfaceFunc->FunctionFlags);
				if (!bCanOverride)
				{
					continue;
				}
				const FName FuncName = InterfaceFunc->GetFName();
				UEdGraph* ExistingGraph = FindObject<UEdGraph>(Blueprint, *FuncName.ToString());
				UE_LOG(LogForgeMindBridge, Warning, TEXT("implements: ExistingGraph=%s"), ExistingGraph != nullptr ? TEXT("exists") : TEXT("null"));
				if (ExistingGraph != nullptr)
				{
					continue;
				}
				UEdGraph* NewImplGraph = FBlueprintEditorUtils::CreateNewGraph(
					Blueprint, FuncName, UEdGraph::StaticClass(), UEdGraphSchema_K2::StaticClass());
				NewImplGraph->bAllowDeletion = false;
				// Mirror ImplementNewInterface (BlueprintEditorUtils.cpp L6488): bind the
				// graph to the interface function so the compiler treats it as that
				// message's implementation rather than an unrelated same-named graph.
				NewImplGraph->InterfaceGuid = FBlueprintEditorUtils::FindInterfaceFunctionGuid(InterfaceFunc, InterfaceClass);
				FBlueprintEditorUtils::AddInterfaceGraph(Blueprint, NewImplGraph, InterfaceClass);
				// AddInterfaceGraph (CreateFunctionGraphTerminators) only creates a
				// result node when the function has out params. void interface
				// functions get an entry but no result, while the test expects both
				// (FindTerminators looks for K2Node_FunctionResult). Create it manually.
				bool bHasResult = false;
				UK2Node_FunctionEntry* EntryNode = nullptr;
				for (UEdGraphNode* Node : NewImplGraph->Nodes)
				{
					if (Cast<UK2Node_FunctionResult>(Node) != nullptr) bHasResult = true;
					if (EntryNode == nullptr) EntryNode = Cast<UK2Node_FunctionEntry>(Node);
				}
				if (!bHasResult)
				{
					UK2Node_FunctionResult* ResultNode = NewObject<UK2Node_FunctionResult>(NewImplGraph);
					ResultNode->CreateNewGuid();
					ResultNode->PostPlacedNewNode();
					if (EntryNode != nullptr)
					{
						ResultNode->FunctionReference = EntryNode->FunctionReference;
					}
					ResultNode->SetFlags(RF_Transactional);
					ResultNode->AllocateDefaultPins();
					NewImplGraph->AddNode(ResultNode);
				}
				if (ImplDesc != nullptr)
				{
					ImplDesc->Graphs.Add(NewImplGraph);
				}
				// NOTE: deliberately NOT added to Blueprint->FunctionGraphs.
				// ImplementNewInterface (BlueprintEditorUtils.cpp L6490-6492) registers an
				// implementation graph on the FBPInterfaceDescription only; adding it to
				// FunctionGraphs as well makes the compiler see the same graph twice and
				// fail with "found multiple functions with the same name <X>", which also
				// shows up in the editor as the message appearing both under Interfaces and
				// under Functions. Lookups reach it through CollectTargetableGraphs.
				UE_LOG(LogForgeMindBridge, Warning, TEXT("implements: created graph %s, nodes=%d"), *FuncName.ToString(), NewImplGraph->Nodes.Num());
			}
		}
		// Re-compile so the new interface graphs are baked into the class.
		FKismetEditorUtilities::CompileBlueprint(Blueprint);
		Blueprint->MarkPackageDirty();
	}

	Result->SetBoolField(TEXT("created"), true);
	Result->SetBoolField(TEXT("dirty"), Blueprint->GetPackage()->IsDirty());
	Result->SetStringField(TEXT("path"), FString::Printf(TEXT("%s.%s"), *PackagePath, *AssetName));
	Result->SetStringField(TEXT("parentClass"), ParentClass->GetName());
	return Result;
}

// ---------------------------------------------------------------------------
// list_editor_settings
//
// Enumerates all UDeveloperSettings subclasses whose GetCategoryName()
// matches the requested category (case-insensitive substring). For each
// matching class, lists every UPROPERTY whose name contains "color" (case-
// insensitive) along with its current value as "#RRGGBB" hex. If no
// category filter is supplied, all UDeveloperSettings subclasses are
// returned.
//
// Payload keys (all optional):
//   category   - case-insensitive substring to filter by
//                (e.g. "Content Editors", "General", "Plugins")
//   colorsOnly - if true (default), only properties containing "color" are
//                listed; if false, all properties are listed
// ---------------------------------------------------------------------------
TSharedPtr<FJsonObject> FForgeMindBridgeClient::HandleListEditorSettings(const TSharedPtr<FJsonObject>& Payload)
{
	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();

	FString CategoryFilter;
	const bool bHasCategory = Payload.IsValid() && Payload->TryGetStringField(TEXT("category"), CategoryFilter);
	bool bColorsOnly = true;
	if (Payload.IsValid())
	{
		bool bColorsOnlyValue = false;
		if (Payload->TryGetBoolField(TEXT("colorsOnly"), bColorsOnlyValue))
		{
			bColorsOnly = bColorsOnlyValue;
		}
	}

	TArray<TSharedPtr<FJsonValue>> SettingsArray;

	// Iterate over every loaded UClass and collect UDeveloperSettings
	// subclasses. TObjectIterator<UClass> walks all registered class
	// objects in the current process.
	for (TObjectIterator<UClass> ClassIt; ClassIt; ++ClassIt)
	{
		UClass* Class = *ClassIt;
		if (Class == nullptr)
		{
			continue;
		}
		if (!Class->IsChildOf(UDeveloperSettings::StaticClass()))
		{
			continue;
		}
		if (Class == UDeveloperSettings::StaticClass())
		{
			continue; // skip the base class itself
		}

		// Retrieve the category name through the CDO's virtual function.
		const UDeveloperSettings* CDO = Cast<UDeveloperSettings>(Class->GetDefaultObject(/*bNative*/ false));
		if (CDO == nullptr)
		{
			continue;
		}

		const FString CategoryName = CDO->GetCategoryName().ToString();
		const FString SectionName = CDO->GetSectionName().ToString();

		// Apply category filter (case-insensitive substring match).
		if (bHasCategory && !CategoryName.Contains(CategoryFilter, ESearchCase::IgnoreCase))
		{
			continue;
		}

		// Use the class metadata DisplayName if available.
		FString DisplayName = Class->GetName();
		if (Class->HasMetaData(TEXT("DisplayName")))
		{
			DisplayName = Class->GetMetaData(TEXT("DisplayName"));
		}

		TSharedPtr<FJsonObject> Entry = MakeShared<FJsonObject>();
		Entry->SetStringField(TEXT("className"), Class->GetName());
		Entry->SetStringField(TEXT("category"), CategoryName);
		Entry->SetStringField(TEXT("section"), SectionName);
		Entry->SetStringField(TEXT("displayName"), DisplayName);

		// Walk all UPROPERTY fields and collect those whose name contains
		// "color" (when bColorsOnly is true) or all of them.
		TArray<TSharedPtr<FJsonValue>> PropsArray;
		for (TFieldIterator<FProperty> PropIt(Class); PropIt; ++PropIt)
		{
			FProperty* Prop = *PropIt;
			if (Prop == nullptr)
			{
				continue;
			}

			const FString PropName = Prop->GetName();
			if (bColorsOnly && !PropName.Contains(TEXT("Color"), ESearchCase::IgnoreCase))
			{
				continue;
			}

			TSharedPtr<FJsonObject> PropEntry = MakeShared<FJsonObject>();
			PropEntry->SetStringField(TEXT("name"), PropName);
			PropEntry->SetStringField(TEXT("type"), Prop->GetClass()->GetName());

			// For FColor / FLinearColor properties, extract the current
			// value from the CDO and format it as "#RRGGBB".
			const void* ValuePtr = Prop->ContainerPtrToValuePtr<void>(CDO);
			if (FStructProperty* StructProp = CastField<FStructProperty>(Prop))
			{
				if (StructProp->Struct == TBaseStructure<FColor>::Get())
				{
					const FColor* Color = static_cast<const FColor*>(ValuePtr);
					PropEntry->SetStringField(TEXT("value"),
						FString::Printf(TEXT("#%02X%02X%02X"), Color->R, Color->G, Color->B));
				}
				else if (StructProp->Struct == TBaseStructure<FLinearColor>::Get())
				{
					const FLinearColor* LinearColor = static_cast<const FLinearColor*>(ValuePtr);
					const FColor Color = LinearColor->ToFColor(true);
					PropEntry->SetStringField(TEXT("value"),
						FString::Printf(TEXT("#%02X%02X%02X"), Color.R, Color.G, Color.B));
				}
			}

			PropsArray.Add(MakeShared<FJsonValueObject>(PropEntry));
		}
		Entry->SetArrayField(TEXT("properties"), PropsArray);
		SettingsArray.Add(MakeShared<FJsonValueObject>(Entry));
	}

	Result->SetBoolField(TEXT("success"), true);
	Result->SetArrayField(TEXT("settings"), SettingsArray);
	Result->SetNumberField(TEXT("count"), SettingsArray.Num());
	return Result;
}
