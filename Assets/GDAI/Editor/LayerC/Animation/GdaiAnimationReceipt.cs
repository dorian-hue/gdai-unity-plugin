// =====================================================================================
// GDAI Unity Plugin · T4 Stage-1A (Gate A) · Animation hard receipt (0E-07 Step 9).
//
// Check-row discipline mirrors the playable receipt: independent facts, non-vacuous, a
// receipt that cannot fail is not a receipt. Written ONLY after guard approval (it is a
// plan-listed product write); on guard refusal the ONLY evidence is Library/GDAI/operations.
// Stage-1A event facts are METADATA-ONLY readback (0E-04 #9): the receipt records the
// package's hit/audio/vfx event semantics; it makes NO runtime open/close/cancel claim.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerC.Animation
{
    [Serializable] public class GdaiAnimReceiptCheck { public string key; public string actual; public bool pass; }

    [Serializable]
    public class GdaiAnimReceiptModel
    {
        public string schema_version = "gdai.unity.animation_receipt.v1";
        public string status;                       // PASS | FAIL
        public string package_id;
        public string package_class;
        public string animation_profile_id;
        public string entity_id;
        public string package_content_sha256;
        public string planned_write_set_sha256;
        public string prior_manifest_identity_sha256;
        public int manual_assembly_steps;           // must be 0
        public string generated_at;
        public List<GdaiAnimReceiptCheck> checks = new List<GdaiAnimReceiptCheck>();
        public List<string> failures = new List<string>();
    }

    public static class GdaiAnimationReceipt
    {
        public static string ReceiptAssetPath => GdaiPreMutationGuard.NormalizedReceiptPath;

        /// <summary>Compose + write the receipt (plan-gated). Returns the final status string.</summary>
        public static string Write(GdaiGuardApproval approval, GdaiAnimationPackage p,
            GdaiAnimationAssetsSection section, List<string> verifyFailures)
        {
            approval.AssertInPlan(ReceiptAssetPath); // Phase 5: receipt is itself a planned write

            var m = new GdaiAnimReceiptModel
            {
                package_id = p.package_id,
                package_class = p.package_class,
                animation_profile_id = p.profile_id,
                entity_id = p.entity_id,
                package_content_sha256 = approval.PackageContentSha256,
                planned_write_set_sha256 = approval.PlannedWriteSetSha256,
                prior_manifest_identity_sha256 = approval.PriorManifestIdentitySha256,
                manual_assembly_steps = 0,
                generated_at = DateTime.UtcNow.ToString("o"),
            };
            void Check(string key, string actual, bool pass)
            {
                m.checks.Add(new GdaiAnimReceiptCheck { key = key, actual = actual, pass = pass });
                if (!pass) m.failures.Add(key + " · " + actual);
            }

            // guard row (an approved run reaching here has, by construction, an approval binding)
            Check("animation_pre_mutation_guard", "approved:" + approval.PlannedWriteSetSha256, true);

            // independent readback facts (never builder return values)
            var sheet = section.raw_sheets.FirstOrDefault();
            string liveSheetGuid = sheet != null ? AssetDatabase.AssetPathToGUID(sheet.path) : null;
            Check("sheet_imported", sheet?.path + " guid=" + liveSheetGuid, sheet != null && !string.IsNullOrEmpty(liveSheetGuid) && liveSheetGuid == sheet.guid);

            int liveSprites = sheet == null ? 0 : AssetDatabase.LoadAllAssetRepresentationsAtPath(sheet.path).OfType<Sprite>()
                .Count(s => s.name.StartsWith("GDAI__", StringComparison.Ordinal));
            Check("sprites_sliced", liveSprites + "/" + p.cells.Count, liveSprites == p.cells.Count && p.cells.Count > 0);

            int liveClips = section.clips.Count(c => AssetDatabase.LoadAssetAtPath<AnimationClip>(c.path) != null);
            Check("clips_built", liveClips + "/" + p.clips.Count, liveClips == p.clips.Count && p.clips.Count > 0);

            var ctrlRec = section.controllers.FirstOrDefault();
            var ctrl = ctrlRec != null ? AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ctrlRec.path) : null;
            int liveStates = ctrl != null ? ctrl.layers[0].stateMachine.states.Length : 0;
            Check("controller_states", liveStates + "/" + p.clips.Count, ctrl != null && liveStates == p.clips.Count);

            var manifest = GdaiPlayableOwnershipManifest.Load();
            Check("manifest_v2_written",
                manifest?.schema_version + " sprites=" + (manifest?.animation_assets?.sprites?.Count ?? 0),
                manifest?.schema_version == GdaiPlayableAssetsManifest.SchemaVersionV2 &&
                (manifest?.animation_assets?.sprites?.Count ?? 0) == p.cells.Count);

            Check("animation_profile_id_recorded", manifest?.animation_assets?.materialization?.animation_profile_id,
                manifest?.animation_assets?.materialization?.animation_profile_id == p.profile_id);

            // 0D-03 diff facts — Stage 1A wall: removals ≡ ∅
            var diff = section.reslice_diff;
            Check("reslice_survivors", diff.survivors.Count.ToString(), true);
            Check("reslice_additions", diff.additions.Count.ToString(), true);
            Check("reslice_removals_empty", diff.removals.Count.ToString(), diff.removals.Count == 0);

            // event METADATA readback only (no runtime claim — Stage 2 owns runtime hit-window behavior)
            int evCount = p.clips.Sum(c => c.events.Count);
            Check("hit_event_metadata_readback_only",
                "events=" + evCount + " (OPEN/CLOSE/POINT recorded as data; no runtime window claimed)", true);

            Check("verify_animation_assets", verifyFailures.Count == 0 ? "ok" : verifyFailures[0], verifyFailures.Count == 0);
            foreach (var f in verifyFailures) m.failures.Add(f);

            m.status = m.failures.Count == 0 ? "PASS" : "FAIL";

            string abs = Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReceiptAssetPath));
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            string tmp = abs + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(m, Formatting.Indented));
            if (File.Exists(abs)) File.Replace(tmp, abs, null);
            else File.Move(tmp, abs);
            AssetDatabase.ImportAsset(ReceiptAssetPath);
            return m.status;
        }

        public static GdaiAnimReceiptModel Load()
        {
            try
            {
                string abs = Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, ReceiptAssetPath));
                return File.Exists(abs) ? JsonConvert.DeserializeObject<GdaiAnimReceiptModel>(File.ReadAllText(abs)) : null;
            }
            catch { return null; }
        }
    }
}
