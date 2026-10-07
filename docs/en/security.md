# ForgeMind Security

This document describes the security design and usage recommendations in the public slice.

## 1. Local Communication Boundary

- **The server only listens on loopback by default**: HTTP mode listens on `127.0.0.1:8765`, not exposed to the external network.
- **The bridge is local TCP**: between the host and the engine adapter; the adapter must read `%USERPROFILE%\.forgemind\bridge.json` (containing host, port, credentials) for discovery and authentication before the handshake.
- Do not expose the server port to the public internet; if remote use is required, place it on a trusted network and forward through a secure channel (such as an SSH tunnel).

## 2. Plugin Integrity

- When loading plugins, the host `PluginLoader` performs **manifest integrity verification** (`PluginIntegrityVerifier`) and signature verification (`PluginSigningKeys`): the plugin package must contain the main DLL + same-named `.manifest.json`; missing or mismatched packages are rejected / skipped.
- Plugin-injected tools run inside the host process; only install plugins from trusted sources (built from the public slice or official releases).

## 3. Authorization Model

**Publicly disclosed security mechanisms**:
- `IAuthorizationService`: the host authorization entry abstraction; the default implementation is `OpenAuthorizationService` (community fixed capability, does not parse any license files, does not generate trial licenses).
- Trusted ServiceOverride: plugins can replace host services by abstract type through ServiceOverride in the manifest (the host does not perceive the concept of "commercial", it only replaces by type).
- Plugin identity verification and manifest validation: `PluginIntegrityVerifier` / `PluginSigningKeys` verify plugin manifests and signatures to ensure only trusted plugins are loaded.
- Bridge connection authentication: adapters complete local discovery and credential handshake through `bridge.json`, confirming the connection source is trusted.

**Commercial licensing**: Core provides the `IAuthorizationService` abstraction and the trusted ServiceOverride extension mechanism. The commercial licensing implementation is a closed-source plugin; the specific licensing protocol, issuance, verification and binding logic are not part of the public slice.

## 4. Tool Permissions

- Engine operation tools are constrained by `ServerPermissionChecker` (in each plugin), checking capability authorization before execution.
- Tool `Execute` failures uniformly return a `ToolResult` failure message without leaking internal stack details.

## 5. Usage Recommendations

- Only open the MCP port to trusted clients; clients have engine operation capability, equivalent to local administrative privileges.
- Keep plugins, the host and the sample project updated to receive security fixes promptly.
- Do not copy `bridge.json` credentials to untrusted processes on shared machines.
- Report security issues through the publisher's channel; avoid publicly disclosing details of unfixed vulnerabilities.
