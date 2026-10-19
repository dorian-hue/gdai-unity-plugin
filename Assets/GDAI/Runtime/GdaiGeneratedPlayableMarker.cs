// =====================================================================================
// GDAI Unity Plugin · AUTO-0Q-P2 · §8.2 · Minimal ownership marker (Runtime).
//
// The ONLY runtime type this task adds. Stamped by the playable composer onto the
// canonical scene objects it creates and onto the enemy-prefab root, so a later
// Sync can recognise "GDAI created this" and safely re-touch it — and, crucially,
// so the composer NEVER adopts a same-named human object that lacks this marker
// (a Player the user made by hand is not GDAI-owned). Not a governance platform:
// it carries only the profile id and the source snapshot for provenance.
// =====================================================================================
using UnityEngine;

namespace GDAI.Runtime
{
    /// <summary>Marks a GameObject as owned by the GDAI playable composer.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // hidden from the Add Component menu — composer-managed only
    public sealed class GdaiGeneratedPlayableMarker : MonoBehaviour
    {
        [Tooltip("Playable profile that created this object (e.g. unity.pointer_action_demo.v1).")]
        public string profileId;

        [Tooltip("Coherent snapshot id this object was materialized from.")]
        public string snapshotId;

        [Tooltip("Logical role of this object within the playable contract (player, enemy, manager, camera, controller).")]
        public string ownedRole;
    }
}
