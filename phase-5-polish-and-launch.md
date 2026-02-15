# Phase 5: Polish, Testing, and Community Launch

You are finalizing **Nori** for public release. The C# compiler (Unity package), extern catalog, documentation site, and LSP server are built (Phases 1–4). This phase focuses on testing depth, developer experience polish, and community launch.

Read `DESIGN.md` section 10 (Phase 6) for background scope.

## Deliverables

### 1. Comprehensive Test Suite

**Fuzzing (Nori.Tests.Fuzzer/):**

Build a fuzzer that generates random syntactically-plausible Nori programs and feeds them to the compiler. The compiler must NEVER crash, hang, or throw an unhandled exception — it must always either produce output or produce a diagnostic.

```csharp
public class NoriFuzzer
{
    private Random _rng;

    // Generate a random .nori program with 1-20 declarations
    public string GenerateProgram()
    {
        var sb = new StringBuilder();
        int declCount = _rng.Next(1, 20);
        for (int i = 0; i < declCount; i++)
        {
            sb.AppendLine(GenerateDeclaration());
        }
        return sb.ToString();
    }

    // Random declaration: variable, event, function, custom event
    private string GenerateDeclaration() { /* ... */ }

    // Random expression tree up to depth 5
    private string GenerateExpression(int depth = 0) { /* ... */ }

    // Random statement
    private string GenerateStatement(int depth = 0) { /* ... */ }
}

[Test]
public void Fuzz_10000Programs_NoCrashes()
{
    var fuzzer = new NoriFuzzer(seed: 42);
    for (int i = 0; i < 10000; i++)
    {
        string source = fuzzer.GenerateProgram();
        Assert.DoesNotThrow(() =>
        {
            var result = NoriCompiler.Compile(source, $"fuzz_{i}.nori");
            // Either succeeds or has diagnostics — both are fine
        }, $"Crash on fuzz program {i}:\n{source}");
    }
}
```

Run with multiple seeds. Target: zero crashes across 50,000 random programs.

**Snapshot tests (Nori.Tests.Snapshots/):**

For each example file and test fixture, capture the generated `.uasm` as a snapshot file. On each test run, compare output against the snapshot. Any change to codegen requires an explicit snapshot update.

```
Tests/
  Snapshots/
    hello.nori.uasm.expected
    scoreboard.nori.uasm.expected
    door.nori.uasm.expected
    ...
```

```csharp
[TestCase("hello.nori")]
[TestCase("scoreboard.nori")]
[TestCase("door.nori")]
public void Snapshot_AssemblyMatchesExpected(string filename)
{
    string source = File.ReadAllText(Path.Combine(ExamplesDir, filename));
    var result = NoriCompiler.Compile(source, filename);
    Assert.IsTrue(result.Success);

    string expectedPath = Path.Combine(SnapshotsDir, filename + ".uasm.expected");
    if (!File.Exists(expectedPath))
    {
        // First run — create the snapshot
        File.WriteAllText(expectedPath, result.Uasm);
        Assert.Inconclusive("Snapshot created. Re-run to verify.");
    }

    string expected = File.ReadAllText(expectedPath);
    Assert.AreEqual(expected, result.Uasm,
        $"Assembly output changed for {filename}. " +
        "If intentional, delete the .expected file and re-run.");
}
```

**Error message tests (Nori.Tests.ErrorMessages/):**

For every error code, write a `.nori` file that triggers exactly that error. Verify the error code, message substring, and line number.

```csharp
[TestCase("E0001", "unterminated_string.nori", 1)]
[TestCase("E0002", "unterminated_comment.nori", 1)]
[TestCase("E0011", "missing_let_after_pub.nori", 1)]
[TestCase("E0012", "invalid_sync_mode.nori", 1)]
[TestCase("E0040", "type_mismatch.nori", 3)]
[TestCase("E0042", "generic_type.nori", 1)]
[TestCase("E0070", "undefined_variable.nori", 3)]
[TestCase("E0100", "recursion.nori", 1)]
[TestCase("E0130", "method_not_found.nori", 3)]
public void ErrorCode_TriggeredCorrectly(string code, string fixture, int expectedLine)
{
    string source = File.ReadAllText(Path.Combine(ErrorFixturesDir, fixture));
    var result = NoriCompiler.Compile(source, fixture);

    var matching = result.Diagnostics.All
        .Where(d => d.Code == code)
        .ToList();

    Assert.IsNotEmpty(matching, $"Expected error {code} but got: " +
        string.Join(", ", result.Diagnostics.All.Select(d => d.Code)));
    Assert.AreEqual(expectedLine, matching[0].Span.Start.Line);
}
```

**Structural validation tests (Nori.Tests.Validation/):**

For every program that compiles successfully, validate the generated assembly is structurally sound:

```csharp
public static class UasmValidator
{
    public static List<string> Validate(string uasm)
    {
        var errors = new List<string>();

        // Has .data_start and .data_end
        // Has .code_start and .code_end
        // Every PUSH references a variable declared in .data_start
        // Every EXTERN has valid signature format: Type.__Method__Params__Return
        // Every JUMP target is a declared label or 0xFFFFFFFC
        // Every .export in code section references an existing label
        // No duplicate labels
        // No duplicate variable names in data section
        // Every .sync references a declared variable

        return errors;
    }
}
```

### 2. Source Maps

Generate a source map alongside the `.uasm` output so Udon runtime errors can be traced back to Nori source lines.

Output file: `<name>.nori.map` (JSON):
```json
{
  "version": 1,
  "source": "scoreboard.nori",
  "mappings": [
    { "label": "_start", "line": 8, "col": 1, "construct": "on Start" },
    { "label": "AddPoint", "line": 14, "col": 1, "construct": "event AddPoint" },
    { "instruction_offset": 42, "line": 16, "col": 5, "construct": "score = score + 1" }
  ]
}
```

In the Unity integration, when Udon reports a runtime error with an instruction address, check if a `.map` file exists and translate the address to a source location. Log both the Udon error and the Nori source location:

```
[Nori] Runtime error in scoreboard.nori at line 16: score = score + 1
       Udon instruction 0x2A in event AddPoint
```

### 3. Community Examples

Create 10 real-world, production-quality examples in `Samples~/`:

1. **hello.nori** — Minimal Start + Interact
2. **toggle-door.nori** — State toggle with smooth animation in Update
3. **scoreboard.nori** — Networked score with ownership and sync
4. **teleporter.nori** — Move player to a target transform on interact
5. **pickup-gun.nori** — Raycast from a pickup object
6. **mirror-toggle.nori** — Toggle a VRC Mirror component on/off
7. **player-counter.nori** — Track and display player count via Join/Leave events
8. **timed-door.nori** — Opens for N seconds then auto-closes (Update timer pattern)
9. **ownership-request.nori** — Clean ownership transfer + sync pattern
10. **audio-player.nori** — Play/pause/skip with AudioSource control

Each example includes:
- Header comment block explaining what it does and what Unity setup is needed
- Inline comments on non-obvious lines
- All `pub` variables documented with expected types of GameObjects/components to assign

### 4. Performance Benchmarks

Create `Nori.Tests.Benchmarks/` using BenchmarkDotNet:

```csharp
[MemoryDiagnoser]
public class CompilerBenchmarks
{
    private string _small;    // ~50 lines
    private string _medium;   // ~500 lines
    private string _large;    // ~2000 lines
    private ExternCatalog _catalog;

    [GlobalSetup]
    public void Setup()
    {
        _catalog = ExternCatalog.LoadBuiltin();
        _small = GenerateSyntheticProgram(50);
        _medium = GenerateSyntheticProgram(500);
        _large = GenerateSyntheticProgram(2000);
    }

    [Benchmark] public void Compile_50Lines() => NoriCompiler.Compile(_small, "bench.nori", _catalog);
    [Benchmark] public void Compile_500Lines() => NoriCompiler.Compile(_medium, "bench.nori", _catalog);
    [Benchmark] public void Compile_2000Lines() => NoriCompiler.Compile(_large, "bench.nori", _catalog);

    [Benchmark] public void LexOnly_2000Lines() => new Lexer(_large, "bench.nori", new DiagnosticBag()).Tokenize();
    [Benchmark] public void ParseOnly_2000Lines() { /* lex then parse */ }
}
```

Targets:
- 50 lines: <10ms
- 500 lines: <50ms
- 2000 lines: <200ms
- Memory: <20MB for a 2000-line file
- Catalog load: <100ms

Store results in `benchmarks/results.md` and update with each release.

### 5. Contributing Guide

Create `CONTRIBUTING.md`:

- **Setup**: Clone repo, open `nori.sln`, build. Run tests: `dotnet test`.
- **Project structure**: What each project/directory contains.
- **Adding a language feature**: Step-by-step (add token → update parser → add AST node → update semantic analyzer → update codegen → add tests → update docs).
- **Adding an error code**: Add to ErrorDatabase, write a test fixture, add to docs error database, regenerate error index.
- **Code style**: Follow existing conventions. No global usings. XML doc comments on public APIs.
- **Testing**: Every change needs tests. Snapshots must be updated if codegen changes.
- **PR process**: Fork, branch, test, PR. CI must pass.

### 6. Distribution and Installation

**Unity Package (VCC):**
- Create a VCC-compatible listing.json for the VRChat Creator Companion.
- Creators add the Nori repository URL to VCC → Nori appears as an installable package.
- Document: how to add the repo, how to install, how to verify.

**Unity Package (UPM git):**
- For creators not using VCC: `Window > Package Manager > Add package from git URL`.
- URL format: `https://github.com/YOUR_USERNAME/nori.git?path=Nori.Unity`

**LSP Server:**
- GitHub Releases with self-contained binaries for Windows/macOS/Linux.
- VS Code extension on the VS Code Marketplace.
- Rider/VS setup instructions link to the release binaries.

**NuGet (Nori.Compiler):**
- Publish the shared compiler library to NuGet for anyone building custom tooling.

### 7. CI/CD Pipeline

GitHub Actions workflows:

**ci.yml** (on push/PR):
```yaml
- Restore and build Nori.Compiler, Nori.Lsp, Nori.Unity
- Run all tests (unit, integration, error messages, structural validation)
- Run fuzzer with 10,000 programs
- Build LSP binaries for all platforms
- Build VS Code extension
```

**release.yml** (on tag):
```yaml
- All CI steps
- Run benchmarks and update results
- Publish LSP binaries to GitHub Releases
- Publish VS Code extension to Marketplace
- Publish Nori.Compiler to NuGet
- Build and deploy documentation site
- Create GitHub Release with changelog
```

### 8. Launch Checklist

Create `LAUNCH.md`:

**Pre-launch:**
- [ ] All tests pass (unit, snapshot, error, fuzzing, validation)
- [ ] Benchmarks meet targets
- [ ] Documentation site deployed and all links work
- [ ] VS Code extension published
- [ ] LSP binaries on GitHub Releases
- [ ] Unity package installable via VCC and UPM git URL
- [ ] README has quick start, screenshots, and links
- [ ] LICENSE (MIT)
- [ ] CONTRIBUTING.md
- [ ] GitHub Issues templates (Bug Report, Feature Request)
- [ ] GitHub Discussions enabled

**Launch posts:**
- [ ] VRChat Discord (official, #community-tools or equivalent)
- [ ] VRC Prefabs Discord
- [ ] UdonSharp Discord / community
- [ ] r/VRChat
- [ ] r/VRchatCreators
- [ ] Twitter/X thread with demo GIF
- [ ] YouTube: 3-minute demo video showing Nori vs UdonSharp side by side

**Demo video outline:**
1. Show UdonSharp error message (cryptic assembly-level error) — 15 seconds
2. Show the same mistake in Nori (clear error with fix suggestion) — 15 seconds
3. Write a simple interactive object in Nori from scratch — 60 seconds
4. Show it working in VRChat — 20 seconds
5. Show autocomplete and hover in VS Code — 30 seconds
6. Show the docs site API reference — 15 seconds
7. Call to action: link to docs, GitHub, VCC install — 15 seconds

### 9. Known Limitations Document

Create `docs/KNOWN_LIMITATIONS.md` documenting what doesn't work yet and what's planned:

- No ternary operator (`? :`) — use if/else
- No switch/match statement — use if/else chains
- No multi-file programs — each `.nori` file is one UdonBehaviour
- No import/module system
- No debugger integration (source maps help but no breakpoints)
- No hot reload (recompile requires re-entering Play mode)
- Limited array operations (no sort, filter, map — use manual loops)
- Web playground not available (compiler is C#, not browser-compatible)

For each: explain why, what the workaround is, and whether it's planned for a future version.

## Definition of Done

- [ ] Fuzzer: 50,000 random programs, zero crashes
- [ ] Snapshot tests for all example files
- [ ] Error tests for every implemented error code
- [ ] Structural validation passes for all compilable programs
- [ ] Source maps generated and used for runtime error translation
- [ ] 10 production-quality example files with documentation
- [ ] Benchmarks meet targets, results documented
- [ ] CONTRIBUTING.md with full development guide
- [ ] VCC listing created and tested
- [ ] UPM git install tested
- [ ] CI/CD pipeline running (tests, builds, releases)
- [ ] VS Code extension on Marketplace
- [ ] LSP binaries on GitHub Releases
- [ ] Documentation site deployed
- [ ] LAUNCH.md checklist completed
- [ ] Demo video recorded
- [ ] Launch posts drafted
