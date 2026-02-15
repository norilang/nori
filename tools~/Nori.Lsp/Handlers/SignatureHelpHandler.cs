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
    public class SignatureHelpHandler
    {
        private readonly DocumentManager _documents;
        private readonly IExternCatalog _catalog;

        public SignatureHelpHandler(DocumentManager documents, IExternCatalog catalog)
        {
            _documents = documents;
            _catalog = catalog;
        }

        public SignatureHelp Handle(SignatureHelpParams p)
        {
            var uri = p.TextDocument.Uri.ToString();
            var doc = _documents.GetDocument(uri);
            if (doc?.Ast == null) return null;

            var (line, col) = PositionMapper.ToNori(p.Position);
            var callCtx = AstNodeFinder.FindCallContext(doc.Ast, line, col);
            if (callCtx == null) return null;

            var (call, paramIndex) = callCtx.Value;

            var signatures = new List<SignatureInformation>();
            int activeSignature = 0;

            if (call.Callee is MemberExpr memberExpr && memberExpr.Object is Expr objExpr)
            {
                string ownerType = objExpr.ResolvedType;
                if (ownerType != null)
                {
                    bool isStatic = objExpr is NameExpr name &&
                        (name.ResolvedSymbol?.Kind == NoriSymbolKind.StaticType ||
                         name.ResolvedSymbol?.Kind == NoriSymbolKind.EnumType);

                    if (isStatic && objExpr is NameExpr sn)
                        ownerType = sn.ResolvedSymbol.UdonType;

                    var overloads = isStatic
                        ? _catalog.GetStaticMethodOverloads(ownerType, memberExpr.MemberName)
                        : _catalog.GetMethodOverloads(ownerType, memberExpr.MemberName);

                    foreach (var sig in overloads)
                        signatures.Add(ToSignatureInfo(memberExpr.MemberName, sig));

                    // Find best matching overload
                    if (call.ResolvedExtern != null)
                    {
                        for (int i = 0; i < overloads.Count; i++)
                        {
                            if (overloads[i].Extern == call.ResolvedExtern.Extern)
                            {
                                activeSignature = i;
                                break;
                            }
                        }
                    }
                }
            }
            else if (call.Callee is NameExpr funcName)
            {
                // User-defined function
                foreach (var decl in doc.Ast.Declarations)
                {
                    if (decl is FunctionDecl f && f.Name == funcName.Name)
                    {
                        var paramInfos = f.Parameters.Select(p2 =>
                            new ParameterInformation
                            {
                                Label = $"{p2.Name}: {p2.TypeName}",
                            }).ToArray();

                        var ret = f.ReturnTypeName ?? "void";
                        var paramStr = string.Join(", ", f.Parameters.Select(p2 => $"{p2.Name}: {p2.TypeName}"));

                        signatures.Add(new SignatureInformation
                        {
                            Label = $"{f.Name}({paramStr}) -> {ret}",
                            Parameters = paramInfos,
                        });
                        break;
                    }
                }
            }

            if (signatures.Count == 0) return null;

            return new SignatureHelp
            {
                Signatures = signatures.ToArray(),
                ActiveSignature = activeSignature,
                ActiveParameter = paramIndex
            };
        }

        private static SignatureInformation ToSignatureInfo(string methodName, ExternSignature sig)
        {
            var paramInfos = new List<ParameterInformation>();
            var paramStrs = new List<string>();

            for (int i = 0; i < sig.ParamTypes.Length; i++)
            {
                string name = sig.Params != null && i < sig.Params.Length
                    ? sig.Params[i].Name : $"arg{i}";
                string type = TypeSystem.ToNoriType(sig.ParamTypes[i]);
                string label = $"{name}: {type}";

                paramStrs.Add(label);
                paramInfos.Add(new ParameterInformation { Label = label });
            }

            string ret = TypeSystem.ToNoriType(sig.ReturnType);
            return new SignatureInformation
            {
                Label = $"{methodName}({string.Join(", ", paramStrs)}) -> {ret}",
                Parameters = paramInfos.ToArray(),
            };
        }
    }
}
