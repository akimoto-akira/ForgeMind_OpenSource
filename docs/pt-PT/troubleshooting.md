# Resolução de problemas do ForgeMind

Problemas comuns e soluções (baseados nos mecanismos reais do slice público).

## 1. Erros de compilação

**A compilação direta de `ForgeMind.sln` reporta «projeto Commercial\... não encontrado»**
Causa: `ForgeMind.sln` contém entradas de referência a projetos comerciais (`Commercial\ForgeMindLicenseGenerator`, `Commercial\ForgeMindLicenseServer`), mas a diretoria comercial não é publicada com o slice open source.
Solução: compilar os projetos individualmente (contributing.md §3), não a solução completa; ou usar o script de compilação do publisher.

**Depois de compilar o plugin, o host ainda não vê a ferramenta**
- Verificar se a DLL do plugin e o `.manifest.json` homónimo foram colocados em `plugins\<nome_do_plugin>\` do host (ou na diretoria indicada por `FORGEMIND_PLUGINS_DIR`);
- verificar nos registos do host se a digitalização de plugins saltou esse pacote (`PluginLoader` salta apenas o pacote em erro, sem afetar o host);
- reiniciar o host (a tabela de ferramentas é carregada lazy no primeiro acesso).

## 2. Problemas de arranque do servidor

**Porta ocupada**
Quando `http://127.0.0.1:8765` já está ocupado, é reportado `HttpListenerException`.
Solução: mudar de porta com `--port N` ou a variável de ambiente `FORGEMIND_PORT`; atualizar em conformidade o endereço do cliente.

**O cliente não liga / tools/list está vazio**
- Verificar que o servidor está em escuta (registo `Listening on http://127.0.0.1:<port>/mcp`);
- verificar que o URL do cliente é `http://127.0.0.1:8765/mcp` (modo HTTP);
- verificar que o plugin foi carregado (ver acima).

## 3. Problemas de ligação motor / bridge

**O editor está aberto, mas as ferramentas reportam «motor não ligado / bridge indisponível»**
- Verificar que o plugin `ForgeMindForUE` está ativado no projeto (`UEProject\ForgeMind_5_0\ForgeMind_5_0\Plugins\ForgeMindForUE\`);
- o adaptador bridge lê `%USERPROFILE%\.forgemind\bridge.json` para discovery e autenticação; se o ficheiro faltar ou as credenciais não corresponderem, a ligação falha;
- ver o registo do editor UE `LogForgeMindBridge`; em caso de desconexão há reconexão com backoff exponencial, aguardar a reconexão.

**`launch` / `build` bloqueados**
As tarefas longas são executadas de forma assíncrona; usar `get_task_result` para consultar o resultado, `list_executing_tasks` para ver tarefas em curso; `cleanup_tasks` pode limpar os registos.

## 4. Problemas de testes

**A pré-compilação de `ForgeMindServerTest` falha**
`--NoBuild` salta a pré-compilação (usar quando os artefactos já estão garantidos frescos pela CI); o fluxo predefinido compila o host e faz staging dos plugins em `<entrada>\plugins\`, em caso de erro termina sem executar os casos.

**Interrupção fail-fast dos testes**
Os testes de integração são executados por Group / Order, fail-fast: para no primeiro erro (os casos must-run de fecho do editor são ainda executados no grupo). Após a correção, voltar a executar o grupo com `ForgeMindServerTest.exe [group]`.

**Os testes dependem do editor / do projeto de exemplo**
Os casos em `Task\UE5_0\` requerem o editor UE e a sessão; os casos de puro processamento de dados (SPK / digitalização) são executados em `ForgeMind.PublicTests`, sem dependências do editor.

## 5. Problemas de dados / assets

**A leitura de `asset` falha**
A leitura offline de `.uasset` requer a correspondência da versão do motor (`UAssetVersions`); se a versão não corresponder, a análise pode falhar. Confirmar a versão do motor e o âmbito de suporte da ferramenta.

**Impossível carregar `.spk`**
Verificar que o ficheiro foi gerado por `ForgeMind.EngineScanner <spkPath>` e não está danificado; o SPK inclui verificação CRC (`Speedy\Internal\Crc32`), ficheiros corrompidos são rejeitados.

## 6. Outros

**Como ativar os registos**
Os registos do servidor são emitidos por `LogManager` (categorias incluídas: `ForgeMindServerEntry`, `ForgeMindServerHost`, `ForgeMindServerBridgeServer`, etc.); do lado do bridge, ver os registos de saída do editor UE.

**Problemas relacionados com capacidades comerciais**
Para sintaxe DSL, autorização e questões de licenças, contactar o publisher; a versão OpenSource não analisa ficheiros de licença, as capacidades são fixas e abertas (ver `security.md`).
