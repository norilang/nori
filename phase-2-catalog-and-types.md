# Phase 2: Extern Catalog and Full Type System

You are extending the **Nori** compiler, a C# Unity Editor package that compiles `.nori` files to Udon Assembly. Phase 1 built the core compiler with a hardcoded builtin catalog. Phase 2 adds a complete extern catalog scraped from the VRChat SDK and a comprehensive type checker.

Read `DESIGN.md` section 5 (Extern Catalog) and section 4.4 (Semantic Analysis) for architecture.

## Context

The Phase 1 compiler hardcodes ~50 common externs (Debug.Log, basic arithmetic, Transform basics, etc.) and makes best-effort guesses for everything else. This means:

- Property setters don't work (`transform.position = x` generates wrong assembly).
- Most method calls generate incorrect extern signatures.
- No overload resolution — if a method has multiple signatures, the compiler picks wrong.
- No type checking against actual available APIs — the compiler happily generates externs that don't exist in Udon.

Phase 2 fixes all of this. Because the compiler is C# running inside Unity, we have direct access to the VRChat SDK's type information at editor time.

## Part A: Catalog Scraper

### CatalogScraper.cs

A Unity Editor script that runs inside a project with the VRChat SDK installed. Accessible from `Tools > Nori > Generate Extern Catalog`.

The scraper uses the VRChat SDK's node registry to enumerate every available Udon extern:

```csharp
using VRC.Udon.Editor;

public static class CatalogScraper
{
    [MenuItem("Tools/Nori/Generate Extern Catalog")]
    public static void GenerateCatalog()
    {
        var registries = UdonEditorManager.Instance.GetNodeRegistries();

        var catalog = new CatalogData();

        foreach (var registry in registries)
        {
            foreach (var (key, definition) in registry.Value)
            {
                // key is the full extern signature string
                // definition contains: fullName, type, inputParameters, outputParameters
                ProcessNode(catalog, key, definition);
            }
        }

        // Write to JSON
        string json = JsonUtility.ToJson(catalog, prettyPrint: true);
        // JsonUtility doesn't handle complex nesting well —
        // use Newtonsoft.Json or a manual serializer
        File.WriteAllText(outputPath, json);

        Debug.Log($"[Nori] Generated catalog: {catalog.TotalExterns} externs, " +
                  $"{catalog.TotalTypes} types");
    }
}
```

**Processing each node definition:**

Each node in the Udon registry has:
- A full extern signature string (e.g., `UnityEngineTransform.__get_position__UnityEngineVector3`)
- Input parameters (name, type)
- Output parameters (name, type)
- The full .NET type name it belongs to

The scraper must:

1. **Parse the extern signature** to extract: the owning type, method/property name, whether it's a getter/setter/method/operator/constructor, parameter types, return type.

2. **Classify the extern:**
   - `__get_{name}__{ReturnType}` → property getter
   - `__set_{name}__{ParamType}__SystemVoid` → property setter
   - `__op_{Name}__{Types}` → operator
   - `__ctor__{Types}` → constructor
   - `__{Name}__{Types}` → method (instance if first implicit param matches owning type)

3. **Organize by namespace and type.** Split `UnityEngineTransform` back into namespace `UnityEngine` and type `Transform`. Map Udon type names to their original .NET names for documentation.

4. **Handle overloads.** Multiple externs can map to the same method name with different parameter types. Store all overloads as an array.

5. **Detect instance vs static.** Instance methods have an implicit `this` parameter that doesn't appear in the extern's declared input parameters but IS encoded in the signature. Static methods have no implicit receiver.

6. **Handle ref/out parameters.** Parameters with `Ref` suffix in the extern signature are `ref` or `out` params. Record this in the catalog so the codegen knows to pre-allocate the output variable.

### Catalog JSON Schema

```json
{
  "version": "1.0.0",
  "sdk_version": "3.7.0",
  "unity_version": "2022.3.22f1",
  "generated_at": "2025-02-14T00:00:00Z",
  "total_externs": 4821,
  "total_types": 347,
  "namespaces": {
    "UnityEngine": {
      "types": {
        "Transform": {
          "udon_type": "UnityEngineTransform",
          "dotnet_type": "UnityEngine.Transform",
          "base_type": "UnityEngineComponent",
          "is_abstract": false,
          "properties": {
            "position": {
              "type": "UnityEngineVector3",
              "has_get": true,
              "has_set": true,
              "get_extern": "UnityEngineTransform.__get_position__UnityEngineVector3",
              "set_extern": "UnityEngineTransform.__set_position__UnityEngineVector3__SystemVoid"
            },
            "rotation": { ... },
            "localPosition": { ... }
          },
          "methods": {
            "Rotate": [
              {
                "extern": "UnityEngineTransform.__Rotate__UnityEngineVector3_SystemSingle__SystemVoid",
                "params": [
                  { "name": "axis", "type": "UnityEngineVector3" },
                  { "name": "angle", "type": "SystemSingle" }
                ],
                "return_type": "SystemVoid",
                "is_instance": true
              },
              {
                "extern": "UnityEngineTransform.__Rotate__UnityEngineVector3__SystemVoid",
                "params": [
                  { "name": "eulers", "type": "UnityEngineVector3" }
                ],
                "return_type": "SystemVoid",
                "is_instance": true
              }
            ],
            "Translate": [ ... ],
            "LookAt": [ ... ]
          },
          "static_methods": {},
          "operators": [],
          "constructors": []
        }
      }
    },
    "System": {
      "types": {
        "Int32": {
          "udon_type": "SystemInt32",
          "dotnet_type": "System.Int32",
          "operators": [
            {
              "extern": "SystemInt32.__op_Addition__SystemInt32_SystemInt32__SystemInt32",
              "op": "+",
              "left_type": "SystemInt32",
              "right_type": "SystemInt32",
              "return_type": "SystemInt32"
            }
          ]
        }
      }
    },
    "VRC.SDKBase": {
      "types": {
        "VRCPlayerApi": {
          "udon_type": "VRCSDKBaseVRCPlayerApi",
          "properties": {
            "displayName": {
              "type": "SystemString",
              "has_get": true,
              "has_set": false,
              "get_extern": "VRCSDKBaseVRCPlayerApi.__get_displayName__SystemString"
            }
          },
          "methods": { ... }
        }
      }
    }
  }
}
```

### Catalog Validation

After generating, the scraper should validate:
- Every extern signature follows the naming convention.
- Every type referenced in parameter/return types exists in the catalog.
- No duplicate extern signatures.
- Print a summary: total externs, types by namespace, any warnings.

## Part B: ExternCatalog.cs (Loader + Query API)

Replace the Phase 1 hardcoded catalog with a proper catalog that loads from JSON and provides efficient lookups.

```csharp
public class ExternCatalog
{
    // Load from a JSON file (generated by the scraper)
    public static ExternCatalog LoadFromFile(string path);

    // Load the built-in fallback (compiled into the assembly)
    public static ExternCatalog LoadBuiltin();

    // ── Type Resolution ──────────────────────────────────────
    // Map Nori type name to catalog type. Handles aliases:
    // "int" → SystemInt32, "Player" → VRCSDKBaseVRCPlayerApi, etc.
    public CatalogType ResolveType(string noriTypeName);

    // Check if typeA is assignable to typeB (exact match, or subtype, or implicit conversion)
    public bool IsAssignable(string fromType, string toType);

    // ── Property Resolution ──────────────────────────────────
    // Look up a property on a type. Returns getter and/or setter info.
    public PropertyInfo ResolveProperty(string ownerUdonType, string propertyName);

    // ── Method Resolution ────────────────────────────────────
    // Look up instance methods by name on a type. Returns all overloads.
    public List<MethodOverload> GetMethodOverloads(string ownerUdonType, string methodName);

    // Resolve to a specific overload given argument types.
    // Returns null if no match, throws if ambiguous.
    public MethodOverload ResolveMethod(string ownerUdonType, string methodName, string[] argTypes);

    // Same for static methods.
    public MethodOverload ResolveStaticMethod(string ownerUdonType, string methodName, string[] argTypes);

    // ── Operator Resolution ──────────────────────────────────
    public OperatorInfo ResolveOperator(string op, string leftType, string rightType);
    public OperatorInfo ResolveUnaryOperator(string op, string operandType);

    // ── Discovery (for LSP and error suggestions) ────────────
    public List<string> GetAllPropertyNames(string ownerUdonType);
    public List<string> GetAllMethodNames(string ownerUdonType);
    public List<string> GetAllTypeNames();  // Nori-friendly names
}
```

### Overload Resolution Algorithm

When multiple overloads exist for a method call:

1. **Exact match**: Every argument type matches the corresponding parameter type exactly.
2. **Widening match**: Implicit numeric widening is allowed:
   - `int` → `float` → `double`
   - `int` → `long`
   - `float` → `double`
3. **Object match**: Any type matches `SystemObject` (last resort).
4. **Scoring**: If multiple overloads match after widening, prefer the one with the most exact matches. If still tied, report ambiguity.
5. **No match**: List available overloads in the error message.

```
error[E0130]: No matching overload for Transform.Rotate with arguments (int, int, int)
  --> door.nori:12:5
   |
12 |     transform.Rotate(0, 90, 0)
   |               ^^^^^^
   Available overloads:
     Rotate(eulers: Vector3) → void
     Rotate(axis: Vector3, angle: float) → void
     Rotate(xAngle: float, yAngle: float, zAngle: float) → void

   help: Did you mean to use float arguments?
       transform.Rotate(0.0, 90.0, 0.0)
```

### Implicit Type Coercion

The codegen must insert conversion externs when types don't exactly match but are implicitly convertible. This is common:

```nori
transform.Rotate(Vector3.up, 90)  // 90 is int, Rotate expects float
```

The semantic analyzer should:
1. Detect the mismatch: argument is `SystemInt32`, parameter expects `SystemSingle`.
2. Check if implicit conversion exists (int → float: yes).
3. Insert a conversion node in the typed AST.

The codegen then emits:
```
PUSH, __const_90          // SystemInt32
PUSH, __tmp_conv_0        // SystemSingle
EXTERN, "SystemConvert.__ToSingle__SystemObject__SystemSingle"
```

Build a table of implicit conversions and the externs that perform them.

## Part C: Semantic Analyzer Rewrite

Rewrite `SemanticAnalyzer.cs` to use the full catalog. The analyzer now produces a **TypedAST** where every node has complete type and extern information.

### TypedAST Annotations

Every expression node gets:
- `ResolvedType: string` — the Udon type of the expression result.

Method calls additionally get:
- `ResolvedExtern: string` — the exact extern signature to emit.
- `ResolvedParams: List<(string name, string type)>` — parameter info for the resolved overload.
- `ImplicitConversions: List<(int argIndex, string fromType, string toType, string conversionExtern)>`

Property accesses get:
- `ResolvedGetExtern: string` / `ResolvedSetExtern: string`
- `IsAssignment: bool` — set during assignment analysis so codegen knows to use the setter.

Operator expressions get:
- `ResolvedExtern: string` — the operator extern.

### Assignment to Member Expressions

This is a critical codegen fix. When the target of an assignment is a member expression:

```nori
transform.position = newPos
```

The codegen must NOT emit a property get + COPY. It must emit the property setter extern:
```
PUSH, __transform           // this
PUSH, newPos                // value
EXTERN, "UnityEngineTransform.__set_position__UnityEngineVector3__SystemVoid"
```

The semantic analyzer must detect this pattern: when the LHS of an `AssignStmt` is a `MemberExpr`, resolve the property's setter (not getter). If the property has no setter, error.

Compound assignment to properties (`transform.position += offset`) must: get the current value, perform the operation, then set the result.

### Enum Support

VRChat uses enums for: `Space` (Self/World), `KeyCode`, `ForceMode`, `SendMessageOptions`, `NetworkEventTarget`, etc.

In Udon, enums are their underlying integer type, but extern signatures use the enum type name. The catalog should record enum types with their values:

```json
"VRCSDKBaseNetworkEventTarget": {
  "kind": "enum",
  "underlying_type": "SystemInt32",
  "values": {
    "All": 1,
    "Owner": 2
  }
}
```

In Nori syntax: `Space.Self`, `KeyCode.E`, `ForceMode.Impulse`. The semantic analyzer resolves these to the integer constant, and the codegen emits the constant with the enum's Udon type name (since that's what the extern signature expects).

### Static Method and Property Access

Some types have static members: `Time.deltaTime`, `Vector3.zero`, `Networking.LocalPlayer`, `Mathf.Lerp(...)`.

The semantic analyzer must distinguish:
- `transform.position` → instance property get on `transform` variable.
- `Time.deltaTime` → static property get on `Time` type (no receiver object).
- `Vector3.Lerp(a, b, t)` → static method call on `Vector3` type.
- `Networking.IsOwner(player, obj)` → static method call on `Networking` type.

Static member access has no `this` parameter pushed. The extern signature is the same format but the call pattern differs.

Build a list of names that resolve to types (for static access) vs variables. If `Time` is not a declared variable, check if it's a type name in the catalog. If it is, treat `.deltaTime` as a static property access.

## Part D: Updated Codegen

Update `UdonEmitter.cs` to read resolved type information from the TypedAST:

- **No more `GuessType()` or `GuessPropertyType()`.** Every type and extern comes from the analyzer.
- **Property setters work.** Detect assignment-to-member and emit setter externs.
- **Implicit conversions emit conversion externs.**
- **Static method calls don't push a `this` parameter.**
- **Enum values emit as typed constants.**
- **Overloaded methods emit the correct overload's extern signature.**

## Part E: Testing

**CatalogTests.cs:**
- Scraper produces valid JSON for a real VRChat SDK project.
- Catalog loads from JSON without errors.
- `ResolveType("int")` returns `SystemInt32`.
- `ResolveType("Player")` returns `VRCSDKBaseVRCPlayerApi`.
- `ResolveProperty("UnityEngineTransform", "position")` returns correct getter and setter externs.
- `ResolveMethod("UnityEngineTransform", "Rotate", ["UnityEngineVector3", "SystemSingle"])` returns the correct overload.
- Overload resolution with no match reports available overloads.
- Overload resolution with widening (int→float) succeeds.

**SemanticTests.cs (extended):**
- `transform.position = Vector3.zero` resolves setter extern.
- `transform.Rotate(Vector3.up, 90)` triggers implicit int→float conversion.
- `Time.deltaTime` resolves as static property access.
- `Vector3.Lerp(a, b, t)` resolves as static method call.
- `transform.Fly()` reports "no method 'Fly' on Transform" with suggestions.
- `transform.position = 42` reports type mismatch (int vs Vector3).

**CodeGenTests.cs (extended):**
- Property setter generates correct extern call.
- Static property access generates correct extern call (no `this` push).
- Implicit conversion inserts conversion extern before method call.

## Definition of Done

- [ ] Catalog scraper generates complete JSON from VRChat SDK (run `Tools > Nori > Generate Extern Catalog`)
- [ ] Catalog loads at compiler startup and is used for all extern resolution
- [ ] Every method call resolves to a correct extern signature via the catalog
- [ ] Overload resolution handles exact match, numeric widening, and ambiguity detection
- [ ] Property setters generate correct assembly (`transform.position = x` works)
- [ ] Static member access works (`Time.deltaTime`, `Vector3.Lerp(...)`)
- [ ] Implicit type coercion inserts conversion externs where needed
- [ ] Enum values resolve to typed constants
- [ ] Undefined members suggest alternatives: "'Fly' is not available on Transform. Did you mean 'Translate'?"
- [ ] Type mismatches show both types clearly
- [ ] All Phase 1 tests still pass
- [ ] New tests cover catalog loading, overload resolution, property setters, static access, enums
