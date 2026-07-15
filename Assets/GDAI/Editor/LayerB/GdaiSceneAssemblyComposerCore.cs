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

            // ── content-bearing cores are MANDATORY: any real failure fails-closed the whole compose (requirement
            //    #2 — no partial scene as PASS). arena boundary colliders (AC-1), scene element colliders (AC-3),
            //    spawn markers. The real obstacles come from scene_elements[]; the legacy "demo obstacle" fallback
            //    is intentionally NOT part of the canonical scene assembly (it fails when there is no spare
            //    non-spawn placement, and would otherwise mask a healthy scene). ──
            var blockers = GdaiSceneAssemblyEdgeBlockers.CreateEdgeBlockers();
            msgs.Add("blockers=" + blockers.ok);
            var elements = GdaiSceneAssemblyElements.PlaceSceneElements();
            msgs.Add("elements=" + elements.ok);
            var spawns = GdaiSceneAssemblySpawnMarkers.PlaceSpawnMarkers();
            msgs.Add("spawns=" + spawns.ok);
            bool ok = blockers.ok && elements.ok && spawns.ok;

            // arena bounds is a debug-only gizmo (no collider); best-effort, never gates the compose.
            var bounds = GdaiSceneAssemblyArenaBounds.ShowArenaBounds();
            msgs.Add("bounds=" + bounds.ok);

            return new Result { ok = ok, summary = string.Join(" · ", msgs) };
        }
    }
}
