using System;
using System.Text;

namespace Nori.Compiler
{
    public static class DiagnosticPrinter
    {
        public static string Format(Diagnostic diagnostic, string source = null)
        {
            var sb = new StringBuilder();

            string sev = diagnostic.Severity == Severity.Error ? "error"
                : diagnostic.Severity == Severity.Warning ? "warning" : "info";

            sb.AppendLine($"{sev}[{diagnostic.Code}]: {diagnostic.Message}");

            if (diagnostic.Span.File != null && diagnostic.Span.Start.Line > 0)
            {
                sb.AppendLine($"  --> {diagnostic.Span.File}:{diagnostic.Span.Start.Line}:{diagnostic.Span.Start.Column}");

                if (source != null)
                {
                    string[] lines = source.Split('\n');
                    int lineIdx = diagnostic.Span.Start.Line - 1;
                    if (lineIdx >= 0 && lineIdx < lines.Length)
                    {
                        string line = lines[lineIdx].TrimEnd('\r');
                        int lineNum = diagnostic.Span.Start.Line;
                        string gutter = lineNum.ToString().PadLeft(4);

                        sb.AppendLine("   |");
                        sb.AppendLine($"{gutter} | {line}");

                        // Underline
                        int startCol = Math.Max(0, diagnostic.Span.Start.Column - 1);
                        int endCol = diagnostic.Span.End.Line == diagnostic.Span.Start.Line
                            ? Math.Max(startCol + 1, diagnostic.Span.End.Column - 1)
                            : line.Length;
                        int underlineLen = Math.Max(1, endCol - startCol);

                        string padding = new string(' ', startCol);
                        string underline = new string('^', underlineLen);

                        sb.AppendLine($"{"",4} | {padding}{underline}");
                        sb.AppendLine("   |");
                    }
                }
            }

            if (diagnostic.Explanation != null)
            {
                sb.AppendLine($"   {diagnostic.Explanation}");
                sb.AppendLine();
            }

            if (diagnostic.Hint != null)
            {
                sb.AppendLine($"   help: {diagnostic.Hint}");
            }

            return sb.ToString();
        }

        public static string FormatAll(DiagnosticBag diagnostics, string source = null)
        {
            var sb = new StringBuilder();
            foreach (var diag in diagnostics.All)
            {
                sb.AppendLine(Format(diag, source));
            }

            int errors = diagnostics.ErrorCount;
            int warnings = diagnostics.WarningCount;

            if (errors > 0 || warnings > 0)
            {
                sb.Append($"{errors} error(s)");
                if (warnings > 0)
                    sb.Append($", {warnings} warning(s)");
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
