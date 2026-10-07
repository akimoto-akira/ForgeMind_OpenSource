# Referencia de herramientas de ForgeMind

Todas las herramientas se dividen en 'integradas en el host' e 'inyectadas por complementos'. La versión OpenSource actual incluye 13 herramientas MCP. El complemento comercial puede proporcionar herramientas adicionales (generación de activos DSL, etc., ver §3; no incluidas en la lista de código abierto).

## 1. Herramientas integradas en el host

El host (`OpenSource\ForgeMindServer`) incluye las siguientes herramientas, sin necesidad de complementos:

| Herramienta | Descripción |
|---|---|
| `settings` | Lectura/escritura de configuración del servidor (`McpSettingsManager`, persistencia inmediata) |
| `get_task_result` | Consulta de resultados de tareas de larga duración (launch / build, etc.) |
| `list_executing_tasks` | Lista de tareas de larga duración en curso |
| `cleanup_tasks` | Limpieza de registros de tareas finalizadas |

## 2. Herramientas inyectadas por complementos

Los complementos inyectan herramientas a través de `PluginContext.RegisterTool` en `OnLoad` y se muestran después de la conexión del cliente. Después de compilar, coloque el DLL del complemento + `.manifest.json` del mismo nombre en el directorio `plugins\` del host (o el directorio especificado por `FORGEMIND_PLUGINS_DIR`).

### ForgeMind.OfflineEngineKit (7)

| Herramienta | Descripción |
|---|---|
| `engine` | Consulta de motores instalados localmente (Launcher / compilación desde fuente, incluye versión y nombre de aplicación) |
| `project` | Consulta/gestión de proyectos UE (identificación de archivos de proyecto, consulta de información del proyecto) |
| `project_files` | Listado de archivos del proyecto (filtro por directorio/tipo) |
| `switch_version` | Cambio de versión del proyecto a un motor especificado (a través de UnrealVersionSelector) |
| `launch` | Ejecución del editor o ejecutable empaquetado (admite configuración de depuración) |
| `build` | Compilación del proyecto (objetivo predeterminado / objetivo Editor) |
| `editor` | Operaciones dentro del editor (escena, viewport, activos, etc., emitidas a través del puente) |

Los esquemas de parámetros siguen `GetParameterSchema()` del código fuente de cada herramienta (`OpenSource\ForgeMind.OfflineEngineKit\*.cs`). Algunas operaciones del motor están sujetas a los permisos de `ServerPermissionChecker`.

### ForgeMind.Assets (1)

| Herramienta | Descripción |
|---|---|
| `asset` | Lectura sin conexión de la estructura `.uasset` (nombre, objetos, propiedades, gráficos de Blueprint, consulta por gráfico/propiedad) |

La implementación se basa en `UAsset\UAssetSerializer` / `BlueprintGraphReader`, etc. (`OpenSource\ForgeMind.Assets\`).

### ForgeMind.Knowledge (1)

| Herramienta | Descripción |
|---|---|
| `knowledge` | Búsqueda de conocimiento: consulta de elementos de conocimiento del proyecto/motor (fuentes: fuente de carpeta `FolderKnowledgeSource`, fuente de paquete de índice SPK `IndexPackSource`) |

Implementación en `OpenSource\ForgeMind.Knowledge\Knowledge\`.

## 3. Herramientas de la versión comercial (visión general)

La versión comercial puede añadir generación de activos DSL y herramientas de consulta relacionadas, inyectadas por el complemento comercial. Los nombres, API y funcionalidad DSL específicos siguen la documentación de la versión comercial. OpenSource no incluye herramientas DSL.

## 4. Convenciones de llamada de herramientas

- Las herramientas de tareas de larga duración devuelven un ID de tarea. Consulte los resultados con `get_task_result`.
- Los fallos de herramientas devuelven el mensaje de error de `ToolResult` (se puede mostrar directamente al cliente).
- Las descripciones y esquemas de parámetros de las herramientas son exactamente lo que se muestra en `tools/list` de MCP y coinciden con el código fuente.
