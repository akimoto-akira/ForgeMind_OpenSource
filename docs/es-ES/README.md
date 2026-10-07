# Documentación de ForgeMind (Español)

ForgeMind — plataforma de control de motores de juego con interfaz MCP: a través del protocolo MCP, expone a los clientes de IA la gestión de proyectos de Unreal Engine, la lectura de activos y las operaciones de ejecución/compilación/editor. La implementación actual tiene como objetivo UE (5.0–5.8); el diseño de la plataforma es extensible a otros motores.

> Esta documentación se centra en la **porción de código abierto**: `OpenSource\` (host y complementos), `UEProject\` (proyecto de ejemplo) y `tests\` (pruebas). Las funciones comerciales (generación de activos DSL, implementación de licencias comerciales) solo se describen a nivel general y no están incluidas en la versión OpenSource. OpenSource mantiene las abstracciones de licencia necesarias y la implementación abierta.

## Índice de documentación

| Documento | Contenido |
|---|---|
| [Inicio rápido](getting-started.md) | Requisitos de entorno, compilación, ejecución, primer uso |
| [Arquitectura](architecture.md) | Host / complementos / protocolo de puente / escaneo y SPK |
| [Referencia de API](api-reference.md) | Protocolo MCP, protocolo de puente, contratos de herramientas |
| [Referencia de herramientas](tools-reference.md) | Todas las herramientas MCP y parámetros |
| [Guía de desarrollo](development-guide.md) | Compilación, depuración, adición de herramientas/adaptadores, pruebas |
| [Guía de contribución](contributing.md) | Límites del código abierto, proceso de PR, requisitos de prueba |
| [Solución de problemas](troubleshooting.md) | Problemas comunes y soluciones |
| [Hoja de ruta](roadmap.md) | Dirección planificada |
| [Promoción](promo.md) | Introducción del proyecto y disciplina de comunicación externa |
| [Seguridad](security.md) | Diseño de seguridad y mejores prácticas |

## Resumen del alcance de código abierto

```
OpenSource\
  ForgeMindServer               Host MCP (4 herramientas integradas + carga de complementos + puente TCP)
  ForgeMind.OfflineEngineKit    Herramientas de motor/proyecto/ejecución/compilación/editor (complemento)
  ForgeMind.Assets              Lectura sin conexión de .uasset (complemento)
  ForgeMind.Knowledge           Búsqueda de conocimiento (complemento)
  ForgeMind.Scan.Abstractions   Abstracciones de datos y proveedores de escaneo
  ForgeMind.Scan                Lectura y empaquetado de SPK
  ForgeMind.Scan.Unreal         Implementación de escaneo de Unreal Engine
  ForgeMind.EngineScanner       CLI de escaneo independiente que genera .spk directamente
  ForgeMind.Speedy              Base de bajo nivel de SPK
UEProject\ForgeMind_5_0         Proyecto UE de ejemplo (5.0–5.8)
tests\                          Pruebas unitarias públicas + pruebas de integración de servidor
```

## Licencia

El contenido de la versión OpenSource tiene licencia Apache-2.0. Los módulos comerciales no están incluidos en la versión OpenSource y se ofrecen bajo una licencia comercial independiente.
