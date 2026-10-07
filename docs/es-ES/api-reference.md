# Referencia de API de ForgeMind

Este documento describe la parte pública de las interfaces externas de ForgeMind: conexión de protocolo MCP, protocolo de puente, contratos de herramientas y mecanismo de tareas de larga duración. Las funciones comerciales (DSL) se describen solo a nivel general en §6.

## 1. Conexión MCP

- **Modo HTTP** (por defecto): `http://127.0.0.1:8765/mcp`, Streamable HTTP (compatible con SSE anterior). El proceso del host es residente y se puede usar para múltiples sesiones de clientes con una sola conexión.
- **Modo stdio**: `ForgeMindServer.exe --stdio`, ejecuta un proceso por cliente.
- Puerto: `--port N` > variable de entorno `FORGEMIND_PORT` > valor predeterminado 8765.

Métodos MCP estándar admitidos: `initialize`, `tools/list`, `tools/call`, `notifications/cancelled`, etc. El esquema de las herramientas lo proporciona `ITool.GetParameterSchema()`.

## 2. Contrato de herramientas

Las herramientas del servidor implementan una interfaz unificada (`OpenSource\ForgeMindServer\Compat\ITool.cs`):

- `Name` / `DisplayName`: identificador y nombre para mostrar de la herramienta;
- `Description`: texto de descripción para el cliente de IA;
- `GetParameterSchema()`: declaración de parámetros (estilo JSON Schema);
- `Execute(Guid callerId, Dictionary<string, object> parameters)`: devuelve `ToolResult` (resultado de éxito o mensaje de error) tras la ejecución.

Flujo de llamada: cliente `tools/call` → el host identifica `ITool` → validación de parámetros → `Execute` → devolución de resultados. Las herramientas que requieren funciones del motor emiten comandos internamente a través del puente TCP.

## 3. Protocolo de puente (host ↔ adaptador de motor)

- Transporte: TCP local. Formato de trama = prefijo de longitud de 4 bytes + sobre JSON (`BridgeMessage`, ver `Bridge\BridgeMessage.cs` / `FrameCodec.cs`).
- Descubrimiento y autenticación: el adaptador lee `%USERPROFILE%\.forgemind\bridge.json` (incluye host, puerto y credenciales), completa la conexión → protocolo de enlace → bucle de mensajes.
- Semántica de comandos: la solicitud incluye nombre de comando y parámetros. El servidor enruta el comando a la implementación del adaptador; la respuesta incluye código de estado, mensaje y datos. Los comandos del lado del motor se ejecutan en el hilo principal del motor (hilo de juego de UE).
- Ciclo de vida: el host comienza a escuchar el puente al iniciarse. El adaptador se reconecta con retroceso exponencial al desconectarse.

## 4. Mecanismo de tareas de larga duración

- Las tareas de larga duración (`launch`, `build`, etc.) se ejecutan de forma asíncrona y no bloquean la llamada de herramientas.
- Herramientas integradas relacionadas:
  - `get_task_result`: consulta el resultado de la tarea;
  - `list_executing_tasks`: lista las tareas en curso;
  - `cleanup_tasks`: limpia los registros de tareas.
- `LongTaskManager` interno gestiona el ciclo de vida de las tareas (`LongTaskManager.cs` / `LongTaskTools.cs`).

## 5. Herramientas integradas (incluidas en el host, sin complementos)

| Herramienta | Descripción |
|---|---|
| `settings` | Lectura/escritura de configuración del servidor (`McpSettingsManager`, persistencia inmediata) |
| `get_task_result` | Consulta de resultados de tareas de larga duración |
| `list_executing_tasks` | Consulta de tareas en curso |
| `cleanup_tasks` | Limpieza de registros de tareas |

Para la lista completa de herramientas inyectadas por complementos, consulte `tools-reference.md`.

## 6. Funciones comerciales (DSL, visión general)

- OpenSource proporciona **contratos de extensión de DSL** (interfaces y registros de `ParserFactory` / `ITranslator` / `TranslatorRegistry` / `IIrConverter` / `IrConverterRegistry` / `DiagnosticReporter` en `ForgeMindServer\DSL\` están disponibles públicamente). La implementación específica de la sintaxis DSL, Parser, Translator, reglas IR y lógica de generación de activos pertenece a Commercial y no está en OpenSource.
- Las herramientas DSL las inyecta el complemento comercial. Los nombres y API específicos siguen la documentación de la versión comercial. La lista de herramientas OpenSource no incluye herramientas DSL.

| Función | Atribución |
|---|---|
| Interfaz de extensión DSL | OpenSource ✅ |
| Parser DSL | Commercial 🔒 |
| Translator DSL | Commercial 🔒 |
| Reglas IR | Commercial 🔒 |
| Lógica de generación de activos | Commercial 🔒 |

## 7. Notas de compatibilidad

- El formato de trama del protocolo de puente y el esquema de herramientas siguen este documento. Los cambios destructivos publicarán un aviso de migración con anticipación.
