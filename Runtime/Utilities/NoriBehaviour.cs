using UnityEngine;

namespace Nori
{
    /// <summary>
    /// Abstract base class for Nori proxy MonoBehaviours.
    /// Each subclass represents a .nori script and auto-configures a hidden UdonBehaviour
    /// on the same GameObject with the correct compiled program source.
    /// </summary>
    public abstract class NoriBehaviour : MonoBehaviour
    {
        /// <summary>
        /// Package-relative path to the .nori source file.
        /// Example: "Packages/dev.nori.compiler/Runtime/Utilities/Synced/GlobalToggleObject.nori"
        /// </summary>
        public abstract string NoriScriptPath { get; }

        /// <summary>
        /// Human-readable display name shown in the inspector header.
        /// </summary>
        public abstract string DisplayName { get; }

        /// <summary>
        /// Serialized path to the generated companion .nori.asset file.
        /// Set automatically by the custom editor.
        /// </summary>
        [HideInInspector]
        public string companionAssetPath;
    }
}
