using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GDAI.Bridge;   // GdaiSceneAssemblyMarker + kinds + GdaiArenaBoundsGizmo

// =====================================================================================
// A3-UNITY-CONSUME-LONGRUN-0B · C3 · Arena bounds debug visualization (Editor, Layer B).
//
// Creates a debug-only GDAI_ArenaBounds object under the GDAI_SceneAssembly root, showing the
// arena rectangle as a Gizmo (GdaiArenaBoundsGizmo). NO physical collider (that is C4). Reuses
// the C2 root + ownership + idempotency helpers. Undo · scene dirty not saved · refuses Play Mode.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiSceneAssemblyArenaBounds
    {
        public const string ArenaBoundsName = "GDAI_ArenaBounds";

        [MenuItem("GDAI/Scene · Show Arena Bounds")]
        public static void ShowArenaBoundsMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("GDAI · Show Arena Bounds",
                    "Stop Play Mode first — bounds are placed in Edit Mode.", "OK");
                return;
            }
            var r = ShowArenaBounds();
            EditorUtility.DisplayDialog("GDAI · Show Arena Bounds", r.dialog, "OK");
        }

        public static GdaiSceneAssemblySpawnMarkers.ApplyResult ShowArenaBounds()
        {
            if (!GdaiSceneAssemblyModels.TryLoad(out SceneAssemblyDto dto, out string err))
                return Fail(err);
            if (dto.arena == null || dto.arena.width <= 0f || dto.arena.height <= 0f)
                return Fail("arena missing/invalid — cannot draw bounds.");

            var root = GdaiSceneAssemblySpawnMarkers.EnsureRoot(dto);
            var go = GdaiSceneAssemblySpawnMarkers.UpsertChild(
                root, ArenaBoundsName, GdaiSceneAssemblyKind.ArenaBounds, null, null, dto, out bool created);

            // Arena is centered at world origin (see coordinate contract).
            Undo.RecordObject(go.transform, "GDAI · position arena bounds");
            go.transform.position = GdaiSceneAssemblyCoordinateUtility.CanvasRectCenterToWorld(
                0f, 0f, dto.arena.width, dto.arena.height, dto.arena.width, dto.arena.height);
            go.transform.rotation = Quaternion.identity;

            var gizmo = go.GetComponent<GdaiArenaBoundsGizmo>();
            if (gizmo == null) gizmo = Undo.AddComponent<GdaiArenaBoundsGizmo>(go);
            Undo.RecordObject(gizmo, "GDAI · arena bounds size");
            Vector2 size = GdaiSceneAssemblyCoordinateUtility.CanvasSizeToWorld(dto.arena.width, dto.arena.height);
            gizmo.worldWidth = size.x;
            gizmo.worldHeight = size.y;

            EditorSceneManager.MarkSceneDirty(root.scene);
            string msg = (created ? "Created " : "Updated ") + ArenaBoundsName +
                         " · " + size.x.ToString("0.##") + " × " + size.y.ToString("0.##") + " world units (debug gizmo, no collider)." +
                         "\nScene marked dirty (not saved).";
            Debug.Log("[GDAI][Scene][ArenaBounds] " + msg);
            return new GdaiSceneAssemblySpawnMarkers.ApplyResult { ok = true, dialog = msg };
        }

        private static GdaiSceneAssemblySpawnMarkers.ApplyResult Fail(string reason)
        {
            Debug.LogWarning("[GDAI][Scene][ArenaBounds] FAIL: " + reason);
            return new GdaiSceneAssemblySpawnMarkers.ApplyResult { ok = false, dialog = "FAIL\n\n" + reason };
        }
    }
}
