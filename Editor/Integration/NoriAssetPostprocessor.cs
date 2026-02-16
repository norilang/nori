#if UNITY_EDITOR
using System.IO;
using UnityEditor;

namespace Nori
{
    /// <summary>
    /// Manages the lifecycle of companion .nori.asset files.
    /// When a .nori file is deleted, moved, or renamed, the companion follows.
    /// </summary>
    public class NoriAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            // Handle deleted .nori files: remove companion .asset
            foreach (string deleted in deletedAssets)
            {
                if (!deleted.EndsWith(".nori"))
                    continue;

                string companionPath = NoriImporter.GetCompanionAssetPath(deleted);
                if (File.Exists(companionPath))
                {
                    AssetDatabase.DeleteAsset(companionPath);
                }
            }

            // Handle moved/renamed .nori files: move companion .asset
            for (int i = 0; i < movedAssets.Length; i++)
            {
                string newPath = movedAssets[i];
                string oldPath = movedFromAssetPaths[i];

                if (!oldPath.EndsWith(".nori"))
                    continue;

                string oldCompanion = NoriImporter.GetCompanionAssetPath(oldPath);
                string newCompanion = NoriImporter.GetCompanionAssetPath(newPath);

                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(oldCompanion) != null)
                {
                    AssetDatabase.MoveAsset(oldCompanion, newCompanion);

                    // Update the display name to match the new filename
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.ScriptableObject>(newCompanion);
                    if (asset != null)
                    {
                        asset.name = Path.GetFileName(newPath);
                        EditorUtility.SetDirty(asset);
                    }
                }
            }
        }
    }
}
#endif
