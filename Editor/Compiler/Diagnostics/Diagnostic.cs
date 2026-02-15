namespace Nori.Compiler
{
    public enum Severity
    {
        Error,
        Warning,
        Info,
    }

    public class Diagnostic
    {
        public Severity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public SourceSpan Span { get; }
        public string Explanation { get; }
        public string Hint { get; }

        public Diagnostic(Severity severity, string code, string message, SourceSpan span,
            string explanation = null, string hint = null)
        {
            Severity = severity;
            Code = code;
            Message = message;
            Span = span;
            Explanation = explanation;
            Hint = hint;
        }

        public override string ToString()
        {
            var sev = Severity == Severity.Error ? "error" : Severity == Severity.Warning ? "warning" : "info";
            return $"{sev}[{Code}]: {Message} at {Span}";
        }
    }
}
