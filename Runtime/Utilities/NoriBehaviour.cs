using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Nori
{
    /// <summary>
    /// Abstract base class for Nori proxy MonoBehaviours.
    /// Each subclass represents a .nori script and auto-configures a hidden UdonBehaviour
    /// on the same GameObject with the correct compiled program source.
    /// </summary>
    public abstract class NoriBehaviour : MonoBehaviour
    {
        /// <summary>
        /// Package-relative path to the .nori source file.
        /// Example: "Packages/dev.nori.compiler/Runtime/Utilities/Synced/GlobalToggleObject.nori"
        /// </summary>
        public abstract string NoriScriptPath { get; }

        /// <summary>
        /// Human-readable display name shown in the inspector header.
        /// </summary>
        public abstract string DisplayName { get; }

        /// <summary>
        /// Serialized path to the generated companion .nori.asset file.
        /// Set automatically by the custom editor.
        /// </summary>
        [HideInInspector]
        public string companionAssetPath;

        /// <summary>
        /// Stores public variable overrides set in the inspector.
        /// Values persist here even when the UdonBehaviour's variable table
        /// is not yet available (before program assembly).
        /// </summary>
        [Serializable]
        internal class VarOverride
        {
            public string name;
            public string type;
            public bool isArray;
            public string serializedValue;
            public UnityEngine.Object objectReference;
            public List<UnityEngine.Object> objectReferences = new List<UnityEngine.Object>();
        }

        [SerializeField, HideInInspector]
        internal List<VarOverride> _varOverrides = new List<VarOverride>();

        // Reflection cache for SetProgramVariable
        private static Type _udonBehaviourType;
        private static System.Reflection.MethodInfo _setProgramVariableMethod;
        private static bool _runtimeReflectionCached;

        /// <summary>
        /// Pushes inspector variable overrides into the UdonBehaviour's program heap
        /// at runtime via SetProgramVariable. This runs in Start(), which is after
        /// UdonManager.OnSceneLoaded (programs are loaded) but before the first Update
        /// frame (where Udon _start events fire).
        /// </summary>
        protected virtual void Start()
        {
            if (_varOverrides == null || _varOverrides.Count == 0)
                return;

            if (!_runtimeReflectionCached)
            {
                _runtimeReflectionCached = true;
                _udonBehaviourType = Type.GetType("VRC.Udon.UdonBehaviour, VRC.Udon");
                if (_udonBehaviourType != null)
                {
                    // Find the non-generic SetProgramVariable(string, object)
                    foreach (var m in _udonBehaviourType.GetMethods(
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance))
                    {
                        if (m.Name == "SetProgramVariable" && !m.IsGenericMethod)
                        {
                            var parms = m.GetParameters();
                            if (parms.Length == 2 &&
                                parms[0].ParameterType == typeof(string))
                            {
                                _setProgramVariableMethod = m;
                                break;
                            }
                        }
                    }
                }
            }

            if (_udonBehaviourType == null || _setProgramVariableMethod == null)
                return;

            var udon = GetComponent(_udonBehaviourType);
            if (udon == null)
                return;

            foreach (var ov in _varOverrides)
            {
                object value = ResolveOverrideValue(ov);
                if (value != null)
                {
                    try
                    {
                        _setProgramVariableMethod.Invoke(udon,
                            new object[] { ov.name, value });
                    }
                    catch (Exception)
                    {
                        // Variable might not exist in the program — skip silently
                    }
                }
            }
        }

        private static object ResolveOverrideValue(VarOverride ov)
        {
            if (ov.isArray)
            {
                if (IsObjectRefType(ov.type))
                    return ToTypedArray(ov.type, ov.objectReferences);
                return DeserializeValueArray(ov.type, ov.serializedValue);
            }

            if (IsObjectRefType(ov.type))
                return ov.objectReference;

            return DeserializeValue(ov.type, ov.serializedValue);
        }

        private static bool IsObjectRefType(string typeName)
        {
            switch (typeName)
            {
                case "int": case "Int32":
                case "float": case "Single":
                case "double": case "Double":
                case "bool": case "Boolean":
                case "string": case "String":
                    return false;
                default:
                    return true;
            }
        }

        private static object DeserializeValue(string typeName, string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var ic = CultureInfo.InvariantCulture;
            try
            {
                switch (typeName)
                {
                    case "int": case "Int32": return int.Parse(s, ic);
                    case "float": case "Single": return float.Parse(s, ic);
                    case "double": case "Double": return double.Parse(s, ic);
                    case "bool": case "Boolean": return bool.Parse(s);
                    case "string": case "String": return s;
                    default: return null;
                }
            }
            catch { return null; }
        }

        private static object DeserializeValueArray(string elemType, string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var parts = s.Split('\n');
            var ic = CultureInfo.InvariantCulture;
            switch (elemType)
            {
                case "string": case "String": return parts;
                case "int": case "Int32":
                    var ia = new int[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                        int.TryParse(parts[i], NumberStyles.Any, ic, out ia[i]);
                    return ia;
                case "float": case "Single":
                    var fa = new float[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                        float.TryParse(parts[i], NumberStyles.Any, ic, out fa[i]);
                    return fa;
                case "bool": case "Boolean":
                    var ba = new bool[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                        bool.TryParse(parts[i], out ba[i]);
                    return ba;
                default: return null;
            }
        }

        private static object ToTypedArray(string elemType, List<UnityEngine.Object> list)
        {
            if (list == null || list.Count == 0) return null;
            switch (elemType)
            {
                case "GameObject":
                    var ga = new GameObject[list.Count];
                    for (int i = 0; i < list.Count; i++)
                        ga[i] = list[i] as GameObject;
                    return ga;
                case "Transform":
                    var ta = new Transform[list.Count];
                    for (int i = 0; i < list.Count; i++)
                        ta[i] = list[i] as Transform;
                    return ta;
                default:
                    var oa = new UnityEngine.Object[list.Count];
                    for (int i = 0; i < list.Count; i++)
                        oa[i] = list[i];
                    return oa;
            }
        }
    }
}
