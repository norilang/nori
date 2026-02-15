using System.Linq;
using NUnit.Framework;
using Nori.Compiler;

namespace Nori.Tests
{
    [TestFixture]
    public class LexerTests
    {
        private (System.Collections.Generic.List<Token> tokens, DiagnosticBag diagnostics) Lex(string source)
        {
            var diagnostics = new DiagnosticBag();
            var lexer = new Lexer(source, "test.nori", diagnostics);
            var tokens = lexer.Tokenize();
            return (tokens, diagnostics);
        }

        [Test]
        public void Keywords_Tokenize_Correctly()
        {
            var (tokens, _) = Lex("let pub sync fn on event return send to if else while for in break continue");

            var kinds = tokens.Select(t => t.Kind).TakeWhile(k => k != TokenKind.Eof).ToArray();
            Assert.AreEqual(new[]
            {
                TokenKind.Let, TokenKind.Pub, TokenKind.Sync, TokenKind.Fn,
                TokenKind.On, TokenKind.Event, TokenKind.Return, TokenKind.Send,
                TokenKind.To, TokenKind.If, TokenKind.Else, TokenKind.While,
                TokenKind.For, TokenKind.In, TokenKind.Break, TokenKind.Continue,
            }, kinds);
        }

        [Test]
        public void Contextual_Keywords_Are_Identifiers()
        {
            var (tokens, _) = Lex("none linear smooth All Owner");
            var kinds = tokens.Select(t => t.Kind).TakeWhile(k => k != TokenKind.Eof).ToArray();
            Assert.That(kinds, Is.All.EqualTo(TokenKind.Identifier));
        }

        [Test]
        public void Operators_Tokenize_Correctly()
        {
            var (tokens, _) = Lex("+ - * / % = == != < <= > >= && || ! += -= *= /= .. -> .");

            var kinds = tokens.Select(t => t.Kind).TakeWhile(k => k != TokenKind.Eof).ToArray();
            Assert.AreEqual(new[]
            {
                TokenKind.Plus, TokenKind.Minus, TokenKind.Star, TokenKind.Slash,
                TokenKind.Percent, TokenKind.Equals, TokenKind.EqualsEquals,
                TokenKind.BangEquals, TokenKind.Less, TokenKind.LessEquals,
                TokenKind.Greater, TokenKind.GreaterEquals, TokenKind.And,
                TokenKind.Or, TokenKind.Bang, TokenKind.PlusEquals,
                TokenKind.MinusEquals, TokenKind.StarEquals, TokenKind.SlashEquals,
                TokenKind.DotDot, TokenKind.Arrow, TokenKind.Dot,
            }, kinds);
        }

        [Test]
        public void IntLiteral_Tokenizes()
        {
            var (tokens, _) = Lex("42");
            Assert.AreEqual(TokenKind.IntLiteral, tokens[0].Kind);
            Assert.AreEqual(42, tokens[0].LiteralValue);
        }

        [Test]
        public void FloatLiteral_Tokenizes()
        {
            var (tokens, _) = Lex("3.14");
            Assert.AreEqual(TokenKind.FloatLiteral, tokens[0].Kind);
            Assert.AreEqual(3.14f, (float)tokens[0].LiteralValue, 0.001f);
        }

        [Test]
        public void Range_Operator_With_Numbers()
        {
            // 0..10 must lex as IntLiteral(0), DotDot, IntLiteral(10)
            var (tokens, _) = Lex("0..10");
            Assert.AreEqual(TokenKind.IntLiteral, tokens[0].Kind);
            Assert.AreEqual(0, tokens[0].LiteralValue);
            Assert.AreEqual(TokenKind.DotDot, tokens[1].Kind);
            Assert.AreEqual(TokenKind.IntLiteral, tokens[2].Kind);
            Assert.AreEqual(10, tokens[2].LiteralValue);
        }

        [Test]
        public void String_Literal_Tokenizes()
        {
            var (tokens, _) = Lex("\"hello world\"");
            Assert.AreEqual(TokenKind.StringLiteral, tokens[0].Kind);
            Assert.AreEqual("hello world", tokens[0].LiteralValue);
        }

        [Test]
        public void String_Escape_Sequences()
        {
            var (tokens, _) = Lex("\"a\\nb\\tc\\\\d\\\"e\\{f\\}\"");
            Assert.AreEqual(TokenKind.StringLiteral, tokens[0].Kind);
            string val = (string)tokens[0].LiteralValue;
            Assert.That(val, Does.Contain("\n"));
            Assert.That(val, Does.Contain("\t"));
            Assert.That(val, Does.Contain("\\"));
            Assert.That(val, Does.Contain("\""));
            Assert.That(val, Does.Contain("{"));
            Assert.That(val, Does.Contain("}"));
        }

        [Test]
        public void String_Interpolation_Preserved()
        {
            var (tokens, _) = Lex("\"Score: {score}\"");
            Assert.AreEqual(TokenKind.StringLiteral, tokens[0].Kind);
            string val = (string)tokens[0].LiteralValue;
            Assert.That(val, Does.Contain("{score}"));
        }

        [Test]
        public void Unterminated_String_Reports_E0001()
        {
            var (tokens, diagnostics) = Lex("\"hello");
            Assert.IsTrue(diagnostics.HasErrors);
            Assert.AreEqual("E0001", diagnostics.All[0].Code);
            // Lexer continues after error
            Assert.That(tokens.Any(t => t.Kind == TokenKind.Eof));
        }

        [Test]
        public void Line_Comment_Skipped()
        {
            var (tokens, _) = Lex("a // comment\nb");
            var kinds = tokens.Select(t => t.Kind).TakeWhile(k => k != TokenKind.Eof).ToArray();
            Assert.AreEqual(new[] { TokenKind.Identifier, TokenKind.Identifier }, kinds);
        }

        [Test]
        public void Nested_Block_Comments()
        {
            var (tokens, diagnostics) = Lex("a /* outer /* inner */ still outer */ b");
            Assert.IsFalse(diagnostics.HasErrors);
            var kinds = tokens.Select(t => t.Kind).TakeWhile(k => k != TokenKind.Eof).ToArray();
            Assert.AreEqual(new[] { TokenKind.Identifier, TokenKind.Identifier }, kinds);
        }

        [Test]
        public void Unterminated_Block_Comment_Reports_E0002()
        {
            var (tokens, diagnostics) = Lex("/* unterminated");
            Assert.IsTrue(diagnostics.HasErrors);
            Assert.AreEqual("E0002", diagnostics.All[0].Code);
        }

        [Test]
        public void Unexpected_Character_Reports_Error_And_Continues()
        {
            var (tokens, diagnostics) = Lex("a @ b");
            Assert.IsTrue(diagnostics.HasErrors);
            // Should still have tokens for 'a' and 'b'
            var identifiers = tokens.Where(t => t.Kind == TokenKind.Identifier).ToArray();
            Assert.AreEqual(2, identifiers.Length);
        }

        [Test]
        public void Bool_Literals()
        {
            var (tokens, _) = Lex("true false");
            Assert.AreEqual(TokenKind.True, tokens[0].Kind);
            Assert.AreEqual(TokenKind.False, tokens[1].Kind);
        }

        [Test]
        public void Null_Literal()
        {
            var (tokens, _) = Lex("null");
            Assert.AreEqual(TokenKind.Null, tokens[0].Kind);
        }

        [Test]
        public void Delimiters_Tokenize()
        {
            var (tokens, _) = Lex("( ) { } [ ] , :");
            var kinds = tokens.Select(t => t.Kind).TakeWhile(k => k != TokenKind.Eof).ToArray();
            Assert.AreEqual(new[]
            {
                TokenKind.LeftParen, TokenKind.RightParen,
                TokenKind.LeftBrace, TokenKind.RightBrace,
                TokenKind.LeftBracket, TokenKind.RightBracket,
                TokenKind.Comma, TokenKind.Colon,
            }, kinds);
        }

        [Test]
        public void Line_And_Column_Tracking()
        {
            var (tokens, _) = Lex("a\nb\nc");
            Assert.AreEqual(1, tokens[0].Span.Start.Line);
            Assert.AreEqual(2, tokens[1].Span.Start.Line);
            Assert.AreEqual(3, tokens[2].Span.Start.Line);
        }

        [Test]
        public void Empty_Source_Gives_Eof()
        {
            var (tokens, diagnostics) = Lex("");
            Assert.IsFalse(diagnostics.HasErrors);
            Assert.AreEqual(1, tokens.Count);
            Assert.AreEqual(TokenKind.Eof, tokens[0].Kind);
        }
    }
}
