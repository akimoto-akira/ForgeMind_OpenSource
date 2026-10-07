# ForgeMind 문서 (한국어)

ForgeMind — MCP를 인터페이스로 하는 게임 엔진 제어 플랫폼: MCP 프로토콜을 통해 Unreal Engine의 프로젝트 관리, 에셋 읽기, 실행/빌드/에디터 조작을 AI 클라이언트에 공개합니다. 현재 구현은 UE(5.0–5.8)를 대상으로 하며, 플랫폼 설계는 다른 엔진으로 확장할 수 있습니다.

> 본 문서는 **공개 오픈소스 슬라이스**를 대상으로 합니다: `OpenSource\`(호스트와 플러그인), `UEProject\`(샘플 프로젝트), `tests\`(테스트). 상용 기능(DSL 에셋 생성, 상용 라이선스 구현)은 개요만 설명하며 OpenSource 릴리스에는 포함되지 않습니다. OpenSource는 필요한 라이선스 추상화와 개방형 구현을 보유합니다.

## 문서 색인

| 문서 | 내용 |
|---|---|
| [빠른 시작](getting-started.md) | 환경 요구 사항, 빌드, 실행, 첫 사용 |
| [아키텍처](architecture.md) | 호스트 / 플러그인 / 브리지 프로토콜 / 스캔 및 SPK |
| [API 레퍼런스](api-reference.md) | MCP 프로토콜, 브리지 프로토콜, 도구 계약 |
| [도구 레퍼런스](tools-reference.md) | 전체 MCP 도구 및 파라미터 |
| [개발 가이드](development-guide.md) | 빌드, 디버깅, 도구/어댑터 추가, 테스트 |
| [기여 가이드](contributing.md) | 오픈소스 경계, PR 프로세스, 테스트 요구 사항 |
| [문제 해결](troubleshooting.md) | 일반적인 문제와 해결책 |
| [로드맵](roadmap.md) | 계획된 방향성 |
| [프로모](promo.md) | 프로젝트 소개와 대외 커뮤니케이션 규율 |
| [보안](security.md) | 보안 설계와 모범 사례 |

## 오픈소스 범위 개요

```
OpenSource\
  ForgeMindServer               MCP 호스트(내장 도구 4개 + 플러그인 로딩 + TCP 브리지)
  ForgeMind.OfflineEngineKit    엔진/프로젝트/실행/빌드/에디터 도구(플러그인)
  ForgeMind.Assets              .uasset 오프라인 읽기(플러그인)
  ForgeMind.Knowledge           지식 검색(플러그인)
  ForgeMind.Scan.Abstractions   스캔 데이터 및 Provider 추상화
  ForgeMind.Scan                SPK 읽기 및 패키징
  ForgeMind.Scan.Unreal         Unreal Engine 스캔 구현
  ForgeMind.EngineScanner       .spk를 직접 출력하는 독립 스캔 CLI
  ForgeMind.Speedy              SPK 저수준 기반
UEProject\ForgeMind_5_0         샘플 UE 프로젝트(5.0–5.8)
tests\                          공개 단위 테스트 + 서버 통합 테스트
```

## 라이선스

OpenSource 릴리스 내용은 Apache-2.0입니다. Commercial 모듈은 OpenSource 릴리스에 포함되지 않으며 별도의 상용 라이선스로 제공됩니다.
