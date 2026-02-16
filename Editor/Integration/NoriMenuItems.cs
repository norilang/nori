#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Nori
{
    public static class NoriMenuItems
    {
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

        [MenuItem("Tools/Nori/About Nori")]
        public static void ShowAbout()
        {
            EditorUtility.DisplayDialog(
                "About Nori",
                "Nori Language v0.1.0\n\n" +
                "A clear, friendly programming language\nfor VRChat worlds.\n\n" +
                "Compiles to Udon Assembly.\n\n" +
                "https://nori-lang.dev\n" +
                "https://github.com/nori-lang/nori",
                "OK");
        }
    }
}
#endif
