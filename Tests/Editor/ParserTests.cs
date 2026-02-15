using System.Linq;
using NUnit.Framework;
using Nori.Compiler;

namespace Nori.Tests
{
    [TestFixture]
    public class ParserTests
    {
        private (ModuleDecl module, DiagnosticBag diagnostics) Parse(string source)
        {
            var diagnostics = new DiagnosticBag();
            var lexer = new Lexer(source, "test.nori", diagnostics);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens, diagnostics);
            var module = parser.Parse();
            return (module, diagnostics);
        }

        [Test]
        public void Parse_LetDecl()
        {
            var (module, diags) = Parse("let health: int = 100");
            Assert.IsFalse(diags.HasErrors);
            Assert.AreEqual(1, module.Declarations.Count);
            var v = module.Declarations[0] as VarDecl;
            Assert.IsNotNull(v);
            Assert.AreEqual("health", v.Name);
            Assert.AreEqual("int", v.TypeName);
            Assert.IsFalse(v.IsPublic);
            Assert.AreEqual(SyncMode.NotSynced, v.SyncMode);
        }

        [Test]
        public void Parse_PubLetDecl()
        {
            var (module, diags) = Parse("pub let speed: float = 5.0");
            Assert.IsFalse(diags.HasErrors);
            var v = module.Declarations[0] as VarDecl;
            Assert.IsNotNull(v);
            Assert.AreEqual("speed", v.Name);
            Assert.IsTrue(v.IsPublic);
        }

        [Test]
        public void Parse_SyncDecl_AllModes()
        {
            foreach (var mode in new[] { ("none", SyncMode.None), ("linear", SyncMode.Linear), ("smooth", SyncMode.Smooth) })
            {
                var (module, diags) = Parse($"sync {mode.Item1} x: int = 0");
                Assert.IsFalse(diags.HasErrors, $"Failed for sync mode: {mode.Item1}");
                var v = module.Declarations[0] as VarDecl;
                Assert.AreEqual(mode.Item2, v.SyncMode);
            }
        }

        [Test]
        public void Parse_EventHandler_NoParams()
        {
            var (module, diags) = Parse("on Start { }");
            Assert.IsFalse(diags.HasErrors);
            var e = module.Declarations[0] as EventHandlerDecl;
            Assert.IsNotNull(e);
            Assert.AreEqual("Start", e.EventName);
            Assert.AreEqual(0, e.Parameters.Count);
        }

        [Test]
        public void Parse_EventHandler_WithParams()
        {
            var (module, diags) = Parse("on PlayerJoined(player: Player) { }");
            Assert.IsFalse(diags.HasErrors);
            var e = module.Declarations[0] as EventHandlerDecl;
            Assert.AreEqual("PlayerJoined", e.EventName);
            Assert.AreEqual(1, e.Parameters.Count);
            Assert.AreEqual("player", e.Parameters[0].Name);
            Assert.AreEqual("Player", e.Parameters[0].TypeName);
        }

        [Test]
        public void Parse_CustomEvent()
        {
            var (module, diags) = Parse("event DoThing { }");
            Assert.IsFalse(diags.HasErrors);
            var ce = module.Declarations[0] as CustomEventDecl;
            Assert.IsNotNull(ce);
            Assert.AreEqual("DoThing", ce.Name);
        }

        [Test]
        public void Parse_Function_NoReturn()
        {
            var (module, diags) = Parse("fn greet(name: string) { }");
            Assert.IsFalse(diags.HasErrors);
            var f = module.Declarations[0] as FunctionDecl;
            Assert.IsNotNull(f);
            Assert.AreEqual("greet", f.Name);
            Assert.AreEqual(1, f.Parameters.Count);
            Assert.IsNull(f.ReturnTypeName);
        }

        [Test]
        public void Parse_Function_WithReturn()
        {
            var (module, diags) = Parse("fn add(a: int, b: int) -> int { return a }");
            Assert.IsFalse(diags.HasErrors);
            var f = module.Declarations[0] as FunctionDecl;
            Assert.AreEqual("add", f.Name);
            Assert.AreEqual(2, f.Parameters.Count);
            Assert.AreEqual("int", f.ReturnTypeName);
        }

        [Test]
        public void Parse_Operator_Precedence()
        {
            // a + b * c should parse as Add(a, Mul(b, c))
            var (module, diags) = Parse("on Start { let r: int = a + b * c }");
            Assert.IsFalse(diags.HasErrors);

            var handler = module.Declarations[0] as EventHandlerDecl;
            var localVar = handler.Body[0] as LocalVarStmt;
            var add = localVar.Initializer as BinaryExpr;
            Assert.IsNotNull(add);
            Assert.AreEqual(TokenKind.Plus, add.Op);

            var mul = add.Right as BinaryExpr;
            Assert.IsNotNull(mul);
            Assert.AreEqual(TokenKind.Star, mul.Op);
        }

        [Test]
        public void Parse_If_Else()
        {
            var (module, diags) = Parse("on Start { if x { } else { } }");
            Assert.IsFalse(diags.HasErrors);

            var handler = module.Declarations[0] as EventHandlerDecl;
            var ifStmt = handler.Body[0] as IfStmt;
            Assert.IsNotNull(ifStmt);
            Assert.IsNotNull(ifStmt.ElseBody);
        }

        [Test]
        public void Parse_While()
        {
            var (module, diags) = Parse("on Start { while true { break } }");
            Assert.IsFalse(diags.HasErrors);

            var handler = module.Declarations[0] as EventHandlerDecl;
            var whileStmt = handler.Body[0] as WhileStmt;
            Assert.IsNotNull(whileStmt);
        }

        [Test]
        public void Parse_ForRange()
        {
            var (module, diags) = Parse("on Start { for i in 0..10 { } }");
            Assert.IsFalse(diags.HasErrors);

            var handler = module.Declarations[0] as EventHandlerDecl;
            var forStmt = handler.Body[0] as ForRangeStmt;
            Assert.IsNotNull(forStmt);
            Assert.AreEqual("i", forStmt.VarName);
        }

        [Test]
        public void Parse_ForEach()
        {
            var (module, diags) = Parse("on Start { for item in items { } }");
            Assert.IsFalse(diags.HasErrors);

            var handler = module.Declarations[0] as EventHandlerDecl;
            var forStmt = handler.Body[0] as ForEachStmt;
            Assert.IsNotNull(forStmt);
            Assert.AreEqual("item", forStmt.VarName);
        }

        [Test]
        public void Parse_SendStmt()
        {
            var (module, diags) = Parse("on Start { send DoThing to All }");
            Assert.IsFalse(diags.HasErrors);

            var handler = module.Declarations[0] as EventHandlerDecl;
            var send = handler.Body[0] as SendStmt;
            Assert.IsNotNull(send);
            Assert.AreEqual("DoThing", send.EventName);
            Assert.AreEqual("All", send.Target);
        }

        [Test]
        public void Parse_SendLocal()
        {
            var (module, diags) = Parse("on Start { send DoThing }");
            Assert.IsFalse(diags.HasErrors);

            var handler = module.Declarations[0] as EventHandlerDecl;
            var send = handler.Body[0] as SendStmt;
            Assert.IsNull(send.Target);
        }

        [Test]
        public void Parse_MemberAccess()
        {
            var (module, diags) = Parse("on Start { transform.position }");
            Assert.IsFalse(diags.HasErrors);

            var handler = module.Declarations[0] as EventHandlerDecl;
            var exprStmt = handler.Body[0] as ExpressionStmt;
            var member = exprStmt.Expression as MemberExpr;
            Assert.IsNotNull(member);
            Assert.AreEqual("position", member.MemberName);
        }

        [Test]
        public void Parse_MethodCall()
        {
            var (module, diags) = Parse("on Start { log(\"hello\") }");
            Assert.IsFalse(diags.HasErrors);

            var handler = module.Declarations[0] as EventHandlerDecl;
            var exprStmt = handler.Body[0] as ExpressionStmt;
            var call = exprStmt.Expression as CallExpr;
            Assert.IsNotNull(call);
            Assert.AreEqual(1, call.Arguments.Count);
        }

        [Test]
        public void Parse_Assignment()
        {
            var (module, diags) = Parse("on Start { x = 5 }");
            Assert.IsFalse(diags.HasErrors);

            var handler = module.Declarations[0] as EventHandlerDecl;
            var assign = handler.Body[0] as AssignStmt;
            Assert.IsNotNull(assign);
            Assert.AreEqual(AssignOp.Assign, assign.Op);
        }

        [Test]
        public void Parse_CompoundAssignment()
        {
            var (module, diags) = Parse("on Start { x += 1 }");
            Assert.IsFalse(diags.HasErrors);

            var handler = module.Declarations[0] as EventHandlerDecl;
            var assign = handler.Body[0] as AssignStmt;
            Assert.AreEqual(AssignOp.AddAssign, assign.Op);
        }

        [Test]
        public void Parse_ArrayType()
        {
            var (module, diags) = Parse("let items: int[] = []");
            Assert.IsFalse(diags.HasErrors);
            var v = module.Declarations[0] as VarDecl;
            Assert.IsTrue(v.IsArray);
            Assert.AreEqual("int", v.TypeName);
        }

        [Test]
        public void Parse_UnaryNot()
        {
            var (module, diags) = Parse("on Start { let r: bool = !x }");
            Assert.IsFalse(diags.HasErrors);

            var handler = module.Declarations[0] as EventHandlerDecl;
            var localVar = handler.Body[0] as LocalVarStmt;
            var unary = localVar.Initializer as UnaryExpr;
            Assert.IsNotNull(unary);
            Assert.AreEqual(TokenKind.Bang, unary.Op);
        }

        [Test]
        public void Pub_Without_Let_Reports_E0011()
        {
            var (_, diags) = Parse("pub speed: float = 5.0");
            Assert.IsTrue(diags.HasErrors);
            Assert.AreEqual("E0011", diags.All[0].Code);
        }

        [Test]
        public void Invalid_SyncMode_Reports_E0012()
        {
            var (_, diags) = Parse("sync fast x: int = 0");
            Assert.IsTrue(diags.HasErrors);
            Assert.AreEqual("E0012", diags.All[0].Code);
        }

        [Test]
        public void Error_Recovery_Reports_Multiple_Errors()
        {
            var source = @"
pub speed
on Start { }
pub health
on Interact { }
";
            var (module, diags) = Parse(source);
            // Should have at least 2 errors (pub without let)
            Assert.IsTrue(diags.ErrorCount >= 2);
            // Should still parse the valid declarations
            var validDecls = module.Declarations.OfType<EventHandlerDecl>().ToArray();
            Assert.IsTrue(validDecls.Length >= 1);
        }

        [Test]
        public void Parse_StringInterpolation()
        {
            var (module, diags) = Parse("on Start { log(\"Score: {score}\") }");
            Assert.IsFalse(diags.HasErrors);
        }

        [Test]
        public void Parse_Return_With_Value()
        {
            var (module, diags) = Parse("fn add(a: int, b: int) -> int { return a }");
            Assert.IsFalse(diags.HasErrors);
            var f = module.Declarations[0] as FunctionDecl;
            var ret = f.Body[0] as ReturnStmt;
            Assert.IsNotNull(ret);
            Assert.IsNotNull(ret.Value);
        }

        [Test]
        public void Parse_Return_Void()
        {
            var (module, diags) = Parse("on Start { return }");
            Assert.IsFalse(diags.HasErrors);
            var handler = module.Declarations[0] as EventHandlerDecl;
            var ret = handler.Body[0] as ReturnStmt;
            Assert.IsNull(ret.Value);
        }
    }
}
