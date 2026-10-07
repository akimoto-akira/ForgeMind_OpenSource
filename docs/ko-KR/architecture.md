# ForgeMind 아키텍처

ForgeMind는 **MCP를 인터페이스로 하는 게임 엔진 제어 플랫폼**입니다: 서버(호스트)는 MCP 프로토콜을 통해 AI 클라이언트에 도구를 공개하고, 도구 기능은 주로 플러그인이 주입합니다. 엔진 측에서는 어댑터 플러그인이 로컬 TCP 브리지를 통해 조작을 수용합니다. UE가 현재 구현이며 플랫폼 기능은 다른 엔진으로 확장할 수 있습니다. 본 문서는 **공개 슬라이스**에서 보이는 코드에 기반하며 상용 기능은 §7에서 개요만 설명합니다.

## 1. 전체 그림

```
┌─────────────┐   MCP(HTTP/stdio)   ┌──────────────────────┐
│ AI 클라이언트   │ ◄────────────────► │ ForgeMindServer(호스트) │
└─────────────┘                     │  • 내장 도구 ×4          │
                                    │  • PluginLoader         │
                                    │  • TCP 브리지 서버        │
                                    │  • 엔진 레지스트리         │
                                    └──────────┬───────────┘
                                               │ 로컬 TCP 브리지(4바이트 길이 + JSON 엔벨로프)
                                    ┌──────────▼───────────┐
                                    │ UE 어댑터 플러그인       │
                                    │ (ForgeMindForUE)      │
                                    └──────────────────────┘
```

- **호스트**(`OpenSource\ForgeMindServer`): MCP 서버 본체.
- **플러그인**(`OpenSource\ForgeMind.*Plugin`): `IPlugin`을 통해 도구를 등록. 호스트는 최소 도구 세트만 포함.
- **엔진 어댑터**(`UEProject\...\Plugins\ForgeMindForUE`): 에디터 내 플러그인. 브리지의 서버로 엔진 측 기능을 구현.
- **스캔/패키징**(`ForgeMind.Scan.Abstractions`, `ForgeMind.Scan`, `ForgeMind.Scan.Unreal`, `ForgeMind.Speedy`, `ForgeMind.EngineScanner`): 엔진 오프라인 기능의 인덱스와 SPK 패키징을 제공.
- **샘플 프로젝트**(`UEProject\ForgeMind_5_0`): 테스트와 공동 디버깅용.

## 2. 호스트(ForgeMindServer)

- **진입점**: `Program.cs`는 기본적으로 `127.0.0.1:8765`의 HTTP 상주 서비스로 실행(Streamable HTTP + SSE 호환). `--stdio`로 spawn-per-client 모드 전환. 포트는 `--port` / `FORGEMIND_PORT`로 덮어쓸 수 있습니다.
- **내장 도구**(`ForgeMindServerHost`의 도구 테이블, 4개만):
  - `settings`(`SettingsTool`, 설정 읽기/쓰기);
  - `get_task_result` / `list_executing_tasks` / `cleanup_tasks`(장시간 작업 조회와 정리).
- **플러그인 로딩**: `PluginLoader`는 `plugins\`(기본 `<호스트 디렉터리>\plugins\`, `FORGEMIND_PLUGINS_DIR`로 덮어씀)을 스캔하고 `PluginContext`를 통해 도구 테이블, 엔진 레지스트리, 브리지를 주입. 플러그인 `OnLoad`는 `RegisterTool`을 호출해 도구를 주입. 플러그인 패키지는 '하위 디렉터리 = 플러그인 패키지'(메인 DLL + 동일 이름 manifest + 의존성)와 '루트의 느슨한 DLL' 두 레이아웃을 지원. 로딩은 지연 평가(도구 테이블 첫 접근 시 트리거), 실패 시 해당 플러그인만 건너뛰고 호스트에 영향 없음.
- **엔진 레지스트리**: `EngineRegistry`는 시작 시 로컬 엔진을 열거(Launcher 설치의 `UE_x.y` 우선, 소스 빌드로 폴백), 읽기 전용 스냅샷을 플러그인에 주입.
- **TCP 브리지 서버**: `ForgeMindServerBridgeServer`. 엔진 어댑터의 디스커버리와 연결용(§5 참조).

## 3. 플러그인과 도구 주입

| 플러그인 | 제공 도구 | 책무 |
|---|---|---|
| `ForgeMind.OfflineEngineKit` | `engine` / `project` / `project_files` / `switch_version` / `launch` / `build` / `editor` | 엔진과 프로젝트 관리, 실행/빌드/에디터 조작 |
| `ForgeMind.Assets` | `asset` | `.uasset` 구조 오프라인 읽기(`UAssetArchive` / `BlueprintGraphReader` 등) |
| `ForgeMind.Knowledge` | `knowledge` | 지식 검색(폴더 소스 / 인덱스 팩 소스, `KnowledgeStore`) |
| `ForgeMind.EngineScanner` | (CLI 도구, MCP 도구 아님) | 엔진 열거 → `UnrealScanner` 스캔 → `ScanResultPacker`로 `.spk` 패키징 |

호스트와 플러그인은 `ITool` 계약(`Compat\ITool.cs`)으로 연동: 이름/설명/파라미터 Schema/`Execute`가 `ToolResult` 반환. `ForgeMindServerTest`도 동일 `ITool`로 도구를 직접 호출(MCP 프로토콜 경유 없이)해 동작을 검증합니다.

## 4. 스캔과 SPK(오프라인 기능 기반)

- `ForgeMind.Scan.Abstractions`: `IScanner` / `IScanDataProvider` / `ScanEntry` / `ScanManifest` / `ScanSignatures` 등 추상.
- `ForgeMind.Scan.Unreal`: `UnrealScanner` + `DocumentEngine`(`Ue5DocumentEngine`, `HeaderScanner`, `RulesCompiler`, `UAssetScanner`, `ScanIndexStore` 등)——UE 헤더 파일, 규칙 파일, 블루프린트 에셋을 스캔 인덱스로 파싱. `ScanIndexStore`는 런타임 메모리 인덱스이며 디스크에 영속화되지 않습니다. 이는 **기존 ScanCache와 다릅니다**——현재 파이프라인은 ScanCache 사전 로딩/분산 캐시를 사용하지 않습니다(소스에 역사적 호환 흔적이 일부 남아 있을 뿐이며 현행 메커니즘을 구성하지 않습니다).
- `ForgeMind.Scan`: `ScanPackLoader` / `ScanPackReader` / `ScanResultPacker` / `SpkScanDataProvider`가 스캔 패키지를 조립·읽기.
- `ForgeMind.Speedy`: `SpeedyPack`은 고성능 패키징 형식을 제공(`FilePackReader` / `IPackReader` / `IPackWriter` / `IPackTransaction`, 내부에 CRC, 디렉터리 매핑, 선행 기록 로그 `WriteAheadLog` 등 포함).
- 사용법: `ForgeMind.EngineScanner <spkPath>`로 `.spk`를 출력. knowledge 등 기능은 `SpkScanDataProvider`를 통해 소비.

## 5. 브리지 프로토콜과 엔진 어댑터

- 브리지: 로컬 TCP. 메시지는 4바이트 길이 접두사 + JSON 엔벨로프(`BridgeMessage` / `BridgeSession` / `FrameCodec`). 어댑터는 먼저 `%USERPROFILE%\.forgemind\bridge.json`을 읽어 디스커버리와 인증을 완료하고 연결 → 핸드셰이크 → 메시지 루프.
- 서버 측은 명령 이름으로 어댑터 구현에 디스패치. 명령 핸들러는 엔진 메인 스레드(UE 게임 스레드)에서 실행되어 에디터 상태와 일관성을 보장.
- 엔진 어댑터(UE 측)는 `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\Source\ForgeMindForUE\`에 있으며 Editor 모듈로 브리지 클라이언트와 IR 어플라이어를 포함. UE 샘플 프로젝트는 5.0–5.8 호환.
- 새 엔진 추가는 동형 브리지 클라이언트와 명령 핸들러만 구현하면 됩니다(`development-guide.md` §4 참조). 호스트와 플러그인 변경 불필요.

## 6. 라이선스와 보안(공개 부분)

- 기본 인가: `OpenAuthorizationService`(`Authorization\Open\`)——커뮤니티 고정 기능, **라이선스 파일을 파싱하지 않습니다**.
- 플러그인 보안: `PluginIntegrityVerifier`(매니페스트 무결성 검증), `PluginSigningKeys`(서명 검증); `ManifestLoader`는 플러그인 매니페스트를 파싱하고 ServiceOverride(추상 타입으로 호스트 서비스 치환, 예: `IAuthorizationService`)를 지원.
- 브리지 인증: 어댑터 연결은 로컬 `bridge.json` 자격 증명을 읽어 핸드셰이크를 완료해야 합니다(`security.md` 참조).

## 7. 상용 기능(개요)

- **DSL 언어와 도메인 번역기**(클로즈드 소스): 사람이 읽을 수 있는 텍스트에서 엔진 에셋을 생성하는 상용 기능. 공개 슬라이스에는 DSL 프레임워크 계층만(`ForgeMindServer\DSL\` 아래 `ParserFactory`, `ITranslator`, `TranslatorRegistry`, `IIrConverter`, `IrConverterRegistry`, `DiagnosticReporter` 등 인터페이스와 레지스트리); 구체적인 문법, 도메인 번역기, IR 규칙은 상용 모듈이 제공하며 슬라이스에 없습니다.
- **상용 라이선스 시스템**(클로즈드 소스): 라이선스 발급, 라이선스 검증, 라이선스 서비스, 옵션 환경 바인딩을 포함. 구체적인 구현, 서명 알고리즘, 라이선스 파일 형식, 서버 측 로직은 공개 슬라이스에 속하지 않으며 외부에는 '추가 기능 제공'으로만 표시됩니다.

> OpenSource 버전은 완전한 오픈소스 도구 세트와 브리지 기능을 제공합니다. Commercial 버전은 동일한 호스트, 플러그인 메커니즘, 브리지 프로토콜에 기반해 상용 기능(DSL 에셋 생성, 상용 라이선스, 엔터프라이즈 지원)을 확장합니다.

## 8. 기능 경계 표

| 기능 | 귀속 |
|---|---|
| MCP / ForgeMindServer | OpenSource ✅ |
| OfflineEngineKit | OpenSource ✅ |
| Assets | OpenSource ✅ |
| Knowledge | OpenSource ✅ |
| Scan / SPK | OpenSource ✅ |
| EngineScanner | OpenSource ✅ |
| Speedy | OpenSource ✅ |
| DSL 확장 인터페이스 | OpenSource ✅ |
| DSL Parser | Commercial 🔒 |
| DSL Translator | Commercial 🔒 |
| IR 규칙 | Commercial 🔒 |
| DSL Asset Generator | Commercial 🔒 |
| IAuthorizationService | OpenSource ✅ |
| 상용 라이선스 구현 | Commercial 🔒 |
| License Generator | Commercial 🔒 |
| License Server | Commercial 🔒 |
| 비밀 키 / 서명 시스템 | Commercial 🔒 |
| 라이선스 프로토콜 내부 세부 사항 | Commercial 🔒 |
