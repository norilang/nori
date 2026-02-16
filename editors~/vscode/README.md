# Nori Language for Visual Studio Code

Syntax highlighting, error diagnostics, autocomplete, hover info, go-to-definition, signature help, and more for the [Nori](https://nori-lang.dev) programming language.

## Features

### Syntax Highlighting

Full TextMate grammar covering all Nori constructs:
- Keywords (`if`, `else`, `while`, `for`, `return`, `break`, `continue`)
- Declarations (`let`, `pub`, `sync`, `fn`, `on`, `event`, `send`, `to`)
- Types after `:` annotations
- String interpolation (`"Hello, {name}"`)
- Nestable block comments (`/* outer /* inner */ still comment */`)
- Numeric literals (int, float, hex)
- Built-in functions (`log`, `warn`, `error`) and variables (`localPlayer`, `gameObject`, `transform`)

### Error Diagnostics

Real-time error and warning squiggles as you type, powered by the Nori LSP server. The same compiler frontend used by the Unity build, so what you see in the editor matches what you get at compile time.

### Autocomplete

Context-aware completions triggered by:
- **`.`** — Type members (methods, properties) based on the resolved type
- **`:`** — Known type names for type annotations
- **`on`** — VRChat event names (Start, Update, Interact, PlayerJoined, etc.)
- **`sync`** — Sync modes (none, linear, smooth)
- **`to`** — Send targets (All, Owner)
- **Statements** — In-scope variables, functions, and keywords

### Hover Information

Hover over any symbol to see:
- Variables: name, type, and sync mode
- Functions: full signature with parameter types and return type
- Methods/Properties: extern signature and Udon mapping
- Events: event name and available parameters

### Go-to-Definition

Ctrl+Click (or F12) to jump to:
- Variable declarations
- Function definitions
- Custom event handlers (from `send` statements)

### Signature Help

When typing function or method calls, see parameter hints with:
- Parameter names and types
- Active parameter highlighting
- All available overloads

### Document Outline

The Outline panel (and breadcrumbs) shows all top-level declarations:
- Variables (with types)
- Functions (with parameter lists)
- Event handlers (`on Start`, `on Update`, etc.)
- Custom events (`event Reset`, etc.)

### Code Snippets

10 built-in snippets for common patterns:

| Prefix | Expands to |
|--------|-----------|
| `on` | Event handler (`on Start { }`) |
| `fn` | Function declaration |
| `event` | Custom event declaration |
| `let` | Variable declaration |
| `pub` | Public variable (Unity Inspector) |
| `sync` | Synced variable (networking) |
| `if` | If statement |
| `for` | For-in range loop |
| `while` | While loop |
| `send` | Send network event |

## Setup

### 1. Install the Extension

**From source** (until published on the Marketplace):

```bash
cd editors~/vscode
npm install
npm run compile
npx vsce package
# Install the generated .vsix: Extensions panel > ... > Install from VSIX
```

### 2. Get the LSP Server

The extension works in two modes:

- **Without LSP server:** Syntax highlighting and snippets only (still useful!)
- **With LSP server:** Full diagnostics, completions, hover, go-to-definition, and signature help

To get the LSP server, either download a release binary or build from source:

```bash
cd tools~/
dotnet publish Nori.Lsp -r win-x64 -c Release
# Binary: Nori.Lsp/bin/Release/net8.0/win-x64/publish/nori-lsp.exe
```

Replace `win-x64` with `osx-x64`, `osx-arm64`, or `linux-x64` as needed.

### 3. Configure the Extension

Place the `nori-lsp` binary in the extension's `server/` folder, or set the path in VS Code settings:

| Setting | Description | Default |
|---------|-------------|---------|
| `nori.lsp.path` | Path to the `nori-lsp` binary | `""` (uses bundled `server/nori-lsp`) |
| `nori.catalog.path` | Path to `extern-catalog.json` for full VRC type info | `""` (uses built-in catalog) |

### 4. Generate the Extern Catalog (Optional)

For full VRChat type coverage (all whitelisted types, methods, properties, and enums):

1. Open your VRChat Unity project.
2. Go to **Tools > Nori > Generate Extern Catalog**.
3. Save the generated JSON file.
4. Set `nori.catalog.path` in VS Code settings to the saved file path.

## Troubleshooting

- **No syntax highlighting:** Ensure the extension is installed and the file has a `.nori` extension.
- **No error squiggles or completions:** The LSP server is not running. Check the Output panel (**View > Output**, select "Nori Language Server") for errors. Verify the server binary path.
- **"Nori language server not found" message:** Set `nori.lsp.path` in settings or place the binary in the extension's `server/` directory.
- **Missing type information (e.g., no completions after `.`):** Generate the extern catalog in Unity and set `nori.catalog.path`.
- **Stale diagnostics after large edits:** The server re-analyzes with a 200ms debounce. If issues persist, close and reopen the file.

## Development

```bash
# Install dependencies
npm install

# Compile TypeScript
npm run compile

# Watch mode (recompiles on save)
npm run watch

# Package as .vsix
npm run package
```
