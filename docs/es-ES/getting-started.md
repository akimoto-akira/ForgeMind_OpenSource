# Inicio rápido de ForgeMind

ForgeMind es una **plataforma de control de motores de juego con interfaz MCP**: a través del protocolo MCP, expone a los clientes de IA la gestión de proyectos de Unreal Engine, la lectura de activos y las operaciones de ejecución/compilación/editor. La implementación actual es UE; la plataforma es extensible. La versión de código abierto incluye el conjunto de herramientas de motor y las funciones de puente; la versión comercial añade generación de activos DSL, entre otros (§5).

Todos los pasos de esta guía se pueden completar con la **porción de código abierto** (`OpenSource\` + `UEProject\` + `tests\`).

## 1. Requisitos de entorno

- SDK de .NET (para compilar host y complementos).
- Unreal Engine 5.0–5.8 (incluido el editor). El proyecto de ejemplo es `UEProject\ForgeMind_5_0\ForgeMind_5_0\ForgeMind_5_0.uproject`.
- Cliente compatible con MCP (Claude Desktop / Cursor / cliente personalizado).

## 2. Compilar servidor y complementos

```powershell
# Host
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# Complementos (después de compilar, colocar DLL + .manifest.json del mismo nombre en el directorio plugins del host)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
```

> No compile `ForgeMind.sln` completo directamente (contiene referencias a proyectos comerciales; el directorio comercial no está incluido en la porción). También puede usar los artefactos de compilación proporcionados por el distribuidor para los complementos.

## 3. Ejecución

1. **Iniciar el servidor** (por defecto residente HTTP, puerto 8765):

   ```powershell
   dotnet run --project OpenSource\ForgeMindServer\ForgeMindServer.csproj
   # O ejecutar el artefacto compilado ForgeMindServer.exe; ajustable con --port N / --stdio / FORGEMIND_PORT
   ```

2. **Abrir el editor UE y el proyecto de ejemplo**: ejecute `ForgeMind_5_0.uproject` en UE; el complemento `ForgeMindForUE` en el editor inicia automáticamente el cliente de puente y se conecta al host.

3. **Configurar el cliente**: agregue el servidor en el cliente MCP:

   ```json
   { "mcpServers": { "forgemind": { "url": "http://127.0.0.1:8765/mcp" } } }
   ```

   Después de la conexión, se mostrará la lista de herramientas (4 integradas + inyección de complementos).

## 4. Primer uso

- Use `engine` para ver la información del motor actual;
- Use `project` / `project_files` para ver el proyecto de ejemplo y sus archivos;
- Use `launch` para iniciar el editor, `editor` para operar el editor;
- Use `asset` para leer la estructura de activos del proyecto;
- Use `knowledge` para buscar conocimiento del proyecto.

> Las tareas de larga duración (launch/build, etc.) se ejecutan de forma asíncrona. Consulte los resultados con `get_task_result`, vea las tareas en curso con `list_executing_tasks` y limpie con `cleanup_tasks`.

## 5. Versión comercial (DSL)

El módulo Commercial es una versión comercial independiente y no está incluido en el repositorio OpenSource. La versión comercial ofrece **generación de activos DSL**: escriba Blueprints, materiales, etc. en texto legible y conviértalos en activos de motor. Las herramientas DSL específicas, la función de conversión de texto y los detalles de sintaxis siguen la documentación de la versión comercial. La funcionalidad de licencias comerciales también pertenece a Commercial. OpenSource no incluye herramientas DSL.

## 6. Siguientes pasos

- Arquitectura y mecanismos: `architecture.md`
- Lista de herramientas y parámetros: `tools-reference.md`
- Desarrollo y contribución: `development-guide.md` / `contributing.md`
- Preguntas frecuentes: `troubleshooting.md`
