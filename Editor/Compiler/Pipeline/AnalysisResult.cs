using System.Collections.Generic;

namespace Nori.Compiler
{
    /// <summary>
    /// Result of running the compiler frontend (lex, parse, analyze) without code generation.
    /// Used by the LSP server for editor features.
    /// </summary>
    public class AnalysisResult
    {
        public List<Token> Tokens { get; }
        public ModuleDecl Ast { get; }
        public DiagnosticBag Diagnostics { get; }
        public Dictionary<AstNode, string> TypeMap { get; }
        public Dictionary<AstNode, Scope> ScopeMap { get; }

        public AnalysisResult(
            List<Token> tokens,
            ModuleDecl ast,
            DiagnosticBag diagnostics,
            Dictionary<AstNode, string> typeMap,
            Dictionary<AstNode, Scope> scopeMap)
        {
            Tokens = tokens;
            Ast = ast;
            Diagnostics = diagnostics;
            TypeMap = typeMap;
            ScopeMap = scopeMap;
        }
    }
}
