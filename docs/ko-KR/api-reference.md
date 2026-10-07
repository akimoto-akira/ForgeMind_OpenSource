# ForgeMind API 레퍼런스

이 문서는 ForgeMind 외부 인터페이스의 공개 부분을 설명합니다: MCP 프로토콜 연결, 브리지 프로토콜, 도구 계약, 장시간 작업 메커니즘. 상용 기능(DSL)은 §6에서 개요만 설명합니다.

## 1. MCP 연결

- **HTTP 모드**(기본): `http://127.0.0.1:8765/mcp`, Streamable HTTP(이전 SSE와 호환). 호스트 프로세스는 상주하며 한 번의 연결로 여러 클라이언트 세션에 걸쳐 사용할 수 있습니다.
- **stdio 모드**: `ForgeMindServer.exe --stdio`, 클라이언트별 프로세스 실행.
- 포트: `--port N` > 환경 변수 `FORGEMIND_PORT` > 기본 8765.

표준 MCP 메서드 지원: `initialize`, `tools/list`, `tools/call`, `notifications/cancelled` 등. 도구 Schema는 `ITool.GetParameterSchema()`가 제공합니다.

## 2. 도구 계약

서버 도구는 통일 인터페이스(`OpenSource\ForgeMindServer\Compat\ITool.cs`)를 구현합니다:

- `Name` / `DisplayName`: 도구 식별자와 표시 이름;
- `Description`: AI 클라이언트용 설명 텍스트;
- `GetParameterSchema()`: 파라미터 선언(JSON Schema 스타일);
- `Execute(Guid callerId, Dictionary<string, object> parameters)`: 실행 후 `ToolResult`(성공 결과 또는 실패 메시지) 반환.

호출 흐름: 클라이언트 `tools/call` → 호스트가 `ITool` 식별 → 파라미터 검증 → `Execute` → 결과 반환. 엔진 측 기능이 필요한 도구는 내부적으로 TCP 브리지를 통해 명령을 발행합니다.

## 3. 브리지 프로토콜(호스트 ↔ 엔진 어댑터)

- 전송: 로컬 TCP. 프레임 형식 = 4바이트 길이 접두사 + JSON 엔벨로프(`BridgeMessage`, `Bridge\BridgeMessage.cs` / `FrameCodec.cs` 참조).
- 디스커버리와 인증: 어댑터는 `%USERPROFILE%\.forgemind\bridge.json`(호스트, 포트, 자격 증명 포함)을 읽고 연결 → 핸드셰이크 → 메시지 루프.
- 명령 의미론: 요청은 명령 이름과 파라미터를 포함합니다. 서버는 명령을 어댑터 구현으로 라우팅하고, 응답은 상태 코드, 메시지, 데이터를 포함합니다. 엔진 측 명령은 엔진 메인 스레드(UE 게임 스레드)에서 실행됩니다.
- 수명 주기: 호스트는 시작과 동시에 브리지를 리슨합니다. 어댑터는 연결 끊김 시 지수 백오프로 재연결합니다.

## 4. 장시간 작업 메커니즘

- 장시간 작업(`launch`, `build` 등)은 비동기로 실행되며 도구 호출을 차단하지 않습니다.
- 관련 내장 도구:
  - `get_task_result`: 작업 결과 조회;
  - `list_executing_tasks`: 진행 중 작업 목록;
  - `cleanup_tasks`: 작업 레코드 정리.
- 내부 `LongTaskManager`가 작업 수명 주기를 관리합니다(`LongTaskManager.cs` / `LongTaskTools.cs`).

## 5. 내장 도구(호스트 포함, 플러그인 불필요)

| 도구 | 설명 |
|---|---|
| `settings` | 서버 설정 읽기/쓰기(`McpSettingsManager`, 변경 즉시 영속화) |
| `get_task_result` | 장시간 작업 결과 조회 |
| `list_executing_tasks` | 진행 중 작업 확인 |
| `cleanup_tasks` | 작업 레코드 정리 |

플러그인 주입 전체 도구 목록은 `tools-reference.md`를 참조.

## 6. 상용 기능(DSL, 개요)

- OpenSource는 **DSL 확장 기반 계약**을 제공합니다(`ParserFactory` / `ITranslator` / `TranslatorRegistry` / `IIrConverter` / `IrConverterRegistry` / `DiagnosticReporter` 등 추상 인터페이스는 공개 사용 가능). 구체적인 DSL 문법 구현, Parser, Translator, IR 규칙, 에셋 생성 로직은 Commercial에 속하며 OpenSource에 포함되지 않습니다.
- DSL 도구는 상용 플러그인이 주입합니다. 구체적인 도구 이름과 API는 상용 버전 문서를 따릅니다. OpenSource 도구 목록에 DSL 도구는 포함되지 않습니다.

| 기능 | 귀속 |
|---|---|
| DSL 확장 인터페이스 | OpenSource ✅ |
| DSL Parser | Commercial 🔒 |
| DSL Translator | Commercial 🔒 |
| IR 규칙 | Commercial 🔒 |
| 에셋 생성 로직 | Commercial 🔒 |

## 7. 호환성 주의

- 브리지 프로토콜 프레임 형식과 도구 Schema는 본 문서를 따릅니다. 파괴적 변경은 사전에 마이그레이션 공지를 게시합니다.
