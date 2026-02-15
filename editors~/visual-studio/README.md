# Nori Language Support for Visual Studio

## Overview

Visual Studio can use the Nori LSP server for language features. There are two setup approaches:

## Option 1: Manual LSP Configuration (Recommended)

Visual Studio 2022 17.8+ supports generic LSP clients.

### 1. Get the LSP Server Binary

Download from [Nori releases](https://github.com/norilang/nori/releases) or build from source:

```bash
cd tools~
dotnet publish Nori.Lsp/Nori.Lsp.csproj -r win-x64 -c Release
```

### 2. Create a `.noriconfig` file

In your solution or project root, create a file to help Visual Studio recognize `.nori` files:

```json
{
  "language": "nori",
  "extensions": [".nori"],
  "lspServer": "C:\\path\\to\\nori-lsp.exe",
  "lspServerArgs": ["--catalog", "C:\\path\\to\\extern-catalog.json"]
}
```

### 3. File Association

1. In Visual Studio, go to **Tools > Options > Text Editor > File Extension**.
2. Add `.nori` with **Editor: Source Code (Text) Editor**.

## Option 2: VS Code Extension in Visual Studio

Visual Studio 2022 supports some VS Code extensions. The Nori VS Code extension (in `editors~/vscode/`) may work with limited functionality through this compatibility layer.

## Features Available

With the LSP server connected:

- Error squiggles for syntax and semantic errors
- Autocomplete for types, events, sync modes, and member access
- Hover information for variables, functions, and methods
- Go-to-definition
- Signature help during function calls
- Document outline

## Building from Source

```bash
cd tools~
dotnet publish Nori.Lsp/Nori.Lsp.csproj -r win-x64 -c Release --self-contained true -p:PublishSingleFile=true
```

The self-contained binary will be in `Nori.Lsp/bin/Release/net8.0/win-x64/publish/nori-lsp.exe`.

## Troubleshooting

- **No IntelliSense:** Ensure the LSP server path is correct and the server is running. Check **View > Output > Language Server** for logs.
- **Missing type information:** Generate the extern catalog using **Tools > Nori > Generate Extern Catalog** in Unity and pass its path via `--catalog`.
