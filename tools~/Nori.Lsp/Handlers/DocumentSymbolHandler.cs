using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Nori.Compiler;
using Nori.Lsp.Server;
using Nori.Lsp.Utilities;
using LspSymbolKind = Microsoft.VisualStudio.LanguageServer.Protocol.SymbolKind;

namespace Nori.Lsp.Handlers
{
    public class DocumentSymbolHandler
    {
        private readonly DocumentManager _documents;

        public DocumentSymbolHandler(DocumentManager documents)
        {
            _documents = documents;
        }

        public DocumentSymbol[] Handle(DocumentSymbolParams p)
        {
            var uri = p.TextDocument.Uri.ToString();
            var doc = _documents.GetDocument(uri);
            if (doc?.Ast == null) return Array.Empty<DocumentSymbol>();

            var symbols = new List<DocumentSymbol>();

            foreach (var decl in doc.Ast.Declarations)
            {
                switch (decl)
                {
                    case VarDecl v:
                        var arr = v.IsArray ? "[]" : "";
                        symbols.Add(new DocumentSymbol
                        {
                            Name = v.Name,
                            Detail = $"{v.TypeName}{arr}",
                            Kind = LspSymbolKind.Variable,
                            Range = PositionMapper.ToLspRange(v.Span),
                            SelectionRange = PositionMapper.ToLspRange(v.Span),
                        });
                        break;

                    case EventHandlerDecl e:
                        symbols.Add(new DocumentSymbol
                        {
                            Name = $"on {e.EventName}",
                            Kind = LspSymbolKind.Event,
                            Range = PositionMapper.ToLspRange(e.Span),
                            SelectionRange = PositionMapper.ToLspRange(e.Span),
                        });
                        break;

                    case CustomEventDecl ce:
                        symbols.Add(new DocumentSymbol
                        {
                            Name = $"event {ce.Name}",
                            Kind = LspSymbolKind.Event,
                            Range = PositionMapper.ToLspRange(ce.Span),
                            SelectionRange = PositionMapper.ToLspRange(ce.Span),
                        });
                        break;

                    case FunctionDecl f:
                        var paramStr = string.Join(", ", f.Parameters.Select(p2 => $"{p2.Name}: {p2.TypeName}"));
                        var ret = f.ReturnTypeName ?? "void";
                        symbols.Add(new DocumentSymbol
                        {
                            Name = f.Name,
                            Detail = $"({paramStr}) -> {ret}",
                            Kind = LspSymbolKind.Function,
                            Range = PositionMapper.ToLspRange(f.Span),
                            SelectionRange = PositionMapper.ToLspRange(f.Span),
                        });
                        break;
                }
            }

            return symbols.ToArray();
        }
    }
}
