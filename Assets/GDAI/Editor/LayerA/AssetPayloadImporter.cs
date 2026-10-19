using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// =====================================================================================
// GDAI Unity Plugin · Layer A · DOWNSTREAM-BUILD-1 · binary asset payload import.
//
// Additive companion to CoherentBundleImporter: AFTER the text/code bundle import
// succeeds, this materializes server-resolved base64 payloads as one atomic batch under
// Assets/GDAI_Generated/. The complete batch is validated before the first file write;
// any later filesystem, import-settings, or registry failure restores prior bytes/meta.
//
// Validation/decode planning is PURE (no Unity APIs) so it can be reasoned about and
// mirrored by the backend fixture harness (scripts/test-asset-transport-fixture.ts in
// the flowcraft repo enforces the same path/extension rules server-side).
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerA
{
    public static class AssetPayloadImporter
    {
        public const string GeneratedRootPrefix = "Assets/GDAI_Generated/";

        // Image slice enabled now; audio extensions are schema-ready (accepted here so a
        // future backend slice needs no plugin update), per task "接口宽,数据流窄".
        private static readonly HashSet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp",
            ".wav", ".mp3", ".ogg",
        };

        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp",
        };

        public class ImportSummary
        {
            public bool Succeeded;
            public int Imported;
            public string Error;
            public readonly List<string> WrittenPaths = new List<string>();
            public readonly List<string> SkippedWithReason = new List<string>();
        }

        private sealed class PlannedAsset
        {
            public GdaiBundleProxyAsset Asset;
            public string RelativePath;
            public string AbsolutePath;
            public byte[] Bytes;
        }

        private sealed class FileState
        {
            public string Path;
            public bool FileExisted;
            public byte[] Bytes;
            public bool MetaExisted;
            public byte[] MetaBytes;
            public bool DirectoryOccupied;
        }

        // ---------- PURE VALIDATION (no Unity APIs) ----------

        /// <summary>Returns null when the entry is importable; otherwise a skip reason.</summary>
        public static string ValidateEntry(GdaiBundleProxyAsset a)
        {
            if (a == null) return "null_entry";
            if (string.IsNullOrEmpty(a.asset_id)) return "missing_asset_id";
            if (string.IsNullOrEmpty(a.unity_path)) return "missing_unity_path";

            string p = a.unity_path.Replace('\\', '/');
            if (!p.StartsWith(GeneratedRootPrefix, StringComparison.Ordinal)) return "path_outside_generated_root";
            if (p.Contains("..")) return "path_contains_dotdot";
            if (Path.IsPathRooted(p) || Regex.IsMatch(p, "^[A-Za-z]:")) return "absolute_path_rejected";

            string ext = Path.GetExtension(p);
            if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext)) return "unsupported_extension:" + (ext ?? "none");

            string expectedMime = ExpectedMime(ext);
            if (!string.Equals(a.mime_type, expectedMime, StringComparison.Ordinal)) return "media_mismatch";
            bool audio = expectedMime.StartsWith("audio/", StringComparison.Ordinal);
            if (audio != string.Equals(a.asset_type, "audio", StringComparison.Ordinal)) return "asset_type_media_mismatch";

            if (a.payload_mode != "base64") return "unsupported_payload_mode:" + (a.payload_mode ?? "null");
            if (string.IsNullOrEmpty(a.payload_base64)) return "empty_payload";
            if (a.source == null || string.IsNullOrEmpty(a.source.project_id)) return "missing_project_lineage";
            if (string.IsNullOrEmpty(a.sha256) || !Regex.IsMatch(a.sha256, "^[a-fA-F0-9]{64}$")) return "invalid_sha256";
            if (a.byte_size <= 0) return "invalid_byte_size";
            return null;
        }

        private static string ExpectedMime(string extension)
        {
            string ext = extension.ToLowerInvariant();
            if (ext == ".png") return "image/png";
            if (ext == ".jpg" || ext == ".jpeg") return "image/jpeg";
            if (ext == ".webp") return "image/webp";
            if (ext == ".wav") return "audio/wav";
            if (ext == ".mp3") return "audio/mpeg";
            if (ext == ".ogg") return "audio/ogg";
            return string.Empty;
        }

        /// <summary>Pure decode + optional integrity check. Returns null + reason on failure.</summary>
        public static byte[] DecodePayload(GdaiBundleProxyAsset a, out string failReason)
        {
            failReason = null;
            if (a == null || string.IsNullOrEmpty(a.payload_base64)) { failReason = "empty_payload"; return null; }
            if (string.IsNullOrEmpty(a.sha256)) { failReason = "invalid_sha256"; return null; }
            byte[] bytes;
            try { bytes = Convert.FromBase64String(a.payload_base64); }
            catch (FormatException) { failReason = "invalid_base64"; return null; }

            if (bytes == null || bytes.Length == 0) { failReason = "decoded_empty"; return null; }

            if (bytes.LongLength != a.byte_size) { failReason = "byte_size_mismatch"; return null; }
            // sha256 (hex over raw bytes) is mandatory for an atomic admitted batch.
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(hex, a.sha256.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                {
                    failReason = "sha256_mismatch";
                    return null;
                }
            }
            return bytes;
        }

        /// <summary>Validates the complete delivered batch without filesystem or Unity mutation.</summary>
        public static List<string> ValidateBatch(List<GdaiBundleProxyAsset> assets, string expectedProjectId = null)
        {
            var errors = new List<string>();
            if (assets == null || assets.Count == 0) return errors;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var projects = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asset in assets)
            {
                string label = asset != null ? (asset.asset_id ?? asset.file_name ?? "?") : "?";
                string invalid = ValidateEntry(asset);
                if (invalid != null) { errors.Add(label + ": " + invalid); continue; }
                string path = asset.unity_path.Replace('\\', '/');
                if (!ids.Add(asset.asset_id)) errors.Add(label + ": duplicate_asset_id");
                if (!paths.Add(path)) errors.Add(label + ": duplicate_unity_path");
                projects.Add(asset.source.project_id);
                if (!string.IsNullOrEmpty(expectedProjectId) && asset.source.project_id != expectedProjectId)
                    errors.Add(label + ": project_lineage_mismatch");
                if (DecodePayload(asset, out string decodeFailure) == null)
                    errors.Add(label + ": " + decodeFailure);
            }
            if (projects.Count > 1) errors.Add("cross_project_asset_batch");
            if (errors.Count == 0)
            {
                try { GdaiImportedAssetRegistry.Build(assets, paths, string.Empty); }
                catch (Exception e) { errors.Add("registry_preflight: " + e.Message); }
            }
            return errors;
        }

        // ---------- UNITY-TOUCHING IMPORT ----------

        /// <summary>
        /// Atomically writes the admitted batch and its registry. Never throws: failure
        /// is visible in the returned summary after restoring every prior file and meta.
        /// </summary>
        public static ImportSummary ImportAll(List<GdaiBundleProxyAsset> assets, string sourceSnapshotId = null)
        {
            var summary = new ImportSummary();
            if (assets == null || assets.Count == 0) { summary.Succeeded = true; return summary; }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string genGuard = Path.GetFullPath(Path.Combine(projectRoot, "Assets/GDAI_Generated"))
                                  .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var validation = ValidateBatch(assets);
            if (validation.Count > 0)
            {
                summary.Error = "binary asset batch rejected before mutation: " + string.Join(" | ", validation);
                summary.SkippedWithReason.AddRange(validation);
                return summary;
            }

            var plans = new List<PlannedAsset>();
            foreach (var asset in assets)
            {
                string relative = asset.unity_path.Replace('\\', '/');
                string absolute = Path.GetFullPath(Path.Combine(projectRoot, relative));
                if (!absolute.StartsWith(genGuard, StringComparison.Ordinal) || Directory.Exists(absolute))
                {
                    summary.Error = "binary asset batch rejected before mutation: path containment or occupied target: " + relative;
                    summary.SkippedWithReason.Add(summary.Error);
                    return summary;
                }
                plans.Add(new PlannedAsset { Asset = asset, RelativePath = relative, AbsolutePath = absolute, Bytes = DecodePayload(asset, out _) });
            }

            string registryAbsolute = Path.GetFullPath(Path.Combine(projectRoot, GdaiImportedAssetRegistry.RegistryPath));
            List<FileState> states;
            try
            {
                states = plans.Select(plan => Capture(plan.AbsolutePath)).ToList();
                states.Add(Capture(registryAbsolute));
            }
            catch (Exception captureFailure)
            {
                summary.Error = "binary asset batch rejected before mutation: prior-state capture failed: " + captureFailure.Message;
                summary.SkippedWithReason.Add(summary.Error);
                return summary;
            }
            var createdDirectories = new List<string>();
            try
            {
                EnsureDirectory(Path.GetDirectoryName(registryAbsolute), createdDirectories);
                foreach (var plan in plans)
                {
                    EnsureDirectory(Path.GetDirectoryName(plan.AbsolutePath), createdDirectories);
                    File.WriteAllBytes(plan.AbsolutePath, plan.Bytes);
                    AssetDatabase.ImportAsset(plan.RelativePath, ImportAssetOptions.ForceSynchronousImport);
                    SetSpriteImportSettings(plan.RelativePath);
                }
                var registry = GdaiImportedAssetRegistry.Build(
                    assets, new HashSet<string>(plans.Select(plan => plan.RelativePath)), sourceSnapshotId);
                string registryError;
                if (!GdaiImportedAssetRegistry.Write(registry, out registryError))
                    throw new IOException("registry write failed: " + registryError);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                summary.Imported = plans.Count;
                summary.WrittenPaths.AddRange(plans.Select(plan => plan.RelativePath));
                summary.Succeeded = true;
                Debug.Log($"[GDAI][LayerA][Assets] Atomically imported {summary.Imported} binary asset(s) and registry.");
            }
            catch (Exception failure)
            {
                var rollbackErrors = Restore(states, createdDirectories);
                summary.Imported = 0;
                summary.WrittenPaths.Clear();
                summary.Error = "binary asset transaction failed: " + failure.Message +
                    (rollbackErrors.Count == 0 ? "" : " | rollback failed: " + string.Join("; ", rollbackErrors));
                summary.SkippedWithReason.Add(summary.Error);
                Debug.LogError("[GDAI][LayerA][Assets] " + summary.Error);
            }
            return summary;
        }

        private static FileState Capture(string path)
        {
            return new FileState
            {
                Path = path,
                DirectoryOccupied = Directory.Exists(path),
                FileExisted = File.Exists(path),
                Bytes = File.Exists(path) ? File.ReadAllBytes(path) : null,
                MetaExisted = File.Exists(path + ".meta"),
                MetaBytes = File.Exists(path + ".meta") ? File.ReadAllBytes(path + ".meta") : null,
            };
        }

        private static void EnsureDirectory(string directory, List<string> created)
        {
            if (Directory.Exists(directory)) return;
            var missing = new Stack<string>();
            for (string cursor = directory; !Directory.Exists(cursor); cursor = Path.GetDirectoryName(cursor)) missing.Push(cursor);
            while (missing.Count > 0) { string path = missing.Pop(); Directory.CreateDirectory(path); created.Add(path); }
        }

        private static List<string> Restore(List<FileState> states, List<string> createdDirectories)
        {
            var errors = new List<string>();
            foreach (var state in states)
            {
                try
                {
                    if (!state.DirectoryOccupied)
                    {
                        if (state.FileExisted) File.WriteAllBytes(state.Path, state.Bytes);
                        else if (File.Exists(state.Path)) File.Delete(state.Path);
                    }
                    if (state.MetaExisted) File.WriteAllBytes(state.Path + ".meta", state.MetaBytes);
                    else if (File.Exists(state.Path + ".meta")) File.Delete(state.Path + ".meta");
                }
                catch (Exception e) { errors.Add(Path.GetFileName(state.Path) + ": " + e.Message); }
            }
            try { AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); }
            catch (Exception e) { errors.Add("asset refresh: " + e.Message); }
            for (int i = createdDirectories.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (Directory.Exists(createdDirectories[i]) && !Directory.EnumerateFileSystemEntries(createdDirectories[i]).Any())
                    {
                        Directory.Delete(createdDirectories[i]);
                        if (File.Exists(createdDirectories[i] + ".meta")) File.Delete(createdDirectories[i] + ".meta");
                    }
                }
                catch (Exception e) { errors.Add("directory cleanup: " + e.Message); }
            }
            try { AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); }
            catch (Exception e) { errors.Add("post-cleanup refresh: " + e.Message); }
            return errors;
        }

        private static void SetSpriteImportSettings(string unityPath)
        {
            string ext = Path.GetExtension(unityPath);
            if (string.IsNullOrEmpty(ext) || !ImageExtensions.Contains(ext)) return;

            var importer = AssetImporter.GetAtPath(unityPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("texture importer unavailable: " + unityPath);
            if (importer.textureType == TextureImporterType.Sprite) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }
    }
}
