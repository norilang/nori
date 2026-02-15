using System.Collections.Generic;

namespace Nori.Compiler
{
    public static class ErrorDatabase
    {
        public class ErrorInfo
        {
            public string Title { get; }
            public string Explanation { get; }
            public string Suggestion { get; }

            public ErrorInfo(string title, string explanation, string suggestion)
            {
                Title = title;
                Explanation = explanation;
                Suggestion = suggestion;
            }
        }

        private static readonly Dictionary<string, ErrorInfo> Errors = new Dictionary<string, ErrorInfo>
        {
            ["E0001"] = new ErrorInfo(
                "Unterminated string literal",
                "The string was opened with a '\"' but never closed before end of line or file. " +
                "This usually means a missing closing quote.",
                "Add a closing '\"' to terminate the string."),

            ["E0002"] = new ErrorInfo(
                "Unterminated block comment",
                "A block comment was opened with '/*' but never closed with '*/'. " +
                "Block comments can be nested in Nori, so make sure every '/*' has a matching '*/'.",
                "Add a closing '*/' to terminate the comment."),

            ["E0003"] = new ErrorInfo(
                "Unexpected character",
                "The lexer encountered a character that is not valid in Nori source code.",
                "Remove or replace the unexpected character."),

            ["E0010"] = new ErrorInfo(
                "Unexpected token at top level",
                "Top-level declarations in a .nori file must start with one of: " +
                "let, pub, sync, on, event, or fn.",
                "Check the spelling and placement of your declaration."),

            ["E0011"] = new ErrorInfo(
                "Expected 'let' after 'pub'",
                "'pub' is a modifier for variable declarations. It must be followed by 'let' " +
                "to declare a public variable that appears in the Unity Inspector.",
                "Use: pub let variableName: type = value"),

            ["E0012"] = new ErrorInfo(
                "Invalid sync mode",
                "The sync keyword must be followed by a valid interpolation mode. " +
                "Valid modes are: none (manual sync), linear (linear interpolation), " +
                "smooth (smooth interpolation).",
                "Use one of: sync none, sync linear, sync smooth"),

            ["E0020"] = new ErrorInfo(
                "Invalid send target",
                "Network event targets must be 'All' (send to all clients) or 'Owner' " +
                "(send to the owner of this GameObject).",
                "Use: send EventName to All  or  send EventName to Owner"),

            ["E0030"] = new ErrorInfo(
                "Expected expression",
                "The parser expected an expression (a value, variable, function call, etc.) " +
                "but found something else.",
                "Check for missing operands or misplaced operators."),

            ["E0031"] = new ErrorInfo(
                "Unexpected token",
                "The parser found a token that doesn't fit the expected syntax at this position.",
                "Check the syntax of your code."),

            ["E0032"] = new ErrorInfo(
                "Expected identifier",
                "An identifier (name) was expected at this position.",
                "Provide a valid name (letters, numbers, underscores, starting with a letter or underscore)."),

            ["E0040"] = new ErrorInfo(
                "Type mismatch",
                "The types in this expression are not compatible. Nori's type system maps directly " +
                "to Udon types, and implicit conversions are limited.",
                "Check that both sides of the operation have compatible types."),

            ["E0042"] = new ErrorInfo(
                "Generic types are not supported",
                "Udon's type system is based on concrete .NET types exposed through the extern system. " +
                ".NET generics (List<T>, Dictionary<K,V>, etc.) are not part of the extern whitelist. " +
                "This is a fundamental limitation of the Udon VM, not a Nori limitation.",
                "Use a typed array instead: int[], string[], GameObject[]"),

            ["E0070"] = new ErrorInfo(
                "Undefined variable",
                "This variable name was not found in the current scope. Variables must be declared " +
                "with 'let' before they can be used.",
                "Check for typos in the variable name, or add a declaration."),

            ["E0071"] = new ErrorInfo(
                "Undefined function",
                "This function name was not found. Functions must be declared with 'fn' " +
                "before they can be called.",
                "Check for typos in the function name, or add a function declaration."),

            ["E0100"] = new ErrorInfo(
                "Recursion detected",
                "Udon has no call stack, so recursive function calls are not possible. " +
                "The compiler detected a cycle in the call graph where functions call each other " +
                "in a way that would require recursion.",
                "Rewrite using iterative loops (while, for) instead of recursive calls."),

            ["E0130"] = new ErrorInfo(
                "Method not found",
                "The method was not found on the given type, or no overload matches the provided arguments.",
                "Check the method name and argument types. If overloads are listed, match one of them."),

            ["E0131"] = new ErrorInfo(
                "Ambiguous overload",
                "Multiple method overloads match the provided arguments equally well. " +
                "The compiler cannot determine which one to call.",
                "Add explicit type conversions to disambiguate."),

            ["E0132"] = new ErrorInfo(
                "Property not writable",
                "This property is read-only and does not have a setter. " +
                "Assignment to read-only properties is not allowed.",
                "Check if a different property or method should be used."),

            ["E0133"] = new ErrorInfo(
                "Enum value not found",
                "The specified enum value does not exist on this enum type.",
                "Check the spelling of the enum value name."),
        };

        public static ErrorInfo Lookup(string code)
        {
            return Errors.TryGetValue(code, out var info) ? info : null;
        }

        public static IEnumerable<string> AllCodes => Errors.Keys;
    }
}
