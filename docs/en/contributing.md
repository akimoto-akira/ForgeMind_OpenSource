# ForgeMind Contributing Guide

Thank you for your interest in ForgeMind. The public repository corresponds to the OpenSource release slice of ForgeMind and only contains the open-source projects, tests and sample projects released publicly. Commercial modules (DSL domain translators, licensing systems, etc.) are not in the public repository; please understand the scope division before contributing.

## 1. Public Repository Structure

The top-level structure of the open-source slice (the repository you clone) is as follows:

```
ForgeMind.sln                          # Solution (contains references to commercial projects, see below)
OpenSource\                            # Open-source projects
  ForgeMindServer\                     # MCP server (host)
  ForgeMind.OfflineEngineKit\          # Engine offline tools plugin (engine/project/launch/build/editor etc.)
  ForgeMind.Assets\                    # Asset reading plugin (asset)
  ForgeMind.Knowledge\                 # Knowledge retrieval plugin (knowledge)
  ForgeMind.EngineScanner\             # Engine scanner (command line)
  ForgeMind.Scan\                      # Scan packaging
  ForgeMind.Scan.Abstractions\         # Scan abstractions
  ForgeMind.Scan.Unreal\               # UE scan implementation (DocumentEngine)
  ForgeMind.Speedy\                    # SPK packaging format
UEProject\
  ForgeMind_5_0\                       # Sample UE project (5.0–5.8 compatible)
tests\
  ForgeMind.PublicTests\               # Public unit tests (SPK / scan data)
  ForgeMindServerTest\                 # Server integration tests (requires UE editor)
```

> Note: the solution file of the current development repository (`ForgeMind.sln`) contains Commercial project references (such as `Commercial\ForgeMindLicenseGenerator`, `Commercial\ForgeMindLicenseServer`); the public release **does not include the `Commercial/` directory**, so OpenSource users should build each public project individually (see §3) and must not `dotnet build ForgeMind.sln` directly.

## 2. Open-Source Boundaries

- **OpenSource\**: all open source (Apache-2.0), the main area for contributions.
- **UEProject\**: the sample project is open source, but `*.uasset` binary assets are excluded during slice export (only text assets such as maps and the directory skeleton are kept under Content).
- **tests\**: open source; contributions require accompanying tests.
- **Commercial is not in the public repository**: DSL domain translators, commercial licensing, etc. do not appear in the slice; open-source code is prohibited from referencing commercial module types.
- Export automatically excludes build artifacts and temporary directories such as `bin` / `obj` / `Binaries` / `Intermediate` / `Saved` / `DerivedDataCache`; license identifiers in file headers are uniformly replaced with Apache-2.0 during export.

## 3. Build

```powershell
# Build individually (do not build the whole sln directly, see above)
dotnet build OpenSource\ForgeMindServer\ForgeMindServer.csproj
dotnet build OpenSource\ForgeMind.Scan.Abstractions\ForgeMind.Scan.Abstractions.csproj
dotnet build OpenSource\ForgeMind.Scan\ForgeMind.Scan.csproj
dotnet build OpenSource\ForgeMind.Scan.Unreal\ForgeMind.Scan.Unreal.csproj
dotnet build OpenSource\ForgeMind.Speedy\ForgeMind.Speedy.csproj
# Plugin projects (after building, place the output + same-named .manifest.json into the host plugins directory)
dotnet build OpenSource\ForgeMind.OfflineEngineKit\ForgeMind.OfflineEngineKit.csproj
dotnet build OpenSource\ForgeMind.Assets\ForgeMind.Assets.csproj
dotnet build OpenSource\ForgeMind.Knowledge\ForgeMind.Knowledge.csproj
dotnet build OpenSource\ForgeMind.EngineScanner\ForgeMind.EngineScanner.csproj
```

The host loads plugins from `<host directory>\plugins\` (overridable with the environment variable `FORGEMIND_PLUGINS_DIR`); a plugin package = main DLL + same-named `.manifest.json` + dependencies (see `development-guide.md` §3).

## 4. Code Standards

- All source files keep the SPDX license identifier and copyright notice at the top (currently Apache-2.0).
- Namespaces are organized by module (`ForgeMindServer.*` / `ForgeMind.*`); public contracts go through abstract interfaces such as `ITool` / `IPlugin`.
- Server tool classes must provide a complete `Description` and parameter Schema (these texts are exposed directly to AI clients).
- Engine adapter code follows the corresponding engine coding standards (current UE side: type prefixes F / U / T, `TEXT()` macro, game thread safety constraints).
- New code must not reference or depend on any commercial module.

## 5. PR Process

1. Create a branch from `main`; naming suggestion `feat/<description>` or `fix/<description>`.
2. Commit message: `<type>: <one-line description>`.
3. Pre-commit self-check:
   - Build individually per §3, all passing;
   - When bridge command / tool behavior changes, the relevant `ForgeMindServerTest` Groups pass (run method in `development-guide.md` §6);
   - When SPK / scan data changes, `ForgeMind.PublicTests` pass;
   - No references to commercial modules introduced;
   - Synchronously update the corresponding document in `docs\zh-CN\`.
4. When opening the PR, fill in: change content, test result summary, and whether maintainer review of specific modules is needed.
5. Maintainer review focus: open-source boundaries, API compatibility, test coverage.

## 6. Testing Requirements

- Bridge command / tool behavior / adapter logic changes must be accompanied by test cases (add `*_Tests.cs` under `tests\ForgeMindServerTest\Task\`).
- Server integration tests require the UE editor environment; pure data processing tests (SPK, scanning) go under `tests\ForgeMind.PublicTests\`, no engine dependency.
- Cases must pass stably in the default order (fail-fast, stop at the first failure).

## 7. Documentation Contributions

- The Chinese documentation is located in `docs\zh-CN\`; content must be consistent with the source code facts; mark uncertain items as "to be confirmed".
- When changing the external tool list, update `tools-reference.md` accordingly.
- Commercial capabilities (DSL syntax, licensing mechanisms) are only outlined in the documentation; internal details are not expanded.

## 8. Code of Conduct

- Respect existing design decisions (bridge protocol, tool contracts, SPK format); initiate discussion first for major changes.
- Do not disparage competitors externally; promotional content follows the external communication discipline in `promo.md`.
