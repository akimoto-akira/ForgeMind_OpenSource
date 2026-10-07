# Guia de contribuição do ForgeMind

Obrigado pelo seu interesse no ForgeMind. O repositório público corresponde ao slice de lançamento OpenSource do ForgeMind e contém apenas os projetos open source publicados, os testes e os projetos de exemplo. Os módulos comerciais (tradutores de domínio DSL, sistema de autorização, etc.) não fazem parte do repositório público: antes de contribuir, esclareça a divisão de âmbitos.

## 1. Estrutura do repositório público

Estrutura de topo do slice open source (ou seja, o repositório que clona):

```
ForgeMind.sln                          # Solução (contém referências a projetos comerciais, ver abaixo)
OpenSource\                            # Projetos open source
  ForgeMindServer\                     # Servidor MCP (host)
  ForgeMind.OfflineEngineKit\          # Plugin de ferramentas offline do motor (engine/project/launch/build/editor, etc.)
  ForgeMind.Assets\                    # Plugin de leitura de assets (asset)
  ForgeMind.Knowledge\                 # Plugin de pesquisa knowledge (knowledge)
  ForgeMind.EngineScanner\             # Scanner de motores (linha de comandos)
  ForgeMind.Scan\                      # Empacotamento de digitalização
  ForgeMind.Scan.Abstractions\         # Abstração de digitalização
  ForgeMind.Scan.Unreal\               # Implementação de digitalização UE (DocumentEngine)
  ForgeMind.Speedy\                    # Formato de empacotamento SPK
UEProject\
  ForgeMind_5_0\                       # Projeto UE de exemplo (compatível 5.0–5.8)
tests\
  ForgeMind.PublicTests\               # Testes unitários públicos (SPK / dados de digitalização)
  ForgeMindServerTest\                 # Testes de integração do servidor (requer editor UE)
```

> Nota: o ficheiro de solução do repositório de desenvolvimento atual (`ForgeMind.sln`) contém referências a projetos Commercial (por exemplo, `Commercial\ForgeMindLicenseGenerator`, `Commercial\ForgeMindLicenseServer`); o produto publicado **não inclui a diretoria `Commercial/`**, pelo que os utilizadores OpenSource devem compilar individualmente os projetos públicos (ver §3) e não executar `dotnet build ForgeMind.sln`.

## 2. Limites do OpenSource

- **OpenSource\**: tudo open source (Apache-2.0), é a área principal de contribuição.
- **UEProject\**: o projeto de exemplo é open source, mas na exportação do slice são excluídos os assets binários `*.uasset` (em Content ficam apenas os assets de texto, como mapas, e a estrutura de diretorias).
- **tests\**: open source; as contribuições devem ser acompanhadas de testes.
- **Commercial não está no repositório público**: tradutores de domínio DSL, autorização comercial, etc. não aparecem no slice; o código open source não pode referenciar tipos de módulos comerciais.
- Na exportação são excluídas automaticamente as diretorias de build e temporárias como `bin` / `obj` / `Binaries` / `Intermediate` / `Saved` / `DerivedDataCache`; os identificadores de licença nos ficheiros são substituídos uniformemente por Apache-2.0 na exportação.

## 3. Compilação

```powershell
# Compilar individualmente (não compilar diretamente a solução completa, ver acima)
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
dotnet build OpenSource\ForgeMind.Scan.Abstractions\ForgeMind.Scan.Abstractions.csproj
dotnet build OpenSource\ForgeMind.Scan\ForgeMind.Scan.csproj
dotnet build OpenSource\ForgeMind.Scan.Unreal\ForgeMind.Scan.Unreal.csproj
dotnet build OpenSource\ForgeMind.Speedy\ForgeMind.Speedy.csproj
# Projetos de plugin (após compilar, colocar os artefactos + .manifest.json homónimo na diretoria plugins do host)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
dotnet build OpenSource\ForgeMind.EngineScanner\ForgeMind.EngineScanner.csproj
```

O host carrega plugins de `<diretoria_do_host>\plugins\` (override pela variável de ambiente `FORGEMIND_PLUGINS_DIR`); o pacote de plugin = DLL principal + `.manifest.json` homónimo + dependências (ver `development-guide.md` §3).

## 4. Convenções de código

- Todos os ficheiros de origem mantêm no cabeçalho o identificador SPDX da licença e a declaração de copyright (atualmente Apache-2.0).
- Os namespaces são organizados por módulo (`ForgeMindServer.*` / `ForgeMind.*`); os contratos públicos passam por interfaces abstratas como `ITool` / `IPlugin`.
- As classes de ferramentas do servidor devem fornecer `Description` completa e schema de parâmetros (esses textos são expostos diretamente aos clientes AI).
- O código dos adaptadores de motor segue as convenções de codificação do motor correspondente (lado UE atual: prefixos de tipo F / U / T, macro `TEXT()`, restrições de segurança do game thread).
- O novo código não pode referenciar nem depender de nenhum módulo comercial.

## 5. Fluxo de PRs

1. Criar um branch a partir de `main`, nomeação sugerida `feat/<descrição>` ou `fix/<descrição>`.
2. Mensagem de commit: `<tipo>: <descrição numa frase>`.
3. Autoverificação antes do commit:
   - compilar individualmente segundo §3, tudo deve passar;
   - se a alteração envolver comandos bridge / comportamento de ferramentas, os Group relevantes de `ForgeMindServerTest` devem passar (modo de execução em `development-guide.md` §6);
   - se envolver SPK / dados de digitalização, `ForgeMind.PublicTests` deve passar;
   - nenhuma referência introduzida a módulos comerciais;
   - atualizar sincronizadamente as páginas correspondentes em `docs\zh-CN\`.
4. Ao abrir a PR, preencher: conteúdo da alteração, resumo dos resultados dos testes, necessidade de revisão de módulos específicos pelo maintainer.
5. Pontos de revisão do maintainer: limites open source, compatibilidade de API, cobertura de testes.

## 6. Requisitos de testes

- Alterações em comandos bridge / comportamento de ferramentas / lógica de adaptadores devem ser acompanhadas de casos de teste (adicionar `*_Tests.cs` sob `tests\ForgeMindServerTest\Task\`).
- Os testes de integração do servidor requerem o ambiente do editor UE; os testes de puro processamento de dados (SPK, digitalização) vão para `tests\ForgeMind.PublicTests\`, sem dependências do motor.
- Os casos devem passar de forma estável na ordem predefinida (fail-fast, para no primeiro erro).

## 7. Contribuição para a documentação

- A documentação em chinês fica em `docs\zh-CN\`; o conteúdo deve ser coerente com os factos do código-fonte; em caso de incerteza, assinalar com «a confirmar».
- Ao alterar a lista de ferramentas públicas, atualizar também `tools-reference.md`.
- As capacidades comerciais (sintaxe DSL, mecanismo de autorização) na documentação são apenas uma visão geral, sem detalhes internos.

## 8. Código de conduta

- Respeitar as decisões de design existentes (protocolo bridge, contratos de ferramentas, formato SPK); alterações importantes devem ser primeiro discutidas.
- Não denegrir publicamente produtos concorrentes; os conteúdos promocionais seguem as regras de comunicação pública em `promo.md`.
