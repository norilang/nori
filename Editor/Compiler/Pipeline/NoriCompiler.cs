namespace Nori.Compiler
{
    public static class NoriCompiler
    {
        public static CompileResult Compile(string source, string filePath,
            IExternCatalog catalog = null)
        {
            var diagnostics = new DiagnosticBag();
            catalog = catalog ?? BuiltinCatalog.Instance;

            // Phase 1: Lex
            var lexer = new Lexer(source, filePath, diagnostics);
            var tokens = lexer.Tokenize();
            if (diagnostics.HasErrors)
                return CompileResult.Failed(diagnostics);

            // Phase 2: Parse
            var parser = new Parser(tokens, diagnostics);
            var ast = parser.Parse();
            var metadata = NoriCompileMetadata.FromAst(ast, diagnostics);

            if (diagnostics.HasErrors)
                return CompileResult.Failed(diagnostics, ast, metadata);

            // Phase 3: Semantic Analysis
            var analyzer = new SemanticAnalyzer(ast, catalog, diagnostics);
            analyzer.Analyze();
            metadata = NoriCompileMetadata.FromAst(ast, diagnostics);

            if (diagnostics.HasErrors)
                return CompileResult.Failed(diagnostics, ast, metadata);

            // Phase 4: IR Lowering
            var lowering = new IrLowering(ast, diagnostics);
            var ir = lowering.Lower();

            // Phase 5: Emit Udon Assembly
            var emitter = new UdonEmitter(ir);
            var uasm = emitter.Emit();

            metadata = NoriCompileMetadata.FromAst(ast, diagnostics);

            return CompileResult.Succeeded(uasm, ast, diagnostics, metadata);
        }
    }
}
