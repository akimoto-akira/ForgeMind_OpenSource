# Dokumentacja ForgeMind (polski)

ForgeMind — platforma sterowania silnikami gier oparta na MCP: poprzez protokół MCP udostępnia klientom AI zarządzanie projektami Unreal Engine, odczyt assetów oraz operacje uruchamiania/budowania/edycji. Obecna implementacja to UE (5.0–5.8); projekt platformy jest rozszerzalny na inne silniki.

> Niniejsza dokumentacja koncentruje się na części **open source**: `OpenSource\` (host i wtyczki), `UEProject\` (projekt przykładowy) i `tests\` (testy). Funkcje komercyjne (generowanie assetów DSL, implementacja licencji komercyjnych) są opisane tylko ogólnie i nie są zawarte w wersji open source. OpenSource zachowuje niezbędne abstrakcje licencji i otwartą implementację.

## Indeks dokumentacji

| Dokument | Zawartość |
|---|---|
| [Szybki start](getting-started.md) | Wymagania środowiskowe, budowanie, uruchamianie, pierwsze użycie |
| [Architektura](architecture.md) | Host / wtyczki / protokół bridge / skanowanie i SPK |
| [Referencja API](api-reference.md) | Protokół MCP, protokół bridge, kontrakty narzędzi |
| [Referencja narzędzi](tools-reference.md) | Wszystkie narzędzia MCP i ich parametry |
| [Przewodnik rozwoju](development-guide.md) | Budowanie, debugowanie, dodawanie narzędzi/adapterów, testy |
| [Przewodnik kontrybucji](contributing.md) | Limity open source, proces PR, wymagania testowe |
| [Rozwiązywanie problemów](troubleshooting.md) | Częste problemy i rozwiązania |
| [Mapa drogowa](roadmap.md) | Planowany kierunek |
| [Promocja](promo.md) | Prezentacja projektu i zasady komunikacji |
| [Bezpieczeństwo](security.md) | Projekt bezpieczeństwa i dobre praktyki |

## Przegląd zakresu open source

```
OpenSource\
  ForgeMindServer               Host MCP (4 wbudowane narzędzia + ładowanie wtyczek + bridge TCP)
  ForgeMind.OfflineEngineKit    Narzędzia silnika/projektu/uruchamiania/budowania/edycji (wtyczka)
  ForgeMind.Assets              Odczyt offline .uasset (wtyczka)
  ForgeMind.Knowledge           Wyszukiwanie wiedzy (wtyczka)
  ForgeMind.Scan.Abstractions   Abstrakcje danych skanowania i dostawcy
  ForgeMind.Scan                Odczyt i pakowanie SPK
  ForgeMind.Scan.Unreal         Implementacja skanowania Unreal Engine
  ForgeMind.EngineScanner       Autonomiczny skaner CLI generujący bezpośrednio .spk
  ForgeMind.Speedy              Niskopoziomowa baza SPK
UEProject\ForgeMind_5_0         Przykładowy projekt UE (5.0–5.8)
tests\                          Publiczne testy jednostkowe + testy integracyjne serwera
```

## Licencja

Zawartość części open source jest licencjonowana na Apache-2.0. Moduły komercyjne nie są częścią open source i są dostarczane na osobnej licencji komercyjnej.
