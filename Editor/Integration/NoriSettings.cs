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
        [SerializeField] private string _catalogGeneratedFromDll = "";
        [SerializeField] private long _catalogGeneratedTimestamp;

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

        // Cached catalog to avoid re-reading JSON on every compile
        private Compiler.IExternCatalog _cachedCatalog;
        private string _cachedCatalogPath;

        public Compiler.IExternCatalog LoadCatalog()
        {
            // Return cached catalog if the path hasn't changed
            if (_cachedCatalog != null && _cachedCatalogPath == _externCatalogPath)
                return _cachedCatalog;

            _cachedCatalogPath = _externCatalogPath;

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
                    _cachedCatalog = catalog;
                    return catalog;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[Nori] Failed to load catalog from {_externCatalogPath}: {ex.Message}. Falling back to BuiltinCatalog.");
                }
            }

            _cachedCatalog = Compiler.BuiltinCatalog.Instance;
            return _cachedCatalog;
        }

        public void InvalidateCatalogCache()
        {
            _cachedCatalog = null;
            _cachedCatalogPath = null;
        }

        public string CatalogGeneratedFromDll
        {
            get => _catalogGeneratedFromDll;
            set { _catalogGeneratedFromDll = value; Save(true); }
        }

        public long CatalogGeneratedTimestamp
        {
            get => _catalogGeneratedTimestamp;
            set { _catalogGeneratedTimestamp = value; Save(true); }
        }

        public bool IsCatalogStale()
        {
            if (string.IsNullOrEmpty(_externCatalogPath) ||
                !System.IO.File.Exists(_externCatalogPath))
                return false;

            if (_catalogGeneratedTimestamp == 0)
                return true;

            if (string.IsNullOrEmpty(_catalogGeneratedFromDll) ||
                !System.IO.File.Exists(_catalogGeneratedFromDll))
                return true;

            long currentTicks = System.IO.File.GetLastWriteTimeUtc(_catalogGeneratedFromDll).Ticks;
            return currentTicks != _catalogGeneratedTimestamp;
        }
    }
}
#endif
