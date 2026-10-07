# Mapa drogowa ForgeMind

Kierunek jest przedstawiony w perspektywie publicznej, w podziale na kamienie milowe; konkretne terminy zależą od wydań.

## Krótki okres

- **Ukończenie wycinka open source**: zintegrowanie skryptów budowania i instrukcji połączenia w jedną komendę, aby obniżyć koszt wejścia nowych kontrybutorów.
- **Synchronizacja dokumentacji**: utrzymywanie stron w `docs\zh-CN\` zgodnie ze zmianami kodu źródłowego, zapewniając spójność z publicznym wycinkiem.
- **Pokrycie testami**: rozszerzenie przypadków `ForgeMind.PublicTests` i `ForgeMindServerTest` (polecenia mostu, skanowanie, scenariusze brzegowe SPK).

## Średni okres

- **Rozszerzenie adapterów silników**: na bazie istniejącego adaptera UE (5.0–5.8) zbadanie integracji innych silników / edytorów (analogiczny protokół mostu).
- **Wzmocnienie skanowania i knowledge**: poprawa pokrycia parsowania DocumentEngine dla headerów / blueprintów / plików reguł oraz optymalizacja wydajności i aktualizacji przyrostowej pakowania SPK.
- **Ekologia wtyczek**: udoskonalenie API wtyczek i specyfikacji manifestu, wsparcie bogatszych wtyczek stron trzecich.

## Długi okres

- **Otwartość dokumentacji użycia DSL**: gdy komercyjne DSL będzie stabilne, wg strategii produktu może zostać opublikowana część dokumentacji użycia, przykładów i treści dydaktycznych; implementacja DSL, Translator, reguły IR i logika generowania assetów pozostają modułami komercyjnymi.
- **Platforma**: pozycjonowanie ForgeMind jako wielosilnikowej platformy sterowania MCP, konsolidacja protokołu i warstw abstrakcji niezależnych od silnika.

> Kierunek wersji komercyjnej (system autoryzacji, wsparcie enterprise itd.) nie jest pogłębiany w tej publicznej mapie drogowej: skontaktuj się z wydawcą.
