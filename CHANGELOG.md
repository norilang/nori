# Changelog

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
