using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Networking;

namespace GDAI.Bridge
{
    /// <summary>
    /// Polls hot reload snapshots and applies incoming assets into the Unity project.
    /// </summary>
    public class HotReloadListener : MonoBehaviour
    {
        [Header("Supabase Connection")]
        [SerializeField]
        private string supabaseUrl = "https://YOUR-PROJECT.supabase.co";

        [SerializeField] [TextArea(3, 5)]
        private string anonKey = "PASTE_ANON_KEY_HERE";

        [SerializeField, HideInInspector] // Deprecated in v0.1.0-alpha.2 · use anonKey
        private string jwtToken = "";

        [Header("Polling Target")]
        [SerializeField]
        [Tooltip("Optional. Leave empty to auto-poll latest pending snapshot for the project (v0.1.0-alpha.5 default).")]
        private string sessionId = "";

        [SerializeField] [TextArea(3, 5)]
        [Tooltip("Project ID · used as query filter when fetching snapshots")]
        private string projectId = "";

        [Header("Polling Behavior")]
        [SerializeField] private float pollIntervalSeconds = 2.0f;
        [SerializeField]
        [Tooltip("Total polling duration. 0 = unlimited (default v0.1.0-alpha.5).")]
        private int pollTimeoutSeconds = 0;
        [SerializeField]
        [Tooltip("Stop polling after first snapshot. Default false (v0.1.0-alpha.5 continuous mode).")]
        private bool stopAfterFirstSnapshot = false;

        private string _lastSnapshotId;
        private HashSet<string> _processedSnapshotIds = new HashSet<string>();

        private const string EDITOR_PREFS_KEY_PREFIX = "GDAI_LastSnapshotId_";

        private string GetEditorPrefsKey()
        {
            return EDITOR_PREFS_KEY_PREFIX + (string.IsNullOrEmpty(projectId) ? "default" : projectId);
        }

        private bool _isPolling;

        void Start()
        {
            if (string.IsNullOrWhiteSpace(anonKey) || anonKey == "PASTE_ANON_KEY_HERE")
            {
                Debug.LogError("[GDAI] Anon Key not set. Fill 'Anon Key' in Inspector.");
                return;
            }

            if (string.IsNullOrWhiteSpace(projectId))
            {
                Debug.LogError("[GDAI] projectId is empty; HotReloadListener cannot start. " +
                               "Fill 'Polling Target · Project Id' in Inspector with the Supabase project UUID.");
                return;
            }

            bool isValidUuid = System.Text.RegularExpressions.Regex.IsMatch(
                projectId,
                @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!isValidUuid)
            {
                Debug.LogWarning($"[GDAI] projectId '{projectId}' 不符合 UUID 格式。" +
                                 "如果是 Supabase project · 必须是 UUID。如果是别的场景 · 忽略本警告。");
            }

            Debug.Log($"[GDAI] projectId raw value: '{projectId}' (length={projectId?.Length ?? 0})");
            // sessionId 不再强制 · 空值触发 auto-poll 模式
            bool autoPollMode = string.IsNullOrWhiteSpace(sessionId);
            if (autoPollMode)
                Debug.Log($"[GDAI] HotReloadListener (v0.1.0-alpha.6) starting in AUTO-POLL mode for project: {projectId}");
            else
                Debug.Log($"[GDAI] HotReloadListener (v0.1.0-alpha.6) starting for session: {sessionId}");
            Debug.Log($"[GDAI] Polling every {pollIntervalSeconds}s, timeout {(pollTimeoutSeconds == 0 ? "unlimited" : pollTimeoutSeconds + "s")}");
#if UNITY_EDITOR
            string restoredId = UnityEditor.EditorPrefs.GetString(GetEditorPrefsKey(), null);
            if (!string.IsNullOrEmpty(restoredId))
            {
                _lastSnapshotId = restoredId;
                Debug.Log($"[GDAI] Restored _lastSnapshotId from EditorPrefs: {restoredId}");
            }
#endif

            _isPolling = true;
            StartCoroutine(PollForSnapshots());
        }

        void OnDisable() { _isPolling = false; }

        private IEnumerator PollForSnapshots()
        {
            float startTime = Time.time;
            int pollCount = 0;
            while (_isPolling && (pollTimeoutSeconds == 0 || (Time.time - startTime) < pollTimeoutSeconds))
            {
                pollCount++;
                yield return FetchLatestSnapshot(pollCount);
                if (!_isPolling) yield break;
                yield return new WaitForSeconds(pollIntervalSeconds);
            }
            if (_isPolling)
            {
                Debug.LogWarning($"[GDAI] Polling timeout after {pollTimeoutSeconds}s. Last seen: {_lastSnapshotId ?? "none"}");
            }
        }

        private IEnumerator FetchLatestSnapshot(int pollCount)
        {
            string url;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                // Auto-poll: 取 project 的最新 pending snapshot (v0.1.0-alpha.5)
                url = $"{supabaseUrl}/rest/v1/hot_reload_snapshots" +
                      $"?project_id=eq.{UnityWebRequest.EscapeURL(projectId)}" +
                      $"&status=eq.pending" +
                      $"&target_engine=eq.unity" +
                      $"&order=created_at.desc&limit=1" +
                      $"&select=id,session_id,project_id,user_id,status,target_engine,preview_url,diff,assets,created_at";
            }
            else
            {
                // Legacy session-pinned (v0.1.0-alpha.3 行为 · 向后兼容)
                url = $"{supabaseUrl}/rest/v1/hot_reload_snapshots" +
                      $"?session_id=eq.{UnityWebRequest.EscapeURL(sessionId)}" +
                      $"&project_id=eq.{UnityWebRequest.EscapeURL(projectId)}" +
                      $"&target_engine=eq.unity" +
                      $"&order=created_at.desc&limit=1" +
                      $"&select=id,session_id,project_id,user_id,status,target_engine,preview_url,diff,assets,created_at";
            }

#if UNITY_EDITOR
            if (pollCount == 1 || pollCount % 30 == 0)
            {
                Debug.Log($"[GDAI] DEBUG · Fetch URL (poll #{pollCount}): {url}");
            }
#endif

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("Authorization", $"Bearer {anonKey}");
                req.SetRequestHeader("apikey", anonKey);
                req.SetRequestHeader("Accept", "application/json");

                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[GDAI] Poll #{pollCount} failed: HTTP {req.responseCode}: {req.error}");
                    yield break;
                }

                string json = req.downloadHandler.text;
                if (string.IsNullOrWhiteSpace(json) || json == "[]")
                {
                    if (pollCount == 1 || pollCount % 5 == 0)
                        Debug.Log($"[GDAI] Poll #{pollCount}: no snapshots yet for session {sessionId}");
                    yield break;
                }

                // JsonUtility can't parse top-level arrays → wrap it.
                string wrapped = "{\"items\":" + json + "}";

                HotReloadSnapshotWrapper wrapper = null;
                try { wrapper = JsonUtility.FromJson<HotReloadSnapshotWrapper>(wrapped); }
                catch (Exception e)
                {
                    Debug.LogError($"[GDAI] Poll #{pollCount} parse error: {e.Message}");
                    Debug.LogError($"[GDAI] Raw JSON (first 500 chars): {json.Substring(0, Math.Min(500, json.Length))}");
                    yield break;
                }

                if (wrapper?.items == null || wrapper.items.Length == 0) yield break;

                HotReloadSnapshot latest = wrapper.items[0];
                if (latest.id == _lastSnapshotId) yield break;

                _lastSnapshotId = latest.id;

#if UNITY_EDITOR
                UnityEditor.EditorPrefs.SetString(GetEditorPrefsKey(), latest.id);
#endif

                if (_processedSnapshotIds.Contains(latest.id))
                {
                    yield break;
                }
                _processedSnapshotIds.Add(latest.id);
                yield return OnNewSnapshot(latest, pollCount);

                if (stopAfterFirstSnapshot)
                {
                    Debug.Log($"[GDAI] stopAfterFirstSnapshot=true, stopping polling");
                    _isPolling = false;
                }
            }
        }

        private IEnumerator OnNewSnapshot(HotReloadSnapshot snapshot, int pollCount)
        {
            Debug.Log($"[GDAI] === New snapshot received (poll #{pollCount}) ===");
            Debug.Log($"[GDAI] ID: {snapshot.id}");
            Debug.Log($"[GDAI] Session: {snapshot.session_id}");
            Debug.Log($"[GDAI] Project: {snapshot.project_id}");
            Debug.Log($"[GDAI] User: {snapshot.user_id}");
            Debug.Log($"[GDAI] Status: {snapshot.status}");
            Debug.Log($"[GDAI] Target engine: {snapshot.target_engine}");
            Debug.Log($"[GDAI] Preview URL: {snapshot.preview_url ?? "(none)"}");
            Debug.Log($"[GDAI] Created at: {snapshot.created_at}");

            int addedCount    = snapshot.diff?.added?.Length    ?? 0;
            int modifiedCount = snapshot.diff?.modified?.Length ?? 0;
            int deletedCount  = snapshot.diff?.deleted?.Length  ?? 0;
            Debug.Log($"[GDAI] Diff: added={addedCount}, modified={modifiedCount}, deleted={deletedCount}");

            if (snapshot.diff?.added != null)
                foreach (var f in snapshot.diff.added)
                    Debug.Log($"[GDAI]   + {f.path} ({f.content?.Length ?? 0} chars)");
            if (snapshot.diff?.modified != null)
                foreach (var f in snapshot.diff.modified)
                    Debug.Log($"[GDAI]   ~ {f.path} ({f.content?.Length ?? 0} chars)");
            if (snapshot.diff?.deleted != null)
                foreach (var f in snapshot.diff.deleted)
                    Debug.Log($"[GDAI]   - {f.path}");

            int assetsCount = snapshot.assets?.Length ?? 0;
            Debug.Log($"[GDAI] Assets: {assetsCount} item(s)");
            if (snapshot.assets != null)
            {
                for (int i = 0; i < snapshot.assets.Length; i++)
                {
                    var a = snapshot.assets[i];
                    string assetType = string.IsNullOrEmpty(a.type) ? a.asset_type : a.type;
                    int clen = a.content?.Length ?? 0;
                    string preview = clen > 0
                        ? a.content.Substring(0, Math.Min(120, clen)).Replace("\n", " ").Replace("\r", "")
                        : "(empty)";
                    Debug.Log($"[GDAI]   Assets[{i}]: path={a.path} | lang={a.language} | module_id={a.module_id} | asset_type={assetType} | content={clen} chars");
                    Debug.Log($"[GDAI]     preview: {preview}");
                }
            }

            Debug.Log($"[GDAI] === Snapshot received DONE ===");

            if (snapshot.assets == null || snapshot.assets.Length == 0)
            {
                yield break;
            }

            for (int i = 0; i < snapshot.assets.Length; i++)
            {
                var asset = snapshot.assets[i];
                string assetType = string.IsNullOrEmpty(asset.type) ? asset.asset_type : asset.type;

                if (assetType != "sprite" && assetType != "visual" && assetType != "code")
                {
                    Debug.Log($"[GDAI] Skipping asset type '{assetType}' (alpha.6 supports sprite/visual/code)");
                    continue;
                }

                // --- alpha.6 · code 分支:不走 URL 下载 · 直接从 asset.content 写盘 ---
                if (assetType == "code")
                {
                    WriteCodeAsset(asset, snapshot.id);
                    continue;
                }

                // sprite / visual 继续走原下载路径
                yield return DownloadAndImportAsset(asset, snapshot.id);
            }

#if UNITY_EDITOR
            AssetDatabase.Refresh();
            Debug.Log($"[GDAI] AssetDatabase refreshed · snapshot {snapshot.id.Substring(0, 8)} done");
#endif
        }

        private IEnumerator DownloadAndImportAsset(SnapshotAsset asset, string snapshotId)
        {
            const string BUCKET_NAME = "entity-assets";
            string downloadUrl = $"{supabaseUrl}/storage/v1/object/authenticated/{BUCKET_NAME}/{asset.path}";

            Debug.Log($"[GDAI] Downloading: {downloadUrl}");

            using (UnityWebRequest req = UnityWebRequest.Get(downloadUrl))
            {
                req.timeout = 30;
                // Authenticated storage download requires anon key headers.
                req.SetRequestHeader("Authorization", $"Bearer {anonKey}");
                req.SetRequestHeader("apikey", anonKey);

                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[GDAI] Download failed: {req.error} · HTTP {req.responseCode} · URL: {downloadUrl}");
                    yield break;
                }

                byte[] bytes = req.downloadHandler.data;

                string entityId = string.IsNullOrEmpty(asset.entity_id) ? "unknown" : asset.entity_id;
                string shortSnapshotId = snapshotId.Length >= 8 ? snapshotId.Substring(0, 8) : snapshotId;
                string fileName = $"{entityId}_{shortSnapshotId}.png";
                string relativePath = $"Assets/GDAI_Generated/sprites/{fileName}";
                string absolutePath = Path.Combine(Application.dataPath, "GDAI_Generated", "sprites", fileName);

                string dirPath = Path.GetDirectoryName(absolutePath);
                if (!Directory.Exists(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                }

                try
                {
                    File.WriteAllBytes(absolutePath, bytes);
                    Debug.Log($"[GDAI] Saved {bytes.Length} bytes to {relativePath}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[GDAI] WriteAllBytes failed: {ex.Message}");
                    yield break;
                }

#if UNITY_EDITOR
                AssetDatabase.ImportAsset(relativePath);
                TextureImporter importer = AssetImporter.GetAtPath(relativePath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.filterMode = FilterMode.Point;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                    Debug.Log($"[GDAI] Sprite importer configured for {relativePath}");
                }
#endif
            }
        }

        /// <summary>
        /// alpha.6 · Write code asset (C# script) to Assets/ directory.
        /// Consumes asset.content directly (no URL download).
        /// Path handling: respects asset.path as-is, prepends "Assets/" if missing.
        /// Example: asset.path = "Scripts/PlayerMotor.cs" → Assets/Scripts/PlayerMotor.cs
        /// </summary>
        private void WriteCodeAsset(SnapshotAsset asset, string snapshotId)
        {
            // Guard: empty content
            if (string.IsNullOrEmpty(asset.content))
            {
                Debug.LogWarning($"[GDAI] Code asset has empty content (path={asset.path}, snapshot={snapshotId}) · skipping");
                return;
            }

            // Guard: missing path
            if (string.IsNullOrEmpty(asset.path))
            {
                Debug.LogWarning($"[GDAI] Code asset has empty path (snapshot={snapshotId}) · skipping");
                return;
            }

            try
            {
                // Path handling: normalize to Assets-relative
                string relativePath = asset.path.StartsWith("Assets/")
                    ? asset.path
                    : $"Assets/{asset.path}";

                // Absolute filesystem path: <ProjectRoot>/Assets/...
                string absolutePath = Path.Combine(Application.dataPath, "..", relativePath);
                absolutePath = Path.GetFullPath(absolutePath);

                // Ensure directory exists
                string dirPath = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrEmpty(dirPath) && !Directory.Exists(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                    Debug.Log($"[GDAI] Created directory: {dirPath}");
                }

                // Write (UTF-8 no BOM by default)
                File.WriteAllText(absolutePath, asset.content);
                Debug.Log($"[GDAI] Wrote code asset: {relativePath} ({asset.content.Length} chars)");

                // Trigger Unity import
#if UNITY_EDITOR
                AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
                Debug.Log($"[GDAI] Imported: {relativePath}");
#endif
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GDAI] WriteCodeAsset FAILED (path={asset.path}, snapshot={snapshotId}): {e.Message}");
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Reset Consumed Snapshot Tracking")]
        private void ResetConsumedTracking()
        {
            UnityEditor.EditorPrefs.DeleteKey(GetEditorPrefsKey());
            _lastSnapshotId = null;
            _processedSnapshotIds?.Clear();
            Debug.Log($"[GDAI] Consumed snapshot tracking reset for project: {projectId}");
        }
#endif
    }

    [Serializable]
    public class HotReloadSnapshotWrapper { public HotReloadSnapshot[] items; }

    /// <summary>Top-level snapshot row. diff/assets are nested objects.</summary>
    [Serializable]
    public class HotReloadSnapshot
    {
        public string id;
        public string session_id;
        public string project_id;
        public string user_id;
        public string status;
        public string target_engine;
        public string preview_url;
        public HotReloadDiff diff;
        public SnapshotAsset[] assets;
        public string created_at;
    }

    /// <summary>diff object: { added[], modified[], deleted[] }.</summary>
    [Serializable]
    public class HotReloadDiff
    {
        public HotReloadDiffFile[] added;
        public HotReloadDiffFile[] modified;
        public HotReloadDiffFile[] deleted;
    }

    [Serializable]
    public class HotReloadDiffFile
    {
        public string path;
        public string content;
    }

    /// <summary>
    /// Asset entry.
    ///   path, content, language, module_id, asset_type.
    /// Field names are aligned with bridge snapshot payload.
    /// </summary>
    [Serializable]
    public class SnapshotAsset
    {
        public string path;
        public string content;
        public string language;
        public string module_id;
        public string asset_type;
        public string type;
        public string entity_id;
        public string sprite_generation_id;
    }
}
