# Bezpečnost ForgeMind

Tento dokument popisuje návrh zabezpečení a doporučení pro používání veřejného výřezu.

## 1. Hranice lokální komunikace

- **Server ve výchozím stavu naslouchá pouze loopbacku**: v režimu HTTP naslouchá na `127.0.0.1:8765`, nevystavuje se do vnější sítě.
- **Most je lokální TCP**: mezi hostem a adaptérem enginu je lokální TCP spojení; adaptér musí přečíst `%USERPROFILE%\.forgemind\bridge.json` (host, port, pověření) pro discovery a autentizaci před handshakem.
- Nevystavujte port serveru do veřejné sítě; pro vzdálené použití umístěte do důvěryhodné sítě a proxy přes bezpečný kanál (např. SSH tunel).

## 2. Integrita pluginů

- `PluginLoader` hostu provádí **kontrolu integrity manifestu** (`PluginIntegrityVerifier`) a kontrolu podpisu (`PluginSigningKeys`) při načítání pluginů: balíček musí obsahovat hlavní DLL + stejnojmenný `.manifest.json`; při absenci nebo neshodě je odmítnut / přeskočen.
- Injekce nástrojů pluginů probíhá v procesu hostu; instalujte pouze pluginy z důvěryhodných zdrojů (sestavené z veřejného výřezu nebo oficiálních vydání).

## 3. Model autorizace

**Zveřejňované bezpečnostní mechanismy**:
- `IAuthorizationService`: abstrakce vstupního bodu autorizace hostu; výchozí implementace — `OpenAuthorizationService` (pevné komunitní schopnosti, neanalyzuje žádný licenční soubor a negeneruje zkušební autorizace).
- Důvěryhodný ServiceOverride: přes ServiceOverride v manifestu pluginu může plugin nahrazovat služby hostu podle abstraktního typu (host nezná pojem «komerční», pouze nahrazuje podle typu).
- Autentizace identity pluginů a kontrola manifestu: `PluginIntegrityVerifier` / `PluginSigningKeys` ověřují manifesty a podpisy pluginů a zajišťují načítání pouze důvěryhodných pluginů.
- Autentizace mostního spojení: adaptér provádí discovery a handshake pověření přes `bridge.json`, čímž potvrzuje důvěryhodnost zdroje spojení.

**Komerční autorizace**: Core poskytuje abstrakci `IAuthorizationService` a důvěryhodný mechanismus rozšíření ServiceOverride. Implementace komerční autorizace je plugin s uzavřeným zdrojovým kódem; konkrétní protokol autorizace, vydávání, ověřování a logika vazby nejsou součástí veřejného výřezu.

## 4. Oprávnění nástrojů

- Nástroje operací s enginem podléhají `ServerPermissionChecker` (uvnitř pluginů) a před provedením ověřují autorizaci funkcí.
- Neúspěšné `Execute` nástrojů jednotně vrací chybovou zprávu `ToolResult`, bez odhalování vnitřních detailů zásobníku.

## 5. Doporučení pro používání

- Otevírejte port MCP pouze důvěryhodným klientům; klienti mají možnost ovládat engine, což odpovídá oprávněním lokálního administrátora.
- Aktualizujte pluginy, hosta a ukázkový projekt, abyste včas získávali bezpečnostní opravy.
- Nekopírujte pověření `bridge.json` do nedůvěryhodných procesů na sdílených strojích.
- Při bezpečnostních problémech hlaste přes kanály vydavatele, neprozrazujte veřejně detaily neopravených zranitelností.
