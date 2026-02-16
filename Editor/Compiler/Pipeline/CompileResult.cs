using System;
using System.Collections.Generic;
using System.Linq;

namespace Nori.Compiler
{
    public class CompileResult
    {
        public bool Success { get; }
        public string Uasm { get; }
        public ModuleDecl Ast { get; }
        public DiagnosticBag Diagnostics { get; }
        public NoriCompileMetadata Metadata { get; }

        private CompileResult(bool success, string uasm, ModuleDecl ast,
            DiagnosticBag diagnostics, NoriCompileMetadata metadata)
        {
            Success = success;
            Uasm = uasm;
            Ast = ast;
            Diagnostics = diagnostics;
            Metadata = metadata;
        }

        public static CompileResult Succeeded(string uasm, ModuleDecl ast,
            DiagnosticBag diagnostics, NoriCompileMetadata metadata)
        {
            return new CompileResult(true, uasm, ast, diagnostics, metadata);
        }

        public static CompileResult Failed(DiagnosticBag diagnostics,
            ModuleDecl ast = null, NoriCompileMetadata metadata = null)
        {
            return new CompileResult(false, null, ast, diagnostics,
                metadata ?? NoriCompileMetadata.FromDiagnostics(diagnostics));
        }
    }

    [Serializable]
    public class NoriCompileMetadata
    {
        public List<VarInfo> PublicVars = new List<VarInfo>();
        public List<VarInfo> SyncVars = new List<VarInfo>();
        public List<string> Events = new List<string>();
        public List<string> CustomEvents = new List<string>();
        public List<string> Functions = new List<string>();
        public int ErrorCount;
        public int WarningCount;

        [Serializable]
        public class VarInfo
        {
            public string Name;
            public string TypeName;
            public bool IsArray;
            public string DefaultValue;
            public string SyncMode;
            public string DocComment;
        }

        [Serializable]
        public class DiagInfo
        {
            public string Severity;
            public string Code;
            public string Message;
            public int Line;
            public int Column;
            public string Hint;
        }

        public List<DiagInfo> Diagnostics = new List<DiagInfo>();

        public static NoriCompileMetadata FromAst(ModuleDecl module, DiagnosticBag diagnostics,
            string source = null)
        {
            var meta = new NoriCompileMetadata();
            meta.ErrorCount = diagnostics.ErrorCount;
            meta.WarningCount = diagnostics.WarningCount;

            // Populate diagnostics
            foreach (var d in diagnostics.All)
            {
                meta.Diagnostics.Add(new DiagInfo
                {
                    Severity = d.Severity.ToString().ToLowerInvariant(),
                    Code = d.Code,
                    Message = d.Message,
                    Line = d.Span.Start.Line,
                    Column = d.Span.Start.Column,
                    Hint = d.Hint,
                });
            }

            if (module == null) return meta;

            string[] sourceLines = source?.Split('\n');

            foreach (var decl in module.Declarations)
            {
                switch (decl)
                {
                    case VarDecl v:
                        if (v.IsPublic)
                        {
                            var varInfo = new VarInfo
                            {
                                Name = v.Name,
                                TypeName = v.TypeName,
                                IsArray = v.IsArray,
                            };

                            // Extract /// doc comments above the declaration
                            if (sourceLines != null)
                                varInfo.DocComment = ExtractDocComment(sourceLines, v.Span.Start.Line);

                            meta.PublicVars.Add(varInfo);
                        }
                        if (v.SyncMode != SyncMode.NotSynced)
                        {
                            meta.SyncVars.Add(new VarInfo
                            {
                                Name = v.Name,
                                TypeName = v.TypeName,
                                IsArray = v.IsArray,
                                SyncMode = v.SyncMode.ToString().ToLowerInvariant(),
                            });
                        }
                        break;
                    case EventHandlerDecl e:
                        meta.Events.Add(e.EventName);
                        break;
                    case CustomEventDecl ce:
                        meta.CustomEvents.Add(ce.Name);
                        break;
                    case FunctionDecl f:
                        meta.Functions.Add(f.Name);
                        break;
                }
            }

            return meta;
        }

        private static string ExtractDocComment(string[] lines, int declLine)
        {
            int idx = declLine - 2; // declLine is 1-based, go to line above
            var commentLines = new List<string>();
            for (int i = idx; i >= 0; i--)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith("///"))
                    commentLines.Insert(0, trimmed.Substring(3).Trim());
                else if (trimmed == "")
                    continue;
                else
                    break;
            }
            return commentLines.Count > 0 ? string.Join(" ", commentLines) : null;
        }

        public static NoriCompileMetadata FromDiagnostics(DiagnosticBag diagnostics)
        {
            var meta = new NoriCompileMetadata
            {
                ErrorCount = diagnostics.ErrorCount,
                WarningCount = diagnostics.WarningCount,
            };

            foreach (var d in diagnostics.All)
            {
                meta.Diagnostics.Add(new DiagInfo
                {
                    Severity = d.Severity.ToString().ToLowerInvariant(),
                    Code = d.Code,
                    Message = d.Message,
                    Line = d.Span.Start.Line,
                    Column = d.Span.Start.Column,
                    Hint = d.Hint,
                });
            }

            return meta;
        }
    }
}
