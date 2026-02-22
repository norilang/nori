using System.Collections.Generic;

namespace Nori.Compiler
{
    public static class TypeSystem
    {
        private static readonly Dictionary<string, string> NoriToUdon = new Dictionary<string, string>
        {
            ["bool"] = "SystemBoolean",
            ["int"] = "SystemInt32",
            ["uint"] = "SystemUInt32",
            ["float"] = "SystemSingle",
            ["double"] = "SystemDouble",
            ["string"] = "SystemString",
            ["char"] = "SystemChar",
            ["object"] = "SystemObject",
            ["void"] = "SystemVoid",
            ["Vector2"] = "UnityEngineVector2",
            ["Vector3"] = "UnityEngineVector3",
            ["Vector4"] = "UnityEngineVector4",
            ["Quaternion"] = "UnityEngineQuaternion",
            ["Color"] = "UnityEngineColor",
            ["Color32"] = "UnityEngineColor32",
            ["Transform"] = "UnityEngineTransform",
            ["GameObject"] = "UnityEngineGameObject",
            ["Rigidbody"] = "UnityEngineRigidbody",
            ["Collider"] = "UnityEngineCollider",
            ["MeshRenderer"] = "UnityEngineMeshRenderer",
            ["AudioSource"] = "UnityEngineAudioSource",
            ["Animator"] = "UnityEngineAnimator",
            ["Player"] = "VRCSDKBaseVRCPlayerApi",
            ["Collision"] = "UnityEngineCollision",
            ["SerializationResult"] = "VRCSDKBaseVRCSerializationResult",
            ["UdonBehaviour"] = "VRCUdonUdonBehaviour",
            ["VRCPlayerApi"] = "VRCSDKBaseVRCPlayerApi",
            ["Material"] = "UnityEngineMaterial",
            ["LineRenderer"] = "UnityEngineLineRenderer",
            ["Renderer"] = "UnityEngineRenderer",
            ["ConstantForce"] = "UnityEngineConstantForce",
            ["ParticleSystem"] = "UnityEngineParticleSystem",

            // UI types (dotted names collapse: UI.Text -> UIText)
            ["UIText"] = "UnityEngineUIText",
            ["UIDropdown"] = "UnityEngineUIDropdown",
            ["UIInputField"] = "UnityEngineUIInputField",
            ["UIToggle"] = "UnityEngineUIToggle",
            ["UISlider"] = "UnityEngineUISlider",
            ["UIImage"] = "UnityEngineUIImage",
            ["Text"] = "UnityEngineUIText",

            // VRC types
            ["VRCObjectPool"] = "VRCSDK3ComponentsVRCObjectPool",
            ["VRCObjectSync"] = "VRCSDK3ComponentsVRCObjectSync",
            ["VRCAvatarPedestal"] = "VRCSDK3ComponentsVRCAvatarPedestal",
            ["VRCPickup"] = "VRCSDK3ComponentsVRCPickup",
            ["VRCVideoPlayer"] = "VRCSDK3VideoComponentsBaseBaseVRCVideoPlayer",
            ["VRCUrlInputField"] = "VRCSDK3ComponentsVRCUrlInputField",
            ["VRCUrl"] = "VRCSDKBaseVRCUrl",
            ["VRCImageDownloader"] = "VRCSDK3ImageVRCImageDownloader",
            ["TextureInfo"] = "VRCSDK3ImageTextureInfo",
            ["TrackingData"] = "VRCSDKBaseVRCPlayerApiTrackingData",
            ["IVRCStringDownload"] = "VRCSDK3StringLoadingIVRCStringDownload",
            ["IVRCImageDownload"] = "VRCSDK3ImageIVRCImageDownload",
            ["Component"] = "UnityEngineComponent",
            ["Behaviour"] = "UnityEngineBehaviour",

            // Enum types
            ["KeyCode"] = "UnityEngineKeyCode",
            ["TrackingDataType"] = "VRCSDKBaseVRCPlayerApiTrackingDataType",
            ["PickupHand"] = "VRCSDKBaseVRC_PickupPickupHand",
        };

        private static readonly Dictionary<string, string> UdonToNori = new Dictionary<string, string>();

        static TypeSystem()
        {
            foreach (var kv in NoriToUdon)
                UdonToNori[kv.Value] = kv.Key;
        }

        // Catalog reference for fallback type resolution (set by SemanticAnalyzer)
        internal static IExternCatalog Catalog { get; set; }

        public static string ResolveType(string noriType)
        {
            if (noriType == null) return null;

            // Array types
            if (noriType.EndsWith("[]"))
            {
                string elementType = noriType.Substring(0, noriType.Length - 2);
                string resolvedElement = ResolveType(elementType);
                return resolvedElement != null ? resolvedElement + "Array" : null;
            }

            if (NoriToUdon.TryGetValue(noriType, out var udon))
                return udon;

            // Handle dotted type names: UI.Text -> UnityEngineUIText
            if (noriType.Contains("."))
            {
                string collapsed = noriType.Replace(".", "");
                if (NoriToUdon.TryGetValue(collapsed, out var udon2))
                    return udon2;
            }

            // Fallback: try common namespace prefixes against the catalog
            if (Catalog != null)
            {
                string[] prefixes = { "UnityEngine", "System", "VRCSDKBase", "VRCSDKBaseVRC",
                    "VRCUdon", "UnityEngineUI", "VRCSDK3Components", "VRCSDK3Image",
                    "VRCSDK3StringLoading", "VRCSDK3VideoComponentsBase" };
                foreach (var prefix in prefixes)
                {
                    string candidate = prefix + noriType;
                    if (Catalog.IsKnownType(candidate))
                        return candidate;
                }

                // Also try the type name directly (e.g., already a UdonType like "UnityEngineSpace")
                if (Catalog.IsKnownType(noriType))
                    return noriType;
            }

            return null;
        }

        public static string ToNoriType(string udonType)
        {
            if (udonType == null) return null;

            if (udonType.EndsWith("Array"))
            {
                string elementUdon = udonType.Substring(0, udonType.Length - 5);
                string noriElement = ToNoriType(elementUdon);
                return noriElement != null ? noriElement + "[]" : udonType;
            }

            return UdonToNori.TryGetValue(udonType, out var nori) ? nori : udonType;
        }

        public static bool IsAssignable(string targetUdon, string sourceUdon)
        {
            if (targetUdon == sourceUdon) return true;
            if (targetUdon == "SystemObject") return true; // everything assignable to object
            if (targetUdon == "UnityEngineObject" && IsUnityOrVrcType(sourceUdon)) return true; // Unity/VRC → Object
            if (sourceUdon == "SystemInt32" && targetUdon == "SystemSingle") return true; // int -> float
            if (sourceUdon == "SystemInt32" && targetUdon == "SystemDouble") return true; // int -> double
            if (sourceUdon == "SystemSingle" && targetUdon == "SystemDouble") return true; // float -> double

            // Enum -> underlying type (int) and int -> enum
            if (Catalog != null)
            {
                if (Catalog.IsEnumType(sourceUdon) && targetUdon == "SystemInt32") return true;
                if (Catalog.IsEnumType(targetUdon) && sourceUdon == "SystemInt32") return true;
            }

            return false;
        }

        private static bool IsUnityOrVrcType(string udonType)
        {
            return udonType.StartsWith("UnityEngine") ||
                   udonType.StartsWith("VRCSDKBase") ||
                   udonType.StartsWith("VRCSDK3") ||
                   udonType.StartsWith("VRCUdon");
        }

        public static bool IsNumeric(string udonType)
        {
            return udonType == "SystemInt32" || udonType == "SystemUInt32" ||
                   udonType == "SystemSingle" || udonType == "SystemDouble";
        }

        public static bool IsKnownType(string noriType)
        {
            if (noriType.EndsWith("[]"))
                return IsKnownType(noriType.Substring(0, noriType.Length - 2));
            return NoriToUdon.ContainsKey(noriType);
        }

        /// <summary>
        /// Converts a Udon type name to its CLR type name for SystemType constants.
        /// E.g., "UnityEngineMeshRenderer" → "UnityEngine.MeshRenderer"
        /// </summary>
        public static string UdonTypeToClrName(string udonType)
        {
            if (udonType.StartsWith("UnityEngineUI"))
                return "UnityEngine.UI." + udonType.Substring("UnityEngineUI".Length);
            if (udonType.StartsWith("UnityEngine"))
                return "UnityEngine." + udonType.Substring("UnityEngine".Length);
            // VRCSDK3 prefixes must come before VRCSDKBase (both start with "VRC")
            if (udonType.StartsWith("VRCSDK3VideoComponentsBase"))
                return "VRC.SDK3.Video.Components.Base." + udonType.Substring("VRCSDK3VideoComponentsBase".Length);
            if (udonType.StartsWith("VRCSDK3StringLoading"))
                return "VRC.SDK3.StringLoading." + udonType.Substring("VRCSDK3StringLoading".Length);
            if (udonType.StartsWith("VRCSDK3Image"))
                return "VRC.SDK3.Image." + udonType.Substring("VRCSDK3Image".Length);
            if (udonType.StartsWith("VRCSDK3Components"))
                return "VRC.SDK3.Components." + udonType.Substring("VRCSDK3Components".Length);
            if (udonType.StartsWith("VRCSDK3Network"))
                return "VRC.SDK3.Network." + udonType.Substring("VRCSDK3Network".Length);
            if (udonType.StartsWith("VRCSDKBase"))
                return "VRC.SDKBase." + udonType.Substring("VRCSDKBase".Length);
            if (udonType.StartsWith("VRCUdon"))
                return "VRC.Udon." + udonType.Substring("VRCUdon".Length);
            if (udonType.StartsWith("TMPro"))
                return "TMPro." + udonType.Substring("TMPro".Length);
            if (udonType.StartsWith("System"))
                return "System." + udonType.Substring("System".Length);
            return udonType;
        }

        public static string DefaultValue(string udonType)
        {
            switch (udonType)
            {
                case "SystemInt32":
                case "SystemUInt32": return "0";
                case "SystemSingle": return "0";
                case "SystemDouble": return "0";
                default: return "null";
            }
        }

        public static bool IsStaticType(string name)
        {
            return name == "Time" || name == "Networking" || name == "Vector3" ||
                   name == "Vector2" || name == "Vector4" || name == "Quaternion" ||
                   name == "Color" || name == "Mathf" || name == "Physics" ||
                   name == "Random" || name == "Input" || name == "String" ||
                   name == "Utilities" || name == "VRCPlayerApi" ||
                   name == "VRCStringDownloader";
        }

        public static string GetStaticUdonType(string name)
        {
            switch (name)
            {
                case "Time": return "UnityEngineTime";
                case "Networking": return "VRCSDKBaseNetworking";
                case "Vector3": return "UnityEngineVector3";
                case "Vector2": return "UnityEngineVector2";
                case "Vector4": return "UnityEngineVector4";
                case "Quaternion": return "UnityEngineQuaternion";
                case "Color": return "UnityEngineColor";
                case "Mathf": return "UnityEngineMathf";
                case "Physics": return "UnityEnginePhysics";
                case "Random": return "UnityEngineRandom";
                case "Input": return "UnityEngineInput";
                case "String": return "SystemString";
                case "Utilities": return "VRCSDKBaseUtilities";
                case "VRCPlayerApi": return "VRCSDKBaseVRCPlayerApi";
                case "VRCStringDownloader": return "VRCSDKBaseVRC_StringDownloader";
                default: return null;
            }
        }
    }
}
