using System.Collections.Generic;
using System.Linq;

namespace Nori.Compiler
{
    public class BuiltinCatalog : IExternCatalog
    {
        public static readonly BuiltinCatalog Instance = new BuiltinCatalog();

        // Properties: (ownerType, propName) -> PropertyInfo
        private readonly Dictionary<(string, string), PropertyInfo> _properties
            = new Dictionary<(string, string), PropertyInfo>();

        // Methods: (ownerType, methodName) -> list of overloads
        private readonly Dictionary<(string, string), List<ExternSignature>> _methods
            = new Dictionary<(string, string), List<ExternSignature>>();

        // Static methods: (typeUdonName, methodName) -> list of overloads
        private readonly Dictionary<(string, string), List<ExternSignature>> _staticMethods
            = new Dictionary<(string, string), List<ExternSignature>>();

        // Operators: (op, leftType, rightType) -> OperatorInfo
        private readonly Dictionary<(TokenKind, string, string), OperatorInfo> _operators
            = new Dictionary<(TokenKind, string, string), OperatorInfo>();

        // Unary operators: (op, type) -> OperatorInfo
        private readonly Dictionary<(TokenKind, string), OperatorInfo> _unaryOperators
            = new Dictionary<(TokenKind, string), OperatorInfo>();

        private readonly HashSet<string> _knownTypes = new HashSet<string>();

        private BuiltinCatalog()
        {
            RegisterArithmeticOperators();
            RegisterComparisonOperators();
            RegisterBooleanOperators();
            RegisterStringOperations();
            RegisterDebugMethods();
            RegisterArrayOperations();
            RegisterTransformProperties();
            RegisterGameObjectOperations();
            RegisterTimeProperties();
            RegisterNetworkingOperations();
            RegisterPlayerProperties();
            RegisterUdonBehaviourMethods();
            RegisterVector3Operations();
            RegisterQuaternionOperations();
            RegisterMathfMethods();
        }

        private void AddProperty(string owner, string name, string type,
            string getter, string setter = null)
        {
            _knownTypes.Add(owner);
            var getterSig = getter != null
                ? new ExternSignature(getter, new string[0], type, true) : null;
            var setterSig = setter != null
                ? new ExternSignature(setter, new[] { type }, "SystemVoid", true) : null;
            _properties[(owner, name)] = new PropertyInfo(type, getterSig, setterSig);
        }

        private void AddStaticProperty(string owner, string name, string type, string getter)
        {
            _knownTypes.Add(owner);
            var getterSig = new ExternSignature(getter, new string[0], type, false);
            _properties[(owner, name)] = new PropertyInfo(type, getterSig);
        }

        private void AddMethod(string owner, string name, ExternSignature sig)
        {
            _knownTypes.Add(owner);
            var key = (owner, name);
            if (!_methods.ContainsKey(key))
                _methods[key] = new List<ExternSignature>();
            _methods[key].Add(sig);
        }

        private void AddStaticMethod(string owner, string name, ExternSignature sig)
        {
            _knownTypes.Add(owner);
            var key = (owner, name);
            if (!_staticMethods.ContainsKey(key))
                _staticMethods[key] = new List<ExternSignature>();
            _staticMethods[key].Add(sig);
        }

        private void AddOp(TokenKind op, string type, string externSig, string returnType)
        {
            _operators[(op, type, type)] = new OperatorInfo(externSig, returnType);
        }

        private void AddOpMixed(TokenKind op, string left, string right, string externSig, string returnType)
        {
            _operators[(op, left, right)] = new OperatorInfo(externSig, returnType);
        }

        private void RegisterArithmeticOperators()
        {
            foreach (var type in new[] { "SystemInt32", "SystemSingle" })
            {
                AddOp(TokenKind.Plus, type,
                    $"{type}.__op_Addition__{type}_{type}__{type}", type);
                AddOp(TokenKind.Minus, type,
                    $"{type}.__op_Subtraction__{type}_{type}__{type}", type);
                AddOp(TokenKind.Star, type,
                    $"{type}.__op_Multiplication__{type}_{type}__{type}", type);
                AddOp(TokenKind.Slash, type,
                    $"{type}.__op_Division__{type}_{type}__{type}", type);
                AddOp(TokenKind.Percent, type,
                    $"{type}.__op_Modulus__{type}_{type}__{type}", type);

                // Unary negation
                _unaryOperators[(TokenKind.Minus, type)] = new OperatorInfo(
                    $"{type}.__op_UnaryNegation__{type}__{type}", type);
            }
        }

        private void RegisterComparisonOperators()
        {
            foreach (var type in new[] { "SystemInt32", "SystemSingle" })
            {
                AddOp(TokenKind.EqualsEquals, type,
                    $"{type}.__op_Equality__{type}_{type}__SystemBoolean", "SystemBoolean");
                AddOp(TokenKind.BangEquals, type,
                    $"{type}.__op_Inequality__{type}_{type}__SystemBoolean", "SystemBoolean");
                AddOp(TokenKind.Less, type,
                    $"{type}.__op_LessThan__{type}_{type}__SystemBoolean", "SystemBoolean");
                AddOp(TokenKind.LessEquals, type,
                    $"{type}.__op_LessThanOrEqual__{type}_{type}__SystemBoolean", "SystemBoolean");
                AddOp(TokenKind.Greater, type,
                    $"{type}.__op_GreaterThan__{type}_{type}__SystemBoolean", "SystemBoolean");
                AddOp(TokenKind.GreaterEquals, type,
                    $"{type}.__op_GreaterThanOrEqual__{type}_{type}__SystemBoolean", "SystemBoolean");
            }

            // Boolean equality
            AddOp(TokenKind.EqualsEquals, "SystemBoolean",
                "SystemBoolean.__op_Equality__SystemBoolean_SystemBoolean__SystemBoolean", "SystemBoolean");
            AddOp(TokenKind.BangEquals, "SystemBoolean",
                "SystemBoolean.__op_Inequality__SystemBoolean_SystemBoolean__SystemBoolean", "SystemBoolean");

            // String equality
            AddOp(TokenKind.EqualsEquals, "SystemString",
                "SystemString.__op_Equality__SystemString_SystemString__SystemBoolean", "SystemBoolean");
            AddOp(TokenKind.BangEquals, "SystemString",
                "SystemString.__op_Inequality__SystemString_SystemString__SystemBoolean", "SystemBoolean");
        }

        private void RegisterBooleanOperators()
        {
            _unaryOperators[(TokenKind.Bang, "SystemBoolean")] = new OperatorInfo(
                "SystemBoolean.__op_UnaryNegation__SystemBoolean__SystemBoolean", "SystemBoolean");

            // && and || — Phase 1 uses EXTERN-based approach (no short-circuiting)
            AddOp(TokenKind.And, "SystemBoolean",
                "SystemBoolean.__op_ConditionalAnd__SystemBoolean_SystemBoolean__SystemBoolean", "SystemBoolean");
            AddOp(TokenKind.Or, "SystemBoolean",
                "SystemBoolean.__op_ConditionalOr__SystemBoolean_SystemBoolean__SystemBoolean", "SystemBoolean");

            // Object equality/inequality (used for null comparisons: obj == null, obj != null)
            AddOp(TokenKind.EqualsEquals, "SystemObject",
                "SystemObject.__op_Equality__SystemObject_SystemObject__SystemBoolean", "SystemBoolean");
            AddOp(TokenKind.BangEquals, "SystemObject",
                "SystemObject.__op_Inequality__SystemObject_SystemObject__SystemBoolean", "SystemBoolean");
        }

        private void RegisterStringOperations()
        {
            // String.Concat(string, string)
            AddStaticMethod("SystemString", "Concat", new ExternSignature(
                "SystemString.__Concat__SystemString_SystemString__SystemString",
                new[] { "SystemString", "SystemString" }, "SystemString", false));

            // Object.ToString()
            AddMethod("SystemObject", "ToString", new ExternSignature(
                "SystemObject.__ToString__SystemString",
                new string[0], "SystemString", true));

            // String + String
            AddOp(TokenKind.Plus, "SystemString",
                "SystemString.__Concat__SystemString_SystemString__SystemString", "SystemString");
        }

        private void RegisterDebugMethods()
        {
            // Debug.Log(object)
            AddStaticMethod("UnityEngineDebug", "Log", new ExternSignature(
                "UnityEngineDebug.__Log__SystemObject__SystemVoid",
                new[] { "SystemObject" }, "SystemVoid", false));

            AddStaticMethod("UnityEngineDebug", "LogWarning", new ExternSignature(
                "UnityEngineDebug.__LogWarning__SystemObject__SystemVoid",
                new[] { "SystemObject" }, "SystemVoid", false));

            AddStaticMethod("UnityEngineDebug", "LogError", new ExternSignature(
                "UnityEngineDebug.__LogError__SystemObject__SystemVoid",
                new[] { "SystemObject" }, "SystemVoid", false));
        }

        private void RegisterArrayOperations()
        {
            foreach (var elemType in new[] { "SystemInt32", "SystemSingle", "SystemString",
                "SystemBoolean", "SystemObject", "UnityEngineGameObject", "VRCSDKBaseVRCPlayerApi" })
            {
                var arrayType = elemType + "Array";
                _knownTypes.Add(arrayType);

                AddProperty(arrayType, "Length", "SystemInt32",
                    $"{arrayType}.__get_Length__SystemInt32");

                AddMethod(arrayType, "Get", new ExternSignature(
                    $"{arrayType}.__Get__SystemInt32__{elemType}",
                    new[] { "SystemInt32" }, elemType, true));

                AddMethod(arrayType, "Set", new ExternSignature(
                    $"{arrayType}.__Set__SystemInt32_{elemType}__SystemVoid",
                    new[] { "SystemInt32", elemType }, "SystemVoid", true));
            }
        }

        private void RegisterTransformProperties()
        {
            _knownTypes.Add("UnityEngineTransform");

            AddProperty("UnityEngineTransform", "position", "UnityEngineVector3",
                "UnityEngineTransform.__get_position__UnityEngineVector3",
                "UnityEngineTransform.__set_position__UnityEngineVector3__SystemVoid");

            AddProperty("UnityEngineTransform", "rotation", "UnityEngineQuaternion",
                "UnityEngineTransform.__get_rotation__UnityEngineQuaternion",
                "UnityEngineTransform.__set_rotation__UnityEngineQuaternion__SystemVoid");

            AddProperty("UnityEngineTransform", "localPosition", "UnityEngineVector3",
                "UnityEngineTransform.__get_localPosition__UnityEngineVector3",
                "UnityEngineTransform.__set_localPosition__UnityEngineVector3__SystemVoid");

            AddProperty("UnityEngineTransform", "localRotation", "UnityEngineQuaternion",
                "UnityEngineTransform.__get_localRotation__UnityEngineQuaternion",
                "UnityEngineTransform.__set_localRotation__UnityEngineQuaternion__SystemVoid");

            AddMethod("UnityEngineTransform", "Rotate", new ExternSignature(
                "UnityEngineTransform.__Rotate__UnityEngineVector3_SystemSingle__SystemVoid",
                new[] { "UnityEngineVector3", "SystemSingle" }, "SystemVoid", true));

            AddMethod("UnityEngineTransform", "Rotate", new ExternSignature(
                "UnityEngineTransform.__Rotate__UnityEngineVector3__SystemVoid",
                new[] { "UnityEngineVector3" }, "SystemVoid", true));
        }

        private void RegisterGameObjectOperations()
        {
            _knownTypes.Add("UnityEngineGameObject");

            AddMethod("UnityEngineGameObject", "SetActive", new ExternSignature(
                "UnityEngineGameObject.__SetActive__SystemBoolean__SystemVoid",
                new[] { "SystemBoolean" }, "SystemVoid", true));

            AddProperty("UnityEngineGameObject", "activeSelf", "SystemBoolean",
                "UnityEngineGameObject.__get_activeSelf__SystemBoolean");

            AddProperty("UnityEngineGameObject", "transform", "UnityEngineTransform",
                "UnityEngineGameObject.__get_transform__UnityEngineTransform");
        }

        private void RegisterTimeProperties()
        {
            _knownTypes.Add("UnityEngineTime");

            AddStaticProperty("UnityEngineTime", "deltaTime", "SystemSingle",
                "UnityEngineTime.__get_deltaTime__SystemSingle");

            AddStaticProperty("UnityEngineTime", "time", "SystemSingle",
                "UnityEngineTime.__get_time__SystemSingle");
        }

        private void RegisterNetworkingOperations()
        {
            _knownTypes.Add("VRCSDKBaseVRCNetworking");

            AddStaticProperty("VRCSDKBaseVRCNetworking", "LocalPlayer", "VRCSDKBaseVRCPlayerApi",
                "VRCSDKBaseVRCNetworking.__get_LocalPlayer__VRCSDKBaseVRCPlayerApi");

            AddStaticMethod("VRCSDKBaseVRCNetworking", "IsOwner", new ExternSignature(
                "VRCSDKBaseVRCNetworking.__IsOwner__VRCSDKBaseVRCPlayerApi_UnityEngineGameObject__SystemBoolean",
                new[] { "VRCSDKBaseVRCPlayerApi", "UnityEngineGameObject" }, "SystemBoolean", false));

            AddStaticMethod("VRCSDKBaseVRCNetworking", "SetOwner", new ExternSignature(
                "VRCSDKBaseVRCNetworking.__SetOwner__VRCSDKBaseVRCPlayerApi_UnityEngineGameObject__SystemVoid",
                new[] { "VRCSDKBaseVRCPlayerApi", "UnityEngineGameObject" }, "SystemVoid", false));
        }

        private void RegisterPlayerProperties()
        {
            _knownTypes.Add("VRCSDKBaseVRCPlayerApi");

            AddProperty("VRCSDKBaseVRCPlayerApi", "displayName", "SystemString",
                "VRCSDKBaseVRCPlayerApi.__get_displayName__SystemString");

            AddProperty("VRCSDKBaseVRCPlayerApi", "isLocal", "SystemBoolean",
                "VRCSDKBaseVRCPlayerApi.__get_isLocal__SystemBoolean");

            AddProperty("VRCSDKBaseVRCPlayerApi", "isMaster", "SystemBoolean",
                "VRCSDKBaseVRCPlayerApi.__get_isMaster__SystemBoolean");
        }

        private void RegisterUdonBehaviourMethods()
        {
            _knownTypes.Add("VRCUdonCommonInterfacesIUdonEventReceiver");

            AddMethod("VRCUdonCommonInterfacesIUdonEventReceiver", "SendCustomEvent",
                new ExternSignature(
                    "VRCUdonCommonInterfacesIUdonEventReceiver.__SendCustomEvent__SystemString__SystemVoid",
                    new[] { "SystemString" }, "SystemVoid", true));

            AddMethod("VRCUdonCommonInterfacesIUdonEventReceiver", "SendCustomNetworkEvent",
                new ExternSignature(
                    "VRCUdonCommonInterfacesIUdonEventReceiver.__SendCustomNetworkEvent__VRCSDKBaseVRCNetworkingNetworkEventTarget_SystemString__SystemVoid",
                    new[] { "VRCSDKBaseVRCNetworkingNetworkEventTarget", "SystemString" },
                    "SystemVoid", true));

            AddMethod("VRCUdonCommonInterfacesIUdonEventReceiver", "RequestSerialization",
                new ExternSignature(
                    "VRCUdonCommonInterfacesIUdonEventReceiver.__RequestSerialization__SystemVoid",
                    new string[0], "SystemVoid", true));
        }

        private void RegisterVector3Operations()
        {
            _knownTypes.Add("UnityEngineVector3");

            // Static properties
            AddStaticProperty("UnityEngineVector3", "zero", "UnityEngineVector3",
                "UnityEngineVector3.__get_zero__UnityEngineVector3");
            AddStaticProperty("UnityEngineVector3", "one", "UnityEngineVector3",
                "UnityEngineVector3.__get_one__UnityEngineVector3");
            AddStaticProperty("UnityEngineVector3", "up", "UnityEngineVector3",
                "UnityEngineVector3.__get_up__UnityEngineVector3");
            AddStaticProperty("UnityEngineVector3", "forward", "UnityEngineVector3",
                "UnityEngineVector3.__get_forward__UnityEngineVector3");
            AddStaticProperty("UnityEngineVector3", "right", "UnityEngineVector3",
                "UnityEngineVector3.__get_right__UnityEngineVector3");

            // Instance properties
            AddProperty("UnityEngineVector3", "x", "SystemSingle",
                "UnityEngineVector3.__get_x__SystemSingle",
                "UnityEngineVector3.__set_x__SystemSingle__SystemVoid");
            AddProperty("UnityEngineVector3", "y", "SystemSingle",
                "UnityEngineVector3.__get_y__SystemSingle",
                "UnityEngineVector3.__set_y__SystemSingle__SystemVoid");
            AddProperty("UnityEngineVector3", "z", "SystemSingle",
                "UnityEngineVector3.__get_z__SystemSingle",
                "UnityEngineVector3.__set_z__SystemSingle__SystemVoid");
            AddProperty("UnityEngineVector3", "magnitude", "SystemSingle",
                "UnityEngineVector3.__get_magnitude__SystemSingle");

            // Vector3 + Vector3
            AddOp(TokenKind.Plus, "UnityEngineVector3",
                "UnityEngineVector3.__op_Addition__UnityEngineVector3_UnityEngineVector3__UnityEngineVector3",
                "UnityEngineVector3");
            // Vector3 - Vector3
            AddOp(TokenKind.Minus, "UnityEngineVector3",
                "UnityEngineVector3.__op_Subtraction__UnityEngineVector3_UnityEngineVector3__UnityEngineVector3",
                "UnityEngineVector3");
            // Vector3 * float
            AddOpMixed(TokenKind.Star, "UnityEngineVector3", "SystemSingle",
                "UnityEngineVector3.__op_Multiply__UnityEngineVector3_SystemSingle__UnityEngineVector3",
                "UnityEngineVector3");
            // float * Vector3
            AddOpMixed(TokenKind.Star, "SystemSingle", "UnityEngineVector3",
                "UnityEngineVector3.__op_Multiply__SystemSingle_UnityEngineVector3__UnityEngineVector3",
                "UnityEngineVector3");

            // Static methods
            AddStaticMethod("UnityEngineVector3", "Lerp", new ExternSignature(
                "UnityEngineVector3.__Lerp__UnityEngineVector3_UnityEngineVector3_SystemSingle__UnityEngineVector3",
                new[] { "UnityEngineVector3", "UnityEngineVector3", "SystemSingle" },
                "UnityEngineVector3", false));

            AddStaticMethod("UnityEngineVector3", "Distance", new ExternSignature(
                "UnityEngineVector3.__Distance__UnityEngineVector3_UnityEngineVector3__SystemSingle",
                new[] { "UnityEngineVector3", "UnityEngineVector3" },
                "SystemSingle", false));
        }

        private void RegisterQuaternionOperations()
        {
            _knownTypes.Add("UnityEngineQuaternion");

            AddStaticProperty("UnityEngineQuaternion", "identity", "UnityEngineQuaternion",
                "UnityEngineQuaternion.__get_identity__UnityEngineQuaternion");
        }

        private void RegisterMathfMethods()
        {
            _knownTypes.Add("UnityEngineMathf");

            AddStaticMethod("UnityEngineMathf", "Abs", new ExternSignature(
                "UnityEngineMathf.__Abs__SystemSingle__SystemSingle",
                new[] { "SystemSingle" }, "SystemSingle", false));

            AddStaticMethod("UnityEngineMathf", "Min", new ExternSignature(
                "UnityEngineMathf.__Min__SystemSingle_SystemSingle__SystemSingle",
                new[] { "SystemSingle", "SystemSingle" }, "SystemSingle", false));

            AddStaticMethod("UnityEngineMathf", "Max", new ExternSignature(
                "UnityEngineMathf.__Max__SystemSingle_SystemSingle__SystemSingle",
                new[] { "SystemSingle", "SystemSingle" }, "SystemSingle", false));

            AddStaticMethod("UnityEngineMathf", "Clamp", new ExternSignature(
                "UnityEngineMathf.__Clamp__SystemSingle_SystemSingle_SystemSingle__SystemSingle",
                new[] { "SystemSingle", "SystemSingle", "SystemSingle" }, "SystemSingle", false));

            AddStaticMethod("UnityEngineMathf", "Lerp", new ExternSignature(
                "UnityEngineMathf.__Lerp__SystemSingle_SystemSingle_SystemSingle__SystemSingle",
                new[] { "SystemSingle", "SystemSingle", "SystemSingle" }, "SystemSingle", false));
        }

        // --- IExternCatalog implementation ---

        public PropertyInfo ResolveProperty(string ownerUdonType, string propertyName)
        {
            if (_properties.TryGetValue((ownerUdonType, propertyName), out var prop))
                return prop;

            // Try SystemObject fallback for ToString etc
            if (ownerUdonType != "SystemObject" &&
                _properties.TryGetValue(("SystemObject", propertyName), out prop))
                return prop;

            return null;
        }

        public ExternSignature ResolveMethod(string ownerUdonType, string methodName, string[] argTypes)
        {
            if (_methods.TryGetValue((ownerUdonType, methodName), out var overloads))
                return FindBestOverload(overloads, argTypes);

            // Try SystemObject fallback
            if (ownerUdonType != "SystemObject" &&
                _methods.TryGetValue(("SystemObject", methodName), out overloads))
                return FindBestOverload(overloads, argTypes);

            return null;
        }

        public ExternSignature ResolveStaticMethod(string typeUdonName, string methodName, string[] argTypes)
        {
            if (_staticMethods.TryGetValue((typeUdonName, methodName), out var overloads))
                return FindBestOverload(overloads, argTypes);
            return null;
        }

        public OperatorInfo ResolveOperator(TokenKind op, string leftType, string rightType)
        {
            if (_operators.TryGetValue((op, leftType, rightType), out var info))
                return info;

            // Try widening: int op float -> float op float
            if (leftType == "SystemInt32" && rightType == "SystemSingle")
            {
                if (_operators.TryGetValue((op, "SystemSingle", "SystemSingle"), out info))
                    return new OperatorInfo(info.Extern, info.ReturnType, "SystemSingle", "SystemSingle");
            }
            if (leftType == "SystemSingle" && rightType == "SystemInt32")
            {
                if (_operators.TryGetValue((op, "SystemSingle", "SystemSingle"), out info))
                    return new OperatorInfo(info.Extern, info.ReturnType, "SystemSingle", "SystemSingle");
            }

            // Object fallback for equality/inequality (null comparisons)
            if (op == TokenKind.EqualsEquals || op == TokenKind.BangEquals)
            {
                if (leftType == "SystemObject" || rightType == "SystemObject")
                {
                    if (_operators.TryGetValue((op, "SystemObject", "SystemObject"), out info))
                        return new OperatorInfo(info.Extern, info.ReturnType,
                            "SystemObject", "SystemObject");
                }
            }

            return null;
        }

        public OperatorInfo ResolveUnaryOperator(TokenKind op, string operandType)
        {
            _unaryOperators.TryGetValue((op, operandType), out var info);
            return info;
        }

        public bool IsKnownType(string udonType) => _knownTypes.Contains(udonType);

        public IEnumerable<string> GetMethodNames(string ownerUdonType)
        {
            return _methods.Keys
                .Where(k => k.Item1 == ownerUdonType)
                .Select(k => k.Item2);
        }

        public IEnumerable<string> GetPropertyNames(string ownerUdonType)
        {
            return _properties.Keys
                .Where(k => k.Item1 == ownerUdonType)
                .Select(k => k.Item2);
        }

        // Phase 2 interface stubs

        public List<ExternSignature> GetMethodOverloads(string ownerUdonType, string methodName)
        {
            if (_methods.TryGetValue((ownerUdonType, methodName), out var overloads))
                return overloads;
            if (ownerUdonType != "SystemObject" &&
                _methods.TryGetValue(("SystemObject", methodName), out overloads))
                return overloads;
            return new List<ExternSignature>();
        }

        public List<ExternSignature> GetStaticMethodOverloads(string typeUdonName, string methodName)
        {
            if (_staticMethods.TryGetValue((typeUdonName, methodName), out var overloads))
                return overloads;
            return new List<ExternSignature>();
        }

        public EnumTypeInfo ResolveEnum(string udonType) => null;
        public bool IsEnumType(string udonType) => false;
        public CatalogTypeInfo GetTypeInfo(string udonType) => null;

        public ImplicitConversion GetImplicitConversion(string fromType, string toType)
        {
            return ImplicitConversion.Lookup(fromType, toType);
        }

        public IEnumerable<string> GetStaticTypeNames()
        {
            return Enumerable.Empty<string>();
        }

        private ExternSignature FindBestOverload(List<ExternSignature> overloads, string[] argTypes)
        {
            // Exact match first
            foreach (var sig in overloads)
            {
                if (sig.ParamTypes.Length == argTypes.Length)
                {
                    bool match = true;
                    for (int i = 0; i < argTypes.Length; i++)
                    {
                        if (sig.ParamTypes[i] != argTypes[i] &&
                            !TypeSystem.IsAssignable(sig.ParamTypes[i], argTypes[i]))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) return sig;
                }
            }
            return null;
        }
    }
}
