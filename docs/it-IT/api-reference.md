# Riferimento API ForgeMind

Questo documento descrive la parte pubblica delle interfacce esterne di ForgeMind: connessione MCP, protocollo bridge, contratti strumenti e meccanismo attività lunghe. Le funzioni commerciali (DSL) sono descritte solo a livello generale al §6.

## 1. Connessione MCP

- **Modalità HTTP** (default): `http://127.0.0.1:8765/mcp`, Streamable HTTP (compatibile con il vecchio SSE). Il processo host è residente; una connessione serve più sessioni client.
- **Modalità stdio**: `ForgeMindServer.exe --stdio`, un processo separato viene avviato per ogni client.
- Porta: `--port N` > variabile d'ambiente `FORGEMIND_PORT` > default 8765.

Metodi MCP standard supportati: `initialize`, `tools/list`, `tools/call`, `notifications/cancelled`, ecc. Lo schema degli strumenti è fornito da `ITool.GetParameterSchema()`.

## 2. Contratto strumenti

Gli strumenti del server implementano un'interfaccia unificata (`OpenSource\ForgeMindServer\Compat\ITool.cs`):

- `Name` / `DisplayName`: identificatore e nome visualizzato dello strumento;
- `Description`: testo descrittivo per il client AI;
- `GetParameterSchema()`: dichiarazione dei parametri (stile JSON Schema);
- `Execute(Guid callerId, Dictionary<string, object> parameters)`: restituisce `ToolResult` dopo l'esecuzione (risultato di successo o messaggio di errore).

Flusso di chiamata: client `tools/call` → l'host identifica `ITool` → validazione parametri → `Execute` → restituzione del risultato. Gli strumenti che richiedono funzioni motore inviano internamente comandi tramite il bridge TCP.

## 3. Protocollo bridge (host ↔ adattatore motore)

- Trasporto: TCP locale. Formato frame = prefisso di lunghezza 4 byte + envelope JSON (`BridgeMessage`, vedi `Bridge\BridgeMessage.cs` / `FrameCodec.cs`).
- Scoperta e autenticazione: l'adattatore legge `%USERPROFILE%\.forgemind\bridge.json` (contiene host, porta e credenziali), esegue connessione → handshake → ciclo di messaggi.
- Semantica dei comandi: una richiesta contiene il nome del comando e i suoi parametri. Il server instrada il comando all'implementazione dell'adattatore; la risposta contiene codice di stato, messaggio e dati. I comandi lato motore vengono eseguiti nel thread principale del motore (game thread UE).
- Ciclo di vita: l'host inizia ad ascoltare il bridge all'avvio. L'adattatore si riconnette con backoff esponenziale in caso di disconnessione.

## 4. Meccanismo attività lunghe

- Le attività lunghe (`launch`, `build`, ecc.) vengono eseguite in modo asincrono e non bloccano la chiamata dello strumento.
- Strumenti integrati correlati:
  - `get_task_result`: interrogazione del risultato dell'attività;
  - `list_executing_tasks`: elenco delle attività in corso;
  - `cleanup_tasks`: pulizia dei record delle attività.
- Internamente, `LongTaskManager` gestisce il ciclo di vita delle attività (`LongTaskManager.cs` / `LongTaskTools.cs`).

## 5. Strumenti integrati (parte dell'host, senza plugin)

| Strumento | Descrizione |
|---|---|
| `settings` | Lettura/scrittura configurazione server (`McpSettingsManager`, persistenza immediata) |
| `get_task_result` | Interrogazione del risultato delle attività lunghe |
| `list_executing_tasks` | Interrogazione delle attività in corso |
| `cleanup_tasks` | Pulizia dei record delle attività |

L'elenco completo degli strumenti iniettati dai plugin è in `tools-reference.md`.

## 6. Funzioni commerciali (DSL, panoramica)

- OpenSource fornisce i **contratti di estensione DSL** (interfacce e registri `ParserFactory` / `ITranslator` / `TranslatorRegistry` / `IIrConverter` / `IrConverterRegistry` / `DiagnosticReporter` in `ForgeMindServer\DSL\` sono pubblici). Le implementazioni concrete della sintassi DSL, Parser, Translator, regole IR e la logica di generazione asset appartengono a Commercial e non sono in OpenSource.
- Gli strumenti DSL sono iniettati dal plugin commerciale. I nomi e le API concrete seguono la documentazione commerciale. L'elenco strumenti OpenSource non contiene alcuno strumento DSL.

| Funzione | Appartenenza |
|---|---|
| Interfacce di estensione DSL | OpenSource ✅ |
| Parser DSL | Commercial 🔒 |
| Translator DSL | Commercial 🔒 |
| Regole IR | Commercial 🔒 |
| Logica di generazione asset | Commercial 🔒 |

## 7. Note di compatibilità

- Il formato frame del protocollo bridge e gli schemi degli strumenti seguono questo documento. Le modifiche che rompono la compatibilità saranno annunciate in anticipo con un avviso di migrazione.
