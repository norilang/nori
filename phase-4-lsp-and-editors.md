# Phase 4: LSP Server and Editor Support

You are building an LSP (Language Server Protocol) server for **Nori**, a VRChat programming language. The compiler is written in C# and runs inside Unity (Phases 1–2). The LSP server is a **standalone C# console application** that reuses the compiler's frontend (lexer, parser, semantic analyzer) to provide real-time feedback in any LSP-compatible editor.

Read `DESIGN.md` section 9 for the LSP architecture.

## Architecture

```
Editor (VS Code / Rider / Visual Studio)
  │
  │ stdio (JSON-RPC)
  │
  ▼
Nori LSP Server (C# console app)
  ├── LSP Protocol Handler (Microsoft.VisualStudio.LanguageServer.Protocol)
  ├── Document Manager (open file cache, incremental analysis)
  └── Compiler Frontend (shared code with Unity package)
        ├── Lexer
        ├── Parser
        ├── Semantic Analyzer
        └── Extern Catalog
```

The LSP server is a separate project that **references the compiler code as a shared library**. The compiler's core logic (lexer, parser, AST, semantic analyzer, catalog, diagnostics) lives in a shared assembly that both the Unity Editor package and the LSP server reference.

This means: fix a parser bug once, and both Unity and the LSP server get the fix.

### Project Structure

```
nori/
  Nori.Compiler/                      // Shared library (netstandard2.1)
    Nori.Compiler.csproj
    Source/
      SourceSpan.cs
      Token.cs
      Lexer.cs
    Parsing/
      AstNodes.cs
      Parser.cs
    Analysis/
      TypeSystem.cs
      Scope.cs
      SemanticAnalyzer.cs
      TypedAst.cs
    IR/
      IrNodes.cs
      IrLowering.cs
    CodeGen/
      UdonEmitter.cs
    Diagnostics/
      Diagnostic.cs
      DiagnosticBag.cs
      ErrorDatabase.cs
    Catalog/
      ExternCatalog.cs
      CatalogTypes.cs
      BuiltinCatalog.cs
    Pipeline/
      NoriCompiler.cs
      CompileResult.cs

  Nori.Lsp/                           // LSP server (net8.0 console app)
    Nori.Lsp.csproj
    Program.cs                        // Entry point, stdio transport
    Server/
      NoriLanguageServer.cs           // Capability registration, request routing
      DocumentManager.cs              // Open file cache, debounced analysis
    Handlers/
      DiagnosticsHandler.cs           // Publish diagnostics on file change
      CompletionHandler.cs            // Autocomplete
      HoverHandler.cs                 // Hover information
      DefinitionHandler.cs            // Go-to-definition
      SignatureHelpHandler.cs         // Parameter hints during calls
      DocumentSymbolHandler.cs        // Outline / breadcrumbs
      SemanticTokensHandler.cs        // Semantic highlighting (optional, enhances TextMate)
    Utilities/
      PositionMapper.cs               // LSP position (0-based) ↔ Nori position (1-based)
      DebounceTimer.cs                // Debounce analysis after keystrokes

  Nori.Unity/                         // Unity Editor package
    Editor/
      dev.nori.compiler.editor.asmdef
      Integration/
        NoriImporter.cs
        ...
      CompilerRef/                    // Compiled Nori.Compiler.dll (or source link)
        Nori.Compiler.dll

  editors/
    vscode/                           // VS Code extension
      package.json
      src/
        extension.ts
      syntaxes/
        nori.tmLanguage.json
      snippets/
        nori.json
      language-configuration.json
    rider/                            // JetBrains Rider plugin config
      README.md                       // Instructions for LSP setup in Rider
    visual-studio/                    // Visual Studio extension (VSIX) or config
      README.md                       // Instructions for LSP setup in VS

  nori.sln                            // Solution file
```

### Shared Compiler Library

`Nori.Compiler.csproj` targets `netstandard2.1` so it can be referenced by:
- The LSP server (net8.0)
- Unity Editor scripts (Unity's .NET runtime supports netstandard2.1)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

For Unity, either:
- Include the compiled `Nori.Compiler.dll` in the Unity package, or
- Include the source files directly in the Unity Editor assembly (via a symlink or copy step in the build).

The second approach is simpler for Unity development but means changes need to be synced. The first is cleaner for distribution. Choose whichever is more practical and document the build process.

## Part A: LSP Server Core

### Dependencies

```xml
<!-- Nori.Lsp.csproj -->
<ItemGroup>
  <PackageReference Include="Microsoft.VisualStudio.LanguageServer.Protocol" Version="17.*" />
  <PackageReference Include="StreamJsonRpc" Version="2.*" />
  <ProjectReference Include="../Nori.Compiler/Nori.Compiler.csproj" />
</ItemGroup>
```

`Microsoft.VisualStudio.LanguageServer.Protocol` provides the LSP type definitions. `StreamJsonRpc` provides the JSON-RPC transport layer.

### Program.cs (Entry Point)

```csharp
using StreamJsonRpc;

class Program
{
    static async Task Main(string[] args)
    {
        // Parse args: --stdio (default), --catalog <path>
        var catalogPath = ParseCatalogArg(args);

        var server = new NoriLanguageServer(catalogPath);

        // Connect via stdio
        using var rpc = new JsonRpc(Console.OpenStandardOutput(), Console.OpenStandardInput());
        rpc.AddLocalRpcTarget(server);
        rpc.StartListening();

        await server.WaitForShutdown();
    }
}
```

### NoriLanguageServer.cs

Register capabilities and route LSP requests:

```csharp
public class NoriLanguageServer
{
    private DocumentManager _documents;
    private ExternCatalog _catalog;

    [JsonRpcMethod("initialize")]
    public InitializeResult Initialize(InitializeParams @params)
    {
        return new InitializeResult
        {
            Capabilities = new ServerCapabilities
            {
                TextDocumentSync = new TextDocumentSyncOptions
                {
                    OpenClose = true,
                    Change = TextDocumentSyncKind.Incremental,
                    Save = new SaveOptions { IncludeText = true }
                },
                CompletionProvider = new CompletionOptions
                {
                    TriggerCharacters = new[] { ".", ":" },
                    ResolveProvider = false
                },
                HoverProvider = true,
                DefinitionProvider = true,
                SignatureHelpProvider = new SignatureHelpOptions
                {
                    TriggerCharacters = new[] { "(", "," }
                },
                DocumentSymbolProvider = true,
                SemanticTokensOptions = new SemanticTokensOptions
                {
                    Full = true,
                    Legend = BuildTokenLegend()
                }
            }
        };
    }
}
```

### DocumentManager.cs

Manages the state of all open `.nori` files.

```csharp
public class DocumentManager
{
    private readonly ConcurrentDictionary<string, DocumentState> _documents = new();
    private readonly ExternCatalog _catalog;
    private readonly Action<string, List<Diagnostic>> _publishDiagnostics;

    public void OnDocumentOpened(string uri, string text)
    {
        var state = new DocumentState(uri, text);
        _documents[uri] = state;
        AnalyzeDebounced(uri);
    }

    public void OnDocumentChanged(string uri, TextDocumentContentChangeEvent[] changes)
    {
        if (_documents.TryGetValue(uri, out var state))
        {
            state.ApplyChanges(changes);  // Incremental text update
            AnalyzeDebounced(uri);
        }
    }

    public void OnDocumentClosed(string uri)
    {
        _documents.TryRemove(uri, out _);
        _publishDiagnostics(uri, new List<Diagnostic>());  // Clear diagnostics
    }

    // Debounce: wait 200ms after last change before analyzing
    private async void AnalyzeDebounced(string uri)
    {
        if (!_documents.TryGetValue(uri, out var state)) return;

        var version = state.IncrementPendingVersion();
        await Task.Delay(200);

        if (state.PendingVersion != version) return;  // Superseded by newer edit

        Analyze(state);
    }

    private void Analyze(DocumentState state)
    {
        var diagnosticBag = new DiagnosticBag();

        // Lex
        var lexer = new Lexer(state.Text, state.Uri, diagnosticBag);
        state.Tokens = lexer.Tokenize();

        // Parse (even if lex errors — error recovery produces partial AST)
        var parser = new Parser(state.Tokens, diagnosticBag);
        state.Ast = parser.Parse();

        // Semantic analysis (even if parse errors — analyze what we can)
        if (state.Ast != null)
        {
            var analyzer = new SemanticAnalyzer(state.Ast, _catalog, diagnosticBag);
            state.TypedAst = analyzer.Analyze();
            state.ScopeMap = analyzer.GetScopeMap();   // name → declaration mapping
            state.TypeMap = analyzer.GetTypeMap();     // expression → resolved type
        }

        // Convert to LSP diagnostics and publish
        var lspDiagnostics = diagnosticBag.All
            .Select(d => ToLspDiagnostic(d))
            .ToList();
        _publishDiagnostics(state.Uri, lspDiagnostics);
    }
}

public class DocumentState
{
    public string Uri { get; }
    public string Text { get; private set; }
    public List<Token> Tokens { get; set; }
    public NoriProgram Ast { get; set; }
    public TypedProgram TypedAst { get; set; }
    public Dictionary<string, Declaration> ScopeMap { get; set; }
    public Dictionary<AstNode, string> TypeMap { get; set; }
}
```

## Part B: LSP Handlers

### CompletionHandler.cs

Provide context-aware completions:

**After `.` on a typed expression:**
1. Find the expression to the left of the dot.
2. Determine its type from `TypeMap`.
3. Look up all properties and methods of that type in the catalog.
4. Return as CompletionItems with appropriate kinds and type details.

```csharp
// transform.| → position (Vector3), rotation (Quaternion), Rotate(...), Translate(...), ...
// player.|    → displayName (string), isLocal (bool), isMaster (bool), ...
```

**After `on ` (event name context):**
Return all standard VRChat events: Start, Update, LateUpdate, FixedUpdate, Interact, Pickup, Drop, PlayerJoined, PlayerLeft, TriggerEnter, TriggerExit, CollisionEnter, VariableChange, PreSerialization, PostSerialization.

Each with a detail string showing parameters (e.g., `PlayerJoined(player: Player)`).

**After `sync ` (sync mode context):**
Return: `none`, `linear`, `smooth` with descriptions of each.

**After `send ... to ` (network target context):**
Return: `All`, `Owner`.

**After `:` in a declaration (type context):**
Return all known types: primitives (`int`, `float`, `bool`, `string`, etc.), Unity types, VRChat types, array types. Sourced from the catalog's type list.

**Statement context (beginning of a line inside a block):**
Return keywords (`let`, `if`, `while`, `for`, `return`, `break`, `continue`, `send`) plus all in-scope variable and function names.

**Top-level context (beginning of a line outside blocks):**
Return: `let`, `pub`, `sync`, `fn`, `on`, `event`.

### HoverHandler.cs

Return Markdown-formatted hover info based on what's under the cursor:

**Variable name:**
```markdown
`score: int` (sync none)

Declared at line 3.
```

**Function name:**
```markdown
`fn update_display() → void`

Declared at line 8.
```

**Method call on a typed expression:**
```markdown
`Transform.Rotate(axis: Vector3, angle: float) → void`

Rotates the transform around the given axis by the given angle in degrees.

**Extern:** `UnityEngineTransform.__Rotate__UnityEngineVector3_SystemSingle__SystemVoid`
```

**Type name (in a declaration):**
```markdown
`Vector3` → `UnityEngineVector3`

3D vector with x, y, z components.
```

**Event name (after `on`):**
```markdown
`on Interact`

Fires when a player interacts with this object (click or trigger pull).
No parameters.
```

**Keyword:**
Brief description of the keyword's purpose.

### DefinitionHandler.cs

**Variable reference** → jump to the `let`/`pub let`/`sync` declaration.
**Function call** → jump to the `fn` declaration.
**Custom event in `send`** → jump to the `event` declaration.
**Parameter name** → jump to the parameter in the function/event signature.

Implementation: when the cursor is on a NameExpr, look it up in the ScopeMap. The declaration node has a SourceSpan — return that location.

### SignatureHelpHandler.cs

When the cursor is inside parentheses of a function or method call:

1. Determine which function/method is being called.
2. Get all overloads from the catalog (or the single signature for user functions).
3. Determine which parameter the cursor is at (count commas before cursor position).
4. Return all signatures with the active parameter highlighted.

```
transform.Rotate(Vector3.up, |)
                              ^ cursor here

SignatureHelp:
  Signature 1: Rotate(axis: Vector3, **angle: float**) → void    ← active parameter bold
  Signature 2: Rotate(eulers: Vector3) → void
  Active signature: 1 (2 args rules out signature 2)
  Active parameter: 1
```

### DocumentSymbolHandler.cs

Return a hierarchical outline:
- Variable declarations → `SymbolKind.Variable` (or `Field`)
- Event handlers → `SymbolKind.Event`
- Custom events → `SymbolKind.Event`
- Functions → `SymbolKind.Function`

This populates the Outline panel in VS Code/Rider and breadcrumbs.

### SemanticTokensHandler.cs (Optional Enhancement)

Provides richer highlighting than TextMate alone:
- Distinguish variables from parameters from built-in names.
- Highlight type names in expressions (not just after `:`).
- Color synced variables differently from regular variables.
- Highlight unknown/undefined names with a distinct token type.

Define token types: `variable`, `parameter`, `property`, `function`, `event`, `type`, `keyword`, `number`, `string`, `comment`, `operator`, `builtinVariable`, `syncVariable`.

## Part C: VS Code Extension

### package.json

```json
{
  "name": "nori-lang",
  "displayName": "Nori Language",
  "description": "Language support for Nori (.nori) - a VRChat programming language",
  "version": "0.1.0",
  "publisher": "nori-lang",
  "engines": { "vscode": "^1.80.0" },
  "categories": ["Programming Languages"],
  "activationEvents": ["onLanguage:nori"],
  "main": "./out/extension.js",
  "contributes": {
    "languages": [{
      "id": "nori",
      "aliases": ["Nori", "nori"],
      "extensions": [".nori"],
      "configuration": "./language-configuration.json"
    }],
    "grammars": [{
      "language": "nori",
      "scopeName": "source.nori",
      "path": "./syntaxes/nori.tmLanguage.json"
    }],
    "snippets": [{
      "language": "nori",
      "path": "./snippets/nori.json"
    }],
    "configuration": {
      "title": "Nori",
      "properties": {
        "nori.lsp.path": {
          "type": "string",
          "default": "",
          "description": "Path to the Nori LSP server executable. If empty, uses bundled server."
        },
        "nori.catalog.path": {
          "type": "string",
          "default": "",
          "description": "Path to extern catalog JSON. If empty, uses builtin catalog."
        }
      }
    }
  }
}
```

### extension.ts

```typescript
import * as vscode from 'vscode';
import { LanguageClient, TransportKind, Executable } from 'vscode-languageclient/node';
import * as path from 'path';

let client: LanguageClient;

export function activate(context: vscode.ExtensionContext) {
    // Find LSP server: check settings, then bundled location
    const config = vscode.workspace.getConfiguration('nori');
    let serverPath = config.get<string>('lsp.path');

    if (!serverPath) {
        // Use bundled server (platform-specific binary published with extension)
        const platform = process.platform;  // win32, darwin, linux
        const ext = platform === 'win32' ? '.exe' : '';
        serverPath = context.asAbsolutePath(`server/nori-lsp${ext}`);
    }

    const catalogPath = config.get<string>('catalog.path') || '';

    const serverOptions: Executable = {
        command: serverPath,
        args: catalogPath ? ['--catalog', catalogPath] : [],
        transport: TransportKind.stdio
    };

    client = new LanguageClient(
        'nori',
        'Nori Language Server',
        serverOptions,
        {
            documentSelector: [{ scheme: 'file', language: 'nori' }],
            synchronize: {
                fileEvents: vscode.workspace.createFileSystemWatcher('**/*.nori')
            }
        }
    );

    client.start();
}

export function deactivate(): Thenable<void> | undefined {
    return client?.stop();
}
```

### TextMate Grammar (nori.tmLanguage.json)

Provide syntax highlighting that works even without the LSP server (TextMate is always active; semantic tokens from the LSP server enhance it when running):

- **Keywords**: `let`, `pub`, `sync`, `fn`, `on`, `event`, `return`, `send`, `to`, `if`, `else`, `while`, `for`, `in`, `break`, `continue` → `keyword.*.nori`
- **Sync modes**: `none`, `linear`, `smooth` → `keyword.other.sync-mode.nori`
- **Network targets**: `All`, `Owner` → `constant.language.target.nori`
- **Types after `:`**: `int`, `float`, `bool`, `string`, `Vector3`, `Transform`, etc. → `entity.name.type.nori`
- **Function names after `fn`**: → `entity.name.function.nori`
- **Event names after `on`**: → `entity.name.function.event.nori`
- **String literals with interpolation**: `"text {expr} text"` → `string.quoted.double.nori` with `meta.interpolation.nori` and `punctuation.section.interpolation.nori`
- **Numbers**: integers, floats, uint suffix → `constant.numeric.nori`
- **Boolean/null**: `true`, `false`, `null` → `constant.language.nori`
- **Comments**: `//` and `/* */` → `comment.line.nori`, `comment.block.nori`
- **Operators**: all arithmetic, comparison, logical, assignment → `keyword.operator.nori`
- **Built-in functions**: `log`, `warn`, `error` → `support.function.nori`
- **Built-in variables**: `localPlayer`, `gameObject`, `transform` → `support.variable.nori`

### Snippets (nori.json)

Include at minimum:
- `on` → event handler (with choice of event name)
- `fn` → function declaration
- `event` → custom event
- `pub` → public variable
- `sync` → synced variable
- `if` → if block
- `for` → for range loop
- `while` → while loop
- `send` → network send
- `isowner` → ownership check pattern (`if Networking.IsOwner(localPlayer, gameObject) { }`)

## Part D: JetBrains Rider Support

Rider has built-in LSP client support (since 2023.2). Create `editors/rider/README.md` with setup instructions:

1. Install the Nori LSP server (provide download link or build instructions).
2. In Rider: `Settings > Languages & Frameworks > Language Servers > Add`.
3. Set the server path and file pattern (`*.nori`).
4. (Optional) Add a TextMate bundle for syntax highlighting: Rider supports TextMate grammars via the "TextMate Bundles" plugin.

Also provide a `nori.tmbundle/` directory with the TextMate grammar packaged for Rider's TextMate plugin.

If Rider's LSP support has limitations (check current state), document them and provide workarounds.

## Part E: Visual Studio Support

Visual Studio supports LSP via the "Language Server Client" extensibility model. There are two approaches:

**Option 1: VSIX Extension (full integration)**
Create a Visual Studio extension that ships the LSP server and registers it for `.nori` files. This requires a VSIX project with:
- An `ILanguageClient` implementation.
- The LSP server binary bundled in the extension.
- A content type definition for `.nori` files.

**Option 2: Instructions for manual setup (minimal effort)**
If building a VSIX is too much for this phase, provide instructions for using Visual Studio's generic LSP support (if available in the target VS version).

Recommend Option 1 if Visual Studio is a significant portion of the target audience. VRChat creators using Unity often have Visual Studio installed as Unity's default IDE.

Create `editors/visual-studio/README.md` with whichever approach is chosen.

## Part F: LSP Server Distribution

The LSP server must be distributed as a standalone executable (not requiring the .NET SDK on the user's machine).

Build self-contained, single-file binaries for:
- Windows x64: `nori-lsp-win-x64.exe`
- macOS x64: `nori-lsp-osx-x64`
- macOS ARM: `nori-lsp-osx-arm64`
- Linux x64: `nori-lsp-linux-x64`

```xml
<!-- Nori.Lsp.csproj -->
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net8.0</TargetFramework>
  <PublishSingleFile>true</PublishSingleFile>
  <SelfContained>true</SelfContained>
</PropertyGroup>
```

Build with: `dotnet publish -r win-x64 -c Release`

Bundle the appropriate binary in each editor extension. For the VS Code extension, include all platform binaries and select the right one at activation time based on `process.platform` and `process.arch`.

## Testing

### LSP Server Integration Tests

Use a test harness that simulates an LSP client:

```csharp
[Test]
public async Task Diagnostics_PublishedOnOpen()
{
    var client = new TestLspClient(server);
    await client.OpenDocument("test.nori", "on Start { log(undefined_var) }");
    var diagnostics = await client.WaitForDiagnostics("test.nori");

    Assert.That(diagnostics, Has.Count.EqualTo(1));
    Assert.That(diagnostics[0].Code, Is.EqualTo("E0070"));
    Assert.That(diagnostics[0].Message, Contains.Substring("Undefined variable"));
}

[Test]
public async Task Completion_AfterDot_ReturnsMembers()
{
    var client = new TestLspClient(server);
    await client.OpenDocument("test.nori", "on Start { transform. }");
    var completions = await client.RequestCompletion("test.nori", line: 0, character: 20);

    Assert.That(completions.Items.Select(i => i.Label), Contains.Item("position"));
    Assert.That(completions.Items.Select(i => i.Label), Contains.Item("Rotate"));
}

[Test]
public async Task Hover_OnVariable_ShowsType()
{
    var client = new TestLspClient(server);
    await client.OpenDocument("test.nori", "let score: int = 0\non Start { log(score) }");
    var hover = await client.RequestHover("test.nori", line: 1, character: 15);

    Assert.That(hover.Contents.Value, Contains.Substring("int"));
}

[Test]
public async Task GoToDefinition_Variable_JumpsToDeclaration()
{
    var client = new TestLspClient(server);
    await client.OpenDocument("test.nori", "let score: int = 0\non Start { log(score) }");
    var definition = await client.RequestDefinition("test.nori", line: 1, character: 15);

    Assert.That(definition.Range.Start.Line, Is.EqualTo(0));  // Declaration on line 0
}
```

### VS Code Extension Tests

- Open a `.nori` file → syntax highlighting applies (TextMate grammar).
- Type `on ` → completion shows event names.
- Save a file with errors → red underlines appear within 500ms.
- Hover a variable → tooltip shows type.
- Ctrl+click a variable → jumps to declaration.
- Type `transform.` → completion shows Transform members.
- Type inside `Rotate(` → signature help shows parameters.
- Open Outline panel → shows all declarations.

## Definition of Done

- [ ] LSP server starts via stdio and connects to editors
- [ ] Diagnostics published in <500ms after last keystroke (debounced)
- [ ] Autocomplete after `.` shows type-correct members from the catalog
- [ ] Autocomplete for keywords, events, sync modes, types in appropriate contexts
- [ ] Hover shows type info for variables, functions, methods, events, types
- [ ] Go-to-definition works for variables, functions, custom events, parameters
- [ ] Signature help shows parameter info during function/method calls
- [ ] Document symbols populate the Outline panel
- [ ] VS Code extension: TextMate grammar, LSP client, snippets, published to Marketplace (or .vsix)
- [ ] Rider setup documented and tested
- [ ] Visual Studio setup documented (VSIX or manual instructions)
- [ ] LSP server distributed as self-contained single-file binaries for Windows, macOS, Linux
- [ ] Compiler frontend is shared between Unity package and LSP server (single source of truth)
- [ ] All LSP integration tests pass
