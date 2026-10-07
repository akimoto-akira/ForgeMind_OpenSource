# ForgeMind 개발 가이드

ForgeMind를 확장하려는 개발자용: 빌드 방법, 디버깅 방법, MCP 도구 추가, 엔진 어댑터 추가, 테스트 실행. 본 문서는 **공개 슬라이스**(`OpenSource\`, `UEProject\`, `tests\`)의 실제 코드에 기반합니다.

## 1. 프로젝트 개요

| 프로젝트 | 책무 |
|---|---|
| `OpenSource\ForgeMindServer` | MCP 서버 호스트: 내장 도구 4개, TCP 브리지 시작, 플러그인 로딩 |
| `OpenSource\ForgeMind.OfflineEngineKit` | 플러그인: engine / project / project_files / switch_version / launch / build / editor 도구 |
| `OpenSource\ForgeMind.Assets` | 플러그인: asset 도구(오프라인 `.uasset` 읽기) |
| `OpenSource\ForgeMind.Knowledge` | 플러그인: knowledge 도구(지식 검색) |
| `OpenSource\ForgeMind.EngineScanner` | CLI 스캐너: 엔진 열거와 `.spk` 패키징 |
| `OpenSource\ForgeMind.Scan` / `.Scan.Abstractions` / `.Scan.Unreal` | 스캔 인덱스: 추상, 패키징, UE 구현(DocumentEngine) |
| `OpenSource\ForgeMind.Speedy` | SPK 패키징 형식(고성능 팩 파일) |
| `tests\ForgeMind.PublicTests` | 공개 단위 테스트(엔진 비의존) |
| `tests\ForgeMindServerTest` | 서버 통합 테스트(UE 에디터 필수) |
| `UEProject\ForgeMind_5_0` | 샘플 UE 프로젝트(5.0–5.8) |

## 2. 빌드

```powershell
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# 플러그인과 라이브러리 프로젝트: contributing.md §3 참조(ForgeMind.sln 전체를 직접 빌드하지 마세요. 상용 프로젝트 참조 포함)
```

- 호스트 진입점: `OpenSource\ForgeMindServer\Program.cs`(기본 HTTP 상주 `127.0.0.1:8765`, `--stdio`로 전환, `--port` / `FORGEMIND_PORT`로 포트 덮어쓰기).
- 로그: `LogManager`. 도구는 `ToolResult`(성공 / 실패 메시지 포함) 반환.

## 3. 플러그인 메커니즘과 MCP 도구 추가

호스트 내장 도구는 4개뿐입니다: `settings`, `get_task_result`, `list_executing_tasks`, `cleanup_tasks`. 나머지는 **plugins 디렉터리의 플러그인**이 주입합니다:

- 플러그인 디렉터리: `<호스트 디렉터리>\plugins\`(환경 변수 `FORGEMIND_PLUGINS_DIR`로 덮어씀).
- 플러그인 패키지 레이아웃: 메인 DLL + 동일 이름 `.manifest.json` + 의존성을 `plugins\<플러그인 이름>\`에 배치. 루트의 느슨한 DLL도 지원(수동 배치).
- 호스트 `PluginLoader`가 플러그인 스캔 → `PluginContext` 주입(도구 테이블, 엔진 레지스트리, 브리지) → 플러그인 `OnLoad`가 `RegisterTool`로 도구 등록.
- 플러그인 무결성: `PluginIntegrityVerifier` / `PluginSigningKeys`가 플러그인 매니페스트와 서명을 검증. 신뢰된 플러그인은 ServiceOverride로 서비스를 확장할 수 있습니다(§7 참조).

새 MCP 도구를 추가하려면:

1. `ITool` 계약(`OpenSource\ForgeMindServer\Compat\ITool.cs`) 구현:
   - `Name` / `DisplayName`: 도구 이름과 표시 이름;
   - `Description`: AI 클라이언트용 설명;
   - `GetParameterSchema()`: JSON Schema 스타일 파라미터 선언;
   - `Execute(Guid callerId, Dictionary<string, object> parameters)`: `ToolResult` 반환.
2. 새 플러그인 프로젝트 생성(또는 기존 플러그인 예: OfflineEngineKit에 합류), `IPlugin` 구현, `OnLoad`에서 `RegisterTool` 호출.
3. 동일 디렉터리에 동일 이름 `.manifest.json` 배치(플러그인 설명 / 의존성 선언).
4. 엔진 측 기능이 필요하면 브리지 명령으로 전송하고 어댑터 측에서 명령 핸들러 구현(§5 참조).
5. 테스트 케이스(§6) 작성, `tools-reference.md` 업데이트.

## 4. 엔진 어댑터 추가 방법

ForgeMind는 '어댑터'로 엔진 고유 로직을 수용합니다. 새 엔진 추가는 동일 브리지 프로토콜(4바이트 길이 접두사 + JSON 엔벨로프, `api-reference.md` 참조)을 따릅니다:

1. 브리지 클라이언트 구현: `%USERPROFILE%\.forgemind\bridge.json`을 읽어 디스커버리와 인증 완료, 연결 → 핸드셰이크 → 메시지 루프.
2. 명령 이름으로 화이트리스트 핸들러 등록. 모든 명령은 엔진 메인 스레드(UE 게임 스레드 상당)에서 실행.
3. 엔진 측 기능(에셋 읽기/쓰기, 프로젝트 조작 등) 구현, 서버 도구 반환에 매핑.
4. 해당 엔진 테스트 케이스와 문서 추가.

첫 구현은 UE 어댑터 `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\`(Editor 모듈 플러그인, 브리지 클라이언트 + IR 어플라이어).

## 5. 엔진 측 브리지 명령(현재 UE 구현)

- 어댑터 측 `ForgeMindForUE` 플러그인은 브리지 클라이언트를 포함하며 명령 핸들러는 게임 스레드에서 실행.
- 에셋 레벨 조작은 플러그인 내 IR 도메인 핸들러를 재사용해 에디터 로직 중복 방지.
- 디버깅: UE 에디터 로그 `LogForgeMindBridge` 카테고리. 연결 끊김 시 지수 백오프로 재연결.

## 6. 테스트 실행

### 공개 단위 테스트(에디터 불필요)

```powershell
dotnet run --project tests\ForgeMind.PublicTests\ForgeMind.PublicTests.csproj
```

SPK 패키징 왕복, 다중 버전 SPK, 스캔 데이터 Provider 등 커버(`EmbeddedSpkTests` / `ScanPackRoundTripTests` / `SpkMultiVersionTests` / `SpkScanDataProviderTests`).

### 서버 통합 테스트(UE 에디터 + 샘플 프로젝트 필수)

```powershell
# 기본: 호스트와 플러그인 프리빌드, plugins 디렉터리에 스테이징, 호스트 실행해 플러그인 로딩, 리플렉션으로 전체 ITestCase 실행
ForgeMindServerTest.exe

# 필터: group + order
ForgeMindServerTest.exe [group] [order]          # 또는 --Test [group] [order]
# 프리빌드 스킵
ForgeMindServerTest.exe --NoBuild
```

메커니즘:

- 프리빌드: 먼저 `ForgeMindServer.csproj` 빌드, `ForgeMind.*` 디렉터리 아래 동일 이름 `.manifest.json`을 가진 플러그인 프로젝트를 발견해 개별 빌드, `<진입 디렉터리>\plugins\<플러그인 이름>\`에 스테이징.
- 실행: 리플렉션으로 `ITestCase` 발견, `Group` / `Order`로 정렬, **fail-fast**. '에디터 시작 / 에디터 종료'도 일반 케이스(`Order=0` 시작, `Order=int.MaxValue` 종료), 같은 Group 내에서 세션 공유.
- 케이스는 `ITool.Execute`로 도구를 직접 호출(MCP 프로토콜 경유 없이). 도구는 내부에서 브리지 경유로 UE와 통신.
- 출력: 실행 후 JSON 리포트와 읽기 쉬운 요약 출력(출력 디렉터리는 실행 결과 메시지에 표시).
- 테스트 자산: `Task\UE5_0\` 아래 30+ `Blueprint_*_Tests.cs`(블루프린트 도메인: 변수 / 함수 / 이벤트 / 머티리얼 / 매크로 / 인터페이스 등) + `Task\Base\Engine_List_Tests.cs`; `BlueprintTestHelper`가 브리지 명령, 텍스트 디스패치, IR 스냅샷 비교를 캡슐화(오수정 방지).

> 주의: '텍스트 경로(DSL)'에 관련된 테스트 케이스는 상용 DSL 기능에 의존합니다. 공개 슬라이스 내 통합 테스트는 주로 브리지 명령 경로를 사용하며 슬라이스 내 실제 `Task\` 케이스에 따릅니다.

## 7. 라이선스와 상용 기능(개요)

- 기본 `OpenAuthorizationService`(`ForgeMindServer\Authorization\Open\`): 커뮤니티 고정 기능, 라이선스 파일을 전혀 파싱하지 않음.
- 상용 라이선스는 plugin Manifest의 ServiceOverride로 `IAuthorizationService` 구현을 치환(호스트 `ApplyServiceOverride`). 공개 슬라이스에는 그 상용 플러그인이 없고 치환 메커니즘만 유지(호스트는 '상용' 개념을 인식하지 않고 추상 타입으로만 치환).

## 8. 코딩 규약

- 파일 헤더 주석에 SPDX 라이선스 식별자와 저작권 표시(Apache-2.0).
- 상용 모듈을 참조하지 않음. 새 플러그인은 `contributing.md`의 슬라이스 경계를 따름.
