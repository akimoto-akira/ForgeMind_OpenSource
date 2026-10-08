# ForgeMind 문제 해결

일반적인 문제와 해결책(공개 슬라이스의 실제 메커니즘 기반).

## 1. 빌드 실패

**`ForgeMind.sln` 직접 빌드 시 'Commercial\... 프로젝트를 찾을 수 없음'**
원인: `ForgeMind.sln`은 상용 프로젝트 참조 항목(`Commercial\ForgeMindLicenseGenerator`, `Commercial\ForgeMindLicenseServer`)을 포함하지만 상용 디렉터리는 오픈소스 슬라이스에 포함되지 않습니다.
해결: 각 프로젝트를 개별 빌드(`contributing.md` §3). sln 전체를 빌드하지 마세요. 또는 배포자의 빌드 스크립트를 사용하세요.

**플러그인을 빌드해도 호스트에 도구가 안 보임**
- 플러그인 DLL과 동일 이름 `.manifest.json`이 호스트 `plugins\<플러그인 이름>\`(또는 `FORGEMIND_PLUGINS_DIR` 지정 디렉터리)에 배치되었는지 확인;
- 호스트 로그에서 플러그인 스캔이 해당 패키지를 건너뛰지 않았는지 확인(`PluginLoader`는 실패한 단일 패키지만 건너뛰고 호스트에 영향 없음);
- 호스트 재시작(도구 테이블은 첫 접근 시 지연 로딩).

## 2. 서버 시작 문제

**포트 점유**
`http://127.0.0.1:8765`가 점유되면 `HttpListenerException`이 보고됩니다.
해결: `--port N` 또는 환경 변수 `FORGEMIND_PORT`로 포트 변경. 클라이언트 주소도 업데이트.

**클라이언트가 연결 못 함 / tools/list가 비어 있음**
- 서버가 리슨 중인지 확인(로그 `Listening on http://127.0.0.1:<port>/mcp`);
- 클라이언트 URL이 `http://127.0.0.1:8765/mcp`(HTTP 모드)인지 확인;
- 플러그인이 로드되었는지 확인(위 참조).

## 3. 엔진 / 브리지 연결 문제

**에디터는 열렸지만 '엔진 미연결 / 브리지 사용 불가'**
- 프로젝트에서 `ForgeMindForUE` 플러그인이 활성화되었는지 확인(`UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\`);
- 브리지 어댑터는 `%USERPROFILE%\.forgemind\bridge.json`을 읽어 디스커버리와 인증 수행. 파일 누락 / 자격 증명 불일치 시 연결 실패;
- UE 에디터 로그 `LogForgeMindBridge` 확인. 연결 끊김 시 지수 백오프로 재연결하므로 재연결을 기다림.

**`launch` / `build`가 멈춤**
장시간 작업은 비동기 실행. `get_task_result`로 결과 조회, `list_executing_tasks`로 진행 중 작업 확인, `cleanup_tasks`로 레코드 정리 가능.

## 4. 테스트 문제

**`ForgeMindServerTest` 프리빌드 실패**
`--NoBuild`는 프리빌드를 건너뜀(CI가 새 산출물을 보장할 때 사용). 기본 흐름은 호스트를 빌드하고 플러그인을 `<진입>\plugins\`에 스테이징하며 실패 시 케이스를 실행하지 않고 직접 종료.

**테스트가 fail-fast로 중단**
통합 테스트는 Group / Order로 실행되며 fail-fast: 첫 실패에서 중단(같은 Group 내 필수 케이스는 실행됨). 수정 후 `ForgeMindServerTest.exe [group]`으로 해당 그룹 재실행.

**테스트가 에디터 / 샘플 프로젝트에 의존**
`Task\UE5_0\` 아래 케이스는 UE 에디터와 세션이 필요. 순수 데이터 처리 케이스(SPK / 스캔)는 `ForgeMind.PublicTests`에서 실행, 에디터 비의존.

## 5. 데이터 / 에셋 문제

**`asset` 읽기 실패**
`.uasset` 오프라인 읽기는 엔진 버전과 일치해야 함(`UAssetVersions`). 버전 불일치 시 파싱이 실패할 수 있습니다. 엔진 버전이 도구 지원 범위 내인지 확인.

**`.spk`를 읽을 수 없음**
`ForgeMind.EngineScanner <spkPath>`로 생성되고 손상되지 않았는지 확인. SPK는 CRC 검증(`Speedy\Internal\Crc32`)을 포함하며 손상 파일은 로딩을 거부.

## 6. 기타

**로그 활성화 방법**
서버 로그는 `LogManager`가 출력(카테고리: `ForgeMindServerEntry`, `ForgeMindServerHost`, `ForgeMindServerBridgeServer` 등). 브리지 측은 UE 에디터 출력 로그 확인.

**상용 기능 관련 문제**
DSL 문법, 라이선스, License 문제는 배포자에게 문의. OpenSource 버전은 라이선스 파일을 파싱하지 않으며 기능은 고정되고 개방됨(`security.md` 참조).
