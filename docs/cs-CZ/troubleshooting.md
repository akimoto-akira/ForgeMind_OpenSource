# Řešení problémů ForgeMind

Tento dokument shrnuje běžné problémy a řešení. Před řešením potíží se ujistěte, že verze prostředí odpovídá požadavkům v `getting-started.md` §1.

## 1. Server se nespustí

- **Port je obsazený**: zkuste `--port` určit jiný port nebo změnit `FORGEMIND_PORT`; ověřte, že proces běží (`netstat -ano | findstr 8765`).
- **Chyba sestavení**: sestavujte jednotlivé projekty, nesestavujte přímo `ForgeMind.sln` (obsahuje komerční odkazy, viz `development-guide.md` §1).
- **Chyba konfigurace**: odstraňte `%USERPROFILE%\.forgemind\settings.json` (konfigurace se přečte při spuštění; po smazání se vytvoří výchozí) — zálohujte před smazáním.

## 2. Klient se nemůže připojit

- Ověřte adresu serveru: výchozí `http://127.0.0.1:8765/mcp`; parametr `--port` / `FORGEMIND_PORT` musí odpovídat.
- Ověřte, že je server spuštěn, a zkuste `curl http://127.0.0.1:8765/mcp`.
- Zkontrolujte nastavení klienta (mcpServers): formát `{ "mcpServers": { "forgemind": { "url": "..." } } }`.

## 3. Plugin se nenačte

- Ověřte umístění: DLL + `.manifest.json` stejného názvu v adresáři `plugins\` hostu (nebo adresáři určeném `FORGEMIND_PLUGINS_DIR`); zkontrolujte název DLL.
- Zkontrolujte manifest (formát JSON, položky závislostí atd.), poté restartujte host.
- Zkontrolujte, zda se v logu hostu nevyskytuje 'load plugin failed' a podle toho postupujte (přeskočí se pouze daný plugin).

## 4. Bridge se nepřipojí

- Ověřte, že je `bridge.json` v `%USERPROFILE%\.forgemind\` a obsahuje správný port a pověření; poté editor restartujte.
- Ověřte, že je host spuštěn; adaptér se znovu připojuje s exponenciálním backoffem.
- V editoru otevřete výstupní log pluginu `ForgeMindForUE` (Output Log / LogForgeMind) pro zobrazení chyby.

## 5. Nástroj vrací chybu oprávnění

- Operace související s enginem podléhají `ServerPermissionChecker`. Ověřte konfiguraci oprávnění v nastavení serveru (povolené operace).
- `settings` může zobrazit/změnit konfiguraci; po změně restartujte host.

## 6. Pomalé vyhledávání znalostí / SPK

- Vygenerujte indexový balíček SPK přes `ForgeMind.EngineScanner <spkPath>` a ujistěte se, že zdroj indexového balíčku ukazuje na správný balíček.
- Velký projekt: zkontrolujte velikost indexu a filtry (adresář/typ), nastavte přiměřené zdroje a rozsahy.
