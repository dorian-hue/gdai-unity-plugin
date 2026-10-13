using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// =====================================================================================
// GDAI Unity Plugin · DOWNSTREAM-BUILD-2 · Imported asset registry.
//
// Turns AssetPayloadImporter's written files into an ADDRESSABLE surface:
//   asset_id / unity_path  →  registry entry  →  exact imported Unity asset.
// Entity Sprite resolvers remain a filtered view over the complete registry.
//
// Storage: Assets/GDAI_Generated/AssetRegistry/gdai_asset_registry.json
//   - Lives inside GDAI_Generated on purpose: the coherent-bundle clean-replace wipes it
//     and the next asset import regenerates it, so the registry can never describe files
//     from an older bundle than the code beside it.
//
// Pure model/build logic is separated from IO so it can be reasoned about; the resolver
// uses AssetDatabase (Editor-only), which is where Jason / Layer B tooling runs today.
// No hardcoded projects, entities, or names anywhere — data flows from the bundle DTO.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerA
{
    [Serializable]
    public class GdaiAssetRegistryEntry
    {
        public string entity_id;
        public string asset_id;          // proxy DTO asset_id (entity_assets row id)
        public string world_entity_name; // informational; matching MUST use ids
        public string role;              // e.g. "entity_sprite" (manifest role, not gameplay role)
        public string asset_type;        // "sprite" | "image" | ...
        public string mime_type;
        public string unity_path;        // Assets/GDAI_Generated/Art/...
        public string file_name;
        public string source_project_id; // project lineage; required when entity_id is absent
        public string sha256;             // verified imported payload hash when declared
        public string project_audio_asset_id;
        public string audio_candidate_id;
        public string audio_category;
    }

    [Serializable]
    public class GdaiAssetRegistryData
    {
        public int version = 1;
        public string generated_at;
        public string source_snapshot_id;
        public List<GdaiAssetRegistryEntry> entries = new List<GdaiAssetRegistryEntry>();
    }

    public static class GdaiImportedAssetRegistry
    {
        public const string RegistryDir = "Assets/GDAI_Generated/AssetRegistry";
        public const string RegistryPath = RegistryDir + "/gdai_asset_registry.json";

        // ---------- PURE BUILD (no IO, no Unity asset APIs) ----------

        /// <summary>
        /// Builds registry data from DTO assets that were actually written to disk.
        /// Every entry retains exact asset/path identity; project assets do not invent entity IDs.
        /// </summary>
        public static GdaiAssetRegistryData Build(
            List<GdaiBundleProxyAsset> dtoAssets,
            ICollection<string> writtenUnityPaths,
            string sourceSnapshotId)
        {
            var data = new GdaiAssetRegistryData
            {
                generated_at = DateTime.UtcNow.ToString("o"),
                source_snapshot_id = sourceSnapshotId ?? string.Empty,
            };
            if (dtoAssets == null || writtenUnityPaths == null) return data;

            var assetIds = new HashSet<string>(StringComparer.Ordinal);
            var unityPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in dtoAssets)
            {
                if (a == null || string.IsNullOrEmpty(a.unity_path)) continue;
                string rel = a.unity_path.Replace('\\', '/');
                if (!writtenUnityPaths.Contains(rel)) continue; // only files that really materialized
                string entityId = a.source != null ? a.source.entity_id : null;
                string projectId = a.source != null ? a.source.project_id : null;
                string projectAudioAssetId = a.source != null ? a.source.project_audio_asset_id : null;
                string audioCandidateId = a.source != null ? a.source.audio_candidate_id : null;
                string audioCategory = a.source != null ? a.source.audio_category : null;
                string invalid = ValidateEntry(a.asset_id, rel, a.asset_type, a.mime_type, entityId, projectId,
                    a.sha256, projectAudioAssetId, audioCandidateId, audioCategory, false);
                if (invalid != null) throw new InvalidOperationException("invalid imported registry entry: " + invalid);
                if (!assetIds.Add(a.asset_id)) throw new InvalidOperationException("duplicate imported registry asset_id: " + a.asset_id);
                if (!unityPaths.Add(rel)) throw new InvalidOperationException("duplicate imported registry unity_path: " + rel);

                data.entries.Add(new GdaiAssetRegistryEntry
                {
                    entity_id = entityId,
                    asset_id = a.asset_id,
                    world_entity_name = a.source != null ? a.source.world_entity_name : null,
                    role = a.role,
                    asset_type = a.asset_type,
                    mime_type = a.mime_type,
                    unity_path = rel,
                    file_name = a.file_name,
                    source_project_id = projectId,
                    sha256 = a.sha256,
                    project_audio_asset_id = projectAudioAssetId,
                    audio_candidate_id = audioCandidateId,
                    audio_category = audioCategory,
                });
            }
            return data;
        }

        // ---------- IO ----------

        /// <summary>Writes the registry JSON under GDAI_Generated and imports it. Never throws.</summary>
        public static bool Write(GdaiAssetRegistryData data, out string error)
        {
            error = null;
            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string absDir = Path.GetFullPath(Path.Combine(projectRoot, RegistryDir));
                Directory.CreateDirectory(absDir);
                string absPath = Path.Combine(absDir, "gdai_asset_registry.json");
                File.WriteAllText(absPath, JsonConvert.SerializeObject(data, Formatting.Indented));
                AssetDatabase.ImportAsset(RegistryPath);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>Loads the registry if present; returns null when missing/unreadable.</summary>
        public static GdaiAssetRegistryData Load()
        {
            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string absPath = Path.GetFullPath(Path.Combine(projectRoot, RegistryPath));
                if (!File.Exists(absPath)) return null;
                var data = JsonConvert.DeserializeObject<GdaiAssetRegistryData>(File.ReadAllText(absPath));
                string invalid = Validate(data);
                if (invalid != null) throw new InvalidDataException(invalid);
                return data;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GDAI][Assets][Registry] Failed to load registry: " + e.Message);
                return null;
            }
        }

        // ---------- RESOLVERS (Editor · AssetDatabase) ----------

        public static bool TryGetSpriteForEntity(string entityId, out Sprite sprite, out string reason)
        {
            sprite = null;
            reason = null;
            if (string.IsNullOrEmpty(entityId)) { reason = "empty_entity_id"; return false; }
            var data = Load();
            if (data == null || data.entries == null || data.entries.Count == 0) { reason = "registry_missing_or_empty"; return false; }
            var entry = data.entries.Find(e => IsEntitySprite(e) && e.entity_id == entityId);
            if (entry == null) { reason = "entity_not_in_registry"; return false; }
            return TryLoadSprite(entry, out sprite, out reason);
        }

        public static bool TryGetSpriteByAssetId(string assetId, out Sprite sprite, out string reason)
        {
            sprite = null;
            reason = null;
            if (string.IsNullOrEmpty(assetId)) { reason = "empty_asset_id"; return false; }
            var data = Load();
            if (data == null || data.entries == null || data.entries.Count == 0) { reason = "registry_missing_or_empty"; return false; }
            var entry = data.entries.Find(e => IsEntitySprite(e) && e.asset_id == assetId);
            if (entry == null) { reason = "asset_not_in_registry"; return false; }
            return TryLoadSprite(entry, out sprite, out reason);
        }

        /// <summary>All registry entries (empty list when registry absent). For Jason/Layer B enumeration.</summary>
        public static List<GdaiAssetRegistryEntry> All()
        {
            var data = Load();
            return data != null && data.entries != null ? data.entries : new List<GdaiAssetRegistryEntry>();
        }

        private static bool IsEntitySprite(GdaiAssetRegistryEntry entry)
        {
            return entry != null && !string.IsNullOrEmpty(entry.entity_id) &&
                   string.Equals(entry.asset_type, "sprite", StringComparison.Ordinal);
        }

        private static string Validate(GdaiAssetRegistryData data)
        {
            if (data == null) return "registry_data_missing";
            if (data.entries == null) return "registry_entries_missing";
            var assetIds = new HashSet<string>(StringComparer.Ordinal);
            var unityPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in data.entries)
            {
                if (entry == null) return "registry_entry_null";
                string invalid = ValidateEntry(entry.asset_id, entry.unity_path, entry.asset_type,
                    entry.mime_type, entry.entity_id, entry.source_project_id, entry.sha256,
                    entry.project_audio_asset_id, entry.audio_candidate_id, entry.audio_category, true);
                if (invalid != null) return invalid;
                if (!assetIds.Add(entry.asset_id)) return "registry_asset_id_duplicate:" + entry.asset_id;
                if (!unityPaths.Add(entry.unity_path)) return "registry_unity_path_duplicate:" + entry.unity_path;
            }
            return null;
        }

        private static string ValidateEntry(string assetId, string unityPath, string assetType,
            string mimeType, string entityId, string projectId, string sha256,
            string projectAudioAssetId, string audioCandidateId, string audioCategory, bool allowLegacyEntityEntry)
        {
            if (string.IsNullOrEmpty(assetId)) return "registry_asset_id_missing";
            if (string.IsNullOrEmpty(unityPath)) return "registry_unity_path_missing:" + assetId;
            string path = unityPath.Replace('\\', '/');
            if (!path.StartsWith("Assets/GDAI_Generated/", StringComparison.Ordinal) || path.Contains(".."))
                return "registry_unity_path_invalid:" + assetId;
            if (string.IsNullOrEmpty(entityId) && string.IsNullOrEmpty(projectId))
                return "registry_source_identity_missing:" + assetId;
            if (!allowLegacyEntityEntry && string.IsNullOrEmpty(projectId))
                return "registry_project_lineage_missing:" + assetId;
            if (!allowLegacyEntityEntry && (string.IsNullOrEmpty(sha256) ||
                !System.Text.RegularExpressions.Regex.IsMatch(sha256, "^[a-fA-F0-9]{64}$")))
                return "registry_sha256_invalid:" + assetId;
            var types = new HashSet<string>(StringComparer.Ordinal)
                { "sprite", "image", "audio", "ui", "vfx", "animation", "other" };
            if (!types.Contains(assetType ?? string.Empty)) return "registry_asset_type_invalid:" + assetId;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            string expectedMime = extension == ".png" ? "image/png" :
                extension == ".jpg" || extension == ".jpeg" ? "image/jpeg" :
                extension == ".webp" ? "image/webp" :
                extension == ".wav" ? "audio/wav" :
                extension == ".mp3" ? "audio/mpeg" :
                extension == ".ogg" ? "audio/ogg" : null;
            if (expectedMime == null || !string.Equals(mimeType, expectedMime, StringComparison.Ordinal))
                return "registry_media_mismatch:" + assetId;
            bool audio = expectedMime.StartsWith("audio/", StringComparison.Ordinal);
            if (audio != string.Equals(assetType, "audio", StringComparison.Ordinal))
                return "registry_asset_type_media_mismatch:" + assetId;
            bool hasProjectAudioIdentity = !string.IsNullOrEmpty(projectAudioAssetId) || !string.IsNullOrEmpty(audioCandidateId);
            if (audio && string.IsNullOrEmpty(entityId))
            {
                if (!string.Equals(audioCategory, "bgm", StringComparison.Ordinal) ||
                    !string.Equals(projectAudioAssetId, assetId, StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(audioCandidateId) || string.IsNullOrEmpty(projectId))
                    return "registry_bgm_lineage_incomplete:" + assetId;
            }
            else if (audio && !string.IsNullOrEmpty(entityId))
            {
                bool legacy = allowLegacyEntityEntry && string.IsNullOrEmpty(audioCategory) && !hasProjectAudioIdentity;
                if (!legacy && (!string.Equals(audioCategory, "sfx", StringComparison.Ordinal) || hasProjectAudioIdentity))
                    return "registry_sfx_lineage_invalid:" + assetId;
            }
            else if (!audio && (!string.IsNullOrEmpty(audioCategory) || hasProjectAudioIdentity))
                return "registry_non_audio_lineage_invalid:" + assetId;
            return null;
        }

        private static bool TryLoadSprite(GdaiAssetRegistryEntry entry, out Sprite sprite, out string reason)
        {
            reason = null;
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(entry.unity_path);
            if (sprite != null) return true;
            // Texture exists but no Sprite sub-asset → importer type not Sprite (warn precisely).
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(entry.unity_path);
            reason = tex != null
                ? "texture_present_but_not_sprite_import_type:" + entry.unity_path
                : "asset_file_missing:" + entry.unity_path;
            return false;
        }
    }
}
