# Comunicazione pubblica di ForgeMind

## Presentazione in una frase

ForgeMind è una piattaforma di controllo per motori di gioco con interfaccia MCP: espone ai client AI la gestione dei progetti Unreal Engine, la lettura degli asset, l'avvio / la build / le operazioni nell'editor, consentendo all'AI di operare sul motore come se usasse uno strumento.

## Punti salienti

- **Integrazione nativa MCP**: protocollo MCP standard (HTTP / stdio), i principali client AI si connettono e usano subito.
- **Slice open source auto-compilabile**: il repository pubblico contiene host, tutti i plugin open source, il progetto UE di esempio e i test; con Apache-2.0 è possibile compilare ed estendere in autonomia.
- **Architettura ad adattatori per motore**: host e motore sono disaccoppiati tramite bridge TCP locale, estendibile a motori diversi da UE.
- **Lettura offline degli asset**: parsing della struttura `.uasset` senza aprire l'editor, combinato con l'indice di scansione (SPK) per fornire capacità di knowledge retrieval.
- **Iniezione delle capacità tramite plugin**: l'host integra solo un set minimale di strumenti; le capacità sono estese dai plugin, con confini chiari e riducibili.

## Versione commerciale (in una frase)

La versione commerciale offre, sulla base di OpenSource, generazione di asset DSL, autorizzazione commerciale e supporto enterprise; per i dettagli sulla sintassi DSL e le modalità di autorizzazione, contattare il publisher.

## Regole di comunicazione pubblica

- Non promettere funzionalità non rilasciate; la roadmap fa fede a `roadmap.md`.
- Non denigrare strumenti simili; non divulgare all'esterno i dettagli interni dei moduli commerciali (algoritmi di autorizzazione, regole IR interne del DSL, ecc.).
- Per i comportamenti relativi al codice sorgente, fare sempre riferimento allo slice pubblico; gli elementi incerti vanno contrassegnati con «da confermare».
