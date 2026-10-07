# Hoja de ruta de ForgeMind

La dirección se describe desde una perspectiva pública y se ordena por hitos. Los plazos específicos siguen los lanzamientos.

## Corto plazo

- **Perfeccionamiento de la porción de código abierto**: completar scripts de compilación y procedimientos de depuración conjunta con un clic para reducir el costo de incorporación de nuevos contribuyentes.
- **Sincronización de documentación**: mantener los documentos en `docs\zh-CN\` conforme cambia el código fuente, manteniendo coherencia con la porción pública.
- **Cobertura de pruebas**: ampliar los casos de `ForgeMind.PublicTests` y `ForgeMindServerTest` (comandos de puente, escaneo, escenarios límite de SPK).

## Medio plazo

- **Extensión de adaptadores de motor**: además del adaptador UE existente (5.0–5.8), explorar integraciones con más motores/editores (protocolo de puente isomórfico).
- **Refuerzo de escaneo y conocimiento**: mejorar la cobertura de análisis de archivos de cabecera / Blueprint / reglas de DocumentEngine, y mejorar la eficiencia de empaquetado SPK y las actualizaciones incrementales.
- **Ecosistema de complementos**: perfeccionar la API de complementos y la especificación de manifiestos para admitir un ecosistema más rico de complementos de herramientas de terceros.

## Largo plazo

- **Documentación abierta de uso de DSL**: después de que el DSL comercial se estabilice, es posible publicar parte de la documentación de uso, ejemplos y contenido educativo según la estrategia del producto. La implementación de DSL, Translator, reglas IR y lógica de generación de activos siguen siendo módulos comerciales.
- **Plataformización**: posicionar ForgeMind como una plataforma de control MCP multipropósito para múltiples motores, acumulando protocolos independientes del motor y capas de abstracción.

> La dirección de la versión comercial (sistema de licencias, soporte empresarial, etc.) no se expande en esta hoja de ruta pública. Contacte al distribuidor.
