using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GDAI.Bridge;   // GdaiSceneAssemblyMarker + kinds

// =====================================================================================
// UNITY-SCENE-VISUAL-CLEANUP-1E · A · Remove demo-only visual noise (Editor, Layer B).
//
// Deletes ONLY objects that GDAI generated as demo/preview scaffolding and that are NOT part
// of the scene_elements 1D main chain:
//   · GDAI_DemoObstacle_*        (C5 demo obstacle, marker kind=demo_obstacle)
//   · GDAI_ImportedAssetPreview  (GdaiAssetBindingUtility preview root)
//
// HARD PROTECT (never deleted): GDAI_SceneAssembly, GDAI_SceneElement_*, GDAI_SceneBackground,
// Player, EnemyManager, GameIntegrationController, Main Camera, Global Light 2D — and anything
// without our demo marker / not one of the two demo categories above.
//
// Idempotent · Undo supported · scene marked dirty but NEVER saved · refuses Play Mode.
// Does not touch scene_elements consumer data, physics.confirmed, ProjectSettings, or generated code.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiSceneVisualCleanup
    {
        public const string ImportedAssetPreviewName = "GDAI_ImportedAssetPreview";

        // Defense-in-depth denylist: these are never removed even if some future path matched them.
        private static readonly HashSet<string> Protected = new HashSet<string>
        {
            "GDAI_SceneAssembly", "GDAI_SceneBackground", "Player", "EnemyManager",
            "GameIntegrationController", "Main Camera", "Global Light 2D",
        };

        [MenuItem("GDAI/Scene · Cleanup Demo Visual Noise")]
        public static void CleanupMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("GDAI · Cleanup Demo Visual Noise",
                    "Stop Play Mode first — cleanup runs in Edit Mode.", "OK");
                return;
            }
            string summary = Cleanup();
            EditorUtility.DisplayDialog("GDAI · Cleanup Demo Visual Noise", summary, "OK");
        }

        public static string Cleanup()
        {
            var scene = SceneManager.GetActiveScene();
            var removed = new List<string>();

            // 1 · demo obstacles (our marker kind=demo_obstacle) — safe, C5 demo-draft only.
            foreach (var m in Object.FindObjectsByType<GdaiSceneAssemblyMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (m == null || m.kind != GdaiSceneAssemblyKind.DemoObstacle) continue;
                var go = m.gameObject;
                if (IsProtected(go.name)) continue;
                removed.Add(go.name);
                Undo.DestroyObjectImmediate(go);
            }

            // 2 · imported-asset preview root (preview only — gameplay objects untouched).
            var preview = GameObject.Find(ImportedAssetPreviewName);
            if (preview != null && !IsProtected(preview.name))
            {
                removed.Add(preview.name);
                Undo.DestroyObjectImmediate(preview);
            }

            if (removed.Count > 0) EditorSceneManager.MarkSceneDirty(scene);

            string msg = "Removed " + removed.Count + " old demo object(s)." +
                         (removed.Count > 0 ? "\n  · " + string.Join("\n  · ", removed) : " (nothing to clean)") +
                         "\nProtected (untouched): GDAI_SceneAssembly / GDAI_SceneElement_* / GDAI_SceneBackground / Player / EnemyManager / ..." +
                         "\nScene marked dirty (not saved).";
            Debug.Log("[GDAI][Scene][Cleanup] " + msg);
            return msg;
        }

        private static bool IsProtected(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            if (Protected.Contains(name)) return true;
            if (name == "GDAI_SceneAssembly") return true;
            if (name.StartsWith("GDAI_SceneElement_")) return true;   // never delete real 1D scene elements
            return false;
        }
    }
}
