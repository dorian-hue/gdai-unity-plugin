// =====================================================================================
// GDAI Unity Plugin · T4 Stage-1A (Gate A) · VerifyAnimationAssets (0D-06 + 0E-03/0E-05).
//
// Closes recon erratum E1: flat FILE-assets (sheet/sprites/clips/controller) get a real
// forward+reverse ownership verification the playable Verify() never gave them.
//   forward: every record → live GUID at the recorded path, expected type, sprite_name +
//            file_id resolve, every content_fingerprint RECOMPUTED (no bypass);
//   reverse: every live GDAI__-sprite under recorded sheets and every labelled owned asset
//            under the Animations root must be recorded — a stamp without a record is
//            FLAGGED (VERIFY_ANIM_UNRECORDED_OWNED_STAMP), NEVER deleted (label/userData
//            alone never authorize overwrite/deletion — operator ruling).
// Non-vacuous: locators assert they found items before judging. This class never mutates.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerC.Animation
{
    public static class GdaiVerifyAnimationAssets
    {
        public static bool Verify(GdaiPlayableAssetsManifest manifest, List<string> failures)
        {
            int before = failures.Count;
            var a = manifest?.animation_assets;
            if (a == null) { failures.Add("VERIFY_ANIM_VACUOUS:section_absent"); return false; }
            if (a.materialization == null) { failures.Add("VERIFY_ANIM_VACUOUS:pin_absent"); return false; }
            if (a.raw_sheets.Count == 0 || a.sprites.Count == 0 || a.clips.Count == 0 || a.controllers.Count == 0)
            { failures.Add("VERIFY_ANIM_VACUOUS:empty_owned_set"); return false; }
            if (a.materialization.package_schema != GdaiAnimationPackage.SchemaId)
                failures.Add("VERIFY_ANIM_PIN_PACKAGE_SCHEMA:" + a.materialization.package_schema);
            if (!string.Equals(a.materialization.snapshot_id, manifest.snapshot_id, StringComparison.Ordinal))
                failures.Add("VERIFY_ANIM_PIN_SNAPSHOT_MISMATCH");

            // ── forward: sheets ──
            foreach (var s in a.raw_sheets)
            {
                if (!ForwardResolve(s.path, s.guid, typeof(Texture2D), "raw_sheet", failures)) continue;
                string fp = GdaiAnimJson.Sha256Hex(File.ReadAllBytes(Abs(s.path)));
                if (!string.Equals(fp, s.content_fingerprint, StringComparison.Ordinal))
                    failures.Add("VERIFY_ANIM_HUMAN_EDITED:raw_sheet:" + s.path);
                if (s.label != GdaiAnimationPackage.OwnershipLabel)
                    failures.Add("VERIFY_ANIM_LABEL_MISSING:record:" + s.path);
            }

            // ── forward: sprites (verbatim name + string file_id + geometry fingerprint) ──
            var liveByName = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            foreach (var sheet in a.raw_sheets)
                foreach (var sp in AssetDatabase.LoadAllAssetRepresentationsAtPath(sheet.path).OfType<Sprite>())
                    liveByName[sp.name] = sp;
            if (liveByName.Count == 0) failures.Add("VERIFY_ANIM_VACUOUS:no_live_sprites");
            var sheetFpByPath = a.raw_sheets.ToDictionary(s => s.path, s => s.content_fingerprint, StringComparer.Ordinal);
            foreach (var rec in a.sprites)
            {
                if (!liveByName.TryGetValue(rec.sprite_name, out var live))
                { failures.Add("VERIFY_ANIM_SPRITE_NAME_MISMATCH:" + rec.sprite_name); continue; }
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(live, out string g, out long fid) ||
                    !string.Equals(fid.ToString(System.Globalization.CultureInfo.InvariantCulture), rec.file_id, StringComparison.Ordinal))
                { failures.Add("VERIFY_ANIM_SUBSPRITE_FILEID_UNRESOLVED:" + rec.sprite_name); continue; }
                if (!string.Equals(g, rec.sheet_guid, StringComparison.Ordinal))
                { failures.Add("VERIFY_ANIM_GUID_MISMATCH:sprite_parent:" + rec.sprite_name); continue; }
                float px = live.rect.width > 0 ? live.pivot.x / live.rect.width : 0f;
                float py = live.rect.height > 0 ? live.pivot.y / live.rect.height : 0f;
                string sheetFp = sheetFpByPath.TryGetValue(rec.sheet_path, out var v) ? v : null;
                string fp = GdaiAnimFingerprint.Sprite(sheetFp, rec.sprite_name,
                    (int)live.rect.x, (int)live.rect.y, (int)live.rect.width, (int)live.rect.height,
                    px, py, GdaiAnimFingerprint.AlignmentFromPivot(px, py));
                if (!string.Equals(fp, rec.content_fingerprint, StringComparison.Ordinal))
                    failures.Add("VERIFY_ANIM_HUMAN_EDITED:sprite:" + rec.sprite_name);
            }

            // ── forward: clips (file fingerprint + every object-ref keyframe resolves in order) ──
            var clipGuidById = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var rec in a.clips)
            {
                if (!ForwardResolve(rec.path, rec.guid, typeof(AnimationClip), "clip", failures)) continue;
                clipGuidById[rec.clip_id] = rec.guid;
                string fp = GdaiAnimJson.Sha256Hex(File.ReadAllBytes(Abs(rec.path)));
                if (!string.Equals(fp, rec.content_fingerprint, StringComparison.Ordinal))
                { failures.Add("VERIFY_ANIM_HUMAN_EDITED:clip:" + rec.path); continue; }
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(rec.path);
                var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                var keys = bindings.Length == 1 ? AnimationUtility.GetObjectReferenceCurve(clip, bindings[0]) : null;
                if (keys == null || keys.Length != rec.frame_sprite_names.Count)
                { failures.Add("VERIFY_ANIM_CLIP_REF_BROKEN:count:" + rec.clip_id); continue; }
                for (int i = 0; i < keys.Length; i++)
                {
                    var spr = keys[i].value as Sprite;
                    if (spr == null || !string.Equals(spr.name, rec.frame_sprite_names[i], StringComparison.Ordinal))
                    { failures.Add("VERIFY_ANIM_CLIP_REF_BROKEN:" + rec.clip_id + ":f" + i); break; }
                }
            }

            // ── forward: controller (fingerprint + data-bound states) ──
            foreach (var rec in a.controllers)
            {
                if (!ForwardResolve(rec.path, rec.guid, typeof(AnimatorController), "controller", failures)) continue;
                string fp = GdaiAnimJson.Sha256Hex(File.ReadAllBytes(Abs(rec.path)));
                if (!string.Equals(fp, rec.content_fingerprint, StringComparison.Ordinal))
                { failures.Add("VERIFY_ANIM_HUMAN_EDITED:controller:" + rec.path); continue; }
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(rec.path);
                var liveStates = ctrl.layers[0].stateMachine.states.Select(s => s.state).ToDictionary(s => s.name, StringComparer.Ordinal);
                foreach (var st in rec.states)
                {
                    if (!liveStates.TryGetValue(st.state_name, out var liveState) || liveState.motion == null)
                    { failures.Add("VERIFY_ANIM_CONTROLLER_STATE_MISMATCH:" + st.state_name); continue; }
                    if (clipGuidById.TryGetValue(st.clip_id, out var wantGuid) && !string.IsNullOrEmpty(st.clip_guid) &&
                        !string.Equals(st.clip_guid, wantGuid, StringComparison.Ordinal))
                        failures.Add("VERIFY_ANIM_CONTROLLER_STATE_MISMATCH:guid:" + st.state_name);
                }
                var liveDefault = ctrl.layers[0].stateMachine.defaultState;
                if ((liveDefault != null ? liveDefault.name : null) != rec.default_state)
                    failures.Add("VERIFY_ANIM_CONTROLLER_STATE_MISMATCH:default:" + rec.default_state);
            }

            // ── reverse: live GDAI__ sprites under recorded sheets must be recorded ──
            var recordedNames = new HashSet<string>(a.sprites.Select(s => s.sprite_name), StringComparer.Ordinal);
            foreach (var kv in liveByName)
                if (kv.Key.StartsWith("GDAI__", StringComparison.Ordinal) && !recordedNames.Contains(kv.Key))
                    failures.Add("VERIFY_ANIM_UNRECORDED_SPRITE:" + kv.Key);

            // ── reverse: labelled owned assets under the Animations root must be recorded ──
            var recordedPaths = new HashSet<string>(
                a.raw_sheets.Select(s => s.path).Concat(a.clips.Select(c => c.path)).Concat(a.controllers.Select(c => c.path)),
                StringComparer.Ordinal);
            foreach (var guid in AssetDatabase.FindAssets("l:" + GdaiAnimationPackage.OwnershipLabel,
                new[] { GdaiAnimationPackage.TargetRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!recordedPaths.Contains(path))
                    failures.Add("VERIFY_ANIM_UNRECORDED_OWNED_STAMP:" + path); // flag — NEVER delete
            }

            return failures.Count == before;
        }

        private static bool ForwardResolve(string path, string guid, Type type, string kind, List<string> failures)
        {
            string live = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(live)) { failures.Add("VERIFY_ANIM_ASSET_MISSING:" + kind + ":" + path); return false; }
            if (!string.Equals(live, guid, StringComparison.Ordinal)) { failures.Add("VERIFY_ANIM_GUID_MISMATCH:" + kind + ":" + path); return false; }
            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj == null || !type.IsInstanceOfType(obj)) { failures.Add("VERIFY_ANIM_TYPE_MISMATCH:" + kind + ":" + path); return false; }
            return true;
        }

        private static string Abs(string assetPath) =>
            Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath));
    }
}
