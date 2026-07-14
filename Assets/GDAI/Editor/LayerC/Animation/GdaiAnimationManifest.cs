// =====================================================================================
// GDAI Unity Plugin · T4 Stage-1A (Gate A) · Manifest v2 `animation_assets` section (0E-03).
//
// Records live INSIDE the single playable manifest GDAIPlayableAssets.json — there is NO
// nested Animations/Manifests authority (0E correction #8). Field names are the ratified
// ones: animation_profile_id / animation_profile_version (separate namespace from the
// playable top-level profile_id; NO equality). `file_id` is a STRING (int64 > JSON safe int).
// AUTHORITY = manifest GUID records; label GDAI_Owned_Animation = discovery; userData =
// corroboration. A label/userData stamp alone never authorizes overwrite or deletion.
// =====================================================================================
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace GDAI.Bridge.Editor.LayerC.Animation
{
    [Serializable] public class GdaiAnimMaterializationPin
    {
        public string package_schema;               // == gdai.animation.materialization_package.v1
        public string package_class;                // TEST_ONLY | PRODUCTION (authoritative echo)
        public string package_id;
        public string animation_profile_id;         // the PACKAGE's profile — SEPARATE namespace (0D-FINAL R2)
        public int? animation_profile_version;
        public string entity_id;
        public string snapshot_id;                  // MUST == manifest top-level snapshot_id
        public string adapter_version;
        public string source_axis_origin;
        public string package_content_sha256;       // recomputed, never trusted (0E-05 §1/§4)
        public string license_status;
        public string adoption_event_id;
        public string qa_event_id;
        public string materialized_at;
    }

    [Serializable] public class GdaiAnimSheetRecord
    {
        public string kind = "raw_sheet";
        public string path;
        public string guid;                         // AUTHORITY
        public string content_fingerprint;          // 0E-05 §3: SHA256(png bytes) == sheet.content_sha256
        public string importer_type = "TextureImporter";
        public string sprite_mode = "Multiple";
        public int cell_width, cell_height, columns, rows;
        public string label = GdaiAnimationPackage.OwnershipLabel;
        public string userData_token;               // CORROBORATION ONLY
    }

    [Serializable] public class GdaiAnimSpriteRecord
    {
        public string kind = "sprite";
        public string sprite_name;                  // VERBATIM identity + diff key (0D-03)
        public string sheet_guid;                   // parent sheet GUID = AUTHORITY
        public string sheet_path;
        public string file_id;                      // sub-sprite localIdentifierInFile — STRING (R1)
        public string cell_id;
        public string clip_id;
        public int frame_index;
        public string direction;
        public int row, column;                     // traceability only, never identity
        public string content_fingerprint;          // 0E-05 §3 sprite witness (sheet hash + geometry)
        public string label = GdaiAnimationPackage.OwnershipLabel;
    }

    [Serializable] public class GdaiAnimClipRecord
    {
        public string kind = "clip";
        public string path;
        public string guid;                         // AUTHORITY
        public string clip_id;
        public string content_fingerprint;          // SHA256(.anim file bytes)
        public List<string> frame_sprite_names = new List<string>();
        public string label = GdaiAnimationPackage.OwnershipLabel;
        public string userData_token;
    }

    [Serializable] public class GdaiAnimControllerState
    {
        public string state_name;
        public string clip_id;
        public string clip_guid;
        public bool is_default;
    }

    [Serializable] public class GdaiAnimControllerRecord
    {
        public string kind = "controller";
        public string path;
        public string guid;                         // AUTHORITY
        public string content_fingerprint;          // SHA256(.controller file bytes)
        public string default_state;
        public List<GdaiAnimControllerState> states = new List<GdaiAnimControllerState>();
        public string label = GdaiAnimationPackage.OwnershipLabel;
        public string userData_token;
    }

    [Serializable] public class GdaiAnimReslicePrior
    {
        public string package_id;
        public string snapshot_id;
        public string manifest_content_sha256;      // SHA256 of the prior manifest file bytes
    }

    [Serializable] public class GdaiAnimResliceDiff
    {
        public GdaiAnimReslicePrior prior;          // null on first materialization
        public List<string> survivors = new List<string>();   // keyed by sprite_name (0D-03 diff)
        public List<string> additions = new List<string>();
        public List<string> removals = new List<string>();    // Stage 1A: MUST stay empty
    }

    [Serializable] public class GdaiAnimationAssetsSection
    {
        public GdaiAnimMaterializationPin materialization;
        public List<GdaiAnimSheetRecord> raw_sheets = new List<GdaiAnimSheetRecord>();
        public List<GdaiAnimSpriteRecord> sprites = new List<GdaiAnimSpriteRecord>();
        public List<GdaiAnimClipRecord> clips = new List<GdaiAnimClipRecord>();
        public List<GdaiAnimControllerRecord> controllers = new List<GdaiAnimControllerRecord>();
        public GdaiAnimResliceDiff reslice_diff = new GdaiAnimResliceDiff();
    }

    public static class GdaiAnimFingerprint
    {
        /// <summary>
        /// Pure pivot→SpriteAlignment mapping used IDENTICALLY at write and guard/verify time, so the
        /// fingerprint's alignment input is recomputable from the live Sprite alone (which does not
        /// expose the importer's alignment enum): Center / BottomCenter / else Custom.
        /// </summary>
        public static int AlignmentFromPivot(float px, float py)
        {
            const float eps = 1e-4f;
            bool cx = Math.Abs(px - 0.5f) < eps;
            if (cx && Math.Abs(py - 0.5f) < eps) return 0;  // SpriteAlignment.Center
            if (cx && Math.Abs(py) < eps) return 7;         // SpriteAlignment.BottomCenter
            return 9;                                       // SpriteAlignment.Custom
        }

        /// <summary>
        /// 0E-05 §3 sprite witness: SHA256(JCS({sheet_sha256, sprite_name, rect[x,y,w,h] (sheet-space,
        /// bottom-left), pivot[px,py], alignment(int), import_mode:"Multiple"})). Sprites have no
        /// standalone file; a human re-slice moves the geometry and therefore the fingerprint.
        /// </summary>
        public static string Sprite(string sheetSha256, string spriteName, int x, int y, int w, int h,
            float pivotX, float pivotY, int alignment)
        {
            var o = new JObject
            {
                ["sheet_sha256"] = sheetSha256,
                ["sprite_name"] = spriteName,
                ["rect"] = new JArray(x, y, w, h),
                ["pivot"] = new JArray(pivotX, pivotY),
                ["alignment"] = alignment,
                ["import_mode"] = "Multiple",
            };
            return GdaiAnimJson.JcsSha256Hex(o);
        }
    }
}
