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
    public class CompletionHandler
    {
        private readonly DocumentManager _documents;
        private readonly IExternCatalog _catalog;

        // Known VRChat events with parameter info
        private static readonly Dictionary<string, string> EventDescriptions = new()
        {
            ["Start"] = "Fires once when the world loads",
            ["Update"] = "Fires every frame",
            ["LateUpdate"] = "Fires every frame after Update",
            ["FixedUpdate"] = "Fires at fixed physics rate",
            ["Interact"] = "Fires when player interacts",
            ["Pickup"] = "Fires when object is picked up",
            ["Drop"] = "Fires when object is dropped",
            ["PickupUseDown"] = "Fires when use button pressed while holding",
            ["PickupUseUp"] = "Fires when use button released while holding",
            ["PlayerJoined"] = "PlayerJoined(player: Player)",
            ["PlayerLeft"] = "PlayerLeft(player: Player)",
            ["TriggerEnter"] = "TriggerEnter(other: Collider)",
            ["TriggerExit"] = "TriggerExit(other: Collider)",
            ["CollisionEnter"] = "CollisionEnter(collision: Collision)",
            ["VariableChange"] = "Fires after synced variables are deserialized",
            ["PreSerialization"] = "Fires before synced variables are serialized",
            ["PostSerialization"] = "PostSerialization(result: SerializationResult)",
            ["Enable"] = "Fires when the object is enabled",
            ["Disable"] = "Fires when the object is disabled",
            ["InputJump"] = "InputJump(value: bool) — Jump button pressed/released",
            ["InputUse"] = "InputUse(value: bool) — Use button pressed/released",
            ["InputGrab"] = "InputGrab(value: bool) — Grab button pressed/released",
            ["InputDrop"] = "InputDrop(value: bool) — Drop button pressed/released",
            ["InputMoveHorizontal"] = "InputMoveHorizontal(value: float) — Horizontal movement axis",
            ["InputMoveVertical"] = "InputMoveVertical(value: float) — Vertical movement axis",
            ["InputLookHorizontal"] = "InputLookHorizontal(value: float) — Horizontal look axis",
            ["InputLookVertical"] = "InputLookVertical(value: float) — Vertical look axis",
        };

        private static readonly string[] SyncModes = { "none", "linear", "smooth" };
        private static readonly string[] NetworkTargets = { "All", "Owner" };
        private static readonly string[] TopLevelKeywords = { "let", "pub", "sync", "fn", "on", "event" };
        private static readonly string[] StatementKeywords =
            { "let", "if", "while", "for", "return", "break", "continue", "send" };

        public CompletionHandler(DocumentManager documents, IExternCatalog catalog)
        {
            _documents = documents;
            _catalog = catalog;
        }

        public CompletionList Handle(CompletionParams p)
        {
            var uri = p.TextDocument.Uri.ToString();
            var doc = _documents.GetDocument(uri);
            if (doc?.Ast == null)
                return new CompletionList { Items = Array.Empty<CompletionItem>() };

            var (line, col) = PositionMapper.ToNori(p.Position);
            var textLine = GetLineText(doc.Text, p.Position.Line);
            var textBeforeCursor = textLine.Substring(0, Math.Min(p.Position.Character, textLine.Length));

            var items = new List<CompletionItem>();

            // Detect context from text before cursor
            if (textBeforeCursor.TrimEnd().EndsWith("."))
            {
                // Dot completion — find type of expression before dot
                AddDotCompletions(items, doc, line, col);
            }
            else if (IsAfterKeyword(textBeforeCursor, "on"))
            {
                AddEventCompletions(items);
            }
            else if (IsAfterKeyword(textBeforeCursor, "sync"))
            {
                AddSyncModeCompletions(items);
            }
            else if (IsAfterKeyword(textBeforeCursor, "to"))
            {
                AddNetworkTargetCompletions(items);
            }
            else if (textBeforeCursor.TrimEnd().EndsWith(":") || textBeforeCursor.TrimEnd().EndsWith("->"))
            {
                AddTypeCompletions(items);
            }
            else if (IsTopLevel(doc.Text, p.Position.Line))
            {
                AddTopLevelCompletions(items, doc, line, col);
            }
            else
            {
                AddStatementCompletions(items, doc, line, col);
            }

            return new CompletionList
            {
                IsIncomplete = false,
                Items = items.ToArray()
            };
        }

        private void AddDotCompletions(List<CompletionItem> items, DocumentState doc, int line, int col)
        {
            // Find the expression before the dot
            var node = AstNodeFinder.FindDeepestNode(doc.Ast, line, col - 1);

            string ownerType = null;

            if (node is Expr expr && expr.ResolvedType != null)
            {
                ownerType = expr.ResolvedType;
            }
            else if (node is NameExpr nameExpr && nameExpr.ResolvedSymbol != null)
            {
                ownerType = nameExpr.ResolvedSymbol.UdonType;
            }

            if (ownerType == null) return;

            // Properties
            foreach (var propName in _catalog.GetPropertyNames(ownerType))
            {
                var prop = _catalog.ResolveProperty(ownerType, propName);
                items.Add(new CompletionItem
                {
                    Label = propName,
                    Kind = CompletionItemKind.Property,
                    Detail = prop != null ? TypeSystem.ToNoriType(prop.Type) : null,
                });
            }

            // Methods
            foreach (var methodName in _catalog.GetMethodNames(ownerType))
            {
                var overloads = _catalog.GetMethodOverloads(ownerType, methodName);
                string detail = overloads.Count > 0 ? FormatSignature(overloads[0]) : null;
                items.Add(new CompletionItem
                {
                    Label = methodName,
                    Kind = CompletionItemKind.Method,
                    Detail = detail,
                    InsertText = methodName,
                });
            }
        }

        private void AddEventCompletions(List<CompletionItem> items)
        {
            foreach (var kv in EventDescriptions)
            {
                items.Add(new CompletionItem
                {
                    Label = kv.Key,
                    Kind = CompletionItemKind.Event,
                    Detail = kv.Value,
                });
            }
        }

        private void AddSyncModeCompletions(List<CompletionItem> items)
        {
            foreach (var mode in SyncModes)
            {
                items.Add(new CompletionItem
                {
                    Label = mode,
                    Kind = CompletionItemKind.EnumMember,
                    Detail = $"Sync mode: {mode}",
                });
            }
        }

        private void AddNetworkTargetCompletions(List<CompletionItem> items)
        {
            foreach (var target in NetworkTargets)
            {
                items.Add(new CompletionItem
                {
                    Label = target,
                    Kind = CompletionItemKind.EnumMember,
                    Detail = target == "All" ? "Send to all players" : "Send to object owner",
                });
            }
        }

        private void AddTypeCompletions(List<CompletionItem> items)
        {
            // Primitive types
            foreach (var type in new[] { "int", "float", "bool", "string", "double", "uint", "char", "object" })
            {
                items.Add(new CompletionItem
                {
                    Label = type,
                    Kind = CompletionItemKind.TypeParameter,
                });
            }

            // Known complex types
            foreach (var type in new[] { "Vector2", "Vector3", "Vector4", "Quaternion", "Color", "Color32",
                "Transform", "GameObject", "Rigidbody", "Collider", "AudioSource", "Animator", "Player",
                "MeshRenderer", "UdonBehaviour" })
            {
                items.Add(new CompletionItem
                {
                    Label = type,
                    Kind = CompletionItemKind.Class,
                });
            }

            // Types from catalog
            foreach (var typeName in _catalog.GetStaticTypeNames())
            {
                var noriName = TypeSystem.ToNoriType(typeName);
                if (!items.Any(i => i.Label == noriName))
                {
                    items.Add(new CompletionItem
                    {
                        Label = noriName,
                        Kind = CompletionItemKind.Class,
                    });
                }
            }
        }

        private void AddTopLevelCompletions(List<CompletionItem> items, DocumentState doc, int line, int col)
        {
            foreach (var kw in TopLevelKeywords)
            {
                items.Add(new CompletionItem { Label = kw, Kind = CompletionItemKind.Keyword });
            }
        }

        private void AddStatementCompletions(List<CompletionItem> items, DocumentState doc, int line, int col)
        {
            foreach (var kw in StatementKeywords)
            {
                items.Add(new CompletionItem { Label = kw, Kind = CompletionItemKind.Keyword });
            }

            // In-scope symbols
            if (doc.ScopeMap != null)
            {
                var node = AstNodeFinder.FindDeepestNode(doc.Ast, line, col);
                if (node != null && doc.ScopeMap.TryGetValue(node, out var scope))
                {
                    foreach (var sym in scope.AllSymbols())
                    {
                        if (sym.Kind == NoriSymbolKind.StaticType || sym.Kind == NoriSymbolKind.EnumType)
                            continue;

                        items.Add(new CompletionItem
                        {
                            Label = sym.Name,
                            Kind = SymbolKindToLsp(sym.Kind),
                            Detail = TypeSystem.ToNoriType(sym.UdonType),
                        });
                    }
                }
            }
        }

        private static CompletionItemKind SymbolKindToLsp(NoriSymbolKind kind)
        {
            switch (kind)
            {
                case NoriSymbolKind.Variable: return CompletionItemKind.Variable;
                case NoriSymbolKind.Function: return CompletionItemKind.Function;
                case NoriSymbolKind.Parameter: return CompletionItemKind.Variable;
                case NoriSymbolKind.Builtin: return CompletionItemKind.Variable;
                case NoriSymbolKind.CustomEvent: return CompletionItemKind.Event;
                default: return CompletionItemKind.Text;
            }
        }

        private static bool IsAfterKeyword(string textBefore, string keyword)
        {
            var trimmed = textBefore.TrimEnd();
            return trimmed.EndsWith(keyword + " ") || trimmed == keyword;
        }

        private static bool IsTopLevel(string text, int lineIndex)
        {
            // Rough heuristic: if the line is not indented and not inside braces
            var lines = text.Split('\n');
            if (lineIndex >= lines.Length) return true;
            var line = lines[lineIndex];
            return line.Length == 0 || !char.IsWhiteSpace(line[0]);
        }

        private static string GetLineText(string text, int lineIndex)
        {
            var lines = text.Split('\n');
            return lineIndex < lines.Length ? lines[lineIndex] : "";
        }

        private static string FormatSignature(ExternSignature sig)
        {
            var paramStrs = new List<string>();
            for (int i = 0; i < sig.ParamTypes.Length; i++)
            {
                string name = sig.Params != null && i < sig.Params.Length
                    ? sig.Params[i].Name : $"arg{i}";
                paramStrs.Add($"{name}: {TypeSystem.ToNoriType(sig.ParamTypes[i])}");
            }
            string ret = sig.ReturnType == "SystemVoid" ? "void" : TypeSystem.ToNoriType(sig.ReturnType);
            return $"({string.Join(", ", paramStrs)}) -> {ret}";
        }
    }
}
