#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Nori
{
    public static class NoriMenuItems
    {
        [InitializeOnLoadMethod]
        private static void CheckCatalogStaleness()
        {
            if (NoriSettings.instance.IsCatalogStale())
            {
                Debug.LogWarning(
                    "[Nori] The extern catalog may be outdated. The VRC SDK has been updated " +
                    "since the catalog was last generated. Regenerate it via " +
                    "Tools > Nori > Generate Extern Catalog or Project Settings > Nori.");
            }
        }

        [MenuItem("Tools/Nori/Compile All Nori Scripts")]
        public static void CompileAll()
        {
            string[] guids = AssetDatabase.FindAssets("", new[] { "Assets" });
            int count = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".nori"))
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    count++;
                }
            }

            if (count > 0)
                Debug.Log($"[Nori] Recompiled {count} Nori script(s).");
            else
                Debug.Log("[Nori] No .nori files found in Assets/.");
        }

        [MenuItem("Tools/Nori/Settings")]
        public static void OpenSettings()
        {
            SettingsService.OpenProjectSettings("Project/Nori");
        }

        [MenuItem("Tools/Nori/Setup Editor...")]
        public static void OpenEditorSetup()
        {
            EditorSetupWizard.ShowWindow();
        }

        [MenuItem("Assets/Create/Nori Script", false, 80)]
        public static void CreateNoriScript()
        {
            const string template =
                "on Start {\n" +
                "    log(\"Hello from Nori!\")\n" +
                "}\n" +
                "\n" +
                "on Interact {\n" +
                "    log(\"You clicked me!\")\n" +
                "}\n";

            ProjectWindowUtil.CreateAssetWithContent(
                "NewNoriScript.nori", template);
        }

        [MenuItem("Tools/Nori/About Nori")]
        public static void ShowAbout()
        {
            EditorUtility.DisplayDialog(
                "About Nori",
                "Nori Language v0.1.0\n\n" +
                "A clear, friendly programming language\nfor VRChat worlds.\n\n" +
                "Compiles to Udon Assembly.\n\n" +
                "https://nori-lang.dev\n" +
                "https://github.com/norilang/nori",
                "OK");
        }
    }
}
#endif
