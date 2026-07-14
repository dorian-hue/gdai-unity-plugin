// =====================================================================================
// GDAI Unity Plugin · T4 Stage-1A (Gate A) · PRE-MUTATION ownership guard (0E-02).
//
// THE SOLE WRITE PRECONDITION: no AssetDatabase import / create / overwrite / label /
// userData / manifest / receipt write happens until Evaluate(...) returns APPROVED. The
// approval binds package_content_sha256 + prior-manifest identity + planned_write_set_sha256
// and the plan becomes IMMUTABLE — any attempted write not in the approved plan throws
// STOP_WRITE_NOT_IN_APPROVED_PLAN before the write executes (Phase 5 clarification).
//
// On REFUSED: Assets writes = 0, manifest writes = 0, product receipt writes = 0, label/
// userData writes = 0. Failure evidence goes ONLY to Library/GDAI/operations/**.
// Ordering (first violation wins): Phase 0 admission → 1 prior manifest+pins → 2 recorded→
// live → 3 sprite identity+hashes → 4 path scope+authority. Stage 1A: plan.removals ≡ ∅.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerC.Animation
{
    public enum GdaiPlannedWriteKind { Sheet, SpriteMeta, Clip, Controller, Manifest, Receipt }

    [Serializable]
    public class GdaiPlannedWrite
    {
        public string kind;
        public string path;
        public static GdaiPlannedWrite Of(GdaiPlannedWriteKind k, string path) =>
            new GdaiPlannedWrite { kind = k.ToString().ToLowerInvariant(), path = path };
    }

    /// <summary>Immutable approval: the ONLY object that authorizes writes.</summary>
    public sealed class GdaiGuardApproval
    {
        public readonly string PackageContentSha256;
        public readonly string PriorManifestIdentitySha256; // sha256 of prior manifest file bytes, or "none"
        public readonly string PlannedWriteSetSha256;
        private readonly HashSet<string> _approvedPaths;
        public IReadOnlyCollection<string> ApprovedPaths => _approvedPaths;

        internal GdaiGuardApproval(string pkgSha, string priorSha, string planSha, IEnumerable<string> paths)
        {
            PackageContentSha256 = pkgSha;
            PriorManifestIdentitySha256 = priorSha;
            PlannedWriteSetSha256 = planSha;
            _approvedPaths = new HashSet<string>(paths, StringComparer.Ordinal);
        }

        /// <summary>Phase 5: every mutation calls this FIRST. Non-membership aborts before the write.</summary>
        public void AssertInPlan(string assetPath)
        {
            if (!_approvedPaths.Contains(assetPath))
                throw new GdaiAnimGateException("STOP_WRITE_NOT_IN_APPROVED_PLAN:" + assetPath);
        }
    }

    public static class GdaiPreMutationGuard
    {
        // The animation hard receipt lives beside the manifest (Manifests/ dir) — a plan-listed,
        // guard-scoped product write. On refusal it is NEVER written (Library operations only).
        public static string NormalizedReceiptPath => "Assets/GDAI_Project/Generated/Manifests/GDAIAnimationReceipt.json";

        private static string ProjectRoot() => Directory.GetParent(Application.dataPath).FullName;
        private static string Abs(string assetPath) => Path.GetFullPath(Path.Combine(ProjectRoot(), assetPath));

        /// <summary>
        /// Build the Stage-1A fixed-set write plan for a package (pure; no mutation): sheet + clips +
        /// controller + manifest + receipt. Sprite metadata rides the sheet importer (same path).
        /// </summary>
        public static List<GdaiPlannedWrite> BuildFixedSetPlan(GdaiAnimationPackage p)
        {
            var plan = new List<GdaiPlannedWrite> { GdaiPlannedWrite.Of(GdaiPlannedWriteKind.Sheet, p.SheetAssetPath) };
            foreach (var c in p.clips.OrderBy(c => c.clip_id, StringComparer.Ordinal))
                plan.Add(GdaiPlannedWrite.Of(GdaiPlannedWriteKind.Clip, p.ClipAssetPath(c.clip_id)));
            plan.Add(GdaiPlannedWrite.Of(GdaiPlannedWriteKind.Controller, p.ControllerAssetPath));
            plan.Add(GdaiPlannedWrite.Of(GdaiPlannedWriteKind.Manifest, GdaiPlayableOwnershipManifest.ManifestPath));
            plan.Add(GdaiPlannedWrite.Of(GdaiPlannedWriteKind.Receipt, NormalizedReceiptPath));
            return plan;
        }

        public static string PlannedWriteSetSha256(List<GdaiPlannedWrite> plan)
        {
            var arr = new JArray(plan.OrderBy(w => w.path, StringComparer.Ordinal)
                .Select(w => new JObject { ["kind"] = w.kind, ["path"] = w.path }));
            return GdaiAnimJson.JcsSha256Hex(new JObject { ["writes"] = arr });
        }

        // §4.1 exact path-scope closure: the three owned dirs + the single manifest + the receipt.
        private static readonly string[] AllowedDirPrefixes =
        {
            "Assets/GDAI_Project/Generated/Animations/Sprites/",
            "Assets/GDAI_Project/Generated/Animations/Clips/",
            "Assets/GDAI_Project/Generated/Animations/Controllers/",
        };

        private static bool InScope(string assetPath)
        {
            if (assetPath == GdaiPlayableOwnershipManifest.ManifestPath) return true;
            if (assetPath == NormalizedReceiptPath) return true;
            if (assetPath.Contains("..")) return false; // no escape
            return AllowedDirPrefixes.Any(p => assetPath.StartsWith(p, StringComparison.Ordinal));
        }

        /// <summary>
        /// THE guard. Performs NO mutation. Returns APPROVED (with the immutable plan binding) or
        /// REFUSED with the FIRST violated code; on refusal an operations record is written under
        /// Library/GDAI/operations/** only.
        /// </summary>
        public static bool Evaluate(GdaiAnimationPackage package, string runClass,
            List<GdaiPlannedWrite> plan, List<string> plannedRemovals,
            out GdaiGuardApproval approval, out string refusedCode)
        {
            approval = null;
            refusedCode = null;
            try
            {
                // ── Phase 0 · package admission (gates + recomputed package hash; 0E-01/0E-05) ──
                package.ValidateForMaterialization(runClass);

                // ── Phase 1 · prior manifest + identity pins (0E-03) ──
                string manifestAbs = Abs(GdaiPlayableOwnershipManifest.ManifestPath);
                if (!File.Exists(manifestAbs)) throw new GdaiAnimGateException("GUARD_MANIFEST_LOAD_FAILED:absent");
                var manifest = GdaiPlayableOwnershipManifest.Load();
                if (manifest == null) throw new GdaiAnimGateException("GUARD_MANIFEST_LOAD_FAILED:unreadable");
                if (manifest.schema_version != GdaiPlayableAssetsManifest.SchemaVersion &&
                    manifest.schema_version != GdaiPlayableAssetsManifest.SchemaVersionV2)
                    throw new GdaiAnimGateException("GUARD_MANIFEST_SCHEMA_UNSUPPORTED:" + manifest.schema_version);

                string priorSha = GdaiAnimJson.Sha256Hex(File.ReadAllBytes(manifestAbs));
                var prior = manifest.animation_assets; // null ⇒ first materialization (v1 or animation-less v2)
                if (prior?.materialization != null)
                {
                    var pin = prior.materialization;
                    if (pin.package_schema != GdaiAnimationPackage.SchemaId) throw new GdaiAnimGateException("GUARD_PIN_PACKAGE_SCHEMA:" + pin.package_schema);
                    if (!string.Equals(pin.snapshot_id, manifest.snapshot_id, StringComparison.Ordinal)) throw new GdaiAnimGateException("GUARD_PIN_SNAPSHOT_MISMATCH");
                    if (!string.Equals(pin.animation_profile_id, package.profile_id, StringComparison.Ordinal)) throw new GdaiAnimGateException("GUARD_PIN_PROFILE_UNSTABLE:" + pin.animation_profile_id + "->" + package.profile_id);
                    if (!string.Equals(pin.entity_id, package.entity_id, StringComparison.Ordinal)) throw new GdaiAnimGateException("GUARD_PIN_PROFILE_UNSTABLE:entity:" + pin.entity_id + "->" + package.entity_id);

                    // ── Phase 2 · recorded → live resolution for every record this run touches ──
                    foreach (var rec in prior.raw_sheets)
                        RequireRecorded(rec.path, rec.guid, typeof(Texture2D), "raw_sheet");
                    foreach (var rec in prior.clips)
                        RequireRecorded(rec.path, rec.guid, typeof(AnimationClip), "clip");
                    foreach (var rec in prior.controllers)
                        RequireRecorded(rec.path, rec.guid, typeof(UnityEditor.Animations.AnimatorController), "controller");

                    // ── Phase 3 · sprite identity (file_id + verbatim name) + content hashes ──
                    foreach (var sheet in prior.raw_sheets)
                    {
                        string liveSheetSha = GdaiAnimJson.Sha256Hex(File.ReadAllBytes(Abs(sheet.path)));
                        // surviving same sheet must still match its pinned content (0E-02 GUARD_PIN_SHEET_CONTENT
                        // / per-kind fingerprint — for raw_sheet the two are the same hash by construction)
                        if (!string.Equals(liveSheetSha, sheet.content_fingerprint, StringComparison.Ordinal))
                            throw new GdaiAnimGateException("GUARD_FINGERPRINT_DRIFT:raw_sheet:" + sheet.path);
                        // (an incoming DIFFERENT sheet at the same recorded path is an overwrite the
                        //  manifest-GUID record authorizes — no extra check needed here)
                    }
                    var liveSprites = LoadSpriteIndex(prior.raw_sheets.Select(r => r.path));
                    var sheetByPath = prior.raw_sheets.ToDictionary(r => r.path, r => r, StringComparer.Ordinal);
                    foreach (var s in prior.sprites)
                    {
                        if (!liveSprites.TryGetValue(s.sprite_name, out var live))
                            throw new GdaiAnimGateException("GUARD_SPRITE_NAME_NOT_FOUND:" + s.sprite_name);
                        if (!string.Equals(live.fileId.ToString(System.Globalization.CultureInfo.InvariantCulture), s.file_id, StringComparison.Ordinal))
                            throw new GdaiAnimGateException("GUARD_SPRITE_FILEID_UNRESOLVED:" + s.sprite_name);
                        if (!sheetByPath.TryGetValue(s.sheet_path, out var parentSheet))
                            throw new GdaiAnimGateException("GUARD_SPRITE_NAME_MISMATCH:orphan-sheet:" + s.sprite_name);
                        string fp = GdaiAnimFingerprint.Sprite(parentSheet.content_fingerprint, s.sprite_name,
                            live.rect.x, live.rect.y, live.rect.w, live.rect.h, live.pivotX, live.pivotY, live.alignment);
                        if (!string.Equals(fp, s.content_fingerprint, StringComparison.Ordinal))
                            throw new GdaiAnimGateException("GUARD_FINGERPRINT_DRIFT:sprite:" + s.sprite_name);
                    }
                    foreach (var c in prior.clips)
                    {
                        string fp = GdaiAnimJson.Sha256Hex(File.ReadAllBytes(Abs(c.path)));
                        if (!string.Equals(fp, c.content_fingerprint, StringComparison.Ordinal))
                            throw new GdaiAnimGateException("GUARD_FINGERPRINT_DRIFT:clip:" + c.path);
                    }
                    foreach (var c in prior.controllers)
                    {
                        string fp = GdaiAnimJson.Sha256Hex(File.ReadAllBytes(Abs(c.path)));
                        if (!string.Equals(fp, c.content_fingerprint, StringComparison.Ordinal))
                            throw new GdaiAnimGateException("GUARD_FINGERPRINT_DRIFT:controller:" + c.path);
                    }
                }

                // ── Phase 4 · exact path scope + authority ──
                foreach (var w in plan)
                {
                    if (!InScope(w.path)) throw new GdaiAnimGateException("GUARD_PATH_OUT_OF_SCOPE:" + w.path);
                    string abs = Abs(w.path);
                    if (!abs.StartsWith(Path.GetFullPath(ProjectRoot()), StringComparison.Ordinal))
                        throw new GdaiAnimGateException("GUARD_PATH_OUT_OF_SCOPE:escape:" + w.path);

                    if (w.kind == "manifest" || w.kind == "receipt") continue; // the two authorized files
                    if (File.Exists(abs))
                    {
                        bool recorded = prior != null && (
                            prior.raw_sheets.Any(r => r.path == w.path) ||
                            prior.clips.Any(r => r.path == w.path) ||
                            prior.controllers.Any(r => r.path == w.path));
                        if (!recorded)
                        {
                            var mainObj = AssetDatabase.LoadMainAssetAtPath(w.path);
                            bool stamped = false;
                            if (mainObj != null)
                                stamped = AssetDatabase.GetLabels(mainObj).Contains(GdaiAnimationPackage.OwnershipLabel);
                            var imp = AssetImporter.GetAtPath(w.path);
                            if (!stamped && imp != null && !string.IsNullOrEmpty(imp.userData) && imp.userData.Contains("gdai:owner:animation"))
                                stamped = true;
                            // label/userData ALONE never authorize overwrite (operator ruling): both cases refuse.
                            throw new GdaiAnimGateException(stamped
                                ? "GUARD_UNRECORDED_OWNED_STAMP:" + w.path
                                : "GUARD_TARGET_OCCUPIED_FOREIGN:" + w.path);
                        }
                    }
                }

                // Stage 1A scope wall: fixed set only — NO prune, NO shrink (0E-04).
                if (plannedRemovals != null && plannedRemovals.Count > 0)
                    throw new GdaiAnimGateException("STOP_STAGE_1A_SCOPE_BREACH:removals:" + plannedRemovals.Count);

                string planSha = PlannedWriteSetSha256(plan);
                approval = new GdaiGuardApproval(package.package_content_sha256, prior == null ? PriorNone(priorSha) : priorSha, planSha, plan.Select(w => w.path));
                return true;
            }
            catch (GdaiAnimGateException e)
            {
                refusedCode = e.Code;
                WriteRefusalRecord(package, e.Code);
                return false;
            }
        }

        // prior manifest EXISTS (playable) but has no animation section: bind to its file identity
        // anyway — the approval still pins the exact pre-write manifest bytes.
        private static string PriorNone(string manifestFileSha) => manifestFileSha;

        private struct LiveSprite { public long fileId; public (int x, int y, int w, int h) rect; public float pivotX, pivotY; public int alignment; }

        private static Dictionary<string, LiveSprite> LoadSpriteIndex(IEnumerable<string> sheetPaths)
        {
            var map = new Dictionary<string, LiveSprite>(StringComparer.Ordinal);
            foreach (var path in sheetPaths)
            {
                foreach (var obj in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                {
                    if (!(obj is Sprite s)) continue;
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(s, out _, out long fid)) continue;
                    float px = s.rect.width > 0 ? s.pivot.x / s.rect.width : 0f;
                    float py = s.rect.height > 0 ? s.pivot.y / s.rect.height : 0f;
                    map[s.name] = new LiveSprite
                    {
                        fileId = fid,
                        rect = ((int)s.rect.x, (int)s.rect.y, (int)s.rect.width, (int)s.rect.height),
                        pivotX = px,
                        pivotY = py,
                        // same pure pivot→alignment mapping the materializer uses at write time
                        alignment = GdaiAnimFingerprint.AlignmentFromPivot(px, py),
                    };
                }
            }
            return map;
        }

        private static void RequireRecorded(string path, string guid, Type type, string kind)
        {
            string live = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(live)) throw new GdaiAnimGateException("GUARD_RECORDED_GUID_UNRESOLVED:" + kind + ":" + path);
            if (!string.Equals(live, guid, StringComparison.Ordinal)) throw new GdaiAnimGateException("GUARD_RECORDED_PATH_GUID_MISMATCH:" + kind + ":" + path);
            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj == null || !type.IsInstanceOfType(obj)) throw new GdaiAnimGateException("GUARD_RECORDED_TYPE_MISMATCH:" + kind + ":" + path);
        }

        private static void WriteRefusalRecord(GdaiAnimationPackage p, string code)
        {
            try
            {
                string dir = Path.Combine(ProjectRoot(), "Library", "GDAI", "operations");
                Directory.CreateDirectory(dir);
                var rec = new JObject
                {
                    ["decision"] = "REFUSED",
                    ["code"] = code,
                    ["package_id"] = p?.package_id,
                    ["package_content_sha256"] = p?.package_content_sha256,
                    ["at"] = DateTime.UtcNow.ToString("o"),
                    ["writes_performed"] = 0,
                };
                File.WriteAllText(Path.Combine(dir, "anim-guard-" + DateTime.UtcNow.Ticks + ".json"), rec.ToString(Formatting.Indented));
            }
            catch { /* evidence write must never mask the refusal */ }
        }
    }
}
