# Documentação ForgeMind (português)

ForgeMind — plataforma de controlo de motores de jogos baseada em MCP: através do protocolo MCP, fornece a clientes de IA a gestão de projetos Unreal Engine, leitura de ativos e operações de arranque/compilação/edição. A implementação atual é UE (5.0–5.8); a arquitetura da plataforma é extensível a outros motores.

> Esta documentação centra-se na parte **open source**: `OpenSource\` (host e plugins), `UEProject\` (projeto de exemplo) e `tests\` (testes). As funcionalidades comerciais (geração de ativos DSL, implementação de licenças comerciais) são descritas apenas de forma geral e não estão incluídas na versão open source. O OpenSource mantém as abstrações de licença necessárias e a implementação aberta.

## Índice da documentação

| Documento | Conteúdo |
|---|---|
| [Início rápido](getting-started.md) | Requisitos de ambiente, compilação, arranque, primeira utilização |
| [Arquitetura](architecture.md) | Host / plugins / protocolo bridge / scan e SPK |
| [Referência de API](api-reference.md) | Protocolo MCP, protocolo bridge, contratos de ferramentas |
| [Referência de ferramentas](tools-reference.md) | Todas as ferramentas MCP e respetivos parâmetros |
| [Guia de desenvolvimento](development-guide.md) | Compilação, depuração, adição de ferramentas/adaptadores, testes |
| [Guia de contribuição](contributing.md) | Limites open source, processo PR, requisitos de teste |
| [Resolução de problemas](troubleshooting.md) | Problemas comuns e soluções |
| [Roteiro](roadmap.md) | Direção planeada |
| [Promoção](promo.md) | Apresentação do projeto e regras de comunicação |
| [Segurança](security.md) | Conceção de segurança e boas práticas |

## Visão geral do âmbito open source

```
OpenSource\
  ForgeMindServer               Host MCP (4 ferramentas incorporadas + carregamento de plugins + bridge TCP)
  ForgeMind.OfflineEngineKit    Ferramentas de motor/projeto/arranque/compilação/edição (plugin)
  ForgeMind.Assets              Leitura offline de .uasset (plugin)
  ForgeMind.Knowledge           Pesquisa de conhecimento (plugin)
  ForgeMind.Scan.Abstractions   Abstrações de dados de scan e fornecedores
  ForgeMind.Scan                Leitura e empacotamento SPK
  ForgeMind.Scan.Unreal         Implementação de scan Unreal Engine
  ForgeMind.EngineScanner       Scanner CLI autónomo que gera diretamente .spk
  ForgeMind.Speedy              Base SPK de baixo nível
UEProject\ForgeMind_5_0         Projeto UE de exemplo (5.0–5.8)
tests\                          Testes públicos unitários + testes de integração do servidor
```

## Licença

O conteúdo da parte open source está licenciado sob Apache-2.0. Os módulos comerciais não fazem parte do open source e são fornecidos sob uma licença comercial separada.
