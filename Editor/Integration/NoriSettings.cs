#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Nori
{
    [FilePath("ProjectSettings/NoriSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class NoriSettings : ScriptableSingleton<NoriSettings>
    {
        [SerializeField] private string _externCatalogPath = "";
        [SerializeField] private bool _autoCompile = true;
        [SerializeField] private bool _verboseDiagnostics;

        public string ExternCatalogPath
        {
            get => _externCatalogPath;
            set { _externCatalogPath = value; Save(true); }
        }

        public bool AutoCompile
        {
            get => _autoCompile;
            set { _autoCompile = value; Save(true); }
        }

        public bool VerboseDiagnostics
        {
            get => _verboseDiagnostics;
            set { _verboseDiagnostics = value; Save(true); }
        }

        public Compiler.IExternCatalog LoadCatalog()
        {
            // Try loading FullCatalog from JSON path
            if (!string.IsNullOrEmpty(_externCatalogPath) &&
                System.IO.File.Exists(_externCatalogPath))
            {
                try
                {
                    string json = System.IO.File.ReadAllText(_externCatalogPath);
                    var catalog = Compiler.FullCatalog.LoadFromJson(json);
                    if (catalog.ExternCount == 0)
                    {
                        Debug.LogWarning($"[Nori] Catalog at {_externCatalogPath} is empty (0 externs). Using FullCatalog with BuiltinCatalog fallback. Re-run Tools > Nori > Generate Extern Catalog.");
                    }
                    else
                    {
                        Debug.Log($"[Nori] Loaded extern catalog from {_externCatalogPath} ({catalog.ExternCount} externs)");
                    }
                    return catalog;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[Nori] Failed to load catalog from {_externCatalogPath}: {ex.Message}. Falling back to BuiltinCatalog.");
                }
            }

            return Compiler.BuiltinCatalog.Instance;
        }
    }
}
#endif
