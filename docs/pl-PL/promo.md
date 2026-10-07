# Komunikacja publiczna ForgeMind

## Prezentacja w jednym zdaniu

ForgeMind to platforma sterowania silnikiem gier przez interfejs MCP: udostępnia klientom AI zarządzanie projektami Unreal Engine, odczyt assetów, uruchamianie / build / operacje w edytorze, pozwalając AI obsługiwać silnik jak narzędzie.

## Najważniejsze cechy

- **Natywna integracja MCP**: standardowy protokół MCP (HTTP / stdio), główne klienty AI łączą się i działają od razu.
- **Samobudujący się wycinek open source**: publiczne repozytorium zawiera host, wszystkie wtyczki open source, przykładowy projekt UE i testy; z Apache-2.0 można samodzielnie budować i rozszerzać.
- **Architektura adapterów silników**: host i silnik są rozdzielone przez lokalny most TCP, rozszerzalne poza UE.
- **Offline odczyt assetów**: parsowanie struktury `.uasset` bez otwierania edytora, połączone z indeksem skanowania (SPK) w celu dostarczenia funkcji wyszukiwania wiedzy.
- **Rozszerzanie funkcji przez wtyczki**: host integruje tylko minimalny zestaw narzędzi; funkcje są rozszerzane przez wtyczki, z jasnymi i redukowalnymi granicami.

## Wersja komercyjna (w jednym zdaniu)

Wersja komercyjna oferuje, na bazie OpenSource, generowanie assetów DSL, autoryzację komercyjną i wsparcie enterprise; szczegóły składni DSL i trybów autoryzacji — kontakt z wydawcą.

## Zasady komunikacji publicznej

- Nie obiecuj funkcji nieopublikowanych; mapa drogowa obowiązuje wg `roadmap.md`.
- Nie oczerniaj podobnych narzędzi; nie ujawniaj na zewnątrz wewnętrznych szczegółów modułów komercyjnych (algorytmy autoryzacji, wewnętrzne reguły IR DSL itd.).
- Przy zachowaniach związanych z kodem źródłowym odwołuj się zawsze do publicznego wycinka; niepewne elementy oznaczaj jako «do potwierdzenia».
