using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Stores public variable overrides set in the inspector.
        /// Values persist here even when the UdonBehaviour's variable table
        /// is not yet available (before program assembly).
        /// </summary>
        [Serializable]
        internal class VarOverride
        {
            public string name;
            public string type;
            public bool isArray;
            public string serializedValue;
            public UnityEngine.Object objectReference;
            public List<UnityEngine.Object> objectReferences = new List<UnityEngine.Object>();
        }

        [SerializeField, HideInInspector]
        internal List<VarOverride> _varOverrides = new List<VarOverride>();
    }
}
