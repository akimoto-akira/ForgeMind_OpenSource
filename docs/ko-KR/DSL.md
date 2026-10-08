# ForgeMind DSL (Domain-Specific Language)

> 이 문서는 ForgeMind DSL이 무엇인지, 어떻게 작동하는지, 그리고 전체 시스템에서의 역할에 대해 설명합니다. ForgeMind DSL을 이해하거나 사용하려는 개발자를 대상으로 합니다.

---

## 1. DSL이란

**DSL(Domain-Specific Language)**은 ForgeMind에서 AI가 게임 엔진 에셋을 일괄 생성/수정하기 위한 **선언형 코드 채널**입니다.

AI가 C#과 유사한 구문 스타일로 작성 가능한 텍스트를 사용하여 생성 또는 수정할 엔진 에셋(블루프린트, 머티리얼, 비헤이비어 트리 등)을 설명한 다음, 트랜슬레이터를 통해 구조화된 IR(Intermediate Representation)로 변환하여 엔진 편집기로 전송하여 적용합니다.

### 1.1 핵심 모델

ForgeMind DSL을 이해하는 핵심은 다음 파이프라인입니다:

```
AI
  ↓  DSL 문자열 출력
DSL
  ↓  트랜슬레이터 번역
IR (중간 표현)
  ↓  Bridge 전송
Companion (엔진 어댑터)
  ↓  적용
엔진 에셋
```

이 파이프라인은 두 단계로 나뉩니다:

- **번역 단계**(MCP 서버 측): DSL 텍스트가 파서에 의해 IR JSON으로 번역됩니다. 트랜슬레이터는 구문 분석만 수행하며, 컴파일, 외부 참조 해결, 디스크 액세스를 수행하지 않습니다. 번역 오류가 발생하면 전체 배치가 종료되며 IR은 엔진으로 전송되지 않습니다.
- **적용 단계**(엔진 측): Companion이 IR JSON을 수신하고 에셋별로 생성/업데이트/삭제 작업을 적용합니다. 하나의 에셋 실패가 같은 배치의 다른 에셋에 영향을 미치지 않습니다.

### 1.2 DSL과 MCP Action의 관계

ForgeMind의 AI 제어 기능은 두 가지 도구 유형에서 제공됩니다:

| 차원 | MCP Action | DSP |
|------|-----------|-----|
| 세분성 | 단일 엔터티 정밀 제어 | 일괄 선언적 생성/수정 |
| 예시 | 변수 추가, 와이어 연결 | 블루프린트 에셋 전체를 생성하는 텍스트 블록 |
| 채널 | Bridge를 통해 엔진으로 직접 전송 | 먼저 IR로 번역한 후 Bridge를 통해 전송 |
| 역할 | 세부 조정 | 전체 아키텍처 구축 |

둘은 **보완적**인 관계입니다 — DSL은 "처음부터" 일괄 구성을 담당하고, Action은 "더 나은 상태로" 정밀 조정을 담당합니다.

---

## 2. DSL의 시스템 내 위치

```
┌──────────────┐   MCP   ┌──────────────────────────┐
│  AI 클라이언트   │ ◄────► │  ForgeMindServer (호스트)   │
└──────────────┘         │  • DSL 문자열 수신           │
                         │  • 트랜슬레이터를 호출하여    │
                         │    IR 생성               │
                         │  • Bridge를 통해 IR 전송   │
                         └────────────┬─────────────┘
                                      │ 로컬 TCP 브리지
                         ┌────────────▼─────────────┐
                         │  Companion (엔진 어댑터)   │
                         │  • IR 수신              │
                         │  • 에셋별 적용           │
                         │  • 컴파일 / 저장          │
                         └──────────────────────────┘
```

ForgeMind에서 DSL의 위치:
- AI가 DSL 선언 텍스트를 출력
- MCP `dsl` 도구가 텍스트를 수신하고 트랜슬레이터를 호출하여 IR JSON 생성
- IR JSON이 TCP Bridge를 통해 라이브 엔진 편집기로 전송
- Companion(엔진 어댑터)이 편집기 내에서 적용하여 에셋 생성 완료

---

## 3. DSL 기본 구문 스타일

DSL은 C#의 구문 형식을 빌려오지만 **C# 컴파일러를 거치지 않습니다**. C#의 작성 관례를 모방하지만, 컴파일러 내부 의미 분석(타입 체크, 오버로드 해결 등)은 트랜슬레이터 자체에서 처리됩니다.

### 3.1 선언 블록 구조

각 DSL 선언 블록은 `[Attribute]`를 가진 최상위 클래스(또는 struct/enum) 선언입니다:

```csharp
[DomainAttr(Path = "/Game/Path/AssetName", Mode = Create)]
[AdditionalAttributes]
public class AssetName
{
    // 멤버 선언: 변수, 컴포넌트, 함수 등
}
```

### 3.2 Create / Update / Delete 모든

모든 도메인은 3가지 작업 모드를 통일적으로 지원합니다:

| 모드 값 | 의미 | 페이로드 |
|---------|------|----------|
| `Create` | 새 에셋 생성 | `definition` (완전한 에셋 정의) |
| `Update` | 기존 에셋 수정 | `patches` (멤버별 대상 상태 배열) |
| `Delete` | 에셋 전체 삭제 | 비어있는 `patches` |

**Create 예시:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Chest
{
    [Variable] float Health = 100f;
}
```

**Update 예시:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Update)]
public class BP_Chest
{
    [Variable] float MaxHealth = 200f;     // 에셋에 없음 → 추가
    [Variable(Category = "Stats")]
    float Health = 90f;                    // 에셋에 있음 → 교체
    [Delete]                              // 삭제 표시
    float LegacyVariable;                  // → 기존 멤버 삭제
}
```

실제 작업 유형(추가/교체/삭제)은 Companion이 대상 에셋에 멤버가 존재하는지, 그리고 `[Delete]` 주석이 있는지를 기반으로 추론합니다 — DSL 선언이 아닙니다.

**Delete 예시:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_OBSOLETE", Delete = true)]
public class BP_OBSOLETE { }
```

### 3.3 다른 에셋 참조 — using 별칭

DSL은 에셋 간 참조를 단순화하기 위해 using 별칭 구문을 지원합니다:

```csharp
// 파일 수준 using (모든 블록에 적용)
using WoodTex = "/Game/Textures/T_Wood.uasset";

// 블록 수준 using (해당 블록에만 적용)
[Blueprint(Path = "/Game/Chest/BP_GoldenChest", Mode = Create)]
public class BP_GoldenChest
{
    using GoldMat = "/Game/Materials/M_Gold.uasset";
    [Variable] MaterialInstance Mat = MaterialInstance(GoldMat);
}
```

이것은 DSL 구칙 규칙입니다 — `using`은 식별자를 에셋 경로 문자열에 매핑하여 선언에서 참조하기 쉽게 합니다.

### 3.4 블록 간 형식 참조

명시적 `using` 외에도 DSL은 암시적 블록 간 형식 참조를 지원합니다:

최상위 선언이 `[Blueprint(Path="/Game/AI/BP_Hunter")] public class BP_Hunter { ... }`인 경우, 후속 블록에서 `BP_Hunter` 식별자를 직접 사용할 수 있으며, 자동으로 에셋 경로 `"/Game/AI/BP_Hunter"`로 확인됩니다.

---

## 4. IR (중간 표현)

IR은 트랜슬레이터와 Companion 사이의 번역 결과물 — JSON 형식의 구조화된 에셋 설명입니다. DSL과 엔진 사이의 "브릿지 언어"입니다.

### 4.1 배치 IR vs 단일 에셋 IR

**배치 IR** (다중 에셋 배치):
```json
{
  "version": "5.6",
  "batch": true,
  "assets": [
    { "assetType": "blueprint", "path": "/Game/BP_A", "mode": "create", ... },
    { "assetType": "material",  "path": "/Game/M_Wood", "mode": "create", ... }
  ]
}
```

**단일 에셋 IR:**
```json
{
  "version": "5.6",
  "assetType": "blueprint",
  "path": "/Game/Blueprints/BP_Chest",
  "mode": "create",
  "compile": true,
  "save": false,
  "definition": { ... }
}
```

### 4.2 단일 에셋 IR 필드 참조

| 필드 | 유형 | 설명 |
|-----|------|------|
| `version` | string | 엔진 버전(예: "5.6"), 엔진 주 버전과 정확히 일치해야 함 |
| `assetType` | string | 도메인 유형 (블록의 Attribute에 의해 결정됨) |
| `path` | string | 에셋 경로 (필수) |
| `mode` | string | `create` / `update` / `delete` |
| `compile` | bool | 적용 후 컴파일 여부 (기본값 true) |
| `save` | bool | 적용 후 디스크에 저장 여부 |
| `patches` | array | update 모드 필수 (멤버별 대상 상태, op 필드 없음) |
| `definition` | object | create 모드 필수 (완전한 에셋 정의) |

> **모드/페이로드 제약**: `mode=create`는 `definition`을 포함해야 하며 `patches`를 포함할 수 없습니다. `mode=update`는 `patches`를 포함해야 하며 `definition`을 포함할 수 없습니다. `mode=delete`는 비어있는 `patches`를 포함합니다. 불일치는 잘못된 페이로드입니다.

### 4.3 양방향 대칭성

ForgeMind의 IR은 "쓰기 방향"(apply_asset_ir)과 "읽기 방향"(get_asset_ir) 모두에서 **동일한 페이로드 구조**를 사용합니다:

| 방향 | 명령 | `mode` 값 |
|------|------|----------|
| 쓰기 | `apply_asset_ir` | `create` / `update` / `delete` |
| 읽기 | `get_asset_ir` | `read` |

이를 통해 AI는 스키마 변환 없이 동일한 IR을 "읽기 → 모드 수정 → 쓰기 되돌리기"할 수 있습니다.

---

## 5. 도메인 인식 및 라우팅

### 5.1 속성이 도메인 결정

DSL 문자열의 각 선언 블록은 `[Attribute]` 마커를 통해 자동으로 에셋 유형으로 라우팅됩니다:

```csharp
[Blueprint(Path = "...")]          // → 블루프린트 트랜슬레이터 → assetType = "blueprint"
[Material(Path = "...")]           // → 머티리얼 트랜슬레이터 → assetType = "material"
[BehaviorTree(Path = "...")]       // → 비헤이비어 트리 트랜슬레이터 → assetType = "behavior_tree"
```

**속성 → assetType 매핑 테이블:**

| 속성 | 에셋 유형 |
|-----|----------|
| `[Blueprint]` | `blueprint` |
| `[WidgetBlueprint]` | `widget_blueprint` |
| `[Material]` | `material` / `material_instance_constant` |
| `[MaterialFunction]` | `material_function` |
| `[BehaviorTree]` | `behavior_tree` |
| `[Blackboard]` | `blackboard` |
| `[AnimBlueprint]` | `anim_blueprint` |
| `[Enum]` | `user_defined_enum` |
| `[Struct]` | `user_defined_struct` |
| `[InputAction]` | `input_action` |
| `[InputMappingContext]` | `input_mapping_context` |
| `[NiagaraEmitter]` | `niagara_emitter` |
| `[NiagaraSystem]` | `niagara_system` |
| `[MetaSoundSource]` | `meta_sound_source` |
| `[ChaosSolver]` | `chaos_solver_actor` |
| `[GroomBinding]` | `groom_binding_asset` |
| `[Landscape]` | `landscape` |
| `[FoliageType]` | `foliage_type` |
| `[PhysicsAsset]` | `physics_asset` |
| `[PaperFlipbook]` | `paper_flipbook` |
| `[LevelSequence]` | `level_sequence` |
| … | … |

> 위는 현재 알려진 모든 도메인의 매핑입니다. 새 도메인을 추가하려면 변환 측에서 새 속성 → 트랜슬레이터 매핑을 등록하면 됩니다 — 호스트 프레임워크는 변경할 필요가 없습니다.

### 5.2 라우팅 규칙

- 파서는 각 선언 블록에 대해 정확한 이름 일치를 수행합니다(토큰 수준, 하위 문자열 매칭 아님)
- 여러 속성이 있을 경우 매핑 테이블에서 첫 번째로 일치하는 것이 우선합니다(`[WidgetBlueprint]`이 `[Blueprint]`보다 우선)
- **인식할 수 없는 속성은 자동으로 대체되지 않습니다** — 오류를 보고하고 전체 배치를 종료합니다

### 5.3 하나의 DSL에서 여러 도메인 혼합

단일 DSL 문자열에 **임의의 수, 임의의 도메인** 선언 블록을 포함할 수 있습니다:

```csharp
using WoodTex = "/Game/Textures/T_Wood.uasset";

// ── 블록 1: 블루프린트 도메인 ──
[Blueprint(Path = "/Game/AI/BP_Hunter", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Hunter
{
    [Variable(Category = "Stats")]
    float Health = 100f;
}

// ── 블록 2: 비헤이비어 트리 도메인 ──
[BehaviorTree(Path = "/Game/AI/BT_Hunter", Mode = Create)]
public class BT_Hunter { }

// ── 블록 3: 머티리얼 도메인 ──
[Material(Path = "/Game/Materials/M_Wood", Mode = Create)]
public function M_Wood
{
    BaseColor = Texture2D(WoodTex);
    Roughness = 0.7f;
}
```

동일한 DSL의 모든 선언 블록은 독립적으로 번역되며, 각각 자체 IR을 생성하여 하나의 배치 IR로 조립되어 Bridge를 통해 엔진으로 함께 전송됩니다.

---

## 6. Bridge와 Companion

### 6.1 Bridge 전송

IR은 로컬 TCP 브릿지를 통해 엔진 편집기로 전송됩니다:
- 브릿지 계층은 IR을 엔진 쪽으로 안정적으로 전달할 책임이 있습니다
- Bridge는 ForgeMind의 로컬 프로세스 간 통신 인프라입니다. 호스트 측이 브릿지 서버를 유지하고 엔진 어댑터 측이 브릿지 클라이언트를 유지합니다

### 6.2 Companion의 역할

Companion은 엔진 내부의 어댑터 플러그인입니다(예: ForgeMindForUE). 그 역할은 다음과 같습니다:

1. **IR JSON 수신** → `assetType` 기반으로 엔진 도메인 핸들러로 디스패치
2. **Create**: `definition`에서 완전한 에셋 구축 → 에셋 레지스트리에 등록 → 선택적으로 컴파일 → 선택적으로 디스크에 저장
3. **Update**: `patches`에서 멤버 수준 수정 적용 (add / replace / delete를 Companion이 추론)
4. **Delete**: 에셋 패키지 언로드 → 에셋 레지스트리에서 제거 → 디스크 파일 삭제
5. **결과 보고**: 에셋별로 성공/오류 반환; 쓰기 방향은 `patchResults[].actualOp`를 반환합니다

> 하나의 에셋 실패가 같은 배치의 다른 에셋에 영향을 미치지 않습니다(트랜잭션 롤백 없음). 이것은 ForgeMind의 의도적인 설계 선택입니다 — 편집기 에셋의 상태 공간이 크고, 트랜잭션 비용이 높으며, 부분적 성공이 전부 아니면 전무 방식보다 우선시됩니다.

---

## 7. 진단 및 오류 처리

DSL 오류 처리는 두 단계로 나뉩니다:

| 단계 | 오류 유형 | 처리 |
|------|----------|------|
| 번역 단계(MCP 측) | 구문 오류, 인식할 수 없는 속성, 필수 매개변수 누락 등 | 모든 오류 → 전체 배치 종료, IR은 Bridge로 전송되지 않음 |
| 적용 단계(Companion 측) | 에셋을 찾을 수 없음, 유형 불일치, 컴파일 실패 등 | 단일 에셋 실패가 다른 에셋에 영향을 미치지 않음 |

번역 단계 동안의 경고(Warn)는 배치를 종료하지 않지만 AI에게 반환되는 응답 텍스트에 첨부됩니다.

### 7.1 일반적인 번역 단계 오류 코드

일부 오류 코드 예시(세부 사항은 도메인 문서 참조):

| 오류 코드 | 의미 |
|----------|------|
| `FM1004` | 잘못된 모드 값 |
| `FM1005` | 필수 경로 매개변수가 없습니다 |
| `FM1002` | 도메인 번역 실패(제네릭 폴백 코드) |

### 7.2 주요 에셋 수준 오류 코드(적용 단계)

| 오류 코드 | 의미 |
|----------|------|
| `ASSET_NOT_FOUND` | 에셋 경로가 존재하지 않습니다(업데이트/삭제 시나리오) |
| `ASSET_ALREADY_EXISTS` | 경로가 이미 존재합니다(만들기 시나리오, 기본적으로 덮어쓰지 않음) |
| `ASSET_TYPE_MISMATCH` | 유형 불일치 |
| `ASSET_LOCKED` | 에셋이 편집기에서 잠겨 있습니다 |

---

## 8. DSL 도메인 확장

### 8.1 도메인의 구성 요소

완전한 DSL 도메인은 다음 부분으로 구성됩니다:

1. **속성 마커** — 예: `[Blueprint]`, `[Material]`, 라우팅 인식에 사용
2. **도메인 트랜슬레이터 구현** — 해당 도메인의 DSL 선언을 IR JSON으로 변환할 역할을 담당합니다
3. **DSL 구문 문서** — 도메인의 선언 규칙 및 속성 매개변수를 정의합니다
4. **IR 스키마 규칙** — 도메인의 `definition` / `patches` JSON 구조를 정의합니다
5. **Companion 도메인 핸들러** — 엔진 측의 해당 구현으로, IR에서 에셋을 편집기에 적용합니다

### 8.2 새 도메인을 추가하는 기본 단계

1. 도메인의 루트 속성 이름 정의(예: `[NewDomain]`)
2. 도메인 트랜슬레이터 구현(트랜슬레이터 기본 클래스 상속, 구문 트리 방문자 메서드 재정의하여 도메인 의미 추출, IR JSON 출력)
3. 프레임워크에 등록하여 파서가 `assetType`을 통해 트랜슬레이터를 찾을 수 있도록 함
4. DSL 구문 문서 및 IR 스키약 규칙 작성
5. 엔진 Companion 측에서 해당 도메인 핸들러 구현

새 도메인 추가에는 **호스트 코드 변경이 필요하지 않습니다** — 프레임워크는 등록 메커니즘을 통해 새 도메인 트랜슬레이터를 발견합니다.

### 8.3 트랜슬레이터 구현 원칙

트랜슬레이터는 다음 동작 규칙을 따릅니다. DSL 도메인을 확장할 때 준수해야 합니다:

- **순수 함수**: 트랜텔레이터는 DSL 문자열을 입력으로 사용하고 IR JSON을 출력으로 내보냅니다. 부작용이나 잔여 상태가 없습니다. 동일한 DSL은 항상 동일한 IR을 생성합니다.
- **구문 분석만**: 트랜텔레이터는 C# 컴파일을 실행하지 않고, 디스크에 액세스하지 않으며, 외부 에셋을 확인하지 않습니다. DSL의 구문 형태를 IR로 변환만 수행합니다. 의미 검증은 엔진 측의 책임입니다.
- **오류 복원**: 번역을 중단시키지 않고 비표준 DSL 구칙(예: using 별칭에 문자열 할당)을 허용할 수 있습니다.
- **알 수 없는 구문 저하**: 정확하게 변환할 수 없는 구문을 만나면 오류 대신 경고를 보고하고 엔진 적용 단계에 맡깁니다.

---

## 9. 오픈 소스 및 상업적 경계

ForgeMind의 DSL 기능은 두 부분으로 나뉩니다:

### 9.1 오픈 소스(공개 코어)

오픈 소스 코어는 DSL 호스트 프레임워크를 제공합니다:

- **DSL 프레임워크 계층**: DSL 문자열 구문 분석(최상위 선언 블록별 분할, 속성 인식 및 트린슬레이터로 라우팅), 트랜슬레이터 인터페이스 및 등록 메커니즘, 진단 보고 및 기타 인프라를 담당합니다.
- **플러그인 계약 및 등록 메커니즘**: 새 도메인 트랜슬레이터는 등록을 통해 발견됩니다. 호스트 프레임워크에는 도메인별 분기 로직이 필요하지 않습니다.
- **오픈 소스는 도메인 트랜슬레이터를 포함하지 않습니다**: 특정 도메인 변환 로직(블루프린트, 머티리얼, 비헤이비어 트리 등)은 오픈 소스 코어의 일부가 아닙니다.

### 9.2 상업적(유료 확장)

상업적 확장 모듈은 오픈 소스 프레임워크 위에 구축되어 다음을 제공합니다:

- 모든 특정 도메인 트랜슬레이터 구현(블루프린트, 머티리얼, 비헤이비어 트리, 애니메이션 블루프린트, 니아가라, 메타사운드, 카오스, 그룸, 랜드스케이프, 폴리지, 물리, Paper2D, 시퀀서, …)
- DSL 변환 및 파견 실행 포인트 역할을 하는 MCP `dsl` 도구 진입점
- IR이 엔진으로 전송되기 전 도메인 특정 처리를 수행하는 도메인별 IR 변환기

> 오픈 소스 코어는 프레임워크와 확장 메커니즘을 정의합니다. 특정 도메인 기능(블루프린트, 머티리얼, 비헤이비어 트리 등에 대한 DSL 구문 및 트랜슬레이터)은 상업적 확장 모듈에 의해 제공됩니다. 오픈 소스 사용자는 프레임워크를 기반으로 사용자 정의 도메인 트랜슬레이터를 등록하여 자체 DSL 기능을 확장할 수 있습니다.

---

## 10. 동작 제약 및 설계 원칙

| 태그 | DSL 동작 | 의미 |
|-----|---------|------|
| 구문 스타일 | C#에서 영감을 받음 | C#은 AI 훈련 데이터가 가장 많은 언어로, 인지적 부담을 줄입니다 |
| 노드 좌표 | DSL에서 표현하지 않습니다 | "무엇이 존재하고 어떻게 연결되어 있는지" 토폴로지 의미에만 집중합니다. 레이아웃은 엔진이 자동 생성합니다 |
| 필수 경로 | 예 | 명명 규칙 및 모듈 소속은 AI가 명시적으로 결정합니다 |
| 버전 일치 | 정확한 주 버전 | IR `version`은 엔진의 주 버전과 일치해야 합니다 |
| 트랜잭션 정책 | 롤백 없음 | 배치 내 일부 에셋 실패가 다른 에셋에 영향을 미치지 않습니다 |
| 시험 실행 | 존재하지 않음 | IR은 엔진으로 파견되며 호출자에게는 반환되지 않습니다 |
| 인식되지 않는 속성 | 배치를 종료합니다 | 자동으로 대체되면 AI가 자동으로 잘못된 에셋 유형을 만들게 됩니다 |
| 업데이트 패치 | 대상 상태를 표현합니다 | 패치에 `op` 필드가 없습니다; 작업은 Companion이 멤버 존재 여부에 따라 추론합니다 |

---

## 11. 빠른 참조

### DSL 선언 블록 템플릿

```csharp
using SomeAsset = "/Game/Path/SomeAsset.uasset";

[DomainAttr(Path = "/Game/Path/YourAsset", Mode = Create)]
[AdditionalAttribute]
public class YourAsset
{
    using BlockAlias = "/Game/Path/AnotherAsset.uasset";
    [Variable] float YourVariable = 1f;
    [Function(Pure = true)]
    bool YourFunction() => YourVariable > 0;
}
```

### 싱글 에셋 IR 템플릿

```json
{
  "version": "<major.minor>",
  "assetType": "<snake_case_type>",
  "path": "/Game/Path/Asset",
  "mode": "create",
  "compile": true,
  "save": false,
  "definition": { }
}
```

### 파이프라인 개요

```
AI 클라이언트
  → DSL(하나 이상의 도메인 선언 블록을 포함한 텍스트)이 포함됨
  → MCP dsl 도구(번역 단계)
    → 파서가 속성을 기반으로 도메인 트랜슬레이터로 라우팅
    → 트랜슬레이터가 IR JSON 생성
  → 배치 IR({ version, batch, assets })
  → Bridge(로컬 TCP)
  → Companion(적용 단계)
    → 에셋별로 생성/업데이트/삭제/컴파일/저장 수행
  → 엔진 에셋
```
