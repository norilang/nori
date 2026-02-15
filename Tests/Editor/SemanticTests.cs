using System.Linq;
using NUnit.Framework;
using Nori.Compiler;

namespace Nori.Tests
{
    [TestFixture]
    public class SemanticTests
    {
        private (ModuleDecl module, DiagnosticBag diagnostics) Analyze(string source)
        {
            var diagnostics = new DiagnosticBag();
            var lexer = new Lexer(source, "test.nori", diagnostics);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens, diagnostics);
            var module = parser.Parse();

            if (diagnostics.HasErrors)
                return (module, diagnostics);

            var analyzer = new SemanticAnalyzer(module, BuiltinCatalog.Instance, diagnostics);
            analyzer.Analyze();
            return (module, diagnostics);
        }

        [Test]
        public void Undefined_Variable_Reports_E0070()
        {
            var (_, diags) = Analyze(@"
let score: int = 0
on Start { let x: int = scroe }
");
            Assert.IsTrue(diags.HasErrors);
            var error = diags.All.First(d => d.Code == "E0070");
            Assert.IsNotNull(error);
            Assert.That(error.Message, Does.Contain("scroe"));
            Assert.That(error.Message, Does.Contain("score"));
        }

        [Test]
        public void Type_Mismatch_Reports_E0040()
        {
            var (_, diags) = Analyze(@"
let count: int = 0
on Start { count = ""hello"" }
");
            Assert.IsTrue(diags.HasErrors);
            Assert.That(diags.All.Any(d => d.Code == "E0040"));
        }

        [Test]
        public void Recursion_Detected_Reports_E0100()
        {
            var (_, diags) = Analyze(@"
fn foo() { bar() }
fn bar() { foo() }
");
            Assert.IsTrue(diags.HasErrors);
            var error = diags.All.First(d => d.Code == "E0100");
            Assert.IsNotNull(error);
            Assert.That(error.Message, Does.Contain("foo"));
            Assert.That(error.Message, Does.Contain("bar"));
        }

        [Test]
        public void Builtins_Resolve()
        {
            var (_, diags) = Analyze(@"
on Start {
    log(""hello"")
    warn(""warning"")
    error(""error"")
}
");
            Assert.IsFalse(diags.HasErrors);
        }

        [Test]
        public void LocalPlayer_Resolves()
        {
            var (_, diags) = Analyze(@"
on Start {
    let p: Player = localPlayer
}
");
            Assert.IsFalse(diags.HasErrors);
        }

        [Test]
        public void Transform_Resolves()
        {
            var (_, diags) = Analyze(@"
on Start {
    let t: Transform = transform
}
");
            Assert.IsFalse(diags.HasErrors);
        }

        [Test]
        public void GameObject_Resolves()
        {
            var (_, diags) = Analyze(@"
on Start {
    let g: GameObject = gameObject
}
");
            Assert.IsFalse(diags.HasErrors);
        }

        [Test]
        public void Static_Type_Access_Time()
        {
            var (_, diags) = Analyze(@"
on Update {
    let dt: float = Time.deltaTime
}
");
            Assert.IsFalse(diags.HasErrors);
        }

        [Test]
        public void Arithmetic_Operators()
        {
            var (_, diags) = Analyze(@"
let a: int = 0
let b: int = 0
on Start {
    let r: int = a + b
    let s: int = a - b
    let t: int = a * b
    let u: int = a / b
}
");
            Assert.IsFalse(diags.HasErrors);
        }

        [Test]
        public void Comparison_Operators()
        {
            var (_, diags) = Analyze(@"
let a: int = 0
let b: int = 0
on Start {
    let r: bool = a == b
    let s: bool = a != b
    let t: bool = a < b
    let u: bool = a >= b
}
");
            Assert.IsFalse(diags.HasErrors);
        }

        [Test]
        public void Boolean_Operators()
        {
            var (_, diags) = Analyze(@"
let a: bool = false
let b: bool = true
on Start {
    let r: bool = a && b
    let s: bool = a || b
    let t: bool = !a
}
");
            Assert.IsFalse(diags.HasErrors);
        }

        [Test]
        public void Function_Call_Resolves()
        {
            var (_, diags) = Analyze(@"
fn greet() {
    log(""hi"")
}
on Start {
    greet()
}
");
            Assert.IsFalse(diags.HasErrors);
        }

        [Test]
        public void Break_Outside_Loop_Reports_Error()
        {
            var (_, diags) = Analyze(@"
on Start { break }
");
            Assert.IsTrue(diags.HasErrors);
            Assert.That(diags.All.Any(d => d.Code == "E0101"));
        }

        [Test]
        public void Continue_Outside_Loop_Reports_Error()
        {
            var (_, diags) = Analyze(@"
on Start { continue }
");
            Assert.IsTrue(diags.HasErrors);
            Assert.That(diags.All.Any(d => d.Code == "E0102"));
        }

        [Test]
        public void Break_Inside_Loop_Accepted()
        {
            var (_, diags) = Analyze(@"
on Start {
    while true {
        break
    }
}
");
            Assert.IsFalse(diags.HasErrors);
        }

        [Test]
        public void Member_Property_Access()
        {
            var (_, diags) = Analyze(@"
on Start {
    let pos: Vector3 = transform.position
}
");
            Assert.IsFalse(diags.HasErrors);
        }

        [Test]
        public void Static_Method_Call()
        {
            var (_, diags) = Analyze(@"
on Start {
    let owner: bool = Networking.IsOwner(localPlayer, gameObject)
}
");
            Assert.IsFalse(diags.HasErrors);
        }

        [Test]
        public void Vector3_Static_Properties()
        {
            var (_, diags) = Analyze(@"
on Start {
    let v: Vector3 = Vector3.up
    let z: Vector3 = Vector3.zero
}
");
            Assert.IsFalse(diags.HasErrors);
        }

        [Test]
        public void ForRange_Accepted()
        {
            var (_, diags) = Analyze(@"
on Start {
    for i in 0..10 {
        log(""iteration"")
    }
}
");
            Assert.IsFalse(diags.HasErrors);
        }

        [Test]
        public void Send_Undefined_Event_Reports_Error()
        {
            var (_, diags) = Analyze(@"
on Start {
    send NoSuchEvent to All
}
");
            Assert.IsTrue(diags.HasErrors);
        }

        [Test]
        public void Custom_Event_Send_Resolves()
        {
            var (_, diags) = Analyze(@"
event MyEvent {
    log(""event fired"")
}
on Start {
    send MyEvent to All
}
");
            Assert.IsFalse(diags.HasErrors);
        }

        // --- Phase 2 tests ---

        private (ModuleDecl module, DiagnosticBag diagnostics) AnalyzeWithCatalog(
            string source, IExternCatalog catalog)
        {
            var diagnostics = new DiagnosticBag();
            var lexer = new Lexer(source, "test.nori", diagnostics);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens, diagnostics);
            var module = parser.Parse();

            if (diagnostics.HasErrors)
                return (module, diagnostics);

            var analyzer = new SemanticAnalyzer(module, catalog, diagnostics);
            analyzer.Analyze();
            return (module, diagnostics);
        }

        [Test]
        public void Property_Setter_Resolves_On_Assign()
        {
            // This tests with BuiltinCatalog which has setters for transform.position
            var (_, diags) = Analyze(@"
on Start {
    transform.position = Vector3.up
}
");
            Assert.IsFalse(diags.HasErrors);
        }

        [Test]
        public void Int_Float_Widening_Accepted()
        {
            // int -> float is assignable
            var (_, diags) = Analyze(@"
let speed: float = 5
on Start { }
");
            Assert.IsFalse(diags.HasErrors);
        }

        [Test]
        public void Compound_Assignment_Resolves()
        {
            var (module, diags) = Analyze(@"
let score: int = 0
on Start {
    score += 10
}
");
            Assert.IsFalse(diags.HasErrors);
            // The AssignStmt should have a ResolvedOperator
            var start = module.Declarations.OfType<EventHandlerDecl>().First();
            var assign = start.Body.OfType<AssignStmt>().First();
            Assert.IsNotNull(assign.ResolvedOperator);
            Assert.That(assign.ResolvedOperator.Extern, Does.Contain("op_Addition"));
        }

        [Test]
        public void Method_Call_With_Widening_Resolves()
        {
            // Mathf.Abs expects float, but int should widen via IsAssignable
            var (_, diags) = Analyze(@"
on Start {
    let x: float = Mathf.Abs(5.0)
}
");
            Assert.IsFalse(diags.HasErrors);
        }

        [Test]
        public void Unknown_Method_Reports_E0130()
        {
            var (_, diags) = Analyze(@"
on Start {
    transform.DoesNotExist()
}
");
            Assert.IsTrue(diags.HasErrors);
            Assert.That(diags.All.Any(d => d.Code == "E0130"));
        }
    }
}
