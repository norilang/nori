#if UNITY_EDITOR
using System;
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
        private static bool _cacheInitialized;

        private bool _showUasm;
        private bool _showDisassembly;
        private bool _showPublicVars = true;
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

            _tryGetVariableValueMethod = variableTableType.GetMethod("TryGetVariableValue",
                ReflectionBindingFlags.Public | ReflectionBindingFlags.Instance);
            _trySetVariableValueMethod = variableTableType.GetMethod("TrySetVariableValue",
                ReflectionBindingFlags.Public | ReflectionBindingFlags.Instance);
        }

        private void OnEnable()
        {
            var proxy = (NoriBehaviour)target;
            EnsureCache();
            EnsureVariableReflection();
            EnsureCompanionAsset(proxy);
            EnsureUdonBehaviour(proxy);
        }

        private void EnsureCompanionAsset(NoriBehaviour proxy)
        {
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
            else
            {
                EditorGUILayout.HelpBox("Compiled OK", MessageType.Info);
            }

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

            // Draw public variables from metadata (filtered, no __this_* vars)
            if (_udonBehaviour != null)
            {
                DrawPublicVariables(proxy);
            }

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

        private void DrawPublicVariables(NoriBehaviour proxy)
        {
            // Load metadata from the importer to know which vars are public
            var importer = AssetImporter.GetAtPath(proxy.NoriScriptPath) as NoriImporter;
            NoriCompileMetadata metadata = importer?.GetMetadata();

            if (metadata == null || metadata.PublicVars.Count == 0)
                return;

            EditorGUILayout.Space(5);
            _showPublicVars = EditorGUILayout.Foldout(_showPublicVars, "Public Variables");
            if (!_showPublicVars)
                return;

            if (_publicVariablesProperty == null)
            {
                // Fallback: just show variable names from metadata
                foreach (var v in metadata.PublicVars)
                    EditorGUILayout.LabelField($"  {v.Name}: {v.TypeName}");
                return;
            }

            var pubVars = _publicVariablesProperty.GetValue(_udonBehaviour);
            if (pubVars == null)
            {
                foreach (var v in metadata.PublicVars)
                    EditorGUILayout.LabelField($"  {v.Name}: {v.TypeName}");
                return;
            }

            EditorGUI.BeginChangeCheck();

            foreach (var varInfo in metadata.PublicVars)
            {
                // Skip internal variables
                if (varInfo.Name.StartsWith("__"))
                    continue;

                object currentValue = null;
                if (_tryGetVariableValueMethod != null)
                {
                    var args = new object[] { varInfo.Name, null };
                    bool found = (bool)_tryGetVariableValueMethod.Invoke(pubVars, args);
                    if (found)
                        currentValue = args[1];
                }

                object newValue = DrawVariableField(varInfo.Name, varInfo.TypeName, currentValue);

                if (newValue != currentValue && _trySetVariableValueMethod != null)
                {
                    Undo.RecordObject(_udonBehaviour, $"Change {varInfo.Name}");
                    _trySetVariableValueMethod.Invoke(pubVars,
                        new[] { varInfo.Name, newValue });
                    EditorUtility.SetDirty(_udonBehaviour);
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_udonBehaviour);
            }
        }

        private object DrawVariableField(string name, string typeName, object value)
        {
            string label = ObjectNames.NicifyVariableName(name);

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
                        value != null ? value.ToString() : "(null)");
                    return value;
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
