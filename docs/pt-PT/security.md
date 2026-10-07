# Segurança do ForgeMind

Este documento descreve o design de segurança e as sugestões de utilização do slice público.

## 1. Limites da comunicação local

- **O servidor escuta por predefinição apenas em loopback**: no modo HTTP escuta em `127.0.0.1:8765`, não é exposto à rede externa.
- **O bridge é TCP local**: entre o host e o adaptador do motor é uma ligação TCP local; o adaptador deve ler `%USERPROFILE%\.forgemind\bridge.json` (host, porta, credenciais) para completar discovery e autenticação antes do handshake.
- Não expor a porta do servidor na rede pública; para utilização remota, colocar numa rede de confiança e encaminhar através de um canal seguro (por exemplo, túnel SSH).

## 2. Integridade dos plugins

- O `PluginLoader` do host executa a **verificação de integridade do manifesto** (`PluginIntegrityVerifier`) e a verificação da assinatura (`PluginSigningKeys`) ao carregar plugins: o pacote deve conter a DLL principal + o `.manifest.json` homónimo; se faltar ou não corresponder, é rejeitado / saltado.
- A injeção de ferramentas dos plugins ocorre no processo interno do host; instalar apenas plugins de fontes de confiança (compilados do slice público ou de lançamentos oficiais).

## 3. Modelo de autorização

**Mecanismos de segurança divulgáveis**:
- `IAuthorizationService`: abstração do ponto de entrada de autorização do host; a implementação predefinida é `OpenAuthorizationService` (capacidades fixas da comunidade, não analisa nenhum ficheiro de licença e não gera autorizações de avaliação).
- ServiceOverride de confiança: através do ServiceOverride no manifesto, um plugin pode substituir serviços do host por tipo abstrato (o host não conhece o conceito «comercial», apenas substitui por tipo).
- Autenticação da identidade dos plugins e verificação do manifesto: `PluginIntegrityVerifier` / `PluginSigningKeys` verificam manifestos e assinaturas dos plugins, garantindo o carregamento apenas de plugins de confiança.
- Autenticação da ligação bridge: o adaptador completa discovery e handshake de credenciais via `bridge.json`, confirmando a fiabilidade da origem da ligação.

**Autorização comercial**: o Core fornece a abstração `IAuthorizationService` e o mecanismo de extensão ServiceOverride de confiança. A implementação da autorização comercial é um plugin closed source; protocolo de autorização específico, emissão, verificação e lógica de binding não fazem parte do slice público.

## 4. Permissões das ferramentas

- As ferramentas de operação no motor estão sujeitas a `ServerPermissionChecker` (dentro dos plugins) e verificam a autorização das capacidades antes da execução.
- Um `Execute` falhado das ferramentas retorna uniformemente a mensagem de erro de `ToolResult`, sem expor detalhes internos da stack.

## 5. Sugestões de utilização

- Abrir a porta MCP apenas a clientes de confiança; os clientes têm capacidade de operação no motor, equivalente a permissões de administração local.
- Manter plugins, host e projeto de exemplo atualizados para receber prontamente as correções de segurança.
- Não copiar as credenciais de `bridge.json` para processos não confiáveis em máquinas partilhadas.
- Em caso de problemas de segurança, reportar pelos canais do publisher, evitando divulgar publicamente detalhes de vulnerabilidades não corrigidas.
