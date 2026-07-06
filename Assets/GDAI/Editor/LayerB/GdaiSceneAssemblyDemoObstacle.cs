using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GDAI.Bridge;                 // marker + kinds + GdaiDemoObstacleGizmo
using GDAI.Bridge.Editor.LayerA;   // GdaiImportedAssetRegistry

// =====================================================================================
// A3-UNITY-CONSUME-C5-DEMO-OBSTACLE · Workbench placement → visible demo obstacle (Editor, Layer B).
//
// Turns ONE non-spawn sceneAssembly.placement (a location/item/prop entity that is NOT the
// player/enemy spawn) into a deterministic, visible Unity object so the demo shows interior
// editable scene affordances — WITHOUT pretending a full scene_geometry SSOT exists.
//
//   GDAI_SceneAssembly
//     └─ GDAI_DemoObstacle_<entityId8>   (marker kind=demo_obstacle, role=unity_demo_draft)
//
//   · position via the shared C1 coordinate converter (canvas→world, PPU=100, arena-centered)
//   · visible: SpriteRenderer if the registry resolves a sprite; else a fallback gizmo + warning
//   · BoxCollider2D (isTrigger=false) on the DEFAULT layer — explicitly labeled demo-draft
//   · reuses C2 root + ownership + idempotency helpers; prunes only its OWN stale demo obstacles
//
// HONESTY (SC-mandated): console + dialog state this is unity_demo_draft, NOT Flowcraft SSOT.
// Forbidden here: ProjectSettings / TagManager / custom layers, sprite-asset mutation, scene save,
// scene_geometry, triggers/interactables. Undo supported; scene marked dirty but never saved.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiSceneAssemblyDemoObstacle
    {
        public const string ObstaclePrefix = "GDAI_DemoObstacle_";
        public const string DemoDraftRole = "unity_demo_draft";
        private const int ObstacleSortingOrder = -1;              // above background(-100), below gameplay(0)
        private const float DefaultObstacleWorldSize = 0.8f;
        private const string HonestyNote = "This is not Flowcraft scene_geometry SSOT.";

        [MenuItem("GDAI/Scene · Create Demo Obstacle")]
        public static void CreateDemoObstacleMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("GDAI · Create Demo Obstacle",
                    "Stop Play Mode first — the obstacle is created in Edit Mode.", "OK");
                return;
            }
            var r = CreateDemoObstacle();
            EditorUtility.DisplayDialog("GDAI · Create Demo Obstacle", r.dialog, "OK");
        }

        public static GdaiSceneAssemblySpawnMarkers.ApplyResult CreateDemoObstacle()
        {
            if (!GdaiSceneAssemblyModels.TryLoad(out SceneAssemblyDto dto, out string err))
                return Fail(err);
            if (dto.arena == null || dto.arena.width <= 0f || dto.arena.height <= 0f)
                return Fail("arena missing/invalid — cannot place obstacle.");

            // Candidate = first placement that is NOT player/enemy and whose entity_id is NOT a spawn.
            var spawnIds = new HashSet<string>();
            if (dto.spawns != null)
                foreach (var s in dto.spawns)
                    if (s != null && !string.IsNullOrEmpty(s.entity_id)) spawnIds.Add(s.entity_id);

            PlacementDto candidate = null;
            if (dto.placements != null)
            {
                foreach (var p in dto.placements)
                {
                    if (p == null || string.IsNullOrEmpty(p.entity_id)) continue;
                    bool isSpawnRole = p.semantic_role == "player" || p.semantic_role == "enemy";
                    if (!isSpawnRole && !spawnIds.Contains(p.entity_id)) { candidate = p; break; }
                }
            }
            if (candidate == null)
                return Fail("no non-spawn placement candidate found — nothing created " +
                            "(place an interior/prop entity in the workbench, then re-import the bundle).");

            var root = GdaiSceneAssemblySpawnMarkers.EnsureRoot(dto);
            string name = ObstaclePrefix + GdaiSceneAssemblyModels.ShortId(candidate.entity_id);
            var go = GdaiSceneAssemblySpawnMarkers.UpsertChild(
                root, name, GdaiSceneAssemblyKind.DemoObstacle, candidate.entity_id, DemoDraftRole, dto, out bool created);

            Undo.RecordObject(go.transform, "GDAI · position demo obstacle");
            go.transform.position = GdaiSceneAssemblyCoordinateUtility.CanvasToWorld(candidate.x, candidate.y, dto.arena.width, dto.arena.height);
            go.transform.rotation = Quaternion.identity;

            // Visual: sprite if the registry resolves one, else fallback gizmo (never fail on missing sprite).
            bool spriteResolved = GdaiImportedAssetRegistry.TryGetSpriteForEntity(candidate.entity_id, out Sprite sprite, out string reason);
            string visual;
            Vector2 colliderSize;
            if (spriteResolved && sprite != null)
            {
                RemoveIfPresent<GdaiDemoObstacleGizmo>(go);
                var sr = go.GetComponent<SpriteRenderer>();
                if (sr == null) sr = Undo.AddComponent<SpriteRenderer>(go);
                Undo.RecordObject(sr, "GDAI · demo obstacle sprite");
                sr.sprite = sprite;
                sr.color = Color.white;
                sr.sortingLayerName = "Default";
                sr.sortingOrder = ObstacleSortingOrder;      // do NOT mutate the imported sprite asset
                Vector3 b = sprite.bounds.size;
                colliderSize = (b.x > 1e-3f && b.y > 1e-3f) ? new Vector2(b.x, b.y) : new Vector2(DefaultObstacleWorldSize, DefaultObstacleWorldSize);
                visual = "sprite '" + sprite.name + "'";
            }
            else
            {
                RemoveIfPresent<SpriteRenderer>(go);
                var giz = go.GetComponent<GdaiDemoObstacleGizmo>();
                if (giz == null) giz = Undo.AddComponent<GdaiDemoObstacleGizmo>(go);
                Undo.RecordObject(giz, "GDAI · demo obstacle gizmo");
                giz.worldSize = DefaultObstacleWorldSize;
                colliderSize = new Vector2(DefaultObstacleWorldSize, DefaultObstacleWorldSize);
                visual = "gizmo fallback (no sprite: " + (reason ?? "unresolved") + ")";
                Debug.LogWarning("[GDAI][Scene][DemoObstacle] No sprite resolved for demo obstacle; using gizmo fallback. " + (reason ?? ""));
            }

            // Demo-draft collider (Default layer only — no ProjectSettings/TagManager).
            var col = go.GetComponent<BoxCollider2D>();
            if (col == null) col = Undo.AddComponent<BoxCollider2D>(go);
            Undo.RecordObject(col, "GDAI · demo obstacle collider");
            col.isTrigger = false;
            col.offset = Vector2.zero;
            col.size = colliderSize;

            // Idempotency: prune our own stale demo obstacles (different entity → different name).
            var keep = new HashSet<string> { name };
            int pruned = GdaiSceneAssemblySpawnMarkers.PruneStaleChildren(root, keep, GdaiSceneAssemblyKind.DemoObstacle);

            EditorSceneManager.MarkSceneDirty(root.scene);

            string body = (created ? "Created " : "Updated ") + name + " · " + visual +
                          " · BoxCollider2D(Default layer) · role=" + DemoDraftRole + "." +
                          (pruned > 0 ? "\n" + pruned + " stale demo obstacle(s) removed." : "");
            string full = "Created demo obstacle as unity_demo_draft. " + HonestyNote + "\n" + body;
            Debug.Log("[GDAI][Scene][DemoObstacle] " + full);
            return new GdaiSceneAssemblySpawnMarkers.ApplyResult
            {
                ok = true,
                dialog = "Created demo obstacle as unity_demo_draft.\n" + HonestyNote + "\n\n" + body
            };
        }

        private static void RemoveIfPresent<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            if (c != null) Undo.DestroyObjectImmediate(c);
        }

        private static GdaiSceneAssemblySpawnMarkers.ApplyResult Fail(string reason)
        {
            Debug.LogWarning("[GDAI][Scene][DemoObstacle] FAIL: " + reason);
            return new GdaiSceneAssemblySpawnMarkers.ApplyResult { ok = false, dialog = "FAIL\n\n" + reason };
        }
    }
}
