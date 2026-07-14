// =====================================================================================
// GDAI Unity Plugin · T4 Stage-1A (Gate A) · Fixed-set Unity materializer.
//
// ONE importer for the normalized package ONLY — zero frame_map / batch_layout /
// column_actions / row_meaning handling (resolved upstream by the Flowcraft normalizer).
// Order (0E-07): fixture → gates → PRE-MUTATION GUARD → sheet → slice(cells[].sprite_name
// VERBATIM, never rename a survivor) → clips (event metadata NOT baked as AnimationEvents —
// Stage-1A metadata-only, 0E-04 #9) → controller → labels/userData → Manifest v2 → Verify →
// hard receipt. EVERY mutation calls approval.AssertInPlan(path) FIRST
// (STOP_WRITE_NOT_IN_APPROVED_PLAN). Stage 1A: fixed set, removals ≡ ∅ — no asset-removal
// API of any kind exists in this file (enforced by a source-scan test).
// =====================================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
#if GDAI_HAS_2D_SPRITE
using UnityEditor.U2D.Sprites;
#endif

namespace GDAI.Bridge.Editor.LayerC.Animation
{
    public class GdaiAnimMaterializeResult
    {
        public bool ok;
        public string error;                       // first GUARD_/GATE_/HASH_/STOP_/FIXTURE_ code on failure
        public GdaiGuardApproval approval;         // null when the guard refused
        public GdaiAnimationAssetsSection section; // the recorded section (written to Manifest v2)
        public List<string> verifyFailures = new List<string>();
        public string receiptStatus;
        public int survivors, additions;
    }

    public static class GdaiAnimationMaterializer
    {
        /// <summary>
        /// Stage-1A entry: materialize one normalized TEST_ONLY package whose golden sheet is at
        /// sourceSheetAbsolutePath. Fail-closed; performs ZERO writes unless the guard approved.
        /// </summary>
        public static GdaiAnimMaterializeResult Run(string packageJson, string sourceSheetAbsolutePath, string runClass)
        {
            var r = new GdaiAnimMaterializeResult();
            GdaiAnimationPackage p;
            try { p = GdaiAnimationPackage.Parse(packageJson); }
            catch (GdaiAnimGateException e) { r.error = e.Code; return r; }

#if !GDAI_HAS_2D_SPRITE
            r.error = "STOP_2D_SPRITE_PACKAGE_MISSING"; // environment prerequisite; nothing written
            return r;
#else
            try
            {
                // Option B fixture verification (0F erratum): binary golden + pixel semantics + grid
                // agreement. The materializer refuses to import a sheet that fails its contract.
                byte[] sheetBytes = File.Exists(sourceSheetAbsolutePath) ? File.ReadAllBytes(sourceSheetAbsolutePath) : null;
                if (sheetBytes == null) { r.error = "FIXTURE_FILE_HASH_DRIFT:absent-source"; return r; }
                string srcSha = GdaiAnimJson.Sha256Hex(sheetBytes);
                if (!string.Equals(srcSha, p.sheet_content_sha256, StringComparison.Ordinal))
                { r.error = "HASH_SHEET_MISMATCH:" + srcSha; return r; }

                // ── THE PRE-MUTATION GUARD (sole write precondition) ──
                var plan = GdaiPreMutationGuard.BuildFixedSetPlan(p);
                if (!GdaiPreMutationGuard.Evaluate(p, runClass, plan, new List<string>(), out var approval, out var refused))
                { r.error = refused; return r; }
                r.approval = approval;

                // prior owned sprite set (for the 0D-03 diff), captured BEFORE this run's writes
                var manifestBefore = GdaiPlayableOwnershipManifest.Load();
                var prevNames = new HashSet<string>(
                    manifestBefore?.animation_assets?.sprites?.Select(s => s.sprite_name) ?? Enumerable.Empty<string>(),
                    StringComparer.Ordinal);

                // ── sheet bytes + importer + slice (verbatim names; stable deterministic spriteIDs) ──
                approval.AssertInPlan(p.SheetAssetPath);
                EnsureFolder(Path.GetDirectoryName(p.SheetAssetPath).Replace('\\', '/'));
                string sheetAbs = Abs(p.SheetAssetPath);
                bool bytesIdentical = File.Exists(sheetAbs) && GdaiAnimJson.Sha256Hex(File.ReadAllBytes(sheetAbs)) == srcSha;
                if (!bytesIdentical) File.WriteAllBytes(sheetAbs, sheetBytes);
                AssetDatabase.ImportAsset(p.SheetAssetPath, ImportAssetOptions.ForceSynchronousImport);

                var ti = (TextureImporter)AssetImporter.GetAtPath(p.SheetAssetPath);
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Multiple;
                ti.spritePixelsPerUnit = p.pixels_per_unit;
                ti.filterMode = p.filter_mode == "Bilinear" ? FilterMode.Bilinear : FilterMode.Point;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.userData = "gdai:owner:animation:pkg:" + p.package_id + ":sheet:GDAI__" + p.Scope; // corroboration only

                int H = p.rows * p.cell_height;
                var factories = new SpriteDataProviderFactories();
                factories.Init();
                var dp = factories.GetSpriteEditorDataProviderFromObject(ti);
                dp.InitSpriteEditorDataProvider();
                int alignment = GdaiAnimFingerprint.AlignmentFromPivot(p.pivot_x, p.pivot_y);
                var rects = p.cells.OrderBy(c => c.sprite_name, StringComparer.Ordinal).Select(c => new SpriteRect
                {
                    name = c.sprite_name,                                   // VERBATIM — the fileID anchor
                    spriteID = DeterministicSpriteId(c.sprite_name),        // stable; never regenerated
                    rect = new Rect(c.column * p.cell_width, H - (c.row + 1) * p.cell_height, p.cell_width, p.cell_height),
                    pivot = new Vector2(p.pivot_x, p.pivot_y),
                    alignment = (SpriteAlignment)alignment,
                }).ToArray();
                dp.SetSpriteRects(rects);
                var nameFileId = dp.GetDataProvider<ISpriteNameFileIdDataProvider>();
                if (nameFileId != null)
                    nameFileId.SetNameFileIdPairs(rects.Select(sr => new SpriteNameFileIdPair(sr.name, sr.spriteID)).ToList());
                dp.Apply();
                ti.SaveAndReimport();

                var mainTex = AssetDatabase.LoadMainAssetAtPath(p.SheetAssetPath);
                AssetDatabase.SetLabels(mainTex, new[] { GdaiAnimationPackage.OwnershipLabel });

                // readback: live sprites by verbatim name
                var liveSprites = AssetDatabase.LoadAllAssetRepresentationsAtPath(p.SheetAssetPath)
                    .OfType<Sprite>().ToDictionary(s => s.name, s => s, StringComparer.Ordinal);
                foreach (var c in p.cells)
                    if (!liveSprites.ContainsKey(c.sprite_name)) { r.error = "VERIFY_ANIM_SPRITE_NAME_MISMATCH:post-slice:" + c.sprite_name; return r; }

                // ── clips (update-in-place; object-ref curves; NO AnimationEvents — metadata only) ──
                var clipAssets = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
                foreach (var clip in p.clips.OrderBy(c => c.clip_id, StringComparer.Ordinal))
                {
                    string clipPath = p.ClipAssetPath(clip.clip_id);
                    approval.AssertInPlan(clipPath);
                    EnsureFolder(Path.GetDirectoryName(clipPath).Replace('\\', '/'));
                    var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                    var a = existing != null ? existing : new AnimationClip();
                    a.frameRate = clip.nominal_fps;
                    var frames = p.cells.Where(c => c.clip_id == clip.clip_id).OrderBy(c => c.frame_index).ToList();
                    var keys = frames.Select(f => new ObjectReferenceKeyframe
                    {
                        time = f.frame_index / clip.nominal_fps,
                        value = liveSprites[f.sprite_name],
                    }).ToArray();
                    var binding = new EditorCurveBinding { type = typeof(SpriteRenderer), path = "", propertyName = "m_Sprite" };
                    AnimationUtility.SetObjectReferenceCurve(a, binding, keys);
                    var settings = AnimationUtility.GetAnimationClipSettings(a);
                    settings.loopTime = clip.loop;
                    AnimationUtility.SetAnimationClipSettings(a, settings);
                    if (existing == null) AssetDatabase.CreateAsset(a, clipPath);
                    else EditorUtility.SetDirty(a);
                    AssetDatabase.SetLabels(AssetDatabase.LoadMainAssetAtPath(clipPath) ?? a, new[] { GdaiAnimationPackage.OwnershipLabel });
                    clipAssets[clip.clip_id] = a;
                }

                // ── controller (update-in-place; one state per clip; deterministic default) ──
                approval.AssertInPlan(p.ControllerAssetPath);
                EnsureFolder(Path.GetDirectoryName(p.ControllerAssetPath).Replace('\\', '/'));
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(p.ControllerAssetPath);
                if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(p.ControllerAssetPath);
                var sm = ctrl.layers[0].stateMachine;
                string defaultClipId = p.clips.OrderBy(c => c.clip_id, StringComparer.Ordinal).First().clip_id;
                foreach (var clip in p.clips.OrderBy(c => c.clip_id, StringComparer.Ordinal))
                {
                    var st = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == clip.clip_id);
                    if (st == null) st = sm.AddState(clip.clip_id);
                    st.motion = clipAssets[clip.clip_id];
                    if (clip.clip_id == defaultClipId) sm.defaultState = st;
                }
                AssetDatabase.SetLabels(ctrl, new[] { GdaiAnimationPackage.OwnershipLabel });
                EditorUtility.SetDirty(ctrl);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // ── independent readback → Manifest v2 records (0E-03) ──
                var section = BuildSection(p, srcSha, prevNames, out var removals);
                if (removals.Count > 0) { r.error = "STOP_STAGE_1A_SCOPE_BREACH:removals:" + removals.Count; return r; }

                approval.AssertInPlan(GdaiPlayableOwnershipManifest.ManifestPath);
                if (!WriteAnimationSection(section, out var werr)) { r.error = "GUARD_MANIFEST_LOAD_FAILED:write:" + werr; return r; }
                r.section = section;
                r.survivors = section.reslice_diff.survivors.Count;
                r.additions = section.reslice_diff.additions.Count;

                // ── Verify (forward+reverse, recomputed hashes) + hard receipt ──
                var manifestNow = GdaiPlayableOwnershipManifest.Load();
                GdaiVerifyAnimationAssets.Verify(manifestNow, r.verifyFailures);
                r.receiptStatus = GdaiAnimationReceipt.Write(approval, p, section, r.verifyFailures);
                r.ok = r.verifyFailures.Count == 0 && r.receiptStatus == "PASS";
                if (!r.ok && r.error == null) r.error = r.verifyFailures.FirstOrDefault() ?? "RECEIPT_NOT_PASS";
                return r;
            }
            catch (GdaiAnimGateException e) { r.error = e.Code; return r; }
            catch (Exception e) { r.error = "MATERIALIZER_UNEXPECTED:" + e.Message; return r; }
#endif
        }

#if GDAI_HAS_2D_SPRITE
        private static GdaiAnimationAssetsSection BuildSection(GdaiAnimationPackage p, string sheetSha,
            HashSet<string> prevNames, out List<string> removals)
        {
            var section = new GdaiAnimationAssetsSection();
            var manifest = GdaiPlayableOwnershipManifest.Load();
            string manifestAbs = Abs(GdaiPlayableOwnershipManifest.ManifestPath);
            section.materialization = new GdaiAnimMaterializationPin
            {
                package_schema = GdaiAnimationPackage.SchemaId,
                package_class = p.package_class,
                package_id = p.package_id,
                animation_profile_id = p.profile_id,
                animation_profile_version = p.profile_version,
                entity_id = p.entity_id,
                snapshot_id = manifest?.snapshot_id,
                adapter_version = p.adapter_version,
                source_axis_origin = p.source_axis_origin,
                package_content_sha256 = GdaiAnimJson.PackageContentSha256(p.Raw), // recomputed, never trusted
                license_status = p.license_status,
                adoption_event_id = p.adoption_event_id,
                qa_event_id = p.qa_event_id,
                materialized_at = DateTime.UtcNow.ToString("o"),
            };

            string sheetGuid = AssetDatabase.AssetPathToGUID(p.SheetAssetPath);
            section.raw_sheets.Add(new GdaiAnimSheetRecord
            {
                path = p.SheetAssetPath,
                guid = sheetGuid,
                content_fingerprint = sheetSha,
                cell_width = p.cell_width, cell_height = p.cell_height, columns = p.columns, rows = p.rows,
                userData_token = "gdai:owner:animation:pkg:" + p.package_id + ":sheet:GDAI__" + p.Scope,
            });

            var live = AssetDatabase.LoadAllAssetRepresentationsAtPath(p.SheetAssetPath).OfType<Sprite>()
                .ToDictionary(s => s.name, s => s, StringComparer.Ordinal);
            foreach (var c in p.cells.OrderBy(c => c.sprite_name, StringComparer.Ordinal))
            {
                var s = live[c.sprite_name];
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(s, out _, out long fid);
                float px = s.rect.width > 0 ? s.pivot.x / s.rect.width : 0f;
                float py = s.rect.height > 0 ? s.pivot.y / s.rect.height : 0f;
                section.sprites.Add(new GdaiAnimSpriteRecord
                {
                    sprite_name = c.sprite_name,
                    sheet_guid = sheetGuid,
                    sheet_path = p.SheetAssetPath,
                    file_id = fid.ToString(System.Globalization.CultureInfo.InvariantCulture), // STRING (R1)
                    cell_id = c.cell_id, clip_id = c.clip_id, frame_index = c.frame_index,
                    direction = c.direction, row = c.row, column = c.column,
                    content_fingerprint = GdaiAnimFingerprint.Sprite(sheetSha, c.sprite_name,
                        (int)s.rect.x, (int)s.rect.y, (int)s.rect.width, (int)s.rect.height,
                        px, py, GdaiAnimFingerprint.AlignmentFromPivot(px, py)),
                });
            }

            foreach (var clip in p.clips.OrderBy(c => c.clip_id, StringComparer.Ordinal))
            {
                string path = p.ClipAssetPath(clip.clip_id);
                section.clips.Add(new GdaiAnimClipRecord
                {
                    path = path,
                    guid = AssetDatabase.AssetPathToGUID(path),
                    clip_id = clip.clip_id,
                    content_fingerprint = GdaiAnimJson.Sha256Hex(File.ReadAllBytes(Abs(path))),
                    frame_sprite_names = p.cells.Where(c => c.clip_id == clip.clip_id)
                        .OrderBy(c => c.frame_index).Select(c => c.sprite_name).ToList(),
                });
            }

            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(p.ControllerAssetPath);
            var defaultState = ctrl.layers[0].stateMachine.defaultState;
            var ctrlRec = new GdaiAnimControllerRecord
            {
                path = p.ControllerAssetPath,
                guid = AssetDatabase.AssetPathToGUID(p.ControllerAssetPath),
                content_fingerprint = GdaiAnimJson.Sha256Hex(File.ReadAllBytes(Abs(p.ControllerAssetPath))),
                default_state = defaultState != null ? defaultState.name : null,
            };
            foreach (var st in ctrl.layers[0].stateMachine.states.OrderBy(s => s.state.name, StringComparer.Ordinal))
            {
                var clipRec = section.clips.FirstOrDefault(c => c.clip_id == st.state.name);
                ctrlRec.states.Add(new GdaiAnimControllerState
                {
                    state_name = st.state.name,
                    clip_id = st.state.name,
                    clip_guid = clipRec?.guid,
                    is_default = defaultState == st.state,
                });
            }
            section.controllers.Add(ctrlRec);

            // 0D-03 re-slice diff keyed by sprite_name
            var nextNames = new HashSet<string>(p.cells.Select(c => c.sprite_name), StringComparer.Ordinal);
            section.reslice_diff = new GdaiAnimResliceDiff
            {
                prior = prevNames.Count == 0 ? null : new GdaiAnimReslicePrior
                {
                    package_id = GdaiPlayableOwnershipManifest.Load()?.animation_assets?.materialization?.package_id,
                    snapshot_id = manifest?.snapshot_id,
                    manifest_content_sha256 = File.Exists(manifestAbs) ? GdaiAnimJson.Sha256Hex(File.ReadAllBytes(manifestAbs)) : null,
                },
                survivors = nextNames.Where(prevNames.Contains).OrderBy(n => n, StringComparer.Ordinal).ToList(),
                additions = nextNames.Where(n => !prevNames.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList(),
                removals = prevNames.Where(n => !nextNames.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList(),
            };
            removals = section.reslice_diff.removals;
            return section;
        }

        /// <summary>Additive Manifest v2 write: v1 fields preserved verbatim; schema → v2; atomic.</summary>
        private static bool WriteAnimationSection(GdaiAnimationAssetsSection section, out string error)
        {
            error = null;
            try
            {
                var m = GdaiPlayableOwnershipManifest.Load();
                if (m == null) { error = "manifest absent"; return false; }
                m.schema_version = GdaiPlayableAssetsManifest.SchemaVersionV2;
                m.animation_assets = section;
                string abs = Abs(GdaiPlayableOwnershipManifest.ManifestPath);
                string tmp = abs + ".tmp";
                File.WriteAllText(tmp, Newtonsoft.Json.JsonConvert.SerializeObject(m, Newtonsoft.Json.Formatting.Indented));
                if (File.Exists(abs)) File.Replace(tmp, abs, null);
                else File.Move(tmp, abs);
                AssetDatabase.ImportAsset(GdaiPlayableOwnershipManifest.ManifestPath);
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        private static GUID DeterministicSpriteId(string spriteName)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var hex = string.Concat(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(spriteName)).Select(b => b.ToString("x2")));
                return new GUID(hex);
            }
        }
#endif

        private static string ProjectRoot() => Directory.GetParent(Application.dataPath).FullName;
        private static string Abs(string assetPath) => Path.GetFullPath(Path.Combine(ProjectRoot(), assetPath));

        private static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder)) return;
            var parts = assetFolder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
