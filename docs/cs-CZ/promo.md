# Veřejná komunikace ForgeMind

## Prezentace v jedné větě

ForgeMind je platforma pro správu herních enginů přes rozhraní MCP: poskytuje AI klientům správu projektů Unreal Engine, čtení assetů, spouštění / sestavení / operace v editoru, a umožňuje AI pracovat s enginem jako s nástrojem.

## Klíčové funkce

- **Nativní integrace MCP**: standardní protokol MCP (HTTP / stdio), hlavní AI klienti se připojí a fungují okamžitě.
- **Samostatně sestavitelný open source výřez**: veřejný repozitář obsahuje hosta, všechny open source pluginy, ukázkový projekt UE a testy; s Apache-2.0 je možné sestavit a rozšiřovat samostatně.
- **Architektura adaptérů enginů**: host a engine jsou odděleny přes lokální TCP most, rozšiřitelné nad rámec UE.
- **Offline čtení assetů**: parsování struktury `.uasset` bez otevírání editoru, v kombinaci s indexem skenování (SPK) pro funkce vyhledávání znalostí.
- **Rozšiřování funkcí pluginy**: host integruje pouze minimální sadu nástrojů; funkce rozšiřují pluginy, hranice jsou jasné a redukovatelné.

## Komerční verze (jedna věta)

Komerční verze nabízí na bázi OpenSource generování assetů DSL, komerční autorizaci a enterprise podporu; detaily syntaxe DSL a režimů autorizace na vyžádání u vydavatele.

## Pravidla veřejné komunikace

- Neslibujte nevydané funkce; plán je závazný podle `roadmap.md`.
- Nehanobte podobné nástroje; neprozrazujte navenek vnitřní detaily komerčních modulů (algoritmy autorizace, interní IR pravidla DSL atd.).
- U chování souvisejícího se zdrojovým kódem vždy odkazujte na veřejný výřez; nejisté prvky označujte jako «čeká na potvrzení».
