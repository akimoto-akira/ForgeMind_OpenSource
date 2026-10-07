# Plán ForgeMind

Směr je prezentován z veřejné perspektivy, po milnících; konkrétní termíny určují vydání.

## Blízké období

- **Dokončení open source výřezu**: sjednotit skripty sestavení a instrukce pro připojení do jednoho příkazu, aby se snížil práh vstupu pro nové přispěvatele.
- **Synchronizace dokumentace**: udržovat stránky v `docs\zh-CN\` v souladu se změnami zdrojového kódu a zajistit konzistenci s veřejným výřezem.
- **Pokrytí testy**: rozšířit případy `ForgeMind.PublicTests` a `ForgeMindServerTest` (mostní příkazy, skenování, hraniční scénáře SPK).

## Střední období

- **Rozšiřování adaptérů enginů**: na základě stávajícího UE adaptéru (5.0–5.8) zkoumat integraci dalších enginů / editorů (analogický mostní protokol).
- **Posílení skenování a knowledge**: zlepšit pokrytí parsování DocumentEngine pro headery / blueprints / soubory pravidel a optimalizovat efektivitu a inkrementální aktualizaci balení SPK.
- **Ekosystém pluginů**: vylepšit API pluginů a specifikaci manifestu, podporovat bohatší pluginy třetích stran.

## Dlouhodobé období

- **Otevření dokumentace použití DSL**: až se komerční DSL stabilizuje, může být v souladu se strategií produktu publikována část dokumentace použití, příkladů a výukových materiálů; implementace DSL, Translator, IR pravidla a logika generování assetů zůstávají komerčními moduly.
- **Platforma**: pozicionovat ForgeMind jako multienginovou MCP platformu pro správu, konsolidující protokol a vrstvy abstrakce nezávislé na enginu.

> Směr komerční verze (systém autorizace, enterprise podpora atd.) není v tomto veřejném plánu zveřejňován: obraťte se na vydavatele.
