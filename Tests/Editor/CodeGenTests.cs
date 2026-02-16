using NUnit.Framework;
using Nori.Compiler;

namespace Nori.Tests
{
    [TestFixture]
    public class CodeGenTests
    {
        private CompileResult Compile(string source)
        {
            return NoriCompiler.Compile(source, "test.nori");
        }

        [Test]
        public void Hello_Compiles_With_Start_Export()
        {
            var result = Compile(@"
on Start {
    log(""hi"")
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain(".export _start"));
            Assert.That(result.Uasm, Does.Contain("_start:"));
            Assert.That(result.Uasm, Does.Contain("EXTERN, \"UnityEngineDebug.__Log__SystemObject__SystemVoid\""));
            Assert.That(result.Uasm, Does.Contain("JUMP, 0xFFFFFFFC"));
        }

        [Test]
        public void Interact_Event_Exports()
        {
            var result = Compile(@"
on Interact {
    log(""clicked"")
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain(".export _interact"));
            Assert.That(result.Uasm, Does.Contain("_interact:"));
        }

        [Test]
        public void SyncVar_In_Data_Section()
        {
            var result = Compile(@"
sync none score: int = 0
on Start { }
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain(".sync score, none"));
            Assert.That(result.Uasm, Does.Contain("score: %SystemInt32"));
        }

        [Test]
        public void PubVar_Exports()
        {
            var result = Compile(@"
pub let speed: float = 5.0
on Start { }
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain(".export speed"));
            Assert.That(result.Uasm, Does.Contain("speed: %SystemSingle"));
        }

        [Test]
        public void Send_To_All_Generates_NetworkEvent()
        {
            var result = Compile(@"
event DoThing {
    log(""do"")
}
on Start {
    send DoThing to All
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain("SendCustomNetworkEvent"));
        }

        [Test]
        public void Send_Local_Generates_SendCustomEvent()
        {
            var result = Compile(@"
event DoThing {
    log(""do"")
}
on Start {
    send DoThing
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain("SendCustomEvent"));
        }

        [Test]
        public void Assembly_Has_DataAndCode_Sections()
        {
            var result = Compile("on Start { }");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain(".data_start"));
            Assert.That(result.Uasm, Does.Contain(".data_end"));
            Assert.That(result.Uasm, Does.Contain(".code_start"));
            Assert.That(result.Uasm, Does.Contain(".code_end"));

            // Verify order
            int dataStart = result.Uasm.IndexOf(".data_start");
            int dataEnd = result.Uasm.IndexOf(".data_end");
            int codeStart = result.Uasm.IndexOf(".code_start");
            int codeEnd = result.Uasm.IndexOf(".code_end");

            Assert.Less(dataStart, dataEnd);
            Assert.Less(dataEnd, codeStart);
            Assert.Less(codeStart, codeEnd);
        }

        [Test]
        public void This_References_Declared()
        {
            var result = Compile("on Start { }");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain("this"));
        }

        [Test]
        public void This_Variable_Uses_UdonBehaviour_Type()
        {
            var result = Compile("on Start { }");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain("__this_VRCUdonUdonBehaviour_0"));
            Assert.That(result.Uasm, Does.Contain("%VRCUdonUdonBehaviour"));
            Assert.That(result.Uasm, Does.Not.Contain("VRCUdonCommonInterfacesIUdonEventReceiver"));
        }

        [Test]
        public void Arithmetic_Generates_Extern()
        {
            var result = Compile(@"
let a: int = 1
let b: int = 2
on Start {
    let c: int = a + b
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain("op_Addition"));
        }

        [Test]
        public void String_Interpolation_Generates_Concat()
        {
            var result = Compile(@"
let name: string = ""World""
on Start {
    log(""Hello, {name}!"")
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain("Concat"));
        }

        [Test]
        public void Function_Call_Generates_Jump()
        {
            var result = Compile(@"
fn greet() {
    log(""hi"")
}
on Start {
    greet()
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain("__fn_greet"));
            Assert.That(result.Uasm, Does.Contain("__retaddr_greet"));
        }

        [Test]
        public void Boolean_Negation_Generates_Extern()
        {
            var result = Compile(@"
let flag: bool = true
on Start {
    let r: bool = !flag
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain("UnaryNegation"));
        }

        [Test]
        public void Metadata_Contains_Declarations()
        {
            var result = Compile(@"
pub let speed: float = 5.0
sync none score: int = 0
event DoThing { }
fn helper() { }
on Start { }
on Interact { }
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.IsNotNull(result.Metadata);
            Assert.AreEqual(1, result.Metadata.PublicVars.Count);
            Assert.AreEqual("speed", result.Metadata.PublicVars[0].Name);
            Assert.AreEqual(1, result.Metadata.SyncVars.Count);
            Assert.AreEqual("score", result.Metadata.SyncVars[0].Name);
            Assert.AreEqual(1, result.Metadata.CustomEvents.Count);
            Assert.AreEqual("DoThing", result.Metadata.CustomEvents[0]);
            Assert.AreEqual(1, result.Metadata.Functions.Count);
            Assert.AreEqual("helper", result.Metadata.Functions[0]);
            Assert.IsTrue(result.Metadata.Events.Contains("Start"));
            Assert.IsTrue(result.Metadata.Events.Contains("Interact"));
        }

        [Test]
        public void ForRange_Compiles()
        {
            var result = Compile(@"
on Start {
    for i in 0..5 {
        log(""iter"")
    }
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain("op_LessThan"));
            Assert.That(result.Uasm, Does.Contain("op_Addition"));
        }

        [Test]
        public void If_Else_Generates_Jumps()
        {
            var result = Compile(@"
let x: bool = true
on Start {
    if x {
        log(""yes"")
    } else {
        log(""no"")
    }
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain("JUMP_IF_FALSE"));
        }

        [Test]
        public void While_Loop_Generates_Jumps()
        {
            var result = Compile(@"
let x: bool = true
on Start {
    while x {
        log(""loop"")
    }
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain("JUMP_IF_FALSE"));
            // Should have a backward jump for the loop
            Assert.That(result.Uasm, Does.Contain("JUMP, 0x"));
        }

        // --- Phase 2 tests ---

        [Test]
        public void Setter_Generates_Set_Extern()
        {
            var result = Compile(@"
on Start {
    transform.position = Vector3.up
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain("set_position"));
        }

        [Test]
        public void Static_Property_No_This_Push()
        {
            // Vector3.up should NOT push a 'this' before the getter
            var result = Compile(@"
on Start {
    let v: Vector3 = Vector3.up
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain("get_up"));
        }

        [Test]
        public void Compound_Assign_Generates_Operator()
        {
            var result = Compile(@"
let score: int = 0
on Start {
    score += 5
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain("op_Addition"));
        }

        [Test]
        public void Compound_Assign_To_Property()
        {
            // transform.position += Vector3.up should emit getter then setter
            var result = Compile(@"
on Start {
    transform.position = Vector3.up
}
");
            Assert.IsTrue(result.Success, FormatErrors(result));
            Assert.That(result.Uasm, Does.Contain("get_up"));
            Assert.That(result.Uasm, Does.Contain("set_position"));
        }

        [Test]
        public void Enum_Constant_With_FullCatalog()
        {
            // Test with FullCatalog using inline JSON
            string json = @"{
  ""externs"": [
    {""extern"":""SystemInt32.__op_Addition__SystemInt32_SystemInt32__SystemInt32"",""owner"":""SystemInt32"",""method"":""op_Addition"",""kind"":""operator"",""instance"":false,""paramTypes"":[""SystemInt32"",""SystemInt32""],""paramNames"":[""a"",""b""],""returnType"":""SystemInt32""},
    {""extern"":""SystemInt32.__op_Equality__SystemInt32_SystemInt32__SystemBoolean"",""owner"":""SystemInt32"",""method"":""op_Equality"",""kind"":""operator"",""instance"":false,""paramTypes"":[""SystemInt32"",""SystemInt32""],""paramNames"":[""a"",""b""],""returnType"":""SystemBoolean""},
    {""extern"":""SystemInt32.__op_LessThan__SystemInt32_SystemInt32__SystemBoolean"",""owner"":""SystemInt32"",""method"":""op_LessThan"",""kind"":""operator"",""instance"":false,""paramTypes"":[""SystemInt32"",""SystemInt32""],""paramNames"":[""a"",""b""],""returnType"":""SystemBoolean""},
    {""extern"":""UnityEngineDebug.__Log__SystemObject__SystemVoid"",""owner"":""UnityEngineDebug"",""method"":""Log"",""kind"":""static_method"",""instance"":false,""paramTypes"":[""SystemObject""],""paramNames"":[""message""],""returnType"":""SystemVoid""},
    {""extern"":""SystemBoolean.__op_UnaryNegation__SystemBoolean__SystemBoolean"",""owner"":""SystemBoolean"",""method"":""op_UnaryNegation"",""kind"":""operator"",""instance"":false,""paramTypes"":[""SystemBoolean""],""paramNames"":[""a""],""returnType"":""SystemBoolean""},
    {""extern"":""SystemBoolean.__op_Equality__SystemBoolean_SystemBoolean__SystemBoolean"",""owner"":""SystemBoolean"",""method"":""op_Equality"",""kind"":""operator"",""instance"":false,""paramTypes"":[""SystemBoolean"",""SystemBoolean""],""paramNames"":[""a"",""b""],""returnType"":""SystemBoolean""},
    {""extern"":""SystemBoolean.__op_Inequality__SystemBoolean_SystemBoolean__SystemBoolean"",""owner"":""SystemBoolean"",""method"":""op_Inequality"",""kind"":""operator"",""instance"":false,""paramTypes"":[""SystemBoolean"",""SystemBoolean""],""paramNames"":[""a"",""b""],""returnType"":""SystemBoolean""},
    {""extern"":""SystemBoolean.__op_ConditionalAnd__SystemBoolean_SystemBoolean__SystemBoolean"",""owner"":""SystemBoolean"",""method"":""op_ConditionalAnd"",""kind"":""operator"",""instance"":false,""paramTypes"":[""SystemBoolean"",""SystemBoolean""],""paramNames"":[""a"",""b""],""returnType"":""SystemBoolean""},
    {""extern"":""SystemBoolean.__op_ConditionalOr__SystemBoolean_SystemBoolean__SystemBoolean"",""owner"":""SystemBoolean"",""method"":""op_ConditionalOr"",""kind"":""operator"",""instance"":false,""paramTypes"":[""SystemBoolean"",""SystemBoolean""],""paramNames"":[""a"",""b""],""returnType"":""SystemBoolean""},
    {""extern"":""SystemString.__Concat__SystemString_SystemString__SystemString"",""owner"":""SystemString"",""method"":""Concat"",""kind"":""static_method"",""instance"":false,""paramTypes"":[""SystemString"",""SystemString""],""paramNames"":[""str0"",""str1""],""returnType"":""SystemString""},
    {""extern"":""SystemString.__op_Equality__SystemString_SystemString__SystemBoolean"",""owner"":""SystemString"",""method"":""op_Equality"",""kind"":""operator"",""instance"":false,""paramTypes"":[""SystemString"",""SystemString""],""paramNames"":[""a"",""b""],""returnType"":""SystemBoolean""},
    {""extern"":""SystemString.__op_Inequality__SystemString_SystemString__SystemBoolean"",""owner"":""SystemString"",""method"":""op_Inequality"",""kind"":""operator"",""instance"":false,""paramTypes"":[""SystemString"",""SystemString""],""paramNames"":[""a"",""b""],""returnType"":""SystemBoolean""},
    {""extern"":""SystemObject.__ToString__SystemString"",""owner"":""SystemObject"",""method"":""ToString"",""kind"":""method"",""instance"":true,""paramTypes"":[],""paramNames"":[],""returnType"":""SystemString""}
  ],
  ""enums"": [
    {""udonType"":""UnityEngineSpace"",""underlyingType"":""SystemInt32"",""values"":{""World"":0,""Self"":1}}
  ],
  ""types"": [
    {""udonType"":""UnityEngineSpace"",""dotNetType"":""UnityEngine.Space"",""baseType"":""SystemEnum"",""isEnum"":true},
    {""udonType"":""SystemInt32"",""dotNetType"":""System.Int32"",""baseType"":""SystemValueType"",""isEnum"":false},
    {""udonType"":""SystemSingle"",""dotNetType"":""System.Single"",""baseType"":""SystemValueType"",""isEnum"":false},
    {""udonType"":""SystemBoolean"",""dotNetType"":""System.Boolean"",""baseType"":""SystemValueType"",""isEnum"":false},
    {""udonType"":""SystemString"",""dotNetType"":""System.String"",""baseType"":""SystemObject"",""isEnum"":false},
    {""udonType"":""SystemObject"",""dotNetType"":""System.Object"",""baseType"":"""",""isEnum"":false},
    {""udonType"":""UnityEngineDebug"",""dotNetType"":""UnityEngine.Debug"",""baseType"":""SystemObject"",""isEnum"":false}
  ]
}";
            var catalog = FullCatalog.LoadFromJson(json);
            var source = @"
let spaceVal: int = Space.Self
on Start {
    log(spaceVal)
}
";
            var result = NoriCompiler.Compile(source, "test.nori", catalog);
            Assert.IsTrue(result.Success, FormatErrors(result));
            // The enum value Space.Self=1 should appear as a constant in the data section
            Assert.That(result.Uasm, Does.Contain("%UnityEngineSpace"));
        }

        private string FormatErrors(CompileResult result)
        {
            if (result.Success) return "";
            return DiagnosticPrinter.FormatAll(result.Diagnostics);
        }
    }
}
