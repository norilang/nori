using System.Collections.Generic;
using Nori.Compiler;

namespace Nori.Lsp.Utilities
{
    /// <summary>
    /// Walks the AST to find nodes at a given position (1-based line/column).
    /// </summary>
    public static class AstNodeFinder
    {
        /// <summary>Find the deepest AST node whose span contains the position.</summary>
        public static AstNode FindDeepestNode(ModuleDecl module, int line, int col)
        {
            AstNode best = null;

            foreach (var decl in module.Declarations)
            {
                var found = FindInDecl(decl, line, col);
                if (found != null) best = found;
            }

            return best ?? module;
        }

        /// <summary>Find the expression at the given cursor position.</summary>
        public static Expr FindExpressionAt(ModuleDecl module, int line, int col)
        {
            var node = FindDeepestNode(module, line, col);
            return node as Expr;
        }

        /// <summary>
        /// Find the enclosing CallExpr and determine which parameter index the cursor is at.
        /// Returns null if cursor is not inside a call.
        /// </summary>
        public static (CallExpr call, int paramIndex)? FindCallContext(ModuleDecl module, int line, int col)
        {
            CallExpr bestCall = null;
            int bestParamIndex = 0;

            foreach (var decl in module.Declarations)
                FindCallInDecl(decl, line, col, ref bestCall, ref bestParamIndex);

            if (bestCall != null)
                return (bestCall, bestParamIndex);
            return null;
        }

        private static AstNode FindInDecl(Decl decl, int line, int col)
        {
            if (!decl.Span.Contains(line, col)) return null;

            AstNode best = decl;

            switch (decl)
            {
                case VarDecl v:
                    if (v.Initializer != null)
                    {
                        var found = FindInExpr(v.Initializer, line, col);
                        if (found != null) best = found;
                    }
                    break;

                case EventHandlerDecl e:
                    foreach (var param in e.Parameters)
                        if (param.Span.Contains(line, col)) best = param;
                    var bodyResult = FindInBlock(e.Body, line, col);
                    if (bodyResult != null) best = bodyResult;
                    break;

                case CustomEventDecl ce:
                    bodyResult = FindInBlock(ce.Body, line, col);
                    if (bodyResult != null) best = bodyResult;
                    break;

                case FunctionDecl f:
                    foreach (var param in f.Parameters)
                        if (param.Span.Contains(line, col)) best = param;
                    bodyResult = FindInBlock(f.Body, line, col);
                    if (bodyResult != null) best = bodyResult;
                    break;
            }

            return best;
        }

        private static AstNode FindInBlock(List<Stmt> stmts, int line, int col)
        {
            if (stmts == null) return null;
            foreach (var stmt in stmts)
            {
                var found = FindInStmt(stmt, line, col);
                if (found != null) return found;
            }
            return null;
        }

        private static AstNode FindInStmt(Stmt stmt, int line, int col)
        {
            if (!stmt.Span.Contains(line, col)) return null;

            AstNode best = stmt;

            switch (stmt)
            {
                case LocalVarStmt lv:
                    if (lv.Initializer != null)
                    {
                        var found = FindInExpr(lv.Initializer, line, col);
                        if (found != null) best = found;
                    }
                    break;

                case AssignStmt assign:
                    var target = FindInExpr(assign.Target, line, col);
                    if (target != null) best = target;
                    var val = FindInExpr(assign.Value, line, col);
                    if (val != null) best = val;
                    break;

                case IfStmt ifStmt:
                    var cond = FindInExpr(ifStmt.Condition, line, col);
                    if (cond != null) best = cond;
                    var then = FindInBlock(ifStmt.ThenBody, line, col);
                    if (then != null) best = then;
                    if (ifStmt.ElseBody != null)
                    {
                        var els = FindInBlock(ifStmt.ElseBody, line, col);
                        if (els != null) best = els;
                    }
                    break;

                case WhileStmt whileStmt:
                    cond = FindInExpr(whileStmt.Condition, line, col);
                    if (cond != null) best = cond;
                    var body = FindInBlock(whileStmt.Body, line, col);
                    if (body != null) best = body;
                    break;

                case ForRangeStmt forRange:
                    var start = FindInExpr(forRange.Start, line, col);
                    if (start != null) best = start;
                    var end = FindInExpr(forRange.End, line, col);
                    if (end != null) best = end;
                    body = FindInBlock(forRange.Body, line, col);
                    if (body != null) best = body;
                    break;

                case ForEachStmt forEach:
                    var collection = FindInExpr(forEach.Collection, line, col);
                    if (collection != null) best = collection;
                    body = FindInBlock(forEach.Body, line, col);
                    if (body != null) best = body;
                    break;

                case ReturnStmt ret:
                    if (ret.Value != null)
                    {
                        var retVal = FindInExpr(ret.Value, line, col);
                        if (retVal != null) best = retVal;
                    }
                    break;

                case ExpressionStmt exprStmt:
                    var expr = FindInExpr(exprStmt.Expression, line, col);
                    if (expr != null) best = expr;
                    break;
            }

            return best;
        }

        private static AstNode FindInExpr(Expr expr, int line, int col)
        {
            if (expr == null || !expr.Span.Contains(line, col)) return null;

            AstNode best = expr;

            switch (expr)
            {
                case BinaryExpr binary:
                    var left = FindInExpr(binary.Left, line, col);
                    if (left != null) best = left;
                    var right = FindInExpr(binary.Right, line, col);
                    if (right != null) best = right;
                    break;

                case UnaryExpr unary:
                    var operand = FindInExpr(unary.Operand, line, col);
                    if (operand != null) best = operand;
                    break;

                case MemberExpr member:
                    var obj = FindInExpr(member.Object, line, col);
                    if (obj != null) best = obj;
                    break;

                case CallExpr call:
                    var callee = FindInExpr(call.Callee, line, col);
                    if (callee != null) best = callee;
                    foreach (var arg in call.Arguments)
                    {
                        var argFound = FindInExpr(arg, line, col);
                        if (argFound != null) best = argFound;
                    }
                    break;

                case IndexExpr index:
                    obj = FindInExpr(index.Object, line, col);
                    if (obj != null) best = obj;
                    var idx = FindInExpr(index.Index, line, col);
                    if (idx != null) best = idx;
                    break;

                case InterpolatedStringExpr interp:
                    foreach (var part in interp.Parts)
                    {
                        var partFound = FindInExpr(part, line, col);
                        if (partFound != null) best = partFound;
                    }
                    break;

                case ArrayLiteralExpr arr:
                    foreach (var elem in arr.Elements)
                    {
                        var elemFound = FindInExpr(elem, line, col);
                        if (elemFound != null) best = elemFound;
                    }
                    break;
            }

            return best;
        }

        private static void FindCallInDecl(Decl decl, int line, int col,
            ref CallExpr bestCall, ref int bestParamIndex)
        {
            if (!decl.Span.Contains(line, col)) return;

            switch (decl)
            {
                case VarDecl v:
                    if (v.Initializer != null) FindCallInExpr(v.Initializer, line, col, ref bestCall, ref bestParamIndex);
                    break;
                case EventHandlerDecl e:
                    FindCallInBlock(e.Body, line, col, ref bestCall, ref bestParamIndex);
                    break;
                case CustomEventDecl ce:
                    FindCallInBlock(ce.Body, line, col, ref bestCall, ref bestParamIndex);
                    break;
                case FunctionDecl f:
                    FindCallInBlock(f.Body, line, col, ref bestCall, ref bestParamIndex);
                    break;
            }
        }

        private static void FindCallInBlock(List<Stmt> stmts, int line, int col,
            ref CallExpr bestCall, ref int bestParamIndex)
        {
            if (stmts == null) return;
            foreach (var stmt in stmts)
                FindCallInStmt(stmt, line, col, ref bestCall, ref bestParamIndex);
        }

        private static void FindCallInStmt(Stmt stmt, int line, int col,
            ref CallExpr bestCall, ref int bestParamIndex)
        {
            if (!stmt.Span.Contains(line, col)) return;

            switch (stmt)
            {
                case ExpressionStmt es:
                    FindCallInExpr(es.Expression, line, col, ref bestCall, ref bestParamIndex);
                    break;
                case LocalVarStmt lv:
                    if (lv.Initializer != null) FindCallInExpr(lv.Initializer, line, col, ref bestCall, ref bestParamIndex);
                    break;
                case AssignStmt a:
                    FindCallInExpr(a.Target, line, col, ref bestCall, ref bestParamIndex);
                    FindCallInExpr(a.Value, line, col, ref bestCall, ref bestParamIndex);
                    break;
                case IfStmt ifs:
                    FindCallInExpr(ifs.Condition, line, col, ref bestCall, ref bestParamIndex);
                    FindCallInBlock(ifs.ThenBody, line, col, ref bestCall, ref bestParamIndex);
                    if (ifs.ElseBody != null) FindCallInBlock(ifs.ElseBody, line, col, ref bestCall, ref bestParamIndex);
                    break;
                case WhileStmt ws:
                    FindCallInExpr(ws.Condition, line, col, ref bestCall, ref bestParamIndex);
                    FindCallInBlock(ws.Body, line, col, ref bestCall, ref bestParamIndex);
                    break;
                case ForRangeStmt fr:
                    FindCallInExpr(fr.Start, line, col, ref bestCall, ref bestParamIndex);
                    FindCallInExpr(fr.End, line, col, ref bestCall, ref bestParamIndex);
                    FindCallInBlock(fr.Body, line, col, ref bestCall, ref bestParamIndex);
                    break;
                case ForEachStmt fe:
                    FindCallInExpr(fe.Collection, line, col, ref bestCall, ref bestParamIndex);
                    FindCallInBlock(fe.Body, line, col, ref bestCall, ref bestParamIndex);
                    break;
                case ReturnStmt r:
                    if (r.Value != null) FindCallInExpr(r.Value, line, col, ref bestCall, ref bestParamIndex);
                    break;
            }
        }

        private static void FindCallInExpr(Expr expr, int line, int col,
            ref CallExpr bestCall, ref int bestParamIndex)
        {
            if (expr == null || !expr.Span.Contains(line, col)) return;

            if (expr is CallExpr call)
            {
                // Determine which parameter index cursor is at by counting args before position
                int paramIdx = 0;
                for (int i = 0; i < call.Arguments.Count; i++)
                {
                    var arg = call.Arguments[i];
                    if (arg.Span.Start.Line > line ||
                        (arg.Span.Start.Line == line && arg.Span.Start.Column > col))
                        break;
                    paramIdx = i;
                }
                bestCall = call;
                bestParamIndex = paramIdx;
            }

            switch (expr)
            {
                case BinaryExpr b:
                    FindCallInExpr(b.Left, line, col, ref bestCall, ref bestParamIndex);
                    FindCallInExpr(b.Right, line, col, ref bestCall, ref bestParamIndex);
                    break;
                case UnaryExpr u:
                    FindCallInExpr(u.Operand, line, col, ref bestCall, ref bestParamIndex);
                    break;
                case MemberExpr m:
                    FindCallInExpr(m.Object, line, col, ref bestCall, ref bestParamIndex);
                    break;
                case CallExpr c:
                    FindCallInExpr(c.Callee, line, col, ref bestCall, ref bestParamIndex);
                    foreach (var arg in c.Arguments)
                        FindCallInExpr(arg, line, col, ref bestCall, ref bestParamIndex);
                    break;
                case IndexExpr ix:
                    FindCallInExpr(ix.Object, line, col, ref bestCall, ref bestParamIndex);
                    FindCallInExpr(ix.Index, line, col, ref bestCall, ref bestParamIndex);
                    break;
            }
        }
    }
}
