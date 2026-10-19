using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GDAI.Bridge;   // GdaiSceneAssemblyMarker + GdaiSceneAssemblyKind

// =====================================================================================
// A3-UNITY-CONSUME-LONGRUN-0B · C2 · Deterministic spawn markers (Editor, Layer B).
//
// Creates editor-only spawn MARKER objects from sceneAssembly.spawns (does NOT move real
// Player/Enemy gameplay objects):
//   GDAI_SceneAssembly           (root; owns marker kind=root)
//     ├─ GDAI_PlayerSpawn
//     └─ GDAI_EnemySpawn_<id8>
// Positions come from the shared coordinate utility. No colliders, no layers/tags.
//
// Idempotency / ownership discipline (mirrors GdaiSceneBackgroundBinder, proven on main):
//   · find root by marker kind first, then name; create if missing.
//   · upsert children in place by (kind, entity_id); create missing.
//   · prune stale children ONLY if they carry our marker AND sit under our root.
//   · NEVER touch hand-authored objects (no marker / not under root).
//   · Undo supported · scene marked dirty but NEVER saved · refuses Play Mode.
//
// EnsureRoot / UpsertChild / FindChildrenByKind are public so C3 (bounds) and C4 (blockers)
// reuse the exact same root + ownership model.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiSceneAssemblySpawnMarkers
    {
        public const string RootName = "GDAI_SceneAssembly";
        public const string PlayerSpawnName = "GDAI_PlayerSpawn";
        public const string EnemySpawnPrefix = "GDAI_EnemySpawn_";

        public struct ApplyResult { public bool ok; public string dialog; }

        // ------------------------------ menu ------------------------------

        [MenuItem("GDAI/Scene · Place Spawn Markers")]
        public static void PlaceSpawnMarkersMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("GDAI · Place Spawn Markers",
                    "Stop Play Mode first — markers are placed in Edit Mode.", "OK");
                return;
            }
            var r = PlaceSpawnMarkers();
            EditorUtility.DisplayDialog("GDAI · Place Spawn Markers", r.dialog, "OK");
        }

        // ------------------------------ core ------------------------------

        public static ApplyResult PlaceSpawnMarkers()
        {
            if (!GdaiSceneAssemblyModels.TryLoad(out SceneAssemblyDto dto, out string err))
                return Fail(err);
            if (dto.arena == null || dto.arena.width <= 0f || dto.arena.height <= 0f)
                return Fail("arena missing/invalid — cannot place markers.");
            if (dto.spawns == null)
                return Fail("spawns array missing.");

            var root = EnsureRoot(dto);
            int created = 0, updated = 0;
            var keepNames = new HashSet<string>();

            foreach (var s in dto.spawns)
            {
                if (s == null || string.IsNullOrEmpty(s.role) || string.IsNullOrEmpty(s.entity_id)) continue;

                string name, kind;
                if (s.role == "player_spawn") { name = PlayerSpawnName; kind = GdaiSceneAssemblyKind.PlayerSpawn; }
                else if (s.role == "enemy_spawn") { name = EnemySpawnPrefix + GdaiSceneAssemblyModels.ShortId(s.entity_id); kind = GdaiSceneAssemblyKind.EnemySpawn; }
                else continue;

                keepNames.Add(name);
                var go = UpsertChild(root, name, kind, s.entity_id, s.role, dto, out bool wasCreated);
                Undo.RecordObject(go.transform, "GDAI · position spawn marker");
                go.transform.position = GdaiSceneAssemblyCoordinateUtility.CanvasToWorld(s.x, s.y, dto.arena.width, dto.arena.height);
                go.transform.rotation = Quaternion.identity;
                if (wasCreated) created++; else updated++;
            }

            int pruned = PruneStaleChildren(root, keepNames, GdaiSceneAssemblyKind.PlayerSpawn, GdaiSceneAssemblyKind.EnemySpawn);

            EditorSceneManager.MarkSceneDirty(root.scene);
            string msg = "Spawn markers: " + created + " created, " + updated + " updated, " + pruned + " stale removed.\n" +
                         "Under " + RootName + " · no colliders · scene marked dirty (not saved).";
            Debug.Log("[GDAI][Scene][SpawnMarkers] " + msg);
            return new ApplyResult { ok = true, dialog = msg };
        }

        // ------------------------------ shared helpers (reused by C3/C4) ------------------------------

        /// <summary>Find-or-create the GDAI_SceneAssembly root, marker kind=root. Undo-able.</summary>
        public static GameObject EnsureRoot(SceneAssemblyDto dto)
        {
            var existing = FindByKind(GdaiSceneAssemblyKind.Root, null);
            GameObject root = existing != null ? existing : GameObject.Find(RootName);
            if (root == null)
            {
                root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, "GDAI · create scene assembly root");
            }
            else if (root.name != RootName)
            {
                Undo.RecordObject(root, "GDAI · rename scene assembly root");
                root.name = RootName;
            }
            var m = root.GetComponent<GdaiSceneAssemblyMarker>();
            if (m == null) m = Undo.AddComponent<GdaiSceneAssemblyMarker>(root);
            Undo.RecordObject(m, "GDAI · root marker");
            m.kind = GdaiSceneAssemblyKind.Root;
            m.entityId = null;
            m.role = null;
            m.assemblyVersion = dto != null ? dto.version : 0;
            m.sourcePath = GdaiSceneAssemblyModels.SceneAssemblyPath;
            return root;
        }

        /// <summary>Find-or-create a child under root matched by (kind, entityId); update its marker.</summary>
        public static GameObject UpsertChild(GameObject root, string name, string kind, string entityId, string role, SceneAssemblyDto dto, out bool created)
        {
            created = false;
            GameObject go = FindChildByKindEntity(root, kind, entityId, name);
            if (go == null)
            {
                go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "GDAI · create " + kind);
                go.transform.SetParent(root.transform, true);
                created = true;
            }
            else if (go.name != name)
            {
                Undo.RecordObject(go, "GDAI · rename " + kind);
                go.name = name;
            }
            var m = go.GetComponent<GdaiSceneAssemblyMarker>();
            if (m == null) m = Undo.AddComponent<GdaiSceneAssemblyMarker>(go);
            Undo.RecordObject(m, "GDAI · marker " + kind);
            m.kind = kind;
            m.entityId = entityId;
            m.role = role;
            m.assemblyVersion = dto != null ? dto.version : 0;
            m.sourcePath = GdaiSceneAssemblyModels.SceneAssemblyPath;
            return go;
        }

        /// <summary>All GDAI markers of a given kind (optionally restricted to children of root).</summary>
        public static List<GdaiSceneAssemblyMarker> FindChildrenByKind(GameObject root, string kind)
        {
            var all = Object.FindObjectsByType<GdaiSceneAssemblyMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var outList = new List<GdaiSceneAssemblyMarker>();
            foreach (var m in all)
            {
                if (m == null || m.kind != kind) continue;
                if (root != null && !m.transform.IsChildOf(root.transform)) continue;
                outList.Add(m);
            }
            return outList;
        }

        private static GameObject FindByKind(string kind, GameObject root)
        {
            var list = FindChildrenByKind(root, kind);
            return list.Count > 0 ? list[0].gameObject : null;
        }

        private static GameObject FindChildByKindEntity(GameObject root, string kind, string entityId, string fallbackName)
        {
            foreach (var m in FindChildrenByKind(root, kind))
            {
                // singletons (player_spawn/arena_bounds) match by kind; multi (enemy_spawn/blocker) by entity_id
                if (string.IsNullOrEmpty(entityId) || m.entityId == entityId) return m.gameObject;
            }
            // last resort: exact deterministic name under root
            if (root != null)
            {
                var t = root.transform.Find(fallbackName);
                if (t != null) return t.gameObject;
            }
            return null;
        }

        /// <summary>Delete GDAI-owned children (given kinds) under root whose name is not in keepNames.</summary>
        public static int PruneStaleChildren(GameObject root, HashSet<string> keepNames, params string[] kinds)
        {
            var kindSet = new HashSet<string>(kinds);
            int removed = 0;
            foreach (var m in Object.FindObjectsByType<GdaiSceneAssemblyMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (m == null || !kindSet.Contains(m.kind)) continue;
                if (!m.transform.IsChildOf(root.transform)) continue;
                if (keepNames.Contains(m.gameObject.name)) continue;
                Undo.DestroyObjectImmediate(m.gameObject);
                removed++;
            }
            return removed;
        }

        private static ApplyResult Fail(string reason)
        {
            Debug.LogWarning("[GDAI][Scene][SpawnMarkers] FAIL: " + reason);
            return new ApplyResult { ok = false, dialog = "FAIL\n\n" + reason };
        }
    }
}
