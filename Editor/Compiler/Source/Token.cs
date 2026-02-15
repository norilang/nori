namespace Nori.Compiler
{
    public enum TokenKind
    {
        // Literals
        IntLiteral,
        FloatLiteral,
        StringLiteral,
        True,
        False,
        Null,

        // Identifier
        Identifier,

        // Keywords
        Let,
        Pub,
        Sync,
        Fn,
        On,
        Event,
        Return,
        Send,
        To,
        If,
        Else,
        While,
        For,
        In,
        Break,
        Continue,

        // Operators
        Plus,           // +
        Minus,          // -
        Star,           // *
        Slash,          // /
        Percent,        // %
        Equals,         // =
        EqualsEquals,   // ==
        BangEquals,     // !=
        Less,           // <
        LessEquals,     // <=
        Greater,        // >
        GreaterEquals,  // >=
        And,            // &&
        Or,             // ||
        Bang,           // !
        PlusEquals,     // +=
        MinusEquals,    // -=
        StarEquals,     // *=
        SlashEquals,    // /=
        DotDot,         // ..
        Arrow,          // ->
        Dot,            // .

        // Delimiters
        LeftParen,      // (
        RightParen,     // )
        LeftBrace,      // {
        RightBrace,     // }
        LeftBracket,    // [
        RightBracket,   // ]
        Comma,          // ,
        Colon,          // :

        // Special
        Eof,
        Error,
    }

    public readonly struct Token
    {
        public readonly TokenKind Kind;
        public readonly string Text;
        public readonly SourceSpan Span;
        public readonly object LiteralValue;

        public Token(TokenKind kind, string text, SourceSpan span, object literalValue = null)
        {
            Kind = kind;
            Text = text;
            Span = span;
            LiteralValue = literalValue;
        }

        public override string ToString() => $"{Kind}({Text})";
    }
}
