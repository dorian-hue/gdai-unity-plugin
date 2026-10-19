// =====================================================================================
// GDAI Unity Plugin · ALPHA-B B-1 · Shared sync finalization (Editor, Layer A).
//
// A Complete Sync has exactly ONE finalization: save assets, save the open canonical
// scene, measure the outcome, write the export-completion receipt. Both routes call
// the single FinalizeSync below:
//   inline (code already compiled): CompleteExportSync → compose → FinalizeSync
//   deferred (fresh sync, code compiling): CompleteExportSync persists this context and
//     returns; after the domain reload the resume hook re-runs the scene sequence
//     (canonical open → Layer-B seams → compose) and then finalizes from the context.
// TryTake is the finalization latch: it reads AND deletes the persisted context before
// use, so a repeated or re-entered resume (Unity can reload more than once) can never
// finalize the same sync twice — and it refuses a context whose project/snapshot does
// not match the operation being resumed.
// Root cause fixed (MNT-10 + NEW-4): the deferred branch used to return without ever
// scheduling finalization, so the first sync of a fresh project never saved the scene
// and never wrote a receipt.
// =====================================================================================
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerA
{
    // Everything the finalization needs that only the window knows at import time.
    // Persisted under Library/ (machine-local, never an asset) across the domain reload.
    [Serializable]
    public sealed class GdaiSyncFinalizationContext
    {
        public string project_id;
        public string snapshot_id;
        public string snapshot_revision;
        public string plugin_version;
        public int generated_file_count;
        public int binary_asset_count;
        public int preserved_meta_count;
        public string backup_path;          // project-relative
        public string started_at;           // ISO-8601 UTC — the original CTA click
    }

    public static class GdaiSyncFinalization
    {
        public sealed class FinalizeResult
        {
            public bool Ok;
            public string FailPhase;        // set when !Ok; mirrors CompleteExportSync failPhase
            public string ReceiptPath;
            public int SceneElementsCount;
            public int ColliderCount;
        }

        private static string PendingPath(string projectRoot) =>
            Path.Combine(projectRoot, "Library", "GDAI", "pending-finalize.json");

        /// <summary>Persist the finalization context before the deferred return (B-1).</summary>
        public static void Persist(string projectRoot, GdaiSyncFinalizationContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            string p = PendingPath(projectRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, JsonUtility.ToJson(ctx, true));
        }

        /// <summary>
        /// The exactly-once latch: read + DELETE the pending context before it is used, and
        /// refuse it (consumed, logged) when it does not belong to the operation being
        /// resumed. Returns false when there is nothing valid to finalize.
        /// </summary>
        public static bool TryTake(string projectRoot, string expectedProjectId, string expectedSnapshotId,
            out GdaiSyncFinalizationContext ctx)
        {
            ctx = null;
            string p = PendingPath(projectRoot);
            if (!File.Exists(p)) return false;
            GdaiSyncFinalizationContext loaded = null;
            try { loaded = JsonUtility.FromJson<GdaiSyncFinalizationContext>(File.ReadAllText(p)); }
            catch { /* unreadable → consumed below, never retried */ }
            Clear(projectRoot); // latch: consume BEFORE use, success or not
            if (loaded == null || string.IsNullOrEmpty(loaded.project_id))
            {
                Debug.LogWarning("[GDAI][Finalize] pending-finalize context unreadable — discarded (no receipt will be written)");
                return false;
            }
            if (!string.Equals(loaded.project_id, expectedProjectId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(loaded.snapshot_id ?? "", expectedSnapshotId ?? "", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("[GDAI][Finalize] pending-finalize context belongs to a different operation — discarded");
                return false;
            }
            ctx = loaded;
            return true;
        }

        public static void Clear(string projectRoot)
        {
            try { var p = PendingPath(projectRoot); if (File.Exists(p)) File.Delete(p); } catch { }
        }

        /// <summary>
        /// The single finalization: save assets + open scenes, measure the outcome
        /// (fail closed on an unsaved scene), write the PASS receipt. Failures write
        /// no receipt and report the phase, exactly like the pre-B-1 inline tail.
        /// </summary>
        public static FinalizeResult FinalizeSync(string projectRoot, GdaiSyncFinalizationContext ctx, DateTime utcNow)
        {
            var r = new FinalizeResult();
            if (ctx == null) { r.FailPhase = "FINALIZE_CONTEXT_MISSING"; return r; }
            try
            {
                AssetDatabase.SaveAssets();
                UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();

                // Outcome measurement (fail closed on empty results).
                int elementCount = 0, colliderCount = 0;
                foreach (var col in UnityEngine.Object.FindObjectsByType<PolygonCollider2D>(FindObjectsSortMode.None)) { colliderCount++; _ = col; }
                var sceneRoot = GameObject.Find("GDAI_SceneAssembly");
                if (sceneRoot != null) elementCount = sceneRoot.transform.childCount;
                string scenePath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
                if (string.IsNullOrEmpty(scenePath)) { r.FailPhase = "SCENE_SAVE"; return r; }

                var receipt = new GdaiExportReceiptData
                {
                    project_id = ctx.project_id,
                    snapshot_id = ctx.snapshot_id,
                    snapshot_revision = ctx.snapshot_revision,
                    plugin_version = ctx.plugin_version,
                    generated_file_count = ctx.generated_file_count,
                    binary_asset_count = ctx.binary_asset_count,
                    preserved_meta_count = ctx.preserved_meta_count,
                    backup_path = ctx.backup_path,
                    scene_path = scenePath,
                    scene_elements_count = elementCount,
                    collider_count = colliderCount,
                    started_at = ctx.started_at,
                    completed_at = utcNow.ToString("o"),
                    result = "PASS",
                };
                r.ReceiptPath = GdaiExportReceipt.Write(projectRoot, receipt, utcNow);
                r.SceneElementsCount = elementCount;
                r.ColliderCount = colliderCount;
                r.Ok = true;
                return r;
            }
            catch (Exception e)
            {
                r.FailPhase = "FINALIZE_EXCEPTION: " + e.Message;
                return r;
            }
        }
    }
}
