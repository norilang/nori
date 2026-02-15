using System.Linq;
using NUnit.Framework;
using Nori.Compiler;

namespace Nori.Tests
{
    [TestFixture]
    public class CatalogTests
    {
        private const string TestCatalogJson = @"{
  ""externs"": [
    {""extern"":""UnityEngineTransform.__get_position__UnityEngineVector3"",""owner"":""UnityEngineTransform"",""method"":""position"",""kind"":""getter"",""instance"":true,""paramTypes"":[],""paramNames"":[],""returnType"":""UnityEngineVector3""},
    {""extern"":""UnityEngineTransform.__set_position__UnityEngineVector3__SystemVoid"",""owner"":""UnityEngineTransform"",""method"":""position"",""kind"":""setter"",""instance"":true,""paramTypes"":[""UnityEngineVector3""],""paramNames"":[""value""],""returnType"":""SystemVoid""},
    {""extern"":""UnityEngineTransform.__Rotate__UnityEngineVector3__SystemVoid"",""owner"":""UnityEngineTransform"",""method"":""Rotate"",""kind"":""method"",""instance"":true,""paramTypes"":[""UnityEngineVector3""],""paramNames"":[""eulers""],""returnType"":""SystemVoid""},
    {""extern"":""UnityEngineTransform.__Rotate__UnityEngineVector3_SystemSingle__SystemVoid"",""owner"":""UnityEngineTransform"",""method"":""Rotate"",""kind"":""method"",""instance"":true,""paramTypes"":[""UnityEngineVector3"",""SystemSingle""],""paramNames"":[""axis"",""angle""],""returnType"":""SystemVoid""},
    {""extern"":""UnityEngineDebug.__Log__SystemObject__SystemVoid"",""owner"":""UnityEngineDebug"",""method"":""Log"",""kind"":""static_method"",""instance"":false,""paramTypes"":[""SystemObject""],""paramNames"":[""message""],""returnType"":""SystemVoid""},
    {""extern"":""UnityEngineMathf.__Abs__SystemSingle__SystemSingle"",""owner"":""UnityEngineMathf"",""method"":""Abs"",""kind"":""static_method"",""instance"":false,""paramTypes"":[""SystemSingle""],""paramNames"":[""f""],""returnType"":""SystemSingle""},
    {""extern"":""UnityEngineMathf.__Max__SystemSingle_SystemSingle__SystemSingle"",""owner"":""UnityEngineMathf"",""method"":""Max"",""kind"":""static_method"",""instance"":false,""paramTypes"":[""SystemSingle"",""SystemSingle""],""paramNames"":[""a"",""b""],""returnType"":""SystemSingle""},
    {""extern"":""SystemInt32.__op_Addition__SystemInt32_SystemInt32__SystemInt32"",""owner"":""SystemInt32"",""method"":""op_Addition"",""kind"":""operator"",""instance"":false,""paramTypes"":[""SystemInt32"",""SystemInt32""],""paramNames"":[""a"",""b""],""returnType"":""SystemInt32""},
    {""extern"":""SystemSingle.__op_Addition__SystemSingle_SystemSingle__SystemSingle"",""owner"":""SystemSingle"",""method"":""op_Addition"",""kind"":""operator"",""instance"":false,""paramTypes"":[""SystemSingle"",""SystemSingle""],""paramNames"":[""a"",""b""],""returnType"":""SystemSingle""},
    {""extern"":""SystemInt32.__op_Equality__SystemInt32_SystemInt32__SystemBoolean"",""owner"":""SystemInt32"",""method"":""op_Equality"",""kind"":""operator"",""instance"":false,""paramTypes"":[""SystemInt32"",""SystemInt32""],""paramNames"":[""a"",""b""],""returnType"":""SystemBoolean""},
    {""extern"":""UnityEngineVector3.__get_up__UnityEngineVector3"",""owner"":""UnityEngineVector3"",""method"":""up"",""kind"":""getter"",""instance"":false,""paramTypes"":[],""paramNames"":[],""returnType"":""UnityEngineVector3""},
    {""extern"":""UnityEngineTransform.__get_localPosition__UnityEngineVector3"",""owner"":""UnityEngineTransform"",""method"":""localPosition"",""kind"":""getter"",""instance"":true,""paramTypes"":[],""paramNames"":[],""returnType"":""UnityEngineVector3""}
  ],
  ""enums"": [
    {""udonType"":""UnityEngineSpace"",""underlyingType"":""SystemInt32"",""values"":{""World"":0,""Self"":1}},
    {""udonType"":""UnityEngineKeyCode"",""underlyingType"":""SystemInt32"",""values"":{""None"":0,""A"":97,""E"":101,""Space"":32}}
  ],
  ""types"": [
    {""udonType"":""UnityEngineTransform"",""dotNetType"":""UnityEngine.Transform"",""baseType"":""UnityEngineComponent"",""isEnum"":false},
    {""udonType"":""UnityEngineVector3"",""dotNetType"":""UnityEngine.Vector3"",""baseType"":""SystemValueType"",""isEnum"":false},
    {""udonType"":""UnityEngineSpace"",""dotNetType"":""UnityEngine.Space"",""baseType"":""SystemEnum"",""isEnum"":true},
    {""udonType"":""UnityEngineKeyCode"",""dotNetType"":""UnityEngine.KeyCode"",""baseType"":""SystemEnum"",""isEnum"":true},
    {""udonType"":""UnityEngineMathf"",""dotNetType"":""UnityEngine.Mathf"",""baseType"":""SystemValueType"",""isEnum"":false},
    {""udonType"":""UnityEngineDebug"",""dotNetType"":""UnityEngine.Debug"",""baseType"":""SystemObject"",""isEnum"":false},
    {""udonType"":""SystemInt32"",""dotNetType"":""System.Int32"",""baseType"":""SystemValueType"",""isEnum"":false},
    {""udonType"":""SystemSingle"",""dotNetType"":""System.Single"",""baseType"":""SystemValueType"",""isEnum"":false},
    {""udonType"":""SystemBoolean"",""dotNetType"":""System.Boolean"",""baseType"":""SystemValueType"",""isEnum"":false}
  ]
}";

        private FullCatalog LoadTestCatalog()
        {
            return FullCatalog.LoadFromJson(TestCatalogJson);
        }

        [Test]
        public void LoadFromJson_Succeeds()
        {
            var catalog = LoadTestCatalog();
            Assert.IsNotNull(catalog);
        }

        [Test]
        public void Property_Getter_Resolves()
        {
            var catalog = LoadTestCatalog();
            var prop = catalog.ResolveProperty("UnityEngineTransform", "position");
            Assert.IsNotNull(prop);
            Assert.AreEqual("UnityEngineVector3", prop.Type);
            Assert.IsNotNull(prop.Getter);
            Assert.That(prop.Getter.Extern, Does.Contain("get_position"));
        }

        [Test]
        public void Property_Setter_Resolves()
        {
            var catalog = LoadTestCatalog();
            var prop = catalog.ResolveProperty("UnityEngineTransform", "position");
            Assert.IsNotNull(prop);
            Assert.IsNotNull(prop.Setter);
            Assert.That(prop.Setter.Extern, Does.Contain("set_position"));
        }

        [Test]
        public void ReadOnly_Property_Has_No_Setter()
        {
            var catalog = LoadTestCatalog();
            var prop = catalog.ResolveProperty("UnityEngineTransform", "localPosition");
            Assert.IsNotNull(prop);
            Assert.IsNotNull(prop.Getter);
            Assert.IsNull(prop.Setter);
        }

        [Test]
        public void Overload_Resolution_Exact_Match()
        {
            var catalog = LoadTestCatalog();
            var sig = catalog.ResolveMethod("UnityEngineTransform", "Rotate",
                new[] { "UnityEngineVector3" });
            Assert.IsNotNull(sig);
            Assert.AreEqual(1, sig.ParamTypes.Length);
        }

        [Test]
        public void Overload_Resolution_Multiple_Params()
        {
            var catalog = LoadTestCatalog();
            var sig = catalog.ResolveMethod("UnityEngineTransform", "Rotate",
                new[] { "UnityEngineVector3", "SystemSingle" });
            Assert.IsNotNull(sig);
            Assert.AreEqual(2, sig.ParamTypes.Length);
        }

        [Test]
        public void Overload_Resolution_No_Match_Returns_Null()
        {
            var catalog = LoadTestCatalog();
            var sig = catalog.ResolveMethod("UnityEngineTransform", "Rotate",
                new[] { "SystemInt32", "SystemInt32", "SystemInt32" });
            Assert.IsNull(sig);
        }

        [Test]
        public void Overload_Resolution_Widening()
        {
            var catalog = LoadTestCatalog();
            // int -> float widening: Abs(int) should match Abs(float) via IsAssignable
            var sig = catalog.ResolveStaticMethod("UnityEngineMathf", "Abs",
                new[] { "SystemInt32" });
            Assert.IsNotNull(sig);
            Assert.AreEqual("SystemSingle", sig.ReturnType);
        }

        [Test]
        public void Enum_Resolves()
        {
            var catalog = LoadTestCatalog();
            var enumInfo = catalog.ResolveEnum("UnityEngineSpace");
            Assert.IsNotNull(enumInfo);
            Assert.AreEqual("UnityEngineSpace", enumInfo.UdonType);
            Assert.AreEqual("SystemInt32", enumInfo.UnderlyingType);
            Assert.IsTrue(enumInfo.Values.ContainsKey("Self"));
            Assert.AreEqual(1, enumInfo.Values["Self"]);
            Assert.IsTrue(enumInfo.Values.ContainsKey("World"));
            Assert.AreEqual(0, enumInfo.Values["World"]);
        }

        [Test]
        public void IsEnumType_Returns_True_For_Enums()
        {
            var catalog = LoadTestCatalog();
            Assert.IsTrue(catalog.IsEnumType("UnityEngineSpace"));
            Assert.IsTrue(catalog.IsEnumType("UnityEngineKeyCode"));
            Assert.IsFalse(catalog.IsEnumType("UnityEngineTransform"));
        }

        [Test]
        public void Implicit_Conversion_Int_To_Float()
        {
            var catalog = LoadTestCatalog();
            var conv = catalog.GetImplicitConversion("SystemInt32", "SystemSingle");
            Assert.IsNotNull(conv);
            Assert.AreEqual("SystemInt32", conv.FromType);
            Assert.AreEqual("SystemSingle", conv.ToType);
            Assert.That(conv.ConversionExtern, Does.Contain("ToSingle"));
        }

        [Test]
        public void Implicit_Conversion_No_Match()
        {
            var catalog = LoadTestCatalog();
            var conv = catalog.GetImplicitConversion("SystemString", "SystemInt32");
            Assert.IsNull(conv);
        }

        [Test]
        public void Operator_Resolution()
        {
            var catalog = LoadTestCatalog();
            var op = catalog.ResolveOperator(TokenKind.Plus, "SystemInt32", "SystemInt32");
            Assert.IsNotNull(op);
            Assert.That(op.Extern, Does.Contain("op_Addition"));
            Assert.AreEqual("SystemInt32", op.ReturnType);
        }

        [Test]
        public void Operator_Widening_Int_Plus_Float()
        {
            var catalog = LoadTestCatalog();
            var op = catalog.ResolveOperator(TokenKind.Plus, "SystemInt32", "SystemSingle");
            Assert.IsNotNull(op);
            Assert.AreEqual("SystemSingle", op.ReturnType);
        }

        [Test]
        public void GetMethodOverloads_Returns_All()
        {
            var catalog = LoadTestCatalog();
            var overloads = catalog.GetMethodOverloads("UnityEngineTransform", "Rotate");
            Assert.AreEqual(2, overloads.Count);
        }

        [Test]
        public void Static_Method_Resolves()
        {
            var catalog = LoadTestCatalog();
            var sig = catalog.ResolveStaticMethod("UnityEngineDebug", "Log",
                new[] { "SystemObject" });
            Assert.IsNotNull(sig);
            Assert.AreEqual("SystemVoid", sig.ReturnType);
        }

        [Test]
        public void Type_Info_Resolves()
        {
            var catalog = LoadTestCatalog();
            var info = catalog.GetTypeInfo("UnityEngineTransform");
            Assert.IsNotNull(info);
            Assert.AreEqual("UnityEngine.Transform", info.DotNetType);
            Assert.IsFalse(info.IsEnum);
        }

        [Test]
        public void IsKnownType_Works()
        {
            var catalog = LoadTestCatalog();
            Assert.IsTrue(catalog.IsKnownType("UnityEngineTransform"));
            Assert.IsTrue(catalog.IsKnownType("UnityEngineSpace"));
            Assert.IsFalse(catalog.IsKnownType("NonExistentType"));
        }

        [Test]
        public void Named_Params_Preserved()
        {
            var catalog = LoadTestCatalog();
            var overloads = catalog.GetMethodOverloads("UnityEngineTransform", "Rotate");
            var twoParam = overloads.FirstOrDefault(s => s.ParamTypes.Length == 2);
            Assert.IsNotNull(twoParam);
            Assert.IsNotNull(twoParam.Params);
            Assert.AreEqual("axis", twoParam.Params[0].Name);
            Assert.AreEqual("angle", twoParam.Params[1].Name);
        }
    }
}
