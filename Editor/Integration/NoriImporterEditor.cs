#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Nori
{
    [CustomEditor(typeof(NoriImporter))]
    public class NoriImporterEditor : ScriptedImporterEditor
    {
        private bool _showUasm;
        private Vector2 _sourceScrollPos;
        private Vector2 _uasmScrollPos;

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

            // Buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Recompile"))
            {
                AssetDatabase.ImportAsset(importer.assetPath, ImportAssetOptions.ForceUpdate);
            }
            if (GUILayout.Button("Open in Editor"))
            {
                AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<Object>(importer.assetPath));
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

            _sourceScrollPos = EditorGUILayout.BeginScrollView(_sourceScrollPos,
                GUILayout.MaxHeight(300));
            var lines = source.Split('\n');
            int maxLines = Mathf.Min(lines.Length, 20);
            var style = new GUIStyle(EditorStyles.label)
            {
                font = Font.CreateDynamicFontFromOSFont("Consolas", 12),
                wordWrap = false,
                richText = false,
            };

            for (int i = 0; i < maxLines; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{i + 1,4}", GUILayout.Width(40));
                EditorGUILayout.SelectableLabel(lines[i].TrimEnd('\r'),
                    style, GUILayout.Height(16));
                EditorGUILayout.EndHorizontal();
            }

            if (lines.Length > 20)
                EditorGUILayout.LabelField($"  ... ({lines.Length - 20} more lines)");

            EditorGUILayout.EndScrollView();

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

            // Debug: Generated assembly
            EditorGUILayout.Space(10);
            _showUasm = EditorGUILayout.Foldout(_showUasm, "Generated Udon Assembly (Debug)");
            if (_showUasm)
            {
                // Try to load the generated assembly
                string uasmPath = importer.assetPath;
                var subAssets = AssetDatabase.LoadAllAssetsAtPath(uasmPath);
                string uasmText = null;
                foreach (var asset in subAssets)
                {
                    if (asset is TextAsset ta && ta.name == "Generated Assembly")
                    {
                        uasmText = ta.text;
                        break;
                    }
                }

                if (uasmText != null)
                {
                    _uasmScrollPos = EditorGUILayout.BeginScrollView(_uasmScrollPos,
                        GUILayout.MaxHeight(400));
                    EditorGUILayout.TextArea(uasmText, EditorStyles.textArea);
                    EditorGUILayout.EndScrollView();

                    if (GUILayout.Button("Copy to Clipboard", GUILayout.Width(140)))
                    {
                        EditorGUIUtility.systemCopyBuffer = uasmText;
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("No generated assembly available.", MessageType.None);
                }
            }

            EditorGUILayout.Space(5);
            ApplyRevertGUI();
        }
    }
}
#endif
