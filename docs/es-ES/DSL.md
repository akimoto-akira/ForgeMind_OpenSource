# ForgeMind DSL (Domain-Specific Language)

> Este documento explica qué es ForgeMind DSL, cómo funciona y su papel en el sistema general. Está dirigido a desarrolladores que quieran entender o usar ForgeMind DSL.

---

## 1. Qué es DSL

**DSL (Domain-Specific Language)** es un **canal de código declarativo** en ForgeMind para que la IA cree o modifique activos del motor de juego por lotes.

Permite a la IA utilizar texto legible escrito en un estilo sintáctico similar al de C# para describir los activos que se van a crear o modificar (como Blueprints, Materiales, Árboles de Comportamiento, etc.), luego traducirlos a IR (Intermediate Representation) estructurada mediante traductores y enviarlos al editor del motor para su aplicación.

### 1.1 Modelo principal

La clave para entender ForgeMind DSL es la siguiente tubería:

```
IA
  ↓  emite cadena DSL
DSL
  ↓  traducido por el traductor
IR (Representación Intermedia)
  ↓  transmitido vía Bridge
Companion (adaptador del motor)
  ↓  aplicado
Activo del motor
```

Esta tubería se divide en dos fases:

- **Fase de Traducción** (lado del servidor MCP): El texto DSL es traducido a IR JSON por el analizador. El traductor solo realiza análisis sintáctico; no compila, no resuelve referencias externas, no accede al disco. Cualquier error de traducción termina el lote completo, y la IR no se envía al motor.
- **Fase de Aplicación** (lado del motor): Companion recibe el IR JSON y aplica operaciones de creación/actualización/eliminación activo por activo. Un fallo en un activo no afecta a otros activos del mismo lote.

### 1.2 Relación entre DSL y MCP Action

Las capacidades de control de IA de ForgeMind provienen de dos tipos de herramientas:

| Dimensión | MCP Action | DSL |
|-----------|-----------|-----|
| Granularidad | Control preciso de entidad individual | Creación/modificación declarativa por lotes |
| Ejemplo | Añadir una variable, conectar un cable | Un bloque de texto que crea un activo Blueprint completo |
| Canal | Enviado directamente al motor vía Bridge | Primero traducido a IR, luego enviado vía Bridge |
| Papel | Ajustes finos | Andamiaje de arquitectura global |

Ambos son **complementarios** — DSL se encarga de la construcción por lotes "desde cero", mientras que Action se encarga del ajuste preciso "de bueno a excelente".

---

## 2. Dónde Encaja DSL en el Sistema

```
┌──────────────┐   MCP   ┌──────────────────────────┐
│  Cliente IA   │ ◄────► │  ForgeMindServer (host)   │
└──────────────┘         │  • Recibe la cadena DSL     │
                         │  • Invoca al traductor      │
                         │    para producir IR         │
                         │  • Envía IR vía Bridge      │
                         └────────────┬─────────────┘
                                      │ puente TCP local
                         ┌────────────▼─────────────┐
                         │  Companion (adapt. motor) │
                         │  • Recibe IR              │
                         │  • Aplica por activo      │
                         │  • Compila / Guarda        │
                         └──────────────────────────┘
```

Posición de DSL en ForgeMind:
- La IA emite texto de declaración DSL
- La herramienta MCP `dsl` recibe el texto e invoca al traductor para producir IR JSON
- IR JSON se envía al editor del motor en línea a través de TCP Bridge
- Companion (adaptador del motor) lo aplica en el editor para completar la generación de activos

---

## 3. Estilo Sintáctico Básico de DSL

DSL toma prestada la forma sintáctica de C#, pero **no pasa por el compilador de C#**. Imita las convenciones de escritura de C#, pero gestiona el análisis semántico interno del compilador (verificación de tipos, resolución de sobrecarga, etc.) dentro del propio traductor.

### 3.1 Estructura de Bloque de Declaración

Cada bloque de declaración de DSL es una declaración de clase de nivel superior (o struct/enum) con un `[Attribute]`:

```csharp
[DomainAttr(Path = "/Game/Ruta/NombreActivo", Mode = Create)]
[AtributosAdicionales]
public class NombreActivo
{
    // declaraciones de miembros: variables, componentes, funciones, etc.
}
```

### 3.2 Modos Create / Update / Delete

Todos los dominios soportan uniformemente tres modos de operación:

| Valor del Modo | Significado | Carga |
|----------------|-------------|-------|
| `Create` | Crear un nuevo activo | `definition` (definición completa del activo) |
| `Update` | Modificar un activo existente | `patches` (array de estado objetivo por miembro) |
| `Delete` | Eliminar el activo completo | `patches` vacío |

**Ejemplo de Create:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Chest
{
    [Variable] float Health = 100f;
}
```

**Ejemplo de Update:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Update)]
public class BP_Chest
{
    [Variable] float MaxHealth = 200f;     // no existe en el activo → añadir
    [Variable(Category = "Stats")]
    float Health = 90f;                    // ya existe en el activo → reemplazar
    [Delete]                              // marcar para eliminación
    float LegacyVariable;                  // → eliminar miembro existente
}
```

El tipo de operación real (add / replace / delete) lo infiere Companion basándose en si el miembro existe en el activo objetivo y si está anotado con `[Delete]` — no se declara explícitamente en DSL.

**Ejemplo de Delete:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_OBSOLETE", Delete = true)]
public class BP_OBSOLETE { }
```

### 3.3 Referenciar Otros Activos — alias using

DSL soporta la sintaxis de alias `using` para simplificar las referencias entre activos:

```csharp
// using a nivel de archivo (aplica a todos los bloques)
using WoodTex = "/Game/Textures/T_Wood.uasset";

// using a nivel de bloque (aplica solo a ese bloque)
[Blueprint(Path = "/Game/Chest/BP_GoldenChest", Mode = Create)]
public class BP_GoldenChest
{
    using GoldMat = "/Game/Materials/M_Gold.uasset";
    [Variable] MaterialInstance Mat = MaterialInstance(GoldMat);
}
```

Esta es una convención sintáctica de DSL — `using` mapea identificadores a cadenas de ruta de activos para facilitar su referencia en las declaraciones.

### 3.4 Referencias de Tipo Entre Bloques

Además de `using` explícito, DSL también soporta referencias de tipo implícitas entre bloques:

Si una declaración de nivel superior es `[Blueprint(Path="/Game/AI/BP_Hunter")] public class BP_Hunter { ... }`, el identificador `BP_Hunter` puede usarse directamente en bloques posteriores, y se resolverá automáticamente a la ruta de activo `"/Game/AI/BP_Hunter"`.

### 3.5 Comentarios

DSL soporta comentarios al estilo C#:
- `//` comentarios de una sola línea
- `/* */` comentarios de bloque

Los comentarios no producen salida IR.

---

## 4. IR (Representación Intermedia)

IR es el artefacto de traducción entre el traductor y Companion — una descripción de activo estructurada en formato JSON. Es el "lenguaje puente" entre DSL y el motor.

### 4.1 IR por Lote vs. IR por Activo

**IR por Lote** (lote multi-activo):
```json
{
  "version": "5.6",
  "batch": true,
  "assets": [
    { "assetType": "blueprint", "path": "/Game/BP_A", "mode": "create", ... },
    { "assetType": "material",  "path": "/Game/M_Wood", "mode": "create", ... }
  ]
}
```

**IR por Activo**:
```json
{
  "version": "5.6",
  "assetType": "blueprint",
  "path": "/Game/Blueprints/BP_Chest",
  "mode": "create",
  "compile": true,
  "save": false,
  "definition": { ... }
}
```

### 4.2 Referencia de Campos de IR por Activo

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `version` | string | Versión del motor (ej. `"5.6"`), debe coincidir exactamente con la versión mayor |
| `assetType` | string | Tipo de dominio (determinado por el Attribute del bloque) |
| `path` | string | Ruta del activo (obligatorio) |
| `mode` | string | `create` / `update` / `delete` |
| `compile` | bool | Si compilar tras la aplicación (por defecto true) |
| `save` | bool | Si guardar en disco tras la aplicación |
| `patches` | array | Obligatorio para modo update (estado objetivo por miembro, sin campo op) |
| `definition` | object | Obligatorio para modo create (definición completa del activo) |

> **Restricción Modo/Carga**: `mode=create` debe llevar `definition` y no debe incluir `patches`; `mode=update` debe llevar `patches` y no debe incluir `definition`; `mode=delete` lleva `patches` vacío. Cualquier inconsistencia es una carga inválida.

### 4.3 Simetría Bidireccional

La IR de ForgeMind utiliza **la misma estructura de carga útil** tanto en la "dirección de escritura" (`apply_asset_ir`) como en la "dirección de lectura" (`get_asset_ir`):

| Dirección | Comando | Valor `mode` |
|-----------|---------|-------------|
| Escritura | `apply_asset_ir` | `create` / `update` / `delete` |
| Lectura | `get_asset_ir` | `read` |

Esto permite a la IA "leer → modificar modo → escribir de vuelta" la misma IR sin conversión de esquema.

---

## 5. Reconocimiento y Enrutamiento de Dominio

### 5.1 Los Attributes Determinan el Dominio

Cada bloque de declaración en una cadena DSL se enruta automáticamente a su tipo de activo mediante su marcador `[Attribute]`:

```csharp
[Blueprint(Path = "...")]          // → traductor Blueprint → assetType = "blueprint"
[Material(Path = "...")]           // → traductor Material  → assetType = "material"
[BehaviorTree(Path = "...")]       // → traductor Árbol de Comportamiento → assetType = "behavior_tree"
```

**Tabla de Mapeo Attribute → assetType:**

| Attribute | Tipo de Activo |
|-----------|----------------|
| `[Blueprint]` | `blueprint` |
| `[WidgetBlueprint]` | `widget_blueprint` |
| `[Material]` | `material` / `material_instance_constant` |
| `[MaterialFunction]` | `material_function` |
| `[BehaviorTree]` | `behavior_tree` |
| `[Blackboard]` | `blackboard` |
| `[AnimBlueprint]` | `anim_blueprint` |
| `[Enum]` | `user_defined_enum` |
| `[Struct]` | `user_defined_struct` |
| `[InputAction]` | `input_action` |
| `[InputMappingContext]` | `input_mapping_context` |
| `[NiagaraEmitter]` | `niagara_emitter` |
| `[NiagaraSystem]` | `niagara_system` |
| `[MetaSoundSource]` | `meta_sound_source` |
| `[ChaosSolver]` | `chaos_solver_actor` |
| `[GroomBinding]` | `groom_binding_asset` |
| `[Landscape]` | `landscape` |
| `[FoliageType]` | `foliage_type` |
| `[PhysicsAsset]` | `physics_asset` |
| `[PaperFlipbook]` | `paper_flipbook` |
| `[LevelSequence]` | `level_sequence` |
| … | … |

> La tabla anterior mapea todos los dominios actualmente conocidos. Agregar un nuevo dominio solo requiere registrar un nuevo mapeo Attribute → traductor en el lado de traducción — el framework anfitrión no necesita cambios.

### 5.2 Reglas de Enrutamiento

- El analizador realiza una coincidencia exacta de nombre para cada bloque de declaración (a nivel de token, no coincidencia de subcadena)
- Cuando hay múltiples Attributes, el primero en la tabla de mapeo gana (`[WidgetBlueprint]` tiene prioridad sobre `[Blueprint]`)
- **Un Attribute no reconocido no retrocede silenciosamente** — reporta un error y termina el lote completo

### 5.3 Mezclar Múltiples Dominios en un DSL

Una sola cadena DSL puede contener **cualquier número de bloques de declaración de cualquier dominio**:

```csharp
using WoodTex = "/Game/Textures/T_Wood.uasset";

// ── Bloque 1: dominio Blueprint ──
[Blueprint(Path = "/Game/AI/BP_Hunter", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Hunter
{
    [Variable(Category = "Stats")]
    float Health = 100f;
}

// ── Bloque 2: dominio Árbol de Comportamiento ──
[BehaviorTree(Path = "/Game/AI/BT_Hunter", Mode = Create)]
public class BT_Hunter { }

// ── Bloque 3: dominio Material ──
[Material(Path = "/Game/Materials/M_Wood", Mode = Create)]
public function M_Wood
{
    BaseColor = Texture2D(WoodTex);
    Roughness = 0.7f;
}
```

Todos los bloques de declaración en el mismo DSL se traducen de forma independiente, cada uno produce su propia IR, ensamblada en una única IR por lote enviada al motor junto con el Bridge.

---

## 6. Bridge y Companion

### 6.1 Transporte por Bridge

La IR se envía al editor del motor a través de un puente TCP local:
- La capa de puente es responsable de entregar la IR de forma fiable al lado del motor
- Bridge es la infraestructura de comunicación interprocesos local de ForgeMind: el lado del host mantiene el servidor del puente, el lado del adaptador del motor mantiene el cliente del puente

### 6.2 Responsabilidades de Companion

Companion es el plugin adaptador dentro del motor (ej. ForgeMindForUE). Sus responsabilidades incluyen:

1. **Recibir IR JSON** → despachar al manejador de dominio del motor basado en `assetType`
2. **Create**: Construir el activo completo desde `definition` → registrar en el registro de activos → compilar opcionalmente → guardar opcionalmente en disco
3. **Update**: Aplicar modificaciones por miembro desde `patches` (add / replace / delete inferido por Companion)
4. **Delete**: Descargar el paquete de activos → eliminar del registro de activos → eliminar archivos del disco
5. **Reporte de Resultados**: Devolver éxito / error por activo; la dirección de escritura devuelve `patchResults[].actualOp`

> Un fallo en un activo no afecta a otros activos del mismo lote (sin rollback de transacción). Esta es una elección de diseño deliberada en ForgeMind — el espacio de estados de activos del editor es grande, el costo de transacción es alto, y el éxito parcial es preferible a todo o nada.

---

## 7. Diagnóstico y Manejo de Errores

El manejo de errores de DSL se divide en dos fases:

| Fase | Tipos de Error | Manejo |
|------|---------------|--------|
| Fase de traducción (lado MCP) | Errores de sintaxis, Attributes no reconocidos, parámetros obligatorios faltantes, etc. | Cualquier Error → lote terminado, IR no enviado al Bridge |
| Fase de aplicación (lado Companion) | Activo no encontrado, tipo no compatible, fallo de compilación, etc. | Fallo de activo individual no afecta a otros activos del lote |

Las advertencias (Warn) durante la fase de traducción no terminan el lote, pero se adjuntan al texto de respuesta devuelto a la IA.

### 7.1 Códigos de Error Comunes de Traducción

Algunos códigos de error de ejemplo (ver documentación del dominio para detalles):

| Código de Error | Significado |
|-----------------|-------------|
| `FM1004` | Valor de Mode inválido |
| `FM1005` | Parámetro obligatorio Path faltante |
| `FM1002` | Fallo de traducción de dominio (código genérico) |

### 7.2 Códigos de Error de Nivel de Activo (Fase de Aplicación)

| Código de Error | Significado |
|-----------------|-------------|
| `ASSET_NOT_FOUND` | Ruta del activo inexistente (escenarios update / delete) |
| `ASSET_ALREADY_EXISTS` | Ruta ya existente (escenario create, sin sobrescritura por defecto) |
| `ASSET_TYPE_MISMATCH` | Tipo no compatible |
| `ASSET_LOCKED` | Activo bloqueado en el editor |

---

## 8. Extensión de Dominios DSL

### 8.1 Componentes de un Dominio

Un dominio DSL completo consta de los siguientes componentes:

1. **Marcador de Attribute** — ej. `[Blueprint]`, `[Material]`, usado para reconocimiento de enrutamiento
2. **Implementación del Traductor de Dominio** — responsable de traducir las declaraciones DSL de ese dominio a IR JSON
3. **Documentación de Sintaxis DSL** — define las reglas de declaración y parámetros de Attribute para el dominio
4. **Convención de Esquema IR** — define la estructura JSON del `definition` / `patches` del dominio
5. **Manejador de Dominio Companion** — la implementación correspondiente en el lado del motor que aplica activos desde IR en el editor

### 8.2 Pasos Básicos para Agregar un Nuevo Dominio

1. Definir el nombre de Attribute raíz del dominio (ej. `[NewDomain]`)
2. Implementar el traductor de dominio (heredar la clase base traductor, sobrescribir métodos de visita del árbol sintáctico para extraer semántica de dominio, producir IR JSON)
3. Registrarse en el framework para que el analizador pueda encontrar el traductor vía `assetType`
4. Escribir documentación de sintaxis DSL y convenciones de esquema IR
5. Implementar el manejador de dominio correspondiente en el lado Companion del motor

Agregar un nuevo dominio **no requiere cambios en el código anfitrión** — el framework descubre nuevos traductores de dominio a través del mecanismo de registro.

### 8.3 Principios de Implementación de Traductores

Los traductores siguen estos contratos de comportamiento que deben cumplirse al extender dominios DSL:

- **Función Pura**: El traductor toma una cadena DSL como entrada e IR JSON como salida, sin efectos secundarios y sin estado residual. El mismo DSL siempre produce la misma IR.
- **Solo Análisis Sintáctico**: Los traductores no ejecutan compilación C#, no acceden al disco, no resuelven activos externos; solo traducen la forma sintáctica de DSL a IR. La validación semántica es responsabilidad del lado del motor.
- **Recuperación de Errores**: Puede tolerar sintaxis DSL no estándar (como asignación de cadena a alias `using`) sin interrumpir la traducción.
- **Degradación de Sintaxis Desconocida**: Al encontrar sintaxis que no se puede traducir con precisión, reporta una advertencia en lugar de un error, dejándolo a la fase de aplicación del motor.

---

## 9. Límite de Código Abierto y Comercial

Las capacidades DSL de ForgeMind se dividen en dos partes:

### 9.1 Código Abierto (Núcleo Público)

El núcleo de código abierto proporciona el framework anfitrión DSL:

- **Capa del Framework DSL**: Responsable de analizar cadenas DSL (dividir por bloques de declaración de nivel superior, reconocer Attributes y enrutar a traductores), interfaces de traductor y mecanismos de registro, informes de diagnóstico y otra infraestructura.
- **Contrato de Plugin y Mecanismo de Registro**: Los nuevos traductores de dominio se descubren vía registro; el framework anfitrión no necesita lógica de ramificación específica de dominio.
- **Código Abierto Excluye Traductores de Dominio**: La lógica de traducción específica de dominio (Blueprints, Materiales, Árboles de Comportamiento, etc.) no es parte del núcleo de código abierto.

### 9.2 Comercial (Extensión de Pago)

El módulo de extensión comercial se basa en el framework de código abierto para proporcionar:

- Todas las implementaciones específicas de traductores de dominio (Blueprint, Material, Árbol de Comportamiento, AnimBlueprint, Niagara, MetaSound, Chaos, Groom, Landscape, Foliage, Physics, Paper2D, Sequencer, …)
- El punto de entrada de la herramienta MCP `dsl`, sirviendo como punto de ejecución para la traducción y envío de DSL
- Convertidores IR por dominio que realizan procesamiento específico de dominio antes de que la IR se envíe al motor

> El núcleo de código abierto define el framework y el mecanismo de extensión; las capacidades específicas de dominio (sintaxis DSL y traductores para Blueprints, Materiales, Árboles de Comportamiento, etc.) son proporcionadas por el módulo de extensión comercial. Los usuarios de código abierto pueden registrar traductores de dominio personalizados basados en el framework para extender sus propias capacidades DSL.

---

## 10. Restricciones de Comportamiento y Principios de Diseño

| Etiqueta | Comportamiento DSL | Significado |
|----------|-------------------|----------|
| Estilo Sintáctico | Inspirado en C# | C# es el idioma con más datos de entrenamiento de IA, reduciendo la sobrecarga cognitiva |
| Coordenadas de Nodo | No expresado en DSL | Solo se centra en "qué existe y cómo se conectan" — semántica topológica; la disposición es generada automáticamente por el motor |
| Path Obligatorio | Sí | Las convenciones de nomenclatura y la pertenencia al módulo son decididas explícitamente por la IA |
| Coincidencia de Versión | Versión mayor exacta | La `version` IR debe coincidir con la versión mayor del motor |
| Política de Transacción | Sin rollback | Un fallo parcial de activo en un lote no afecta a otros activos |
| Simulación | No existe | La IR solo se envía al motor y no se devuelve al llamador |
| Attribute No Reconocido | Termina el lote | El retroceso silencioso haría que la IA creara inadvertidamente el tipo de activo incorrecto |
| Update patch | Expresar estado objetivo | El patch no lleva campo `op`; las operaciones son inferidas por Companion según la existencia del miembro |

---

## 11. Referencia Rápida

### Plantilla de Bloque de Declaración DSL

```csharp
using SomeAsset = "/Game/Path/SomeAsset.uasset";

[DomainAttr(Path = "/Game/Path/YourAsset", Mode = Create)]
[AdditionalAttribute]
public class YourAsset
{
    using BlockAlias = "/Game/Path/AnotherAsset.uasset";
    [Variable] float YourVariable = 1f;
    [Function(Pure = true)]
    bool YourFunction() => YourVariable > 0;
}
```

### Plantilla de IR por Activo

```json
{
  "version": "<major.minor>",
  "assetType": "<snake_case_type>",
  "path": "/Game/Path/Asset",
  "mode": "create",
  "compile": true,
  "save": false,
  "definition": { }
}
```

### Resumen de Tubería

```
Cliente IA
  → DSL (texto con uno o más bloques de declaración de dominio)
  → Herramienta MCP dsl (fase de traducción)
    → analizador enruta al traductor de dominio según Attribute
    → traductores producen IR JSON
  → IR por lote ({ version, batch, assets })
  → Bridge (TCP local)
  → Companion (fase de aplicación)
    → Crear / Actualizar / Eliminar / Compilar / Guardar por activo
  → Activo del motor
```
