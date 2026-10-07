# Promoción de ForgeMind

## Presentación en una línea

ForgeMind es una plataforma de control de motores de juego con interfaz MCP: a través del protocolo MCP, expone a los clientes de IA la gestión de proyectos de Unreal Engine, la lectura de activos y las operaciones de ejecución/compilación/editor, permitiendo que la IA opere el motor como una herramienta.

## Puntos destacados principales

- **Conexión MCP nativa**: protocolo MCP estándar (HTTP / stdio), utilizable inmediatamente en los clientes de IA más populares.
- **Porción de código abierto autocompilable**: el repositorio público incluye el host, todos los complementos de código abierto, el proyecto UE de ejemplo y las pruebas, con licencia Apache-2.0, autocompilable y ampliable.
- **Arquitectura de adaptadores de motor**: el host y el motor están débilmente acoplados mediante un puente TCP local, extensible a motores distintos de UE.
- **Lectura de activos sin conexión**: analiza la estructura `.uasset` sin abrir el editor. Combinado con el índice de escaneo (SPK), proporciona funciones de búsqueda de conocimiento.
- **Inyección de funciones tipo complemento**: el host solo incluye un conjunto mínimo de herramientas; las funciones se amplían con complementos, con límites claros y recortables.

## Versión comercial (en una línea)

La versión comercial ofrece generación de activos DSL, funciones de licencias comerciales y soporte empresarial sobre OpenSource. Contacte al distribuidor para los detalles de la sintaxis DSL y los términos de licencia.

## Disciplina externa

- No prometa funciones no publicadas. La hoja de ruta sigue `roadmap.md`.
- No denigre herramientas similares. No divulgue externamente los internos de los módulos comerciales (algoritmos de licencias, reglas IR internas de DSL, etc.).
- Todo lo relacionado con el comportamiento del código fuente siempre sigue la porción pública; los elementos inciertos se marcan como 'por confirmar'.
