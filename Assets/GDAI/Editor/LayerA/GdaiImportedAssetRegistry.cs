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
//   entity_id / asset_id  →  registry entry  →  loadable Sprite.
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
        /// Entries without a source entity_id are excluded (registry is entity-addressed).
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

            foreach (var a in dtoAssets)
            {
                if (a == null || string.IsNullOrEmpty(a.unity_path)) continue;
                string rel = a.unity_path.Replace('\\', '/');
                if (!writtenUnityPaths.Contains(rel)) continue; // only files that really materialized
                string entityId = a.source != null ? a.source.entity_id : null;
                if (string.IsNullOrEmpty(entityId)) continue;   // entity-addressed registry only

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
                return JsonConvert.DeserializeObject<GdaiAssetRegistryData>(File.ReadAllText(absPath));
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
            var entry = data.entries.Find(e => e != null && e.entity_id == entityId);
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
            var entry = data.entries.Find(e => e != null && e.asset_id == assetId);
            if (entry == null) { reason = "asset_not_in_registry"; return false; }
            return TryLoadSprite(entry, out sprite, out reason);
        }

        /// <summary>All registry entries (empty list when registry absent). For Jason/Layer B enumeration.</summary>
        public static List<GdaiAssetRegistryEntry> All()
        {
            var data = Load();
            return data != null && data.entries != null ? data.entries : new List<GdaiAssetRegistryEntry>();
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
