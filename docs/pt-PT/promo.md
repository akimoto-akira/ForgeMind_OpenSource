# Comunicação pública do ForgeMind

## Apresentação numa frase

O ForgeMind é uma plataforma de controlo de motores de jogo com interface MCP: expõe aos clientes AI a gestão de projetos Unreal Engine, a leitura de assets, o arranque / a build / as operações no editor, permitindo que a AI opere o motor como se usasse uma ferramenta.

## Destaques

- **Integração nativa MCP**: protocolo MCP standard (HTTP / stdio), os principais clientes AI ligam e usam de imediato.
- **Slice open source auto-compilável**: o repositório público contém host, todos os plugins open source, o projeto UE de exemplo e os testes; com Apache-2.0 é possível compilar e expandir de forma autónoma.
- **Arquitetura de adaptadores de motor**: host e motor são desacoplados via bridge TCP local, extensível a motores além da UE.
- **Leitura offline de assets**: parsing da estrutura `.uasset` sem abrir o editor, combinado com o índice de digitalização (SPK) para fornecer capacidades de knowledge retrieval.
- **Injeção de capacidades por plugins**: o host integra apenas um conjunto mínimo de ferramentas; as capacidades são estendidas pelos plugins, com limites claros e reduzíveis.

## Versão comercial (numa frase)

A versão comercial oferece, sobre a base OpenSource, geração de assets DSL, autorização comercial e suporte enterprise; para detalhes da sintaxe DSL e das modalidades de autorização, contactar o publisher.

## Regras de comunicação pública

- Não prometer funcionalidades não lançadas; o roadmap faz fé a `roadmap.md`.
- Não denegrir ferramentas semelhantes; não divulgar externamente os detalhes internos dos módulos comerciais (algoritmos de autorização, regras IR internas do DSL, etc.).
- Para comportamentos relativos ao código-fonte, referir sempre o slice público; elementos incertos devem ser assinalados com «a confirmar».
