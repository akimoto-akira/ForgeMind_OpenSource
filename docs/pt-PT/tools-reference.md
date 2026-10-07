# Referência de ferramentas ForgeMind

Todas as ferramentas estão divididas em «incorporadas no host» e «injetadas por plugins». A versão open source atual contém 13 ferramentas MCP. O plugin comercial pode fornecer outras ferramentas (geração de ativos DSL, etc., ver §3; não incluídas na lista open source).

## 1. Ferramentas incorporadas no host

O host (`OpenSource\ForgeMindServer`) contém as seguintes ferramentas sem plugins:

| Ferramenta | Descrição |
|---|---|
| `settings` | Leitura/escrita da configuração do servidor (`McpSettingsManager`, persistência imediata) |
| `get_task_result` | Consulta do resultado de tarefas longas (launch / build, etc.) |
| `list_executing_tasks` | Lista das tarefas longas atuais |
| `cleanup_tasks` | Limpeza dos registos de tarefas concluídas |

## 2. Ferramentas injetadas por plugins

Os plugins injetam ferramentas através de `PluginContext.RegisterTool` em `OnLoad`; aparecem após a ligação do cliente. Após a compilação, coloque a DLL do plugin + `.manifest.json` com o mesmo nome na pasta `plugins\` do host (ou na pasta definida por `FORGEMIND_PLUGINS_DIR`).

### ForgeMind.OfflineEngineKit (7)

| Ferramenta | Descrição |
|---|---|
| `engine` | Consulta de motores instalados localmente (Launcher / compilações de fonte, incluindo versão e nome da aplicação) |
| `project` | Consulta/gestão de projetos UE (identificação do ficheiro de projeto, consulta de informações do projeto) |
| `project_files` | Lista de ficheiros do projeto (filtro de pasta/tipo) |
| `switch_version` | Alteração da versão do projeto para o motor indicado (através de UnrealVersionSelector) |
| `launch` | Arranque do editor ou de executáveis empacotados (configuração de depuração suportada) |
| `build` | Compilação do projeto (target predefinido / target Editor) |
| `editor` | Operações no editor (cena, viewport, ativos, etc., enviadas através do bridge) |

Os esquemas de parâmetros estão sujeitos a `GetParameterSchema()` do código-fonte de cada ferramenta (`OpenSource\ForgeMind.OfflineEngineKit\*.cs`). Algumas operações do motor estão sujeitas às permissões do `ServerPermissionChecker`.

### ForgeMind.Assets (1)

| Ferramenta | Descrição |
|---|---|
| `asset` | Leitura offline da estrutura de `.uasset` (nomes, objetos, propriedades, gráficos Blueprint, consulta por gráfico/propriedade) |

A implementação baseia-se em `UAsset\UAssetSerializer` / `BlueprintGraphReader`, etc. (`OpenSource\ForgeMind.Assets\`).

### ForgeMind.Knowledge (1)

| Ferramenta | Descrição |
|---|---|
| `knowledge` | Pesquisa de conhecimento: consulta de entradas de conhecimento do projeto/motor (fontes: fonte de pasta `FolderKnowledgeSource`, fonte de pacote de índice SPK `IndexPackSource`) |

Implementação em `OpenSource\ForgeMind.Knowledge\Knowledge\`.

## 3. Ferramentas da versão comercial (visão geral)

A versão comercial pode adicionar geração de ativos DSL e ferramentas de consulta relacionadas, injetadas pelo plugin comercial. Os nomes, APIs e funcionalidades DSL concretos estão sujeitos à documentação comercial. O OpenSource não contém nenhuma ferramenta DSL.

## 4. Convenções de chamada de ferramentas

- As ferramentas de tarefas longas devolvem um ID de tarefa. O resultado obtém-se com `get_task_result`.
- Os erros das ferramentas devolvem `ToolResult` de erro (apresentado diretamente ao cliente).
- As descrições e esquemas de parâmetros das ferramentas correspondem ao que `tools/list` do MCP mostra e estão de acordo com o código-fonte.
