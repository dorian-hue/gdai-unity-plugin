using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// =====================================================================================
// GDAI Unity Plugin · Layer A · DOWNSTREAM-BUILD-1 · binary asset payload import.
//
// Additive companion to CoherentBundleImporter: AFTER the text/code bundle import
// succeeds, this materializes server-resolved base64 payloads (sprites/images; audio
// schema-ready) as real files under Assets/GDAI_Generated/ and imports them via
// AssetDatabase. It NEVER touches the text/code import path, NEVER fetches from the
// network, and NEVER fails the whole import — every asset is isolated (skip + reason).
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
            public int Imported;
            public readonly List<string> WrittenPaths = new List<string>();
            public readonly List<string> SkippedWithReason = new List<string>();
        }

        // ---------- PURE VALIDATION (no Unity APIs) ----------

        /// <summary>Returns null when the entry is importable; otherwise a skip reason.</summary>
        public static string ValidateEntry(GdaiBundleProxyAsset a)
        {
            if (a == null) return "null_entry";
            if (string.IsNullOrEmpty(a.unity_path)) return "missing_unity_path";

            string p = a.unity_path.Replace('\\', '/');
            if (!p.StartsWith(GeneratedRootPrefix, StringComparison.Ordinal)) return "path_outside_generated_root";
            if (p.Contains("..")) return "path_contains_dotdot";
            if (Path.IsPathRooted(p) || Regex.IsMatch(p, "^[A-Za-z]:")) return "absolute_path_rejected";

            string ext = Path.GetExtension(p);
            if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext)) return "unsupported_extension:" + (ext ?? "none");

            if (a.payload_mode != "base64") return "unsupported_payload_mode:" + (a.payload_mode ?? "null");
            if (string.IsNullOrEmpty(a.payload_base64)) return "empty_payload";
            return null;
        }

        /// <summary>Pure decode + optional integrity check. Returns null + reason on failure.</summary>
        public static byte[] DecodePayload(GdaiBundleProxyAsset a, out string failReason)
        {
            failReason = null;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(a.payload_base64); }
            catch (FormatException) { failReason = "invalid_base64"; return null; }

            if (bytes == null || bytes.Length == 0) { failReason = "decoded_empty"; return null; }

            // sha256 (hex over raw bytes) — backend declares it; verify when present.
            if (!string.IsNullOrEmpty(a.sha256))
            {
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
            }
            return bytes;
        }

        // ---------- UNITY-TOUCHING IMPORT ----------

        /// <summary>
        /// Writes every valid payload under Assets/GDAI_Generated/ and imports it.
        /// Per-asset isolation: one bad asset never aborts the rest. Never throws.
        /// After writing, generates the entity asset registry (DOWNSTREAM-BUILD-2) so
        /// imported sprites are addressable by entity_id / asset_id.
        /// </summary>
        public static ImportSummary ImportAll(List<GdaiBundleProxyAsset> assets, string sourceSnapshotId = null)
        {
            var summary = new ImportSummary();
            if (assets == null || assets.Count == 0) return summary;

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string genGuard = Path.GetFullPath(Path.Combine(projectRoot, "Assets/GDAI_Generated"))
                                  .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            foreach (var a in assets)
            {
                string label = a != null ? (a.file_name ?? a.asset_id ?? "?") : "?";
                try
                {
                    string invalid = ValidateEntry(a);
                    if (invalid != null) { summary.SkippedWithReason.Add(label + ": " + invalid); continue; }

                    var bytes = DecodePayload(a, out string decodeFail);
                    if (bytes == null) { summary.SkippedWithReason.Add(label + ": " + decodeFail); continue; }

                    string rel = a.unity_path.Replace('\\', '/');
                    string abs = Path.GetFullPath(Path.Combine(projectRoot, rel));
                    // Final absolute-path containment re-check (mirrors CoherentBundleImporter).
                    if (!abs.StartsWith(genGuard, StringComparison.Ordinal))
                    {
                        summary.SkippedWithReason.Add(label + ": containment_recheck_failed");
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(abs));
                    File.WriteAllBytes(abs, bytes);
                    AssetDatabase.ImportAsset(rel);
                    TrySetSpriteImportSettings(rel);

                    summary.Imported++;
                    summary.WrittenPaths.Add(rel);
                }
                catch (Exception e)
                {
                    summary.SkippedWithReason.Add(label + ": exception:" + e.Message);
                }
            }

            // ---- DOWNSTREAM-BUILD-2 · entity asset registry (never fails the import) ----
            try
            {
                var registry = GdaiImportedAssetRegistry.Build(
                    assets, new HashSet<string>(summary.WrittenPaths), sourceSnapshotId);
                if (registry.entries.Count > 0)
                {
                    string regErr;
                    if (GdaiImportedAssetRegistry.Write(registry, out regErr))
                        Debug.Log($"[GDAI][Assets][Registry] Registered {registry.entries.Count} entity sprite(s) → {GdaiImportedAssetRegistry.RegistryPath}");
                    else
                        Debug.LogWarning("[GDAI][Assets][Registry] Registry write failed (import itself succeeded): " + regErr);
                }
            }
            catch (Exception regEx)
            {
                Debug.LogWarning("[GDAI][Assets][Registry] Registry generation failed (import itself succeeded): " + regEx.Message);
            }

            try { AssetDatabase.Refresh(); } catch { /* refresh best-effort */ }

            Debug.Log(string.Format(
                "[GDAI][LayerA][Assets] Imported {0} binary asset(s), skipped {1}.{2}{3}",
                summary.Imported,
                summary.SkippedWithReason.Count,
                summary.WrittenPaths.Count > 0 ? "\n  written: " + string.Join(", ", summary.WrittenPaths) : "",
                summary.SkippedWithReason.Count > 0 ? "\n  skipped: " + string.Join(" | ", summary.SkippedWithReason) : ""));

            return summary;
        }

        // Images → Sprite import settings (safe/guarded; default import already yields a
        // usable Texture2D, so failure here is non-fatal by design).
        private static void TrySetSpriteImportSettings(string unityPath)
        {
            try
            {
                string ext = Path.GetExtension(unityPath);
                if (string.IsNullOrEmpty(ext) || !ImageExtensions.Contains(ext)) return;

                var importer = AssetImporter.GetAtPath(unityPath) as TextureImporter;
                if (importer == null) return;
                if (importer.textureType == TextureImporterType.Sprite) return;

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[GDAI][LayerA][Assets] Sprite import settings skipped for " + unityPath + ": " + e.Message);
            }
        }
    }
}
