using UnityEngine;

namespace Nori
{
    /// <summary>
    /// Synced toggle that any player can interact with.
    /// Toggles the assigned GameObject's active state for all players.
    /// </summary>
    [AddComponentMenu("Nori/Utilities/Global Toggle Object")]
    public class GlobalToggleObject : NoriBehaviour
    {
        public override string NoriScriptPath =>
            "Packages/dev.nori.compiler/Runtime/Utilities/Synced/GlobalToggleObject.nori";

        public override string DisplayName => "Global Toggle Object";
    }
}
