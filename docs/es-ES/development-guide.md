# Guía de desarrollo de ForgeMind

Para desarrolladores que quieran ampliar ForgeMind: cómo compilar, cómo depurar, añadir herramientas MCP, añadir adaptadores de motor y ejecutar pruebas. Este documento se basa en el código real de la **porción pública** (`OpenSource\`, `UEProject\`, `tests\`).

## 1. Resumen del proyecto

| Proyecto | Responsabilidad |
|---|---|
| `OpenSource\ForgeMindServer` | Host del servidor MCP: 4 herramientas integradas, inicio del puente TCP, carga de complementos |
| `OpenSource\ForgeMind.OfflineEngineKit` | Complemento: herramientas engine / project / project_files / switch_version / launch / build / editor |
| `OpenSource\ForgeMind.Assets` | Complemento: herramienta asset (lectura sin conexión de `.uasset`) |
| `OpenSource\ForgeMind.Knowledge` | Complemento: herramienta knowledge (búsqueda de conocimiento) |
| `OpenSource\ForgeMind.EngineScanner` | Escáner de línea de comandos: enumeración de motores y empaquetado `.spk` |
| `OpenSource\ForgeMind.Scan` / `.Scan.Abstractions` / `.Scan.Unreal` | Índice de escaneo: abstracción, empaquetado, implementación UE (DocumentEngine) |
| `OpenSource\ForgeMind.Speedy` | Formato de empaquetado SPK (archivo de paquete de alto rendimiento) |
| `tests\ForgeMind.PublicTests` | Pruebas unitarias públicas (independientes del motor) |
| `tests\ForgeMindServerTest` | Pruebas de integración del servidor (requieren editor UE) |
| `UEProject\ForgeMind_5_0` | Proyecto UE de ejemplo (5.0–5.8) |

## 2. Compilación

```powershell
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# Proyectos de complementos y bibliotecas: ver contributing.md §3 (no compile ForgeMind.sln completo directamente; incluye referencias a proyectos comerciales)
```

- Punto de entrada del host: `OpenSource\ForgeMindServer\Program.cs` (HTTP residente por defecto `127.0.0.1:8765`, conmutable con `--stdio`, puerto sobrescrito con `--port` / `FORGEMIND_PORT`).
- Registros: `LogManager`. Las herramientas devuelven `ToolResult` (con mensajes de éxito/error).

## 3. Mecanismo de complementos y adición de herramientas MCP

El host solo incluye 4 herramientas integradas: `settings`, `get_task_result`, `list_executing_tasks`, `cleanup_tasks`. El resto lo inyectan los **complementos en el directorio plugins**:

- Directorio de complementos: `<directorio del host>\plugins\` (sobrescrito por la variable de entorno `FORGEMIND_PLUGINS_DIR`).
- Diseño del paquete de complemento: DLL principal + `.manifest.json` del mismo nombre + dependencias en `plugins\<nombre del complemento>\`. También se admiten DLL sueltos en la raíz (despliegue manual).
- El `PluginLoader` del host escanea complementos → inyecta `PluginContext` (tabla de herramientas, registro de motores, puente) → el complemento `OnLoad` registra herramientas con `RegisterTool`.
- Integridad de complementos: `PluginIntegrityVerifier` / `PluginSigningKeys` verifican el manifiesto y la firma del complemento. Los complementos de confianza pueden ampliar servicios con ServiceOverride (§7).

Para añadir una nueva herramienta MCP:

1. Implemente el contrato `ITool` (`OpenSource\ForgeMindServer\Compat\ITool.cs`):
   - `Name` / `DisplayName`: nombre y nombre para mostrar de la herramienta;
   - `Description`: descripción para el cliente de IA;
   - `GetParameterSchema()`: declaración de parámetros estilo JSON Schema;
   - `Execute(Guid callerId, Dictionary<string, object> parameters)`: devuelve `ToolResult`.
2. Cree un nuevo proyecto de complemento (o únase al complemento existente OfflineEngineKit), implemente `IPlugin` y llame a `RegisterTool` en `OnLoad`.
3. Coloque el `.manifest.json` del mismo nombre en el mismo directorio (descripción del complemento / declaración de dependencias).
4. Si necesita funciones del lado del motor, envíelas como comandos de puente e implemente manejadores de comandos en el adaptador (§5).
5. Escriba casos de prueba (§6) y actualice `tools-reference.md`.

## 4. Cómo añadir un adaptador de motor

ForgeMind acepta la lógica específica del motor a través de 'adaptadores'. Para añadir un nuevo motor, siga el mismo protocolo de puente (prefijo de longitud de 4 bytes + sobre JSON, ver `api-reference.md`):

1. Implemente el cliente de puente: lea `%USERPROFILE%\.forgemind\bridge.json` para completar el descubrimiento y la autenticación, conexión → protocolo de enlace → bucle de mensajes.
2. Registre manejadores de lista blanca por nombre de comando. Todos los comandos se ejecutan en el hilo principal del motor (equivalente al hilo de juego de UE).
3. Implemente las funciones del lado del motor (lectura/escritura de activos, operaciones de proyecto, etc.) y asigne a los resultados de las herramientas del servidor.
4. Añada casos de prueba y documentación para ese motor.

La primera implementación es el adaptador UE `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\` (complemento de módulo Editor, cliente de puente + aplicador IR).

## 5. Comandos de puente del lado del motor (implementación UE actual)

- El complemento `ForgeMindForUE` del lado del adaptador incluye el cliente de puente; los manejadores de comandos se ejecutan en el hilo de juego.
- Las operaciones a nivel de activos pueden reutilizar los manejadores de dominio IR dentro del complemento, evitando duplicar la lógica del editor.
- Depuración: categoría de registro del editor UE `LogForgeMindBridge`. Al desconectarse, se reconecta con retroceso exponencial.

## 6. Ejecución de pruebas

### Pruebas unitarias públicas (sin editor)

```powershell
dotnet run --project tests\ForgeMind.PublicTests\ForgeMind.PublicTests.csproj
```

Cubren ida y vuelta de empaquetado SPK, SPK multiversión, proveedores de datos de escaneo, etc. (`EmbeddedSpkTests` / `ScanPackRoundTripTests` / `SpkMultiVersionTests` / `SpkScanDataProviderTests`).

### Pruebas de integración del servidor (requieren editor UE + proyecto de ejemplo)

```powershell
# Predeterminado: precompila host y complementos, los coloca en el directorio plugins, inicia el host para cargar complementos y ejecuta todos los ITestCase por reflexión
ForgeMindServerTest.exe

# Filtro: group + order
ForgeMindServerTest.exe [group] [order]          # o --Test [group] [order]
# Omitir precompilación
ForgeMindServerTest.exe --NoBuild
```

Mecanismo:

- Precompilación: primero compila `ForgeMindServer.csproj`, descubre proyectos de complementos con `.manifest.json` del mismo nombre bajo directorios `ForgeMind.*`, los compila individualmente y los coloca en `<directorio de entrada>\plugins\<nombre del complemento>\`.
- Ejecución: descubre `ITestCase` por reflexión, ordena por `Group` / `Order`, **fail-fast**. 'Inicio de editor / cierre de editor' también son casos normales (`Order=0` inicia, `Order=int.MaxValue` cierra), con sesión compartida dentro del mismo Group.
- Los casos invocan herramientas directamente con `ITool.Execute` (sin pasar por el protocolo MCP). Las herramientas se comunican con UE internamente a través del puente.
- Salida: después de la ejecución, genera un informe JSON y un resumen legible (el directorio de salida se muestra en el mensaje de resultados).
- Activos de prueba: 30+ `Blueprint_*_Tests.cs` bajo `Task\UE5_0\` (dominio de Blueprint: variables / funciones / eventos / materiales / macros / interfaces, etc.) + `Task\Base\Engine_List_Tests.cs`; `BlueprintTestHelper` encapsula comandos de puente, despacho de texto y comparación de instantáneas IR (evita modificaciones erróneas).

> Nota: los casos de prueba relacionados con la 'ruta de texto (DSL)' dependen de las funciones DSL comerciales. Las pruebas de integración en la porción pública usan principalmente la ruta de comandos de puente, siguiendo los casos reales de `Task\` en la porción.

## 7. Licencias y funciones comerciales (visión general)

- `OpenAuthorizationService` predeterminado (`ForgeMindServer\Authorization\Open\`): funciones fijas de la comunidad, no analiza ningún archivo de licencia.
- La licencia comercial sustituye la implementación de `IAuthorizationService` con ServiceOverride del Manifest del complemento (host `ApplyServiceOverride`). La porción pública no incluye ese complemento comercial; solo se mantiene el mecanismo de sustitución (el host no conoce el concepto 'comercial', solo sustituye por tipos abstractos).

## 8. Convenciones de codificación

- Los comentarios de cabecera de archivo incluyen identificador de licencia SPDX y aviso de derechos de autor (Apache-2.0).
- No haga referencia a módulos comerciales. Los nuevos complementos siguen los límites de porción de `contributing.md`.
