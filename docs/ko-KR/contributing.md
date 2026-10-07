# ForgeMind 기여 가이드

ForgeMind에 관심을 가져주셔서 감사합니다. 공개 저장소는 ForgeMind의 OpenSource 릴리스 슬라이스에 해당하며 공개된 오픈소스 프로젝트, 테스트, 샘플 프로젝트만 포함합니다. 상용 모듈(DSL 도메인 번역기, 라이선스 시스템 등)은 공개 저장소에 없습니다. 기여 전에 범위 구분을 이해하세요.

## 1. 공개 저장소 구조

오픈소스 슬라이스(clone한 저장소)의 최상위 구조:

```
ForgeMind.sln                          # 솔루션(상용 프로젝트 참조 포함, 아래 참조)
OpenSource\                            # 오픈소스 프로젝트
  ForgeMindServer\                     # MCP 서버(호스트)
  ForgeMind.OfflineEngineKit\          # 엔진 오프라인 도구 플러그인(engine/project/launch/build/editor 등)
  ForgeMind.Assets\                    # 에셋 읽기 플러그인(asset)
  ForgeMind.Knowledge\                 # 지식 검색 플러그인(knowledge)
  ForgeMind.EngineScanner\             # 엔진 스캐너(CLI)
  ForgeMind.Scan\                      # 스캔 패키징
  ForgeMind.Scan.Abstractions\         # 스캔 추상
  ForgeMind.Scan.Unreal\               # UE 스캔 구현(DocumentEngine)
  ForgeMind.Speedy\                    # SPK 패키징 형식
UEProject\
  ForgeMind_5_0\                       # 샘플 UE 프로젝트(5.0–5.8 호환)
tests\
  ForgeMind.PublicTests\               # 공개 단위 테스트(SPK / 스캔 데이터)
  ForgeMindServerTest\                 # 서버 통합 테스트(UE 에디터 필수)
```

> 주의: 현재 개발 저장소의 솔루션 파일(`ForgeMind.sln`)은 Commercial 프로젝트 참조(`Commercial\ForgeMindLicenseGenerator`, `Commercial\ForgeMindLicenseServer` 등)를 포함합니다. 공개 배포**에는 `Commercial/` 디렉터리가 포함되지 않으므로** OpenSource 사용자는 공개 프로젝트를 개별 빌드하고(§3 참조), `dotnet build ForgeMind.sln`을 직접 실행하지 마세요.

## 2. 오픈소스 경계

- **OpenSource\**: 전부 오픈소스(Apache-2.0). 기여의 주요 영역.
- **UEProject\**: 샘플 프로젝트는 오픈소스지만 슬라이스 내보내기 시 `*.uasset` 바이너리 에셋을 제외(Content 아래는 맵 등 텍스트 에셋과 디렉터리 골격만 유지).
- **tests\**: 오픈소스. 기여에는 테스트 첨부.
- **Commercial은 공개 저장소에 없음**: DSL 도메인 번역기, 상용 라이선스 등은 슬라이스에 등장하지 않음. 오픈소스 코드는 상용 모듈 타입 참조 금지.
- 내보내기는 `bin` / `obj` / `Binaries` / `Intermediate` / `Saved` / `DerivedDataCache` 등 빌드 산출물과 임시 디렉터리를 자동 제외. 파일 헤더 라이선스 식별자는 내보내기 시 Apache-2.0으로 통일 치환.

## 3. 빌드

```powershell
# 개별 빌드(sln 전체 직접 빌드 금지, 위 참조)
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
dotnet build OpenSource\ForgeMind.Scan.Abstractions\ForgeMind.Scan.Abstractions.csproj
dotnet build OpenSource\ForgeMind.Scan\ForgeMind.Scan.csproj
dotnet build OpenSource\ForgeMind.Scan.Unreal\ForgeMind.Scan.Unreal.csproj
dotnet build OpenSource\ForgeMind.Speedy\ForgeMind.Speedy.csproj
# 플러그인 프로젝트(빌드 후 출력 + 동일 이름 .manifest.json을 호스트 plugins 디렉터리에 배치)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
dotnet build OpenSource\ForgeMind.EngineScanner\ForgeMind.EngineScanner.csproj
```

호스트는 `<호스트 디렉터리>\plugins\`에서 플러그인을 로드(환경 변수 `FORGEMIND_PLUGINS_DIR`로 덮어씀). 플러그인 패키지 = 메인 DLL + 동일 이름 `.manifest.json` + 의존성(`development-guide.md` §3 참조).

## 4. 코드 규약

- 모든 소스 파일 상단에 SPDX 라이선스 식별자와 저작권 표시 유지(현재 Apache-2.0).
- 네임스페이스는 모듈 단위로 정리(`ForgeMindServer.*` / `ForgeMind.*`). 공개 계약은 `ITool` / `IPlugin` 등 추상 인터페이스 경유.
- 서버 도구 클래스는 완전한 `Description`과 파라미터 Schema 제공 필수(이 텍스트는 AI 클라이언트에 직접 표시됨).
- 엔진 어댑터 코드는 해당 엔진 코딩 규약을 따름(현재 UE 측: F/U/T 타입 접두사, `TEXT()` 매크로, 게임 스레드 안전 제약).
- 새 코드는 상용 모듈을 참조·의존해서는 안 됨.

## 5. PR 프로세스

1. `main`에서 브랜치 생성. 이름 제안: `feat/<설명>` 또는 `fix/<설명>`.
2. 커밋 메시지: `<type>: <한 줄 설명>`.
3. 커밋 전 자가 점검:
   - §3에 따라 개별 빌드 모두 성공;
   - 브리지 명령 / 도구 동작 변경 시 관련 `ForgeMindServerTest` Group 성공(실행 방법은 `development-guide.md` §6);
   - SPK / 스캔 데이터 변경 시 `ForgeMind.PublicTests` 성공;
   - 상용 모듈 참조를 도입하지 않음;
   - `docs\zh-CN\`의 해당 문서를 동기 업데이트.
4. PR을 열 때 작성: 변경 내용, 테스트 결과 요약, 특정 모듈의 메인테이너 리뷰 필요 여부.
5. 메인테이너 리뷰 중점: 오픈소스 경계, API 호환성, 테스트 커버리지.

## 6. 테스트 요구 사항

- 브리지 명령 / 도구 동작 / 어댑터 로직 변경에는 테스트 케이스 첨부 필수(`tests\ForgeMindServerTest\Task\` 아래 `*_Tests.cs` 추가).
- 서버 통합 테스트는 UE 에디터 환경 필요. 순수 데이터 처리 테스트(SPK, 스캔)는 `tests\ForgeMind.PublicTests\`에 배치, 엔진 비의존.
- 케이스는 기본 순서에서 안정적으로 성공해야 함(fail-fast, 첫 실패에서 중단).

## 7. 문서 기여

- 중국어 문서는 `docs\zh-CN\`에 있습니다. 내용은 소스 코드 사실과 일치해야 하며 불확실한 항목은 '확인 필요'로 표시.
- 대외 도구 목록 변경 시 `tools-reference.md` 동기 업데이트.
- 상용 기능(DSL 문법, 라이선스 메커니즘)은 문서에서 개요만 설명하고 내부 세부 사항을 펼치지 않음.

## 8. 행동 규범

- 기존 설계 결정(브리지 프로토콜, 도구 계약, SPK 형식)을 존중. 큰 변경은 먼저 논의 시작.
- 대외적으로 동종 제품을 폄하하지 않음. 홍보 내용은 `promo.md`의 대외 규율을 따름.
