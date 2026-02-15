using System.Collections.Generic;
using System.Linq;

namespace Nori.Compiler
{
    public class DiagnosticBag
    {
        private readonly List<Diagnostic> _diagnostics = new List<Diagnostic>();

        public bool HasErrors => _diagnostics.Any(d => d.Severity == Severity.Error);
        public IReadOnlyList<Diagnostic> All => _diagnostics;
        public int ErrorCount => _diagnostics.Count(d => d.Severity == Severity.Error);
        public int WarningCount => _diagnostics.Count(d => d.Severity == Severity.Warning);

        public void Report(Diagnostic diagnostic) => _diagnostics.Add(diagnostic);

        public void ReportError(string code, string message, SourceSpan span,
            string explanation = null, string hint = null)
        {
            _diagnostics.Add(new Diagnostic(Severity.Error, code, message, span, explanation, hint));
        }

        public void ReportWarning(string code, string message, SourceSpan span,
            string explanation = null, string hint = null)
        {
            _diagnostics.Add(new Diagnostic(Severity.Warning, code, message, span, explanation, hint));
        }

        public void ReportInfo(string code, string message, SourceSpan span,
            string explanation = null, string hint = null)
        {
            _diagnostics.Add(new Diagnostic(Severity.Info, code, message, span, explanation, hint));
        }

        // Lexer errors
        public void ReportUnterminatedString(SourceSpan span) =>
            ReportError("E0001", "Unterminated string literal", span,
                "The string was opened but never closed before end of line or file.",
                "Add a closing '\"' to terminate the string.");

        public void ReportUnterminatedComment(SourceSpan span) =>
            ReportError("E0002", "Unterminated block comment", span,
                "A block comment '/*' was opened but never closed.",
                "Add a closing '*/' to terminate the comment.");

        // Parser errors
        public void ReportUnexpectedToken(string expected, Token found) =>
            ReportError("E0031", $"Expected {expected}, found '{found.Text}'", found.Span);

        public void ReportExpectedExpression(Token found) =>
            ReportError("E0030", $"Expected expression, found '{found.Text}'", found.Span);

        public void ReportExpectedLetAfterPub(SourceSpan span) =>
            ReportError("E0011", "'pub' must be followed by 'let'", span,
                "'pub' is a modifier for variable declarations.",
                "Add 'let' after 'pub': pub let variableName: type = value");

        public void ReportInvalidSyncMode(string mode, SourceSpan span) =>
            ReportError("E0012", $"Invalid sync mode '{mode}'", span,
                "Valid sync modes are: none, linear, smooth.",
                "Use one of: sync none, sync linear, sync smooth");

        // Semantic errors
        public void ReportTypeMismatch(string expected, string actual, SourceSpan span) =>
            ReportError("E0040", $"Type mismatch: expected '{expected}', found '{actual}'", span);

        public void ReportGenericTypeUsed(string typeName, SourceSpan span) =>
            ReportError("E0042", $"Generic types are not supported", span,
                "Udon does not support generic collection types like List<T>. The VM only supports fixed-type arrays.",
                $"Use a typed array instead.");

        public void ReportUndefinedVariable(string name, SourceSpan span, string suggestion = null) =>
            ReportError("E0070", $"Undefined variable '{name}'" +
                (suggestion != null ? $". Did you mean '{suggestion}'?" : ""), span);

        public void ReportUndefinedFunction(string name, SourceSpan span, string suggestion = null) =>
            ReportError("E0071", $"Undefined function '{name}'" +
                (suggestion != null ? $". Did you mean '{suggestion}'?" : ""), span);

        public void ReportRecursionDetected(string chain, SourceSpan span) =>
            ReportError("E0100", $"Recursion is not allowed: {chain}", span,
                "Udon has no call stack, so recursive function calls are not possible.",
                "Rewrite using iterative loops instead.");

        public void ReportInvalidSendTarget(string target, SourceSpan span) =>
            ReportError("E0020", $"Invalid send target '{target}'", span,
                "Network event targets must be 'All' or 'Owner'.",
                "Use: send EventName to All  or  send EventName to Owner");

        public void ReportExpectedIdentifier(Token found) =>
            ReportError("E0032", $"Expected identifier, found '{found.Text}'", found.Span);

        // Phase 2 additions

        public void ReportAmbiguousOverload(string typeName, string methodName, SourceSpan span) =>
            ReportError("E0131", $"Ambiguous overload for '{typeName}.{methodName}'", span,
                "Multiple overloads match the provided arguments equally well.",
                "Add explicit type conversions to disambiguate.");

        public void ReportPropertyNotWritable(string propertyName, SourceSpan span) =>
            ReportError("E0132", $"Property '{propertyName}' is read-only", span,
                "This property does not have a setter.",
                "Check if a different property or method should be used.");

        public void ReportEnumValueNotFound(string valueName, string enumType, SourceSpan span) =>
            ReportError("E0133", $"Enum value '{valueName}' not found on type '{TypeSystem.ToNoriType(enumType)}'", span,
                "The enum does not contain a member with this name.",
                "Check the spelling of the enum value.");
    }
}
