// =====================================================================================
// GDAI Unity Plugin · T4 Stage-1A (Gate A) · gdai.animation.materialization_package.v1
// consumer model (0E-01 amended schema).
//
// The Unity importer consumes ONLY this normalized contract. It contains ZERO handling of
// the historical frame_map / batch_layout / column_actions / row_meaning protocols — all
// layout/axis divergence is resolved upstream by the Flowcraft normalizer (operator ruling;
// STOP_AUTO_0A_SCHEMA_CONFLICT: DEFERRED_PENDING_NORMALIZATION).
//
// Fail-closed: ValidateStructure/ValidateForMaterialization throw GdaiAnimGateException with
// the FIRST violated GATE_* / NORM_* / HASH_* code. package_class is AUTHORITATIVE (markers
// are corroboration only); PRODUCTION requires license CLEARED and hard-rejects TEST_ONLY.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GDAI.Bridge.Editor.LayerC.Animation
{
    public class GdaiAnimGateException : Exception
    {
        public string Code { get; }
        public GdaiAnimGateException(string code) : base(code) { Code = code; }
    }

    [Serializable] public class GdaiAnimClipEvent
    {
        public string kind;              // HIT | AUDIO | VFX
        public int frame_index;
        public string semantic;          // OPEN | CLOSE | POINT — Stage-1A: METADATA ONLY (0E-04 #9)
        public bool cancel_on_interrupt; // metadata; runtime behavior is Stage 2
        public string payload_ref;
    }

    [Serializable] public class GdaiAnimClip
    {
        public string clip_id;
        public string action;
        public string direction;         // null for direction-less clips
        public int frame_count;
        public float nominal_fps;
        public bool loop;
        public List<GdaiAnimClipEvent> events = new List<GdaiAnimClipEvent>();
    }

    [Serializable] public class GdaiAnimCell
    {
        public string cell_id;
        public string sprite_name;       // VERBATIM identity (0D-03); never renamed on re-slice
        public string clip_id;
        public int frame_index;
        public string direction;
        public int row;                  // canonical row-major, TOP_LEFT origin
        public int column;
    }

    public class GdaiAnimationPackage
    {
        public const string SchemaId = "gdai.animation.materialization_package.v1";
        public const string OwnershipLabel = "GDAI_Owned_Animation";
        public const string TargetRoot = "Assets/GDAI_Project/Generated/Animations";

        public JObject Raw;              // parsed source of truth (hashing works on this)
        public string schema;
        public string package_class;     // TEST_ONLY | PRODUCTION — AUTHORITATIVE
        public string package_id;
        public string package_content_sha256;
        public string profile_id;        // animation package profile (→ manifest animation_profile_id)
        public int? profile_version;
        public string entity_id;
        public string adoption_event_id; public bool adopted; public bool revoked;
        public string qa_event_id; public string qa_status; public string qa_gate_version;
        public string provider_id; public string capability_status; public string license_status;
        public string sheet_content_sha256; public int cell_width, cell_height, columns, rows; public string alpha;
        public float pixels_per_unit; public string pivot_alignment; public float pivot_x, pivot_y;
        public string filter_mode; public float nominal_fps;
        public List<GdaiAnimClip> clips = new List<GdaiAnimClip>();
        public List<GdaiAnimCell> cells = new List<GdaiAnimCell>();
        public string source_schema, source_package_id, source_axis_origin, adapter_version, axis_convention;

        public string Scope => Slug(entity_id);

        public static string Slug(string s)
        {
            var lowered = (s ?? "").ToLowerInvariant();
            var sb = new System.Text.StringBuilder();
            bool lastUnderscore = false;
            foreach (char c in lowered)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
                if (ok) { sb.Append(c); lastUnderscore = false; }
                else if (!lastUnderscore && sb.Length > 0) { sb.Append('_'); lastUnderscore = true; }
            }
            var res = sb.ToString().TrimEnd('_');
            if (res.Length == 0) throw new GdaiAnimGateException("NORM_NAME_CHARSET:empty-slug");
            return res;
        }

        public static GdaiAnimationPackage Parse(string json)
        {
            JObject o;
            try { o = GdaiAnimJson.ParseObject(json); }
            catch (Exception e) { throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:json:" + e.Message); }
            var p = new GdaiAnimationPackage { Raw = o };
            try
            {
                p.schema = (string)o["schema"];
                p.package_class = (string)o["package_class"];
                p.package_id = (string)o["package_id"];
                p.package_content_sha256 = (string)o["package_content_sha256"];
                p.profile_id = (string)o["profile_id"];
                p.profile_version = o["profile_version"]?.Type == JTokenType.Integer ? (int?)(int)o["profile_version"] : null;
                p.entity_id = (string)o["entity_id"];
                var ad = (JObject)o["adoption"];
                p.adoption_event_id = (string)ad["adoption_event_id"]; p.adopted = (bool)ad["adopted"]; p.revoked = (bool)ad["revoked"];
                var qa = (JObject)o["qa"];
                p.qa_event_id = (string)qa["qa_event_id"]; p.qa_status = (string)qa["status"]; p.qa_gate_version = (string)qa["gate_version"];
                var pv = (JObject)o["provider_provenance"];
                p.provider_id = (string)pv["provider_id"]; p.capability_status = (string)pv["capability_status"]; p.license_status = (string)pv["license_status"];
                var sh = (JObject)o["sheet"];
                p.sheet_content_sha256 = (string)sh["content_sha256"];
                p.cell_width = (int)sh["cell_width"]; p.cell_height = (int)sh["cell_height"];
                p.columns = (int)sh["columns"]; p.rows = (int)sh["rows"]; p.alpha = (string)sh["alpha"];
                var ip = (JObject)o["import_policy"];
                p.pixels_per_unit = (float)ip["pixels_per_unit"];
                var piv = (JObject)ip["pivot"];
                p.pivot_alignment = (string)piv["alignment"]; p.pivot_x = (float)piv["x"]; p.pivot_y = (float)piv["y"];
                p.filter_mode = (string)ip["filter_mode"]; p.nominal_fps = (float)ip["nominal_fps"];
                p.clips = ((JArray)o["clips"]).Select(t => new GdaiAnimClip
                {
                    clip_id = (string)t["clip_id"], action = (string)t["action"],
                    direction = t["direction"].Type == JTokenType.Null ? null : (string)t["direction"],
                    frame_count = (int)t["frame_count"], nominal_fps = (float)t["nominal_fps"], loop = (bool)t["loop"],
                    events = ((JArray)t["events"]).Select(e => new GdaiAnimClipEvent
                    {
                        kind = (string)e["kind"], frame_index = (int)e["frame_index"], semantic = (string)e["semantic"],
                        cancel_on_interrupt = (bool)e["cancel_on_interrupt"],
                        payload_ref = e["payload_ref"].Type == JTokenType.Null ? null : (string)e["payload_ref"],
                    }).ToList(),
                }).ToList();
                p.cells = ((JArray)o["cells"]).Select(t => new GdaiAnimCell
                {
                    cell_id = (string)t["cell_id"], sprite_name = (string)t["sprite_name"], clip_id = (string)t["clip_id"],
                    frame_index = (int)t["frame_index"],
                    direction = t["direction"].Type == JTokenType.Null ? null : (string)t["direction"],
                    row = (int)t["row"], column = (int)t["column"],
                }).ToList();
                var nz = (JObject)o["normalization"];
                p.source_schema = (string)nz["source_schema"]; p.source_package_id = (string)nz["source_package_id"];
                p.source_axis_origin = (string)nz["source_axis_origin"]; p.adapter_version = (string)nz["adapter_version"];
                p.axis_convention = (string)nz["axis_convention"];
            }
            catch (GdaiAnimGateException) { throw; }
            catch (Exception e) { throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:shape:" + e.Message); }
            return p;
        }

        /// <summary>0E-01 structural rules 1–5 + constants + recomputed package hash (0E-05 §1, no bypass).</summary>
        public void ValidateStructure()
        {
            if (schema != SchemaId) throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:schema");
            if (package_class != "TEST_ONLY" && package_class != "PRODUCTION") throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:package_class");
            var mt = (JObject)Raw["materialization_target"];
            if (mt == null || (string)mt["root"] != TargetRoot || (string)mt["sprites_dir"] != "Sprites" ||
                (string)mt["clips_dir"] != "Clips" || (string)mt["controllers_dir"] != "Controllers" ||
                (string)mt["ownership_label"] != OwnershipLabel)
                throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:materialization_target");
            if (mt.Property("manifests_dir") != null) throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:manifests_dir_forbidden");
            if (axis_convention != "CANONICAL_ROW_MAJOR") throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:axis_convention");
            if (source_axis_origin != "TOP_LEFT" && source_axis_origin != "BOTTOM_LEFT") throw new GdaiAnimGateException("STOP_NORMALIZER_AXIS_AMBIGUOUS:source_axis_origin");

            string recomputed = GdaiAnimJson.PackageContentSha256(Raw);
            if (!string.Equals(recomputed, package_content_sha256, StringComparison.Ordinal))
                throw new GdaiAnimGateException("HASH_PACKAGE_CONTENT_MISMATCH");

            var clipsById = clips.ToDictionary(c => c.clip_id);
            if (clipsById.Count != clips.Count) throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:duplicate_clip_id");
            var frames = new Dictionary<string, HashSet<int>>();
            foreach (var cell in cells)
            {
                if (!clipsById.TryGetValue(cell.clip_id, out var clip)) throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:cell_unknown_clip:" + cell.clip_id);
                if (!string.Equals(cell.direction, clip.direction, StringComparison.Ordinal)) throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:cell_direction_mismatch:" + cell.cell_id);
                if (!frames.TryGetValue(cell.clip_id, out var set)) frames[cell.clip_id] = set = new HashSet<int>();
                if (!set.Add(cell.frame_index)) throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:duplicate_frame_index:" + cell.cell_id);
            }
            foreach (var clip in clips)
            {
                frames.TryGetValue(clip.clip_id, out var set);
                if (set == null || set.Count != clip.frame_count) throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:frame_count_mismatch:" + clip.clip_id);
                for (int i = 0; i < clip.frame_count; i++)
                    if (!set.Contains(i)) throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:frame_gap:" + clip.clip_id + ":f" + i);
                foreach (var ev in clip.events)
                {
                    if (ev.frame_index < 0 || ev.frame_index >= clip.frame_count) throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:event_frame_oob:" + clip.clip_id);
                    if (!ev.cancel_on_interrupt) throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:cancel_on_interrupt");
                }
            }
            var cellIds = new HashSet<string>(); var names = new HashSet<string>(); var rc = new HashSet<string>();
            foreach (var cell in cells)
            {
                if (!cellIds.Add(cell.cell_id)) throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:duplicate_cell_id:" + cell.cell_id);
                if (!names.Add(cell.sprite_name)) throw new GdaiAnimGateException("NORM_SPRITE_NAME_COLLISION:" + cell.sprite_name);
                if (!cell.sprite_name.StartsWith("GDAI__", StringComparison.Ordinal)) throw new GdaiAnimGateException("NORM_MISSING_OWNER_PREFIX:" + cell.sprite_name);
                if (cell.row < 0 || cell.row >= rows || cell.column < 0 || cell.column >= columns) throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:cell_out_of_grid:" + cell.cell_id);
                if (!rc.Add(cell.row + "," + cell.column)) throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:duplicate_row_column:" + cell.cell_id);
            }
            if (columns * rows < cells.Count) throw new GdaiAnimGateException("GATE_STRUCTURE_INVALID:grid_smaller_than_cells");
        }

        /// <summary>0E-01 cross-field gates. runClass = the class of THIS materialization run.</summary>
        public void ValidateForMaterialization(string runClass)
        {
            ValidateStructure();
            bool markerSaysTest = (package_id ?? "").StartsWith("TESTONLY-", StringComparison.Ordinal)
                || (adoption_event_id ?? "").Contains("-TESTONLY-") || (qa_event_id ?? "").Contains("-TESTONLY-");
            if (package_class == "TEST_ONLY" && !markerSaysTest) throw new GdaiAnimGateException("GATE_CLASS_MARKER_DISAGREEMENT:test_without_markers");
            if (package_class == "PRODUCTION" && markerSaysTest) throw new GdaiAnimGateException("GATE_CLASS_MARKER_DISAGREEMENT:production_with_test_markers");
            if (runClass == "PRODUCTION" && package_class == "TEST_ONLY") throw new GdaiAnimGateException("GATE_PROD_REJECTS_TESTONLY");
            if (package_class == "PRODUCTION" && license_status != "CLEARED") throw new GdaiAnimGateException("GATE_PROD_LICENSE_UNCLEARED");
            if (qa_status != "PASSED") throw new GdaiAnimGateException("GATE_PRECONDITION_UNMET:qa_not_passed");
            if (!adopted) throw new GdaiAnimGateException("GATE_PRECONDITION_UNMET:not_adopted");
            if (revoked) throw new GdaiAnimGateException("GATE_PRECONDITION_UNMET:revoked");
        }

        // ── R7 owned file-path stems (0D-FINAL R7) ──
        public string SheetAssetPath => TargetRoot + "/Sprites/GDAI__" + Scope + ".png";
        public string ClipAssetPath(string clipId) => TargetRoot + "/Clips/GDAI__" + Scope + "__" + clipId + ".anim";
        public string ControllerAssetPath => TargetRoot + "/Controllers/GDAI__" + Scope + ".controller";
    }
}
