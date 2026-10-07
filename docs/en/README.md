# ForgeMind Documentation (English)

ForgeMind — a game engine control platform with MCP as its interface: it exposes Unreal Engine project management, asset reading, launch/build/editor operations and other capabilities to AI clients through the MCP protocol. The current implementation targets UE (5.0–5.8), and the platform design is extensible to other engines.

> This documentation covers the **public open-source slice**: `OpenSource\` (host and plugins), `UEProject\` (sample project), `tests\` (tests). Commercial capabilities (DSL asset generation, commercial licensing implementation) are only outlined here and are not part of the OpenSource release; OpenSource retains the necessary licensing abstractions and open implementations.

## Documentation Index

| Document | Content |
|---|---|
| [Getting Started](getting-started.md) | Environment requirements, build, launch, first use |
| [Architecture](architecture.md) | Host / Plugins / Bridge Protocol / Scan and SPK |
| [API Reference](api-reference.md) | MCP protocol, bridge protocol, tool contracts |
| [Tools Reference](tools-reference.md) | All MCP tools and parameters |
| [Development Guide](development-guide.md) | Build, debug, adding tools / adapters, tests |
| [Contributing Guide](contributing.md) | Open-source boundaries, PR process, testing requirements |
| [Troubleshooting](troubleshooting.md) | Common problems and solutions |
| [Roadmap](roadmap.md) | Planned directions |
| [Promo](promo.md) | Project introduction and external communication discipline |
| [Security](security.md) | Security design and best practices |

## Open-Source Scope Overview

```
OpenSource\
  ForgeMindServer               MCP host (4 built-in tools + plugin loading + TCP bridge)
  ForgeMind.OfflineEngineKit    Engine/project/launch/build/editor tools (plugin)
  ForgeMind.Assets              .uasset offline reading (plugin)
  ForgeMind.Knowledge           Knowledge retrieval (plugin)
  ForgeMind.Scan.Abstractions   Scan data and Provider abstractions
  ForgeMind.Scan               SPK reading and packaging
  ForgeMind.Scan.Unreal        Unreal Engine scan implementation
  ForgeMind.EngineScanner      Standalone scanning CLI that directly outputs .spk
  ForgeMind.Speedy             SPK low-level infrastructure
UEProject\ForgeMind_5_0         Sample UE project (5.0–5.8)
tests\                          Public unit tests + server integration tests
```

## License

The OpenSource release content is licensed under Apache-2.0. Commercial modules are not included in the OpenSource release and are licensed under a separate commercial license.
