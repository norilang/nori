using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Nori.Compiler
{
    public class Lexer
    {
        private readonly string _source;
        private readonly string _file;
        private readonly DiagnosticBag _diagnostics;
        private int _pos;
        private int _line;
        private int _col;

        private static readonly Dictionary<string, TokenKind> Keywords = new Dictionary<string, TokenKind>
        {
            ["let"] = TokenKind.Let,
            ["pub"] = TokenKind.Pub,
            ["sync"] = TokenKind.Sync,
            ["fn"] = TokenKind.Fn,
            ["on"] = TokenKind.On,
            ["event"] = TokenKind.Event,
            ["return"] = TokenKind.Return,
            ["send"] = TokenKind.Send,
            ["to"] = TokenKind.To,
            ["if"] = TokenKind.If,
            ["else"] = TokenKind.Else,
            ["while"] = TokenKind.While,
            ["for"] = TokenKind.For,
            ["in"] = TokenKind.In,
            ["break"] = TokenKind.Break,
            ["continue"] = TokenKind.Continue,
            ["true"] = TokenKind.True,
            ["false"] = TokenKind.False,
            ["null"] = TokenKind.Null,
        };

        public Lexer(string source, string file, DiagnosticBag diagnostics)
        {
            _source = source ?? "";
            _file = file;
            _diagnostics = diagnostics;
            _pos = 0;
            _line = 1;
            _col = 1;
        }

        public List<Token> Tokenize()
        {
            var tokens = new List<Token>();
            while (true)
            {
                var token = NextToken();
                tokens.Add(token);
                if (token.Kind == TokenKind.Eof)
                    break;
            }
            return tokens;
        }

        private char Peek() => _pos < _source.Length ? _source[_pos] : '\0';
        private char PeekAt(int offset) => (_pos + offset) < _source.Length ? _source[_pos + offset] : '\0';
        private bool IsAtEnd => _pos >= _source.Length;

        private char Advance()
        {
            char c = _source[_pos];
            _pos++;
            if (c == '\n')
            {
                _line++;
                _col = 1;
            }
            else
            {
                _col++;
            }
            return c;
        }

        private SourcePos CurrentPos() => new SourcePos(_line, _col);

        private void SkipWhitespace()
        {
            while (!IsAtEnd && char.IsWhiteSpace(Peek()))
                Advance();
        }

        private bool SkipLineComment()
        {
            if (Peek() == '/' && PeekAt(1) == '/')
            {
                while (!IsAtEnd && Peek() != '\n')
                    Advance();
                return true;
            }
            return false;
        }

        private bool SkipBlockComment()
        {
            if (Peek() != '/' || PeekAt(1) != '*')
                return false;

            var start = CurrentPos();
            Advance(); // /
            Advance(); // *
            int depth = 1;

            while (!IsAtEnd && depth > 0)
            {
                if (Peek() == '/' && PeekAt(1) == '*')
                {
                    Advance();
                    Advance();
                    depth++;
                }
                else if (Peek() == '*' && PeekAt(1) == '/')
                {
                    Advance();
                    Advance();
                    depth--;
                }
                else
                {
                    Advance();
                }
            }

            if (depth > 0)
            {
                _diagnostics.ReportUnterminatedComment(
                    new SourceSpan(_file, start, CurrentPos()));
            }
            return true;
        }

        private void SkipTrivia()
        {
            while (true)
            {
                SkipWhitespace();
                if (SkipLineComment()) continue;
                if (SkipBlockComment()) continue;
                break;
            }
        }

        private Token NextToken()
        {
            SkipTrivia();
            if (IsAtEnd)
                return new Token(TokenKind.Eof, "", new SourceSpan(_file, CurrentPos(), CurrentPos()));

            var start = CurrentPos();
            char c = Peek();

            // Numbers
            if (char.IsDigit(c))
                return LexNumber(start);

            // Strings
            if (c == '"')
                return LexString(start);

            // Identifiers and keywords
            if (char.IsLetter(c) || c == '_')
                return LexIdentifierOrKeyword(start);

            // Operators and delimiters
            return LexOperatorOrDelimiter(start);
        }

        private Token LexNumber(SourcePos start)
        {
            int startPos = _pos;
            while (!IsAtEnd && char.IsDigit(Peek()))
                Advance();

            // Check for float: must be '.' followed by a digit (not '..' range operator)
            if (!IsAtEnd && Peek() == '.' && PeekAt(1) != '.' && char.IsDigit(PeekAt(1)))
            {
                Advance(); // consume '.'
                while (!IsAtEnd && char.IsDigit(Peek()))
                    Advance();

                string floatText = _source.Substring(startPos, _pos - startPos);
                float floatVal = float.Parse(floatText, CultureInfo.InvariantCulture);
                return new Token(TokenKind.FloatLiteral, floatText,
                    new SourceSpan(_file, start, CurrentPos()), floatVal);
            }

            string text = _source.Substring(startPos, _pos - startPos);
            int intVal = int.Parse(text, CultureInfo.InvariantCulture);
            return new Token(TokenKind.IntLiteral, text,
                new SourceSpan(_file, start, CurrentPos()), intVal);
        }

        private Token LexString(SourcePos start)
        {
            int startPos = _pos;
            Advance(); // consume opening '"'
            var sb = new StringBuilder();
            int braceDepth = 0;

            while (!IsAtEnd)
            {
                char c = Peek();
                if (c == '\n' && braceDepth == 0)
                    break;

                if (c == '{' && braceDepth == 0)
                {
                    braceDepth++;
                    sb.Append(c);
                    Advance();
                    continue;
                }

                if (braceDepth > 0)
                {
                    if (c == '{') braceDepth++;
                    else if (c == '}') braceDepth--;
                    sb.Append(c);
                    Advance();
                    continue;
                }

                if (c == '"')
                {
                    Advance(); // consume closing '"'
                    string text = _source.Substring(startPos, _pos - startPos);
                    return new Token(TokenKind.StringLiteral, text,
                        new SourceSpan(_file, start, CurrentPos()), sb.ToString());
                }

                if (c == '\\')
                {
                    Advance(); // consume '\'
                    if (IsAtEnd) break;
                    char escaped = Peek();
                    switch (escaped)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case '{': sb.Append('{'); break;
                        case '}': sb.Append('}'); break;
                        default: sb.Append('\\'); sb.Append(escaped); break;
                    }
                    Advance();
                    continue;
                }

                sb.Append(c);
                Advance();
            }

            // Unterminated string
            _diagnostics.ReportUnterminatedString(
                new SourceSpan(_file, start, CurrentPos()));
            string errText = _source.Substring(startPos, _pos - startPos);
            return new Token(TokenKind.Error, errText,
                new SourceSpan(_file, start, CurrentPos()), sb.ToString());
        }

        private Token LexIdentifierOrKeyword(SourcePos start)
        {
            int startPos = _pos;
            while (!IsAtEnd && (char.IsLetterOrDigit(Peek()) || Peek() == '_'))
                Advance();

            string text = _source.Substring(startPos, _pos - startPos);
            var span = new SourceSpan(_file, start, CurrentPos());

            if (Keywords.TryGetValue(text, out var kind))
                return new Token(kind, text, span);

            // none, linear, smooth, All, Owner are NOT keywords - they are identifiers
            return new Token(TokenKind.Identifier, text, span);
        }

        private Token LexOperatorOrDelimiter(SourcePos start)
        {
            char c = Advance();
            var twoChar = !IsAtEnd ? Peek() : '\0';

            switch (c)
            {
                case '+':
                    if (twoChar == '=') { Advance(); return Tok(TokenKind.PlusEquals, "+=", start); }
                    return Tok(TokenKind.Plus, "+", start);
                case '-':
                    if (twoChar == '>') { Advance(); return Tok(TokenKind.Arrow, "->", start); }
                    if (twoChar == '=') { Advance(); return Tok(TokenKind.MinusEquals, "-=", start); }
                    return Tok(TokenKind.Minus, "-", start);
                case '*':
                    if (twoChar == '=') { Advance(); return Tok(TokenKind.StarEquals, "*=", start); }
                    return Tok(TokenKind.Star, "*", start);
                case '/':
                    if (twoChar == '=') { Advance(); return Tok(TokenKind.SlashEquals, "/=", start); }
                    return Tok(TokenKind.Slash, "/", start);
                case '%':
                    return Tok(TokenKind.Percent, "%", start);
                case '=':
                    if (twoChar == '=') { Advance(); return Tok(TokenKind.EqualsEquals, "==", start); }
                    return Tok(TokenKind.Equals, "=", start);
                case '!':
                    if (twoChar == '=') { Advance(); return Tok(TokenKind.BangEquals, "!=", start); }
                    return Tok(TokenKind.Bang, "!", start);
                case '<':
                    if (twoChar == '=') { Advance(); return Tok(TokenKind.LessEquals, "<=", start); }
                    return Tok(TokenKind.Less, "<", start);
                case '>':
                    if (twoChar == '=') { Advance(); return Tok(TokenKind.GreaterEquals, ">=", start); }
                    return Tok(TokenKind.Greater, ">", start);
                case '&':
                    if (twoChar == '&') { Advance(); return Tok(TokenKind.And, "&&", start); }
                    break;
                case '|':
                    if (twoChar == '|') { Advance(); return Tok(TokenKind.Or, "||", start); }
                    break;
                case '.':
                    if (twoChar == '.') { Advance(); return Tok(TokenKind.DotDot, "..", start); }
                    return Tok(TokenKind.Dot, ".", start);
                case '(': return Tok(TokenKind.LeftParen, "(", start);
                case ')': return Tok(TokenKind.RightParen, ")", start);
                case '{': return Tok(TokenKind.LeftBrace, "{", start);
                case '}': return Tok(TokenKind.RightBrace, "}", start);
                case '[': return Tok(TokenKind.LeftBracket, "[", start);
                case ']': return Tok(TokenKind.RightBracket, "]", start);
                case ',': return Tok(TokenKind.Comma, ",", start);
                case ':': return Tok(TokenKind.Colon, ":", start);
            }

            // Unexpected character
            _diagnostics.ReportError("E0003", $"Unexpected character '{c}'",
                new SourceSpan(_file, start, CurrentPos()));
            return new Token(TokenKind.Error, c.ToString(),
                new SourceSpan(_file, start, CurrentPos()));
        }

        private Token Tok(TokenKind kind, string text, SourcePos start) =>
            new Token(kind, text, new SourceSpan(_file, start, CurrentPos()));
    }
}
