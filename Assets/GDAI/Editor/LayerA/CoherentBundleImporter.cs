using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

// =====================================================================================
// GDAI Unity Plugin · Layer A · fetch (Channel A: hot_reload_snapshots) + safe import.
// Import = backup -> clean-replace -> verbatim write -> AssetDatabase.Refresh.
// NEVER merges into a stale Assets/GDAI_Generated. NEVER rewrites generated C#.
// Layer A does NOT auto-bind references, create scenes/prefabs, or fix packages.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerA
{
    public static class CoherentBundleImporter
    {
        public const string GeneratedFolder = "Assets/GDAI_Generated";
        // Backups live OUTSIDE Assets/ so Unity never compiles backed-up .cs files
        // (compiling them caused duplicate type/member errors, CS0101/CS0111). Still
        // project-relative, recoverable, revealable in Finder, and gitignorable (.gdai/).
        public const string BackupRoot = ".gdai/generated_backups";

        private const string SelectColumns =
            "select=id,project_id,session_id,status,target_engine,assets,context_snapshot,created_at";

        // ---- Channel A fetch: explicit snapshot id ----
        public static async Task<GdaiHotReloadSnapshot> FetchById(string supabaseUrl, string anonKey, string snapshotId)
        {
            string url = $"{supabaseUrl.TrimEnd('/')}/rest/v1/hot_reload_snapshots" +
                         $"?id=eq.{UnityWebRequest.EscapeURL(snapshotId)}&{SelectColumns}";
            var list = await GetArray(url, anonKey);
            if (list == null || list.Count == 0)
                throw new Exception($"No snapshot found for id {snapshotId}.");
            return list[0];
        }

        // ---- Channel A fetch: latest valid coherent bundle for a project ----
        public static async Task<GdaiHotReloadSnapshot> FetchLatestCoherent(string supabaseUrl, string anonKey, string projectId)
        {
            string url = $"{supabaseUrl.TrimEnd('/')}/rest/v1/hot_reload_snapshots" +
                         $"?project_id=eq.{UnityWebRequest.EscapeURL(projectId)}" +
                         "&target_engine=eq.unity&order=created_at.desc&limit=20&" + SelectColumns;
            var list = await GetArray(url, anonKey);
            if (list == null) return null;

            foreach (var snap in list)
            {
                var ctx = snap.context_snapshot;
                if (ctx == null) continue;
                bool icOk = ctx.integrationController != null &&
                            (ctx.integrationController.status == "generated" || ctx.integrationController.status == "partial");
                if (ctx.source == "codegen-assembly" &&
                    ctx.bundleType == "unity_core_bundle" &&
                    ctx.compileReadySharedTypes &&
                    icOk)
                {
                    return snap;
                }
            }
            return null; // none matched the coherent-bundle gate
        }

        private static async Task<List<GdaiHotReloadSnapshot>> GetArray(string url, string anonKey)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("Authorization", $"Bearer {anonKey}");
                req.SetRequestHeader("apikey", anonKey);
                req.SetRequestHeader("Accept", "application/json");

                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                if (req.result != UnityWebRequest.Result.Success)
                    throw new Exception($"HTTP {req.responseCode}: {req.error}\n{req.downloadHandler.text}");

                string json = req.downloadHandler.text;
                // PostgREST returns a JSON array; Newtonsoft parses nested JSONB columns directly.
                return JsonConvert.DeserializeObject<List<GdaiHotReloadSnapshot>>(json);
            }
        }

        /// <summary>
        /// Backup + clean-replace + verbatim write. Caller MUST run CoherentBundleValidator first.
        /// Returns RefreshTriggered on success, FailedWrite on failure (with best-effort restore).
        /// Preserves existing .meta (script GUIDs) for same-path files so scene MonoScript
        /// references survive the clean-replace.
        /// </summary>
        public static BundleStatus ImportVerbatim(GdaiHotReloadSnapshot snap, out string message, out List<string> written, out string backupPath, out int preservedMetaCount)
        {
            written = new List<string>();
            backupPath = null;
            message = string.Empty;
            preservedMetaCount = 0;

            string projectRoot = Directory.GetParent(Application.dataPath).FullName; // <project>/
            string genAbs = Path.GetFullPath(Path.Combine(projectRoot, GeneratedFolder));
            string genGuard = genAbs.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string backupAbs = null;

            try
            {
                // 0. Capture existing .meta (script GUIDs) for SAME-PATH incoming files, BEFORE the
                //    folder is moved to backup. Restoring these beside the new files keeps Unity's
                //    MonoScript GUIDs stable, so existing scene component references don't break.
                var preservedMetas = CaptureExistingMetas(genAbs, snap.assets);
                string folderMetaAbs = genAbs + ".meta"; // Assets/GDAI_Generated.meta (sibling of the folder)
                string folderMeta = File.Exists(folderMetaAbs) ? File.ReadAllText(folderMetaAbs) : null;

                // 1. Backup + remove any existing generated folder (no stale merge).
                if (Directory.Exists(genAbs))
                {
                    string ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    // <projectRoot>/.gdai/generated_backups/<ts>/GDAI_Generated  (outside Assets/)
                    string backupParentAbs = Path.GetFullPath(Path.Combine(projectRoot, BackupRoot, ts));
                    backupAbs = Path.Combine(backupParentAbs, "GDAI_Generated");
                    Directory.CreateDirectory(backupParentAbs);
                    Directory.Move(genAbs, backupAbs); // moves the whole stale tree (incl. .meta) away
                    backupPath = ToProjectRelative(projectRoot, backupAbs);
                }

                // 2. Fresh folder (+ preserve the folder's own .meta GUID).
                Directory.CreateDirectory(genAbs);
                if (folderMeta != null) File.WriteAllText(folderMetaAbs, folderMeta);

                // 3. Verbatim write, with a final per-file containment re-check, restoring the
                //    captured .meta for each same-path file so its script GUID is preserved.
                foreach (var a in snap.assets)
                {
                    string rel = a.path.Replace('\\', '/');
                    string abs = Path.GetFullPath(Path.Combine(projectRoot, rel));
                    if (!abs.StartsWith(genGuard, StringComparison.Ordinal))
                        throw new Exception($"Refusing to write outside generated folder: {rel}");

                    Directory.CreateDirectory(Path.GetDirectoryName(abs));
                    File.WriteAllText(abs, a.content ?? string.Empty); // verbatim, UTF-8 no BOM
                    written.Add(rel);

                    string relInGen = RelWithinGenerated(rel);
                    if (relInGen != null && preservedMetas.TryGetValue(relInGen, out var metaContent))
                    {
                        File.WriteAllText(abs + ".meta", metaContent); // preserve old GUID for this path
                        preservedMetaCount++;
                    }
                }
            }
            catch (Exception e)
            {
                message = $"FAILED_WRITE: {e.Message}";
                // Best-effort restore of the previous generated folder.
                try
                {
                    if (backupAbs != null && Directory.Exists(backupAbs))
                    {
                        if (Directory.Exists(genAbs)) Directory.Delete(genAbs, true);
                        Directory.Move(backupAbs, genAbs);
                        message += " (restored previous Assets/GDAI_Generated from backup)";
                    }
                }
                catch
                {
                    message += " (RESTORE ALSO FAILED — recover manually from " + (backupPath ?? BackupRoot) + ")";
                }
                AssetDatabase.Refresh();
                return BundleStatus.FailedWrite;
            }

            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(GeneratedFolder, ImportAssetOptions.ImportRecursive);
            message = $"IMPORTED {written.Count} assets into {GeneratedFolder}. Preserved {preservedMetaCount} existing meta GUID(s). " +
                      $"Backup: {(backupPath ?? "none")}. Watch the Unity Console for the compile result.";
            return BundleStatus.RefreshTriggered;
        }

        // Capture existing .meta contents for incoming same-path files (keyed by path within the
        // generated folder, e.g. "InputManager.cs"). Only incoming paths are captured, so files that
        // are NOT in the new snapshot are still dropped (clean-replace semantics preserved).
        private static Dictionary<string, string> CaptureExistingMetas(string generatedFolderAbs, IEnumerable<GdaiSnapshotAsset> assets)
        {
            var map = new Dictionary<string, string>();
            if (assets == null || !Directory.Exists(generatedFolderAbs)) return map;
            foreach (var a in assets)
            {
                string relInGen = a != null ? RelWithinGenerated(a.path) : null;
                if (string.IsNullOrEmpty(relInGen)) continue;
                string oldMetaAbs = Path.Combine(generatedFolderAbs, relInGen) + ".meta";
                if (File.Exists(oldMetaAbs) && !map.ContainsKey(relInGen))
                    map[relInGen] = File.ReadAllText(oldMetaAbs);
            }
            return map;
        }

        // "Assets/GDAI_Generated/InputManager.cs" -> "InputManager.cs"; null if outside the folder.
        private static string RelWithinGenerated(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            string p = assetPath.Replace('\\', '/');
            string prefix = GeneratedFolder + "/";
            return p.StartsWith(prefix, StringComparison.Ordinal) ? p.Substring(prefix.Length) : null;
        }

        private static string ToProjectRelative(string projectRoot, string abs)
        {
            string full = Path.GetFullPath(abs);
            string root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.Ordinal)
                ? full.Substring(root.Length).Replace('\\', '/')
                : full;
        }
    }
}
