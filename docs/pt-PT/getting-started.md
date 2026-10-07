# Início rápido ForgeMind

ForgeMind é uma **plataforma de controlo de motores de jogos baseada em MCP**: através do protocolo MCP, fornece a clientes de IA a gestão de projetos Unreal Engine, leitura de ativos e operações de arranque/compilação/edição. A implementação atual é UE; a plataforma é extensível. A versão open source inclui um conjunto de ferramentas de motor e funcionalidades de bridge; a versão comercial adiciona geração de ativos DSL (§5).

Todos os passos deste guia podem ser realizados com a parte **open source** (`OpenSource\` + `UEProject\` + `tests\`).

## 1. Requisitos de ambiente

- SDK .NET (para compilar o host e os plugins).
- Unreal Engine 5.0–5.8 (com editor). O projeto de exemplo é `UEProject\ForgeMind_5_0\ForgeMind_5_0\ForgeMind_5_0.uproject`.
- Cliente compatível com MCP (Claude Desktop / Cursor / cliente personalizado).

## 2. Compilar o servidor e os plugins

```powershell
# Host
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
# Plugins (após a compilação, coloque a DLL + .manifest.json com o mesmo nome na pasta plugins do host)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
```

> Não compile diretamente todo o `ForgeMind.sln` (contém referências a projetos comerciais; o diretório comercial não está no repositório). Os plugins também podem ser utilizados a partir de artefactos de compilação fornecidos pelo distribuidor.

## 3. Arranque

1. **Inicie o servidor** (predefinição: HTTP residente, porta 8765):

   ```powershell
   dotnet run --project OpenSource\ForgeMindServer\ForgeMindServer.csproj
   # Ou execute o ForgeMindServer.exe compilado; configurável através de --port N / --stdio / FORGEMIND_PORT
   ```

2. **Abra o editor UE e o projeto de exemplo**: execute `ForgeMind_5_0.uproject` no UE; o plugin `ForgeMindForUE` inicia automaticamente o cliente bridge no editor e liga-se ao host.

3. **Configure o cliente**: adicione o servidor ao cliente MCP:

   ```json
   { "mcpServers": { "forgemind": { "url": "http://127.0.0.1:8765/mcp" } } }
   ```

   Após a ligação, a lista de ferramentas aparece (4 incorporadas + as injetadas pelos plugins).

## 4. Primeira utilização

- `engine` — apresentar informações do motor atual;
- `project` / `project_files` — apresentar o projeto de exemplo e os seus ficheiros;
- `launch` — iniciar o editor, `editor` — operações no editor;
- `asset` — ler a estrutura de ativos do projeto;
- `knowledge` — pesquisar no conhecimento do projeto.

> As tarefas longas (launch/build etc.) são executadas de forma assíncrona. Os resultados obtêm-se com `get_task_result`, as tarefas atuais com `list_executing_tasks`, a limpeza com `cleanup_tasks`.

## 5. Versão comercial (DSL)

O módulo Commercial é uma edição comercial separada e não faz parte do repositório open source. A versão comercial oferece **geração de ativos DSL**: escreva Blueprints, materiais, etc. como texto legível e converta-os em ativos de motor. As ferramentas DSL específicas, as funções de conversão de texto e os detalhes de sintaxe estão sujeitos à documentação comercial. As funcionalidades de licença comercial também pertencem ao Commercial. O OpenSource não contém nenhuma ferramenta DSL.

## 6. Próximos passos

- Arquitetura e mecanismos: `architecture.md`
- Lista de ferramentas e parâmetros: `tools-reference.md`
- Desenvolvimento e contribuições: `development-guide.md` / `contributing.md`
- Perguntas frequentes: `troubleshooting.md`
