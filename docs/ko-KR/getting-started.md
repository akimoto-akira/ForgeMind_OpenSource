# ForgeMind 빠른 시작

ForgeMind는 **MCP를 인터페이스로 하는 게임 엔진 제어 플랫폼**입니다: MCP 프로토콜을 통해 Unreal Engine의 프로젝트 관리, 에셋 읽기, 실행/빌드/에디터 조작을 AI 클라이언트에 공개합니다. 현재 구현은 UE이며 플랫폼은 확장 가능합니다. 오픈소스 버전은 엔진 도구 세트와 브리지 기능을 포함하고, 상용 버전은 DSL 에셋 생성 등을 추가합니다(§5 참조).

이 가이드의 모든 절차는 **오픈소스 슬라이스**(`OpenSource\` + `UEProject\` + `tests\`)로 완결할 수 있습니다.

## 1. 환경 요구 사항

- .NET SDK(호스트와 플러그인 빌드용).
- Unreal Engine 5.0–5.8(에디터 포함). 샘플 프로젝트는 `UEProject\ForgeMind_5_0\ForgeMind_5_0\ForgeMind_5_0.uproject`.
- MCP 지원 클라이언트(Claude Desktop / Cursor / 커스텀 클라이언트 모두 가능).

## 2. 서버와 플러그인 빌드

```powershell
# 호스트
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# 플러그인(빌드 후 DLL + 동일 이름 .manifest.json을 호스트의 plugins 디렉터리에 배치)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
```

> `ForgeMind.sln` 전체를 직접 빌드하지 마세요(상용 프로젝트 참조를 포함하며 상용 디렉터리는 슬라이스에 포함되지 않습니다). 플러그인은 배포자가 제공하는 빌드 산출물을 사용해도 됩니다.

## 3. 실행

1. **서버 실행**(기본은 HTTP 상주, 포트 8765):

   ```powershell
   dotnet run --project OpenSource\ForgeMindServer\ForgeMindServer.csproj
   # 또는 빌드 산출물 ForgeMindServer.exe 실행; --port N / --stdio / FORGEMIND_PORT로 조정 가능
   ```

2. **UE 에디터와 샘플 프로젝트 열기**: UE에서 `ForgeMind_5_0.uproject`를 실행하면 에디터 내 `ForgeMindForUE` 플러그인이 브리지 클라이언트를 자동 실행해 호스트에 연결합니다.

3. **클라이언트 설정**: MCP 클라이언트에 서버 추가:

   ```json
   { "mcpServers": { "forgemind": { "url": "http://127.0.0.1:8765/mcp" } } }
   ```

   연결 후 도구 목록(내장 4개 + 플러그인 주입)이 표시됩니다.

## 4. 첫 사용

- `engine` 도구로 현재 엔진 정보 확인;
- `project` / `project_files`로 샘플 프로젝트와 파일 확인;
- `launch`로 에디터 실행, `editor`로 에디터 조작;
- `asset`으로 프로젝트 에셋 구조 읽기;
- `knowledge`로 프로젝트 지식 검색.

> 장시간 작업(launch/build 등)은 비동기로 실행됩니다. `get_task_result`로 결과를 조회하고, `list_executing_tasks`로 진행 중 작업을 확인하며, `cleanup_tasks`로 정리합니다.

## 5. 상용 버전(DSL)

Commercial 모듈은 독립적인 상용 릴리스이며 OpenSource 저장소에 포함되지 않습니다. 상용 버전은 **DSL 에셋 생성**을 제공합니다: 사람이 읽을 수 있는 텍스트로 블루프린트, 머티리얼 등을 작성하고 엔진 에셋으로 변환합니다. 구체적인 DSL 도구, 텍스트 변환 기능, 문법 세부 사항은 상용 버전 문서를 따릅니다. 상용 라이선스 기능도 Commercial에 속합니다. OpenSource에는 DSL 도구가 포함되지 않습니다.

## 6. 다음 단계

- 아키텍처와 메커니즘: `architecture.md`
- 도구 목록과 파라미터: `tools-reference.md`
- 개발과 기여: `development-guide.md` / `contributing.md`
- 자주 묻는 질문: `troubleshooting.md`
