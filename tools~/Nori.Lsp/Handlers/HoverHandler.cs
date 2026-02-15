using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Nori.Compiler;
using Nori.Lsp.Server;
using Nori.Lsp.Utilities;
using NoriSymbolKind = Nori.Compiler.SymbolKind;

namespace Nori.Lsp.Handlers
{
    public class HoverHandler
    {
        private readonly DocumentManager _documents;
        private readonly IExternCatalog _catalog;

        public HoverHandler(DocumentManager documents, IExternCatalog catalog)
        {
            _documents = documents;
            _catalog = catalog;
        }

        public Hover Handle(TextDocumentPositionParams p)
        {
            var uri = p.TextDocument.Uri.ToString();
            var doc = _documents.GetDocument(uri);
            if (doc?.Ast == null) return null;

            var (line, col) = PositionMapper.ToNori(p.Position);
            var node = AstNodeFinder.FindDeepestNode(doc.Ast, line, col);

            string markdown = GetHoverContent(node, doc);
            if (markdown == null) return null;

            return new Hover
            {
                Contents = new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = markdown
                },
                Range = PositionMapper.ToLspRange(node.Span)
            };
        }

        private string GetHoverContent(AstNode node, DocumentState doc)
        {
            switch (node)
            {
                case NameExpr name when name.ResolvedSymbol != null:
                    return FormatSymbolHover(name.ResolvedSymbol);

                case MemberExpr member:
                    return FormatMemberHover(member);

                case CallExpr call:
                    return FormatCallHover(call);

                case VarDecl varDecl:
                    return FormatVarDeclHover(varDecl);

                case FunctionDecl funcDecl:
                    return FormatFunctionDeclHover(funcDecl);

                case EventHandlerDecl eventDecl:
                    return FormatEventDeclHover(eventDecl);

                case CustomEventDecl customEvent:
                    return $"`event {customEvent.Name}`";

                case LocalVarStmt localVar:
                    return $"`{localVar.Name}: {localVar.TypeName ?? "unknown"}`";

                default:
                    if (node is Expr expr && expr.ResolvedType != null)
                        return $"`{TypeSystem.ToNoriType(expr.ResolvedType)}`";
                    return null;
            }
        }

        private string FormatSymbolHover(Symbol sym)
        {
            var type = TypeSystem.ToNoriType(sym.UdonType);

            switch (sym.Kind)
            {
                case NoriSymbolKind.Variable:
                    var parts = new List<string>();
                    if (sym.IsPublic) parts.Add("pub");
                    if (sym.SyncMode != SyncMode.NotSynced)
                        parts.Add($"sync {sym.SyncMode.ToString().ToLowerInvariant()}");
                    parts.Add($"{sym.Name}: {type}");
                    return $"`{string.Join(" ", parts)}`";

                case NoriSymbolKind.Function:
                    return $"`fn {sym.Name}() -> {type}`";

                case NoriSymbolKind.Parameter:
                    return $"`{sym.Name}: {type}` (parameter)";

                case NoriSymbolKind.Builtin:
                    return $"`{sym.Name}: {type}` (built-in)";

                case NoriSymbolKind.StaticType:
                    return $"`{sym.Name}` -> `{sym.UdonType}`\n\nStatic type";

                case NoriSymbolKind.EnumType:
                    var enumInfo = _catalog.ResolveEnum(sym.UdonType);
                    if (enumInfo != null)
                    {
                        var values = string.Join(", ", enumInfo.Values.Keys.Take(10));
                        if (enumInfo.Values.Count > 10) values += ", ...";
                        return $"`enum {sym.Name}`\n\nValues: {values}";
                    }
                    return $"`enum {sym.Name}`";

                case NoriSymbolKind.CustomEvent:
                    return $"`event {sym.Name}`";

                default:
                    return $"`{sym.Name}: {type}`";
            }
        }

        private string FormatMemberHover(MemberExpr member)
        {
            if (member.IsEnumValue)
            {
                var enumType = TypeSystem.ToNoriType(member.EnumUdonType);
                return $"`{enumType}.{member.MemberName}` = {member.EnumIntValue}";
            }

            if (member.ResolvedGetter != null)
            {
                var propType = TypeSystem.ToNoriType(member.ResolvedType);
                var readOnly = member.ResolvedSetter == null ? " (read-only)" : "";
                return $"`{member.MemberName}: {propType}`{readOnly}\n\n**Extern:** `{member.ResolvedGetter.Extern}`";
            }

            return null;
        }

        private string FormatCallHover(CallExpr call)
        {
            if (call.ResolvedExtern != null)
            {
                var sig = call.ResolvedExtern;
                var paramStrs = new List<string>();
                for (int i = 0; i < sig.ParamTypes.Length; i++)
                {
                    string name = sig.Params != null && i < sig.Params.Length
                        ? sig.Params[i].Name : $"arg{i}";
                    paramStrs.Add($"{name}: {TypeSystem.ToNoriType(sig.ParamTypes[i])}");
                }
                string ret = TypeSystem.ToNoriType(sig.ReturnType);
                string methodName = call.Callee is MemberExpr m ? m.MemberName : "call";
                return $"`{methodName}({string.Join(", ", paramStrs)}) -> {ret}`\n\n**Extern:** `{sig.Extern}`";
            }

            if (call.ResolvedFunctionName != null)
            {
                var ret = TypeSystem.ToNoriType(call.ResolvedType);
                return $"`fn {call.ResolvedFunctionName}() -> {ret}`";
            }

            return null;
        }

        private string FormatVarDeclHover(VarDecl v)
        {
            var parts = new List<string>();
            if (v.IsPublic) parts.Add("pub");
            if (v.SyncMode != SyncMode.NotSynced)
                parts.Add($"sync {v.SyncMode.ToString().ToLowerInvariant()}");
            var arr = v.IsArray ? "[]" : "";
            parts.Add($"let {v.Name}: {v.TypeName}{arr}");
            return $"`{string.Join(" ", parts)}`";
        }

        private string FormatFunctionDeclHover(FunctionDecl f)
        {
            var paramStrs = f.Parameters.Select(p => $"{p.Name}: {p.TypeName}");
            var ret = f.ReturnTypeName ?? "void";
            return $"`fn {f.Name}({string.Join(", ", paramStrs)}) -> {ret}`";
        }

        private string FormatEventDeclHover(EventHandlerDecl e)
        {
            if (e.Parameters.Count > 0)
            {
                var paramStrs = e.Parameters.Select(p => $"{p.Name}: {p.TypeName}");
                return $"`on {e.EventName}({string.Join(", ", paramStrs)})`";
            }
            return $"`on {e.EventName}`";
        }
    }
}
