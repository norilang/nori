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
    [ScriptedImporter(1, "nori")]
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

            // Try to create UdonAssemblyProgramAsset via reflection
            TryCreateUdonProgram(ctx, result.Uasm);
        }

        private void TryCreateUdonProgram(AssetImportContext ctx, string uasm)
        {
            EnsureReflectionCache();

            if (_programAssetType == null)
            {
                // VRChat SDK not installed
                return;
            }

            try
            {
                var programAsset = ScriptableObject.CreateInstance(_programAssetType);

                // Set the assembly source text via SerializedObject
                var serializedObj = new SerializedObject(programAsset);
                var udonAssemblyProp = serializedObj.FindProperty("udonAssembly");
                if (udonAssemblyProp != null)
                {
                    udonAssemblyProp.stringValue = uasm;
                    serializedObj.ApplyModifiedPropertiesWithoutUndo();
                }

                ctx.AddObjectToAsset("program", programAsset);

                // RefreshProgram calls AssetDatabase.CreateAsset/SaveAssets/Refresh internally,
                // which are restricted during asset importing. Defer to after import completes.
                string assetPath = ctx.assetPath;
                EditorApplication.delayCall += () => DeferredRefreshProgram(assetPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Nori] Failed to create Udon program asset: {e.Message}");
            }
        }

        private static void DeferredRefreshProgram(string assetPath)
        {
            if (!_reflectionCacheInitialized)
                EnsureReflectionCache();
            if (_programAssetType == null) return;

            try
            {
                var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                if (assets == null) return;

                foreach (var asset in assets)
                {
                    if (asset != null && _programAssetType.IsInstanceOfType(asset))
                    {
                        var refreshMethod = _programAssetType.GetMethod("RefreshProgram",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (refreshMethod != null)
                        {
                            refreshMethod.Invoke(asset, null);
                        }
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Nori] Deferred Udon program refresh failed: {e.Message}");
            }
        }
    }
}
#endif
