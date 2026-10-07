# ForgeMind Promo

## One-Line Introduction

ForgeMind is a game engine control platform with MCP as its interface: it exposes Unreal Engine project management, asset reading, launch / build / editor operations to AI clients, letting AI operate the engine like a tool.

## Core Highlights

- **Native MCP access**: standard MCP protocol (HTTP / stdio), mainstream AI clients work out of the box.
- **Self-buildable open-source slice**: the public repository contains the host, all open-source plugins, a sample UE project and tests, Apache-2.0, self-buildable and extensible.
- **Engine adapter architecture**: the host and the engine are decoupled through a local TCP bridge, extensible to engines beyond UE.
- **Offline asset reading**: parse `.uasset` structure without opening the editor; combined with scan indexes (SPK) provides knowledge retrieval capabilities.
- **Plugin-based capability injection**: the host only bundles a minimal toolset; capabilities are extended by plugins, with clear, cuttable boundaries.

## Commercial Edition (One Line)

The commercial edition provides DSL asset generation, commercial licensing capabilities and enterprise support on top of OpenSource; contact the publisher for DSL syntax details and licensing terms.

## External Communication Discipline

- Do not promise unreleased features; the roadmap is governed by `roadmap.md`.
- Do not disparage similar tools; do not disclose commercial module internals externally (licensing algorithms, DSL internal IR rules, etc.).
- For anything involving source code behavior, always follow the public slice; mark uncertain items as "to be confirmed".
