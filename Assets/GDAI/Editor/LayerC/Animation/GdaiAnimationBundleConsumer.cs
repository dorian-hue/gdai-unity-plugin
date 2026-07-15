// =====================================================================================
// GDAI Unity Plugin · T4 0H Phase 4 · Complete-Sync animation consumer (additive).
//
// The "final segment": turns an IMPORTED coherent-bundle animation package + its imported sheet
// PNG into a materialized playable animation, invoked as one additive step inside the existing
// Complete GDAI Export / Sync operation (NOT a new public CTA). It NEVER reconstructs package
// fields by hand — it hands the raw imported JSON string + imported PNG path straight to
// GdaiAnimationMaterializer.Run.
//
// Snapshot binding: the package lives under Assets/GDAI_Generated/Animation/, and GDAI_Generated
// is the bundle's WHOLE-DIRECTORY clean-replace root (structure standard) — so any package found
// there was written by THE CURRENT snapshot's ImportVerbatim; a stale prior-snapshot package
// cannot survive the clean-replace. Co-location IS the snapshot binding (no per-package snapshot
// field, hence no schema change / no STOP_SCHEMA_CHANGE).
//
// Fail-closed with stable codes: exactly one eligible package (0 → NotPresent; >1 → Ambiguous);
// the paired sheet PNG resolves by deterministic convention and its raw-byte SHA equals
// package.sheet.content_sha256 (never a supplied hash trusted without recompute);
// package_content_sha256 recomputed inside Run. Evidence only under Library/GDAI/operations.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerC.Animation
{
    public enum GdaiAnimConsumeOutcome { NotPresent, Consumed, Failed }

    public class GdaiAnimConsumeResult
    {
        public GdaiAnimConsumeOutcome outcome;
        public string code;                 // stable code on Failed
        public string packagePath;
        public string sheetPath;
        public string receiptStatus;
        public GdaiAnimMaterializeResult materialize;
    }

    public static class GdaiAnimationBundleConsumer
    {
        public const string ImportRoot = "Assets/GDAI_Generated/Animation";
        public const string PackageSuffix = ".materialization.v1.json";

        // Run-class policy for the product paths (Complete-Sync step 10b + the resume seam). A real sync is
        // PRODUCTION (a TEST_ONLY package is rejected there — the producer never emits one into a Production
        // snapshot). The editor TEST HARNESS sets this to "TEST_ONLY" to exercise the chain with sealed
        // fixtures; Production code never assigns it, so it cannot leak TEST_ONLY into a real sync.
        public static string DefaultRunClass = "PRODUCTION";

        private static string ProjectRoot() => Directory.GetParent(Application.dataPath).FullName;
        private static string Abs(string assetPath) => Path.GetFullPath(Path.Combine(ProjectRoot(), assetPath));

        /// <summary>Deterministic sheet-PNG path paired with a package file (same stem, .sheet.png).</summary>
        public static string SheetPathFor(string packageAssetPath) =>
            packageAssetPath.Substring(0, packageAssetPath.Length - PackageSuffix.Length) + ".sheet.png";

        /// <summary>
        /// Consume the imported animation package (if any) for the current snapshot. `runClass` is the
        /// class of the RUN (TEST_ONLY harness / PRODUCTION sync). No write unless the materializer's
        /// own PRE-MUTATION guard approves. `snapshotId` is used for evidence only (binding is co-location).
        /// </summary>
        public static GdaiAnimConsumeResult Consume(string snapshotId, string runClass)
        {
            var r = new GdaiAnimConsumeResult();
            try
            {
                string dirAbs = Abs(ImportRoot);
                if (!Directory.Exists(dirAbs)) { r.outcome = GdaiAnimConsumeOutcome.NotPresent; r.code = "no_animation_import_dir"; return r; }

                // discovery: eligible = the file parses as a materialization package
                var candidates = new List<string>();
                foreach (var file in Directory.GetFiles(dirAbs, "*" + PackageSuffix, SearchOption.TopDirectoryOnly))
                {
                    try { GdaiAnimationPackage.Parse(File.ReadAllText(file)); }
                    catch (GdaiAnimGateException) { continue; }
                    candidates.Add(ImportRoot + "/" + Path.GetFileName(file));
                }
                if (candidates.Count == 0) { r.outcome = GdaiAnimConsumeOutcome.NotPresent; r.code = "no_animation_package_in_bundle"; return r; }
                if (candidates.Count > 1) { r.outcome = GdaiAnimConsumeOutcome.Failed; r.code = "CONSUME_AMBIGUOUS_PACKAGE:" + candidates.Count; WriteEvidence(r, snapshotId); return r; }

                string packagePath = candidates[0];
                r.packagePath = packagePath;
                string packageJson = File.ReadAllText(Abs(packagePath));
                var package = GdaiAnimationPackage.Parse(packageJson);

                // pairing: PNG resolves by convention; raw-byte SHA == package.sheet.content_sha256
                string sheetPath = SheetPathFor(packagePath);
                r.sheetPath = sheetPath;
                string sheetAbs = Abs(sheetPath);
                if (!File.Exists(sheetAbs)) { r.outcome = GdaiAnimConsumeOutcome.Failed; r.code = "CONSUME_SHEET_PAYLOAD_MISSING:" + sheetPath; WriteEvidence(r, snapshotId); return r; }
                string sheetSha = GdaiAnimJson.Sha256Hex(File.ReadAllBytes(sheetAbs));
                if (!string.Equals(sheetSha, package.sheet_content_sha256, StringComparison.Ordinal))
                { r.outcome = GdaiAnimConsumeOutcome.Failed; r.code = "CONSUME_SHEET_HASH_MISMATCH"; WriteEvidence(r, snapshotId); return r; }

                // materialize: raw imported JSON + imported PNG path → the real pipeline (Run recomputes
                // package_content_sha256 and fails closed on HASH_PACKAGE_CONTENT_MISMATCH itself).
                var m = GdaiAnimationMaterializer.Run(packageJson, sheetAbs, runClass);
                r.materialize = m;
                r.receiptStatus = m.receiptStatus;
                if (!m.ok)
                {
                    r.outcome = GdaiAnimConsumeOutcome.Failed;
                    r.code = m.error ?? (m.verifyFailures.FirstOrDefault() ?? "CONSUME_MATERIALIZE_FAILED");
                    WriteEvidence(r, snapshotId); return r;
                }
                r.outcome = GdaiAnimConsumeOutcome.Consumed;
                return r;
            }
            catch (GdaiAnimGateException e) { r.outcome = GdaiAnimConsumeOutcome.Failed; r.code = e.Code; WriteEvidence(r, snapshotId); return r; }
            catch (Exception e) { r.outcome = GdaiAnimConsumeOutcome.Failed; r.code = "CONSUME_UNEXPECTED:" + e.Message; WriteEvidence(r, snapshotId); return r; }
        }

        private static void WriteEvidence(GdaiAnimConsumeResult r, string snapshotId)
        {
            try
            {
                string dir = Path.Combine(ProjectRoot(), "Library", "GDAI", "operations");
                Directory.CreateDirectory(dir);
                var rec = new JObject
                {
                    ["decision"] = "ANIMATION_CONSUME_" + r.outcome,
                    ["code"] = r.code, ["snapshot_id"] = snapshotId,
                    ["package_path"] = r.packagePath, ["sheet_path"] = r.sheetPath,
                    ["at"] = DateTime.UtcNow.ToString("o"),
                };
                File.WriteAllText(Path.Combine(dir, "anim-consume-" + DateTime.UtcNow.Ticks + ".json"), rec.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch { }
        }
    }
}
