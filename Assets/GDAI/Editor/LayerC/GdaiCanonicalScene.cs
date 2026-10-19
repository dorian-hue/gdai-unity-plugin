// =====================================================================================
// GDAI Unity Plugin · AUTO-0Q-P2 · P2-C · Canonical scene lifecycle (Editor, Layer C).
//
// Owns Assets/Scenes/Main.unity for the playable contract: load it if it exists,
// else create it via the Editor API and save to the EXACT contract path — never a
// Save As dialog, never a path outside Assets, never overwriting an unrelated user
// scene. Also ensures the scene is the single enabled entry in Build Settings.
// The same path/GUID is reused across every Sync (idempotent).
// =====================================================================================
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GDAI.Bridge.Editor.LayerC
{
    public static class GdaiCanonicalScene
    {
        public class Result
        {
            public bool Ok;
            public bool Created;
            public string ScenePath;
            public string Error;
        }

        /// <summary>Load or create + save the canonical scene, and add it to Build Settings.</summary>
        public static Result EnsureSavedAndInBuild(string scenePath)
        {
            var r = new Result { ScenePath = scenePath };
            if (string.IsNullOrEmpty(scenePath) || !scenePath.StartsWith("Assets/Scenes/") || !scenePath.EndsWith(".unity"))
            {
                r.Error = "canonical_scene.path must be Assets/Scenes/*.unity";
                return r;
            }

            var abs = Path.Combine(Directory.GetCurrentDirectory(), scenePath);
            Directory.CreateDirectory(Path.GetDirectoryName(abs));

            Scene scene;
            if (File.Exists(abs))
            {
                // reuse the exact scene (preserves its GUID); load it as the active single scene
                scene = SceneManager.GetActiveScene().path == scenePath
                    ? SceneManager.GetActiveScene()
                    : EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
            else
            {
                // create empty (no default objects) so the composer owns the whole graph,
                // then persist to the exact path — NO Save As dialog.
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!EditorSceneManager.SaveScene(scene, scenePath))
                {
                    r.Error = "failed to save canonical scene to " + scenePath;
                    return r;
                }
                r.Created = true;
            }

            if (!AddToBuildSettings(scenePath))
            {
                r.Error = "failed to register scene in Build Settings";
                return r;
            }
            r.Ok = true;
            return r;
        }

        // B-2 (MNT-8): the one scene this normalization may ever disable — the template's
        // own SampleScene inside a GDAI-owned scaffold. User scenes are NEVER touched.
        private const string TemplateSampleScenePath = "Assets/Scenes/SampleScene.unity";

        // GDAI-owned scaffold marker: the exported project carries the binding file. Its
        // presence scopes the build-settings normalization; in any other project the
        // legacy append-and-enable behaviour is preserved unchanged.
        private static bool IsGdaiOwnedScaffold() =>
            File.Exists(Path.Combine(Directory.GetCurrentDirectory(),
                GDAI.Bridge.Editor.LayerA.GdaiProjectBinding.RelativePath));

        /// <summary>Make the canonical scene the single enabled Build Settings entry (idempotent).
        /// B-2: inside a GDAI-owned scaffold the template SampleScene is disabled (kept in the
        /// list, never deleted) so the canonical scene is the only enabled entry; previously
        /// this registration was append-and-enable only and SampleScene stayed enabled forever
        /// (MNT-8). Scoped fail-safe: gated on the exported binding marker, and the only entry
        /// ever disabled is the template's own SampleScene.</summary>
        public static bool AddToBuildSettings(string scenePath)
        {
            var existing = EditorBuildSettings.scenes.ToList();
            var match = existing.FirstOrDefault(s => s.path == scenePath);
            bool changed = false;
            if (match != null)
            {
                if (!match.enabled)
                {
                    match.enabled = true;
                    changed = true;
                }
            }
            else
            {
                existing.Add(new EditorBuildSettingsScene(scenePath, true));
                changed = true;
            }
            if (IsGdaiOwnedScaffold())
            {
                foreach (var s in existing)
                {
                    if (s.enabled && s.path == TemplateSampleScenePath && s.path != scenePath)
                    {
                        s.enabled = false;
                        changed = true;
                    }
                }
            }
            if (changed) EditorBuildSettings.scenes = existing.ToArray();
            return EditorBuildSettings.scenes.Any(s => s.path == scenePath && s.enabled);
        }

        public static bool IsInBuildSettings(string scenePath) =>
            EditorBuildSettings.scenes.Any(s => s.path == scenePath && s.enabled);

        public static void Save()
        {
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }
    }
}
