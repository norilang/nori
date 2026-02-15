using System.Collections.Generic;
using System.Text;

namespace Nori.Compiler
{
    public class Parser
    {
        private readonly List<Token> _tokens;
        private readonly DiagnosticBag _diagnostics;
        private int _pos;

        public Parser(List<Token> tokens, DiagnosticBag diagnostics)
        {
            _tokens = tokens;
            _diagnostics = diagnostics;
            _pos = 0;
        }

        private Token Current => _pos < _tokens.Count ? _tokens[_pos] : _tokens[_tokens.Count - 1];
        private Token Peek() => Current;
        private Token PeekAt(int offset)
        {
            int idx = _pos + offset;
            return idx < _tokens.Count ? _tokens[idx] : _tokens[_tokens.Count - 1];
        }

        private bool IsAtEnd => Current.Kind == TokenKind.Eof;
        private bool Check(TokenKind kind) => Current.Kind == kind;

        private Token Advance()
        {
            var token = Current;
            if (!IsAtEnd) _pos++;
            return token;
        }

        private bool Match(TokenKind kind)
        {
            if (Check(kind))
            {
                Advance();
                return true;
            }
            return false;
        }

        private Token Expect(TokenKind kind, string expected)
        {
            if (Check(kind))
                return Advance();

            _diagnostics.ReportUnexpectedToken(expected, Current);
            return Current; // don't advance - let caller decide
        }

        // --- Entry point ---

        public ModuleDecl Parse()
        {
            var declarations = new List<Decl>();
            var startSpan = Current.Span;

            while (!IsAtEnd)
            {
                try
                {
                    var decl = ParseDeclaration();
                    if (decl != null)
                        declarations.Add(decl);
                }
                catch (ParseException)
                {
                    SyncToDeclaration();
                }
            }

            var endSpan = Current.Span;
            return new ModuleDecl(startSpan.File, declarations, startSpan.Merge(endSpan));
        }

        // --- Declarations ---

        private Decl ParseDeclaration()
        {
            switch (Current.Kind)
            {
                case TokenKind.Let:
                    return ParseVarDecl(false, SyncMode.NotSynced);

                case TokenKind.Pub:
                    return ParsePubDecl();

                case TokenKind.Sync:
                    return ParseSyncDecl();

                case TokenKind.On:
                    return ParseEventHandler();

                case TokenKind.Event:
                    return ParseCustomEvent();

                case TokenKind.Fn:
                    return ParseFunction();

                default:
                    // Check for common mistake: pub without let
                    if (Current.Kind == TokenKind.Identifier)
                    {
                        _diagnostics.ReportError("E0010",
                            $"Unexpected identifier '{Current.Text}' at top level",
                            Current.Span, null,
                            "Top-level declarations must start with: let, pub, sync, on, event, or fn");
                        throw new ParseException();
                    }
                    _diagnostics.ReportUnexpectedToken("declaration", Current);
                    throw new ParseException();
            }
        }

        private Decl ParsePubDecl()
        {
            var pubToken = Advance(); // consume 'pub'

            if (!Check(TokenKind.Let))
            {
                _diagnostics.ReportExpectedLetAfterPub(pubToken.Span.Merge(Current.Span));
                throw new ParseException();
            }

            return ParseVarDecl(true, SyncMode.NotSynced);
        }

        private Decl ParseSyncDecl()
        {
            var syncToken = Advance(); // consume 'sync'

            // Expect sync mode: none, linear, or smooth
            if (!Check(TokenKind.Identifier))
            {
                _diagnostics.ReportInvalidSyncMode(Current.Text, syncToken.Span.Merge(Current.Span));
                throw new ParseException();
            }

            var modeToken = Advance();
            SyncMode mode;
            switch (modeToken.Text)
            {
                case "none": mode = SyncMode.None; break;
                case "linear": mode = SyncMode.Linear; break;
                case "smooth": mode = SyncMode.Smooth; break;
                default:
                    _diagnostics.ReportInvalidSyncMode(modeToken.Text, modeToken.Span);
                    throw new ParseException();
            }

            return ParseVarDeclBody(false, mode, syncToken.Span);
        }

        private VarDecl ParseVarDecl(bool isPublic, SyncMode syncMode)
        {
            var letToken = Advance(); // consume 'let'
            return ParseVarDeclBody(isPublic, syncMode, letToken.Span);
        }

        private VarDecl ParseVarDeclBody(bool isPublic, SyncMode syncMode, SourceSpan startSpan)
        {
            var nameToken = Expect(TokenKind.Identifier, "variable name");
            if (nameToken.Kind != TokenKind.Identifier)
                throw new ParseException();
            string name = nameToken.Text;

            Expect(TokenKind.Colon, "':'");
            if (Current.Kind == TokenKind.Eof) throw new ParseException();

            // Parse type (including array types like int[])
            var typeToken = Expect(TokenKind.Identifier, "type name");
            if (typeToken.Kind != TokenKind.Identifier)
                throw new ParseException();
            string typeName = typeToken.Text;

            bool isArray = false;
            if (Check(TokenKind.LeftBracket) && PeekAt(1).Kind == TokenKind.RightBracket)
            {
                Advance(); // [
                Advance(); // ]
                isArray = true;
            }

            Expr initializer = null;
            if (Match(TokenKind.Equals))
            {
                initializer = ParseExpression();
            }

            return new VarDecl(name, typeName, isArray, isPublic, syncMode,
                initializer, startSpan.Merge(Current.Span));
        }

        private EventHandlerDecl ParseEventHandler()
        {
            var onToken = Advance(); // consume 'on'

            var nameToken = Expect(TokenKind.Identifier, "event name");
            if (nameToken.Kind != TokenKind.Identifier)
                throw new ParseException();
            string eventName = nameToken.Text;

            var parameters = new List<ParamDecl>();
            if (Match(TokenKind.LeftParen))
            {
                if (!Check(TokenKind.RightParen))
                {
                    do
                    {
                        parameters.Add(ParseParamDecl());
                    } while (Match(TokenKind.Comma));
                }
                Expect(TokenKind.RightParen, "')'");
            }

            var body = ParseBlock();
            return new EventHandlerDecl(eventName, parameters, body,
                onToken.Span.Merge(Current.Span));
        }

        private CustomEventDecl ParseCustomEvent()
        {
            var eventToken = Advance(); // consume 'event'

            var nameToken = Expect(TokenKind.Identifier, "event name");
            if (nameToken.Kind != TokenKind.Identifier)
                throw new ParseException();

            var body = ParseBlock();
            return new CustomEventDecl(nameToken.Text, body,
                eventToken.Span.Merge(Current.Span));
        }

        private FunctionDecl ParseFunction()
        {
            var fnToken = Advance(); // consume 'fn'

            var nameToken = Expect(TokenKind.Identifier, "function name");
            if (nameToken.Kind != TokenKind.Identifier)
                throw new ParseException();

            Expect(TokenKind.LeftParen, "'('");
            var parameters = new List<ParamDecl>();
            if (!Check(TokenKind.RightParen))
            {
                do
                {
                    parameters.Add(ParseParamDecl());
                } while (Match(TokenKind.Comma));
            }
            Expect(TokenKind.RightParen, "')'");

            string returnType = null;
            if (Match(TokenKind.Arrow))
            {
                var retTypeToken = Expect(TokenKind.Identifier, "return type");
                if (retTypeToken.Kind == TokenKind.Identifier)
                    returnType = retTypeToken.Text;
            }

            var body = ParseBlock();
            return new FunctionDecl(nameToken.Text, parameters, returnType, body,
                fnToken.Span.Merge(Current.Span));
        }

        private ParamDecl ParseParamDecl()
        {
            var nameToken = Expect(TokenKind.Identifier, "parameter name");
            if (nameToken.Kind != TokenKind.Identifier)
                throw new ParseException();

            Expect(TokenKind.Colon, "':'");

            var typeToken = Expect(TokenKind.Identifier, "parameter type");
            if (typeToken.Kind != TokenKind.Identifier)
                throw new ParseException();

            return new ParamDecl(nameToken.Text, typeToken.Text,
                nameToken.Span.Merge(typeToken.Span));
        }

        // --- Blocks & Statements ---

        private List<Stmt> ParseBlock()
        {
            Expect(TokenKind.LeftBrace, "'{'");
            var stmts = new List<Stmt>();

            while (!Check(TokenKind.RightBrace) && !IsAtEnd)
            {
                try
                {
                    stmts.Add(ParseStatement());
                }
                catch (ParseException)
                {
                    SyncToStatement();
                }
            }

            Expect(TokenKind.RightBrace, "'}'");
            return stmts;
        }

        private Stmt ParseStatement()
        {
            switch (Current.Kind)
            {
                case TokenKind.Let:
                    return ParseLocalVar();
                case TokenKind.If:
                    return ParseIf();
                case TokenKind.While:
                    return ParseWhile();
                case TokenKind.For:
                    return ParseFor();
                case TokenKind.Return:
                    return ParseReturn();
                case TokenKind.Break:
                    { var t = Advance(); return new BreakStmt(t.Span); }
                case TokenKind.Continue:
                    { var t = Advance(); return new ContinueStmt(t.Span); }
                case TokenKind.Send:
                    return ParseSend();
                default:
                    return ParseExpressionOrAssignment();
            }
        }

        private LocalVarStmt ParseLocalVar()
        {
            var letToken = Advance(); // consume 'let'

            var nameToken = Expect(TokenKind.Identifier, "variable name");
            if (nameToken.Kind != TokenKind.Identifier)
                throw new ParseException();

            Expect(TokenKind.Colon, "':'");

            var typeToken = Expect(TokenKind.Identifier, "type name");
            if (typeToken.Kind != TokenKind.Identifier)
                throw new ParseException();
            string typeName = typeToken.Text;

            bool isArray = false;
            if (Check(TokenKind.LeftBracket) && PeekAt(1).Kind == TokenKind.RightBracket)
            {
                Advance(); // [
                Advance(); // ]
                isArray = true;
            }

            Expr initializer = null;
            if (Match(TokenKind.Equals))
            {
                initializer = ParseExpression();
            }

            return new LocalVarStmt(nameToken.Text, typeName, isArray,
                initializer, letToken.Span.Merge(Current.Span));
        }

        private IfStmt ParseIf()
        {
            var ifToken = Advance(); // consume 'if'
            var condition = ParseExpression();
            var thenBody = ParseBlock();

            List<Stmt> elseBody = null;
            if (Match(TokenKind.Else))
            {
                if (Check(TokenKind.If))
                {
                    // else if -> wrap in list
                    elseBody = new List<Stmt> { ParseIf() };
                }
                else
                {
                    elseBody = ParseBlock();
                }
            }

            return new IfStmt(condition, thenBody, elseBody,
                ifToken.Span.Merge(Current.Span));
        }

        private WhileStmt ParseWhile()
        {
            var whileToken = Advance();
            var condition = ParseExpression();
            var body = ParseBlock();
            return new WhileStmt(condition, body, whileToken.Span.Merge(Current.Span));
        }

        private Stmt ParseFor()
        {
            var forToken = Advance(); // consume 'for'

            var nameToken = Expect(TokenKind.Identifier, "loop variable");
            if (nameToken.Kind != TokenKind.Identifier)
                throw new ParseException();

            Expect(TokenKind.In, "'in'");
            var startExpr = ParseExpression();

            if (Match(TokenKind.DotDot))
            {
                // for..in range
                var endExpr = ParseExpression();
                var body = ParseBlock();
                return new ForRangeStmt(nameToken.Text, startExpr, endExpr, body,
                    forToken.Span.Merge(Current.Span));
            }
            else
            {
                // for..in collection
                var body = ParseBlock();
                return new ForEachStmt(nameToken.Text, startExpr, body,
                    forToken.Span.Merge(Current.Span));
            }
        }

        private ReturnStmt ParseReturn()
        {
            var retToken = Advance();
            Expr value = null;

            // Return has a value if the next token is not '}' or a statement-starting keyword
            if (!Check(TokenKind.RightBrace) && !IsStatementStart(Current.Kind))
            {
                value = ParseExpression();
            }

            return new ReturnStmt(value, retToken.Span.Merge(Current.Span));
        }

        private SendStmt ParseSend()
        {
            var sendToken = Advance();

            var nameToken = Expect(TokenKind.Identifier, "event name");
            if (nameToken.Kind != TokenKind.Identifier)
                throw new ParseException();

            string target = null;
            if (Match(TokenKind.To))
            {
                var targetToken = Expect(TokenKind.Identifier, "'All' or 'Owner'");
                if (targetToken.Kind == TokenKind.Identifier)
                {
                    if (targetToken.Text != "All" && targetToken.Text != "Owner")
                    {
                        _diagnostics.ReportInvalidSendTarget(targetToken.Text, targetToken.Span);
                    }
                    target = targetToken.Text;
                }
            }

            return new SendStmt(nameToken.Text, target,
                sendToken.Span.Merge(Current.Span));
        }

        private Stmt ParseExpressionOrAssignment()
        {
            var expr = ParseExpression();

            // Check for assignment operators
            AssignOp? assignOp = null;
            switch (Current.Kind)
            {
                case TokenKind.Equals: assignOp = AssignOp.Assign; break;
                case TokenKind.PlusEquals: assignOp = AssignOp.AddAssign; break;
                case TokenKind.MinusEquals: assignOp = AssignOp.SubAssign; break;
                case TokenKind.StarEquals: assignOp = AssignOp.MulAssign; break;
                case TokenKind.SlashEquals: assignOp = AssignOp.DivAssign; break;
            }

            if (assignOp.HasValue)
            {
                Advance(); // consume the assignment operator
                var value = ParseExpression();
                return new AssignStmt(expr, assignOp.Value, value,
                    expr.Span.Merge(value.Span));
            }

            return new ExpressionStmt(expr, expr.Span);
        }

        // --- Expressions (precedence climbing) ---

        private Expr ParseExpression() => ParseOr();

        private Expr ParseOr()
        {
            var left = ParseAnd();
            while (Check(TokenKind.Or))
            {
                var op = Advance();
                var right = ParseAnd();
                left = new BinaryExpr(left, op.Kind, right, left.Span.Merge(right.Span));
            }
            return left;
        }

        private Expr ParseAnd()
        {
            var left = ParseEquality();
            while (Check(TokenKind.And))
            {
                var op = Advance();
                var right = ParseEquality();
                left = new BinaryExpr(left, op.Kind, right, left.Span.Merge(right.Span));
            }
            return left;
        }

        private Expr ParseEquality()
        {
            var left = ParseComparison();
            while (Check(TokenKind.EqualsEquals) || Check(TokenKind.BangEquals))
            {
                var op = Advance();
                var right = ParseComparison();
                left = new BinaryExpr(left, op.Kind, right, left.Span.Merge(right.Span));
            }
            return left;
        }

        private Expr ParseComparison()
        {
            var left = ParseAddition();
            while (Check(TokenKind.Less) || Check(TokenKind.LessEquals) ||
                   Check(TokenKind.Greater) || Check(TokenKind.GreaterEquals))
            {
                var op = Advance();
                var right = ParseAddition();
                left = new BinaryExpr(left, op.Kind, right, left.Span.Merge(right.Span));
            }
            return left;
        }

        private Expr ParseAddition()
        {
            var left = ParseMultiplication();
            while (Check(TokenKind.Plus) || Check(TokenKind.Minus))
            {
                var op = Advance();
                var right = ParseMultiplication();
                left = new BinaryExpr(left, op.Kind, right, left.Span.Merge(right.Span));
            }
            return left;
        }

        private Expr ParseMultiplication()
        {
            var left = ParseUnary();
            while (Check(TokenKind.Star) || Check(TokenKind.Slash) || Check(TokenKind.Percent))
            {
                var op = Advance();
                var right = ParseUnary();
                left = new BinaryExpr(left, op.Kind, right, left.Span.Merge(right.Span));
            }
            return left;
        }

        private Expr ParseUnary()
        {
            if (Check(TokenKind.Bang) || Check(TokenKind.Minus))
            {
                var op = Advance();
                var operand = ParseUnary();
                return new UnaryExpr(op.Kind, operand, op.Span.Merge(operand.Span));
            }
            return ParsePostfix();
        }

        private Expr ParsePostfix()
        {
            var expr = ParsePrimary();

            while (true)
            {
                if (Check(TokenKind.Dot))
                {
                    Advance(); // consume '.'
                    var memberToken = Expect(TokenKind.Identifier, "member name");
                    if (memberToken.Kind != TokenKind.Identifier)
                        throw new ParseException();
                    expr = new MemberExpr(expr, memberToken.Text,
                        expr.Span.Merge(memberToken.Span));
                }
                else if (Check(TokenKind.LeftParen))
                {
                    Advance(); // consume '('
                    var args = new List<Expr>();
                    if (!Check(TokenKind.RightParen))
                    {
                        do
                        {
                            args.Add(ParseExpression());
                        } while (Match(TokenKind.Comma));
                    }
                    var closeParen = Expect(TokenKind.RightParen, "')'");
                    expr = new CallExpr(expr, args, expr.Span.Merge(closeParen.Span));
                }
                else if (Check(TokenKind.LeftBracket))
                {
                    Advance(); // consume '['
                    var index = ParseExpression();
                    var closeBracket = Expect(TokenKind.RightBracket, "']'");
                    expr = new IndexExpr(expr, index, expr.Span.Merge(closeBracket.Span));
                }
                else
                {
                    break;
                }
            }

            return expr;
        }

        private Expr ParsePrimary()
        {
            switch (Current.Kind)
            {
                case TokenKind.IntLiteral:
                {
                    var t = Advance();
                    return new IntLiteralExpr((int)t.LiteralValue, t.Span);
                }
                case TokenKind.FloatLiteral:
                {
                    var t = Advance();
                    return new FloatLiteralExpr((float)t.LiteralValue, t.Span);
                }
                case TokenKind.StringLiteral:
                {
                    var t = Advance();
                    string val = (string)t.LiteralValue;
                    return ParseStringWithInterpolation(val, t.Span);
                }
                case TokenKind.True:
                {
                    var t = Advance();
                    return new BoolLiteralExpr(true, t.Span);
                }
                case TokenKind.False:
                {
                    var t = Advance();
                    return new BoolLiteralExpr(false, t.Span);
                }
                case TokenKind.Null:
                {
                    var t = Advance();
                    return new NullLiteralExpr(t.Span);
                }
                case TokenKind.Identifier:
                {
                    var t = Advance();
                    return new NameExpr(t.Text, t.Span);
                }
                case TokenKind.LeftParen:
                {
                    Advance();
                    var expr = ParseExpression();
                    Expect(TokenKind.RightParen, "')'");
                    return expr;
                }
                case TokenKind.LeftBracket:
                {
                    return ParseArrayLiteral();
                }
                default:
                    _diagnostics.ReportExpectedExpression(Current);
                    throw new ParseException();
            }
        }

        private Expr ParseStringWithInterpolation(string value, SourceSpan span)
        {
            // Check if string contains interpolation markers
            if (!value.Contains("{"))
                return new StringLiteralExpr(value, span);

            var parts = new List<Expr>();
            var sb = new StringBuilder();
            int i = 0;
            while (i < value.Length)
            {
                if (value[i] == '{')
                {
                    // Flush accumulated text
                    if (sb.Length > 0)
                    {
                        parts.Add(new StringLiteralExpr(sb.ToString(), span));
                        sb.Clear();
                    }

                    // Extract expression text between { and }
                    i++; // skip {
                    int braceDepth = 1;
                    var exprText = new StringBuilder();
                    while (i < value.Length && braceDepth > 0)
                    {
                        if (value[i] == '{') braceDepth++;
                        else if (value[i] == '}') { braceDepth--; if (braceDepth == 0) { i++; break; } }
                        exprText.Append(value[i]);
                        i++;
                    }

                    // Parse the interpolated expression
                    var interpDiagnostics = new DiagnosticBag();
                    var interpLexer = new Lexer(exprText.ToString(), span.File, interpDiagnostics);
                    var interpTokens = interpLexer.Tokenize();
                    var interpParser = new Parser(interpTokens, _diagnostics);
                    var interpExpr = interpParser.ParseExpression();
                    parts.Add(interpExpr);
                }
                else
                {
                    sb.Append(value[i]);
                    i++;
                }
            }

            // Flush remaining text
            if (sb.Length > 0)
                parts.Add(new StringLiteralExpr(sb.ToString(), span));

            if (parts.Count == 1 && parts[0] is StringLiteralExpr)
                return parts[0];

            return new InterpolatedStringExpr(parts, span);
        }

        private ArrayLiteralExpr ParseArrayLiteral()
        {
            var openBracket = Advance(); // consume '['
            var elements = new List<Expr>();

            if (!Check(TokenKind.RightBracket))
            {
                do
                {
                    elements.Add(ParseExpression());
                } while (Match(TokenKind.Comma));
            }

            var closeBracket = Expect(TokenKind.RightBracket, "']'");
            return new ArrayLiteralExpr(elements, openBracket.Span.Merge(closeBracket.Span));
        }

        // --- Error recovery ---

        private void SyncToDeclaration()
        {
            while (!IsAtEnd)
            {
                switch (Current.Kind)
                {
                    case TokenKind.On:
                    case TokenKind.Fn:
                    case TokenKind.Event:
                    case TokenKind.Let:
                    case TokenKind.Pub:
                    case TokenKind.Sync:
                        return;
                }
                Advance();
            }
        }

        private void SyncToStatement()
        {
            while (!IsAtEnd)
            {
                switch (Current.Kind)
                {
                    case TokenKind.Let:
                    case TokenKind.If:
                    case TokenKind.While:
                    case TokenKind.For:
                    case TokenKind.Return:
                    case TokenKind.Break:
                    case TokenKind.Continue:
                    case TokenKind.Send:
                    case TokenKind.RightBrace:
                        return;
                }
                Advance();
            }
        }

        private bool IsStatementStart(TokenKind kind)
        {
            switch (kind)
            {
                case TokenKind.Let:
                case TokenKind.If:
                case TokenKind.While:
                case TokenKind.For:
                case TokenKind.Return:
                case TokenKind.Break:
                case TokenKind.Continue:
                case TokenKind.Send:
                    return true;
                default:
                    return false;
            }
        }

        private class ParseException : System.Exception { }
    }
}
