# Solución de problemas de ForgeMind

Problemas comunes y soluciones (basados en los mecanismos reales de la porción pública).

## 1. Fallos de compilación

**Al compilar `ForgeMind.sln` directamente: 'no se encuentra el proyecto Commercial\...'**
Causa: `ForgeMind.sln` incluye entradas de referencia a proyectos comerciales (`Commercial\ForgeMindLicenseGenerator`, `Commercial\ForgeMindLicenseServer`), pero el directorio comercial no se incluye en la porción de código abierto.
Solución: compile cada proyecto individualmente (`contributing.md` §3). No compile el sln completo. O use el script de compilación del distribuidor.

**Compilé el complemento pero el host no ve las herramientas**
- Verifique que el DLL del complemento y el `.manifest.json` del mismo nombre estén en el directorio `plugins\<nombre del complemento>\` del host (o el directorio especificado por `FORGEMIND_PLUGINS_DIR`);
- Verifique en los registros del host si el escaneo de complementos omite ese paquete (`PluginLoader` solo omite el paquete individual con error, sin afectar al host);
- Reinicie el host (la tabla de herramientas se carga diferidamente al primer acceso).

## 2. Problemas de inicio del servidor

**Puerto en uso**
Si `http://127.0.0.1:8765` está en uso, se reporta `HttpListenerException`.
Solución: cambie el puerto con `--port N` o la variable de entorno `FORGEMIND_PORT`. Actualice también la dirección del cliente.

**El cliente no puede conectarse / tools/list está vacío**
- Verifique que el servidor está escuchando (registro `Listening on http://127.0.0.1:<port>/mcp`);
- Verifique que la URL del cliente sea `http://127.0.0.1:8765/mcp` (modo HTTP);
- Verifique que los complementos estén cargados (ver arriba).

## 3. Problemas de conexión de motor / puente

**El editor está abierto pero 'motor no conectado / puente no disponible'**
- Verifique que el complemento `ForgeMindForUE` esté habilitado en el proyecto (`UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\`);
- El adaptador de puente lee `%USERPROFILE%\.forgemind\bridge.json` para el descubrimiento y la autenticación. Archivo faltante / credenciales incorrectas provocan fallo de conexión;
- Consulte el registro del editor UE `LogForgeMindBridge`. Al desconectarse, se reconecta con retroceso exponencial; espere la reconexión.

**`launch` / `build` se bloquean**
Las tareas de larga duración se ejecutan de forma asíncrona. Consulte los resultados con `get_task_result`, vea las tareas en curso con `list_executing_tasks` y limpie los registros con `cleanup_tasks`.

## 4. Problemas de pruebas

**Fallo de precompilación de `ForgeMindServerTest`**
`--NoBuild` omite la precompilación (úselo cuando CI garantice artefactos nuevos). El flujo predeterminado compila el host y coloca los complementos en `<entrada>\plugins\`; si falla, termina directamente sin ejecutar casos.

**La prueba se detiene por fail-fast**
Las pruebas de integración se ejecutan por Group / Order con fail-fast: se detienen en el primer fallo (los casos obligatorios del mismo Group sí se ejecutan). Después de corregir, reejecute el grupo con `ForgeMindServerTest.exe [group]`.

**La prueba depende del editor / proyecto de ejemplo**
Los casos bajo `Task\UE5_0\` requieren el editor UE y sesión. Los casos de procesamiento de datos puro (SPK / escaneo) se ejecutan con `ForgeMind.PublicTests`, independientes del editor.

## 5. Problemas de datos / activos

**Fallo de lectura de `asset`**
La lectura sin conexión de `.uasset` debe coincidir con la versión del motor (`UAssetVersions`). La discrepancia de versiones puede causar fallos de análisis. Verifique que la versión del motor esté dentro del rango de soporte de la herramienta.

**No se puede leer `.spk`**
Verifique que se generó con `ForgeMind.EngineScanner <spkPath>` y no está dañado. SPK incluye verificación CRC (`Speedy\Internal\Crc32`) y rechaza cargar archivos dañados.

## 6. Otros

**Cómo habilitar registros**
Los registros del servidor los emite `LogManager` (categorías: `ForgeMindServerEntry`, `ForgeMindServerHost`, `ForgeMindServerBridgeServer`, etc.). En el lado del puente, consulte el registro de salida del editor UE.

**Problemas relacionados con funciones comerciales**
Para problemas de sintaxis DSL, licencias y License, contacte al distribuidor. La versión OpenSource no analiza archivos de licencia; las funciones son fijas y abiertas (ver `security.md`).
