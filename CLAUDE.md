# Nori Compiler — Project Instructions

## Overview
Nori is a programming language for VRChat worlds that compiles to Udon Assembly. This package is a Unity Editor package (UPM) containing a pure C# compiler and Unity integration layer.

## Architecture

### Compiler Pipeline
Lexer → Parser → SemanticAnalyzer → IrLowering → UdonEmitter

All compiler code lives in `Editor/Compiler/` with no Unity dependencies. Unity integration code is in `Editor/Integration/` guarded by `#if UNITY_EDITOR`.

### Namespaces
- `Nori.Compiler` — compiler core
- `Nori` — Unity integration

### Key Files
- `Editor/Compiler/Lexer.cs` — tokenization
- `Editor/Compiler/Parser.cs` — AST construction
- `Editor/Compiler/SemanticAnalyzer.cs` — type checking, scope resolution
- `Editor/Compiler/IR/IrLowering.cs` — AST → IR instructions
- `Editor/Compiler/Emit/UdonEmitter.cs` — IR → Udon Assembly text
- `Editor/Compiler/Catalog/BuiltinCatalog.cs` — hardcoded ~50 VRChat/Unity externs
- `Editor/Compiler/Catalog/FullCatalog.cs` — full extern catalog from JSON
- `Editor/Compiler/NoriCompiler.cs` — orchestrates full pipeline + LSP analysis
- `Editor/Integration/NoriImporter.cs` — Unity ScriptedImporter
- `Tests/Editor/IntegrationTests.cs` — end-to-end compilation tests

### External Tools (outside Unity)
- `tools~/Nori.Lsp/` — LSP server (.NET 8 console app)
- `tools~/Nori.Lsp.Tests/` — LSP integration tests (NUnit)
- `editors~/vscode/` — VS Code extension (TextMate grammar + LSP client)

## Build & Test

### LSP Tests (runs outside Unity)
```bash
cd tools~/Nori.Lsp.Tests
dotnet test
```

### Unity Integration Tests
Run via Unity Test Runner (Window > General > Test Runner) — tests are in `Tests/Editor/`.

### VS Code Extension
```bash
cd editors~/vscode
npm install && npm run compile
```

## Design Decisions

- **No VRChat SDK compile-time reference** — all VRC types accessed via reflection at catalog-scrape time
- **`IExternCatalog` interface** — `BuiltinCatalog` (hardcoded) for tests/minimal setup, `FullCatalog` (from JSON) for full VRC support
- **Reflection-based catalog** — `CatalogScraper` generates JSON via Tools > Nori > Generate Extern Catalog
- **Manual JSON parsing** — FullCatalog parses catalog JSON without Newtonsoft dependency
- **No short-circuit `&&`/`||`** — uses EXTERN-based boolean evaluation
- **Identifiers, not keywords** — `none`, `linear`, `smooth`, `All`, `Owner` are identifiers
- **Heap-only memory** — Udon VM has no local variables and no call stack
- **Function calls** — use `JUMP_INDIRECT` with return address stored in a heap variable
- **Implicit conversions** — int→float, int→double, float→double via `SystemConvert` externs

## Udon VM Constraints

- Events must end with `JUMP, 0xFFFFFFFC` (halt sentinel)
- Boolean heap variables must use `null` initial value (compute `true` at runtime)
- 9 opcodes: NOP(4B), PUSH(8B), POP(4B), JUMP_IF_FALSE(8B), JUMP(8B), EXTERN(8B), ANNOTATION(8B), JUMP_INDIRECT(8B), COPY(4B)
- All computation via EXTERN (whitelisted .NET method calls)
- Extern signature format: `TypeName.__MethodName__ParamType1_ParamType2__ReturnType`

## Conventions

- Compiler core must stay free of Unity dependencies
- Unity integration uses `#if UNITY_EDITOR` guards
- Tests use `BuiltinCatalog` (no JSON file needed)
- Enum values stored as typed constants (e.g., `%UnityEngineSpace, 1`)
- Variable name collisions handled by `DeclareHeapVar` renaming + `_varNameMap` tracking
