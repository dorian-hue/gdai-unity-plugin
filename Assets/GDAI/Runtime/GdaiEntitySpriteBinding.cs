using UnityEngine;

namespace GDAI.Bridge
{
    /// <summary>
    /// DOWNSTREAM-BUILD-2 · Data marker linking a scene object to an imported GDAI entity
    /// asset. Pure data (no logic, no editor dependency) so Layer B / Jason tooling can
    /// find bound objects by stable ids instead of display-name guessing.
    ///
    /// Set by editor tooling (GdaiAssetBindingUtility preview path today; semantic role
    /// binding is a future task). Safe to ship in runtime builds — it holds strings only.
    /// </summary>
    public class GdaiEntitySpriteBinding : MonoBehaviour
    {
        [Tooltip("world_entities.id this sprite belongs to (stable lineage key).")]
        public string entityId;

        [Tooltip("Imported asset id (entity_assets row id).")]
        public string assetId;

        [Tooltip("Display name of the world entity at import time (informational only — do NOT match by name).")]
        public string worldEntityName;

        [Tooltip("Asset role from the bundle manifest (e.g. entity_sprite). Semantic gameplay roles (player/enemy) are NOT expressed here yet.")]
        public string role;
    }
}
