using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("dev.nori.compiler.editor")]

namespace Nori
{
    /// <summary>
    /// Generic NoriBehaviour that can load any .nori script via a TextAsset field.
    /// Add Component > Nori > Nori Script, then drag any .nori file into the inspector.
    /// </summary>
    [AddComponentMenu("Nori/Nori Script")]
    public class NoriScript : NoriBehaviour
    {
        [SerializeField] internal TextAsset _noriSource;

        [HideInInspector]
        [SerializeField] private string _noriScriptPath = "";

        public override string NoriScriptPath => _noriScriptPath;

        public override string DisplayName =>
            _noriSource != null ? _noriSource.name : "Nori Script (unassigned)";

        /// <summary>
        /// Called by the custom editor to update the script path when the TextAsset changes.
        /// </summary>
        internal void SetScriptPath(string path)
        {
            _noriScriptPath = path ?? "";
        }
    }
}
