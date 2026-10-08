# ForgeMind DSL (Domain-Specific Language)

> Ten dokument wyjaśnia, czym jest ForgeMind DSL, jak działa i jaką rolę odgrywa w ogólnym systemie. Jest przeznaczony dla programistów, którzy chcą zrozumieć lub używać ForgeMind DSL.

---

## 1. Czym jest DSL

**DSL (Domain-Specific Language)** to **deklaratywny kanał kodu** w ForgeMind umożliwiający AI masowe tworzenie lub modyfikowanie zasobów silnika gry.

Pozwala AI używać czytelnego tekstu napisanego w stylu składni C# do opisywania zasobów do utworzenia lub zmodyfikowania (takich jak Blueprints, Materiały, Drzewa Zachowań itp.), a następnie tłumaczyć je na ustrukturyzowane IR (Intermediate Representation) za pomocą tłumaczy i wysyłać do edytora silnika w celu zastosowania.

### 1.1 Model Żródłowy

Kluczem do zrozumienia ForgeMind DSL jest następujący pipeline:

```
AI
  ↓  wyprowadza ciąg znaków DSL
DSL
  ↓  tłumaczone przez tłumacza
IR (Reprezentacja Pośrednia)
  ↓  przesyłane przez Bridge
Adapter silnika (Companion)
  ↓  stosowane
Zasób silnika
```

Ten pipeline dzieli się na dwie fazy:

- **Faza Tłumaczenia** (strona serwera MCP): Tekst DSL jest tłumaczony na IR JSON przez parser. Tłumacz wykonuje tylko analizę składniową; nie kompiluje, nie rozwiązuje odwołań zewnętrznych, nie uzyskuje dostępu do dysku. Jakikolwiek błąd tłumczenia przerywa cały pakiet, a IR nie jest wysyłane do silnika.
- **Faza Aplikacji** (strona silnika): Companion odbiera IR JSON i stosuje operacje tworzenia/aktualizacji/usuwania zasób po zasobie. Awaria jednego zasobu nie wpływa na inne zasoby w tym samym pakiecie.

### 2.2 Relacja między DSL a MCP Action

Możliwości kontroli AI ForgeMind pochodzą z dwóch typów narzędzi:

| Wymiar | MCP Action | DSL |
|--------|-----------|-----|
| Ziarnistość | Precyzyjna kontrola pojedynczej jednostki | Zarządzeniowe tworzenie/modyfikacja masowe |
| Przykład | Dodanie zmiennej, podłączenie przewodu | Blok tekstu, który tworzy cały zasób Blueprint |
| Kanał | Wysłane bezpośrednio do silnika przez Bridge | Najpierw przetłumaczone na IR, potem wysłane przez Bridge |
| Rola | precyzyjne dostrojenie | Architektura na wysokim poziomie |

Oba są **komplementarne** — DSL zajmuje się konstrukcją masową "od zera", podczas gdy Action zajmuje się precyzyjnym dopasowaniem "od dobrego do wybitnego".

---

## 2. Gdzie DSL Pasuje w Systemie

```
┌──────────────┐   MCP   ┌──────────────────────────┐
│  Klient AI    │ ◄────► │  ForgeMindServer (host)   │
└──────────────┘         │  • Odbiera ciąg znaków DSL │
                         │  • Wywołuje tłumacza       │
                         │    do wytworzenia IR       │
                         │  • Wysyła IR przez Bridge  │
                         └────────────┬─────────────┘
                                      │ mostek TCP
                         ┌────────────▼─────────────┐
                         │  Companion (adapter siln.)│
                         │  • Odbiera IR             │
                         │  • Stosuje per zasób      │
                         │  • Kompiluje / Zapisuje    │
                         └──────────────────────────┘
```

Pozycja DSL w ForgeMind:
- AI generuje tekst deklaracji DSL
- Narzędzie MCP `dsl` odbiera tekst i wywołuje tłumacza do wytworzenia IR JSON
- IR JSON jest wysyłane do edytora silnika poprzez TCP Bridge
- Companion (adapter silnika) stosuje go w edytorze, aby zakończyć generowanie zasobów

---

## 3. Podstawowy Styl Składni DSL

DSL pożycza składniową formę C#, ale **nie przechodzi przez kompilator C#**. Naśladuje C# konwencji pisowni, ale zarządza wewnętrzną analizą semantyczną kompilatora (sprawdzanie typów, rozwiązywanie przeciążeń itp.) w samym tłumaczu.

### 3.1 Struktura Bloku Deklaracji

Każdy blok deklaracji DSL jest deklaracją klasy najwyższego poziomu (lub struct/enum) z `[Attribute]`:

```csharp
[DomainAttr(Path = "/Game/Path/AssetName", Mode = Create)]
[AdditionalAttributes]
public class AssetName
{
    // deklaracje składowych: zmienne, komponenty, funkcje itp.
}
```

### 3.2 Tryby Create / Update / Delete

Wszystkie domenny wspierają jednolicie tryby operacyjne:

| Wartość Trybu | Znaczenie | Ładunek |
|---------------|-----------|---------|
| `Create` | Utwóryć nowy zasób | `definition` (kompletna definicja zasobu) |
| `Update` | Zmodyfikować istniejący zasób | `patches` (tablica stanu docelowego) |
| `Delete` | Usunąć cały zasób | puste `patches` |

**Przykład Create:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Chest
{
    [Variable] float Health = 100f;
}
```

**Przykład Update:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Update)]
public class BP_Chest
{
    [Variable] float MaxHealth = 200f;     // nie w zasobie → dodać
    [Variable(Category = "Stats")]
    float Health = 90f;                    // już w zasobie → zamienić
    [Delete]                              // oznaczyć do usunięcia
    float LegacyVariable;                  // → usunąć istniejącą składową
}
```

Rzeczywisty typ operacji (add / replace / delete) jest wnioskowany przez Companion na podstawie istnienia składowej w zasobie docelowym i obecnotacji adnotacji `[Delete]` — nie jest deklarowany explicite w DSL.

**Przykład Delete:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_OBSOLETE", Delete = true)]
public class BP_OBSOLETE { }
```

### 3.3 Odwołanie do Innych Zasobów — using aliasy

DSL obsługuje składnię aliasów `using` w celu uproszczenia odwołań między zasobami:

```csharp
// using na poziomie pliku (dotyczy wszystkich bloków)
using WoodTex = "/Game/Textures/T_Wood.uasset";

// using na poziomie bloku (dotyczy tylko tego bloku)
[Blueprint(Path = "/Game/Chest/BP_GoldenChest", Mode = Create)]
public class BP_GoldenChest
{
    using GoldMat = "/Game/Materials/M_Gold.uasset";
    [Variable] MaterialInstance Mat = MaterialInstance(GoldMat);
}
```

Jest to konwencja składniowa DSL — `using` mapuje identyfikatory na ciągi ścieżki zasobu w celu ułatwienia odwołań w deklaracjach.

### 3.4 Odwołania Typów Między Blokami

Oprócz wyraźnego `using`, DSL obsługuje także dorozumiane odwołania typów między blokami:

Jeśli deklaracja najwyższego poziomu to `[Blueprint(Path="/Game/AI/BP_Hunter")] public class BP_Hunter { ... }`, identyfikator `BP_Hunter` może być używany bezpośrednio w kolejnych blokach, i automatycznie rozwiąże się w ścieżkę zasobu `"/Game/AI/BP_Hunter"`.

### 3.5 Komentarze

DSL obsługuje komentarze w stylu C#:
- `//` jednoliniowe komentarze
- `/* */` komentarze blokowe

Komentarze nie generują wyjścia IR.

---

## 4. IR (Reprezentacja Pośrednia)

IR to pośredni artefakt tłumaczenia między tłumaczem a Companion — opis zasobu ustrukturyzowany w formacie JSON. Język "mostowy" między DSL a silnikiem.

### 4.1 Batch IR vs. IR Pojedynczego Zasobu

**Batch IR** (wielozasobowy pakiet):
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

**IR Pojedynczego Zasobu**:
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

### 4.2 Tabela Pól IR Pojedynczego Zasobu

| Pole | Typ | Opis |
|------|-----|------|
| `version` | string | Wersja silnika (np. `"5.6"`), musi dokładnie pasować do głównej wersji silnika |
| `assetType` | string | Typ domeny (określony przez Attribute bloku) |
| `path` | string | Ścieżka zasobu (wymagane) |
| `mode` | string | `create` / `update` / `delete` |
| `compile` | bool | Czy kompilować po zastosowaniu (domyślnie true) |
| `save` | bool | Czy zapisać na dysk po zastosowaniu |
| `patches` | array | Wymagany dla trybu update (stan docelowy na składową, bez pola op) |
| `definition` | object | Wymagany dla trybu create (pełna definicja zasobu) |

> **Ograniczenie trybu/ładunku**: `mode=create` musi nieść `definition` i nie może zawierać `patches`; `mode=update` musi nieść `patches` i nie może zawierać `definition`; `mode=delete` niesie puste `patches`. Jakakolwiek niespójność jest nieprawidłowym ładunkiem.

### 4.3 Symetria Dwukierunkowa

IR ForgeMind używa **tej samej struktury ładunku** zarówno w "kierunku zapisu" (`apply_asset_ir`), jak i "kierunku odczytu" (`get_asset_ir`):

| Kierunek | Polecenie | Wartość `mode` |
|----------|-----------|----------------|
| Zapis | `apply_asset_ir` | `create` / `update` / `delete` |
| Odczyt | `get_asset_ir` | `read` |

Pozwala to AI na "odczyt → modyfikacja mode → zapis" tego samego IR bez konwersji schematu.

---

## 5. Rozpoznawanie i Routing Domen

### 5.1 Attribute Określają Dome

Każdy blok deklaracji w ciągu znaków DSL jest automatycznie kierowany do typu zasobu za pomocą znacznika `[Attribute]`:

```csharp
[Blueprint(Path = "...")]          // → tłumacz Blueprint → assetType = "blueprint"
[Material(Path = "...")]           // → tłumacz Materiał  → assetType = "material"
[BehaviorTree(Path = "...")]       // → tłumacz Drzewo Zachowań → assetType = "behavior_tree"
```

**Tabela Mapowania Attribute → assetType:**

| Attribute | Typ Zasobu |
|-----------|------------|
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

> Tabela powyżej mapuje wszystkie aktualnie znane domeny. Dodanie nowej domeny wymaga tylko zarejestrowania nowego mapowania Attribute → tłumacza po stronie tłumaczenia — struktura hosta nie wymaga zmian.

### 5.2 Zasady Routingu

- Parser wykonuje dokładne dopasowanie nazwy dla każdego bloku deklaracji (na poziomie tokenów, nie dopasowania podciągów)
- Gdy wiele Attributes, pierwszy trafiony w tabeli mapowania wygrywa (`[WidgetBlueprint]` ma pierwszeństwo przed `[Blueprint]`)
- **Nierozpoznany Attribute nie wycicha milcząco** — zgłasza błąd i przerywa cały pakiet

### 5.3 Mieszanie Wielu Domen w Jednym DSL

Pojedynczy ciąg znaków DSL może zawierać **dowolną liczbę bloków deklaracji dowolnej domeny**:

```csharp
using WoodTex = "/Game/Textures/T_Wood.uasset";

// ── Blok 1: domena Blueprint ──
[Blueprint(Path = "/Game/AI/BP_Hunter", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Hunter
{
    [Variable(Category = "Stats")]
    float Health = 100f;
}

// ── Blok 2: domena Drzewo Zachowań ──
[BehaviorTree(Path = "/Game/AI/BT_Hunter", Mode = Create)]
public class BT_Hunter { }

// ── Blok 3: domena Materiał ──
[Material(Path = "/Game/Materials/M_Wood", Mode = Create)]
public function M_Wood
{
    BaseColor = Texture2D(WoodTex);
    Roughness = 0.7f;
}
```

Wszystkie bloki deklaracji w tym samym DSL są tłumaczone niezależnie, każdy produkuje swój własny IR, montowane w jednym Batch IR wysyłanym do silnika razem przez Bridge.

---

## 6. Bridge i Companion

### 6.1 Bridge Transport

IR jest wysyłany do edytora silnika przez lokalny most TCP:
- Warstwa mostu odpowiada za niezawodną dostawę IR po stronie silnika
- Bridge jest lokalną infrastrukturą komunikacji międzyprocesowej ForgeMind: strona hosta utrzymuje serwer mostu, strona adaptera silnika utrzymuje klienta mostu

### 6.2 Obowiązki Companion

Companion to plugin adaptera wewnątrz silnika (np. ForgeMindForUE). Jego obowiązki obejmują:

1. **Odbiór IR JSON** → wysłanie do obsługującego domeny silnika na podstawie `assetType`
2. **Create**: Zbudowanie kompletnego zasobu z `definition` → zarejestrowanie w rejestrze zasobów → opcjonalnie skompilować → opcjonalnie zapisać na dysku
3. **Update**: Zastosować modyfikacje na poziomie składowych z `patches` (add / replace / delete wnioskowane przez Companion)
4. **Delete**: Rozpakować pakiet zasobu → usunąć z rejestru zasobów → usunąć pliki z dysku
5. **Raportowanie Wyników**: Zwrócić sukces / błąd na zasób; kierunek zapisu zwraca `patchResults[].actualOp`

> Awaria jednego zasobu nie wpływa na inne zasoby w tym samym pakiecie (bez wycofywania transakcji). Jest to celowy wybór projektowy w ForgeMind — przestrzeń stanów zasobów edytora jest duża, koszty transakcji są wysokie, i częściowy sukces jest preferowany ponad wszystko albo nic.

---

## 7. Diagnostyka i Obsługa Błędów

Obsługa błędów DSL dzieli się na dwie fazy:

| Faza | Typy Błędów | Obsługa |
|------|------------|---------|
| Faza tłumczenia (strona MCP) | Błędy składni, nierozpoznane Attributes, brakujące wymagane parametry | Jakikolwiek błąd → pakiet zakończony, IR nie wysłany do Bridge |
| Faza zastosowania (strona Companion) | Zasób nie znaleziony, typ niedopasowany, błąd kompilacji | Awaria pojedynczego zasobu nie wpływa na inne zasoby w pakiecie |

Ostrzeżenia (Warn) podczas fazy tłumczenia nie przerywają pakietu, ale są dołączane do tekstu odpowiedzi zwracanego do AI.

### 7.1 Typowe Kody Błędów Fazy Tłumaczenia

Przykładowe kody błędów (w dokumentacji domeny znajdziesz szczegóły):

| Kod Błędu | Znaczenie |
|-----------|-----------|
| `FM1004` | Nieprawidłowa wartość Mode |
| `FM1005` | Brak wymaganego parametru Path |
| `FM1002` | Błąd tłumczenia domeny (kod wycofania ogólnego) |

### 7.2 Główne Kody Błędów na Poziomie Zasobu (Faza Zastosowania)

| Kod Błędu | Znaczenie |
|-----------|-----------|
| `ASSET_NOT_FOUND` | Ścieżka zasobu nie istnieje (scenariusze update / delete) |
| `ASSET_ALREADY_EXISTS` | Ścieżka już istnieje (scenariusze create, domyślnie nie nadpisuje) |
| `ASSET_TYPE_MISMATCH` | Typ nie pasuje |
| `ASSET_LOCKED` | Zasób zablokowany w edytorze |

---

## 8. Rozszerzanie Domen DSL

### 8.1 Składniki Domeny

Kompletna domena DSL składa się z następujących składników:

1. **Znacznik Attribute** — np. `[Blueprint]`, `[Material]`, używany do rozpoznawania routingu
2. **Implementacja Tłumacza Domeny** — odpowiedzialne za tłumaczenie deklaracji DSL tej domeny na IR JSON
3. **Dokumentacja Składni DSL** — definiuje reguły deklaracji i parametry Attribute domeny
4. **Konwencja Schematu IR** — definiuje strukturę JSON `definition` / `patches` domeny
5. **Obsługujący Domeny Companion** — odpowiednia implementacja po stronie silnika, który stosuje zasoby z IR w edytorze

### 8.2 Podstawowe Kroki Dodania Nowej Domeny

1. Zdefiniuj nazwę głównego Attribute domeny (np. `[NewDomain]`)
2. Zrealizuj tłumacza domeny (odziedzicz klasę bazową tłumacza, nadpisz metody wizytora drzewa składniowego do ekstrakcji semantyki domeny, wytwórz IR JSON)
3. Zarejestruj się w strukturze, aby parser mógł znaleźć tłumacza przez `assetType`
4. Napisz dokumentację składni DSL i konwencje schematu IR
5. Zrealizuj odpowiedni obsługujący domeny po stronie Companion silnika

Dodanie nowej domeny **nie wymaga żadnych zmian w kodzie hosta** — struktura odkrywa nowych tłumaczy domen przez mechanizm rejestracji.

### 8.3 Zasady Implementacji Tłumaczy

Tłumacze przestrzegają następujących umów behawioralnych, które należy przestrzegać przy rozszerzaniu domen DSL:

- **Funkcja Czysta**: Tłumacz przyjmuje ciąg znaków DSL jako wejście i wydaje IR JSON jako wyjście, bez efektów ubocznych i bez stanu reszty. Ten sam DSL zawsze wytwarza ten sam IR.
- **Tylko Analiza Składniowa**: Tłumacze nie wykonują kompilacji C#, nie otrzymują dostępu do dysku, nie rozwiązują zewnętrznych zasobów; tylko tłumaczą składniową formę DSL na IR. Walidacja semantyczna leży po stronie silnika.
- **Odzyskiwanie po Błędzach**: Może tolerować niestandardową składnię DSL (np. przypisanie ciągu do aliasów `using`) bez przerywania tłumaczenia.
- **Degradacja Nieznanej Składni**: Podczas napotkania składni, której nie można dokładnie przetłumaczyć, zgłasza ostrzeżenie zamiast błędu, pozostawiając do fazy aplikacji silnika.

---

## 9. Granica Open Source i Komercyjna

Możliwości DSL ForgeMind dzielą się na dwie części:

### 9.1 Open Source (Publiczne Rdzeń)

Rdzeń open source dostarcza strukturę hosta DSL:

- **Warstwa Struktury DSL**: Odpowiedzialna za analizowanie ciągów znaków DSL (podział według bloków deklaracji najwyższego poziomu, rozpoznawanie Attributes i routing do tłumaczy), interfejsy tłumacza i mechanizmy rejestracji, raportowanie diagnostyczne i inna infrastruktura.
- **Umowa Pluginu i Mechanizm Rejestracji**: Nowi tłumacze domen są odkrywani przez rejestrację; struktura hosta nie wymaga wbudowanej logiki rozgałęzienia specyficznej dla domeny.
- **Open Source wyklucza Tłumaczy Domen**: Konkretna logika tłumaczenia domeny (Blueprints, Materiały, Drzewa Zachowań itp.) nie jest częścią publicznego rdzenia open source.

### 9.2 Komercyjne (Płatne Rozszerzenie)

Moduł rozszerzenia komercyjnego opiera się na strukturze open source, aby zapewnić:

- Wszystkie implementacje konkretnych tłumaczy domen (Blueprint, Materiał, Drzewo Zachowań, AnimBlueprint, Niagara, MetaSound, Chaos, Groom, Landscape, Foliage, Physics, Paper2D, Sequencer, …)
- Punkt wejścia narzędzia MCP `dsl`, służący jako punkt wykonania tłumczenia i wysyłania DSL
- Konwertery IR według domeny, wykonujące przetwarzanie specyficzne dla domeny zanim IR zostanie wysłane do silnika

> Rdzeń open source definiuje strukturę i mechanizm rozszerzenia; konkretne możliwości domen (składnia i tłumacze DSL dla Blueprints, Materiałów, Drzew Zachowań itp.) są dostarczane przez moduł rozszerzenia komercyjnego. Użytkownicy open source mogą rejestrować niestandardowe tłumacze domen w oparciu o strukturę, aby rozszerzyć własne możliwości DSL.

---

## 10. Ograniczenia Behawioralne i Zasady Projektowania

| Etykieta | Zachowanie DSL | Znaczenie |
|----------|---------------|----------|
| Styl Składniowy | Inspirowany C# | C# jest językiem z największą ilością danych treningowych AI, zmniejszającym obciążenie poznawcze |
| Współrzędne Węzła | Nie wyrażone w DSL | Dotyczy tylko "co istnieje i jak są połączone" — semantyka topologiczna; układ jest generowany automatycznie przez silnik |
| Path Wymagane | Tak | Konwencje nazewnictwa i przynależność do modułu są jawnie decydowane przez AI |
| Pasowanie Wersji | Dokładna główna wersja | IR `version` musi pasować do głównej wersji silnika |
| Polityka Transakcji | Bez wycofania | Częściowa awaria zasobu w pakiecie nie wpływa na inne zasoby |
| Próba na Sucho | Nie istnieje | IR jest wysyłane tylko do silnika, nie zwrócone do wywołującego |
| Nierozpoznany Attribute | Przerywa pakiet | Milczone wycofanie spowodowałoby, że AI nieświadomie utworzyłoby zły typ zasobu |
| Update patch | Wyraża stan docelowy | Patch nie zawiera pola `op`; operacje są wnioskowane przez Companion na podstawie istnienia składowej |

---

## 11. Szybka Referencja

### Szablon Bloku Deklaracji DSL

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

### Szablon IR Pojedynczego Zasobu

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

### Przegląd Pipeline

```
Klient AI
  → DSL (tekst zawierający jeden lub więcej bloków deklaracji domeny)
  → Narzędzie MCP dsl (faza tłumaczenia)
    → parser kieruje do tłumacza domeny na podstawie Attribute
    → tłumacze produkują IR JSON
  → Batch IR ({ version, batch, assets })
  → Bridge (lokalny TCP)
  → Companion (faza aplikacji)
    → Utwóryć / Aktualizacja / Usuwanie / Kompiluj / Zapisz na zasób
  → Zasób silnika
```
