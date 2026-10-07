# Guia de desenvolvimento do ForgeMind

Para programadores que pretendem expandir o ForgeMind: como compilar, depurar, adicionar novas ferramentas MCP, adicionar adaptadores de motor e executar testes. Este documento baseia-se no código real do **slice público** (`OpenSource\`, `UEProject\`, `tests\`).

## 1. Visão geral do projeto

| Projeto | Responsabilidade |
|---|---|
| `OpenSource\ForgeMindServer` | Host do servidor MCP: inclui 4 ferramentas, inicia o bridge TCP e carrega plugins |
| `OpenSource\ForgeMind.OfflineEngineKit` | Plugin: ferramentas engine / project / project_files / switch_version / launch / build / editor |
| `OpenSource\ForgeMind.Assets` | Plugin: ferramenta asset (leitura offline de `.uasset`) |
| `OpenSource\ForgeMind.Knowledge` | Plugin: ferramenta knowledge (pesquisa na base de conhecimento) |
| `OpenSource\ForgeMind.EngineScanner` | Scanner de linha de comandos: enumera motores e empacota `.spk` |
| `OpenSource\ForgeMind.Scan` / `.Scan.Abstractions` / `.Scan.Unreal` | Índice de digitalização: abstração, empacotamento, implementação UE (DocumentEngine) |
| `OpenSource\ForgeMind.Speedy` | Formato de empacotamento SPK (ficheiro de pacote de elevado desempenho) |
| `tests\ForgeMind.PublicTests` | Testes unitários públicos (sem dependência do motor) |
| `tests\ForgeMindServerTest` | Testes de integração do servidor (requer editor UE) |
| `UEProject\ForgeMind_5_0` | Projeto UE de exemplo (5.0–5.8) |

## 2. Compilação

```powershell
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# Projetos de plugins e bibliotecas: ver contributing.md §3 (não compilar diretamente o ForgeMind.sln completo, contém referências a projetos comerciais)
```

- Ponto de entrada do host: `OpenSource\ForgeMindServer\Program.cs` (HTTP em escuta em `127.0.0.1:8765` por predefinição, alternância para `--stdio`, override da porta com `--port` / `FORGEMIND_PORT`).
- Registos: `LogManager`; as ferramentas retornam `ToolResult` (sucesso / falha com mensagem).

## 3. Mecanismo de plugins e novas ferramentas MCP

O host inclui apenas 4 ferramentas: `settings`, `get_task_result`, `list_executing_tasks`, `cleanup_tasks`. As restantes ferramentas são fornecidas pelos **plugins na diretoria plugins**:

- Diretoria de plugins: `<diretoria_do_host>\plugins\` (override pela variável de ambiente `FORGEMIND_PLUGINS_DIR`).
- Layout do pacote de plugin: dentro de `plugins\<nome_do_plugin>\` ficam a DLL principal, o `.manifest.json` homónimo e as dependências; também são suportadas DLLs soltas na diretoria raiz (drop-in manual).
- O `PluginLoader` do host digitaliza os plugins → injeção do `PluginContext` (tabela de ferramentas, registo de motores, bridge) → o plugin chama `RegisterTool` em `OnLoad` para registar ferramentas.
- Integridade dos plugins: `PluginIntegrityVerifier` / `PluginSigningKeys` verificam manifestos e assinaturas; plugins de confiança podem estender serviços via ServiceOverride (ver §7).

Para adicionar uma nova ferramenta MCP:

1. Implementar o contrato `ITool` (`OpenSource\ForgeMindServer\Compat\ITool.cs`):
   - `Name` / `DisplayName`: nome e nome apresentado da ferramenta;
   - `Description`: descrição destinada aos clientes AI;
   - `GetParameterSchema()`: declaração de parâmetros em estilo JSON Schema;
   - `Execute(Guid callerId, Dictionary<string, object> parameters)`: retorna `ToolResult`.
2. Criar um novo projeto de plugin (ou adicionar a um existente, por exemplo OfflineEngineKit), implementar `IPlugin` e chamar `RegisterTool` em `OnLoad`.
3. Colocar na mesma diretoria o `.manifest.json` homónimo (descrição do plugin / declaração de dependências).
4. Para capacidades do lado do motor, enviar comandos através do bridge e implementar o processador de comandos do lado do adaptador (ver §5).
5. Escrever casos de teste (§6) e atualizar `tools-reference.md`.

## 4. Como adicionar um novo adaptador de motor

O ForgeMind trata a lógica específica do motor através de «adaptadores»; um novo motor segue o mesmo protocolo bridge (prefixo de 4 bytes para comprimento + envelope JSON, ver `api-reference.md`):

1. Implementar o cliente bridge: ler `%USERPROFILE%\.forgemind\bridge.json` para discovery e autenticação, ligar → handshake → ciclo de mensagens.
2. Registar processadores na whitelist por nome de comando; todos os comandos são executados no thread principal do motor (correspondente ao game thread da UE).
3. Implementar capacidades do lado do motor (leitura/escrita de assets, operações de projeto, etc.) e mapeá-las para os resultados das ferramentas do servidor.
4. Acrescentar casos de teste e documentação para esse motor.

A primeira implementação é o adaptador UE `UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\` (plugin de módulo Editor, cliente bridge + aplicador IR).

## 5. Comandos bridge do lado do motor (implementação UE atual)

- O plugin `ForgeMindForUE` do lado do adaptador contém o cliente bridge; os processadores de comandos são executados no game thread.
- As operações ao nível de assets podem reutilizar os processadores IR do plugin, evitando duplicar a lógica do editor.
- Depuração: categoria `LogForgeMindBridge` nos registos do editor UE; em caso de desconexão, reconexão com backoff exponencial.

## 6. Execução de testes

### Testes unitários públicos (sem editor)

```powershell
dotnet run --project tests\ForgeMind.PublicTests\ForgeMind.PublicTests.csproj
```

Cobrem o round-trip de empacotamento SPK, SPK multiversão e fornecedores de dados de digitalização (`EmbeddedSpkTests` / `ScanPackRoundTripTests` / `SpkMultiVersionTests` / `SpkScanDataProviderTests`).

### Testes de integração do servidor (requer editor UE + projeto de exemplo)

```powershell
# Predefinição: compila host e plugins, faz staging na diretoria plugins, inicia o host, carrega os plugins e executa reflexivamente todos os ITestCase
ForgeMindServerTest.exe

# Filtro: por grupo + ordem
ForgeMindServerTest.exe [group] [order]          # ou --Test [group] [order]
# Saltar a pré-compilação
ForgeMindServerTest.exe --NoBuild
```

Mecanismo:

- Pré-compilação: compila primeiro `ForgeMindServer.csproj`, depois descobre os projetos de plugin com `.manifest.json` homónimo sob `ForgeMind.*` e compila um a um, fazendo staging em `<diretoria_de_entrada>\plugins\<nome_do_plugin>\`.
- Execução: descoberta reflexiva dos `ITestCase`, ordenados por `Group` / `Order`, **fail-fast**; «abrir editor / fechar editor» também são casos normais (`Order=0` abre, `Order=int.MaxValue` fecha), sessão partilhada por grupo.
- Os casos chamam diretamente as ferramentas via `ITool.Execute` (sem passar pelo protocolo MCP); internamente, as ferramentas comunicam com a UE através do bridge.
- Saída: no final é gerado um relatório JSON e um summary legível (a diretoria de saída é indicada no resultado da execução).
- Assets de teste: 30+ `Blueprint_*_Tests.cs` sob `Task\UE5_0\` (vários domínios de blueprints: variáveis / funções / eventos / materiais / macros / interfaces, etc.) + `Task\Base\Engine_List_Tests.cs`; `BlueprintTestHelper` encapsula comandos bridge, distribuição de texto e comparação de snapshots IR (para evitar alterações acidentais).

> Nota: os casos que envolvem «caminhos de texto (DSL)» dependem das capacidades DSL comerciais; os testes de integração do slice público centram-se principalmente em comandos bridge, conforme os casos reais em `Task\` do slice.

## 7. Autorização e capacidades comerciais (visão geral)

- Predefinição `OpenAuthorizationService` (`ForgeMindServer\Authorization\Open\`): capacidades fixas da comunidade, não analisa nenhum ficheiro de licença.
- A autorização comercial é fornecida substituindo `IAuthorizationService` via ServiceOverride no manifesto do plugin (o host aplica `ApplyServiceOverride`). O slice público não contém esse plugin comercial, apenas mantém o mecanismo de substituição (o host não conhece o conceito «comercial», apenas substitui por tipo abstrato).

## 8. Convenções de codificação

- O cabeçalho dos ficheiros inclui o identificador SPDX da licença e a declaração de copyright (Apache-2.0).
- Não referenciar módulos comerciais; novos plugins respeitam os limites do slice definidos em `contributing.md`.
