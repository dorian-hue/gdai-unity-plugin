using UnityEngine;

namespace GDAI.Bridge
{
    /// <summary>
    /// A3-UNITY-CONSUME-LONGRUN-0B · C2 · Data marker on editor-generated Scene Assembly objects
    /// (root / spawn / bounds / blocker). Lets the plugin find & upsert its OWN objects by stable
    /// kind+entity id instead of name guessing, and prune stale ones without touching hand-authored
    /// objects. Pure data (strings + int) — safe to ship in runtime builds. Set by editor tooling
    /// (GdaiSceneAssemblySpawnMarkers / ...ArenaBounds / ...EdgeBlockers).
    /// </summary>
    public class GdaiSceneAssemblyMarker : MonoBehaviour
    {
        [Tooltip("What this object is: root / player_spawn / enemy_spawn / arena_bounds / blocker.")]
        public string kind;

        [Tooltip("Source entity_id (spawns/placements). Empty for root/bounds/derived blockers.")]
        public string entityId;

        [Tooltip("Semantic role from sceneAssembly (informational).")]
        public string role;

        [Tooltip("sceneAssembly.version this object was generated from.")]
        public int assemblyVersion;

        [Tooltip("Source file the data came from (Assets/GDAI_Generated/GDAI_SceneAssembly.json).")]
        public string sourcePath;

        // Editor-only visualization for spawn points (bounds/blockers draw their own gizmos).
        // OnDrawGizmos is an editor callback — never called in player builds.
        private void OnDrawGizmos()
        {
            if (kind == GdaiSceneAssemblyKind.PlayerSpawn || kind == GdaiSceneAssemblyKind.EnemySpawn)
            {
                Gizmos.color = kind == GdaiSceneAssemblyKind.PlayerSpawn
                    ? new Color(0.3f, 0.8f, 1f, 0.9f)
                    : new Color(1f, 0.45f, 0.3f, 0.9f);
                Vector3 p = transform.position;
                Gizmos.DrawWireSphere(p, 0.25f);
                Gizmos.DrawLine(p + Vector3.up * 0.35f, p - Vector3.up * 0.35f);
                Gizmos.DrawLine(p + Vector3.right * 0.35f, p - Vector3.right * 0.35f);
            }
        }
    }

    /// <summary>Canonical kind strings for GdaiSceneAssemblyMarker (shared runtime + editor).</summary>
    public static class GdaiSceneAssemblyKind
    {
        public const string Root = "root";
        public const string PlayerSpawn = "player_spawn";
        public const string EnemySpawn = "enemy_spawn";
        public const string ArenaBounds = "arena_bounds";
        public const string Blocker = "blocker";
    }
}
