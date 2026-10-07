# ForgeMind 도구 레퍼런스

모든 도구는 '호스트 내장'과 '플러그인 주입'으로 나뉩니다. 현재 OpenSource 릴리스 슬라이스에는 13개의 MCP 도구가 포함됩니다. 상용 플러그인은 추가 도구(DSL 에셋 생성 등, §3 참조, 오픈소스 목록에는 미포함)를 제공할 수 있습니다.

## 1. 호스트 내장 도구

호스트(`OpenSource\ForgeMindServer`)는 다음 도구를 포함하며 플러그인이 필요 없습니다:

| 도구 | 설명 |
|---|---|
| `settings` | 서버 설정 읽기/쓰기(`McpSettingsManager`, 변경 즉시 영속화) |
| `get_task_result` | 장시간 작업(launch / build 등) 실행 결과 조회 |
| `list_executing_tasks` | 현재 진행 중인 장시간 작업 목록 |
| `cleanup_tasks` | 종료된 작업 레코드 정리 |

## 2. 플러그인 주입 도구

플러그인은 `OnLoad`에서 `PluginContext.RegisterTool`로 주입하며 클라이언트 연결 후 표시됩니다. 빌드 후 플러그인 DLL + 동일 이름 `.manifest.json`을 호스트의 `plugins\` 디렉터리(또는 `FORGEMIND_PLUGINS_DIR` 지정 디렉터리)에 배치하세요.

### ForgeMind.OfflineEngineKit(7개)

| 도구 | 설명 |
|---|---|
| `engine` | 로컬 설치 엔진 조회(Launcher / 소스 빌드, 버전과 앱 이름 포함) |
| `project` | UE 프로젝트 조회/관리(프로젝트 파일 식별, 프로젝트 정보 확인) |
| `project_files` | 프로젝트 파일 목록(디렉터리/타입으로 필터) |
| `switch_version` | 프로젝트를 지정 엔진 버전으로 전환(UnrealVersionSelector 경유) |
| `launch` | 에디터 또는 패키징된 실행 파일 실행(디버그 구성 지원) |
| `build` | 프로젝트 빌드(기본 타깃 / Editor 타깃) |
| `editor` | 에디터 내 조작(씬, 뷰포트, 에셋 등, 브리지 경유 발행) |

파라미터 Schema는 각 도구 소스 `GetParameterSchema()`(`OpenSource\ForgeMind.OfflineEngineKit\*.cs`)를 참조. 일부 엔진 조작은 `ServerPermissionChecker`의 권한 제약을 받습니다.

### ForgeMind.Assets(1개)

| 도구 | 설명 |
|---|---|
| `asset` | `.uasset` 구조 오프라인 읽기(이름, 오브젝트, 프로퍼티, 블루프린트 그래프, 그래프/프로퍼티로 조회 지원) |

구현은 `UAsset\UAssetSerializer` / `BlueprintGraphReader` 등(`OpenSource\ForgeMind.Assets\`)에 기반합니다.

### ForgeMind.Knowledge(1개)

| 도구 | 설명 |
|---|---|
| `knowledge` | 지식 검색: 프로젝트/엔진 지식 항목 조회(출처: 폴더 소스 `FolderKnowledgeSource`, SPK 인덱스 팩 소스 `IndexPackSource`) |

구현은 `OpenSource\ForgeMind.Knowledge\Knowledge\`.

## 3. 상용 버전 도구(개요)

상용 버전은 DSL 에셋 생성 및 관련 조회 도구를 추가할 수 있으며 상용 플러그인이 주입합니다. 구체적인 도구 이름, API, DSL 기능은 상용 버전 문서를 따릅니다. OpenSource에는 DSL 도구가 포함되지 않습니다.

## 4. 도구 호출 규약

- 장시간 작업 도구는 작업 ID를 반환합니다. `get_task_result`로 결과를 폴링합니다.
- 도구 실패 시 `ToolResult` 실패 메시지를 반환합니다(클라이언트에 직접 표시 가능).
- 도구 설명과 파라미터 Schema는 MCP `tools/list`에 표시되는 내용 그 자체이며 소스 코드와 일치합니다.
