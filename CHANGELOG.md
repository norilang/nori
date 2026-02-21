# Changelog

## [0.1.3] - 2026-02-20

### Fixed
- Input event parameter naming: VRC runtime uses fixed internal names (`boolValue`/`floatValue`), not user-written parameter names — `inputJumpBoolValue` instead of `inputJumpValue`
- `.export` directive placement in UdonEmitter: now emitted immediately before each label instead of grouped at top of code section
- Integration test sample paths updated for new `Samples/` folder structure

### Added
- `world_settings.nori` sample: configures player movement speeds and optional double/triple jump via Inspector toggles
- 8 VRChat input events in compiler EventNameMap: InputJump, InputUse, InputGrab, InputDrop, InputMoveHorizontal/Vertical, InputLookHorizontal/Vertical
- 8 player movement methods in BuiltinCatalog: SetWalkSpeed, SetRunSpeed, SetStrafeSpeed, SetJumpImpulse, SetGravityStrength, IsPlayerGrounded, GetVelocity, SetVelocity
- Input events in LSP completion with parameter info
- Input events in VS Code snippet choices
- Integration tests for WorldSettings compilation, exports, and InputJump parameter naming
- Basic sample scene with all Nori program sources

### Changed
- Samples moved from `Samples~/` (Unity-hidden) to `Samples/` so users can browse them in the Project window

## [0.1.2] - 2026-02-20

### Fixed
- Variable scoping bug in for-range/for-each loops where renamed heap vars were not resolved correctly in loop bodies

### Added
- Quaternion.Euler(float, float, float) support in BuiltinCatalog
- CLAUDE.md project configuration for AI-assisted development

### Improved
- door.nori sample uses transform.localRotation with Quaternion.Euler for proper rotation

## [0.1.1] - 2026-02-16

### Added
- LSP server with diagnostics, completion, hover, go-to-definition, signature help, and document symbols
- VS Code extension with TextMate grammar, snippets, and LSP client
- Editor setup guides for Rider and Visual Studio
- IR optimization passes (copy propagation + dead variable elimination)
- Inspector array editing for pub variables (string[], int[], float[], GameObject[], etc.)
- Variable override persistence on NoriBehaviour (survives recompiles)
- Missing-component warnings (Collider for Interact, Pickup+Rigidbody for pickup events, etc.)
- Companion .asset system replacing sub-asset model for VRC compatibility
- Asset lifecycle management (delete/move/rename companion with .nori file)
- Drag-and-drop .nori files onto GameObjects and Hierarchy
- NoriBehaviour base class and custom utility scripts
- Create > Nori Script menu item
- Doc comment tooltips for pub variables in inspector
- Inline compile error diagnostics foldout
- Editor setup wizard (Tools > Nori > Setup Editor)
- Extern catalog caching (single JSON read per batch compile)
- Catalog staleness tracking with console/settings/wizard warnings
- Automated release workflow (GitHub Actions)
- 7 new sample files (spinner, lobby, lights, follower, synced_toggle, pickup_toy, quiz_game)

### Fixed
- Implicit conversions now emitted on assignment and return (int→float heap mismatch)
- Event parameter names mangled to match Udon runtime format
- Enum constants emit with correct enum type instead of SystemInt32
- This-variable uses concrete VRCUdonBehaviour type instead of IUdonEventReceiver
- Networking extern type name corrected (VRCSDKBaseNetworking)
- Array literal lowering implemented (was returning empty temp)
- Empty blocks emit NOP to prevent assembler label-collision errors
- Object equality operators added for null comparisons

### Improved
- Editor setup wizard resizable instead of fixed size
- About dialog GitHub URL corrected
- Nori Script moved to top of Create menu
- Sample files updated with null guards, bug fixes, and ClientSim documentation

## [0.1.0] - 2026-02-14

### Added
- Initial compiler pipeline: Lexer, Parser, Semantic Analyzer, IR Lowering, Udon Assembly Emitter
- ScriptedImporter for `.nori` files with auto-compilation on save
- Custom Inspector showing declarations, compile status, and generated assembly
- Project Settings page for Nori configuration
- Hardcoded builtin extern catalog for common VRChat/Unity APIs
- Error messages with source snippets, underlines, and fix suggestions
- Three example files: hello.nori, scoreboard.nori, door.nori
- Unit tests for all compiler phases
