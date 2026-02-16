# Nori Development Tools

This directory contains .NET tooling for the Nori language, built outside of Unity using the `~` convention (invisible to the Unity Editor).

## Projects

| Project | Target | Description |
|---------|--------|-------------|
| `Nori.Compiler/` | netstandard2.1 | Shared compiler library — references `Editor/Compiler/**/*.cs` via `<Compile Include>`, zero file duplication |
| `Nori.Lsp/` | net8.0 | LSP server (`nori-lsp`) — provides editor features over stdio JSON-RPC |
| `Nori.Lsp.Tests/` | net8.0 | Integration tests for all LSP handlers (27 tests) |

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later

## Quick Start

```bash
# Build everything
dotnet build nori.sln

# Run the LSP server (editors connect via stdio)
dotnet run --project Nori.Lsp

# Run with an extern catalog for full VRChat type information
dotnet run --project Nori.Lsp -- --catalog /path/to/extern-catalog.json

# Run tests
dotnet test Nori.Lsp.Tests
```

## Building Release Binaries

The LSP server publishes as a self-contained single-file binary (no .NET runtime required on the target machine):

```bash
# Windows
dotnet publish Nori.Lsp -r win-x64 -c Release

# macOS (Intel)
dotnet publish Nori.Lsp -r osx-x64 -c Release

# macOS (Apple Silicon)
dotnet publish Nori.Lsp -r osx-arm64 -c Release

# Linux
dotnet publish Nori.Lsp -r linux-x64 -c Release
```

Binaries are output to `Nori.Lsp/bin/Release/net8.0/{rid}/publish/nori-lsp{.exe}`.

---

## Nori LSP Server

### Overview

`nori-lsp` is a [Language Server Protocol](https://microsoft.github.io/language-server-protocol/) server for the Nori language. It communicates over stdio using JSON-RPC with content-length headers (the standard LSP transport), making it compatible with any editor that supports LSP.

The server reuses the same compiler frontend that Unity uses — lexer, parser, and semantic analyzer — so diagnostics in your editor match exactly what the Unity compiler produces.

### Command-Line Arguments

| Argument | Description |
|----------|-------------|
| `--catalog <path>` | Path to `extern-catalog.json` for full VRChat type information. Without this, the server falls back to the built-in catalog (~50 common types). Generate the full catalog in Unity via **Tools > Nori > Generate Extern Catalog**. |

### Supported LSP Features

| Feature | Trigger | Description |
|---------|---------|-------------|
| **Diagnostics** | On open/change | Error and warning squiggles with 200ms debounce |
| **Completion** | `.` `:` typing | Type members after `.`, type names after `:`, event names after `on`, sync modes after `sync`, send targets after `to`, keywords, and in-scope variables/functions |
| **Hover** | Mouse hover | Type info for variables, function signatures, event descriptions, extern mappings |
| **Go-to-Definition** | Ctrl+Click | Jump to variable declarations, function definitions, custom event handlers |
| **Signature Help** | `(` `,` | Parameter hints with overload support during function/method calls |
| **Document Symbols** | Outline panel | Lists all variables, functions, event handlers, and custom events |

### Architecture

```
Program.cs                    Entry point, stdio JSON-RPC setup
Server/
  NoriLanguageServer.cs       LSP method routing, lifecycle management
  DocumentManager.cs          Open-file cache, debounced re-analysis
  DocumentState.cs            Per-document: text, AST, diagnostics, type/scope maps
Handlers/
  DiagnosticsPublisher.cs     Pushes diagnostics to the editor
  CompletionHandler.cs        Context-aware autocomplete
  HoverHandler.cs             Markdown hover tooltips
  DefinitionHandler.cs        Go-to-definition
  SignatureHelpHandler.cs     Parameter hints
  DocumentSymbolHandler.cs    Document outline
Utilities/
  PositionMapper.cs           LSP (0-based) <-> Nori (1-based) conversion
  LspDiagnosticConverter.cs   Compiler diagnostics -> LSP diagnostics
  AstNodeFinder.cs            Position-based AST node lookup
```

### How It Works

1. The editor opens a `.nori` file and sends `textDocument/didOpen` with the full source text.
2. `DocumentManager` runs the compiler frontend (`NoriCompiler.AnalyzeForLsp`) which lexes, parses, and analyzes the source **without early termination** — collecting all possible diagnostics and type information even when errors exist.
3. Diagnostics are pushed back to the editor immediately.
4. When the user types, `textDocument/didChange` triggers re-analysis after a 200ms debounce.
5. Feature requests (hover, completion, etc.) use the cached AST, type map, and scope map from the latest analysis pass.

### Extern Catalog

Without `--catalog`, the server uses `BuiltinCatalog` which knows ~50 common VRChat types (Transform, GameObject, Vector3, etc.). For full type coverage:

1. Open your VRChat Unity project.
2. Go to **Tools > Nori > Generate Extern Catalog**.
3. Save the generated `extern-catalog.json`.
4. Pass its path to the LSP server with `--catalog`.

This gives the server access to every VRChat-whitelisted type, method, property, and enum — enabling accurate completions, hover info, and type checking.
