using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspDiag = Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic;
using NoriDiag = Nori.Compiler.Diagnostic;

namespace Nori.Lsp.Utilities
{
    public static class LspDiagnosticConverter
    {
        public static LspDiag[] Convert(IReadOnlyList<NoriDiag> noriDiagnostics)
        {
            return noriDiagnostics.Select(Convert).ToArray();
        }

        public static LspDiag Convert(NoriDiag diag)
        {
            return new LspDiag
            {
                Range = PositionMapper.ToLspRange(diag.Span),
                Severity = ConvertSeverity(diag.Severity),
                Code = diag.Code,
                Source = "nori",
                Message = FormatMessage(diag),
            };
        }

        private static DiagnosticSeverity ConvertSeverity(Nori.Compiler.Severity severity)
        {
            switch (severity)
            {
                case Nori.Compiler.Severity.Error: return DiagnosticSeverity.Error;
                case Nori.Compiler.Severity.Warning: return DiagnosticSeverity.Warning;
                case Nori.Compiler.Severity.Info: return DiagnosticSeverity.Information;
                default: return DiagnosticSeverity.Information;
            }
        }

        private static string FormatMessage(NoriDiag diag)
        {
            var msg = diag.Message;
            if (!string.IsNullOrEmpty(diag.Hint))
                msg += "\n" + diag.Hint;
            return msg;
        }
    }
}
