# Bezpieczeństwo ForgeMind

Ten dokument opisuje projekt bezpieczeństwa i zalecenia użytkowania publicznego wycinka.

## 1. Granice komunikacji lokalnej

- **Serwer domyślnie nasłuchuje tylko w pętli zwrotnej (loopback)**: w trybie HTTP nasłuchuje na `127.0.0.1:8765`, nie jest wystawiany na sieć zewnętrzną.
- **Most to lokalny TCP**: między hostem a adapterem silnika jest lokalne połączenie TCP; adapter musi odczytać `%USERPROFILE%\.forgemind\bridge.json` (host, port, dane uwierzytelniające), aby ukończyć discovery i uwierzytelnienie przed handshake.
- Nie wystawiaj portu serwera w publicznej sieci; do zdalnego użytku umieść go w zaufanej sieci i przekieruj przez bezpieczny kanał (np. tunel SSH).

## 2. Integralność wtyczek

- `PluginLoader` hosta wykonuje **weryfikację integralności manifestu** (`PluginIntegrityVerifier`) i weryfikację podpisu (`PluginSigningKeys`) podczas ładowania wtyczek: pakiet musi zawierać główną DLL + `.manifest.json` o tej samej nazwie; jeśli brakuje lub nie pasuje, zostaje odrzucony / pominięty.
- Wstrzykiwanie narzędzi wtyczek odbywa się w procesie hosta; instaluj tylko wtyczki z zaufanych źródeł (zbudowane z publicznego wycinka lub z oficjalnych wydań).

## 3. Model autoryzacji

**Mechanizmy bezpieczeństwa możliwe do ujawnienia**:
- `IAuthorizationService`: abstrakcja punktu wejścia autoryzacji hosta; domyślna implementacja to `OpenAuthorizationService` (stałe funkcje społeczności, nie analizuje żadnego pliku licencji i nie generuje autoryzacji próbnych).
- Zaufane ServiceOverride: przez ServiceOverride w manifeście wtyczka może zastępować usługi hosta wg typu abstrakcyjnego (host nie zna pojęcia «komercyjny», zastępuje po prostu wg typu).
- Uwierzytelnianie tożsamości wtyczek i weryfikacja manifestu: `PluginIntegrityVerifier` / `PluginSigningKeys` weryfikują manifesty i podpisy wtyczek, gwarantując ładowanie tylko zaufanych wtyczek.
- Uwierzytelnianie połączenia mostu: adapter wykonuje discovery i handshake danych uwierzytelniających przez `bridge.json`, potwierdzając wiarygodność źródła połączenia.

**Autoryzacja komercyjna**: Core dostarcza abstrakcję `IAuthorizationService` i zaufany mechanizm rozszerzania ServiceOverride. Implementacja autoryzacji komercyjnej jest wtyczką closed source; konkretny protokół autoryzacji, wystawianie, weryfikacja i logika wiązania nie należą do publicznego wycinka.

## 4. Uprawnienia narzędzi

- Narzędzia operacyjne silnika podlegają `ServerPermissionChecker` (wewnątrz wtyczek) i sprawdzają autoryzację funkcji przed wykonaniem.
- Nieudane `Execute` narzędzi zwraca jednolicie komunikat błędu `ToolResult`, bez ujawniania wewnętrznych szczegółów stosu.

## 5. Zalecenia użytkowania

- Otwieraj port MCP tylko dla zaufanych klientów; klienci mają możliwość operowania silnikiem, co odpowiada uprawnieniom lokalnego administratora.
- Aktualizuj wtyczki, host i projekt przykładowy, aby szybko otrzymywać poprawki bezpieczeństwa.
- Nie kopiuj danych uwierzytelniających `bridge.json` do niezaufanych procesów na współdzielonych maszynach.
- W razie problemów bezpieczeństwa zgłoś przez kanały wydawcy, nie ujawniając publicznie szczegółów niepoprawionych podatności.
