#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Nori
{
    public class NoriSettingsProvider : SettingsProvider
    {
        public NoriSettingsProvider()
            : base("Project/Nori", SettingsScope.Project)
        {
        }

        public override void OnGUI(string searchContext)
        {
            var settings = NoriSettings.instance;
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Nori Compiler Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Auto-compile toggle
            EditorGUI.BeginChangeCheck();
            bool autoCompile = EditorGUILayout.Toggle(
                new GUIContent("Auto-Compile on Save",
                    "Automatically compile .nori files when they are saved or modified."),
                settings.AutoCompile);
            if (EditorGUI.EndChangeCheck())
                settings.AutoCompile = autoCompile;

            // Verbose diagnostics
            EditorGUI.BeginChangeCheck();
            bool verbose = EditorGUILayout.Toggle(
                new GUIContent("Verbose Diagnostics",
                    "Show extended explanations in error messages."),
                settings.VerboseDiagnostics);
            if (EditorGUI.EndChangeCheck())
                settings.VerboseDiagnostics = verbose;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Extern Catalog", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Catalog path
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            string path = EditorGUILayout.TextField(
                new GUIContent("Catalog Path",
                    "Path to an extern catalog JSON file. Leave empty to use the builtin catalog."),
                settings.ExternCatalogPath);
            if (EditorGUI.EndChangeCheck())
                settings.ExternCatalogPath = path;

            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string selected = EditorUtility.OpenFilePanel(
                    "Select Extern Catalog", "Assets", "json");
                if (!string.IsNullOrEmpty(selected))
                    settings.ExternCatalogPath = selected;
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Generate Catalog from VRC SDK", GUILayout.Width(250)))
            {
                CatalogScraper.Generate();
            }

            if (string.IsNullOrEmpty(settings.ExternCatalogPath))
            {
                EditorGUILayout.HelpBox(
                    "Using builtin catalog (limited API coverage). " +
                    "Click 'Generate Catalog from VRC SDK' for full API coverage.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(20);
            EditorGUILayout.LabelField("About", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Nori Language v0.1.0");
            EditorGUILayout.LabelField("A clear, friendly programming language for VRChat worlds.");

            if (GUILayout.Button("Open Documentation", GUILayout.Width(160)))
            {
                Application.OpenURL("https://nori-lang.dev");
            }
        }

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new NoriSettingsProvider
            {
                keywords = new[] { "nori", "compiler", "udon", "vrchat", "language" }
            };
        }
    }
}
#endif
