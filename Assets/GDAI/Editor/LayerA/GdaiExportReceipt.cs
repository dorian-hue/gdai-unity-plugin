using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerA
{
    // Completion receipt for a user-triggered Complete GDAI Export / Sync.
    // Written ONLY on full success, under <project root>/.gdai/receipts/.
    // Carries no token, no Authorization material, no secrets; path fields
    // are project-relative.
    [Serializable]
    public sealed class GdaiExportReceiptData
    {
        public string schema_version = "gdai.export_completion.v1";
        public string project_id;
        public string snapshot_id;
        public string snapshot_revision;
        public string plugin_version;
        public int generated_file_count;
        public int binary_asset_count;
        public int preserved_meta_count;
        public string backup_path;          // project-relative
        public string scene_path;           // project-relative
        public int scene_elements_count;
        public int collider_count;
        public string started_at;           // ISO-8601 UTC
        public string completed_at;         // ISO-8601 UTC
        public string result;               // PASS only (failures write no receipt)
    }

    public static class GdaiExportReceipt
    {
        public const string ReceiptsDirRelative = ".gdai/receipts";

        public static string BuildFileName(DateTime utc, string snapshotId)
        {
            string snap8 = string.IsNullOrEmpty(snapshotId)
                ? "nosnap"
                : (snapshotId.Length > 8 ? snapshotId.Substring(0, 8) : snapshotId);
            return utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) +
                   "-" + snap8 + "-export-completion.json";
        }

        // Returns the absolute path written. Caller supplies a fully populated
        // receipt; this writer never invents values and never writes secrets.
        public static string Write(string projectRoot, GdaiExportReceiptData receipt, DateTime utcNow)
        {
            if (receipt == null) throw new ArgumentNullException(nameof(receipt));
            if (receipt.result != "PASS")
                throw new InvalidOperationException("only PASS receipts may be written (§9.4 failure semantics)");
            string dir = Path.Combine(projectRoot, ReceiptsDirRelative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, BuildFileName(utcNow, receipt.snapshot_id));
            File.WriteAllText(path, JsonUtility.ToJson(receipt, true));
            return path;
        }
    }
}
