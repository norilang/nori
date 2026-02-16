#if UNITY_EDITOR
using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using Nori.Compiler;
using ReflectionBindingFlags = System.Reflection.BindingFlags;
using ReflectionMethodInfo = System.Reflection.MethodInfo;
using ReflectionPropertyInfo = System.Reflection.PropertyInfo;

namespace Nori
{
    /// <summary>
    /// Custom editor for NoriBehaviour proxy components.
    /// Auto-configures a hidden UdonBehaviour with the correct compiled program source,
    /// and draws status, variable fields, and assembly/disassembly foldouts.
    /// </summary>
    [CustomEditor(typeof(NoriBehaviour), true)]
    public class NoriBehaviourEditor : Editor
    {
        // Reflection caches
        private static Type _udonBehaviourType;
        private static Type _programSourceType;
        private static Type _vrcPickupType;
        private static bool _cacheInitialized;

        private bool _showUasm;
        private bool _showDisassembly;
        private bool _showPublicVars = true;
        private bool _showDiagnostics = true;
        private Vector2 _uasmScrollPos;
        private Vector2 _disassemblyScrollPos;
        private string _cachedDisassembly;

        private Component _udonBehaviour;

        // Reflection caches for IUdonVariableTable
        private static ReflectionMethodInfo _tryGetVariableValueMethod;
        private static ReflectionMethodInfo _trySetVariableValueMethod;
        private static ReflectionPropertyInfo _publicVariablesProperty;
        private static ReflectionMethodInfo _getVariableNamesMethod;
        private static bool _variableReflectionInitialized;

        private static void EnsureCache()
        {
            if (_cacheInitialized) return;
            _cacheInitialized = true;

            _udonBehaviourType = Type.GetType("VRC.Udon.UdonBehaviour, VRC.Udon");
            _programSourceType = Type.GetType(
                "VRC.Udon.AbstractUdonProgramSource, VRC.Udon");
            _vrcPickupType = Type.GetType("VRC.SDK3.Components.VRCPickup, VRCSDK3")
                ?? Type.GetType("VRCSDK2.VRC_Pickup, VRC.SDKBase");
        }

        private static void EnsureVariableReflection()
        {
            if (_variableReflectionInitialized) return;
            _variableReflectionInitialized = true;

            if (_udonBehaviourType == null) return;

            _publicVariablesProperty = _udonBehaviourType.GetProperty("publicVariables",
                ReflectionBindingFlags.Public | ReflectionBindingFlags.Instance);

            // IUdonVariableTable methods
            var variableTableType = Type.GetType(
                "VRC.Udon.Common.Interfaces.IUdonVariableTable, VRC.Udon.Common");
            if (variableTableType == null) return;

            _getVariableNamesMethod = variableTableType.GetMethod("get_VariableSymbols",
                ReflectionBindingFlags.Public | ReflectionBindingFlags.Instance);
            if (_getVariableNamesMethod == null)
            {
                // Try property accessor
                var symbolsProp = variableTableType.GetProperty("VariableSymbols",
                    ReflectionBindingFlags.Public | ReflectionBindingFlags.Instance);
                _getVariableNamesMethod = symbolsProp?.GetGetMethod();
            }

            // TryGetVariableValue/TrySetVariableValue have generic overloads;
            // pick the non-generic (object) variants to avoid AmbiguousMatchException
            foreach (var m in variableTableType.GetMethods(
                ReflectionBindingFlags.Public | ReflectionBindingFlags.Instance))
            {
                if (m.Name == "TryGetVariableValue" && !m.IsGenericMethod)
                    _tryGetVariableValueMethod = m;
                else if (m.Name == "TrySetVariableValue" && !m.IsGenericMethod)
                    _trySetVariableValueMethod = m;
            }
        }

        private void OnEnable()
        {
            var proxy = (NoriBehaviour)target;
            EnsureCache();
            EnsureVariableReflection();

            if (proxy is NoriScript && !SyncNoriScriptPath(proxy as NoriScript))
                return;

            EnsureCompanionAsset(proxy);
            EnsureUdonBehaviour(proxy);

            EditorApplication.projectChanged += OnProjectChanged;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= OnProjectChanged;
        }

        private void OnProjectChanged()
        {
            _cachedDisassembly = null;
            var proxy = (NoriBehaviour)target;
            if (proxy is NoriScript ns)
                SyncNoriScriptPath(ns);
            EnsureCompanionAsset(proxy);
            EnsureUdonBehaviour(proxy);
            Repaint();
        }

        /// <summary>
        /// Syncs the NoriScript's internal path field from its TextAsset reference.
        /// Returns false if no valid .nori source is assigned.
        /// </summary>
        private bool SyncNoriScriptPath(NoriScript noriScript)
        {
            if (noriScript._noriSource == null)
            {
                if (!string.IsNullOrEmpty(noriScript.NoriScriptPath))
                {
                    Undo.RecordObject(noriScript, "Clear Nori script path");
                    noriScript.SetScriptPath("");
                    noriScript.companionAssetPath = "";
                    EditorUtility.SetDirty(noriScript);
                }
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(noriScript._noriSource);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".nori"))
                return false;

            if (assetPath != noriScript.NoriScriptPath)
            {
                Undo.RecordObject(noriScript, "Set Nori script path");
                noriScript.SetScriptPath(assetPath);
                noriScript.companionAssetPath = "";
                EditorUtility.SetDirty(noriScript);
            }

            return true;
        }

        private void EnsureCompanionAsset(NoriBehaviour proxy)
        {
            if (string.IsNullOrEmpty(proxy.NoriScriptPath))
                return;

            if (!string.IsNullOrEmpty(proxy.companionAssetPath))
            {
                var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                    proxy.companionAssetPath);
                if (existing != null)
                    return;
            }

            // Resolve companion path and trigger reimport if missing
            string companionPath = NoriImporter.GetCompanionAssetPath(proxy.NoriScriptPath);
            var companion = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(companionPath);

            if (companion == null)
            {
                // Trigger reimport to create the companion asset
                AssetDatabase.ImportAsset(proxy.NoriScriptPath,
                    ImportAssetOptions.ForceUpdate);
                companion = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(companionPath);
            }

            if (companion != null)
            {
                Undo.RecordObject(proxy, "Set companion asset path");
                proxy.companionAssetPath = companionPath;
                EditorUtility.SetDirty(proxy);
            }
        }

        private void EnsureUdonBehaviour(NoriBehaviour proxy)
        {
            if (string.IsNullOrEmpty(proxy.NoriScriptPath))
                return;

            if (_udonBehaviourType == null || _programSourceType == null)
                return;

            // Find existing UdonBehaviour on the same GameObject
            _udonBehaviour = proxy.GetComponent(_udonBehaviourType);

            if (_udonBehaviour == null)
            {
                _udonBehaviour = Undo.AddComponent(proxy.gameObject, _udonBehaviourType);
            }

            if (_udonBehaviour == null)
                return;

            // Set programSource to the companion .nori.asset
            if (!string.IsNullOrEmpty(proxy.companionAssetPath))
            {
                var companionAsset = AssetDatabase.LoadAssetAtPath(
                    proxy.companionAssetPath, _programSourceType);
                if (companionAsset != null)
                {
                    var so = new SerializedObject(_udonBehaviour);
                    var programSourceProp = so.FindProperty("programSource");
                    if (programSourceProp != null &&
                        programSourceProp.objectReferenceValue != companionAsset)
                    {
                        programSourceProp.objectReferenceValue = companionAsset;
                        so.ApplyModifiedProperties();
                    }
                }
            }

            // Hide the UdonBehaviour in the inspector
            if ((_udonBehaviour.hideFlags & HideFlags.HideInInspector) == 0)
            {
                _udonBehaviour.hideFlags |= HideFlags.HideInInspector;
                EditorUtility.SetDirty(_udonBehaviour);
            }
        }

        public override void OnInspectorGUI()
        {
            var proxy = (NoriBehaviour)target;
            EnsureCache();

            // Header with display name and icon
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Packages/dev.nori.compiler/Editor/Resources/nori-icon.png");
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            if (icon != null)
            {
                GUILayout.Label(icon, GUILayout.Width(24), GUILayout.Height(24));
            }
            EditorGUILayout.LabelField(proxy.DisplayName, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            // NoriScript: TextAsset picker
            if (proxy is NoriScript noriScript)
            {
                EditorGUILayout.Space(3);
                EditorGUI.BeginChangeCheck();
                var newSource = (TextAsset)EditorGUILayout.ObjectField(
                    "Nori Source", noriScript._noriSource, typeof(TextAsset), false);
                if (EditorGUI.EndChangeCheck())
                {
                    // Validate .nori extension
                    if (newSource != null)
                    {
                        string path = AssetDatabase.GetAssetPath(newSource);
                        if (!path.EndsWith(".nori"))
                        {
                            Debug.LogWarning("[Nori] Only .nori files can be assigned to NoriScript.");
                            newSource = null;
                        }
                    }

                    Undo.RecordObject(noriScript, "Change Nori source");
                    noriScript._noriSource = newSource;
                    EditorUtility.SetDirty(noriScript);
                    _cachedDisassembly = null;

                    if (SyncNoriScriptPath(noriScript))
                    {
                        EnsureCompanionAsset(noriScript);
                        EnsureUdonBehaviour(noriScript);
                    }
                }

                if (noriScript._noriSource == null)
                {
                    EditorGUILayout.HelpBox(
                        "Drag a .nori file here to get started.",
                        MessageType.Info);
                    return;
                }
            }

            // Load metadata for status bar and diagnostics
            var importer = AssetImporter.GetAtPath(proxy.NoriScriptPath) as NoriImporter;
            NoriCompileMetadata metadata = importer?.GetMetadata();

            // Status bar
            EditorGUILayout.Space(3);
            if (_udonBehaviourType == null)
            {
                EditorGUILayout.HelpBox(
                    "VRChat SDK not found. Install the VRChat SDK to use Nori components.",
                    MessageType.Warning);
            }
            else if (_udonBehaviour == null)
            {
                EditorGUILayout.HelpBox(
                    "UdonBehaviour could not be created.",
                    MessageType.Error);
            }
            else if (string.IsNullOrEmpty(proxy.companionAssetPath) ||
                     AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                         proxy.companionAssetPath) == null)
            {
                EditorGUILayout.HelpBox(
                    "Compiled program not found. Click Recompile to generate it.",
                    MessageType.Warning);
            }
            else if (metadata != null && (metadata.ErrorCount > 0 || metadata.WarningCount > 0))
            {
                var parts = new System.Collections.Generic.List<string>();
                if (metadata.ErrorCount > 0)
                    parts.Add($"{metadata.ErrorCount} error{(metadata.ErrorCount != 1 ? "s" : "")}");
                if (metadata.WarningCount > 0)
                    parts.Add($"{metadata.WarningCount} warning{(metadata.WarningCount != 1 ? "s" : "")}");
                EditorGUILayout.HelpBox(string.Join(", ", parts),
                    metadata.ErrorCount > 0 ? MessageType.Error : MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("Compiled OK", MessageType.Info);
            }

            // Inline diagnostics foldout
            if (metadata != null && metadata.Diagnostics != null && metadata.Diagnostics.Count > 0)
            {
                _showDiagnostics = EditorGUILayout.Foldout(_showDiagnostics, "Diagnostics");
                if (_showDiagnostics)
                {
                    foreach (var diag in metadata.Diagnostics)
                    {
                        var msgType = diag.Severity == "error" ? MessageType.Error
                            : diag.Severity == "warning" ? MessageType.Warning
                            : MessageType.Info;
                        string text = $"[{diag.Code}] line {diag.Line}: {diag.Message}";
                        if (!string.IsNullOrEmpty(diag.Hint))
                            text += $"\n  hint: {diag.Hint}";
                        EditorGUILayout.HelpBox(text, msgType);
                    }
                }
            }

            // Missing component warnings
            DrawComponentWarnings(proxy, metadata);

            // Source Script link
            EditorGUILayout.Space(3);
            EditorGUI.BeginDisabledGroup(true);
            var sourceAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(proxy.NoriScriptPath);
            EditorGUILayout.ObjectField("Source Script", sourceAsset, typeof(UnityEngine.Object), false);
            EditorGUI.EndDisabledGroup();

            // Companion asset link + Recompile button
            EditorGUILayout.Space(3);
            EditorGUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(proxy.companionAssetPath))
            {
                var companion = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                    proxy.companionAssetPath);
                EditorGUILayout.ObjectField("Program Asset", companion,
                    typeof(UnityEngine.Object), false);
            }
            if (GUILayout.Button("Recompile", GUILayout.Width(80)))
            {
                AssetDatabase.ImportAsset(proxy.NoriScriptPath,
                    ImportAssetOptions.ForceUpdate);
                EnsureCompanionAsset(proxy);
                EnsureUdonBehaviour(proxy);
                _cachedDisassembly = null;
            }
            EditorGUILayout.EndHorizontal();

            // Compile All button
            if (GUILayout.Button("Compile All Nori Programs"))
            {
                string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { "Assets", "Packages" });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith(".nori"))
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }

            // Draw public variables from metadata
            DrawPublicVariables(proxy, metadata);

            // Compiled Nori Udon Assembly foldout
            EditorGUILayout.Space(10);
            _showUasm = EditorGUILayout.Foldout(_showUasm, "Compiled Nori Udon Assembly");
            if (_showUasm)
            {
                DrawUasm(proxy);
            }

            // Program Disassembly foldout
            _showDisassembly = EditorGUILayout.Foldout(_showDisassembly,
                "Program Disassembly");
            if (_showDisassembly)
            {
                DrawDisassembly(proxy);
            }
        }

        private void DrawComponentWarnings(NoriBehaviour proxy, NoriCompileMetadata metadata)
        {
            if (metadata == null || metadata.Events == null || metadata.Events.Count == 0)
                return;

            var events = metadata.Events;

            // Interact → needs any Collider
            if (events.Contains("Interact"))
            {
                if (proxy.GetComponent<Collider>() == null)
                {
                    EditorGUILayout.HelpBox(
                        "This script uses Interact but this GameObject has no Collider.",
                        MessageType.Warning);
                }
            }

            // Pickup events → needs VRC Pickup + Rigidbody
            bool hasPickupEvent = events.Contains("Pickup") || events.Contains("Drop")
                || events.Contains("PickupUseDown") || events.Contains("PickupUseUp");
            if (hasPickupEvent)
            {
                if (_vrcPickupType != null && proxy.GetComponent(_vrcPickupType) == null)
                {
                    EditorGUILayout.HelpBox(
                        "This script uses Pickup events but this GameObject has no VRC Pickup component.",
                        MessageType.Warning);
                }

                if (proxy.GetComponent<Rigidbody>() == null)
                {
                    EditorGUILayout.HelpBox(
                        "This script uses Pickup events but this GameObject has no Rigidbody.",
                        MessageType.Warning);
                }
            }

            // Trigger events → needs a Collider with isTrigger
            bool hasTriggerEvent = events.Contains("OnPlayerTriggerEnter")
                || events.Contains("OnPlayerTriggerExit")
                || events.Contains("TriggerEnter") || events.Contains("TriggerExit");
            if (hasTriggerEvent)
            {
                var colliders = proxy.GetComponents<Collider>();
                bool anyTrigger = false;
                foreach (var col in colliders)
                {
                    if (col.isTrigger)
                    {
                        anyTrigger = true;
                        break;
                    }
                }

                if (!anyTrigger)
                {
                    EditorGUILayout.HelpBox(
                        colliders.Length == 0
                            ? "This script uses trigger events but this GameObject has no Collider."
                            : "This script uses trigger events but no Collider on this GameObject has Is Trigger enabled.",
                        MessageType.Warning);
                }
            }
        }

        private void DrawPublicVariables(NoriBehaviour proxy)
        {
            DrawPublicVariables(proxy, null);
        }

        private void DrawPublicVariables(NoriBehaviour proxy, NoriCompileMetadata metadata)
        {
            // Load metadata from the importer if not provided
            if (metadata == null)
            {
                var importer = AssetImporter.GetAtPath(proxy.NoriScriptPath) as NoriImporter;
                metadata = importer?.GetMetadata();
            }

            if (metadata == null || metadata.PublicVars.Count == 0)
                return;

            EditorGUILayout.Space(5);
            _showPublicVars = EditorGUILayout.Foldout(_showPublicVars, "Public Variables");
            if (!_showPublicVars)
                return;

            // Try to get the UdonBehaviour's public variable table (may be null
            // if VRC hasn't assembled the program yet)
            object pubVars = null;
            if (_publicVariablesProperty != null && _udonBehaviour != null)
                pubVars = _publicVariablesProperty.GetValue(_udonBehaviour);

            foreach (var varInfo in metadata.PublicVars)
            {
                // Skip internal variables
                if (varInfo.Name.StartsWith("__"))
                    continue;

                // Read current value: override storage first, then UdonBehaviour table
                object currentValue;
                if (!TryGetOverrideValue(proxy, varInfo.Name, varInfo.TypeName,
                    varInfo.IsArray, out currentValue))
                {
                    currentValue = null;
                    if (pubVars != null && _tryGetVariableValueMethod != null)
                    {
                        var args = new object[] { varInfo.Name, null };
                        bool found = (bool)_tryGetVariableValueMethod.Invoke(pubVars, args);
                        if (found)
                            currentValue = args[1];
                    }
                }

                object newValue = DrawVariableField(varInfo.Name, varInfo.TypeName, currentValue,
                    varInfo.DocComment, varInfo.IsArray);

                if (!System.Object.Equals(newValue, currentValue))
                {
                    // Always persist to override storage on the proxy component
                    Undo.RecordObject(proxy, $"Change {varInfo.Name}");
                    SetOverrideValue(proxy, varInfo.Name, varInfo.TypeName,
                        varInfo.IsArray, newValue);
                    EditorUtility.SetDirty(proxy);

                    // Also push to UdonBehaviour if its variable table is available
                    if (pubVars != null && _trySetVariableValueMethod != null)
                    {
                        Undo.RecordObject(_udonBehaviour, $"Change {varInfo.Name}");
                        _trySetVariableValueMethod.Invoke(pubVars,
                            new[] { varInfo.Name, newValue });
                        EditorUtility.SetDirty(_udonBehaviour);
                    }
                }
            }

            // Push all overrides to UdonBehaviour when its table becomes available
            SyncOverridesToUdon(proxy, pubVars);
        }

        private bool TryGetOverrideValue(NoriBehaviour proxy, string name, string typeName,
            bool isArray, out object value)
        {
            foreach (var ov in proxy._varOverrides)
            {
                if (ov.name == name)
                {
                    if (isArray)
                    {
                        if (IsObjectReferenceType(typeName))
                            value = ov.objectReferences;
                        else
                            value = DeserializeValueArray(typeName, ov.serializedValue);
                    }
                    else if (IsObjectReferenceType(typeName))
                        value = ov.objectReference;
                    else
                        value = DeserializeValue(typeName, ov.serializedValue);
                    return true;
                }
            }
            value = null;
            return false;
        }

        private void SetOverrideValue(NoriBehaviour proxy, string name, string typeName,
            bool isArray, object value)
        {
            NoriBehaviour.VarOverride existing = null;
            foreach (var ov in proxy._varOverrides)
            {
                if (ov.name == name)
                {
                    existing = ov;
                    break;
                }
            }

            if (existing == null)
            {
                existing = new NoriBehaviour.VarOverride
                    { name = name, type = typeName, isArray = isArray };
                proxy._varOverrides.Add(existing);
            }

            if (isArray)
            {
                if (IsObjectReferenceType(typeName))
                {
                    existing.objectReferences.Clear();
                    if (value is System.Collections.Generic.List<UnityEngine.Object> list)
                    {
                        existing.objectReferences.AddRange(list);
                    }
                }
                else
                {
                    existing.serializedValue = SerializeValueArray(typeName, value);
                }
            }
            else if (IsObjectReferenceType(typeName))
                existing.objectReference = value as UnityEngine.Object;
            else
                existing.serializedValue = SerializeValue(typeName, value);
        }

        private void SyncOverridesToUdon(NoriBehaviour proxy, object pubVars)
        {
            if (pubVars == null || _trySetVariableValueMethod == null)
                return;
            if (proxy._varOverrides.Count == 0)
                return;

            foreach (var ov in proxy._varOverrides)
            {
                object value;
                if (ov.isArray)
                {
                    if (IsObjectReferenceType(ov.type))
                        value = ConvertObjectListToArray(ov.type, ov.objectReferences);
                    else
                        value = ConvertValueListToArray(ov.type, ov.serializedValue);
                }
                else if (IsObjectReferenceType(ov.type))
                    value = ov.objectReference;
                else
                    value = DeserializeValue(ov.type, ov.serializedValue);

                _trySetVariableValueMethod.Invoke(pubVars, new[] { ov.name, value });
            }
        }

        private static object ConvertObjectListToArray(string elemType,
            System.Collections.Generic.List<UnityEngine.Object> list)
        {
            if (list == null) return null;
            switch (elemType)
            {
                case "GameObject":
                    var goArr = new GameObject[list.Count];
                    for (int i = 0; i < list.Count; i++)
                        goArr[i] = list[i] as GameObject;
                    return goArr;
                case "Transform":
                    var trArr = new Transform[list.Count];
                    for (int i = 0; i < list.Count; i++)
                        trArr[i] = list[i] as Transform;
                    return trArr;
                default:
                    var objArr = new UnityEngine.Object[list.Count];
                    for (int i = 0; i < list.Count; i++)
                        objArr[i] = list[i];
                    return objArr;
            }
        }

        private static object ConvertValueListToArray(string elemType, string serialized)
        {
            if (string.IsNullOrEmpty(serialized)) return null;
            var parts = serialized.Split(new[] { '\n' }, StringSplitOptions.None);
            var ic = CultureInfo.InvariantCulture;
            switch (elemType)
            {
                case "string": case "String":
                    return parts;
                case "int": case "Int32":
                    var intArr = new int[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                        int.TryParse(parts[i], NumberStyles.Any, ic, out intArr[i]);
                    return intArr;
                case "float": case "Single":
                    var fArr = new float[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                        float.TryParse(parts[i], NumberStyles.Any, ic, out fArr[i]);
                    return fArr;
                case "bool": case "Boolean":
                    var bArr = new bool[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                        bool.TryParse(parts[i], out bArr[i]);
                    return bArr;
                default:
                    return parts;
            }
        }

        private static bool IsObjectReferenceType(string typeName)
        {
            switch (typeName)
            {
                case "int": case "Int32":
                case "float": case "Single":
                case "double": case "Double":
                case "bool": case "Boolean":
                case "string": case "String":
                case "Vector2":
                case "Vector3":
                case "Quaternion":
                case "Color":
                    return false;
                default:
                    return true;
            }
        }

        private static string SerializeValue(string typeName, object value)
        {
            if (value == null) return null;
            var ic = CultureInfo.InvariantCulture;
            switch (typeName)
            {
                case "int": case "Int32":
                    return ((int)value).ToString(ic);
                case "float": case "Single":
                    return ((float)value).ToString("R", ic);
                case "double": case "Double":
                    return ((double)value).ToString("R", ic);
                case "bool": case "Boolean":
                    return ((bool)value).ToString();
                case "string": case "String":
                    return (string)value;
                case "Vector2":
                    var v2 = (Vector2)value;
                    return string.Format(ic, "{0:R},{1:R}", v2.x, v2.y);
                case "Vector3":
                    var v3 = (Vector3)value;
                    return string.Format(ic, "{0:R},{1:R},{2:R}", v3.x, v3.y, v3.z);
                case "Quaternion":
                    var q = (Quaternion)value;
                    return string.Format(ic, "{0:R},{1:R},{2:R},{3:R}", q.x, q.y, q.z, q.w);
                case "Color":
                    var c = (Color)value;
                    return string.Format(ic, "{0:R},{1:R},{2:R},{3:R}", c.r, c.g, c.b, c.a);
                default:
                    return value.ToString();
            }
        }

        private static object DeserializeValue(string typeName, string serialized)
        {
            if (string.IsNullOrEmpty(serialized)) return null;
            var ic = CultureInfo.InvariantCulture;
            try
            {
                switch (typeName)
                {
                    case "int": case "Int32":
                        return int.Parse(serialized, ic);
                    case "float": case "Single":
                        return float.Parse(serialized, ic);
                    case "double": case "Double":
                        return double.Parse(serialized, ic);
                    case "bool": case "Boolean":
                        return bool.Parse(serialized);
                    case "string": case "String":
                        return serialized;
                    case "Vector2":
                        var v2 = serialized.Split(',');
                        return new Vector2(
                            float.Parse(v2[0], ic), float.Parse(v2[1], ic));
                    case "Vector3":
                        var v3 = serialized.Split(',');
                        return new Vector3(
                            float.Parse(v3[0], ic), float.Parse(v3[1], ic),
                            float.Parse(v3[2], ic));
                    case "Quaternion":
                        var qp = serialized.Split(',');
                        return new Quaternion(
                            float.Parse(qp[0], ic), float.Parse(qp[1], ic),
                            float.Parse(qp[2], ic), float.Parse(qp[3], ic));
                    case "Color":
                        var cp = serialized.Split(',');
                        return new Color(
                            float.Parse(cp[0], ic), float.Parse(cp[1], ic),
                            float.Parse(cp[2], ic), float.Parse(cp[3], ic));
                    default:
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string SerializeValueArray(string elemType, object value)
        {
            if (value == null) return null;
            var ic = CultureInfo.InvariantCulture;
            if (value is string[] sArr)
                return string.Join("\n", sArr);
            if (value is int[] iArr)
            {
                var parts = new string[iArr.Length];
                for (int i = 0; i < iArr.Length; i++) parts[i] = iArr[i].ToString(ic);
                return string.Join("\n", parts);
            }
            if (value is float[] fArr)
            {
                var parts = new string[fArr.Length];
                for (int i = 0; i < fArr.Length; i++) parts[i] = fArr[i].ToString("R", ic);
                return string.Join("\n", parts);
            }
            if (value is bool[] bArr)
            {
                var parts = new string[bArr.Length];
                for (int i = 0; i < bArr.Length; i++) parts[i] = bArr[i].ToString();
                return string.Join("\n", parts);
            }
            return null;
        }

        private static object DeserializeValueArray(string elemType, string serialized)
        {
            if (string.IsNullOrEmpty(serialized)) return null;
            var parts = serialized.Split(new[] { '\n' }, StringSplitOptions.None);
            var ic = CultureInfo.InvariantCulture;
            switch (elemType)
            {
                case "string": case "String":
                    return parts;
                case "int": case "Int32":
                    var iArr = new int[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                        int.TryParse(parts[i], NumberStyles.Any, ic, out iArr[i]);
                    return iArr;
                case "float": case "Single":
                    var fArr = new float[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                        float.TryParse(parts[i], NumberStyles.Any, ic, out fArr[i]);
                    return fArr;
                case "bool": case "Boolean":
                    var bArr = new bool[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                        bool.TryParse(parts[i], out bArr[i]);
                    return bArr;
                default:
                    return null;
            }
        }

        private object DrawVariableField(string name, string typeName, object value,
            string docComment = null, bool isArray = false)
        {
            string labelText = ObjectNames.NicifyVariableName(name);
            var label = new GUIContent(labelText,
                !string.IsNullOrEmpty(docComment) ? docComment : null);

            if (isArray)
                return DrawArrayField(label, typeName, value);

            switch (typeName)
            {
                case "bool":
                case "Boolean":
                    return EditorGUILayout.Toggle(label, value is bool b && b);

                case "int":
                case "Int32":
                    return EditorGUILayout.IntField(label, value is int i ? i : 0);

                case "float":
                case "Single":
                    return EditorGUILayout.FloatField(label, value is float f ? f : 0f);

                case "double":
                case "Double":
                    return EditorGUILayout.DoubleField(label, value is double d ? d : 0.0);

                case "string":
                case "String":
                    return EditorGUILayout.TextField(label, value as string ?? "");

                case "Vector2":
                    return EditorGUILayout.Vector2Field(label,
                        value is Vector2 v2 ? v2 : Vector2.zero);

                case "Vector3":
                    return EditorGUILayout.Vector3Field(label,
                        value is Vector3 v3 ? v3 : Vector3.zero);

                case "Quaternion":
                    var q = value is Quaternion quat ? quat : Quaternion.identity;
                    var euler = EditorGUILayout.Vector3Field(label, q.eulerAngles);
                    return Quaternion.Euler(euler);

                case "Color":
                    return EditorGUILayout.ColorField(label,
                        value is Color c ? c : Color.white);

                case "GameObject":
                    return EditorGUILayout.ObjectField(label,
                        value as GameObject, typeof(GameObject), true);

                case "Transform":
                    return EditorGUILayout.ObjectField(label,
                        value as Transform, typeof(Transform), true);

                default:
                    // Try as UnityEngine.Object
                    if (value is UnityEngine.Object obj)
                    {
                        return EditorGUILayout.ObjectField(label,
                            obj, obj.GetType(), true);
                    }
                    // Generic display
                    EditorGUILayout.LabelField(label,
                        new GUIContent(value != null ? value.ToString() : "(null)"));
                    return value;
            }
        }

        private object DrawArrayField(GUIContent label, string elemType, object value)
        {
            bool isObjRef = IsObjectReferenceType(elemType);

            if (isObjRef)
            {
                var list = value as System.Collections.Generic.List<UnityEngine.Object>
                    ?? new System.Collections.Generic.List<UnityEngine.Object>();

                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                EditorGUI.indentLevel++;

                int newSize = EditorGUILayout.IntField("Size", list.Count);
                newSize = Mathf.Max(0, newSize);
                while (list.Count < newSize) list.Add(null);
                while (list.Count > newSize) list.RemoveAt(list.Count - 1);

                Type fieldType = ResolveUnityType(elemType);
                bool changed = false;
                for (int i = 0; i < list.Count; i++)
                {
                    var elem = EditorGUILayout.ObjectField(
                        $"Element {i}", list[i], fieldType, true);
                    if (elem != list[i]) { list[i] = elem; changed = true; }
                }

                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();

                // Return a new list instance if anything changed so Equals detects it
                return changed || newSize != (value as System.Collections.Generic.List<UnityEngine.Object>)?.Count
                    ? new System.Collections.Generic.List<UnityEngine.Object>(list)
                    : (object)list;
            }
            else
            {
                // Value-type arrays: string[], int[], float[], bool[]
                return DrawValueArrayField(label, elemType, value);
            }
        }

        private object DrawValueArrayField(GUIContent label, string elemType, object value)
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            switch (elemType)
            {
                case "string": case "String":
                {
                    var orig = value as string[] ?? new string[0];
                    var arr = (string[])orig.Clone();
                    int newSize = EditorGUILayout.IntField("Size", arr.Length);
                    newSize = Mathf.Max(0, newSize);
                    if (newSize != arr.Length) Array.Resize(ref arr, newSize);
                    bool changed = arr.Length != orig.Length;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        string prev = arr[i];
                        arr[i] = EditorGUILayout.TextField($"Element {i}", arr[i] ?? "");
                        if (arr[i] != prev) changed = true;
                    }
                    EditorGUI.indentLevel--;
                    EditorGUILayout.EndVertical();
                    return changed ? arr : value;
                }
                case "int": case "Int32":
                {
                    var orig = value as int[] ?? new int[0];
                    var arr = (int[])orig.Clone();
                    int newSize = EditorGUILayout.IntField("Size", arr.Length);
                    newSize = Mathf.Max(0, newSize);
                    if (newSize != arr.Length) Array.Resize(ref arr, newSize);
                    bool changed = arr.Length != orig.Length;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        int prev = arr[i];
                        arr[i] = EditorGUILayout.IntField($"Element {i}", arr[i]);
                        if (arr[i] != prev) changed = true;
                    }
                    EditorGUI.indentLevel--;
                    EditorGUILayout.EndVertical();
                    return changed ? arr : value;
                }
                case "float": case "Single":
                {
                    var orig = value as float[] ?? new float[0];
                    var arr = (float[])orig.Clone();
                    int newSize = EditorGUILayout.IntField("Size", arr.Length);
                    newSize = Mathf.Max(0, newSize);
                    if (newSize != arr.Length) Array.Resize(ref arr, newSize);
                    bool changed = arr.Length != orig.Length;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        float prev = arr[i];
                        arr[i] = EditorGUILayout.FloatField($"Element {i}", arr[i]);
                        if (arr[i] != prev) changed = true;
                    }
                    EditorGUI.indentLevel--;
                    EditorGUILayout.EndVertical();
                    return changed ? arr : value;
                }
                case "bool": case "Boolean":
                {
                    var orig = value as bool[] ?? new bool[0];
                    var arr = (bool[])orig.Clone();
                    int newSize = EditorGUILayout.IntField("Size", arr.Length);
                    newSize = Mathf.Max(0, newSize);
                    if (newSize != arr.Length) Array.Resize(ref arr, newSize);
                    bool changed = arr.Length != orig.Length;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        bool prev = arr[i];
                        arr[i] = EditorGUILayout.Toggle($"Element {i}", arr[i]);
                        if (arr[i] != prev) changed = true;
                    }
                    EditorGUI.indentLevel--;
                    EditorGUILayout.EndVertical();
                    return changed ? arr : value;
                }
                default:
                    EditorGUILayout.LabelField("(unsupported array element type)");
                    EditorGUI.indentLevel--;
                    EditorGUILayout.EndVertical();
                    return value;
            }
        }

        private static Type ResolveUnityType(string typeName)
        {
            switch (typeName)
            {
                case "GameObject": return typeof(GameObject);
                case "Transform": return typeof(Transform);
                case "Material": return typeof(Material);
                case "AudioClip": return typeof(AudioClip);
                case "Sprite": return typeof(Sprite);
                case "Texture2D": return typeof(Texture2D);
                default: return typeof(UnityEngine.Object);
            }
        }

        private void DrawUasm(NoriBehaviour proxy)
        {
            // Load UASM from the companion asset's udonAssembly property
            if (string.IsNullOrEmpty(proxy.companionAssetPath))
            {
                EditorGUILayout.HelpBox("No companion asset available.", MessageType.None);
                return;
            }

            var companion = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                proxy.companionAssetPath);
            if (companion == null)
            {
                EditorGUILayout.HelpBox("Companion asset not found.", MessageType.None);
                return;
            }

            var so = new SerializedObject(companion);
            var udonAssemblyProp = so.FindProperty("udonAssembly");
            string uasmText = udonAssemblyProp?.stringValue;

            if (string.IsNullOrEmpty(uasmText))
            {
                EditorGUILayout.HelpBox("No generated assembly available.", MessageType.None);
                return;
            }

            _uasmScrollPos = EditorGUILayout.BeginScrollView(_uasmScrollPos,
                GUILayout.MaxHeight(400));
            EditorGUILayout.TextArea(uasmText, EditorStyles.textArea);
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Copy to Clipboard", GUILayout.Width(140)))
            {
                EditorGUIUtility.systemCopyBuffer = uasmText;
            }
        }

        private void DrawDisassembly(NoriBehaviour proxy)
        {
            if (_cachedDisassembly == null)
            {
                try
                {
                    if (string.IsNullOrEmpty(proxy.companionAssetPath))
                    {
                        EditorGUILayout.HelpBox(
                            "No companion program asset found.",
                            MessageType.None);
                        return;
                    }

                    var companionAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                        proxy.companionAssetPath);
                    if (companionAsset == null)
                    {
                        EditorGUILayout.HelpBox(
                            "Companion asset not found. Recompile to generate it.",
                            MessageType.None);
                        return;
                    }

                    // Get SerializedProgramAsset via reflection
                    var serializedProgramProp = companionAsset.GetType()
                        .GetProperty("SerializedProgramAsset",
                            ReflectionBindingFlags.Public | ReflectionBindingFlags.Instance);
                    if (serializedProgramProp == null)
                    {
                        EditorGUILayout.HelpBox(
                            "Cannot read program — VRChat SDK types not found.",
                            MessageType.None);
                        return;
                    }

                    var serializedProgramAsset = serializedProgramProp.GetValue(companionAsset);
                    if (serializedProgramAsset == null)
                    {
                        EditorGUILayout.HelpBox(
                            "Program not yet assembled. Try recompiling.",
                            MessageType.None);
                        return;
                    }

                    var retrieveMethod = serializedProgramAsset.GetType()
                        .GetMethod("RetrieveProgram",
                            ReflectionBindingFlags.Public | ReflectionBindingFlags.Instance);
                    if (retrieveMethod == null)
                    {
                        EditorGUILayout.HelpBox(
                            "Cannot retrieve program from serialized asset.",
                            MessageType.None);
                        return;
                    }

                    var program = retrieveMethod.Invoke(serializedProgramAsset, null);
                    if (program == null)
                    {
                        EditorGUILayout.HelpBox(
                            "Program is null — try recompiling.",
                            MessageType.None);
                        return;
                    }

                    // UdonEditorManager.Instance.DisassembleProgram(program)
                    var managerType = Type.GetType(
                        "VRC.Udon.Editor.UdonEditorManager, VRC.Udon.Editor");
                    if (managerType == null)
                    {
                        EditorGUILayout.HelpBox(
                            "UdonEditorManager not found — VRChat SDK Editor assembly missing.",
                            MessageType.None);
                        return;
                    }

                    var instanceProp = managerType.GetProperty("Instance",
                        ReflectionBindingFlags.Public | ReflectionBindingFlags.Static);
                    var manager = instanceProp?.GetValue(null);
                    if (manager == null)
                    {
                        EditorGUILayout.HelpBox(
                            "UdonEditorManager.Instance is null.",
                            MessageType.None);
                        return;
                    }

                    var disassembleMethod = managerType.GetMethod("DisassembleProgram",
                        ReflectionBindingFlags.Public | ReflectionBindingFlags.Instance);
                    if (disassembleMethod == null)
                    {
                        EditorGUILayout.HelpBox(
                            "DisassembleProgram method not found.",
                            MessageType.None);
                        return;
                    }

                    var disassemblyResult = disassembleMethod.Invoke(manager,
                        new[] { program });
                    if (disassemblyResult is string s)
                        _cachedDisassembly = s;
                    else if (disassemblyResult is string[] arr)
                        _cachedDisassembly = string.Join("\n", arr);
                    else
                        _cachedDisassembly = disassemblyResult?.ToString();
                }
                catch (Exception e)
                {
                    _cachedDisassembly = null;
                    EditorGUILayout.HelpBox(
                        $"Failed to disassemble: {e.Message}",
                        MessageType.Warning);
                    return;
                }
            }

            if (string.IsNullOrEmpty(_cachedDisassembly))
            {
                EditorGUILayout.HelpBox("Disassembly is empty.", MessageType.None);
                return;
            }

            _disassemblyScrollPos = EditorGUILayout.BeginScrollView(_disassemblyScrollPos,
                GUILayout.MaxHeight(400));
            EditorGUILayout.TextArea(_cachedDisassembly, EditorStyles.textArea);
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Copy to Clipboard", GUILayout.Width(140)))
            {
                EditorGUIUtility.systemCopyBuffer = _cachedDisassembly;
            }
        }
    }
}
#endif
