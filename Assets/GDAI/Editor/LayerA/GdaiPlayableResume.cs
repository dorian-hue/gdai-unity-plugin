// =====================================================================================
// GDAI Unity Plugin · AUTO-0Q-P2 · P2-B(LIVE) · Operation resume core (Editor, Layer A).
//
// The robust half of the resumable CTA: rather than trusting an async call stack to
// survive a domain reload (it does not), the operation is PERSISTED and re-entered
// from an [InitializeOnLoad] hook after every domain load. This class is the pure,
// testable decision + action:
//   * no active operation            → NoOperation (the hook does nothing → no auto apply)
//   * terminal / aborted / failed     → NotResumable (never re-runs)
//   * bound-project identity mismatch → Refused (project id vs binding, or no bound session)
//   * otherwise                       → resume the EXACT next phase; the destructive
//                                       generated-root replace is skipped if committed.
// A Library-scoped breadcrumb records exactly what happened (never an asset mutation).
// =====================================================================================
using System;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerA
{
    [Serializable]
    public class GdaiResumeBreadcrumb
    {
        public string decision;          // NoOperation | NotResumable | Refused | Resumed
        public string operation_id;
        public string reason;
        public string phase_before;
        public string phase_after;
        public bool destructive_repeated; // must always be false
        public string at;
    }

    public static class GdaiPlayableResume
    {
        public enum Decision { NoOperation, NotResumable, Refused, Resumed }

        public static string BreadcrumbPath(string projectRoot) =>
            Path.Combine(projectRoot, "Library", "GDAI", "resume-breadcrumb.json");

        // A locally-verifiable "a bound session/token exists" signal. Production pairing
        // writes this marker next to the binding; tests create it explicitly. Kept separate
        // from the binding file so "binding present but unpaired" is still Refused.
        public static string BoundSessionMarkerPath(string projectRoot) =>
            Path.Combine(projectRoot, "Library", "GDAI", "bound-session.flag");

        public static bool BoundSessionPresent(string projectRoot) =>
            File.Exists(BoundSessionMarkerPath(projectRoot));

        /// <summary>Reads the bound project id from ProjectSettings/GDAIProjectBinding.json (or null).</summary>
        public static string BoundProjectId(string projectRoot)
        {
            try
            {
                var p = Path.Combine(projectRoot, GdaiProjectBinding.RelativePath);
                if (!File.Exists(p)) return null;
                var data = JsonConvert.DeserializeObject<GdaiProjectBindingData>(File.ReadAllText(p));
                return data?.project_id;
            }
            catch { return null; }
        }

        // The imported playable contract on disk (schema+profile), for a real (non-circular)
        // contract-revision reverify against what the operation recorded.
        public static string OnDiskContractVersion(string projectRoot)
        {
            try
            {
                var p = Path.Combine(projectRoot, "Assets", "GDAI_Generated", GdaiPlayableContract.BundleFileName);
                if (!File.Exists(p)) return null;
                var res = GdaiPlayableContract.Parse(File.ReadAllText(p));
                return res.Ok ? res.Contract.schema_version + "/" + res.Contract.profile_id : null;
            }
            catch { return null; }
        }

        private static void WriteBreadcrumb(string projectRoot, GdaiResumeBreadcrumb bc, DateTime nowUtc)
        {
            bc.at = nowUtc.ToString("o");
            Directory.CreateDirectory(Path.GetDirectoryName(BreadcrumbPath(projectRoot)));
            File.WriteAllText(BreadcrumbPath(projectRoot), JsonConvert.SerializeObject(bc, Formatting.Indented));
        }

        /// <summary>
        /// Evaluate + (if safe) resume the active operation after a domain load. Returns the
        /// decision and always records a breadcrumb. Read-only w.r.t. bound project — it never
        /// switches project, never falls back to project[0], never writes assets here.
        /// Only the operation's own phase is advanced (Library-scoped).
        /// </summary>
        public static Decision ResumeAfterReload(string projectRoot, DateTime nowUtc)
        {
            var op = GdaiPlayableOperation.LoadActive(projectRoot, out int count);
            if (count == 0)
            {
                WriteBreadcrumb(projectRoot, new GdaiResumeBreadcrumb { decision = "NoOperation", reason = "no active operation → no auto apply" }, nowUtc);
                return Decision.NoOperation;
            }
            if (count > 1 || op == null)
            {
                WriteBreadcrumb(projectRoot, new GdaiResumeBreadcrumb { decision = "Refused", reason = "multiple active operations" }, nowUtc);
                return Decision.Refused;
            }

            // reverify bound-project identity (read-only): binding present, project id matches, token present.
            string boundId = BoundProjectId(projectRoot);
            if (string.IsNullOrEmpty(boundId) || !string.Equals(boundId, op.project_id, StringComparison.OrdinalIgnoreCase))
            {
                WriteBreadcrumb(projectRoot, new GdaiResumeBreadcrumb { decision = "Refused", operation_id = op.operation_id, reason = "bound project id mismatch/absent" }, nowUtc);
                return Decision.Refused;
            }
            if (!BoundSessionPresent(projectRoot))
            {
                WriteBreadcrumb(projectRoot, new GdaiResumeBreadcrumb { decision = "Refused", operation_id = op.operation_id, reason = "no bound session/token" }, nowUtc);
                return Decision.Refused;
            }

            // real (non-circular) contract-revision reverify: compare the operation's recorded
            // version to the contract actually imported on disk. If codegen shipped a different
            // revision than the operation started with, refuse.
            var onDisk = OnDiskContractVersion(projectRoot);
            if (string.IsNullOrEmpty(onDisk) || !string.Equals(onDisk, op.playable_contract_version, StringComparison.Ordinal))
            {
                WriteBreadcrumb(projectRoot, new GdaiResumeBreadcrumb { decision = "Refused", operation_id = op.operation_id, reason = "contract revision mismatch (disk vs operation)" }, nowUtc);
                return Decision.Refused;
            }

            var verdict = op.CanResume(op.project_id, op.snapshot_id, op.playable_contract_version, tokenPresent: true, out string why);
            if (verdict == GdaiPlayableOperation.ResumeVerdict.Terminal)
            {
                WriteBreadcrumb(projectRoot, new GdaiResumeBreadcrumb { decision = "NotResumable", operation_id = op.operation_id, reason = why }, nowUtc);
                return Decision.NotResumable;
            }
            if (verdict != GdaiPlayableOperation.ResumeVerdict.Resumable)
            {
                WriteBreadcrumb(projectRoot, new GdaiResumeBreadcrumb { decision = "Refused", operation_id = op.operation_id, reason = why }, nowUtc);
                return Decision.Refused;
            }

            var before = op.phase;
            var next = op.NextPhaseOnResume();
            bool destructiveRepeat = next == GdaiPlayablePhase.GeneratedRootReplaced && op.generated_root_committed;
            if (destructiveRepeat)
            {
                // never re-run the committed destructive phase
                WriteBreadcrumb(projectRoot, new GdaiResumeBreadcrumb { decision = "Refused", operation_id = op.operation_id, reason = "would repeat committed destructive phase", destructive_repeated = false }, nowUtc);
                return Decision.Refused;
            }

            // advance exactly one step (the per-phase asset work is dispatched by the CTA
            // continuation once all builders exist; here we prove the re-entry survives reload).
            op.Advance(projectRoot, next, nowUtc);
            WriteBreadcrumb(projectRoot, new GdaiResumeBreadcrumb
            {
                decision = "Resumed",
                operation_id = op.operation_id,
                phase_before = before.ToString(),
                phase_after = next.ToString(),
                destructive_repeated = false,
                reason = "InitializeOnLoad re-entry after domain reload",
            }, nowUtc);
            return Decision.Resumed;
        }
    }

    /// <summary>
    /// Fires the resume evaluation after every domain reload — the canonical post-reload
    /// hook, which fires reliably (including in batchmode and after a code-import reload),
    /// unlike delayCall which may not tick before an editor exits. Only an active operation
    /// causes any action; a clean project is a guaranteed no-op.
    /// </summary>
    [InitializeOnLoad]
    public static class GdaiPlayableResumeHook
    {
        static GdaiPlayableResumeHook()
        {
            AssemblyReloadEvents.afterAssemblyReload += OnAfterReload;
            // also cover a plain editor open where no reload event is delivered
            EditorApplication.delayCall += OnAfterReload;
        }

        private static void OnAfterReload()
        {
            try { GdaiPlayableResume.ResumeAfterReload(Directory.GetCurrentDirectory(), DateTime.UtcNow); }
            catch (Exception e) { Debug.LogWarning("[GDAI] resume hook error: " + e.Message); }
        }
    }
}
