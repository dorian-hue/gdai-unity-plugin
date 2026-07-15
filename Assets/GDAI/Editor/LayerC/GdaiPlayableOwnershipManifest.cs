// =====================================================================================
// GDAI Unity Plugin · AUTO-0Q-P2 · §5.4b · Ownership manifest (Editor, Layer C).
//
// GDAIPlayableAssets.json is a RECORD of what the composer created — never a licence to
// overwrite or delete. It is written LAST (after the scene is saved) with an atomic temp+
// replace so a crash mid-write never leaves a half manifest. Verify() re-reads it against
// the ACTUAL scene/asset state; disagreement is a hard failure, not a silent trust:
//   * a manifest entry whose live marker identity or asset GUID no longer matches → fail;
//   * a missing manifest → nothing is retroactively treated as owned;
//   * an unknown path is never deleted (this class never deletes anything).
// The manifest lives under the owned generated root so a clean-replace regenerates it.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GDAI.Bridge.Editor.LayerA;
using GDAI.Runtime;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerC
{
    [Serializable] public class GdaiOwnedObjectRecord { public string name; public string role; public string profile_id; public string snapshot_id; }
    [Serializable] public class GdaiOwnedAssetRecord { public string kind; public string path; public string guid; }

    [Serializable]
    public class GdaiPlayableAssetsManifest
    {
        // No default initializer: a foreign manifest that OMITS this field must deserialize to null and
        // be rejected, not silently pass the schema check. Write() always sets it explicitly.
        public string schema_version;
        public string project_id;
        public string profile_id;
        public string snapshot_id;
        public int contract_revision;
        public string contract_sha256;
        public string generated_at;
        public GdaiOwnedAssetRecord input_asset;
        public GdaiOwnedAssetRecord enemy_prefab;
        public GdaiOwnedAssetRecord canonical_scene;
        // Flat list of the owned file assets (input asset, enemy prefab, canonical scene) — the
        // "exact files listed in the manifest" the structure standard documents. The typed records
        // above remain the primary handles; this is the enumerable projection consumers iterate.
        public List<GdaiOwnedAssetRecord> assets = new List<GdaiOwnedAssetRecord>();
        public List<GdaiOwnedObjectRecord> owned_scene_objects = new List<GdaiOwnedObjectRecord>();

        // T4 Stage-1A additive (0E-03 Manifest v2): OPTIONAL animation ownership section. Absent/null
        // in every v1 manifest AND in playable-only v2 manifests ("this profile owns no animation
        // assets"). NullValueHandling keeps the playable Write() output byte-identical to 8.9 when no
        // animation was materialized. Written ONLY by GdaiAnimationMaterializer after guard approval.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Animation.GdaiAnimationAssetsSection animation_assets;

        public const string SchemaVersion = "gdai.unity.playable_assets.v1";
        // T4 Stage-1A (0E-03): a manifest carrying animation_assets is v2. Verify accepts {v1, v2};
        // a v1-only plugin build refuses a v2 file (correct fail-closed: it cannot reason about
        // animation ownership and must not treat those assets as unowned).
        public const string SchemaVersionV2 = "gdai.unity.playable_assets.v2";
    }

    public static class GdaiPlayableOwnershipManifest
    {
        // Under the owned generated root so the coherent clean-replace regenerates it.
        // Canonical structure standard (docs/GDAI/PROJECT-STRUCTURE.md + validate_project_standard.py):
        // the playable ownership record lives in the Manifests/ subdir, parallel to Input/ and Prefabs/.
        public const string ManifestPath = "Assets/GDAI_Project/Generated/Manifests/GDAIPlayableAssets.json";

        private static string ProjectRoot() => Directory.GetParent(Application.dataPath).FullName;
        private static string AbsPath(string assetPath) => Path.GetFullPath(Path.Combine(ProjectRoot(), assetPath));

        private static GdaiOwnedAssetRecord AssetRecord(string kind, string assetPath)
        {
            string guid = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.AssetPathToGUID(assetPath);
            return new GdaiOwnedAssetRecord { kind = kind, path = assetPath, guid = string.IsNullOrEmpty(guid) ? null : guid };
        }

        /// <summary>
        /// Build the manifest from the ACTUAL scene state + resolved asset GUIDs and write it
        /// atomically. Call LAST (scene already saved). Returns false + reason on any write error.
        /// </summary>
        public static bool Write(GdaiPlayableContract contract, string projectId, string snapshotId,
            string contractSha256, string scenePath, out string error)
        {
            error = null;
            try
            {
                var m = new GdaiPlayableAssetsManifest
                {
                    schema_version = GdaiPlayableAssetsManifest.SchemaVersion,
                    project_id = projectId,
                    profile_id = contract.profile_id,
                    snapshot_id = snapshotId,
                    contract_revision = contract.contract_revision,
                    contract_sha256 = contractSha256,
                    generated_at = DateTime.UtcNow.ToString("o"),
                    input_asset = AssetRecord("input", contract.input?.asset_path),
                    enemy_prefab = AssetRecord("enemy_prefab", contract.enemy_prefab?.asset_path),
                    canonical_scene = AssetRecord("canonical_scene", scenePath),
                };
                // enumerable projection of the owned file assets (each a real path + meta) — the
                // "exact files" the structure standard lists in the manifest.
                m.assets = new[] { m.input_asset, m.enemy_prefab, m.canonical_scene }
                    .Where(a => a != null && !string.IsNullOrEmpty(a.path)).ToList();
                foreach (var marker in UnityEngine.Object.FindObjectsByType<GdaiGeneratedPlayableMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    m.owned_scene_objects.Add(new GdaiOwnedObjectRecord
                    {
                        name = marker.gameObject.name,
                        role = marker.ownedRole,
                        profile_id = marker.profileId,
                        snapshot_id = marker.snapshotId,
                    });
                m.owned_scene_objects = m.owned_scene_objects.OrderBy(o => o.name, StringComparer.Ordinal).ToList();

                // ── T4 0J · additive-merge preservation of the v2 animation_assets section ─────────────────
                // The composer owns ONLY the v1/playable fields built above. The additive v2 `animation_assets`
                // section is owned by the animation materializer. This fresh object drops it (NullValueHandling
                // .Ignore) unless we re-attach it — which on a re-sync would leave the pre-mutation guard with
                // prior==null and make it fail-closed (GUARD_UNRECORDED_OWNED_STAMP) on the still-labelled prior
                // sheet. Single authority manifest: preserve the section VERBATIM (no fingerprint recompute) when
                // this write is for the SAME identity and the prior section is structurally valid. Cross-identity
                // animation migration needs a separate transition contract (out of scope) → fail closed, no write.
                var prior = Load();
                if (prior != null && prior.animation_assets != null)
                {
                    bool sameIdentity = prior.project_id == projectId
                        && prior.snapshot_id == snapshotId
                        && prior.contract_revision == contract.contract_revision
                        && prior.contract_sha256 == contractSha256;
                    if (!sameIdentity)
                    { error = "MANIFEST_PRESERVE_IDENTITY_MISMATCH: prior animation_assets belongs to a different project/snapshot/contract; refusing to orphan it (transition contract required)"; return false; }
                    if (!AnimationSectionStructurallyValid(prior.animation_assets, snapshotId))
                    { error = "MANIFEST_PRESERVE_MALFORMED_ANIMATION_SECTION: prior v2 animation_assets is malformed; refusing to write (would drop ownership)"; return false; }
                    // same identity + valid → re-attach the animation ownership records unchanged; schema stays v2.
                    m.animation_assets = prior.animation_assets;
                    m.schema_version = GdaiPlayableAssetsManifest.SchemaVersionV2;
                }
                // (no prior, or prior has no animation section: write the plain v1 manifest as before — nothing to preserve.)

                // ensure owned folder exists
                var dirAsset = Path.GetDirectoryName(ManifestPath).Replace('\\', '/');
                EnsureFolder(dirAsset);

                // atomic write: full temp then replace/move
                string finalAbs = AbsPath(ManifestPath);
                string tmp = finalAbs + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(m, Formatting.Indented));
                if (File.Exists(finalAbs)) File.Replace(tmp, finalAbs, null);
                else File.Move(tmp, finalAbs);
                AssetDatabase.ImportAsset(ManifestPath);
                return true;
            }
            catch (Exception e) { error = "manifest write failed: " + e.Message; return false; }
        }

        public static GdaiPlayableAssetsManifest Load()
        {
            try
            {
                string abs = AbsPath(ManifestPath);
                if (!File.Exists(abs)) return null;
                return JsonConvert.DeserializeObject<GdaiPlayableAssetsManifest>(File.ReadAllText(abs));
            }
            catch { return null; }
        }

        // Minimal structural validity of a prior v2 animation_assets section before the composer re-attaches
        // it: a materialization pin whose snapshot_id matches this write (contract requires equality), and
        // non-null record lists with at least one owned artifact. A present-but-broken/empty section is NOT
        // silently converted to an empty section — the composer fails closed instead.
        static bool AnimationSectionStructurallyValid(Animation.GdaiAnimationAssetsSection s, string expectedSnapshotId)
        {
            if (s == null || s.materialization == null) return false;
            if (string.IsNullOrEmpty(s.materialization.snapshot_id) || s.materialization.snapshot_id != expectedSnapshotId) return false;
            if (s.raw_sheets == null || s.sprites == null || s.clips == null || s.controllers == null) return false;
            if (s.raw_sheets.Count == 0 && s.clips.Count == 0 && s.controllers.Count == 0) return false;
            return true;
        }

        /// <summary>
        /// Re-read the manifest against the ACTUAL scene/asset state AND the current session identity.
        /// Fails closed on: missing/absent-schema manifest; a stale/foreign manifest (different contract
        /// revision/sha256, snapshot, or project); an empty owned list; any recorded asset GUID that no
        /// longer resolves; a recorded owned object without a matching live marker (forward); and a live
        /// owned marker not recorded in the manifest (reverse — no under-recording of ownership).
        /// </summary>
        public static bool Verify(GdaiPlayableContract contract, string projectId, string snapshotId,
            string contractSha256, out string error)
        {
            error = null;
            var m = Load();
            if (m == null) { error = "ownership manifest missing"; return false; }
            // T4 Stage-1A additive: accept v1 AND v2 (a v2 file differs only by the optional
            // animation_assets section, which the animation verifier owns). Anything else fails closed.
            if (m.schema_version != GdaiPlayableAssetsManifest.SchemaVersion &&
                m.schema_version != GdaiPlayableAssetsManifest.SchemaVersionV2)
            { error = "manifest schema mismatch: " + (m.schema_version ?? "<absent>"); return false; }

            // session/contract identity: a manifest from a different composition is refused, so a stale
            // or foreign file can never lend ownership to the current scene.
            if (contract != null && m.contract_revision != contract.contract_revision)
            { error = $"manifest contract_revision {m.contract_revision} != {contract.contract_revision}"; return false; }
            if (!string.Equals(m.contract_sha256, contractSha256, StringComparison.OrdinalIgnoreCase))
            { error = "manifest contract_sha256 mismatch"; return false; }
            if (!string.Equals(m.snapshot_id, snapshotId, StringComparison.Ordinal))
            { error = "manifest snapshot_id mismatch"; return false; }
            if (!string.Equals(m.project_id, projectId, StringComparison.Ordinal))
            { error = "manifest project_id mismatch"; return false; }

            var recorded = m.owned_scene_objects ?? new List<GdaiOwnedObjectRecord>();
            if (recorded.Count == 0) { error = "manifest records no owned objects (would verify vacuously)"; return false; }

            // assets: recorded GUID must equal the live GUID at the recorded path
            foreach (var a in new[] { m.input_asset, m.enemy_prefab, m.canonical_scene })
            {
                if (a == null || string.IsNullOrEmpty(a.path)) { error = "manifest asset record incomplete"; return false; }
                string live = AssetDatabase.AssetPathToGUID(a.path);
                if (string.IsNullOrEmpty(live)) { error = "manifest asset not found at path: " + a.path; return false; }
                if (!string.Equals(live, a.guid, StringComparison.Ordinal)) { error = "manifest asset GUID mismatch at " + a.path; return false; }
            }

            var liveMarkers = UnityEngine.Object.FindObjectsByType<GdaiGeneratedPlayableMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            // forward: each recorded owned object must still have a live marker with matching identity
            foreach (var o in recorded)
            {
                bool ok = liveMarkers.Any(mk => mk.gameObject.name == o.name && mk.ownedRole == o.role
                    && mk.profileId == o.profile_id && mk.snapshotId == o.snapshot_id);
                if (!ok) { error = "manifest owned object has no matching live marker: " + o.name + " (" + o.role + ")"; return false; }
            }

            // reverse: every live owned marker must be recorded (ownership is not under-recorded)
            foreach (var mk in liveMarkers)
            {
                bool recordedHas = recorded.Any(o => o.name == mk.gameObject.name && o.role == mk.ownedRole
                    && o.profile_id == mk.profileId && o.snapshot_id == mk.snapshotId);
                if (!recordedHas) { error = "live owned object not recorded in manifest: " + mk.gameObject.name; return false; }
            }
            return true;
        }

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
