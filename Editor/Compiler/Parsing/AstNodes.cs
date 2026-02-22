using System.Collections.Generic;

namespace Nori.Compiler
{
    public enum SyncMode
    {
        NotSynced,
        None,
        Linear,
        Smooth,
    }

    public enum AssignOp
    {
        Assign,     // =
        AddAssign,  // +=
        SubAssign,  // -=
        MulAssign,  // *=
        DivAssign,  // /=
    }

    // --- Base types ---

    public abstract class AstNode
    {
        public SourceSpan Span { get; set; }
        protected AstNode(SourceSpan span) { Span = span; }
    }

    public abstract class Decl : AstNode
    {
        protected Decl(SourceSpan span) : base(span) { }
    }

    public abstract class Stmt : AstNode
    {
        protected Stmt(SourceSpan span) : base(span) { }
    }

    public abstract class Expr : AstNode
    {
        public string ResolvedType { get; set; } // Udon type, set during semantic analysis
        protected Expr(SourceSpan span) : base(span) { }
    }

    // --- Module ---

    public class ModuleDecl : AstNode
    {
        public string FilePath { get; }
        public List<Decl> Declarations { get; }

        public ModuleDecl(string filePath, List<Decl> declarations, SourceSpan span)
            : base(span)
        {
            FilePath = filePath;
            Declarations = declarations;
        }
    }

    // --- Declarations ---

    public class VarDecl : Decl
    {
        public string Name { get; }
        public string TypeName { get; }
        public bool IsArray { get; }
        public bool IsPublic { get; }
        public SyncMode SyncMode { get; }
        public Expr Initializer { get; }

        public VarDecl(string name, string typeName, bool isArray, bool isPublic,
            SyncMode syncMode, Expr initializer, SourceSpan span)
            : base(span)
        {
            Name = name;
            TypeName = typeName;
            IsArray = isArray;
            IsPublic = isPublic;
            SyncMode = syncMode;
            Initializer = initializer;
        }
    }

    public class ParamDecl : AstNode
    {
        public string Name { get; }
        public string TypeName { get; }

        public ParamDecl(string name, string typeName, SourceSpan span)
            : base(span)
        {
            Name = name;
            TypeName = typeName;
        }
    }

    public class EventHandlerDecl : Decl
    {
        public string EventName { get; }
        public List<ParamDecl> Parameters { get; }
        public List<Stmt> Body { get; }

        public EventHandlerDecl(string eventName, List<ParamDecl> parameters,
            List<Stmt> body, SourceSpan span)
            : base(span)
        {
            EventName = eventName;
            Parameters = parameters;
            Body = body;
        }
    }

    public class CustomEventDecl : Decl
    {
        public string Name { get; }
        public List<Stmt> Body { get; }

        public CustomEventDecl(string name, List<Stmt> body, SourceSpan span)
            : base(span)
        {
            Name = name;
            Body = body;
        }
    }

    public class FunctionDecl : Decl
    {
        public string Name { get; }
        public List<ParamDecl> Parameters { get; }
        public string ReturnTypeName { get; } // null if void
        public List<Stmt> Body { get; }

        public FunctionDecl(string name, List<ParamDecl> parameters,
            string returnTypeName, List<Stmt> body, SourceSpan span)
            : base(span)
        {
            Name = name;
            Parameters = parameters;
            ReturnTypeName = returnTypeName;
            Body = body;
        }
    }

    // --- Statements ---

    public class LocalVarStmt : Stmt
    {
        public string Name { get; }
        public string TypeName { get; }
        public bool IsArray { get; }
        public Expr Initializer { get; }

        public LocalVarStmt(string name, string typeName, bool isArray,
            Expr initializer, SourceSpan span)
            : base(span)
        {
            Name = name;
            TypeName = typeName;
            IsArray = isArray;
            Initializer = initializer;
        }
    }

    public class AssignStmt : Stmt
    {
        public Expr Target { get; }
        public AssignOp Op { get; }
        public Expr Value { get; }
        public OperatorInfo ResolvedOperator { get; set; } // set during semantic analysis for compound ops

        public AssignStmt(Expr target, AssignOp op, Expr value, SourceSpan span)
            : base(span)
        {
            Target = target;
            Op = op;
            Value = value;
        }
    }

    public class IfStmt : Stmt
    {
        public Expr Condition { get; }
        public List<Stmt> ThenBody { get; }
        public List<Stmt> ElseBody { get; } // null if no else

        public IfStmt(Expr condition, List<Stmt> thenBody, List<Stmt> elseBody, SourceSpan span)
            : base(span)
        {
            Condition = condition;
            ThenBody = thenBody;
            ElseBody = elseBody;
        }
    }

    public class WhileStmt : Stmt
    {
        public Expr Condition { get; }
        public List<Stmt> Body { get; }

        public WhileStmt(Expr condition, List<Stmt> body, SourceSpan span)
            : base(span)
        {
            Condition = condition;
            Body = body;
        }
    }

    public class ForRangeStmt : Stmt
    {
        public string VarName { get; }
        public Expr Start { get; }
        public Expr End { get; }
        public List<Stmt> Body { get; }

        public ForRangeStmt(string varName, Expr start, Expr end,
            List<Stmt> body, SourceSpan span)
            : base(span)
        {
            VarName = varName;
            Start = start;
            End = end;
            Body = body;
        }
    }

    public class ForEachStmt : Stmt
    {
        public string VarName { get; }
        public Expr Collection { get; }
        public List<Stmt> Body { get; }

        public ForEachStmt(string varName, Expr collection,
            List<Stmt> body, SourceSpan span)
            : base(span)
        {
            VarName = varName;
            Collection = collection;
            Body = body;
        }
    }

    public class ReturnStmt : Stmt
    {
        public Expr Value { get; } // null for void return

        public ReturnStmt(Expr value, SourceSpan span) : base(span)
        {
            Value = value;
        }
    }

    public class BreakStmt : Stmt
    {
        public BreakStmt(SourceSpan span) : base(span) { }
    }

    public class ContinueStmt : Stmt
    {
        public ContinueStmt(SourceSpan span) : base(span) { }
    }

    public class SendStmt : Stmt
    {
        public string EventName { get; }
        public string Target { get; } // null, "All", or "Owner"

        public SendStmt(string eventName, string target, SourceSpan span)
            : base(span)
        {
            EventName = eventName;
            Target = target;
        }
    }

    public class ExpressionStmt : Stmt
    {
        public Expr Expression { get; }

        public ExpressionStmt(Expr expression, SourceSpan span) : base(span)
        {
            Expression = expression;
        }
    }

    // --- Expressions ---

    public class IntLiteralExpr : Expr
    {
        public int Value { get; }
        public IntLiteralExpr(int value, SourceSpan span) : base(span)
        {
            Value = value;
            ResolvedType = "SystemInt32";
        }
    }

    public class FloatLiteralExpr : Expr
    {
        public float Value { get; }
        public FloatLiteralExpr(float value, SourceSpan span) : base(span)
        {
            Value = value;
            ResolvedType = "SystemSingle";
        }
    }

    public class BoolLiteralExpr : Expr
    {
        public bool Value { get; }
        public BoolLiteralExpr(bool value, SourceSpan span) : base(span)
        {
            Value = value;
            ResolvedType = "SystemBoolean";
        }
    }

    public class StringLiteralExpr : Expr
    {
        public string Value { get; }
        public StringLiteralExpr(string value, SourceSpan span) : base(span)
        {
            Value = value;
            ResolvedType = "SystemString";
        }
    }

    public class NullLiteralExpr : Expr
    {
        public NullLiteralExpr(SourceSpan span) : base(span)
        {
            ResolvedType = "SystemObject";
        }
    }

    public class InterpolatedStringExpr : Expr
    {
        public List<Expr> Parts { get; } // alternating StringLiteralExpr and expression parts

        public InterpolatedStringExpr(List<Expr> parts, SourceSpan span) : base(span)
        {
            Parts = parts;
            ResolvedType = "SystemString";
        }
    }

    public class NameExpr : Expr
    {
        public string Name { get; }
        public Symbol ResolvedSymbol { get; set; } // set during semantic analysis

        public NameExpr(string name, SourceSpan span) : base(span)
        {
            Name = name;
        }
    }

    public class BinaryExpr : Expr
    {
        public Expr Left { get; }
        public TokenKind Op { get; }
        public Expr Right { get; }
        public string ResolvedExtern { get; set; } // set during semantic analysis
        public ImplicitConversion LeftConversion { get; set; }  // operand widening
        public ImplicitConversion RightConversion { get; set; } // operand widening

        public BinaryExpr(Expr left, TokenKind op, Expr right, SourceSpan span) : base(span)
        {
            Left = left;
            Op = op;
            Right = right;
        }
    }

    public class UnaryExpr : Expr
    {
        public TokenKind Op { get; }
        public Expr Operand { get; }
        public string ResolvedExtern { get; set; }

        public UnaryExpr(TokenKind op, Expr operand, SourceSpan span) : base(span)
        {
            Op = op;
            Operand = operand;
        }
    }

    public class MemberExpr : Expr
    {
        public Expr Object { get; }
        public string MemberName { get; }
        public ExternSignature ResolvedGetter { get; set; }
        public ExternSignature ResolvedSetter { get; set; }
        public bool IsEnumValue { get; set; }
        public int EnumIntValue { get; set; }
        public string EnumUdonType { get; set; }

        public MemberExpr(Expr obj, string memberName, SourceSpan span) : base(span)
        {
            Object = obj;
            MemberName = memberName;
        }
    }

    public class CallExpr : Expr
    {
        public Expr Callee { get; }
        public List<Expr> Arguments { get; }
        public ExternSignature ResolvedExtern { get; set; }
        public bool IsBuiltinCall { get; set; }
        public bool IsConstructorCall { get; set; }
        public string ResolvedFunctionName { get; set; }
        public List<ImplicitConversion> ImplicitConversions { get; set; } // per-arg conversions (null entries = no conversion)

        public CallExpr(Expr callee, List<Expr> arguments, SourceSpan span) : base(span)
        {
            Callee = callee;
            Arguments = arguments;
        }
    }

    public class IndexExpr : Expr
    {
        public Expr Object { get; }
        public Expr Index { get; }
        public bool IsArrayConstruction { get; set; } // Type[size] array creation

        public IndexExpr(Expr obj, Expr index, SourceSpan span) : base(span)
        {
            Object = obj;
            Index = index;
        }
    }

    public class ArrayLiteralExpr : Expr
    {
        public List<Expr> Elements { get; }

        public ArrayLiteralExpr(List<Expr> elements, SourceSpan span) : base(span)
        {
            Elements = elements;
        }
    }

    public class CastExpr : Expr
    {
        public Expr Operand { get; }
        public string TargetTypeName { get; }

        public CastExpr(Expr operand, string targetTypeName, SourceSpan span) : base(span)
        {
            Operand = operand;
            TargetTypeName = targetTypeName;
        }
    }
}
