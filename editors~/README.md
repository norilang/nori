# Nori Editor Support

Editor extensions and setup guides for the Nori language. All editors use the same LSP server (`nori-lsp`) built from `tools~/Nori.Lsp/`.

## Editors

| Editor | Type | Directory |
|--------|------|-----------|
| **[VS Code](vscode/)** | Extension (TextMate + LSP client) | `vscode/` |
| **[JetBrains Rider](rider/)** | Setup guide (built-in LSP client) | `rider/` |
| **[Visual Studio](visual-studio/)** | Setup guide (generic LSP client) | `visual-studio/` |

## Feature Matrix

| Feature | VS Code | Rider | Visual Studio |
|---------|---------|-------|---------------|
| Syntax highlighting | TextMate grammar | TextMate bundle (via plugin) | File association |
| Error diagnostics | LSP | LSP | LSP |
| Autocomplete | LSP (`.` `:` triggers) | LSP | LSP |
| Hover info | LSP | LSP | LSP |
| Go-to-definition | LSP | LSP | LSP |
| Signature help | LSP | LSP | LSP |
| Document outline | LSP | LSP | LSP |
| Code snippets | 10 built-in | Manual | Manual |

## Getting the LSP Server

All editors need the `nori-lsp` binary. Build from `tools~/`:

```bash
# Debug (for development)
dotnet run --project tools~/Nori.Lsp

# Release binary (self-contained, no .NET runtime needed)
dotnet publish tools~/Nori.Lsp -r win-x64 -c Release
```

See [tools~/README.md](../tools~/README.md) for full build instructions and platform options.
