#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using Nori.Compiler;

namespace Nori
{
    [ScriptedImporter(6, "nori")]
    public class NoriImporter : ScriptedImporter
    {
        // Cached reflection lookups
        private static Type _programAssetType;
        private static bool _reflectionCacheInitialized;

        [SerializeField] private string _serializedMetadataJson;

        public NoriCompileMetadata GetMetadata()
        {
            if (string.IsNullOrEmpty(_serializedMetadataJson))
                return null;
            return JsonUtility.FromJson<NoriCompileMetadata>(_serializedMetadataJson);
        }

        [InitializeOnLoadMethod]
        private static void ResetCache()
        {
            _reflectionCacheInitialized = false;
            _programAssetType = null;
        }

        // --- Project window custom icon overlay for companion .asset files ---

        private static HashSet<string> _companionGuids;
        private static Texture2D _companionIcon;

        [InitializeOnLoadMethod]
        private static void RegisterProjectBrowserIcon()
        {
            EditorApplication.projectWindowItemOnGUI -= OnProjectWindowItem;
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItem;
            _companionGuids = null; // Rebuild on next draw
        }

        private static void OnProjectWindowItem(string guid, Rect rect)
        {
            if (_companionGuids == null) RebuildCompanionGuids();
            if (!_companionGuids.Contains(guid)) return;

            if (_companionIcon == null)
            {
                _companionIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Packages/dev.nori.compiler/Editor/Resources/nori-icon.png");
                if (_companionIcon == null) return;
            }

            // List view: rect height ≈ 16, icon is a small square at the left
            // Grid/thumbnail view: rect is taller, icon fills the upper square area
            Rect iconRect;
            if (rect.height <= 20)
                iconRect = new Rect(rect.x, rect.y, rect.height, rect.height);
            else
                iconRect = new Rect(rect.x, rect.y, rect.width, rect.width);

            GUI.DrawTexture(iconRect, _companionIcon, ScaleMode.ScaleToFit);
        }

        private static void RebuildCompanionGuids()
        {
            _companionGuids = new HashSet<string>();

            // Companions alongside .nori files in Assets/
            if (Directory.Exists("Assets"))
            {
                foreach (string file in Directory.GetFiles("Assets", "*.nori", SearchOption.AllDirectories))
                {
                    string noriPath = file.Replace('\\', '/');
                    string companionPath = GetCompanionAssetPath(noriPath);
                    string g = AssetDatabase.AssetPathToGUID(companionPath);
                    if (!string.IsNullOrEmpty(g))
                        _companionGuids.Add(g);
                }
            }

            // Companions generated from package .nori files (always under this dir)
            string genDir = "Assets/Nori/Generated";
            if (Directory.Exists(genDir))
            {
                foreach (string file in Directory.GetFiles(genDir, "*.asset", SearchOption.AllDirectories))
                {
                    string assetPath = file.Replace('\\', '/');
                    string g = AssetDatabase.AssetPathToGUID(assetPath);
                    if (!string.IsNullOrEmpty(g))
                        _companionGuids.Add(g);
                }
            }
        }

        private static void EnsureReflectionCache()
        {
            if (_reflectionCacheInitialized) return;
            _reflectionCacheInitialized = true;

            _programAssetType = Type.GetType(
                "VRC.Udon.Editor.ProgramSources.UdonAssemblyProgramAsset, VRC.Udon.Editor");
        }

        /// <summary>
        /// Returns the companion .asset path for a given .nori file.
        /// For Assets/ paths: "Assets/Scripts/Timer.nori" → "Assets/Scripts/Timer.asset"
        /// For Packages/ paths: redirects to "Assets/Nori/Generated/{relative}.asset" since packages are read-only.
        /// </summary>
        public static string GetCompanionAssetPath(string noriPath)
        {
            // Strip .nori extension so companion is Timer.asset, not Timer.nori.asset
            string basePath = noriPath.EndsWith(".nori")
                ? noriPath.Substring(0, noriPath.Length - ".nori".Length)
                : noriPath;

            if (noriPath.StartsWith("Packages/"))
            {
                string relative = basePath.Substring("Packages/".Length);
                return "Assets/Nori/Generated/" + relative + ".asset";
            }
            return basePath + ".asset";
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            string source = File.ReadAllText(ctx.assetPath);

            var catalog = NoriSettings.instance.LoadCatalog();
            var result = NoriCompiler.Compile(source, ctx.assetPath, catalog);

            // Store metadata for inspector
            if (result.Metadata != null)
                _serializedMetadataJson = JsonUtility.ToJson(result.Metadata);

            // Report diagnostics in Unity-clickable format
            foreach (var diag in result.Diagnostics.All)
            {
                string location = $"{ctx.assetPath}({diag.Span.Start.Line},{diag.Span.Start.Column})";
                string msg = $"{location}: {diag.Severity.ToString().ToLowerInvariant()} {diag.Code}: {diag.Message}";

                if (NoriSettings.instance.VerboseDiagnostics && diag.Explanation != null)
                    msg += $"\n  {diag.Explanation}";
                if (diag.Hint != null)
                    msg += $"\n  hint: {diag.Hint}";

                if (diag.Severity == Severity.Error)
                    ctx.LogImportError(msg);
                else if (diag.Severity == Severity.Warning)
                    ctx.LogImportWarning(msg);
            }

            // Create source TextAsset as main object, with custom icon
            var sourceAsset = new TextAsset(source);
            sourceAsset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            var icon = LoadNoriIcon();
            if (icon != null)
                ctx.AddObjectToAsset("source", sourceAsset, icon);
            else
                ctx.AddObjectToAsset("source", sourceAsset);
            ctx.SetMainObject(sourceAsset);

            if (!result.Success)
                return;

            // Create a TextAsset with the generated assembly for debugging
            var uasmAsset = new TextAsset(result.Uasm);
            uasmAsset.name = "Generated Assembly";
            ctx.AddObjectToAsset("uasm", uasmAsset);

            // Schedule companion .asset creation/update after import completes
            string noriPath = ctx.assetPath;
            string uasm = result.Uasm;
            // Name must match the companion filename stem (e.g., "Timer" for "Timer.asset")
            // so Unity doesn't warn about a main-object/filename mismatch.
            string displayName = Path.GetFileNameWithoutExtension(noriPath);
            EditorApplication.delayCall += () => CreateOrUpdateCompanionAsset(noriPath, uasm, displayName);
        }

        /// <summary>
        /// Loads the nori icon from disk bytes. This avoids import-ordering issues
        /// where AssetDatabase.LoadAssetAtPath returns null during OnImportAsset.
        /// </summary>
        private static Texture2D LoadNoriIcon()
        {
            const string iconPackagePath = "Packages/dev.nori.compiler/Editor/Resources/nori-source-icon.png";
            string fullPath = Path.GetFullPath(iconPackagePath);
            if (!File.Exists(fullPath))
                return null;

            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(fullPath));
            tex.name = "nori-icon";
            return tex;
        }

        /// <summary>
        /// Creates or updates a standalone UdonAssemblyProgramAsset companion file.
        /// This runs as a deferred callback after the ScriptedImporter finishes.
        /// </summary>
        internal static void CreateOrUpdateCompanionAsset(string noriPath, string uasm, string displayName)
        {
            EnsureReflectionCache();

            if (_programAssetType == null)
                return; // VRChat SDK not installed

            string companionPath = GetCompanionAssetPath(noriPath);

            try
            {
                // Load existing or create new
                var existing = AssetDatabase.LoadAssetAtPath(companionPath, _programAssetType);
                ScriptableObject programAsset;

                if (existing != null)
                {
                    programAsset = (ScriptableObject)existing;
                }
                else
                {
                    // Ensure directory exists (needed for Packages/ redirected paths)
                    string dir = Path.GetDirectoryName(companionPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    programAsset = ScriptableObject.CreateInstance(_programAssetType);
                    AssetDatabase.CreateAsset(programAsset, companionPath);
                }

                // Set the display name
                programAsset.name = displayName;

                // Set the assembly source text via SerializedObject
                var serializedObj = new SerializedObject(programAsset);
                var udonAssemblyProp = serializedObj.FindProperty("udonAssembly");
                if (udonAssemblyProp != null)
                {
                    udonAssemblyProp.stringValue = uasm;
                    serializedObj.ApplyModifiedPropertiesWithoutUndo();
                }

                // Set the nori icon on the companion asset
                var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Packages/dev.nori.compiler/Editor/Resources/nori-icon.png");
                if (icon != null)
                    EditorGUIUtility.SetIconForObject(programAsset, icon);

                EditorUtility.SetDirty(programAsset);

                // Assemble the program: this triggers VRC's assembler which populates
                // serializedUdonProgramAsset and creates the binary in Assets/SerializedUdonPrograms/
                var refreshMethod = _programAssetType.GetMethod("RefreshProgram",
                    BindingFlags.Public | BindingFlags.Instance);
                if (refreshMethod != null)
                {
                    refreshMethod.Invoke(programAsset, null);
                }

                AssetDatabase.SaveAssets();
                _companionGuids = null; // Invalidate so project window picks up new companion
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Nori] Failed to create/update companion asset at '{companionPath}': {e.Message}");
            }
        }
    }
}
#endif
