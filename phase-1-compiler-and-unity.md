# Phase 1: Compiler Core and Unity Integration

You are building **Nori**, a programming language for VRChat worlds that compiles to Udon Assembly. The compiler is written in C# and runs natively inside the Unity Editor — no external tooling required.

Read the design document at `DESIGN.md` before starting. It contains the full language specification, Udon VM architecture, and compiler pipeline design.

## Architecture Overview

The entire compiler ships as a Unity Editor package. When a creator saves a `.nori` file, Unity's asset pipeline invokes the Nori compiler in-process, which produces Udon Assembly and feeds it directly to the VRChat SDK's `UdonAssemblyProgramAsset`. No Node.js, no child processes, no PATH configuration.

```
Assets/Scripts/door.nori
        │ (file saved)
        ▼
┌───────────────────────┐
│  NoriImporter.cs      │  ScriptedImporter triggers on .nori files
│    │                  │
│    ▼                  │
│  NoriCompiler.cs      │  In-process compilation:
│    ├─ Lexer           │    Source → Tokens → AST → Typed AST → IR → .uasm
│    ├─ Parser          │
│    ├─ SemanticAnalyzer │
│    ├─ IrLowering      │
│    └─ UdonEmitter     │
│    │                  │
│    ▼                  │
│  UdonAssemblyProgram  │  VRChat SDK compiles .uasm → runtime program
└───────────────────────┘
        │
        ▼
  UdonBehaviour ready to run
```

## Project Structure

This is a Unity package. Everything lives under an Editor assembly definition so none of it ships to player builds.

```
Packages/
  dev.nori.compiler/
    package.json                          // UPM manifest
    CHANGELOG.md
    LICENSE
    README.md

    Editor/
      dev.nori.compiler.editor.asmdef     // Assembly definition (Editor only)

      Compiler/
        Source/
          SourceSpan.cs                   // SourcePos, SourceSpan — location tracking
          Token.cs                        // TokenKind enum, Token struct
          Lexer.cs                        // Hand-written lexer
        Parsing/
          AstNodes.cs                     // All AST node types
          Parser.cs                       // Recursive descent parser
        Analysis/
          TypeSystem.cs                   // Nori-to-Udon type mapping, type compatibility
          Scope.cs                        // Scope chain, symbol resolution
          SemanticAnalyzer.cs             // Type checking, constraint validation
          TypedAst.cs                     // AST annotated with resolved types/externs
        IR/
          IrNodes.cs                      // Three-address-code IR types
          IrLowering.cs                   // AST → IR lowering, heap allocation
        CodeGen/
          UdonEmitter.cs                  // IR → Udon Assembly text
        Diagnostics/
          Diagnostic.cs                   // Severity, code, message, span, hint
          DiagnosticBag.cs                // Collects diagnostics during compilation
          DiagnosticPrinter.cs            // Formats diagnostics for Unity Console
          ErrorDatabase.cs                // Error code → explanation/suggestion lookup
        Catalog/
          ExternCatalog.cs                // Loads and queries the extern database
          CatalogTypes.cs                 // Schema types matching catalog.json
          BuiltinCatalog.cs               // Hardcoded fallback for common externs
        Pipeline/
          NoriCompiler.cs                 // Orchestrates the full pipeline
          CompileResult.cs                // Success/failure + diagnostics + .uasm output

      Integration/
        NoriImporter.cs                   // ScriptedImporter for .nori files
        NoriImporterEditor.cs             // Custom Inspector for .nori assets
        NoriSettingsProvider.cs           // Project Settings > Nori
        NoriSettings.cs                   // ScriptableSingleton for settings
        NoriMenuItems.cs                  // Tools > Nori menu entries

      Resources/
        builtin-catalog.json              // Fallback extern catalog (checked in)

    Tests/
      Editor/
        dev.nori.compiler.tests.asmdef
        LexerTests.cs
        ParserTests.cs
        SemanticTests.cs
        CodeGenTests.cs
        IntegrationTests.cs
```

## UPM Package Manifest

```json
{
  "name": "dev.nori.compiler",
  "displayName": "Nori Language",
  "version": "0.1.0",
  "description": "A clear, friendly programming language for VRChat worlds.",
  "unity": "2022.3",
  "documentationUrl": "https://nori-lang.dev",
  "keywords": ["vrchat", "udon", "language", "compiler"],
  "author": {
    "name": "Nori Contributors",
    "url": "https://github.com/YOUR_USERNAME/nori"
  },
  "dependencies": {}
}
```

No dependencies beyond Unity and the VRChat SDK. The VRChat SDK is assumed to be present in the project (creators install it via the Creator Companion). If the SDK isn't installed, the compiler can still run (producing .uasm text) but the ScriptedImporter can't create UdonAssemblyProgramAssets.

## Compiler Implementation

### SourceSpan.cs

Every token and AST node carries a `SourceSpan` — the file path, start position, and end position. This is the foundation of every error message.

```csharp
public readonly struct SourcePos
{
    public readonly int Line;    // 1-indexed
    public readonly int Column;  // 1-indexed
}

public readonly struct SourceSpan
{
    public readonly string File;
    public readonly SourcePos Start;
    public readonly SourcePos End;

    public SourceSpan Merge(SourceSpan other) { /* union of the two spans */ }
}
```

### Lexer

Hand-written, single-pass. Every token carries its `SourceSpan`. Key behaviors:

- Nested block comments (`/* /* */ */` is valid — track depth).
- String literals are single tokens. The parser handles interpolation splitting.
- Keywords: `let`, `pub`, `sync`, `fn`, `on`, `event`, `return`, `send`, `to`, `if`, `else`, `while`, `for`, `in`, `break`, `continue`.
- Contextual keywords recognized by kind: `none`, `linear`, `smooth`, `All`, `Owner`, `true`, `false`, `null`.
- Operators: `+`, `-`, `*`, `/`, `%`, `=`, `==`, `!=`, `<`, `<=`, `>`, `>=`, `&&`, `||`, `!`, `+=`, `-=`, `*=`, `/=`, `..`, `->`, `.`.
- Delimiters: `(`, `)`, `{`, `}`, `[`, `]`, `,`, `:`.
- On error (unterminated string, unexpected character): report diagnostic, produce an Error token, and continue lexing. Never throw.

### Parser

Hand-written recursive descent with error recovery.

**Declarations** (top-level):
- `let x: int = 0` → VarDecl
- `pub let speed: float = 5.0` → VarDecl (isPublic: true)
- `sync none score: int = 0` → VarDecl (sync: SyncMode.None)
- `on Start { ... }` → EventHandler
- `on PlayerJoined(player: Player) { ... }` → EventHandler (with params)
- `event DoThing { ... }` → CustomEvent
- `fn name(x: int) -> int { ... }` → FunctionDecl

**Statements** (inside blocks):
- `let x: int = 0` → LocalVarStmt
- `x = expr` / `x += expr` → AssignStmt
- `if cond { } else { }` → IfStmt
- `while cond { }` → WhileStmt
- `for i in 0..10 { }` → ForRangeStmt
- `for item in array { }` → ForEachStmt
- `return expr` → ReturnStmt
- `break` / `continue` → BreakStmt / ContinueStmt
- `send EventName to All` → SendStmt
- `expr` → ExpressionStmt (covers function calls as statements)

**Expressions** (precedence climbing):
1. Or (`||`)
2. And (`&&`)
3. Equality (`==`, `!=`)
4. Comparison (`<`, `<=`, `>`, `>=`)
5. Range (`..`)
6. Addition (`+`, `-`)
7. Multiplication (`*`, `/`, `%`)
8. Unary (`!`, `-`)
9. Postfix (`.member`, `(args)`, `[index]`)
10. Primary (literals, names, parenthesized, array literals)

**Error recovery:**
- After a parse error in a statement, skip tokens until: `let`, `if`, `while`, `for`, `return`, `break`, `continue`, `send`, `}`.
- After a parse error in a declaration, skip tokens until: `on`, `fn`, `event`, `let`, `pub`, `sync`.
- Catch the parse exception, record the diagnostic, skip, then continue parsing. Report ALL errors in one pass.

### Semantic Analyzer

Walks the AST and produces a TypedAST (or annotates nodes in-place). For Phase 1, use the hardcoded `BuiltinCatalog` for extern resolution. Phase 2 replaces this with the full catalog.

**Scope resolution:**
- Build a module-level scope from all VarDecl and FunctionDecl names.
- Add built-in names: `localPlayer`, `gameObject`, `transform`, `log`, `warn`, `error`, `RequestSerialization`, `Time`, `Networking`, `Vector3`, `Quaternion`, `Vector2`, `Color`, `Mathf`.
- For each NameExpr, resolve to a declaration. If unresolved, compute Levenshtein distance to all in-scope names and suggest the closest match: `error[E0070]: Undefined variable 'scroe'. Did you mean 'score'?`

**Basic type checking:**
- Assign a type to every expression (bottom-up).
- Literals: `42` → `int`, `3.14` → `float`, `"str"` → `string`, `true` → `bool`, `null` → `object`.
- Binary operators: look up the extern for the operator+types. If no match, error.
- Assignment: check that the RHS type is assignable to the LHS type.

**Constraint validation:**
- Recursion detection: build a call graph. If any cycle, report with the full call chain.
- Unsupported features: if the parser sees something that looks like a generic type (`identifier<identifier>`), catch it and produce the E0042 error.

### IR Lowering

The AST is lowered to a flat IR where:
- Every expression is decomposed into operations on named variables (`__tmp_0 = a + b`).
- Every variable (user and compiler-generated) has a unique name and resolved Udon type.
- Control flow is explicit: labeled blocks with JUMP/JUMP_IF_FALSE.
- Function calls are lowered to: set params → set return address → JUMP → return label.

This is where heap allocation happens. Every unique variable name becomes a heap entry.

### Udon Assembly Emitter

Walks the IR and emits textual `.uasm`:

```
.data_start
    .export myVar
    .sync myNetVar, none
    myVar: %SystemSingle, 5.0
    myNetVar: %SystemInt32, 0
    __tmp_0: %SystemBoolean, null
.data_end

.code_start
    .export _start

    _start:
        PUSH, __const_0
        EXTERN, "UnityEngineDebug.__Log__SystemObject__SystemVoid"
        JUMP, 0xFFFFFFFC
.code_end
```

### NoriCompiler.cs (Pipeline Orchestrator)

```csharp
public static class NoriCompiler
{
    public static CompileResult Compile(string source, string filePath, ExternCatalog catalog = null)
    {
        var diagnostics = new DiagnosticBag();
        catalog ??= BuiltinCatalog.Instance;

        // Phase 1: Lex
        var lexer = new Lexer(source, filePath, diagnostics);
        var tokens = lexer.Tokenize();
        if (diagnostics.HasErrors) return CompileResult.Failed(diagnostics);

        // Phase 2: Parse
        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();
        if (diagnostics.HasErrors) return CompileResult.Failed(diagnostics);

        // Phase 3: Analyze
        var analyzer = new SemanticAnalyzer(ast, catalog, diagnostics);
        var typedAst = analyzer.Analyze();
        if (diagnostics.HasErrors) return CompileResult.Failed(diagnostics);

        // Phase 4: Lower to IR
        var lowering = new IrLowering(typedAst, diagnostics);
        var ir = lowering.Lower();

        // Phase 5: Emit
        var emitter = new UdonEmitter(ir);
        var uasm = emitter.Emit();

        return CompileResult.Success(uasm, diagnostics);
    }
}
```

## Unity Integration

### NoriImporter.cs

```csharp
[ScriptedImporter(1, "nori")]
public class NoriImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        string source = File.ReadAllText(ctx.assetPath);

        // Load catalog (try full catalog from settings, fall back to builtin)
        var catalog = NoriSettings.Instance.LoadCatalog();

        var result = NoriCompiler.Compile(source, ctx.assetPath, catalog);

        // Report diagnostics to Unity Console
        foreach (var diag in result.Diagnostics.All)
        {
            // Format: "Assets/Scripts/door.nori(15,5): error E0042: message"
            // This format makes the error clickable in Unity Console
            string location = $"{ctx.assetPath}({diag.Span.Start.Line},{diag.Span.Start.Column})";
            string msg = $"{location}: {diag.Severity} {diag.Code}: {diag.Message}";
            if (diag.Hint != null) msg += $"\n  hint: {diag.Hint}";

            if (diag.Severity == Severity.Error)
                ctx.LogImportError(msg);
            else if (diag.Severity == Severity.Warning)
                ctx.LogImportWarning(msg);
        }

        if (!result.Success) return;

        // Create a TextAsset with the .uasm content for debugging
        var uasmAsset = new TextAsset(result.Uasm);
        ctx.AddObjectToAsset("uasm", uasmAsset);

        // If VRChat SDK is available, create the Udon program asset
        // This requires the VRChat SDK to be installed in the project
        // Use reflection or #if preprocessor to handle SDK absence gracefully
        TryCreateUdonProgram(ctx, result.Uasm);
    }

    private void TryCreateUdonProgram(AssetImportContext ctx, string uasm)
    {
        // Check if VRChat SDK types are available
        var programType = System.Type.GetType(
            "VRC.Udon.Editor.ProgramSources.UdonAssemblyProgramAsset, VRC.Udon.Editor");

        if (programType == null)
        {
            // SDK not installed — just output the .uasm text
            Debug.LogWarning("[Nori] VRChat SDK not found. " +
                "Generated .uasm but cannot create Udon program asset.");
            return;
        }

        // Create program asset via reflection to avoid hard dependency
        // This lets the package compile even without the VRChat SDK installed
        var programAsset = ScriptableObject.CreateInstance(programType);
        // Set the assembly source text
        var field = programType.GetField("udonAssembly",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null) field.SetValue(programAsset, uasm);

        // Compile the assembly
        var refreshMethod = programType.GetMethod("RefreshProgram");
        if (refreshMethod != null) refreshMethod.Invoke(programAsset, null);

        ctx.AddObjectToAsset("program", (UnityEngine.Object)programAsset);
        ctx.SetMainObject((UnityEngine.Object)programAsset);
    }
}
```

Use reflection to reference VRChat SDK types so the Nori package compiles even in projects without the SDK. This is important for development and testing — you don't want a hard dependency on VRC.Udon.Editor.

### NoriImporterEditor.cs (Custom Inspector)

When a `.nori` file is selected in the Project window, show:

- **Source preview**: First 20 lines, monospaced.
- **Declarations summary**: List of `pub let` variables (name, type, default), `sync` variables (name, type, mode), events, custom events, functions.
- **Compile status**: Green "Compiled successfully" or red "N errors".
- **Buttons**: "Recompile" (forces reimport), "Open in Editor" (opens the .nori file in the default external editor).
- **Debug foldout**: Show the generated .uasm text (collapsed by default).

### NoriSettings.cs

A `ScriptableSingleton<NoriSettings>` accessed via `Project Settings > Nori`:

- **Extern catalog path**: Path to `catalog.json`. Default: the bundled `builtin-catalog.json`. After running the catalog scraper (Phase 2), this points to the full generated catalog.
- **Auto-compile on save**: Toggle (default: on). When off, `.nori` files only compile when manually reimported.
- **Verbose diagnostics**: Toggle. When on, errors include the "why" (Udon constraint) explanation in the Console output.

### NoriMenuItems.cs

`Tools > Nori >` menu:
- **Compile All Nori Scripts**: Reimports every `.nori` file in the project.
- **Nori Settings**: Opens the Project Settings page.
- **About Nori**: Shows version, links to docs and repo.

## Error Messages

Every error follows this format when printed to the Unity Console:

```
Assets/Scripts/door.nori(15,5): error E0042: Generic types are not supported

    15 |     let items: List<Item> = []
       |                ^^^^^^^^^^

    Udon does not support generic collection types like List<T>.

    hint: Use a typed array instead:
        let items: Item[] = []
```

The `filename(line,col):` prefix is critical — Unity makes this clickable, jumping to the exact line in the external editor.

For Phase 1, implement error codes: E0001 (unterminated string), E0002 (unterminated comment), E0010 (unexpected declaration), E0011 (expected let after pub), E0012 (invalid sync mode), E0020 (invalid send target), E0030 (expected expression), E0031 (expected token), E0032 (expected identifier), E0040 (type mismatch), E0042 (generic type used), E0070 (undefined variable), E0071 (undefined function), E0100 (recursion detected).

## Hardcoded Extern Catalog (BuiltinCatalog.cs)

For Phase 1, hardcode the most common externs. This is the minimum set needed to compile the example scripts. Phase 2 replaces this with the full scraped catalog.

Must include:
- **Arithmetic operators** for `SystemInt32`, `SystemSingle`, `SystemDouble`: Addition, Subtraction, Multiplication, Division, Modulus
- **Comparison operators** for `SystemInt32`, `SystemSingle`: LessThan, GreaterThan, LessThanOrEqual, GreaterThanOrEqual, Equality, Inequality
- **Boolean operators**: UnaryNegation, ConditionalAnd, ConditionalOr
- **String**: Concat (String+String), Object.ToString
- **Debug**: Log, LogWarning, LogError
- **Array**: get_Length, Get (index), Set (index)
- **Transform**: get_position, set_position, get_rotation, set_rotation, Rotate
- **GameObject**: SetActive, get_activeSelf
- **Time**: get_deltaTime, get_time
- **Networking**: get_LocalPlayer, IsOwner, SetOwner
- **Player (VRCPlayerApi)**: get_displayName, get_isLocal, get_isMaster
- **UdonEventReceiver**: SendCustomEvent, SendCustomNetworkEvent, RequestSerialization
- **Vector3**: zero, one, up, forward, right (static properties), op_Addition, op_Subtraction, op_Multiply (scalar)
- **Quaternion**: identity (static property)
- **Mathf**: Abs, Min, Max, Clamp, Lerp

Structure this as a C# class that implements the same `ExternCatalog` interface the full catalog will use, so swapping is seamless.

## Example Files

Include these in a `Samples~` directory (Unity convention for optional samples):

**hello.nori:**
```nori
on Start {
    log("Hello from Nori!")
}

on Interact {
    log("You clicked me!")
}
```

**scoreboard.nori:**
```nori
pub let max_score: int = 10
sync none score: int = 0
let is_game_over: bool = false

on Start {
    log("Scoreboard ready!")
}

fn update_display() {
    log("Score: {score}")
}

event AddPoint {
    score = score + 1
    update_display()
    if score >= max_score {
        send GameOver to All
    }
}

event GameOver {
    is_game_over = true
    log("Game over! Final score: {score}")
}

on Interact {
    if is_game_over {
        log("Game is over!")
        return
    }
    send AddPoint to All
}
```

**door.nori:**
```nori
pub let speed: float = 90.0
let is_open: bool = false
let current_angle: float = 0.0
let target_angle: float = 0.0

on Interact {
    is_open = !is_open
    if is_open {
        target_angle = 90.0
    } else {
        target_angle = 0.0
    }
}

on Update {
    if current_angle != target_angle {
        let step: float = speed * Time.deltaTime
        if current_angle < target_angle {
            current_angle = current_angle + step
            if current_angle > target_angle {
                current_angle = target_angle
            }
        } else {
            current_angle = current_angle - step
            if current_angle < target_angle {
                current_angle = target_angle
            }
        }
    }
}
```

## Tests

Use Unity's Test Framework (`NUnit`). All tests go in `Tests/Editor/`.

**LexerTests.cs:**
- Tokenizes all operators, keywords, literals correctly
- String escape sequences work (`\n`, `\t`, `\\`, `\"`, `\{`, `\}`)
- Nested block comments work
- Unterminated string reports error with correct line/column
- Unterminated comment reports error
- Unexpected character reports error and continues

**ParserTests.cs:**
- Parses each declaration type into correct AST node
- Parses each statement type
- Operator precedence: `a + b * c` parses as `a + (b * c)`
- Error recovery: file with 3 errors reports all 3
- `pub speed` (missing `let`) reports helpful error

**SemanticTests.cs:**
- Undefined variable triggers E0070 with "did you mean" suggestion
- Type mismatch in assignment triggers E0040
- Recursive function call triggers E0100
- All built-in names resolve correctly

**CodeGenTests.cs:**
- `hello.nori` produces valid assembly with `_start` and `_interact` exports
- `on Start { log("hi") }` produces correct PUSH + EXTERN + JUMP sequence
- `sync none score: int = 0` produces `.sync score, none` in data section
- `pub let speed: float = 5.0` produces `.export speed` in data section
- `send DoThing to All` produces correct SendCustomNetworkEvent extern

**IntegrationTests.cs:**
- Each example file compiles without errors
- Generated assembly is structurally valid:
  - Has `.data_start` and `.data_end`
  - Has `.code_start` and `.code_end`
  - Every PUSH references a declared heap variable
  - Every EXTERN has correct signature format
  - Every JUMP target is a declared label or `0xFFFFFFFC`
  - Every `.export` in code section has a corresponding label

## Definition of Done

- [ ] Unity package installs via git URL or local path
- [ ] `.nori` files auto-compile when saved in Unity
- [ ] `hello.nori`, `scoreboard.nori`, `door.nori` all compile to valid Udon Assembly
- [ ] Compile errors appear in Unity Console with clickable `filename(line,col)` links
- [ ] Custom Inspector shows declarations, compile status, and recompile button
- [ ] Project Settings > Nori page exists with configuration options
- [ ] Tools > Nori menu entries work
- [ ] A file with 5 intentional errors reports all 5 with source snippets and hints
- [ ] Undefined variable suggests closest match
- [ ] Recursive function detected and rejected
- [ ] If VRChat SDK is present, UdonAssemblyProgramAsset is created and usable
- [ ] If VRChat SDK is absent, compilation still works (just no program asset)
- [ ] All unit tests pass
- [ ] Package works in Unity 2022.3 LTS
