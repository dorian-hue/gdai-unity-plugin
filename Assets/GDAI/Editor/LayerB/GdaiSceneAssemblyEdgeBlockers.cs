using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GDAI.Bridge;   // GdaiSceneAssemblyMarker + kinds

// =====================================================================================
// A3-UNITY-CONSUME-LONGRUN-0B · C4A · Default arena-edge blockers (Editor, Layer B).
//
// Turns sceneAssembly.default_blockers (arena-derived edges) into physical BoxCollider2D
// objects under GDAI_SceneAssembly:
//   GDAI_WorldBlocker_arena_left / _right / _top / _bottom
//
// C4A SCOPE (safe): colliders on the DEFAULT layer only.
//   · NO ProjectSettings / TagManager edits, NO custom layer creation (that is C4B — needs
//     explicit Tech Owner approval; do NOT silently edit ProjectSettings).
//   · Reuses C2 root + ownership + idempotency helpers; prunes stale blockers it owns.
//   · Undo · scene dirty not saved · refuses Play Mode.
//
// Coordinate mapping (blocker canvas rect x,y,w,h → world):
//   center = CanvasRectCenterToWorld(x, y, w, h)   (y-flip affects position)
//   size   = CanvasSizeToWorld(w, h)               (no flip)
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiSceneAssemblyEdgeBlockers
    {
        public const string BlockerPrefix = "GDAI_WorldBlocker_";

        [MenuItem("GDAI/Scene · Create Edge Blockers")]
        public static void CreateEdgeBlockersMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("GDAI · Create Edge Blockers",
                    "Stop Play Mode first — blockers are created in Edit Mode.", "OK");
                return;
            }
            var r = CreateEdgeBlockers();
            EditorUtility.DisplayDialog("GDAI · Create Edge Blockers", r.dialog, "OK");
        }

        public static GdaiSceneAssemblySpawnMarkers.ApplyResult CreateEdgeBlockers()
        {
            if (!GdaiSceneAssemblyModels.TryLoad(out SceneAssemblyDto dto, out string err))
                return Fail(err);
            if (dto.arena == null || dto.arena.width <= 0f || dto.arena.height <= 0f)
                return Fail("arena missing/invalid — cannot map blockers.");
            if (dto.default_blockers == null || dto.default_blockers.Count == 0)
                return Fail("no default_blockers in sceneAssembly.");

            var root = GdaiSceneAssemblySpawnMarkers.EnsureRoot(dto);
            int created = 0, updated = 0;
            var keepNames = new HashSet<string>();

            foreach (var b in dto.default_blockers)
            {
                if (b == null || string.IsNullOrEmpty(b.id)) continue;

                string name = BlockerPrefix + b.id;
                keepNames.Add(name);

                var go = GdaiSceneAssemblySpawnMarkers.UpsertChild(
                    root, name, GdaiSceneAssemblyKind.Blocker, b.id, b.kind, dto, out bool wasCreated);

                Undo.RecordObject(go.transform, "GDAI · position blocker");
                go.transform.position = GdaiSceneAssemblyCoordinateUtility.CanvasRectCenterToWorld(
                    b.x, b.y, b.w, b.h, dto.arena.width, dto.arena.height);
                go.transform.rotation = Quaternion.identity;

                var col = go.GetComponent<BoxCollider2D>();
                if (col == null) col = Undo.AddComponent<BoxCollider2D>(go);
                Undo.RecordObject(col, "GDAI · blocker collider");
                Vector2 size = GdaiSceneAssemblyCoordinateUtility.CanvasSizeToWorld(b.w, b.h);
                col.size = size;
                col.offset = Vector2.zero;
                col.isTrigger = false;
                // C4A: leave on Default layer (layer 0). No ProjectSettings/TagManager edits.

                if (wasCreated) created++; else updated++;
            }

            int pruned = GdaiSceneAssemblySpawnMarkers.PruneStaleChildren(root, keepNames, GdaiSceneAssemblyKind.Blocker);

            EditorSceneManager.MarkSceneDirty(root.scene);
            string msg = "Edge blockers: " + created + " created, " + updated + " updated, " + pruned + " stale removed." +
                         "\nBoxCollider2D on Default layer (no ProjectSettings touched) · scene marked dirty (not saved).";
            Debug.Log("[GDAI][Scene][EdgeBlockers] " + msg);
            return new GdaiSceneAssemblySpawnMarkers.ApplyResult { ok = true, dialog = msg };
        }

        private static GdaiSceneAssemblySpawnMarkers.ApplyResult Fail(string reason)
        {
            Debug.LogWarning("[GDAI][Scene][EdgeBlockers] FAIL: " + reason);
            return new GdaiSceneAssemblySpawnMarkers.ApplyResult { ok = false, dialog = "FAIL\n\n" + reason };
        }
    }
}
