# Arquitectura de ForgeMind

ForgeMind es una **plataforma de control de motores de juego con interfaz MCP**: el servidor (host) expone herramientas a los clientes de IA a través del protocolo MCP; las funciones de las herramientas las inyectan principalmente los complementos. En el lado del motor, el complemento adaptador acepta operaciones a través del puente TCP local. UE es la implementación actual; la plataforma se puede extender a otros motores. Este documento se basa en el código visible en la **porción pública**; las funciones comerciales se describen solo a nivel general en §7.

## 1. Panorama general

```
┌─────────────┐   MCP (HTTP/stdio)   ┌──────────────────────┐
│ Cliente de IA │ ◄────────────────► │ ForgeMindServer (host) │
└─────────────┘                     │  • Herramientas integradas ×4 │
                                    │  • PluginLoader             │
                                    │  • Servidor de puente TCP   │
                                    │  • Registro de motores      │
                                    └──────────┬───────────┘
                                               │ Puente TCP local (longitud de 4 bytes + sobre JSON)
                                    ┌──────────▼───────────┐
                                    │ Complemento adaptador UE │
                                    │ (ForgeMindForUE)        │
                                    └──────────────────────┘
```

- **Host** (`OpenSource\ForgeMindServer`): el cuerpo del servidor MCP.
- **Complementos** (`OpenSource\ForgeMind.*Plugin`): registran herramientas a través de `IPlugin`. El host solo incluye un conjunto mínimo de herramientas.
- **Adaptador de motor** (`UEProject\...\Plugins\ForgeMindForUE`): complemento dentro del editor. Implementa las funciones del lado del motor como servidor del puente.
- **Escaneo/empaquetado** (`ForgeMind.Scan.Abstractions`, `ForgeMind.Scan`, `ForgeMind.Scan.Unreal`, `ForgeMind.Speedy`, `ForgeMind.EngineScanner`): proporcionan el índice de funciones sin conexión del motor y el empaquetado SPK.
- **Proyecto de ejemplo** (`UEProject\ForgeMind_5_0`): para pruebas y depuración conjunta.

## 2. Host (ForgeMindServer)

- **Punto de entrada**: `Program.cs` inicia por defecto un servicio HTTP residente en `127.0.0.1:8765` (Streamable HTTP + compatibilidad SSE). `--stdio` cambia al modo spawn-per-client. El puerto se puede sobrescribir con `--port` / `FORGEMIND_PORT`.
- **Herramientas integradas** (tabla de herramientas de `ForgeMindServerHost`, solo 4):
  - `settings` (`SettingsTool`, lectura/escritura de configuración);
  - `get_task_result` / `list_executing_tasks` / `cleanup_tasks` (consulta y limpieza de tareas de larga duración).
- **Carga de complementos**: `PluginLoader` escanea `plugins\` (por defecto `<directorio del host>\plugins\`, sobrescrito por `FORGEMIND_PLUGINS_DIR`) e inyecta la tabla de herramientas, el registro de motores y el puente a través de `PluginContext`. El complemento `OnLoad` llama a `RegisterTool` para inyectar herramientas. Los paquetes de complementos admiten dos diseños: 'subdirectorio = paquete de complemento' (DLL principal + manifest del mismo nombre + dependencias) y 'DLL sueltos en la raíz'. La carga es diferida (se activa al primer acceso a la tabla de herramientas); si falla, solo se omite ese complemento y el host no se ve afectado.
- **Registro de motores**: `EngineRegistry` enumera los motores locales al inicio (prioriza `UE_x.y` de instalaciones Launcher, con respaldo a compilaciones desde fuente) e inyecta una instantánea de solo lectura a los complementos.
- **Servidor de puente TCP**: `ForgeMindServerBridgeServer`. Para el descubrimiento y conexión del adaptador de motor (§5).

## 3. Complementos e inyección de herramientas

| Complemento | Herramientas | Responsabilidad |
|---|---|---|
| `ForgeMind.OfflineEngineKit` | `engine` / `project` / `project_files` / `switch_version` / `launch` / `build` / `editor` | Gestión de motores y proyectos, ejecución/compilación/operaciones de editor |
| `ForgeMind.Assets` | `asset` | Lectura sin conexión de la estructura `.uasset` (`UAssetArchive` / `BlueprintGraphReader`, etc.) |
| `ForgeMind.Knowledge` | `knowledge` | Búsqueda de conocimiento (fuente de carpeta / fuente de paquete de índice, `KnowledgeStore`) |
| `ForgeMind.EngineScanner` | (herramienta CLI, no MCP) | Enumeración de motores → escaneo `UnrealScanner` → empaquetado `.spk` con `ScanResultPacker` |

El host y los complementos interactúan mediante el contrato `ITool` (`Compat\ITool.cs`): nombre/descripción/esquema de parámetros/`Execute` devuelve `ToolResult`. `ForgeMindServerTest` también invoca herramientas directamente a través de `ITool` (sin pasar por el protocolo MCP) para verificar el comportamiento.

## 4. Escaneo y SPK (base de funciones sin conexión)

- `ForgeMind.Scan.Abstractions`: abstracciones `IScanner` / `IScanDataProvider` / `ScanEntry` / `ScanManifest` / `ScanSignatures`, etc.
- `ForgeMind.Scan.Unreal`: `UnrealScanner` + `DocumentEngine` (`Ue5DocumentEngine`, `HeaderScanner`, `RulesCompiler`, `UAssetScanner`, `ScanIndexStore`, etc.) — analiza archivos de cabecera UE, archivos de reglas y activos de Blueprint en índices de escaneo. `ScanIndexStore` es un índice en memoria en tiempo de ejecución y no se persiste en disco. Esto es **diferente del antiguo ScanCache** — el pipeline actual no utiliza la precarga/caché distribuida de ScanCache (solo quedan rastros de compatibilidad histórica en el código fuente; no constituyen el mecanismo actual).
- `ForgeMind.Scan`: `ScanPackLoader` / `ScanPackReader` / `ScanResultPacker` / `SpkScanDataProvider` ensamblan y leen paquetes de escaneo.
- `ForgeMind.Speedy`: `SpeedyPack` proporciona un formato de empaquetado de alto rendimiento (`FilePackReader` / `IPackReader` / `IPackWriter` / `IPackTransaction`, con CRC interno, mapeo de directorios, registro de escritura anticipada `WriteAheadLog`, etc.).
- Uso: `ForgeMind.EngineScanner <spkPath>` genera `.spk`. Funciones como knowledge lo consumen a través de `SpkScanDataProvider`.

## 5. Protocolo de puente y adaptador de motor

- Puente: TCP local. Los mensajes usan prefijo de longitud de 4 bytes + sobre JSON (`BridgeMessage` / `BridgeSession` / `FrameCodec`). El adaptador primero lee `%USERPROFILE%\.forgemind\bridge.json` para completar el descubrimiento y la autenticación, luego conexión → protocolo de enlace → bucle de mensajes.
- El lado del servidor despacha por nombre de comando a la implementación del adaptador. Los manejadores de comandos se ejecutan en el hilo principal del motor (hilo de juego de UE) para garantizar la consistencia con el estado del editor.
- El adaptador de motor (lado UE) está en `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\Source\ForgeMindForUE\`, un módulo Editor que incluye el cliente de puente y el aplicador IR. El proyecto UE de ejemplo es compatible con 5.0–5.8.
- Para añadir un nuevo motor, solo hay que implementar un cliente de puente y manejadores de comandos isomórficos (`development-guide.md` §4). No se necesitan cambios en el host ni en los complementos.

## 6. Licencias y seguridad (parte pública)

- Autorización predeterminada: `OpenAuthorizationService` (`Authorization\Open\`) — funciones fijas de la comunidad, **no analiza archivos de licencia**.
- Seguridad de complementos: `PluginIntegrityVerifier` (verificación de integridad de manifiestos), `PluginSigningKeys` (verificación de firmas); `ManifestLoader` analiza los manifiestos de los complementos y admite ServiceOverride (sustitución de servicios del host por tipos abstractos, p. ej., `IAuthorizationService`).
- Autenticación del puente: la conexión del adaptador debe leer las credenciales locales de `bridge.json` para completar el protocolo de enlace (`security.md`).

## 7. Funciones comerciales (visión general)

- **Lenguaje DSL y traductores de dominio** (código cerrado): función comercial que genera activos de motor a partir de texto legible. La porción pública solo incluye la capa de marco DSL (`ParserFactory`, `ITranslator`, `TranslatorRegistry`, `IIrConverter`, `IrConverterRegistry`, `DiagnosticReporter`, interfaces y registros en `ForgeMindServer\DSL\`); la sintaxis específica, los traductores de dominio y las reglas IR los proporcionan los módulos comerciales y no están en la porción.
- **Sistema de licencias comerciales** (código cerrado): incluye emisión de licencias, verificación de licencias, servicio de licencias y vinculación de entorno opcional. La implementación específica, los algoritmos de firma, el formato de archivos de licencia y la lógica del lado del servidor no pertenecen a la porción pública y solo se presentan externamente como 'provisión de funciones adicionales'.

> La versión OpenSource ofrece un conjunto completo de herramientas de código abierto y funciones de puente. La versión Commercial se basa en el mismo host, mecanismo de complementos y protocolo de puente para extender funciones comerciales (generación de activos DSL, licencias comerciales, soporte empresarial).

## 8. Tabla de límites de funciones

| Función | Atribución |
|---|---|
| MCP / ForgeMindServer | OpenSource ✅ |
| OfflineEngineKit | OpenSource ✅ |
| Assets | OpenSource ✅ |
| Knowledge | OpenSource ✅ |
| Scan / SPK | OpenSource ✅ |
| EngineScanner | OpenSource ✅ |
| Speedy | OpenSource ✅ |
| Interfaz de extensión DSL | OpenSource ✅ |
| Parser DSL | Commercial 🔒 |
| Translator DSL | Commercial 🔒 |
| Reglas IR | Commercial 🔒 |
| Generador de activos DSL | Commercial 🔒 |
| IAuthorizationService | OpenSource ✅ |
| Implementación de licencias comerciales | Commercial 🔒 |
| License Generator | Commercial 🔒 |
| License Server | Commercial 🔒 |
| Claves / sistema de firmas | Commercial 🔒 |
| Detalles internos del protocolo de licencias | Commercial 🔒 |
