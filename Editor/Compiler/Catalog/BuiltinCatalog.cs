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

        // Enums: udonType -> EnumTypeInfo
        private readonly Dictionary<string, EnumTypeInfo> _enums
            = new Dictionary<string, EnumTypeInfo>();

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
            RegisterVector2Operations();
            RegisterVector3Operations();
            RegisterQuaternionOperations();
            RegisterMathfMethods();
            RegisterColorOperations();
            RegisterRandomOperations();
            RegisterInputMethods();
            RegisterMaterialProperties();
            RegisterRendererProperties();
            RegisterRigidbodyProperties();
            RegisterConstantForceProperties();
            RegisterLineRendererOperations();
            RegisterComponentProperties();
            RegisterVRCObjectPoolMethods();
            RegisterVRCObjectSyncMethods();
            RegisterVRCAvatarPedestalMethods();
            RegisterVRCPickupProperties();
            RegisterVRCVideoPlayerMethods();
            RegisterVRCUrlInputFieldMethods();
            RegisterVRCStringDownloaderMethods();
            RegisterVRCImageDownloaderMethods();
            RegisterVRCDownloadInterfaces();
            RegisterUIProperties();
            RegisterUtilitiesMethods();
            RegisterEnums();
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
            // Uses UnityEngine.Object which has explicit op_Equality/op_Inequality (whitelisted in Udon).
            // System.Object does NOT define operator== as a method — it's a C# language feature.
            _knownTypes.Add("UnityEngineObject");
            AddOp(TokenKind.EqualsEquals, "UnityEngineObject",
                "UnityEngineObject.__op_Equality__UnityEngineObject_UnityEngineObject__SystemBoolean", "SystemBoolean");
            AddOp(TokenKind.BangEquals, "UnityEngineObject",
                "UnityEngineObject.__op_Inequality__UnityEngineObject_UnityEngineObject__SystemBoolean", "SystemBoolean");
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
                "SystemBoolean", "SystemObject", "UnityEngineGameObject", "VRCSDKBaseVRCPlayerApi",
                "UnityEngineVector3", "UnityEngineColor", "UnityEngineQuaternion",
                "UnityEngineMaterial", "UnityEngineLineRenderer", "UnityEngineComponent",
                "VRCSDKBaseVRCUrl" })
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

            // Transform inherits from Component — GetComponentsInChildren
            AddMethod("UnityEngineTransform", "GetComponentsInChildren", new ExternSignature(
                "UnityEngineComponent.__GetComponentsInChildren__SystemType__UnityEngineComponentArray",
                new[] { "SystemType" }, "UnityEngineComponentArray", true));
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

            AddProperty("UnityEngineGameObject", "name", "SystemString",
                "UnityEngineObject.__get_name__SystemString");

            // GetComponent(SystemType) → UnityEngineComponent
            AddMethod("UnityEngineGameObject", "GetComponent", new ExternSignature(
                "UnityEngineGameObject.__GetComponent__SystemType__UnityEngineComponent",
                new[] { "SystemType" }, "UnityEngineComponent", true));

            // GetComponentsInChildren(SystemType) → UnityEngineComponentArray
            AddMethod("UnityEngineGameObject", "GetComponentsInChildren", new ExternSignature(
                "UnityEngineComponent.__GetComponentsInChildren__SystemType__UnityEngineComponentArray",
                new[] { "SystemType" }, "UnityEngineComponentArray", true));
        }

        private void RegisterTimeProperties()
        {
            _knownTypes.Add("UnityEngineTime");

            AddStaticProperty("UnityEngineTime", "deltaTime", "SystemSingle",
                "UnityEngineTime.__get_deltaTime__SystemSingle");

            AddStaticProperty("UnityEngineTime", "time", "SystemSingle",
                "UnityEngineTime.__get_time__SystemSingle");

            AddStaticProperty("UnityEngineTime", "realtimeSinceStartup", "SystemSingle",
                "UnityEngineTime.__get_realtimeSinceStartup__SystemSingle");
        }

        private void RegisterNetworkingOperations()
        {
            _knownTypes.Add("VRCSDKBaseNetworking");

            AddStaticProperty("VRCSDKBaseNetworking", "LocalPlayer", "VRCSDKBaseVRCPlayerApi",
                "VRCSDKBaseNetworking.__get_LocalPlayer__VRCSDKBaseVRCPlayerApi");

            AddStaticMethod("VRCSDKBaseNetworking", "IsOwner", new ExternSignature(
                "VRCSDKBaseNetworking.__IsOwner__VRCSDKBaseVRCPlayerApi_UnityEngineGameObject__SystemBoolean",
                new[] { "VRCSDKBaseVRCPlayerApi", "UnityEngineGameObject" }, "SystemBoolean", false));

            AddStaticMethod("VRCSDKBaseNetworking", "SetOwner", new ExternSignature(
                "VRCSDKBaseNetworking.__SetOwner__VRCSDKBaseVRCPlayerApi_UnityEngineGameObject__SystemVoid",
                new[] { "VRCSDKBaseVRCPlayerApi", "UnityEngineGameObject" }, "SystemVoid", false));

            AddStaticMethod("VRCSDKBaseNetworking", "GetServerTimeInSeconds", new ExternSignature(
                "VRCSDKBaseNetworking.__GetServerTimeInSeconds__SystemDouble",
                new string[0], "SystemDouble", false));

            // IsOwner 1-arg overload (checks local player implicitly)
            AddStaticMethod("VRCSDKBaseNetworking", "IsOwner", new ExternSignature(
                "VRCSDKBaseNetworking.__IsOwner__UnityEngineGameObject__SystemBoolean",
                new[] { "UnityEngineGameObject" }, "SystemBoolean", false));

            AddStaticProperty("VRCSDKBaseNetworking", "IsMaster", "SystemBoolean",
                "VRCSDKBaseNetworking.__get_IsMaster__SystemBoolean");
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

            // Player movement methods
            AddMethod("VRCSDKBaseVRCPlayerApi", "SetJumpImpulse", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__SetJumpImpulse__SystemSingle__SystemVoid",
                new[] { "SystemSingle" }, "SystemVoid", true));
            AddMethod("VRCSDKBaseVRCPlayerApi", "SetWalkSpeed", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__SetWalkSpeed__SystemSingle__SystemVoid",
                new[] { "SystemSingle" }, "SystemVoid", true));
            AddMethod("VRCSDKBaseVRCPlayerApi", "SetRunSpeed", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__SetRunSpeed__SystemSingle__SystemVoid",
                new[] { "SystemSingle" }, "SystemVoid", true));
            AddMethod("VRCSDKBaseVRCPlayerApi", "SetStrafeSpeed", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__SetStrafeSpeed__SystemSingle__SystemVoid",
                new[] { "SystemSingle" }, "SystemVoid", true));
            AddMethod("VRCSDKBaseVRCPlayerApi", "SetGravityStrength", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__SetGravityStrength__SystemSingle__SystemVoid",
                new[] { "SystemSingle" }, "SystemVoid", true));
            AddMethod("VRCSDKBaseVRCPlayerApi", "IsPlayerGrounded", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__IsPlayerGrounded__SystemBoolean",
                new string[0], "SystemBoolean", true));
            AddMethod("VRCSDKBaseVRCPlayerApi", "GetVelocity", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__GetVelocity__UnityEngineVector3",
                new string[0], "UnityEngineVector3", true));
            AddMethod("VRCSDKBaseVRCPlayerApi", "SetVelocity", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__SetVelocity__UnityEngineVector3__SystemVoid",
                new[] { "UnityEngineVector3" }, "SystemVoid", true));

            // Tracking data
            AddMethod("VRCSDKBaseVRCPlayerApi", "GetTrackingData", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__GetTrackingData__VRCSDKBaseVRCPlayerApiTrackingDataType__VRCSDKBaseVRCPlayerApiTrackingData",
                new[] { "VRCSDKBaseVRCPlayerApiTrackingDataType" }, "VRCSDKBaseVRCPlayerApiTrackingData", true));

            // TrackingData properties
            _knownTypes.Add("VRCSDKBaseVRCPlayerApiTrackingData");
            AddProperty("VRCSDKBaseVRCPlayerApiTrackingData", "position", "UnityEngineVector3",
                "VRCSDKBaseVRCPlayerApiTrackingData.__get_position__UnityEngineVector3");
            AddProperty("VRCSDKBaseVRCPlayerApiTrackingData", "rotation", "UnityEngineQuaternion",
                "VRCSDKBaseVRCPlayerApiTrackingData.__get_rotation__UnityEngineQuaternion");

            // Haptic feedback
            AddMethod("VRCSDKBaseVRCPlayerApi", "PlayHapticEventInHand", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__PlayHapticEventInHand__VRCSDKBaseVRC_PickupPickupHand_SystemSingle_SystemSingle_SystemSingle__SystemVoid",
                new[] { "VRCSDKBaseVRC_PickupPickupHand", "SystemSingle", "SystemSingle", "SystemSingle" },
                "SystemVoid", true));

            // Audio settings
            AddMethod("VRCSDKBaseVRCPlayerApi", "SetVoiceDistanceFar", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__SetVoiceDistanceFar__SystemSingle__SystemVoid",
                new[] { "SystemSingle" }, "SystemVoid", true));
            AddMethod("VRCSDKBaseVRCPlayerApi", "SetAvatarAudioFarRadius", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__SetAvatarAudioFarRadius__SystemSingle__SystemVoid",
                new[] { "SystemSingle" }, "SystemVoid", true));

            // Station
            AddMethod("VRCSDKBaseVRCPlayerApi", "UseAttachedStation", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__UseAttachedStation__SystemVoid",
                new string[0], "SystemVoid", true));

            // Avatar scaling
            AddMethod("VRCSDKBaseVRCPlayerApi", "SetManualAvatarScalingAllowed", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__SetManualAvatarScalingAllowed__SystemBoolean__SystemVoid",
                new[] { "SystemBoolean" }, "SystemVoid", true));
            AddMethod("VRCSDKBaseVRCPlayerApi", "SetAvatarEyeHeightMinimumByMeters", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__SetAvatarEyeHeightMinimumByMeters__SystemSingle__SystemVoid",
                new[] { "SystemSingle" }, "SystemVoid", true));
            AddMethod("VRCSDKBaseVRCPlayerApi", "SetAvatarEyeHeightMaximumByMeters", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__SetAvatarEyeHeightMaximumByMeters__SystemSingle__SystemVoid",
                new[] { "SystemSingle" }, "SystemVoid", true));
            AddMethod("VRCSDKBaseVRCPlayerApi", "GetAvatarEyeHeightAsMeters", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__GetAvatarEyeHeightAsMeters__SystemSingle",
                new string[0], "SystemSingle", true));
            AddMethod("VRCSDKBaseVRCPlayerApi", "SetAvatarEyeHeightByMeters", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__SetAvatarEyeHeightByMeters__SystemSingle__SystemVoid",
                new[] { "SystemSingle" }, "SystemVoid", true));

            // Networking 1-arg overload
            AddProperty("VRCSDKBaseVRCPlayerApi", "playerId", "SystemInt32",
                "VRCSDKBaseVRCPlayerApi.__get_playerId__SystemInt32");
        }

        private void RegisterUdonBehaviourMethods()
        {
            _knownTypes.Add("VRCUdonUdonBehaviour");
            _knownTypes.Add("VRCUdonCommonInterfacesIUdonEventReceiver");

            AddMethod("VRCUdonUdonBehaviour", "SendCustomEvent", new ExternSignature(
                "VRCUdonCommonInterfacesIUdonEventReceiver.__SendCustomEvent__SystemString__SystemVoid",
                new[] { "SystemString" }, "SystemVoid", true));

            AddMethod("VRCUdonCommonInterfacesIUdonEventReceiver", "SendCustomEvent",
                new ExternSignature(
                    "VRCUdonCommonInterfacesIUdonEventReceiver.__SendCustomEvent__SystemString__SystemVoid",
                    new[] { "SystemString" }, "SystemVoid", true));

            AddMethod("VRCUdonCommonInterfacesIUdonEventReceiver", "SendCustomNetworkEvent",
                new ExternSignature(
                    "VRCUdonCommonInterfacesIUdonEventReceiver.__SendCustomNetworkEvent__VRCSDKBaseNetworkingNetworkEventTarget_SystemString__SystemVoid",
                    new[] { "VRCSDKBaseNetworkingNetworkEventTarget", "SystemString" },
                    "SystemVoid", true));

            AddMethod("VRCUdonCommonInterfacesIUdonEventReceiver", "RequestSerialization",
                new ExternSignature(
                    "VRCUdonCommonInterfacesIUdonEventReceiver.__RequestSerialization__SystemVoid",
                    new string[0], "SystemVoid", true));
        }

        private void RegisterVector2Operations()
        {
            _knownTypes.Add("UnityEngineVector2");

            AddProperty("UnityEngineVector2", "x", "SystemSingle",
                "UnityEngineVector2.__get_x__SystemSingle", null);
            AddProperty("UnityEngineVector2", "y", "SystemSingle",
                "UnityEngineVector2.__get_y__SystemSingle", null);
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

            AddStaticMethod("UnityEngineQuaternion", "Euler", new ExternSignature(
                "UnityEngineQuaternion.__Euler__SystemSingle_SystemSingle_SystemSingle__UnityEngineQuaternion",
                new[] { "SystemSingle", "SystemSingle", "SystemSingle" },
                "UnityEngineQuaternion", false));
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

            AddStaticMethod("UnityEngineMathf", "Sin", new ExternSignature(
                "UnityEngineMathf.__Sin__SystemSingle__SystemSingle",
                new[] { "SystemSingle" }, "SystemSingle", false));

            AddStaticMethod("UnityEngineMathf", "Cos", new ExternSignature(
                "UnityEngineMathf.__Cos__SystemSingle__SystemSingle",
                new[] { "SystemSingle" }, "SystemSingle", false));
        }

        private void RegisterColorOperations()
        {
            _knownTypes.Add("UnityEngineColor");

            AddStaticMethod("UnityEngineColor", "LerpUnclamped", new ExternSignature(
                "UnityEngineColor.__LerpUnclamped__UnityEngineColor_UnityEngineColor_SystemSingle__UnityEngineColor",
                new[] { "UnityEngineColor", "UnityEngineColor", "SystemSingle" }, "UnityEngineColor", false));

            AddStaticMethod("UnityEngineColor", "Lerp", new ExternSignature(
                "UnityEngineColor.__Lerp__UnityEngineColor_UnityEngineColor_SystemSingle__UnityEngineColor",
                new[] { "UnityEngineColor", "UnityEngineColor", "SystemSingle" }, "UnityEngineColor", false));

            // Color constructor
            AddStaticMethod("UnityEngineColor", "ctor", new ExternSignature(
                "UnityEngineColor.__ctor__SystemSingle_SystemSingle_SystemSingle_SystemSingle__UnityEngineColor",
                new[] { "SystemSingle", "SystemSingle", "SystemSingle", "SystemSingle" }, "UnityEngineColor", false));

            // Color equality
            AddOp(TokenKind.EqualsEquals, "UnityEngineColor",
                "UnityEngineColor.__op_Equality__UnityEngineColor_UnityEngineColor__SystemBoolean", "SystemBoolean");
            AddOp(TokenKind.BangEquals, "UnityEngineColor",
                "UnityEngineColor.__op_Inequality__UnityEngineColor_UnityEngineColor__SystemBoolean", "SystemBoolean");
        }

        private void RegisterRandomOperations()
        {
            _knownTypes.Add("UnityEngineRandom");

            AddStaticProperty("UnityEngineRandom", "value", "SystemSingle",
                "UnityEngineRandom.__get_value__SystemSingle");
            AddStaticProperty("UnityEngineRandom", "insideUnitSphere", "UnityEngineVector3",
                "UnityEngineRandom.__get_insideUnitSphere__UnityEngineVector3");
            AddStaticProperty("UnityEngineRandom", "rotation", "UnityEngineQuaternion",
                "UnityEngineRandom.__get_rotation__UnityEngineQuaternion");

            // Range(int, int)
            AddStaticMethod("UnityEngineRandom", "Range", new ExternSignature(
                "UnityEngineRandom.__Range__SystemInt32_SystemInt32__SystemInt32",
                new[] { "SystemInt32", "SystemInt32" }, "SystemInt32", false));
            // Range(float, float)
            AddStaticMethod("UnityEngineRandom", "Range", new ExternSignature(
                "UnityEngineRandom.__Range__SystemSingle_SystemSingle__SystemSingle",
                new[] { "SystemSingle", "SystemSingle" }, "SystemSingle", false));
            // ColorHSV(float, float, float, float, float, float)
            AddStaticMethod("UnityEngineRandom", "ColorHSV", new ExternSignature(
                "UnityEngineRandom.__ColorHSV__SystemSingle_SystemSingle_SystemSingle_SystemSingle_SystemSingle_SystemSingle__UnityEngineColor",
                new[] { "SystemSingle", "SystemSingle", "SystemSingle", "SystemSingle", "SystemSingle", "SystemSingle" },
                "UnityEngineColor", false));
        }

        private void RegisterInputMethods()
        {
            _knownTypes.Add("UnityEngineInput");

            AddStaticMethod("UnityEngineInput", "GetKeyDown", new ExternSignature(
                "UnityEngineInput.__GetKeyDown__UnityEngineKeyCode__SystemBoolean",
                new[] { "UnityEngineKeyCode" }, "SystemBoolean", false));
            AddStaticMethod("UnityEngineInput", "GetKey", new ExternSignature(
                "UnityEngineInput.__GetKey__UnityEngineKeyCode__SystemBoolean",
                new[] { "UnityEngineKeyCode" }, "SystemBoolean", false));
            AddStaticMethod("UnityEngineInput", "GetKeyUp", new ExternSignature(
                "UnityEngineInput.__GetKeyUp__UnityEngineKeyCode__SystemBoolean",
                new[] { "UnityEngineKeyCode" }, "SystemBoolean", false));
        }

        private void RegisterMaterialProperties()
        {
            _knownTypes.Add("UnityEngineMaterial");

            AddProperty("UnityEngineMaterial", "color", "UnityEngineColor",
                "UnityEngineMaterial.__get_color__UnityEngineColor",
                "UnityEngineMaterial.__set_color__UnityEngineColor__SystemVoid");

            AddMethod("UnityEngineMaterial", "SetColor", new ExternSignature(
                "UnityEngineMaterial.__SetColor__SystemString_UnityEngineColor__SystemVoid",
                new[] { "SystemString", "UnityEngineColor" }, "SystemVoid", true));
        }

        private void RegisterRendererProperties()
        {
            _knownTypes.Add("UnityEngineRenderer");
            _knownTypes.Add("UnityEngineMeshRenderer");

            AddProperty("UnityEngineRenderer", "material", "UnityEngineMaterial",
                "UnityEngineRenderer.__get_material__UnityEngineMaterial",
                "UnityEngineRenderer.__set_material__UnityEngineMaterial__SystemVoid");

            AddProperty("UnityEngineMeshRenderer", "material", "UnityEngineMaterial",
                "UnityEngineRenderer.__get_material__UnityEngineMaterial",
                "UnityEngineRenderer.__set_material__UnityEngineMaterial__SystemVoid");
        }

        private void RegisterRigidbodyProperties()
        {
            _knownTypes.Add("UnityEngineRigidbody");

            AddProperty("UnityEngineRigidbody", "position", "UnityEngineVector3",
                "UnityEngineRigidbody.__get_position__UnityEngineVector3",
                "UnityEngineRigidbody.__set_position__UnityEngineVector3__SystemVoid");
            AddProperty("UnityEngineRigidbody", "rotation", "UnityEngineQuaternion",
                "UnityEngineRigidbody.__get_rotation__UnityEngineQuaternion",
                "UnityEngineRigidbody.__set_rotation__UnityEngineQuaternion__SystemVoid");
            AddProperty("UnityEngineRigidbody", "velocity", "UnityEngineVector3",
                "UnityEngineRigidbody.__get_velocity__UnityEngineVector3",
                "UnityEngineRigidbody.__set_velocity__UnityEngineVector3__SystemVoid");
            AddProperty("UnityEngineRigidbody", "angularVelocity", "UnityEngineVector3",
                "UnityEngineRigidbody.__get_angularVelocity__UnityEngineVector3",
                "UnityEngineRigidbody.__set_angularVelocity__UnityEngineVector3__SystemVoid");
        }

        private void RegisterConstantForceProperties()
        {
            _knownTypes.Add("UnityEngineConstantForce");

            AddProperty("UnityEngineConstantForce", "enabled", "SystemBoolean",
                "UnityEngineBehaviour.__get_enabled__SystemBoolean",
                "UnityEngineBehaviour.__set_enabled__SystemBoolean__SystemVoid");
        }

        private void RegisterLineRendererOperations()
        {
            _knownTypes.Add("UnityEngineLineRenderer");

            AddProperty("UnityEngineLineRenderer", "positionCount", "SystemInt32",
                "UnityEngineLineRenderer.__get_positionCount__SystemInt32",
                "UnityEngineLineRenderer.__set_positionCount__SystemInt32__SystemVoid");

            AddMethod("UnityEngineLineRenderer", "SetPosition", new ExternSignature(
                "UnityEngineLineRenderer.__SetPosition__SystemInt32_UnityEngineVector3__SystemVoid",
                new[] { "SystemInt32", "UnityEngineVector3" }, "SystemVoid", true));
            AddMethod("UnityEngineLineRenderer", "GetPositions", new ExternSignature(
                "UnityEngineLineRenderer.__GetPositions__UnityEngineVector3Array__SystemInt32",
                new[] { "UnityEngineVector3Array" }, "SystemInt32", true));
            AddMethod("UnityEngineLineRenderer", "SetPositions", new ExternSignature(
                "UnityEngineLineRenderer.__SetPositions__UnityEngineVector3Array__SystemVoid",
                new[] { "UnityEngineVector3Array" }, "SystemVoid", true));
            AddMethod("UnityEngineLineRenderer", "Simplify", new ExternSignature(
                "UnityEngineLineRenderer.__Simplify__SystemSingle__SystemVoid",
                new[] { "SystemSingle" }, "SystemVoid", true));

            // LineRenderer inherits from Component — register GetComponent
            AddMethod("UnityEngineLineRenderer", "GetComponent", new ExternSignature(
                "UnityEngineComponent.__GetComponent__SystemType__UnityEngineComponent",
                new[] { "SystemType" }, "UnityEngineComponent", true));

            AddProperty("UnityEngineLineRenderer", "gameObject", "UnityEngineGameObject",
                "UnityEngineComponent.__get_gameObject__UnityEngineGameObject");
        }

        private void RegisterComponentProperties()
        {
            _knownTypes.Add("UnityEngineComponent");

            AddProperty("UnityEngineComponent", "gameObject", "UnityEngineGameObject",
                "UnityEngineComponent.__get_gameObject__UnityEngineGameObject");
            AddProperty("UnityEngineComponent", "transform", "UnityEngineTransform",
                "UnityEngineComponent.__get_transform__UnityEngineTransform");

            AddMethod("UnityEngineComponent", "GetComponent", new ExternSignature(
                "UnityEngineComponent.__GetComponent__SystemType__UnityEngineComponent",
                new[] { "SystemType" }, "UnityEngineComponent", true));
            AddMethod("UnityEngineComponent", "GetComponentsInChildren", new ExternSignature(
                "UnityEngineComponent.__GetComponentsInChildren__SystemType__UnityEngineComponentArray",
                new[] { "SystemType" }, "UnityEngineComponentArray", true));
        }

        private void RegisterVRCObjectPoolMethods()
        {
            _knownTypes.Add("VRCSDK3ComponentsVRCObjectPool");

            AddMethod("VRCSDK3ComponentsVRCObjectPool", "TryToSpawn", new ExternSignature(
                "VRCSDK3ComponentsVRCObjectPool.__TryToSpawn__UnityEngineGameObject",
                new string[0], "UnityEngineGameObject", true));
            AddMethod("VRCSDK3ComponentsVRCObjectPool", "Return", new ExternSignature(
                "VRCSDK3ComponentsVRCObjectPool.__Return__UnityEngineGameObject__SystemVoid",
                new[] { "UnityEngineGameObject" }, "SystemVoid", true));
        }

        private void RegisterVRCObjectSyncMethods()
        {
            _knownTypes.Add("VRCSDK3ComponentsVRCObjectSync");

            AddMethod("VRCSDK3ComponentsVRCObjectSync", "Respawn", new ExternSignature(
                "VRCSDK3ComponentsVRCObjectSync.__Respawn__SystemVoid",
                new string[0], "SystemVoid", true));
        }

        private void RegisterVRCAvatarPedestalMethods()
        {
            _knownTypes.Add("VRCSDK3ComponentsVRCAvatarPedestal");

            AddMethod("VRCSDK3ComponentsVRCAvatarPedestal", "SetAvatarUse", new ExternSignature(
                "VRCSDK3ComponentsVRCAvatarPedestal.__SetAvatarUse__VRCSDKBaseVRCPlayerApi__SystemVoid",
                new[] { "VRCSDKBaseVRCPlayerApi" }, "SystemVoid", true));
        }

        private void RegisterVRCPickupProperties()
        {
            _knownTypes.Add("VRCSDK3ComponentsVRCPickup");

            AddProperty("VRCSDK3ComponentsVRCPickup", "IsHeld", "SystemBoolean",
                "VRCSDK3ComponentsVRCPickup.__get_IsHeld__SystemBoolean");
            AddProperty("VRCSDK3ComponentsVRCPickup", "gameObject", "UnityEngineGameObject",
                "UnityEngineComponent.__get_gameObject__UnityEngineGameObject");
        }

        private void RegisterVRCVideoPlayerMethods()
        {
            _knownTypes.Add("VRCSDK3VideoComponentsBaseBaseVRCVideoPlayer");

            AddMethod("VRCSDK3VideoComponentsBaseBaseVRCVideoPlayer", "PlayURL", new ExternSignature(
                "VRCSDK3VideoComponentsBaseBaseVRCVideoPlayer.__PlayURL__VRCSDKBaseVRCUrl__SystemVoid",
                new[] { "VRCSDKBaseVRCUrl" }, "SystemVoid", true));
            AddMethod("VRCSDK3VideoComponentsBaseBaseVRCVideoPlayer", "GetTime", new ExternSignature(
                "VRCSDK3VideoComponentsBaseBaseVRCVideoPlayer.__GetTime__SystemSingle",
                new string[0], "SystemSingle", true));
            AddMethod("VRCSDK3VideoComponentsBaseBaseVRCVideoPlayer", "SetTime", new ExternSignature(
                "VRCSDK3VideoComponentsBaseBaseVRCVideoPlayer.__SetTime__SystemSingle__SystemVoid",
                new[] { "SystemSingle" }, "SystemVoid", true));
        }

        private void RegisterVRCUrlInputFieldMethods()
        {
            _knownTypes.Add("VRCSDK3ComponentsVRCUrlInputField");

            AddMethod("VRCSDK3ComponentsVRCUrlInputField", "GetUrl", new ExternSignature(
                "VRCSDK3ComponentsVRCUrlInputField.__GetUrl__VRCSDKBaseVRCUrl",
                new string[0], "VRCSDKBaseVRCUrl", true));
        }

        private void RegisterVRCStringDownloaderMethods()
        {
            _knownTypes.Add("VRCSDKBaseVRC_StringDownloader");
            _knownTypes.Add("VRCSDKBaseVRCUrl");

            AddStaticMethod("VRCSDKBaseVRC_StringDownloader", "LoadUrl", new ExternSignature(
                "VRCSDKBaseVRC_StringDownloader.__LoadUrl__VRCSDKBaseVRCUrl_VRCUdonCommonInterfacesIUdonEventReceiver__SystemVoid",
                new[] { "VRCSDKBaseVRCUrl" }, "SystemVoid", false));
        }

        private void RegisterVRCImageDownloaderMethods()
        {
            _knownTypes.Add("VRCSDK3ImageVRCImageDownloader");
            _knownTypes.Add("VRCSDK3ImageTextureInfo");

            // Constructor
            AddStaticMethod("VRCSDK3ImageVRCImageDownloader", "ctor", new ExternSignature(
                "VRCSDK3ImageVRCImageDownloader.__ctor____VRCSDK3ImageVRCImageDownloader",
                new string[0], "VRCSDK3ImageVRCImageDownloader", false));

            AddMethod("VRCSDK3ImageVRCImageDownloader", "DownloadImage", new ExternSignature(
                "VRCSDK3ImageVRCImageDownloader.__DownloadImage__VRCSDKBaseVRCUrl_UnityEngineMaterial_VRCUdonCommonInterfacesIUdonEventReceiver_VRCSDK3ImageTextureInfo__SystemVoid",
                new[] { "VRCSDKBaseVRCUrl", "UnityEngineMaterial", "VRCSDK3ImageTextureInfo" },
                "SystemVoid", true));

            AddMethod("VRCSDK3ImageVRCImageDownloader", "Dispose", new ExternSignature(
                "VRCSDK3ImageVRCImageDownloader.__Dispose__SystemVoid",
                new string[0], "SystemVoid", true));
        }

        private void RegisterVRCDownloadInterfaces()
        {
            _knownTypes.Add("VRCSDK3StringLoadingIVRCStringDownload");
            _knownTypes.Add("VRCSDK3ImageIVRCImageDownload");

            // IVRCStringDownload properties
            AddProperty("VRCSDK3StringLoadingIVRCStringDownload", "Result", "SystemString",
                "VRCSDK3StringLoadingIVRCStringDownload.__get_Result__SystemString");
            AddProperty("VRCSDK3StringLoadingIVRCStringDownload", "Error", "SystemString",
                "VRCSDK3StringLoadingIVRCStringDownload.__get_Error__SystemString");
            AddProperty("VRCSDK3StringLoadingIVRCStringDownload", "ErrorCode", "SystemInt32",
                "VRCSDK3StringLoadingIVRCStringDownload.__get_ErrorCode__SystemInt32");

            // IVRCImageDownload properties
            AddProperty("VRCSDK3ImageIVRCImageDownload", "Error", "SystemObject",
                "VRCSDK3ImageIVRCImageDownload.__get_Error__SystemObject");
            AddProperty("VRCSDK3ImageIVRCImageDownload", "ErrorMessage", "SystemString",
                "VRCSDK3ImageIVRCImageDownload.__get_ErrorMessage__SystemString");
        }

        private void RegisterUIProperties()
        {
            _knownTypes.Add("UnityEngineUIText");
            _knownTypes.Add("UnityEngineUIToggle");
            _knownTypes.Add("UnityEngineUISlider");
            _knownTypes.Add("UnityEngineUIDropdown");
            _knownTypes.Add("UnityEngineUIInputField");

            AddProperty("UnityEngineUIText", "text", "SystemString",
                "UnityEngineUIText.__set_text__SystemString__SystemVoid" /* getter below */);
            // Fix: set proper getter/setter
            _properties[("UnityEngineUIText", "text")] = new PropertyInfo("SystemString",
                new ExternSignature("UnityEngineUIText.__get_text__SystemString", new string[0], "SystemString", true),
                new ExternSignature("UnityEngineUIText.__set_text__SystemString__SystemVoid", new[] { "SystemString" }, "SystemVoid", true));

            _properties[("UnityEngineUIToggle", "isOn")] = new PropertyInfo("SystemBoolean",
                new ExternSignature("UnityEngineUIToggle.__get_isOn__SystemBoolean", new string[0], "SystemBoolean", true),
                new ExternSignature("UnityEngineUIToggle.__set_isOn__SystemBoolean__SystemVoid", new[] { "SystemBoolean" }, "SystemVoid", true));

            _properties[("UnityEngineUISlider", "value")] = new PropertyInfo("SystemSingle",
                new ExternSignature("UnityEngineUISlider.__get_value__SystemSingle", new string[0], "SystemSingle", true),
                new ExternSignature("UnityEngineUISlider.__set_value__SystemSingle__SystemVoid", new[] { "SystemSingle" }, "SystemVoid", true));

            _properties[("UnityEngineUIDropdown", "value")] = new PropertyInfo("SystemInt32",
                new ExternSignature("UnityEngineUIDropdown.__get_value__SystemInt32", new string[0], "SystemInt32", true),
                new ExternSignature("UnityEngineUIDropdown.__set_value__SystemInt32__SystemVoid", new[] { "SystemInt32" }, "SystemVoid", true));

            _properties[("UnityEngineUIInputField", "text")] = new PropertyInfo("SystemString",
                new ExternSignature("UnityEngineUIInputField.__get_text__SystemString", new string[0], "SystemString", true),
                new ExternSignature("UnityEngineUIInputField.__set_text__SystemString__SystemVoid", new[] { "SystemString" }, "SystemVoid", true));

            // UI.Text array support
            _knownTypes.Add("UnityEngineUITextArray");
            AddProperty("UnityEngineUITextArray", "Length", "SystemInt32",
                "UnityEngineUITextArray.__get_Length__SystemInt32");
            AddMethod("UnityEngineUITextArray", "Get", new ExternSignature(
                "UnityEngineUITextArray.__Get__SystemInt32__UnityEngineUIText",
                new[] { "SystemInt32" }, "UnityEngineUIText", true));
            AddMethod("UnityEngineUITextArray", "Set", new ExternSignature(
                "UnityEngineUITextArray.__Set__SystemInt32_UnityEngineUIText__SystemVoid",
                new[] { "SystemInt32", "UnityEngineUIText" }, "SystemVoid", true));
        }

        private void RegisterUtilitiesMethods()
        {
            _knownTypes.Add("VRCSDKBaseUtilities");

            AddStaticMethod("VRCSDKBaseUtilities", "IsValid", new ExternSignature(
                "VRCSDKBaseUtilities.__IsValid__UnityEngineObject__SystemBoolean",
                new[] { "UnityEngineObject" }, "SystemBoolean", false));

            // VRCPlayerApi static methods
            AddStaticMethod("VRCSDKBaseVRCPlayerApi", "IsValid", new ExternSignature(
                "VRCSDKBaseUtilities.__IsValid__UnityEngineObject__SystemBoolean",
                new[] { "UnityEngineObject" }, "SystemBoolean", false));
            AddStaticMethod("VRCSDKBaseVRCPlayerApi", "GetPlayers", new ExternSignature(
                "VRCSDKBaseVRCPlayerApi.__GetPlayers__VRCSDKBaseVRCPlayerApiArray__VRCSDKBaseVRCPlayerApiArray",
                new[] { "VRCSDKBaseVRCPlayerApiArray" }, "VRCSDKBaseVRCPlayerApiArray", false));

            // String.Format overloads
            AddStaticMethod("SystemString", "Format", new ExternSignature(
                "SystemString.__Format__SystemString_SystemObject__SystemString",
                new[] { "SystemString", "SystemObject" }, "SystemString", false));
            AddStaticMethod("SystemString", "Format", new ExternSignature(
                "SystemString.__Format__SystemString_SystemObject_SystemObject__SystemString",
                new[] { "SystemString", "SystemObject", "SystemObject" }, "SystemString", false));
            AddStaticMethod("SystemString", "Format", new ExternSignature(
                "SystemString.__Format__SystemString_SystemObject_SystemObject_SystemObject__SystemString",
                new[] { "SystemString", "SystemObject", "SystemObject", "SystemObject" }, "SystemString", false));
        }

        private void RegisterEnums()
        {
            _enums["VRCSDKBaseVRCPlayerApiTrackingDataType"] = new EnumTypeInfo(
                "VRCSDKBaseVRCPlayerApiTrackingDataType", "SystemInt32",
                new Dictionary<string, int>
                {
                    ["Head"] = 0, ["LeftHand"] = 1, ["RightHand"] = 2, ["Origin"] = 3
                });

            _enums["VRCSDKBaseVRC_PickupPickupHand"] = new EnumTypeInfo(
                "VRCSDKBaseVRC_PickupPickupHand", "SystemInt32",
                new Dictionary<string, int>
                {
                    ["Left"] = 0, ["Right"] = 1
                });

            _enums["UnityEngineKeyCode"] = new EnumTypeInfo(
                "UnityEngineKeyCode", "SystemInt32",
                new Dictionary<string, int>
                {
                    ["None"] = 0, ["Backspace"] = 8, ["Tab"] = 9, ["Return"] = 13,
                    ["Escape"] = 27, ["Space"] = 32, ["Delete"] = 127,
                    ["Alpha0"] = 48, ["Alpha1"] = 49, ["Alpha2"] = 50, ["Alpha3"] = 51,
                    ["Alpha4"] = 52, ["Alpha5"] = 53, ["Alpha6"] = 54, ["Alpha7"] = 55,
                    ["Alpha8"] = 56, ["Alpha9"] = 57,
                    ["A"] = 97, ["B"] = 98, ["C"] = 99, ["D"] = 100, ["E"] = 101,
                    ["F"] = 102, ["G"] = 103, ["H"] = 104, ["I"] = 105, ["J"] = 106,
                    ["K"] = 107, ["L"] = 108, ["M"] = 109, ["N"] = 110, ["O"] = 111,
                    ["P"] = 112, ["Q"] = 113, ["R"] = 114, ["S"] = 115, ["T"] = 116,
                    ["U"] = 117, ["V"] = 118, ["W"] = 119, ["X"] = 120, ["Y"] = 121,
                    ["Z"] = 122, ["LeftShift"] = 304, ["RightShift"] = 303,
                    ["LeftControl"] = 306, ["RightControl"] = 305,
                });
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

            // String concatenation: string + <any> or <any> + string
            // Uses String.Concat(string, string) — the non-string side needs .ToString()
            if (op == TokenKind.Plus)
            {
                if (leftType == "SystemString" && rightType != "SystemString")
                {
                    return new OperatorInfo(
                        "SystemString.__Concat__SystemString_SystemString__SystemString",
                        "SystemString", "SystemString", "SystemString");
                }
                if (rightType == "SystemString" && leftType != "SystemString")
                {
                    return new OperatorInfo(
                        "SystemString.__Concat__SystemString_SystemString__SystemString",
                        "SystemString", "SystemString", "SystemString");
                }
            }

            // Object fallback for equality/inequality (null comparisons)
            // Any reference type reaching here legitimately needs UnityEngine.Object's operator==
            if (op == TokenKind.EqualsEquals || op == TokenKind.BangEquals)
            {
                if (_operators.TryGetValue((op, "UnityEngineObject", "UnityEngineObject"), out info))
                    return new OperatorInfo(info.Extern, info.ReturnType,
                        "UnityEngineObject", "UnityEngineObject");
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

        public EnumTypeInfo ResolveEnum(string udonType)
        {
            _enums.TryGetValue(udonType, out var info);
            return info;
        }

        public bool IsEnumType(string udonType) => _enums.ContainsKey(udonType);
        public CatalogTypeInfo GetTypeInfo(string udonType) => null;

        public ImplicitConversion GetImplicitConversion(string fromType, string toType)
        {
            return ImplicitConversion.Lookup(fromType, toType);
        }

        public IEnumerable<string> GetStaticTypeNames()
        {
            return Enumerable.Empty<string>();
        }

        public string GetClrTypeName(string udonType)
        {
            return TypeSystem.UdonTypeToClrName(udonType);
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
