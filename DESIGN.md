# Nori — Design Document

**A programming language for VRChat worlds.**

Nori compiles to Udon Assembly and runs on VRChat's Udon Virtual Machine. It exists because the current tooling — UdonSharp and the Udon Node Graph — produces cryptic errors, has sparse documentation, and hides critical platform constraints behind layers of abstraction. Nori makes those constraints explicit, provides clear error messages, and ships with complete, auto-generated API documentation.

This document is the authoritative reference for the language design, compiler architecture, and development roadmap.

---

## Table of Contents

1. [Problem Statement](#1-problem-statement)
2. [Target Platform: The Udon VM](#2-target-platform-the-udon-vm)
3. [Language Design](#3-language-design)
4. [Compiler Architecture](#4-compiler-architecture)
5. [The Extern Catalog](#5-the-extern-catalog)
6. [Error Message Philosophy](#6-error-message-philosophy)
7. [Unity Editor Integration](#7-unity-editor-integration)
8. [Documentation System](#8-documentation-system)
9. [LSP and Developer Experience](#9-lsp-and-developer-experience)
10. [Development Roadmap](#10-development-roadmap)

---

## 1. Problem Statement

VRChat world creators currently use one of two systems to write world logic:

**The Udon Node Graph** is a visual programming interface. It works for simple interactions but becomes unmanageable at scale. Debugging is difficult because errors appear as node-graph-level failures with no clear connection to intent.

**UdonSharp (U#)** is a C#-to-Udon compiler maintained by VRChat. It lets creators write C#-like code, but it is not C#. It is a subset with undocumented boundaries. Generics don't work. `List<T>` doesn't exist. Field initializers are compile-time only. When you hit a wall, the error message comes from the Udon Assembly layer — a stack trace through a VM you didn't know you were targeting.

The problems:

- **Errors are assembly-level.** When UdonSharp fails, the error often references heap indices, extern signatures, or Udon Assembly instructions. The creator has to reverse-engineer what went wrong in their C# from a failure in a language they've never seen.
- **Documentation is incomplete.** There is no single reference for what externs are available, what types are supported, or what operations are legal. Creators discover API surface by clicking through the Udon Node Graph's GUI tree or reading community wikis.
- **The C# illusion creates false expectations.** Creators write `List<T>`, `try/catch`, `async/await`, or generics — all features that look like they should work in C# — and get mysterious failures. The language doesn't tell them what's off-limits until they've already built on top of it.
- **VRChat has announced Soba as Udon's successor.** Timeline is unclear. Udon will be the platform for the foreseeable future, and the problems above affect every creator shipping worlds today.

Nori addresses all four problems by being honest about what Udon is. It is a purpose-built language where every constraint is a visible language feature, every error is an explanation, and every available API is documented.

---

## 2. Target Platform: The Udon VM

Nori's design is dictated by the Udon VM's architecture. Every language decision traces back to a VM constraint. This section documents the target so that design decisions later in the document are grounded.

### 2.1 VM Overview

Udon is a stack-and-heap virtual machine that runs inside Unity's .NET runtime. It does not use .NET reflection. It has 9 opcodes. All useful computation is dispatched through a single opcode (`EXTERN`) that calls whitelisted .NET methods.

The VM exists for security: it prevents VRChat world code from accessing the file system, making unrestricted network calls, or running arbitrary .NET code. It also ensures cross-platform compatibility between PC and Quest (Android).

### 2.2 Memory Model

There are no local variables. All state lives on the **Udon Heap** — a flat array of typed values. Each variable is a heap index. The compiler must allocate heap slots for every variable, every intermediate expression result, every constant, and every temporary.

There is also an **integer stack** used for passing heap addresses to opcodes. This stack holds addresses, not values. The typical pattern is: PUSH the heap index of an operand, PUSH the heap index of the result slot, then call EXTERN. The extern reads inputs and writes outputs through those heap indices.

### 2.3 Opcode Set

| Opcode | Code | Description |
|--------|------|-------------|
| NOP | 0 | No operation |
| PUSH | 1 | Push a heap address (uint32) onto the integer stack |
| POP | 2 | Pop and discard the top of the integer stack |
| JUMP_IF_FALSE | 4 | Pop a heap address; if the boolean at that address is false, jump to target |
| JUMP | 5 | Unconditional jump. `0xFFFFFFFC` is the halt/return sentinel. |
| EXTERN | 6 | Call a whitelisted .NET method. Arguments and return value are passed via heap addresses previously PUSHed onto the stack. |
| ANNOTATION | 7 | Long no-op (used for debugging metadata) |
| JUMP_INDIRECT | 8 | Jump to the address stored at a heap index |
| COPY | 9 | Pop two heap addresses; copy value from first to second |

### 2.4 Extern System

All useful operations — arithmetic, string manipulation, Unity API calls, VRChat SDK calls — go through `EXTERN`. Each extern is identified by a string signature:

```
TypeName.__MethodName__ParamType1_ParamType2__ReturnType
```

Examples:
```
SystemInt32.__op_Addition__SystemInt32_SystemInt32__SystemInt32
UnityEngineDebug.__Log__SystemObject__SystemVoid
UnityEngineTransform.__get_position__UnityEngineVector3
VRCSDKBaseVRCPlayerApi.__get_displayName__SystemString
```

Rules:
- Instance methods have an implicit `this` parameter (pushed first).
- `Ref` suffix on a parameter type indicates `out` or `ref`.
- Type names are .NET full names with dots and `+` signs removed: `System.Int32` becomes `SystemInt32`, `UnityEngine.Vector3` becomes `UnityEngineVector3`.
- Array types get an `Array` suffix: `System.Int32[]` becomes `SystemInt32Array`.

There is no official comprehensive list of available externs. The set is determined at compile time by VRChat's SDK whitelist. This is one of the core problems Nori solves with the extern catalog (Section 5).

### 2.5 Assembly Format

```
.data_start
    .export variableName              // visible to Unity Inspector
    .sync variableName, none          // networked, interpolation mode
    variableName: %SystemString, "default value"
.data_end

.code_start
    .export _start                    // marks an event entry point

    _start:
        PUSH, 0                       // push heap index 0
        EXTERN, "UnityEngineDebug.__Log__SystemObject__SystemVoid"
        JUMP, 0xFFFFFFFC              // halt
.code_end
```

### 2.6 Events

VRChat dispatches events to UdonBehaviours by jumping to exported labels. Standard events use underscore-prefixed names: `_start`, `_update`, `_interact`, `_onPlayerJoined`. Custom events are plain names. Events are the only entry points into Udon code.

### 2.7 Networking

Variables can be marked with `.sync` to replicate across the network. Sync modes: `none` (manual sync, no interpolation), `linear` (linear interpolation), `smooth` (smooth interpolation). Only the **owner** of a GameObject can write synced variables. Ownership must be explicitly transferred with `Networking.SetOwner`.

Network events are sent via `SendCustomNetworkEvent` with targets `All` or `Owner`.

### 2.8 Constraints Summary

These are hard VM constraints. They are not Nori design choices — they are physical limits of the target:

| Constraint | Reason |
|---|---|
| No local variables | Heap-only memory model |
| No call stack | No native function call/return mechanism |
| No recursion | Requires a call stack (which doesn't exist) |
| No generics | .NET generics not exposed through extern system |
| No user-defined types | No struct/class creation in Udon |
| No closures or lambdas | No local scopes to close over |
| No try/catch | Exception handling not exposed |
| No async/await | Not available in the VM |
| No file I/O | VRChat security sandbox |
| No arbitrary HTTP | VRChat security sandbox |
| Limited API surface | Only whitelisted externs are available |

---

## 3. Language Design

### 3.1 Design Principles

1. **No surprises.** If Udon can't do it, Nori won't let you write it. The language grammar itself makes impossible things unwritable.
2. **Errors are documentation.** Every error tells you what's wrong, where, why (which Udon constraint), and how to fix it, with a link to extended documentation.
3. **Explicitness over magic.** Networking, sync, and ownership are visible in the source code. There is no hidden behavior.
4. **Familiar but honest.** Nori borrows syntax from Rust, TypeScript, and Go where it makes sense. It does not pretend to be C#.

### 3.2 File Structure

A `.nori` file represents a single UdonBehaviour. It contains variable declarations, event handlers, custom events, and functions. There is no `class` keyword — the file *is* the class.

```nori
// toggle_door.nori

pub let speed: float = 2.0
sync none is_open: bool = false

on Interact {
    if Networking.IsOwner(localPlayer, gameObject) {
        is_open = !is_open
        RequestSerialization()
    }
}

on Update {
    if is_open {
        transform.Rotate(Vector3.up, speed * Time.deltaTime)
    }
}
```

### 3.3 Variables

All variables are heap-allocated fields on the UdonBehaviour. There are no stack-local variables at the language level. The compiler allocates temporaries internally, but the programmer sees only fields.

```nori
// Private field
let health: int = 100

// Public field (visible in Unity Inspector)
pub let speed: float = 5.0

// Synced field (networked, with interpolation mode)
sync none score: int = 0
sync linear position: Vector3 = Vector3.zero
sync smooth rotation: Quaternion = Quaternion.identity
```

`let` inside a function or event body declares a module-level field. This is intentional: Udon has no local variables, so pretending otherwise would be dishonest. The compiler may display a note explaining this on first use.

### 3.4 Type System

Nori's types map directly to Udon types. There are no Nori-specific types.

**Primitives:** `bool`, `int`, `uint`, `float`, `double`, `string`, `char`

**Arrays:** `int[]`, `float[]`, `string[]`, `GameObject[]`, etc. Arrays are the only collection type available.

**Unity types:** `Vector2`, `Vector3`, `Vector4`, `Quaternion`, `Color`, `Color32`, `Transform`, `GameObject`, `Rigidbody`, `Collider`, `MeshRenderer`, `AudioSource`, `Animator`, and all other types exposed through the extern whitelist.

**VRChat types:** `Player` (alias for `VRCPlayerApi`), and other VRC SDK types.

**Explicitly unsupported:** `List<T>`, `Dictionary<K,V>`, any generic type, any user-defined struct or class. Attempting to use these produces a compiler error with an explanation and migration path.

### 3.5 Events

Events are first-class. They are the entry points into Udon code and Nori treats them as a primary language construct.

```nori
// Lifecycle events
on Start { }
on Update { }
on FixedUpdate { }
on LateUpdate { }
on Enable { }
on Disable { }

// Interaction events
on Interact { }
on Pickup { }
on Drop { }
on PickupUseDown { }
on PickupUseUp { }

// Player events (with typed parameters)
on PlayerJoined(player: Player) { }
on PlayerLeft(player: Player) { }

// Collision/trigger events
on TriggerEnter(other: Collider) { }
on TriggerExit(other: Collider) { }
on CollisionEnter(collision: Collision) { }

// Serialization events
on VariableChange { }
on PreSerialization { }
on PostSerialization(result: SerializationResult) { }
```

### 3.6 Custom Events

Custom events are named blocks that can be called locally or over the network.

```nori
event ScorePoint {
    score = score + 1
    update_display()
}

// Local call
send ScorePoint

// Network call (all clients)
send ScorePoint to All

// Network call (owner only)
send ScorePoint to Owner
```

### 3.7 Functions

Functions compile to jump-based subroutines. They are not true functions with a call stack — the compiler implements them using `JUMP_INDIRECT` with a return address stored in a heap variable.

```nori
fn greet(name: string) {
    log("Hello, {name}!")
}

fn add(a: int, b: int) -> int {
    return a + b
}
```

**Restrictions:**
- No recursion. The compiler performs static call-graph analysis and rejects recursive call chains with a clear error.
- No closures. Functions can access their parameters and module-level fields only.
- No function references or first-class functions. Functions are called by name.

### 3.8 Control Flow

```nori
// Conditional
if condition {
    // ...
} else if other_condition {
    // ...
} else {
    // ...
}

// While loop
while condition {
    // ...
}

// Range-based for
for i in 0..10 {
    // ...
}

// Array iteration
for item in items {
    // ...
}

// Break and continue
while true {
    if done { break }
    continue
}
```

No `switch`/`match` in v1. It compiles to if/else chains anyway and the syntax sugar can be added later without breaking changes.

### 3.9 Expressions

**Arithmetic:** `+`, `-`, `*`, `/`, `%`
**Comparison:** `==`, `!=`, `<`, `>`, `<=`, `>=`
**Logical:** `&&`, `||`, `!`
**Assignment:** `=`, `+=`, `-=`, `*=`, `/=`
**Member access:** `obj.field`, `obj.Method(args)`
**Indexing:** `arr[i]`
**String interpolation:** `"Player {name} has {health} HP"`
**Array literals:** `[1, 2, 3]`

### 3.10 Built-in Shortcuts

These resolve to standard Udon operations but are available without qualification:

```nori
localPlayer     // Networking.LocalPlayer
gameObject      // this GameObject
transform       // gameObject.transform

log("msg")      // Debug.Log
warn("msg")     // Debug.LogWarning
error("msg")    // Debug.LogError
```

### 3.11 Networking Patterns

```nori
// Check ownership
if Networking.IsOwner(localPlayer, gameObject) {
    // Only the owner can modify synced variables
    score = score + 1
    RequestSerialization()
}

// Transfer ownership
Networking.SetOwner(localPlayer, gameObject)
```

### 3.12 What Nori Does Not Support

This table appears in the language reference and is linked from every relevant error message.

| Feature | Why | Alternative |
|---|---|---|
| `List<T>`, `Dictionary<K,V>` | Udon doesn't support .NET generics | Typed arrays (`int[]`, etc.) |
| Classes, structs, interfaces | Udon has no user-defined types | Multiple variables, parallel arrays |
| Recursion | No call stack in Udon VM | Iterative loops |
| Closures, lambdas | No local scopes in Udon VM | Module-level fields, explicit state |
| `async`/`await` | Not exposed in Udon | Update-loop state machines |
| `try`/`catch` | Exception handling not exposed | Null checks, input validation |
| File I/O | VRChat security sandbox | VRChat Persistence API |
| Arbitrary HTTP | VRChat security sandbox | VRChat approved APIs only |
| Multiple inheritance | Not applicable (no classes) | Composition via references |
| Operator overloading | No user-defined types | Named functions |

---

## 4. Compiler Architecture

### 4.1 Pipeline Overview

```
Source (.nori)
    │
    ▼
┌─────────┐
│  Lexer   │  Tokenizes source. Tracks line/column for every token.
└────┬─────┘
     │  Token stream
     ▼
┌─────────┐
│ Parser   │  Recursive descent. Produces AST. Error recovery at
└────┬─────┘  declaration and statement boundaries.
     │  AST
     ▼
┌───────────────┐
│   Semantic     │  Type checking, scope analysis, extern resolution
│   Analysis     │  against the catalog, constraint validation
└────┬──────────┘  (recursion detection, unsupported feature checks).
     │  Typed AST
     ▼
┌──────────┐
│   IR      │  Flattened representation. All expressions reduced to
│ Lowering  │  three-address-code-like operations. Heap allocation
└────┬──────┘  for all variables and temporaries computed here.
     │  IR
     ▼
┌────────────┐
│  Udon Asm  │  Emits .data_start/.data_end/.code_start/.code_end
│  Emitter   │  with correct heap indices, extern signatures,
└────┬───────┘  and jump targets. Output is textual Udon Assembly.
     │  .uasm
     ▼
┌────────────┐
│   Unity    │  (Future) Converts .uasm to Unity .asset files for
│  Packager  │  direct import without the Udon Graph compiler.
└────────────┘
```

### 4.2 Lexer

Hand-written lexer (no generator). Every token carries a `SourceSpan` (file, start line/col, end line/col). This is the foundation for every error message in the system.

Key decisions:
- Nested block comments (`/* /* */ */` is valid).
- String interpolation is handled in the parser, not the lexer. The lexer treats `"..."` as a single string token; the parser splits on `{`/`}` boundaries.
- Keywords are reserved words. `let`, `fn`, `on`, `sync`, etc. cannot be used as identifiers. Contextual keywords (`none`, `linear`, `smooth`, `All`, `Owner`) are recognized by the parser in the appropriate context.

### 4.3 Parser

Hand-written recursive descent parser. This is a deliberate choice over parser generators (ANTLR, PEG) because:

1. **Error recovery is the product.** Generic parser generators produce generic errors. Hand-written parsers can produce "you wrote `pub speed`, did you mean `pub let speed`?" errors.
2. **Context-sensitive recovery.** After an error in a statement, the parser skips to the next statement boundary. After an error in a declaration, it skips to the next `on`/`fn`/`event`/`let`/`pub`/`sync`.
3. **Precedence climbing for expressions.** Operator precedence is encoded directly in the recursive descent structure (or/and/equality/comparison/range/addition/multiplication/unary/postfix/primary).

### 4.4 Semantic Analysis

This is the phase where most user-facing errors are caught. It runs on the AST after parsing.

**Responsibilities:**
- **Type checking.** Verify that operations are valid for their operand types. Verify assignment compatibility. Flag type mismatches with clear error messages.
- **Scope resolution.** Resolve every `NameExpr` to a variable declaration. Flag undefined variables. Flag shadowing with a warning.
- **Extern resolution.** For every method call and property access, look up the extern signature in the catalog. If the extern doesn't exist, produce an error that says "this method is not available in Udon" — not "extern not found."
- **Constraint validation.** Detect recursion via call graph analysis. Detect use of unsupported types (generics, user-defined types). Detect writes to synced variables by non-owners (where statically determinable). Detect dead code after `return`/`break`.
- **Sync validation.** Verify that `RequestSerialization()` is called after modifying synced variables (warning if not). Verify that synced variable types are serializable.

### 4.5 IR Lowering

The AST is lowered to an intermediate representation where:
- Every expression is decomposed into a sequence of operations on named temporaries.
- Every variable (user-declared and compiler-generated) has a unique name and a resolved Udon type.
- Control flow is represented as labeled blocks with explicit jumps.
- Function calls are lowered to: store arguments in parameter slots, store return address, JUMP to function label, continue at return label.

This is where heap allocation happens. The IR assigns every variable a unique name that will become a heap entry in the assembly output. The actual heap indices are assigned during emission.

### 4.6 Udon Assembly Emitter

The emitter walks the IR and produces textual Udon Assembly. It is responsible for:
- Emitting the `.data_start` section with all heap variables, their types, initial values, export flags, and sync modes.
- Emitting the `.code_start` section with all code, event entry points, function bodies, and labels.
- Generating correct extern signatures by consulting the catalog.
- Assigning jump targets (labels are resolved to instruction addresses in a final pass).

### 4.7 Implementation Language

The compiler is written in **C#** and runs natively inside the Unity Editor. This gives us:
- **Zero external dependencies.** Creators install one Unity package. No Node.js, no PATH configuration, no child process management.
- **In-process compilation.** The ScriptedImporter calls the compiler directly as a method call. No IPC, no JSON serialization of diagnostics, no temp files.
- **Direct SDK access.** The catalog scraper and compiler run in the same .NET runtime as the VRChat SDK, so type information can be validated against the actual loaded SDK assemblies.
- **Same language as the ecosystem.** UdonSharp, the VRChat SDK, Unity scripting, and every creator who might contribute to Nori already use C#.

The compiler core lives in a shared `netstandard2.1` library (`Nori.Compiler`) that is referenced by both the Unity Editor package and the standalone LSP server. This ensures a single source of truth for all language logic.

The LSP server is a separate `net8.0` console application that communicates over stdio using `Microsoft.VisualStudio.LanguageServer.Protocol` and `StreamJsonRpc`. It is distributed as a self-contained single-file binary (no .NET SDK required on the user's machine).

---

## 5. The Extern Catalog

The extern catalog is the single most critical component after the parser. It is a structured database of every Udon extern — every method, property, constructor, and operator that Nori code can call. It is the source of truth for the type checker, the code generator, the LSP autocomplete, and the generated API documentation.

### 5.1 Why It Exists

There is no official machine-readable list of available Udon externs. The information exists in:
- The Udon Node Graph's internal node registry (accessible at editor time via C# reflection on the SDK).
- UdonSharp's "class exposure tree" (a human-readable reference, not a data file).
- Community wikis (incomplete and often outdated).

The catalog scraper extracts this information programmatically and outputs it as structured JSON.

### 5.2 Catalog Schema

```json
{
  "version": "3.7.0",
  "sdk_version": "2024.3.1",
  "generated_at": "2025-02-13T00:00:00Z",
  "namespaces": {
    "UnityEngine": {
      "types": {
        "Transform": {
          "udon_type": "UnityEngineTransform",
          "base_type": "UnityEngineComponent",
          "properties": {
            "position": {
              "type": "UnityEngineVector3",
              "get": {
                "extern": "UnityEngineTransform.__get_position__UnityEngineVector3",
                "params": [],
                "return": "UnityEngineVector3",
                "instance": true
              },
              "set": {
                "extern": "UnityEngineTransform.__set_position__UnityEngineVector3__SystemVoid",
                "params": [{ "name": "value", "type": "UnityEngineVector3" }],
                "return": "SystemVoid",
                "instance": true
              }
            }
          },
          "methods": {
            "Rotate": [
              {
                "extern": "UnityEngineTransform.__Rotate__UnityEngineVector3_SystemSingle__SystemVoid",
                "params": [
                  { "name": "axis", "type": "UnityEngineVector3" },
                  { "name": "angle", "type": "SystemSingle" }
                ],
                "return": "SystemVoid",
                "instance": true
              },
              {
                "extern": "UnityEngineTransform.__Rotate__UnityEngineVector3__SystemVoid",
                "params": [
                  { "name": "eulers", "type": "UnityEngineVector3" }
                ],
                "return": "SystemVoid",
                "instance": true
              }
            ]
          },
          "static_methods": {},
          "constructors": [],
          "operators": []
        }
      }
    }
  }
}
```

### 5.3 Catalog Scraper

The scraper is a Unity Editor script (C#) that runs inside a VRChat project. It:

1. Uses `UdonEditorManager.Instance.GetNodeRegistries()` to enumerate all registered node definitions.
2. For each node, extracts: full extern signature, parameter names and types, return type, whether it's static or instance, which .NET type it belongs to.
3. Organizes by namespace and type.
4. Outputs `catalog.json`.

The scraper must be re-run when the VRChat SDK updates (externs may be added or removed). The output is checked into the Nori repository and versioned alongside the compiler.

### 5.4 Catalog Consumption

- **Semantic analysis** uses the catalog to resolve method calls and property accesses. When a creator writes `transform.Rotate(Vector3.up, 90.0)`, the analyzer looks up `Transform.Rotate` in the catalog, finds the overload matching `(Vector3, float)`, and records the extern signature.
- **Code generation** reads the resolved extern signature from the typed AST node and emits the correct EXTERN instruction.
- **LSP autocomplete** uses the catalog to suggest available methods and properties after `.` on a typed expression.
- **Documentation generator** reads the catalog and produces API reference pages organized by namespace and type.

---

## 6. Error Message Philosophy

Nori's error messages are its primary differentiator. Every error follows a strict format inspired by Rust and Elm.

### 6.1 Error Anatomy

Every error includes all of these:

```
error[E0042]: Generic types are not supported
  --> scripts/inventory.nori:15:5
   |
15 |     let items: List<Item> = []
   |                ^^^^^^^^^^
   |
   Udon does not support generic collection types like List<T>.
   The VM only supports fixed-type arrays.

   help: Use a typed array instead:
       let items: Item[] = []

   See: https://nori-lang.dev/errors/E0042
```

1. **What went wrong** — plain English, no jargon.
2. **Where** — file, line, column, with source code snippet and underline.
3. **Why** — which Udon constraint is being violated.
4. **How to fix** — a concrete, copy-pasteable code suggestion.
5. **Documentation link** — a stable URL to the full error page with examples, background, and common scenarios.

### 6.2 Error Categories

| Code Range | Category | Examples |
|---|---|---|
| E0001–E0009 | Lexer errors | Unterminated string, unterminated comment, unexpected character |
| E0010–E0039 | Parse errors | Expected token, unexpected token, malformed declaration |
| E0040–E0069 | Type errors | Type mismatch, incompatible assignment, unsupported type |
| E0070–E0099 | Scope errors | Undefined variable, undefined function, ambiguous reference |
| E0100–E0129 | Constraint errors | Recursion detected, generic type used, unsupported feature |
| E0130–E0159 | Extern errors | Method not available, wrong argument count, wrong argument types |
| E0160–E0189 | Network errors | Sync type not serializable, missing RequestSerialization |
| W0001–W0099 | Warnings | Unused variable, shadowed variable, unreachable code |

### 6.3 Error Database

Every error code has a corresponding documentation page. The error database is a TOML or Markdown file in the repository:

```toml
[E0042]
title = "Generic types are not supported"
category = "constraint"
message = "Generic types are not supported"
explanation = """
Udon's type system is based on concrete .NET types exposed through
the extern system. .NET generics (List<T>, Dictionary<K,V>, etc.)
are not part of the extern whitelist.

This is a fundamental limitation of the Udon VM, not a Nori limitation.
"""
suggestion = "Use a typed array instead: `int[]`, `string[]`, `GameObject[]`"
examples = [
  { bad = 'let items: List<Item> = []', good = 'let items: Item[] = []' },
  { bad = 'let map: Dictionary<string, int> = {}', good = '// Use parallel arrays:\nlet keys: string[] = []\nlet values: int[] = []' },
]
```

---

## 7. Unity Editor Integration

Nori needs to work inside the Unity Editor for creators to use it in VRChat projects.

### 7.1 ScriptedImporter

A custom `ScriptedImporter` for `.nori` files. When Unity detects a new or modified `.nori` file, it:
1. Invokes the Nori compiler (either the TypeScript CLI via `node` or a C# port).
2. Captures the generated `.uasm`.
3. Feeds the `.uasm` to VRChat's Udon Assembly compiler to produce the final program asset.
4. Displays any errors in the Unity Console with clickable links that open the `.nori` file at the correct line.

### 7.2 Custom Inspector

When a `.nori` file is selected, the Inspector shows:
- The file's `pub` variables with their types and defaults.
- Compile status (success/error count).
- A "Recompile" button.
- Links to the generated `.uasm` for debugging.

When a GameObject with a Nori-compiled UdonBehaviour is selected, the Inspector shows the `pub` fields as editable controls (same as UdonSharp).

### 7.3 Project Structure

```
Assets/
  Nori/
    Editor/
      NoriImporter.cs        // ScriptedImporter
      NoriInspector.cs        // Custom Inspector
      CatalogScraper.cs       // Extern catalog generator
    Runtime/
      catalog.json            // Generated extern catalog
  Scripts/
    my_door.nori              // User's Nori scripts
    scoreboard.nori
```

---

## 8. Documentation System

### 8.1 Documentation Layers

The documentation has five layers, each serving a different audience and need:

**Layer 1: Getting Started Guide.** Zero-to-working-world tutorial. Assumes VRChat Creator Companion and basic Unity knowledge. Covers: installing Nori, creating a `.nori` file, making an interactive object, publishing a world. Target length: 30 minutes.

**Layer 2: Language Reference.** Every keyword, construct, and pattern with examples. Organized by concept (variables, events, functions, control flow, networking). Each section includes common mistakes and what errors they produce.

**Layer 3: API Reference.** Auto-generated from the extern catalog. Organized by namespace (UnityEngine, VRC SDK). Every type has a page listing its available properties and methods with parameter types, return types, and examples. Searchable.

**Layer 4: Udon Internals.** For advanced users. How the Udon VM works, how to read Udon Assembly output, how to debug at the assembly level, how the compiler maps Nori constructs to Udon operations.

**Layer 5: Error Index.** Every error code with its full explanation, cause, fix, and examples. Linked from every compiler error. Searchable.

### 8.2 Documentation Tooling

- **Docs site:** Built with Starlight (Astro), mdBook, or Docusaurus. Deployed as a static site.
- **API reference:** Generated by a script that reads `catalog.json` and produces one Markdown page per type.
- **Error index:** Generated from the error database (TOML/Markdown files).
- **Search:** Algolia DocSearch or Pagefind for client-side search.
- **Versioning:** Documentation is versioned alongside the compiler. Each release tags the docs.

---

## 9. LSP and Developer Experience

### 9.1 Language Server Protocol

An LSP server enables real-time feedback in any editor (VS Code, Neovim, etc.). Nori's LSP provides:

- **Diagnostics.** Errors and warnings appear inline as the creator types. No save-and-compile cycle.
- **Autocomplete.** After typing `.` on a typed expression, the LSP suggests available properties and methods from the extern catalog. After typing `on `, it suggests available event names.
- **Hover information.** Hovering a variable shows its type. Hovering a method call shows the extern signature and parameter names.
- **Go to definition.** Jumping from a function call to its declaration.
- **Signature help.** While typing arguments to a function or method, shows the expected parameter types.

### 9.2 VS Code Extension

A VS Code extension provides:
- Syntax highlighting via a TextMate grammar.
- LSP client that connects to the Nori language server.
- Snippet templates for common patterns (`on Interact { }`, `sync none`, `event`).
- A "Compile" command that runs the CLI compiler.
- Problem matcher integration so compiler errors appear in the Problems panel.

### 9.3 Syntax Highlighting

TextMate grammar for `.nori` files. Covers:
- Keywords (`let`, `pub`, `sync`, `fn`, `on`, `event`, `send`, `to`, `if`, `else`, `while`, `for`, `in`, `return`, `break`, `continue`).
- Types (`int`, `float`, `bool`, `string`, `Vector3`, `Transform`, etc.).
- String interpolation (`{expr}` inside strings).
- Comments (single-line and block).
- Event names after `on` keyword.
- Sync modes (`none`, `linear`, `smooth`).

---

## 10. Development Roadmap

### Phase 1: Compiler Core and Unity Integration

**Goal:** A working C# compiler that runs inside the Unity Editor, compiles .nori files to Udon Assembly on save, and integrates with the VRChat SDK to produce runnable UdonBehaviours.

**Deliverables:**
- Hand-written lexer with full source location tracking.
- Recursive descent parser with error recovery.
- AST definition covering all language constructs.
- Semantic analysis: scope resolution, basic type checking, recursion detection.
- IR lowering with heap allocation for all variables and temporaries.
- Udon Assembly emitter producing valid .uasm output.
- Hardcoded builtin catalog for common externs (~50 methods/properties).
- Error message system with source snippets, underlines, hints, and error codes.
- ScriptedImporter for .nori files (auto-compile on save).
- Custom Inspector showing declarations, compile status, and recompile button.
- Project Settings page for Nori configuration.
- UdonAssemblyProgramAsset integration via reflection (works with or without VRChat SDK).
- Three working examples: hello world, interactive object, networked scoreboard.
- Unit tests for lexer, parser, semantic analysis, and codegen.
- UPM package installable via git URL.

**Out of scope:** Full extern catalog, comprehensive type checking, LSP, documentation site.

### Phase 2: Extern Catalog and Full Type System

**Goal:** A complete, auto-generated database of available externs scraped from the VRChat SDK, and a type checker that uses it for correct codegen and detailed error messages.

**Deliverables:**
- Unity Editor script that scrapes all Udon node definitions and outputs catalog.json.
- Catalog schema definition and validation.
- Semantic analysis updated to resolve all method calls and property accesses against the catalog.
- Overload resolution (choosing the correct extern when a method has multiple signatures).
- Implicit type coercion (inserting conversion externs at call boundaries).
- Property setter codegen (transform.position = newPos generates correct setter extern).
- Static member access (Time.deltaTime, Vector3.Lerp(...)).
- Enum support (Space.Self, KeyCode.E).
- Error messages that reference the catalog.
- Updated examples using full API surface.

### Phase 3: Documentation Site

**Goal:** Complete, searchable documentation for the language, API, and every error code.

**Deliverables:**
- Documentation site (Starlight/Astro).
- Getting Started tutorial (install package via VCC, zero to working world, no external tooling).
- Language Reference (every construct with examples and common mistakes).
- Auto-generated API Reference from catalog.json (one page per type, searchable).
- Error Index (every error code with explanation, cause, fix, examples).
- Udon Internals guide for advanced users.
- Search functionality.
- CI/CD deployment pipeline.

### Phase 4: LSP Server and Editor Support

**Goal:** Real-time feedback in VS Code, JetBrains Rider, and Visual Studio.

**Deliverables:**
- Shared Nori.Compiler library (netstandard2.1) referenced by both Unity package and LSP server.
- C# LSP server (net8.0 console app) communicating over stdio.
- Diagnostics, autocomplete, hover, go-to-definition, signature help.
- VS Code extension with TextMate grammar, LSP client, snippets, published to Marketplace.
- Self-contained LSP binaries for Windows, macOS, Linux.
- JetBrains Rider setup documentation and TextMate bundle.
- Visual Studio setup documentation.

### Phase 5: Polish, Testing, and Community Launch

**Goal:** Production-ready quality and community adoption.

**Deliverables:**
- Fuzzer: 50,000 random programs with zero crashes.
- Snapshot tests, error message tests, structural validation.
- Source maps: map Udon Assembly runtime errors back to .nori source lines.
- Performance benchmarks with BenchmarkDotNet.
- 10 production-quality example files with documentation.
- Contributing guide and development documentation.
- VCC listing for VRChat Creator Companion distribution.
- CI/CD pipeline (tests, builds, releases, doc deployment).
- Community launch: announcement posts, demo video, GitHub Discussions.


---

## Appendix A: Nori-to-Udon Type Mapping

| Nori Type | Udon Type | .NET Type |
|---|---|---|
| `bool` | `SystemBoolean` | `System.Boolean` |
| `int` | `SystemInt32` | `System.Int32` |
| `uint` | `SystemUInt32` | `System.UInt32` |
| `float` | `SystemSingle` | `System.Single` |
| `double` | `SystemDouble` | `System.Double` |
| `string` | `SystemString` | `System.String` |
| `char` | `SystemChar` | `System.Char` |
| `Vector2` | `UnityEngineVector2` | `UnityEngine.Vector2` |
| `Vector3` | `UnityEngineVector3` | `UnityEngine.Vector3` |
| `Vector4` | `UnityEngineVector4` | `UnityEngine.Vector4` |
| `Quaternion` | `UnityEngineQuaternion` | `UnityEngine.Quaternion` |
| `Color` | `UnityEngineColor` | `UnityEngine.Color` |
| `Transform` | `UnityEngineTransform` | `UnityEngine.Transform` |
| `GameObject` | `UnityEngineGameObject` | `UnityEngine.GameObject` |
| `Player` | `VRCSDKBaseVRCPlayerApi` | `VRC.SDKBase.VRCPlayerApi` |
| `int[]` | `SystemInt32Array` | `System.Int32[]` |

## Appendix B: Event Name Mapping

| Nori Event | Udon Label | Parameters |
|---|---|---|
| `Start` | `_start` | — |
| `Enable` | `_onEnable` | — |
| `Disable` | `_onDisable` | — |
| `Update` | `_update` | — |
| `LateUpdate` | `_lateUpdate` | — |
| `FixedUpdate` | `_fixedUpdate` | — |
| `Interact` | `_interact` | — |
| `Pickup` | `_onPickup` | — |
| `Drop` | `_onDrop` | — |
| `PickupUseDown` | `_onPickupUseDown` | — |
| `PickupUseUp` | `_onPickupUseUp` | — |
| `PlayerJoined` | `_onPlayerJoined` | `player: Player` |
| `PlayerLeft` | `_onPlayerLeft` | `player: Player` |
| `TriggerEnter` | `_onTriggerEnter` | `other: Collider` |
| `TriggerExit` | `_onTriggerExit` | `other: Collider` |
| `CollisionEnter` | `_onCollisionEnter` | `collision: Collision` |
| `VariableChange` | `_onDeserialization` | — |
| `PreSerialization` | `_onPreSerialization` | — |
| `PostSerialization` | `_onPostSerialization` | `result: SerializationResult` |

## Appendix C: Operator Extern Patterns

Binary operators compile to externs following this pattern:
```
{Type}.__op_{Name}__{Type}_{Type}__{ReturnType}
```

| Operator | Extern Name | Return Type |
|---|---|---|
| `+` | `op_Addition` | Same as operands |
| `-` | `op_Subtraction` | Same as operands |
| `*` | `op_Multiplication` | Same as operands |
| `/` | `op_Division` | Same as operands |
| `%` | `op_Modulus` | Same as operands |
| `==` | `op_Equality` | `SystemBoolean` |
| `!=` | `op_Inequality` | `SystemBoolean` |
| `<` | `op_LessThan` | `SystemBoolean` |
| `>` | `op_GreaterThan` | `SystemBoolean` |
| `<=` | `op_LessThanOrEqual` | `SystemBoolean` |
| `>=` | `op_GreaterThanOrEqual` | `SystemBoolean` |

Unary operators:
```
{Type}.__op_UnaryNegation__{Type}__{Type}       // -x
SystemBoolean.__op_UnaryNegation__SystemBoolean__SystemBoolean  // !x
```
