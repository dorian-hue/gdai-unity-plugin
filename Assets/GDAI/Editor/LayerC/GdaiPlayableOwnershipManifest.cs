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
        public string schema_version = SchemaVersion;
        public string project_id;
        public string snapshot_id;
        public int contract_revision;
        public string contract_sha256;
        public string generated_at;
        public GdaiOwnedAssetRecord input_asset;
        public GdaiOwnedAssetRecord enemy_prefab;
        public GdaiOwnedAssetRecord canonical_scene;
        public List<GdaiOwnedObjectRecord> owned_scene_objects = new List<GdaiOwnedObjectRecord>();

        public const string SchemaVersion = "gdai.unity.playable_assets.v1";
    }

    public static class GdaiPlayableOwnershipManifest
    {
        // Under the owned generated root so the coherent clean-replace regenerates it.
        public const string ManifestPath = "Assets/GDAI_Project/Generated/GDAIPlayableAssets.json";

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
                    project_id = projectId,
                    snapshot_id = snapshotId,
                    contract_revision = contract.contract_revision,
                    contract_sha256 = contractSha256,
                    generated_at = DateTime.UtcNow.ToString("o"),
                    input_asset = AssetRecord("input", contract.input?.asset_path),
                    enemy_prefab = AssetRecord("enemy_prefab", contract.enemy_prefab?.asset_path),
                    canonical_scene = AssetRecord("canonical_scene", scenePath),
                };
                foreach (var marker in UnityEngine.Object.FindObjectsByType<GdaiGeneratedPlayableMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    m.owned_scene_objects.Add(new GdaiOwnedObjectRecord
                    {
                        name = marker.gameObject.name,
                        role = marker.ownedRole,
                        profile_id = marker.profileId,
                        snapshot_id = marker.snapshotId,
                    });
                m.owned_scene_objects = m.owned_scene_objects.OrderBy(o => o.name, StringComparer.Ordinal).ToList();

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

        /// <summary>
        /// Re-read the manifest against the ACTUAL scene/asset state. Every recorded owned object must
        /// still carry a live marker with matching identity, and every recorded asset GUID must still
        /// resolve at its path. Any disagreement → false + reason (never a silent trust).
        /// </summary>
        public static bool Verify(out string error)
        {
            error = null;
            var m = Load();
            if (m == null) { error = "ownership manifest missing"; return false; }
            if (m.schema_version != GdaiPlayableAssetsManifest.SchemaVersion) { error = "manifest schema mismatch: " + m.schema_version; return false; }

            // assets: recorded GUID must equal the live GUID at the recorded path
            foreach (var a in new[] { m.input_asset, m.enemy_prefab, m.canonical_scene })
            {
                if (a == null || string.IsNullOrEmpty(a.path)) { error = "manifest asset record incomplete"; return false; }
                string live = AssetDatabase.AssetPathToGUID(a.path);
                if (string.IsNullOrEmpty(live)) { error = "manifest asset not found at path: " + a.path; return false; }
                if (!string.Equals(live, a.guid, StringComparison.Ordinal)) { error = "manifest asset GUID mismatch at " + a.path; return false; }
            }

            // owned objects: each must still have a live marker with matching identity
            var liveMarkers = UnityEngine.Object.FindObjectsByType<GdaiGeneratedPlayableMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var o in m.owned_scene_objects ?? new List<GdaiOwnedObjectRecord>())
            {
                bool ok = liveMarkers.Any(mk => mk.gameObject.name == o.name
                    && mk.ownedRole == o.role
                    && mk.profileId == o.profile_id
                    && mk.snapshotId == o.snapshot_id);
                if (!ok) { error = "manifest owned object has no matching live marker: " + o.name + " (" + o.role + ")"; return false; }
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
