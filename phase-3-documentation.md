# Phase 3: Documentation Site

You are building the documentation site for **Nori**, a programming language for VRChat worlds. The C# compiler and extern catalog exist from Phases 1–2. The documentation site is a standalone project (not part of the Unity package) that deploys as a static site.

Read `DESIGN.md` section 8 for the documentation architecture.

## Tech Stack

Use **Starlight** (Astro's documentation framework). Install with `npm create astro@latest -- --template starlight`. This gives you built-in search (Pagefind), sidebar navigation, dark/light mode, and MDX support.

The documentation generators (API reference, error index) are **C# console apps** or **scripts** that read the extern catalog JSON and error database and output Markdown files. This keeps the generator logic close to the compiler (same language, same type understanding) and the Markdown output compatible with any static site framework.

Alternatively, the generators can be simple Node scripts since they're just reading JSON and writing Markdown — use whatever is faster to build.

## Site Structure

```
docs-site/
  astro.config.mjs
  package.json
  generators/
    GenerateApiDocs/          // C# console app or Node script
      Program.cs              // Reads catalog.json → writes API markdown files
    GenerateErrorDocs/        // C# console app or Node script
      Program.cs              // Reads errors.toml → writes error markdown files
    api-descriptions.json     // Hand-written descriptions for common types/members
  data/
    catalog.json              // Copy of the generated extern catalog
    errors.toml               // Error code database
  src/
    content/
      docs/
        getting-started/
          index.md
          your-first-world.md
          examples.md
        language/
          index.md
          variables.md
          types.md
          events.md
          functions.md
          control-flow.md
          expressions.md
          networking.md
          custom-events.md
          limitations.md
        api/
          index.md
          _generated/          // Output of API doc generator
        internals/
          index.md
          udon-vm.md
          assembly.md
          compilation.md
          debugging.md
        errors/
          index.md
          _generated/          // Output of error doc generator
```

## Content Specifications

### Getting Started

**`getting-started/index.md` — Installation**

1. Prerequisites: Unity 2022.3+, VRChat Creator Companion, VRChat SDK 3.x.
2. Install Nori via VCC (add repository URL) or via Unity Package Manager (git URL).
3. Verify: create `Assets/Scripts/hello.nori` with `on Start { log("Hello!") }`, check Unity Console for output.
4. (Optional) Install the VS Code extension for syntax highlighting.

No mention of Node.js, npm, or any external tooling. The entire install is "add the package."

**`getting-started/your-first-world.md` — Tutorial**

Walk through building three things in one world, each building on the last:

**Step 1: A clickable object.** Create a Cube, add an UdonBehaviour, assign the compiled Nori program. Write `on Interact { log("Clicked!") }`. Enter Play mode. Click the cube. See the message.

**Step 2: A toggle door.** Add state (`let is_open: bool = false`), toggle on Interact, animate in Update. Explain each line. Show what the Inspector looks like with `pub let speed: float`.

**Step 3: A networked scoreboard.** Add `sync none score: int = 0`, a custom event, `send AddPoint to All`. Explain ownership. Explain `RequestSerialization`. Show that the score syncs between clients.

Every step: complete `.nori` file, screenshot of what to set up in Unity, expected behavior.

**`getting-started/examples.md` — Example Walkthrough**

5 complete, annotated examples:
1. Hello World
2. Toggle Door
3. Networked Scoreboard
4. Teleporter (move player on interact)
5. Game Timer (countdown with state machine)

Each: full source, line-by-line explanation, "try modifying" suggestions, generated assembly (collapsed).

### Language Reference

Each page follows this template:
1. What this feature does (1–2 paragraphs)
2. Syntax with examples
3. Detailed behavior and rules
4. Common patterns
5. Common mistakes (show the error each mistake produces)
6. "See also" links

**`language/variables.md`:**
- `let` (private field), `pub let` (Inspector-visible), `sync` (networked)
- Type annotation is required (no inference)
- All variables are module-level heap allocations (explain why — Udon has no locals)
- `let` inside a block is still a module-level field (explain this explicitly with the error you'd see if it caused a conflict)
- Sync modes explained: `none` (manual, no interpolation), `linear` (good for positions), `smooth` (good for rotations)
- Common mistake: `var x = 5` → error suggesting `let x: int = 5`

**`language/networking.md`** — This is the highest-value page. VRChat networking confuses everyone.
- The ownership model: only the owner of a GameObject can write its synced variables
- Checking ownership: `Networking.IsOwner(localPlayer, gameObject)`
- Transferring ownership: `Networking.SetOwner(localPlayer, gameObject)`
- The sync cycle: modify variable → `RequestSerialization()` → VRChat sends to other clients → `on VariableChange` fires on receivers
- Network events: `send EventName to All` (runs on everyone) vs `send EventName to Owner` (runs on owner only)
- Common pattern: ownership-request-then-modify with code example
- Common mistakes: modifying sync vars without ownership (nothing happens, no error), forgetting `RequestSerialization` (change is local-only), assuming events execute in order

**`language/limitations.md`:**
The "What Nori Does Not Support" table from the design doc, expanded with full explanations and workarounds for each item. Link to this from every constraint error message.

### API Reference (Auto-Generated)

The generator reads `catalog.json` and `api-descriptions.json` and outputs one `.md` file per type.

**`api-descriptions.json`** contains hand-written descriptions for common types and members:
```json
{
  "UnityEngine.Transform": {
    "description": "Represents the position, rotation, and scale of a GameObject.",
    "properties": {
      "position": "The world-space position of the transform.",
      "rotation": "The world-space rotation as a Quaternion.",
      "localPosition": "Position relative to the parent transform.",
      "localScale": "Scale relative to the parent transform."
    },
    "methods": {
      "Rotate": "Rotates the transform by the given angle or Euler angles.",
      "Translate": "Moves the transform by the given offset.",
      "LookAt": "Rotates the transform so its forward vector points at the target."
    }
  },
  "UnityEngine.Vector3": { ... },
  "VRC.SDKBase.VRCPlayerApi": { ... }
}
```

Write descriptions for at least: Transform, GameObject, Vector3, Vector2, Quaternion, Color, Rigidbody, Collider, AudioSource, Animator, Mathf, VRCPlayerApi, Networking, Time, Debug.

**Generated page format:**

```markdown
# Transform

**Namespace:** `UnityEngine` | **Udon Type:** `UnityEngineTransform`

Represents the position, rotation, and scale of a GameObject.

## Properties

| Property | Type | Access | Description |
|----------|------|--------|-------------|
| `position` | `Vector3` | get, set | World-space position |
| `rotation` | `Quaternion` | get, set | World-space rotation |
| `localPosition` | `Vector3` | get, set | Position relative to parent |
...

### position

**Type:** `Vector3` | **Access:** get, set

The world-space position of the transform.

\```nori
// Read position
let pos: Vector3 = transform.position

// Set position
transform.position = Vector3.zero
\```

## Methods

### Rotate

**Overload 1:** `Rotate(axis: Vector3, angle: float) → void`

Rotates the transform around the given axis by the given angle in degrees.

\```nori
transform.Rotate(Vector3.up, 90.0)
\```

**Overload 2:** `Rotate(eulers: Vector3) → void`

Rotates by Euler angles.

\```nori
transform.Rotate(Vector3(0.0, 90.0, 0.0))
\```
```

### Error Index (Auto-Generated)

**`data/errors.toml`** — the error database:

```toml
[E0001]
title = "Unterminated string literal"
category = "lexer"
explanation = """
String literals must be closed with a matching double quote (") before
the end of the line. Nori does not support multi-line string literals.
"""
suggestion = 'Close the string with " before the line ends.'
bad_example = '''
on Start {
    log("hello
}
'''
good_example = '''
on Start {
    log("hello")
}
'''

[E0012]
title = "Invalid sync mode"
category = "parser"
explanation = """
The sync keyword must be followed by one of three interpolation modes:
none, linear, or smooth. These control how the variable's value is
interpolated on remote clients between network updates.
"""
suggestion = "Use one of: sync none, sync linear, sync smooth"
bad_example = 'sync fast score: int = 0'
good_example = 'sync none score: int = 0'

[E0042]
title = "Generic types are not supported"
category = "constraint"
explanation = """
Udon's type system only supports concrete types exposed through the
extern whitelist. .NET generics (List<T>, Dictionary<K,V>, etc.) are
not available.

This is a hard constraint of the Udon VM, not a Nori limitation.
"""
suggestion = "Use typed arrays: int[], string[], GameObject[]"
bad_example = 'let items: List<Item> = []'
good_example = 'let items: Item[] = []'

[E0070]
title = "Undefined variable"
category = "scope"
explanation = """
The variable name used in this expression has not been declared in the
current scope. Variables must be declared with 'let', 'pub let', or
'sync' before they can be used.
"""
suggestion = "Check the spelling, or declare the variable first."
bad_example = '''
on Start {
    log(scroe)
}
'''
good_example = '''
let score: int = 0

on Start {
    log(score)
}
'''

[E0100]
title = "Recursion detected"
category = "constraint"
explanation = """
Nori detected a recursive function call chain. The Udon VM does not
have a call stack, so functions cannot call themselves (directly or
through other functions).

The compiler implements function calls using JUMP_INDIRECT with a
single return-address variable per function. A recursive call would
overwrite the return address, making it impossible to return correctly.
"""
suggestion = "Rewrite the recursive logic using a while loop."
bad_example = '''
fn factorial(n: int) -> int {
    if n <= 1 { return 1 }
    return n * factorial(n - 1)
}
'''
good_example = '''
fn factorial(n: int) -> int {
    let result: int = 1
    let i: int = n
    while i > 1 {
        result = result * i
        i = i - 1
    }
    return result
}
'''
```

Write entries for all error codes implemented in Phase 1 and 2.

### Udon Internals

**`internals/udon-vm.md`**: VM architecture. The 9 opcodes. Heap memory model. Integer stack (holds addresses, not values). Extern dispatch. Security sandbox. Include a Mermaid diagram of the PUSH → EXTERN → result flow.

**`internals/assembly.md`**: Annotated walkthrough of a generated `.uasm` file. Explain `.data_start` (heap declarations), `.code_start` (instructions), `.export` (entry points and Inspector variables), `.sync` (networked variables). Show the assembly for `hello.nori` with every line commented.

**`internals/compilation.md`**: How Nori compiles each construct to assembly. Variables → heap entries. Events → exported labels ending in `JUMP, 0xFFFFFFFC`. Functions → `JUMP_INDIRECT` subroutines. If/else → `JUMP_IF_FALSE` chains. For loops → counter variable + comparison + conditional jump + increment + unconditional jump. String interpolation → `ToString` + `Concat` chain.

**`internals/debugging.md`**: How to read Unity Console errors from Udon. How to use `--verbose` in Nori settings to see generated assembly. How to map assembly labels back to source code. Tips for narrowing down runtime errors.

## Build Pipeline

```json
{
  "scripts": {
    "generate:api": "dotnet run --project generators/GenerateApiDocs -- data/catalog.json src/content/docs/api/_generated/",
    "generate:errors": "dotnet run --project generators/GenerateErrorDocs -- data/errors.toml src/content/docs/errors/_generated/",
    "generate": "npm run generate:api && npm run generate:errors",
    "dev": "npm run generate && astro dev",
    "build": "npm run generate && astro build",
    "preview": "astro preview"
  }
}
```

If you prefer Node scripts over C# console apps for the generators, that's fine — they're just JSON/TOML → Markdown transforms. Use whatever is faster to build.

Generated files go in `_generated/` directories and are `.gitignore`d.

## Deployment

Configure for GitHub Pages, Netlify, or Vercel. Include a GitHub Actions workflow that:
1. Runs the generators.
2. Builds the Astro site.
3. Deploys to the hosting provider.
4. Triggers on push to `main` and on release tags.

## Definition of Done

- [ ] `npm run build` produces a complete static site
- [ ] Getting Started: installation through working world tutorial, no external tooling mentioned
- [ ] Language Reference: every construct covered with examples and common mistakes
- [ ] API Reference: auto-generated from catalog, one page per type, with descriptions for top 15 types
- [ ] Error Index: auto-generated, entries for all implemented error codes
- [ ] Udon Internals: VM explanation, assembly walkthrough, compilation guide, debugging tips
- [ ] Search works across all pages
- [ ] All code examples are valid Nori syntax
- [ ] CI/CD pipeline deploys on push to main
- [ ] Site URL configured and linked from the Unity package README
