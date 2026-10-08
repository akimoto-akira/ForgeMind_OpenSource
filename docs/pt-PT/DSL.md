# ForgeMind DSL (Domain-Specific Language)

> Este documento explica o que é a ForgeMind DSL, como funciona e qual o seu papel no sistema geral. Destina-se a programadores que pretendem compreender ou utilizar a ForgeMind DSL.

---

## 1. O Que É DSL

**DSL (Domain-Specific Language)** é um **canal de código declarativo** na ForgeMind para a IA criar ou modificar ativos do motor de jogo em lote.

Permite que a IA utilize texto legível escrito num estilo sintático semelhante ao C# para descrever os ativos a criar ou modificar (como Blueprints, Materials, Behaviour Trees, etc.), depois traduzi-los em IR (Intermediate Representation) estruturada através de tradutores e enviá-los para o editor do motor para aplicação.

### 1.1 Modelo Central

A chave para compreender a ForgeMind DSL é o seguinte pipeline:

```
IA
  ↓  gera string DSL
DSL
  ↓  traduzido pelo tradutor
IR (Representação Intermédia)
  ↓  transmitido via Bridge
Companion (adaptador do motor)
  ↓  aplicado
Ativo do motor
```

Este pipeline divide-se em duas fases:

- **Fase de Tradução** (lado do servidor MCP): O texto DSL é traduzido em IR JSON pelo analisador. O tradutor apenas realiza análise sintática; não compila, não resolve referências externas, não acede ao disco. Qualquer erro de tradução termina o lote completo, e a IR não é enviada para o motor.
- **Fase de Aplicação** (lado do motor): A Companion recebe o IR JSON e aplica operações de criação/atualização/eliminação ativo por ativo. Uma falha num ativo não afeta os outros ativos do mesmo lote.

### 1.2 Relação Entre DSL e MCP Action

As capacidades de controlo de IA da ForgeMind provêm de dois tipos de ferramentas:

| Dimensão | MCP Action | DSL |
|----------|-----------|-----|
| Granularidade | Controlo preciso de entidade única | Criação/modificação declarativa em lote |
| Exemplo | Adicionar uma variável, ligar um fio | Um bloco de texto que cria um ativo Blueprint inteiro |
| Canal | Enviado diretamente para o motor via Bridge | Primeiro traduzido em IR, depois enviado via Bridge |
| Papel | Ajustes finos | Estruturação de arquitetura global |

Os dois são **complementares** — a DSL trata da construção em lote "do zero", enquanto a Action trata do ajuste preciso "de bom a excelente".

---

## 2. Onde a DSL se Encaixa no Sistema

```
┌──────────────┐   MCP   ┌──────────────────────────┐
│  Cliente IA   │ ◄────► │  ForgeMindServer (host)   │
└──────────────┘         │  • Recebe a string DSL      │
                         │  • Invoca o tradutor        │
                         │    para produzir IR         │
                         │  • Envia IR via Bridge      │
                         └────────────┬─────────────┘
                                      │ ponte TCP local
                         ┌────────────▼─────────────┐
                         │  Companion (adap. motor)  │
                         │  • Recebe IR              │
                         │  • Aplica por ativo       │
                         │  • Compila / Guarda        │
                         └──────────────────────────┘
```

Posição da DSL na ForgeMind:
- A IA gera texto de declaração DSL
- A ferramenta MCP `dsl` recebe o texto e invoca o tradutor para produzir IR JSON
- O IR JSON é enviado para o editor do motor ao vivo através de TCP Bridge
- A Companion (adaptador do motor) aplica-o no editor para completar a geração de ativos

---

## 3. Estilo Sintático Básico da DSL

A DSL toma emprestada a forma sintática do C#, mas **não passa pelo compilador C#**. Imita as convenções de escrita do C#, mas gere a análise semântica interna do compilador (verificação de tipos, resolução de sobrecarga, etc.) dentro do próprio tradutor.

### 3.1 Estrutura do Bloco de Declaração

Cada bloco de declaração DSL é uma declaração de classe de nível superior (ou struct/enum) com um `[Attribute]`:

```csharp
[DomainAttr(Path = "/Game/Caminho/NomeAtivo", Mode = Create)]
[AtributosAdicionais]
public class NomeAtivo
{
    // declarações de membros: variáveis, componentes, funções, etc.
}
```

### 3.2 Modos Create / Update / Delete

Todos os domínios suportam uniformemente três modos de operação:

| Valor do Modo | Significado | Carga |
|---------------|-------------|-------|
| `Create` | Criar um novo ativo | `definition` (definição completa do ativo) |
| `Update` | Modificar um ativo existente | `patches` (array de estado alvo por membro) |
| `Delete` | Eliminar o ativo inteiro | `patches` vazio |

**Exemplo Create:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Chest
{
    [Variable] float Health = 100f;
}
```

**Exemplo Update:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_Chest", Mode = Update)]
public class BP_Chest
{
    [Variable] float MaxHealth = 200f;     // não existe no ativo → adicionar
    [Variable(Category = "Stats")]
    float Health = 90f;                    // já existe no ativo → substituir
    [Delete]                              // marcar para eliminação
    float LegacyVariable;                  // → eliminar membro existente
}
```

O tipo de operação real (add / replace / delete) é inferido pela Companion com base na existência do membro no ativo alvo e na presença de `[Delete]` — não declarado explicitamente em DSL.

**Exemplo Delete:**
```csharp
[Blueprint(Path = "/Game/Blueprints/BP_OBSOLETE", Delete = true)]
public class BP_OBSOLETE { }
```

### 3.3 Referenciar Outros Ativos — aliases using

A DSL suporta a sintaxe de alias `using` para simplificar referências entre ativos:

```csharp
// using a nível de ficheiro (aplica a todos os blocos)
using WoodTex = "/Game/Textures/T_Wood.uasset";

// using a nível de bloco (aplica apenas a esse bloco)
[Blueprint(Path = "/Game/Chest/BP_GoldenChest", Mode = Create)]
public class BP_GoldenChest
{
    using GoldMat = "/Game/Materials/M_Gold.uasset";
    [Variable] MaterialInstance Mat = MaterialInstance(GoldMat);
}
```

Esta é uma convenção sintática da DSL — `using` mapeia identificadores para strings de caminho de ativo nas declarações.

### 3.4 Referências de Tipo Entre Blocos

Além do `using` explícito, a DSL também suporta referências de tipo implícitas entre blocos:

Se uma declaração de nível superior for `[Blueprint(Path="/Game/AI/BP_Hunter")] public class BP_Hunter { ... }`, o identificador `BP_Hunter` pode ser usado diretamente em blocos subsequentes, e resolver-se-á automaticamente no caminho de ativo `"/Game/AI/BP_Hunter"`.

### 3.5 Comentários

A DSL suporta comentários ao estilo C#:
- `//` comentários de uma linha
- `/* */` comentários de bloco

Os comentários não produzem saída IR.

---

## 4. IR (Representação Intermédia)

A IR é o artefacto de tradução entre o tradutor e a Companion — uma descrição de ativo estruturada em formato JSON. É a "linguagem ponte" entre a DSL e o motor.

### 4.1 Batch IR vs. IR de Ativo Único

**Batch IR** (lote multi-ativo):
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

**IR de Ativo Único**:
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

### 4.2 Referência de Campos da IR de Ativo Único

| Campo | Tipo | Descrição |
|-------|------|-----------|
| `version` | string | Versão do motor (ex. `"5.6"`), deve corresponder exatamente à versão maior do motor |
| `assetType` | string | Tipo de domínio (determinado pelo Attribute do bloco) |
| `path` | string | Caminho do ativo (obrigatório) |
| `mode` | string | `create` / `update` / `delete` |
| `compile` | bool | Se compilar após a aplicão (por defeito true) |
| `save` | bool | Se guardar no disco após a aplicação |
| `patches` | array | Obrigatório para modo update (estado alvo por membro, sem campo op) |
| `definition` | object | Obrigatório para modo create (definição completa do ativo) |

> **Restrição Modo/Carga**: `mode=create` deve conter `definition` e não deve incluir `patches`; `mode=update` deve conter `patches` e não deve incluir `definition`; `mode=delete` contém `patches` vazio. Qualquer inconsistência é uma carga inválida.

### 4.3 Simetria Bidirecional

A IR da ForgeMind utiliza **a mesma estrutura de carga útil** tanto na "direção de escrita" (`apply_asset_ir`) como na "direção de leitura" (`get_asset_ir`):

| Direção | Comando | Valor `mode` |
|---------|---------|-------------|
| Escrita | `apply_asset_ir` | `create` / `update` / `delete` |
| Leitura | `get_asset_ir` | `read` |

Isto permite à IA "ler → modificar mode → reescrever" a mesma IR sem conversão de esquema.

---

## 5. Reconhecimento e Encaminhamento de Domínio

### 5.1 Os Attributes Determinam o Domínio

Cada bloco de declaração numa string DSL é automaticamente encaminhado para o seu tipo de ativo através do seu marcador `[Attribute]`:

```csharp
[Blueprint(Path = "...")]          // → tradutor Blueprint → assetType = "blueprint"
[Material(Path = "...")]           // → tradutor Material  → assetType = "material"
[BehaviorTree(Path = "...")]       // → tradutor Behaviour Tree → assetType = "behavior_tree"
```

**Tabela de Mapeamento Attribute → assetType:**

| Attribute | Tipo de Ativo |
|-----------|---------------|
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

> A tabela acima mapeia todos os domínios atualmente conhecidos. Adicionar um novo domínio apenas requer o registo de um novo mapeamento Attribute → tradutor no lado da tradução — o framework hóspede não precisa de alterações.

### 5.2 Regras de Encaminhamento

- O analisador realiza uma correspondência exata de nome para cada bloco de declaração (ao nível do token, não correspondência de substring)
- Quando múltiplos Attributes estão presentes, o primeiro encontrado na tabela de mapeamento ganha (`[WidgetBlueprint]` tem prioridade sobre `[Blueprint]`)
- **Um Attribute não reconhecido não retrocede silenciosamente** — reporta um erro e termina o lote completo

### 5.3 Misturar Múltiplos Domínios numa DSL

Uma única string DSL pode conter **qualquer número de blocos de declaração de qualquer domínio**:

```csharp
using WoodTex = "/Game/Textures/T_Wood.uasset";

// ── Bloco 1: domínio Blueprint ──
[Blueprint(Path = "/Game/AI/BP_Hunter", Mode = Create)]
[Extends(typeof(Actor))]
public class BP_Hunter
{
    [Variable(Category = "Stats")]
    float Health = 100f;
}

// ── Bloco 2: domínio Behaviour Tree ──
[BehaviorTree(Path = "/Game/AI/BT_Hunter", Mode = Create)]
public class BT_Hunter { }

// ── Bloco 3: domínio Material ──
[Material(Path = "/Game/Materials/M_Wood", Mode = Create)]
public function M_Wood
{
    BaseColor = Texture2D(WoodTex);
    Roughness = 0.7f;
}
```

Todos os blocos de declaração na mesma DSL são traduzidos independentemente, cada um produz a sua própria IR, montados num Batch IR único enviado para o motor através do Bridge.

---

## 6. Bridge e Companion

### 6.1 Transporte Bridge

A IR é enviada para o editor do motor através de uma ponte TCP local:
- A camada de ponte é responsável pela entrega fiável da IR ao lado do motor
- Bridge é a infraestrutura de comunicação inter-processos local da ForgeMind: o lado do hóspede mantém o servidor da ponte, o lado do adaptador do motor mantém o cliente da ponte

### 6.2 Responsabilidades da Companion

Companion é o plugin adaptador dentro do motor (ex. ForgeMindForUE). As suas responsabilidades incluem:

1. **Receber IR JSON** → despachar para o manipulador de domínio do motor baseado em `assetType`
2. **Create**: Construir o ativo completo a partir de `definition` → registar no registo de ativos → compilar opcionalmente → guardar opcionalmente no disco
3. **Update**: Aplicar modificações por membro a partir de `patches` (add / replace / delete inferido pela Companion)
4. **Delete**: Descarregar o pacote do ativo → remover do registo de ativos → eliminar ficheiros do disco
5. **Relatório de Resultados**: Devolver sucesso / erro por ativo; a direção de escrita devolve `patchResults[].actualOp`

> Uma falha num ativo não afeta os outros ativos do mesmo lote (sem rollback de transação). Esta é uma escolha de design deliberada na ForgeMind — o espaço de estados dos ativos do editor é grande, o custo de transação é alto, e o sucesso parcial é preferível a tudo ou nada.

---

## 7. Diagnóstico e Tratamento de Erros

O tratamento de erros DSL divide-se em duas fases:

| Fase | Tipos de Erro | Tratamento |
|------|--------------|------------|
| Fase de tradução (lado MCP) | Erros de sintaxe, Attributes não reconhecidos, parâmetros obrigatórios em falta | Qualquer Erro → lote terminado, IR não enviada para o Bridge |
| Fase de aplicação (lado Companion) | Ativo não encontrado, tipo incompatível, falha de compilação | Falha de ativo individual não afeta os outros ativos |

Os avisos (Warn) durante a fase de tradução não terminam o lote, mas são anexados ao texto de resposta devolvido à IA.

### 7.1 Códigos de Erro Comuns da Fase de Tradução

Alguns códigos de erro de exemplo (ver documentação do domínio para detalhes):

| Código de Erro | Significado |
|----------------|-------------|
| `FM1004` | Valor de Mode inválido |
| `FM1005` | Parâmetro obrigatório Path em falta |
| `FM1002` | Falha de tradução de domínio (código genérico de fallback) |

### 7.2 Principais Códigos de Erro a Nível de Ativo (Fase de Aplicação)

| Código de Erro | Significado |
|----------------|-------------|
| `ASSET_NOT_FOUND` | Caminho do ativo inexistente (cenários update / delete) |
| `ASSET_ALREADY_EXISTS` | Caminho já existente (cenário create, sem sobrescrita por defeito) |
| `ASSET_TYPE_MISMATCH` | Tipo incompatível |
| `ASSET_LOCKED` | Ativo bloqueado no editor |

---

## 8. Extensão de Domínios DSL

### 8.1 Componentes de um Domínio

Um domínio DSL completo consiste nos seguintes componentes:

1. **Marcador de Attribute** — ex. `[Blueprint]`, `[Material]`, usado para reconhecimento de encaminhamento
2. **Implementação do Tradutor de Domínio** — responsável por traduzir as declarações DSL desse domínio em IR JSON
3. **Documentação de Sintaxe DSL** — define as regras de declaração e parâmetros de Attribute para o domínio
4. **Convenções de Esquema IR** — definem a estrutura JSON do `definition` / `patches` do domínio
5. **Manipulador de Domínio Companion** — a implementação correspondente no lado do motor que aplica ativos a partir de IR no editor

### 8.2 Passos Básicos para Adicionar um Novo Domínio

1. Definir o nome do Attribute raiz do domínio (ex. `[NewDomain]`)
2. Implementar o tradutor de domínio (herdar a classe base tradutor, substituir métodos de visita da árvore sintática para extrair semântica de domínio, produzir IR JSON)
3. Registar no framework para que o analisador possa encontrar o tradutor via `assetType`
4. Escrever documentação de sintaxe DSL e convenções de esquema IR
5. Implementar o manipulador de domínio correspondente no lado Companion do motor

Adicionar um novo domínio **não requer alterações no código hóspede** — o framework descobre novos tradutores de domínio através do mecanismo de registo.

### 8.3 Princípios de Implementação dos Tradutores

Os tradutores seguem estes contratos de comportamento que devem ser cumpridos ao estender domínios DSL:

- **Função Pura**: O tradutor recebe uma string DSL como entrada e produz IR JSON como saída, sem efeitos secundários e sem estado residual. A mesma DSL produz sempre a mesma IR.
- **Apenas Análise Sintática**: Os tradutores não executam compilação C#, não acedem ao disco, não resolvem ativos externos; apenas traduzem a forma sintática da DSL em IR. A validação semântica é da responsabilidade do lado do motor.
- **Recuperação de Erros**: Pode tolerar sintaxe DSL não padrão (como atribuição de string a aliases `using`) sem interromper a tradução.
- **Degradação de Sintaxe Desconhecida**: Ao encontrar sintaxe que não pode ser traduzida com precisão, reporta um aviso em vez de um erro, deixando para a fase de aplicação do motor.

---

## 9. Fronteira entre Código Aberto e Comercial

As capacidades DSL da ForgeMind dividem-se em duas partes:

### 9.1 Open Source (Núcleo Público)

O núcleo de código aberto fornece o framework hóspede DSL:

- **Camada Framework DSL**: Responsável pela análise de strings DSL (divisão por blocos de declaração de nível superior, reconhecimento de Attributes e encaminhamento para tradutores), interfaces de tradutor e mecanismos de registo, relatórios de diagnóstico e outras infraestruturas.
- **Contrato de Plugin e Mecanismo de Registo**: Novos tradutores de domínio são descobertos via registo; o framework hóspede não necessita de lógica de ramificação específica de domínio.
- **Open Source Exclui Tradutores de Domínio**: A lógica de tradução específica de domínio (Blueprints, Materials, Behaviour Trees, etc.) não faz parte do núcleo de código aberto.

### 9.2 Comercial (Extensão Paga)

O módulo de extensão comercial constrói-se sobre o framework de código aberto para fornecer:

- Todas as implementações específicas de tradutores de domínio (Blueprint, Material, Behaviour Tree, AnimBlueprint, Niagara, MetaSound, Chaos, Groom, Landscape, Foliage, Physics, Paper2D, Sequencer, …)
- O ponto de entrada da ferramenta MCP `dsl`, servindo como ponto de execução para a tradução e envio da DSL
- Conversores IR por domínio que realizam processamento específico de domínio antes da IR ser enviada para o motor

> O núcleo de código aberto define o framework e o mecanismo de extensão; as capacidades específicas de domínio (sintaxe e tradutores DSL para Blueprints, Materials, Behaviour Trees, etc.) são fornecidas pelo módulo de extensão comercial. Os utilizadores de código aberto podem registar tradutores de domínio personalizados com base no framework para estender as suas capacidades DSL.

---

## 10. Restrições de Comportamento e Princípios de Design

| Etiqueta | Comportamento DSL | Significado |
|----------|------------------|-------------|
| Estilo Sintático | Inspirado em C# | C# é a linguagem com mais dados de treino de IA, reduzindo a carga cognitiva |
| Coordenadas de Nó | Não expressas em DSL | Foca apenas "o que existe e como se ligam" — semântica topológica; a disposição é gerada automaticamente pelo motor |
| Path Obrigatório | Sim | As convenções de nomeamento e a pertença ao módulo são decididas explicitamente pela IA |
| Correspondência de Versão | Versão maior exata | A `version` IR deve corresponder à versão maior do motor |
| Política de Transação | Sem rollback | Uma falha parcial de ativo num lote não afeta os outros ativos |
| Simulação | Não existe | A IR é apenas enviada para o motor, não devolvida à chamada |
| Attribute Não Reconhecido | Termina o lote | O retrocesso silencioso faria com que a IA criasse inadvertidamente o tipo de ativo errado |
| Update patch | Expressa estado alvo | O patch não contém campo `op`; as operações são inferidas pela Companion com base na existência do membro |

---

## 11. Referência Rápida

### Modelo de Bloco de Declaração DSL

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

### Modelo de IR de Ativo Único

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

### Visão Geral do Pipeline

```
Cliente IA
  → DSL (texto com um ou mais blocos de declaração de domínio)
  → Ferramenta MCP dsl (fase de tradução)
    → analisador encaminha para o tradutor de domínio com base no Attribute
    → tradutores produzem IR JSON
  → Batch IR ({ version, batch, assets })
  → Bridge (TCP local)
  → Companion (fase de aplicação)
    → Criar / Atualizar / Eliminar / Compilar / Guardar por ativo
  → Ativo do motor
```
