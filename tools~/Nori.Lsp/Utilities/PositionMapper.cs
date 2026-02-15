using Microsoft.VisualStudio.LanguageServer.Protocol;
using Nori.Compiler;

namespace Nori.Lsp.Utilities
{
    /// <summary>
    /// Converts between LSP 0-based positions and Nori compiler 1-based positions.
    /// </summary>
    public static class PositionMapper
    {
        /// <summary>Convert an LSP 0-based position to Nori 1-based line/column.</summary>
        public static (int line, int column) ToNori(Position lspPosition)
        {
            return (lspPosition.Line + 1, lspPosition.Character + 1);
        }

        /// <summary>Convert a Nori 1-based SourcePos to an LSP 0-based Position.</summary>
        public static Position ToLsp(SourcePos noriPos)
        {
            return new Position(
                System.Math.Max(0, noriPos.Line - 1),
                System.Math.Max(0, noriPos.Column - 1));
        }

        /// <summary>Convert a Nori SourceSpan to an LSP Range.</summary>
        public static Range ToLspRange(SourceSpan span)
        {
            return new Range
            {
                Start = ToLsp(span.Start),
                End = ToLsp(span.End)
            };
        }
    }
}
