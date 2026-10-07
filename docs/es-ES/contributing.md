# Guía de contribución de ForgeMind

Gracias por su interés en ForgeMind. El repositorio público corresponde a la porción de lanzamiento OpenSource de ForgeMind e incluye solo los proyectos de código abierto, pruebas y proyectos de ejemplo publicados. Los módulos comerciales (traductores de dominio DSL, sistema de licencias, etc.) no están en el repositorio público. Comprenda la división de alcance antes de contribuir.

## 1. Estructura del repositorio público

Estructura de nivel superior de la porción de código abierto (repositorio clonado):

```
ForgeMind.sln                          # Solución (incluye referencias a proyectos comerciales, ver abajo)
OpenSource\                            # Proyectos de código abierto
  ForgeMindServer\                     # Servidor MCP (host)
  ForgeMind.OfflineEngineKit\          # Complemento de herramientas sin conexión del motor (engine/project/launch/build/editor, etc.)
  ForgeMind.Assets\                    # Complemento de lectura de activos (asset)
  ForgeMind.Knowledge\                 # Complemento de búsqueda de conocimiento (knowledge)
  ForgeMind.EngineScanner\             # Escáner de motores (CLI)
  ForgeMind.Scan\                      # Empaquetado de escaneo
  ForgeMind.Scan.Abstractions\         # Abstracciones de escaneo
  ForgeMind.Scan.Unreal\               # Implementación de escaneo UE (DocumentEngine)
  ForgeMind.Speedy\                    # Formato de empaquetado SPK
UEProject\
  ForgeMind_5_0\                       # Proyecto UE de ejemplo (compatible 5.0–5.8)
tests\
  ForgeMind.PublicTests\               # Pruebas unitarias públicas (SPK / datos de escaneo)
  ForgeMindServerTest\                 # Pruebas de integración del servidor (requieren editor UE)
```

> Nota: el archivo de solución del repositorio de desarrollo actual (`ForgeMind.sln`) incluye referencias a proyectos Commercial (`Commercial\ForgeMindLicenseGenerator`, `Commercial\ForgeMindLicenseServer`, etc.). La publicación pública **no incluye el directorio `Commercial/`**, por lo que los usuarios de OpenSource deben compilar los proyectos públicos individualmente (ver §3) y no ejecutar `dotnet build ForgeMind.sln` directamente.

## 2. Límites del código abierto

- **OpenSource\**: todo es código abierto (Apache-2.0). Área principal de contribución.
- **UEProject\**: el proyecto de ejemplo es de código abierto, pero la exportación de la porción excluye activos binarios `*.uasset` (bajo Content solo se conservan activos de texto como mapas y el esqueleto de directorios).
- **tests\**: código abierto. Las contribuciones deben incluir pruebas.
- **Commercial no está en el repositorio público**: traductores de dominio DSL, licencias comerciales, etc., no aparecen en la porción. El código de código abierto no debe referenciar tipos de módulos comerciales.
- La exportación excluye automáticamente artefactos de compilación y directorios temporales como `bin` / `obj` / `Binaries` / `Intermediate` / `Saved` / `DerivedDataCache`. Los identificadores de licencia en cabeceras de archivos se reemplazan uniformemente por Apache-2.0 al exportar.

## 3. Compilación

```powershell
# Compilar individualmente (no compile el sln completo directamente, ver arriba)
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
dotnet build OpenSource\ForgeMind.Scan.Abstractions\ForgeMind.Scan.Abstractions.csproj
dotnet build OpenSource\ForgeMind.Scan\ForgeMind.Scan.csproj
dotnet build OpenSource\ForgeMind.Scan.Unreal\ForgeMind.Scan.Unreal.csproj
dotnet build OpenSource\ForgeMind.Speedy\ForgeMind.Speedy.csproj
# Proyectos de complementos (después de compilar, coloque la salida + .manifest.json del mismo nombre en el directorio plugins del host)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
dotnet build OpenSource\ForgeMind.EngineScanner\ForgeMind.EngineScanner.csproj
```

El host carga complementos desde `<directorio del host>\plugins\` (sobrescrito por la variable de entorno `FORGEMIND_PLUGINS_DIR`). Un paquete de complemento = DLL principal + `.manifest.json` del mismo nombre + dependencias (ver `development-guide.md` §3).

## 4. Convenciones de código

- Mantenga el identificador de licencia SPDX y el aviso de derechos de autor al inicio de todos los archivos fuente (actualmente Apache-2.0).
- Organice los espacios de nombres por módulo (`ForgeMindServer.*` / `ForgeMind.*`). Los contratos públicos se definen a través de interfaces abstractas como `ITool` / `IPlugin`.
- Las clases de herramientas del servidor deben proporcionar `Description` completa y esquema de parámetros (este texto se muestra directamente al cliente de IA).
- El código del adaptador de motor sigue las convenciones de codificación del motor correspondiente (lado UE actual: prefijos de tipo F/U/T, macro `TEXT()`, restricciones de seguridad del hilo de juego).
- El código nuevo no debe referenciar ni depender de módulos comerciales.

## 5. Proceso de PR

1. Cree una rama desde `main`. Sugerencia de nombre: `feat/<descripción>` o `fix/<descripción>`.
2. Mensaje de commit: `<type>: <descripción de una línea>`.
3. Autoverificación antes del commit:
   - Las compilaciones individuales según §3 tienen éxito;
   - Si cambian comandos de puente / comportamiento de herramientas, los Groups de `ForgeMindServerTest` relevantes pasan (método de ejecución en `development-guide.md` §6);
   - Si cambian datos SPK / escaneo, `ForgeMind.PublicTests` pasa;
   - No introduce referencias a módulos comerciales;
   - Actualiza sincrónicamente los documentos correspondientes en `docs\zh-CN\`.
4. Al abrir el PR, indique: cambios, resumen de resultados de pruebas y si se requiere revisión de un mantenedor específico.
5. Puntos de revisión del mantenedor: límites del código abierto, compatibilidad de API, cobertura de pruebas.

## 6. Requisitos de prueba

- Los cambios en comandos de puente / comportamiento de herramientas / lógica de adaptador deben incluir casos de prueba (agregue `*_Tests.cs` bajo `tests\ForgeMindServerTest\Task\`).
- Las pruebas de integración del servidor requieren el entorno del editor UE. Las pruebas de procesamiento de datos puro (SPK, escaneo) se colocan en `tests\ForgeMind.PublicTests\`, independientes del motor.
- Los casos deben pasar de forma estable en el orden predeterminado (fail-fast, se detiene ante el primer fallo).

## 7. Contribución a la documentación

- La documentación en chino está en `docs\zh-CN\`. El contenido debe coincidir con los hechos del código fuente; los elementos inciertos se marcan como 'por confirmar'.
- Si cambia la lista de herramientas externas, actualice sincrónicamente `tools-reference.md`.
- Las funciones comerciales (sintaxis DSL, mecanismo de licencias) solo se describen a nivel general en la documentación, sin expandir detalles internos.

## 8. Código de conducta

- Respete las decisiones de diseño existentes (protocolo de puente, contratos de herramientas, formato SPK). Para cambios grandes, inicie primero la discusión.
- No denigre productos similares externamente. El contenido promocional sigue la disciplina externa de `promo.md`.
