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
            public string DefaultValue;
            public string SyncMode;
        }

        public static NoriCompileMetadata FromAst(ModuleDecl module, DiagnosticBag diagnostics)
        {
            var meta = new NoriCompileMetadata();
            meta.ErrorCount = diagnostics.ErrorCount;
            meta.WarningCount = diagnostics.WarningCount;

            if (module == null) return meta;

            foreach (var decl in module.Declarations)
            {
                switch (decl)
                {
                    case VarDecl v:
                        if (v.IsPublic)
                        {
                            meta.PublicVars.Add(new VarInfo
                            {
                                Name = v.Name,
                                TypeName = v.TypeName,
                            });
                        }
                        if (v.SyncMode != SyncMode.NotSynced)
                        {
                            meta.SyncVars.Add(new VarInfo
                            {
                                Name = v.Name,
                                TypeName = v.TypeName,
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

        public static NoriCompileMetadata FromDiagnostics(DiagnosticBag diagnostics)
        {
            return new NoriCompileMetadata
            {
                ErrorCount = diagnostics.ErrorCount,
                WarningCount = diagnostics.WarningCount,
            };
        }
    }
}
