using System;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Nori.Compiler;
using Nori.Lsp.Server;
using Nori.Lsp.Utilities;

namespace Nori.Lsp.Handlers
{
    public class DefinitionHandler
    {
        private readonly DocumentManager _documents;

        public DefinitionHandler(DocumentManager documents)
        {
            _documents = documents;
        }

        public Location[] Handle(TextDocumentPositionParams p)
        {
            var uri = p.TextDocument.Uri.ToString();
            var doc = _documents.GetDocument(uri);
            if (doc?.Ast == null) return Array.Empty<Location>();

            var (line, col) = PositionMapper.ToNori(p.Position);
            var node = AstNodeFinder.FindDeepestNode(doc.Ast, line, col);

            SourceSpan? declSpan = null;

            switch (node)
            {
                case NameExpr name when name.ResolvedSymbol != null:
                    declSpan = name.ResolvedSymbol.DeclSpan;
                    break;

                case CallExpr call when call.ResolvedFunctionName != null:
                    // Find the function declaration
                    foreach (var decl in doc.Ast.Declarations)
                    {
                        if (decl is FunctionDecl f && f.Name == call.ResolvedFunctionName)
                        {
                            declSpan = f.Span;
                            break;
                        }
                    }
                    break;

                case SendStmt send:
                    // Find the custom event declaration
                    foreach (var decl in doc.Ast.Declarations)
                    {
                        if (decl is CustomEventDecl ce && ce.Name == send.EventName)
                        {
                            declSpan = ce.Span;
                            break;
                        }
                    }
                    break;
            }

            if (declSpan == null || declSpan.Value.Start.Line == 0)
                return Array.Empty<Location>();

            return new[]
            {
                new Location
                {
                    Uri = p.TextDocument.Uri,
                    Range = PositionMapper.ToLspRange(declSpan.Value)
                }
            };
        }
    }
}
