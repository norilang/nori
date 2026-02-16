#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Nori
{
    [CustomEditor(typeof(NoriImporter))]
    public class NoriImporterEditor : ScriptedImporterEditor
    {
        private Vector2 _sourceScrollPos;
        private bool _showUasm;
        private bool _showDisassembly;
        private Vector2 _uasmScrollPos;
        private Vector2 _disassemblyScrollPos;
        private string _cachedDisassembly;

        public override void OnInspectorGUI()
        {
            var importer = (NoriImporter)target;
            var metadata = importer.GetMetadata();

            // Compile status
            EditorGUILayout.Space(5);
            if (metadata != null && metadata.ErrorCount == 0)
            {
                EditorGUILayout.HelpBox("Compiled successfully.", MessageType.Info);
            }
            else if (metadata != null)
            {
                string msg = $"{metadata.ErrorCount} error(s)";
                if (metadata.WarningCount > 0)
                    msg += $", {metadata.WarningCount} warning(s)";
                EditorGUILayout.HelpBox(msg, MessageType.Error);
            }

            // Source Script link
            EditorGUI.BeginDisabledGroup(true);
            var sourceAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(importer.assetPath);
            EditorGUILayout.ObjectField("Source Script", sourceAsset, typeof(UnityEngine.Object), false);
            EditorGUI.EndDisabledGroup();

            // Buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Recompile"))
            {
                AssetDatabase.ImportAsset(importer.assetPath, ImportAssetOptions.ForceUpdate);
                _cachedDisassembly = null;
            }
            if (GUILayout.Button("Open in Editor"))
            {
                AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(importer.assetPath));
            }
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
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Source preview
            EditorGUILayout.LabelField("Source Preview", EditorStyles.boldLabel);
            string source = "";
            if (File.Exists(importer.assetPath))
            {
                source = File.ReadAllText(importer.assetPath);
            }

            var lines = source.Split('\n');
            int maxLines = Mathf.Min(lines.Length, 20);

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < maxLines; i++)
                sb.AppendLine($"{i + 1,4}  {lines[i].TrimEnd('\r')}");
            var style = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = false,
                richText = false,
            };
            float lineHeight = style.lineHeight > 0 ? style.lineHeight : EditorGUIUtility.singleLineHeight;
            float textHeight = lineHeight * maxLines + 10;
            _sourceScrollPos = EditorGUILayout.BeginScrollView(_sourceScrollPos,
                GUILayout.MaxHeight(300));
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextArea(sb.ToString(), style, GUILayout.Height(textHeight));
            EditorGUI.EndDisabledGroup();
            if (lines.Length > maxLines)
                EditorGUILayout.LabelField($"  ... ({lines.Length - maxLines} more lines)");
            EditorGUILayout.EndScrollView();

            // Companion program asset
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Program Asset", EditorStyles.boldLabel);

            string companionPath = NoriImporter.GetCompanionAssetPath(importer.assetPath);
            var companionAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(companionPath);

            if (companionAsset != null)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(companionAsset, typeof(UnityEngine.Object), false);
                if (GUILayout.Button("Ping", GUILayout.Width(50)))
                {
                    EditorGUIUtility.PingObject(companionAsset);
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No companion program asset found. Recompile to generate it.\n" +
                    "The VRChat SDK must be installed for program asset generation.",
                    MessageType.Warning);
            }

            // Declarations summary
            if (metadata != null)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Declarations", EditorStyles.boldLabel);

                if (metadata.PublicVars.Count > 0)
                {
                    EditorGUILayout.LabelField("Public Variables:", EditorStyles.miniLabel);
                    foreach (var v in metadata.PublicVars)
                        EditorGUILayout.LabelField($"  {v.Name}: {v.TypeName}");
                }

                if (metadata.SyncVars.Count > 0)
                {
                    EditorGUILayout.LabelField("Synced Variables:", EditorStyles.miniLabel);
                    foreach (var v in metadata.SyncVars)
                        EditorGUILayout.LabelField($"  sync {v.SyncMode} {v.Name}: {v.TypeName}");
                }

                if (metadata.Events.Count > 0)
                {
                    EditorGUILayout.LabelField("Events:", EditorStyles.miniLabel);
                    foreach (var e in metadata.Events)
                        EditorGUILayout.LabelField($"  on {e}");
                }

                if (metadata.CustomEvents.Count > 0)
                {
                    EditorGUILayout.LabelField("Custom Events:", EditorStyles.miniLabel);
                    foreach (var ce in metadata.CustomEvents)
                        EditorGUILayout.LabelField($"  event {ce}");
                }

                if (metadata.Functions.Count > 0)
                {
                    EditorGUILayout.LabelField("Functions:", EditorStyles.miniLabel);
                    foreach (var f in metadata.Functions)
                        EditorGUILayout.LabelField($"  fn {f}");
                }
            }

            // Compiled Nori Udon Assembly foldout
            EditorGUILayout.Space(10);
            _showUasm = EditorGUILayout.Foldout(_showUasm, "Compiled Nori Udon Assembly");
            if (_showUasm)
            {
                DrawUasm(companionPath);
            }

            // Program Disassembly foldout
            _showDisassembly = EditorGUILayout.Foldout(_showDisassembly, "Program Disassembly");
            if (_showDisassembly)
            {
                DrawDisassembly(companionPath);
            }

            EditorGUILayout.Space(5);
            ApplyRevertGUI();
        }

        private void DrawUasm(string companionPath)
        {
            if (string.IsNullOrEmpty(companionPath))
            {
                EditorGUILayout.HelpBox("No companion asset available.", MessageType.None);
                return;
            }

            var companion = AssetDatabase.LoadAssetAtPath<ScriptableObject>(companionPath);
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

        private void DrawDisassembly(string companionPath)
        {
            if (_cachedDisassembly == null)
            {
                try
                {
                    if (string.IsNullOrEmpty(companionPath))
                    {
                        EditorGUILayout.HelpBox(
                            "No companion program asset found.",
                            MessageType.None);
                        return;
                    }

                    var companionAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                        companionPath);
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
                            BindingFlags.Public | BindingFlags.Instance);
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
                            BindingFlags.Public | BindingFlags.Instance);
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
                        BindingFlags.Public | BindingFlags.Static);
                    var manager = instanceProp?.GetValue(null);
                    if (manager == null)
                    {
                        EditorGUILayout.HelpBox(
                            "UdonEditorManager.Instance is null.",
                            MessageType.None);
                        return;
                    }

                    var disassembleMethod = managerType.GetMethod("DisassembleProgram",
                        BindingFlags.Public | BindingFlags.Instance);
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
