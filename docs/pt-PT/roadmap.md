# Roadmap do ForgeMind

A direção é apresentada numa perspetiva pública, ordenada por milestones; os prazos específicos fazem fé aos lançamentos.

## Curto prazo

- **Completar o slice open source**: integrar os scripts de compilação e as instruções de ligação num único comando, para baixar o custo de entrada de novos contribuidores.
- **Sincronização da documentação**: manter as páginas em `docs\zh-CN\` alinhadas com as alterações do código-fonte, garantindo coerência com o slice público.
- **Cobertura de testes**: expandir os casos de `ForgeMind.PublicTests` e `ForgeMindServerTest` (comandos bridge, digitalização, cenários limite SPK).

## Médio prazo

- **Extensão dos adaptadores de motor**: com base no adaptador UE existente (5.0–5.8), explorar a integração de outros motores / editores (protocolo bridge homólogo).
- **Reforço da digitalização e knowledge**: melhorar a cobertura de parsing do DocumentEngine sobre headers / blueprints / ficheiros de regras e otimizar a eficiência e a atualização incremental do empacotamento SPK.
- **Ecossistema de plugins**: aperfeiçoar a API de plugins e as especificações do manifesto, suportando plugins de terceiros mais ricos.

## Longo prazo

- **Abertura da documentação de uso do DSL**: quando o DSL comercial estiver estável, conforme a estratégia de produto, poderá ser publicada parte da documentação de uso, exemplos e conteúdos didáticos; a implementação do DSL, os Translator, as regras IR e a lógica de geração de assets continuam a ser módulos comerciais.
- **Plataforma**: posicionar o ForgeMind como plataforma de controlo MCP multi-motor, consolidando protocolo e camadas de abstração independentes do motor.

> A direção da versão comercial (sistema de autorização, suporte enterprise, etc.) não é aprofundada neste roadmap público: contactar o publisher.
