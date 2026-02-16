using UnityEngine;

namespace Nori
{
    /// <summary>
    /// Synced toggle that only the instance master can interact with.
    /// Toggles the assigned GameObject's active state for all players.
    /// </summary>
    [AddComponentMenu("Nori/Utilities/Master Toggle Object")]
    public class MasterToggleObject : NoriBehaviour
    {
        public override string NoriScriptPath =>
            "Packages/dev.nori.compiler/Runtime/Utilities/Synced/MasterToggleObject.nori";

        public override string DisplayName => "Master Toggle Object";
    }
}
