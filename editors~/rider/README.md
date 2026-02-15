# Nori Language Support for JetBrains Rider

## Prerequisites

- JetBrains Rider 2023.2 or later (built-in LSP client support)
- The Nori LSP server binary (`nori-lsp` / `nori-lsp.exe`)

## Setup

### 1. Get the LSP Server Binary

Download the platform-appropriate binary from the [Nori releases page](https://github.com/norilang/nori/releases), or build from source:

```bash
cd tools~
dotnet publish Nori.Lsp/Nori.Lsp.csproj -r win-x64 -c Release
# Binary: Nori.Lsp/bin/Release/net8.0/win-x64/publish/nori-lsp.exe
```

Replace `win-x64` with `osx-x64`, `osx-arm64`, or `linux-x64` as needed.

### 2. Configure Rider's LSP Client

1. Open **Settings > Languages & Frameworks > Language Servers**.
2. Click **Add** (+) to create a new configuration.
3. Set the following:
   - **Name:** Nori
   - **Server executable:** Path to `nori-lsp` binary
   - **File patterns:** `*.nori`
   - **Arguments:** (optional) `--catalog /path/to/extern-catalog.json`

### 3. TextMate Grammar (Syntax Highlighting)

Rider supports TextMate grammars via the **TextMate Bundles** plugin:

1. Install the **TextMate Bundles** plugin from the JetBrains Marketplace.
2. Go to **Settings > Editor > TextMate Bundles**.
3. Click **Add** and select the `editors~/vscode/` directory from this repository (it contains the `.tmLanguage.json` grammar).
4. Restart Rider.

### 4. File Type Registration

If `.nori` files aren't automatically recognized:

1. Go to **Settings > Editor > File Types**.
2. Under **Recognized File Types**, click **Add** (+).
3. Name the file type `Nori` and add `*.nori` as a pattern.

## Features

With the LSP server running, you get:

- Error diagnostics (red underlines) within ~200ms
- Autocomplete after `.` (type members), `:` (types), `on` (events)
- Hover info showing types, signatures, and extern mappings
- Go-to-definition for variables, functions, and custom events
- Signature help during function/method calls
- Document outline showing all declarations

## Troubleshooting

- **Server not starting:** Check the Rider Event Log for errors. Ensure the binary has execute permissions on macOS/Linux (`chmod +x nori-lsp`).
- **No syntax highlighting:** Verify the TextMate bundle path points to the correct directory.
- **Catalog not loaded:** Pass `--catalog /path/to/catalog.json` in the server arguments. Generate the catalog using **Tools > Nori > Generate Extern Catalog** in Unity.
