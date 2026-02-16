#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using Nori.Compiler;

namespace Nori
{
    [ScriptedImporter(2, "nori")]
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

        private static void EnsureReflectionCache()
        {
            if (_reflectionCacheInitialized) return;
            _reflectionCacheInitialized = true;

            _programAssetType = Type.GetType(
                "VRC.Udon.Editor.ProgramSources.UdonAssemblyProgramAsset, VRC.Udon.Editor");
        }

        /// <summary>
        /// Returns the companion .asset path for a given .nori file.
        /// Example: "Assets/Scripts/Timer.nori" → "Assets/Scripts/Timer.nori.asset"
        /// </summary>
        public static string GetCompanionAssetPath(string noriPath)
        {
            return noriPath + ".asset";
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

            // Create source TextAsset as main object
            var sourceAsset = new TextAsset(source);
            sourceAsset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
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
            // Name must match the companion filename stem (e.g., "Timer.nori" for "Timer.nori.asset")
            // so Unity doesn't warn about a main-object/filename mismatch.
            string displayName = Path.GetFileName(noriPath);
            EditorApplication.delayCall += () => CreateOrUpdateCompanionAsset(noriPath, uasm, displayName);
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
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Nori] Failed to create/update companion asset at '{companionPath}': {e.Message}");
            }
        }
    }
}
#endif
