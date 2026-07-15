// =====================================================================================
// GDAI Unity Plugin · T4 0J Gate B · THE single Scene Assembly composition core.
//
// Root cause closed by Gate B: the Layer-B scene placement used to run in CompleteExportSync BEFORE the
// canonical playable composer opened a fresh EmptyScene in Single mode — so the reset wiped it. This core
// is the ONE Production scene-mutation authority: it is invoked from GdaiPlayableComposerCta.Run AFTER the
// Single-mode canonical scene is open and BEFORE the ownership manifest / receipt / save, so the arena
// boundaries, scene elements, spawn markers and colliders live inside the scene the composer owns.
//
// It does NOT reimplement placement — it orchestrates the existing return-value helper cores in the
// canonical order. The manual Layer-B menu entries call the SAME underlying cores, so there is exactly one
// placement implementation and only one Production caller (the composer). Idempotent by construction (each
// core find-or-creates under the single GDAI_SceneAssembly root and prunes only its own marked kinds).
// =====================================================================================
using System.Collections.Generic;

namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiSceneAssemblyComposerCore
    {
        public struct Result { public bool ok; public string summary; }

        /// <summary>
        /// Consume GDAI_SceneAssembly.json into the currently-open canonical scene, in canonical order:
        /// arena boundary colliders → scene element colliders → spawn markers → arena bounds gizmo → demo
        /// obstacle. No-op (ok) when the bundle carries no scene assembly. Callers must have the canonical
        /// scene already open in Single mode (GdaiPlayableComposerCta.Run guarantees this).
        /// </summary>
        public static Result Compose()
        {
            if (!GdaiSceneAssemblyModels.TryLoad(out var dto, out _) || dto == null)
                return new Result { ok = true, summary = "no GDAI_SceneAssembly.json — scene assembly skipped" };

            var msgs = new List<string>();
            bool ok = true;

            // arena boundary physical colliders (BoxCollider2D per default_blockers[]) — AC-1
            var blockers = GdaiSceneAssemblyEdgeBlockers.CreateEdgeBlockers();
            ok &= blockers.ok; msgs.Add("blockers=" + blockers.ok);

            // explicit scene elements (SpriteRenderer + exactly one authoritative collider) — AC-3
            var elements = GdaiSceneAssemblyElements.PlaceSceneElements();
            ok &= elements.ok; msgs.Add("elements=" + elements.ok);

            // spawn markers (identity only; no colliders)
            var spawns = GdaiSceneAssemblySpawnMarkers.PlaceSpawnMarkers();
            ok &= spawns.ok; msgs.Add("spawns=" + spawns.ok);

            // arena bounds debug gizmo (no collider)
            var bounds = GdaiSceneAssemblyArenaBounds.ShowArenaBounds();
            ok &= bounds.ok; msgs.Add("bounds=" + bounds.ok);

            // demo obstacle (one non-spawn placement → BoxCollider2D)
            var demo = GdaiSceneAssemblyDemoObstacle.CreateDemoObstacle();
            ok &= demo.ok; msgs.Add("demo=" + demo.ok);

            return new Result { ok = ok, summary = string.Join(" · ", msgs) };
        }
    }
}
