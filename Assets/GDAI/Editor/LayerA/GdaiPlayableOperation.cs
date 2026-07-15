// =====================================================================================
// GDAI Unity Plugin · AUTO-0Q-P2 · P2-B · Resumable CTA operation state (Editor, Layer A).
//
// A single user-triggered "Complete GDAI Export / Sync" runs many phases, some of
// which import generated C# and trigger a domain reload that kills the async call
// stack. This persists the operation to Library/GDAI/operations/<id>.json so the
// composer can resume the EXACT next phase after a reload — and, critically:
//   * NO operation on disk  → NO automatic apply on Unity open (never auto-runs).
//   * resume verifies project + token-presence + snapshot + contract revision + phase
//     before touching assets.
//   * the one destructive phase (Backup&Replace of the generated root) is marked
//     Committed once done and is never blindly repeated on resume.
//   * Abort is always safe (marks Aborted, writes nothing else).
// Library/ is never tracked in Git, so operation state never leaks into the project.
// =====================================================================================
using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerA
{
    public enum GdaiPlayablePhase
    {
        Created = 0,
        BoundReadyVerified = 10,
        BundleFetched = 20,
        CanonicalSceneReady = 30,
        GeneratedRootReplaced = 40,   // DESTRUCTIVE — guarded by GeneratedRootCommitted
        CodeImportedAwaitingReload = 50,
        ResumedAfterReload = 60,
        SceneObjectsComposed = 70,
        InputAssetReady = 80,
        PlayerComposed = 90,
        EnemyPrefabReady = 100,
        BindingsApplied = 110,
        SelfChecksPassed = 120,
        SceneSaved = 130,
        ReceiptWritten = 140,
        Complete = 200,
        Aborted = -1,
        Failed = -2,
    }

    [Serializable]
    public class GdaiPlayableOperation
    {
        // operations older than this never auto-resume (opening a weeks-old project
        // must not silently re-run a stale write operation).
        public const double StaleAfterHours = 24.0;

        public string operation_id;
        public string project_id;
        public string snapshot_id;
        // contract identity is pinned as THREE facts so rev3 and rev4 are mechanically
        // distinct (schema_version alone is identical across revisions).
        public string contract_schema;
        public int contract_revision;
        public string contract_sha256;
        public string canonical_scene_path;
        public GdaiPlayablePhase phase = GdaiPlayablePhase.Created;
        public bool generated_root_committed;       // the destructive replace has happened
        public string started_at;
        public string last_updated_at;
        public string result;                        // null | PASS | ABORTED | FAILED:<phase>
        public string non_secret_error;             // never a token/secret

        // ---- storage (Library-scoped; never in Assets, never tracked) ----
        public static string OperationsDir(string projectRoot) =>
            Path.Combine(projectRoot, "Library", "GDAI", "operations");

        public static string PathFor(string projectRoot, string opId) =>
            Path.Combine(OperationsDir(projectRoot), opId + ".json");

        public static GdaiPlayableOperation Create(string projectRoot, string projectId, string snapshotId,
            string contractSchema, int contractRevision, string contractSha256, string scenePath, DateTime nowUtc)
        {
            var op = new GdaiPlayableOperation
            {
                operation_id = Guid.NewGuid().ToString("N").Substring(0, 16),
                project_id = projectId,
                snapshot_id = snapshotId,
                contract_schema = contractSchema,
                contract_revision = contractRevision,
                contract_sha256 = contractSha256,
                canonical_scene_path = scenePath,
                phase = GdaiPlayablePhase.Created,
                started_at = nowUtc.ToString("o"),
            };
            op.Save(projectRoot, nowUtc);
            return op;
        }

        public void Save(string projectRoot, DateTime nowUtc)
        {
            last_updated_at = nowUtc.ToString("o");
            Directory.CreateDirectory(OperationsDir(projectRoot));
            var finalPath = PathFor(projectRoot, operation_id);
            // atomic write: full temp file then replace, so a reload mid-write never reads half JSON.
            var tmp = finalPath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(this, Formatting.Indented));
            if (File.Exists(finalPath)) File.Replace(tmp, finalPath, null);
            else File.Move(tmp, finalPath);
        }

        /// <summary>True when the operation is older than the stale window (must not auto-resume).</summary>
        public bool IsStale(DateTime nowUtc)
        {
            if (!DateTime.TryParse(started_at, null, System.Globalization.DateTimeStyles.RoundtripKind, out var started))
                return true; // unparseable timestamp is treated as stale (fail closed)
            return (nowUtc - started.ToUniversalTime()).TotalHours > StaleAfterHours;
        }

        public void Advance(string projectRoot, GdaiPlayablePhase to, DateTime nowUtc)
        {
            phase = to;
            if (to == GdaiPlayablePhase.GeneratedRootReplaced) generated_root_committed = true;
            Save(projectRoot, nowUtc);
        }

        public void Abort(string projectRoot, DateTime nowUtc, string reason = null)
        {
            phase = GdaiPlayablePhase.Aborted;
            result = "ABORTED";
            if (!string.IsNullOrEmpty(reason)) non_secret_error = reason;
            Save(projectRoot, nowUtc);
        }

        public void Fail(string projectRoot, DateTime nowUtc, string nonSecretError)
        {
            result = "FAILED:" + phase;
            non_secret_error = nonSecretError; // caller guarantees no token/secret
            phase = GdaiPlayablePhase.Failed;
            Save(projectRoot, nowUtc);
        }

        public void Delete(string projectRoot)
        {
            var p = PathFor(projectRoot, operation_id);
            if (File.Exists(p)) File.Delete(p);
        }

        /// <summary>The single in-flight operation, or null. More than one is a hard error (returns null + count).</summary>
        public static GdaiPlayableOperation LoadActive(string projectRoot, out int count)
        {
            count = 0;
            var dir = OperationsDir(projectRoot);
            if (!Directory.Exists(dir)) return null;
            GdaiPlayableOperation found = null;
            foreach (var f in Directory.GetFiles(dir, "*.json"))
            {
                GdaiPlayableOperation op;
                try { op = JsonConvert.DeserializeObject<GdaiPlayableOperation>(File.ReadAllText(f)); }
                catch { continue; }
                if (op == null) continue;
                if (op.phase == GdaiPlayablePhase.Complete || op.phase == GdaiPlayablePhase.Aborted || op.phase == GdaiPlayablePhase.Failed)
                    continue; // terminal — not active
                count++;
                found = op;
            }
            return count == 1 ? found : null;
        }

        public enum ResumeVerdict { NoOperation, Resumable, Unsafe, Terminal }

        /// <summary>
        /// Decide whether a persisted operation may resume, given the CURRENT project/snapshot/
        /// contract facts and whether a bound token is present. Never resumes on a mismatch.
        /// </summary>
        public ResumeVerdict CanResume(string currentProjectId, string currentSnapshotId,
            string currentSchema, int currentRevision, string currentSha256, bool tokenPresent, out string why)
        {
            why = null;
            if (phase == GdaiPlayablePhase.Complete || phase == GdaiPlayablePhase.Aborted || phase == GdaiPlayablePhase.Failed)
            { why = "operation terminal"; return ResumeVerdict.Terminal; }
            if (!tokenPresent) { why = "no bound token"; return ResumeVerdict.Unsafe; }
            if (!string.Equals(project_id, currentProjectId, StringComparison.OrdinalIgnoreCase)) { why = "project mismatch"; return ResumeVerdict.Unsafe; }
            if (!string.Equals(snapshot_id, currentSnapshotId, StringComparison.OrdinalIgnoreCase)) { why = "snapshot changed"; return ResumeVerdict.Unsafe; }
            // all THREE contract-identity facts must match (schema alone can't tell rev3 from rev4)
            if (!string.Equals(contract_schema, currentSchema, StringComparison.Ordinal)) { why = "contract schema changed"; return ResumeVerdict.Unsafe; }
            if (contract_revision != currentRevision) { why = "contract revision changed (" + contract_revision + "->" + currentRevision + ")"; return ResumeVerdict.Unsafe; }
            if (!string.Equals(contract_sha256, currentSha256, StringComparison.OrdinalIgnoreCase)) { why = "contract sha256 changed"; return ResumeVerdict.Unsafe; }
            return ResumeVerdict.Resumable;
        }

        /// <summary>Next phase to run on resume; the destructive replace is skipped if already committed.</summary>
        public GdaiPlayablePhase NextPhaseOnResume()
        {
            if (phase == GdaiPlayablePhase.CodeImportedAwaitingReload) return GdaiPlayablePhase.ResumedAfterReload;
            if (phase == GdaiPlayablePhase.GeneratedRootReplaced && generated_root_committed) return GdaiPlayablePhase.CodeImportedAwaitingReload;
            return phase; // continue from where it stopped
        }
    }
}
