using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GDAI.Bridge.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace GDAI.Bridge
{
    /// <summary>
    /// Utility runner for pulling core artifacts from Bridge API.
    /// Configure Supabase URL, anon key, JWT, and project ID in Inspector.
    /// </summary>
    public class GDAITestRunner : MonoBehaviour
    {
        private const string DefaultSupabaseUrl = "https://YOUR-PROJECT.supabase.co";
        private const string DefaultAnonKey = "PASTE_ANON_KEY_HERE";
        private const string DefaultProjectId = "PASTE_PROJECT_ID_HERE";

        public enum ArtifactType
        {
            ArchitectureGraph,
            WorldEntities,
            CodeIntent,
            All  // Fetches all 3 sequentially
        }

        [Header("Supabase Connection")]
        [SerializeField] 
        private string supabaseUrl = DefaultSupabaseUrl;

        [SerializeField]
        [TextArea(3, 5)]
        [Tooltip("Supabase anon key")]
        private string anonKey = DefaultAnonKey;

        [SerializeField]
        [TextArea(3, 5)]
        [Tooltip("JWT token - grab from browser F12 > Network > any supabase request > Authorization header")]
        private string jwtToken = "PASTE_JWT_HERE";

        [Header("Test Target")]
        [SerializeField]
        [Tooltip("Target project ID")]
        private string projectId = DefaultProjectId;

        [SerializeField]
        [Tooltip("Which artifact type to fetch. 'All' fetches architecture_graph + world_entities + code_intent sequentially.")]
        private ArtifactType artifactType = ArtifactType.All;

        private GDAIBridgeClient _client;

        private struct SyncConfig
        {
            public string SupabaseUrl;
            public string AnonKey;
            public string JwtToken;
            public string ProjectId;
        }

        void Start()
        {
            if (string.IsNullOrWhiteSpace(jwtToken) || jwtToken == "PASTE_JWT_HERE")
            {
                Debug.LogError("[GDAI] JWT token not set. Grab it from browser F12 and paste into Inspector.");
                return;
            }

            _client = new GDAIBridgeClient(supabaseUrl, anonKey, jwtToken);

            switch (artifactType)
            {
                case ArtifactType.ArchitectureGraph:
                    StartCoroutine(RunArchOnly());
                    break;
                case ArtifactType.WorldEntities:
                    StartCoroutine(RunEntitiesOnly());
                    break;
                case ArtifactType.CodeIntent:
                    StartCoroutine(RunCodeOnly());
                    break;
                case ArtifactType.All:
                    StartCoroutine(RunAll());
                    break;
            }
        }

        // ============================================================
        // Individual runners
        // ============================================================

        private IEnumerator RunArchOnly()
        {
            yield return _client.FetchArchitectureGraph(projectId, OnArchSuccess, OnError);
        }

        private IEnumerator RunEntitiesOnly()
        {
            yield return _client.FetchWorldEntities(projectId, OnEntitiesSuccess, OnError);
        }

        private IEnumerator RunCodeOnly()
        {
            yield return _client.FetchCodeIntent(projectId, OnCodeSuccess, OnError);
        }

        private IEnumerator RunAll()
        {
            Debug.Log("[GDAI] === Starting ALL mode (3 artifacts + UI metadata) ===");

            EditorUtility.DisplayProgressBar("GDAI Pull", "Pulling Architecture...", 0.15f);
            yield return _client.FetchArchitectureGraph(projectId, OnArchSuccess, OnError);

            EditorUtility.DisplayProgressBar("GDAI Pull", "Pulling Code Intent...", 0.35f);
            yield return _client.FetchCodeIntent(projectId, OnCodeSuccess, OnError);

            EditorUtility.DisplayProgressBar("GDAI Pull", "Pulling Entities...", 0.55f);
            yield return _client.FetchWorldEntities(projectId, OnEntitiesSuccess, OnError);

            // Assets sync is not implemented in this lightweight runner; keep sequence visibility.
            EditorUtility.DisplayProgressBar("GDAI Pull", "Pulling Assets (placeholder)...", 0.75f);

            EditorUtility.DisplayProgressBar("GDAI Pull", "Importing UI Metadata...", 0.9f);
            yield return UIMetadataImporter.ImportFromBridge(
                supabaseUrl,
                anonKey,
                jwtToken,
                projectId,
                (screenCount, atlasCount) =>
                {
                    Debug.Log($"[GDAI] Pull UI metadata done: {screenCount} screens, {atlasCount} atlases");
                },
                OnError);

            EditorUtility.ClearProgressBar();
            Debug.Log("[GDAI] === ALL mode complete ===");
        }

        // ============================================================
        // Success callbacks
        // ============================================================

        private void OnArchSuccess(ArchitectureGraphResponse resp)
        {
            Debug.Log($"[GDAI] === Architecture Graph ===");
            Debug.Log($"[GDAI] Version: {resp.version}, project_id: {resp.project_id}");
            Debug.Log($"[GDAI] Modules: {resp.modules?.Length ?? 0}");
            
            if (resp.modules != null)
            {
                foreach (var m in resp.modules)
                {
                    Debug.Log($"[GDAI]   - {m.id} ({m.type}) \"{m.name}\" | deps={m.dependencies?.Length ?? 0} | ns=\"{m.@namespace}\"");
                }
            }

            Debug.Log($"[GDAI] Edges: {resp.edges?.Length ?? 0}");
            if (resp.edges != null)
            {
                foreach (var e in resp.edges)
                {
                    Debug.Log($"[GDAI]   - {e.from} -> {e.to} ({e.type}): {e.label}");
                }
            }
            Debug.Log($"[GDAI] === Architecture Graph DONE ===");
        }

        private void OnEntitiesSuccess(WorldEntitiesResponse resp)
        {
            Debug.Log($"[GDAI] === World Entities ===");
            Debug.Log($"[GDAI] Version: {resp.version}");
            Debug.Log($"[GDAI] Entities: {resp.entities?.Length ?? 0}");

            if (resp.entities != null)
            {
                int totalRelations = 0;
                foreach (var e in resp.entities)
                {
                    int relCount = e.relations?.Length ?? 0;
                    int assetCount = e.assets?.Length ?? 0;
                    totalRelations += relCount;

                    string stats = "no stats";
                    if (e.attributes?.stats != null)
                    {
                        stats = $"hp={e.attributes.stats.max_health}, dmg={e.attributes.stats.damage}, speed={e.attributes.stats.move_speed}";
                    }

                    Debug.Log($"[GDAI]   - [{e.type}] {e.name} | {stats} | rel={relCount} assets={assetCount}");

                    if (e.relations != null)
                    {
                        foreach (var r in e.relations)
                        {
                            Debug.Log($"[GDAI]       • {r.direction}: {r.relation_type} ({r.description})");
                        }
                    }
                }
                Debug.Log($"[GDAI] Total relations (bidirectional count): {totalRelations}");
            }

            Debug.Log($"[GDAI] === World Entities DONE ===");
        }

        private void OnCodeSuccess(CodeIntentResponse resp)
        {
            Debug.Log($"[GDAI] === Code Intent ===");
            Debug.Log($"[GDAI] Version: {resp.version}");
            Debug.Log($"[GDAI] Modules: {resp.modules?.Length ?? 0}");

            if (resp.modules != null)
            {
                foreach (var m in resp.modules)
                {
                    int codeLength = m.code?.Length ?? 0;
                    string codePreview = codeLength > 0 ? m.code.Substring(0, System.Math.Min(120, codeLength)).Replace("\n", " ").Replace("\r", "") : "(empty)";
                    
                    Debug.Log($"[GDAI]   - [{m.language}] {m.id} \"{m.name}\" | ns={m.ns} | file={m.filename} | code={codeLength} chars | regen={m.needs_regeneration}");
                    Debug.Log($"[GDAI]       preview: {codePreview}...");
                }

                Debug.LogWarning($"[GDAI] NOTE: Known GDAI handler bug — generic types (e.g. GetComponent<Rigidbody2D>, List<Transform>) are stripped in handler response. Code field may not compile as-is. See CodeIntentResponse.cs class doc.");
            }

            Debug.Log($"[GDAI] === Code Intent DONE ===");
        }

        // ============================================================
        // Error callback
        // ============================================================

        private void OnError(string err)
        {
            Debug.LogError($"[GDAI] ERROR: {err}");
        }

        [MenuItem("GDAI/Sync All + Build Prefabs")]
        public static async void RunAllWithPrefabs()
        {
            var config = ResolveSyncConfig();
            if (string.IsNullOrWhiteSpace(config.JwtToken) || config.JwtToken == "PASTE_JWT_HERE")
            {
                Debug.LogError("[GDAI] JWT token not configured. Add a GDAITestRunner in scene and fill Jwt Token first.");
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("GDAI", "Syncing artifacts...", 0.2f);
                await PullAllAsync(config);

                EditorUtility.DisplayProgressBar("GDAI", "Building prefabs...", 0.65f);
                BuildAllPrefabs();

                EditorUtility.DisplayProgressBar("GDAI", "Fetching C# templates...", 0.85f);
                await FetchAllTemplates(config);

                AssetDatabase.Refresh();
                Debug.Log("[GDAI] Sync + Build Prefabs complete.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[GDAI] Sync All + Build Prefabs failed: {e.Message}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static async Task PullAllAsync(SyncConfig config)
        {
            var client = new GDAIBridgeClient(config.SupabaseUrl, config.AnonKey, config.JwtToken);

            string lastError = null;
            await RunEditorEnumeratorAsync(client.FetchArchitectureGraph(
                config.ProjectId,
                _ => { },
                err => lastError = err));
            if (!string.IsNullOrWhiteSpace(lastError))
            {
                Debug.LogWarning($"[GDAI] Architecture pull warning: {lastError}");
            }

            lastError = null;
            await RunEditorEnumeratorAsync(client.FetchCodeIntent(
                config.ProjectId,
                _ => { },
                err => lastError = err));
            if (!string.IsNullOrWhiteSpace(lastError))
            {
                Debug.LogWarning($"[GDAI] CodeIntent pull warning: {lastError}");
            }

            lastError = null;
            await RunEditorEnumeratorAsync(client.FetchWorldEntities(
                config.ProjectId,
                _ => { },
                err => lastError = err));
            if (!string.IsNullOrWhiteSpace(lastError))
            {
                Debug.LogWarning($"[GDAI] WorldEntities pull warning: {lastError}");
            }

            lastError = null;
            await RunEditorEnumeratorAsync(UIMetadataImporter.ImportFromBridge(
                config.SupabaseUrl,
                config.AnonKey,
                config.JwtToken,
                config.ProjectId,
                (screenCount, atlasCount) => Debug.Log($"[GDAI] Pull UI metadata done: {screenCount} screens, {atlasCount} atlases"),
                err => lastError = err));
            if (!string.IsNullOrWhiteSpace(lastError))
            {
                Debug.LogWarning($"[GDAI] UI metadata pull warning: {lastError}");
            }
        }

        private static void BuildAllPrefabs()
        {
            string uiDir = ResolveUiJsonDirectory();
            if (string.IsNullOrWhiteSpace(uiDir) || !Directory.Exists(uiDir))
            {
                Debug.LogWarning("[GDAI] UI JSON directory not found. Skip prefab build.");
                return;
            }

            const string prefabDir = "Assets/GDAI/UI/Prefabs";
            if (!Directory.Exists(prefabDir))
            {
                Directory.CreateDirectory(prefabDir);
            }

            var jsonFiles = Directory.GetFiles(uiDir, "*.json");
            int built = 0;
            int upToDate = 0;
            foreach (var jsonPath in jsonFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(jsonPath);
                string prefabPath = $"{prefabDir}/{fileName}_Screen.prefab";

                if (File.Exists(prefabPath) &&
                    File.GetLastWriteTimeUtc(jsonPath) <= File.GetLastWriteTimeUtc(prefabPath))
                {
                    upToDate++;
                    Debug.Log($"[GDAI] Prefab up to date: {prefabPath}");
                    continue;
                }

                try
                {
                    var root = GDAIScreenPrefabBuilder.BuildFromJson(jsonPath);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    DestroyImmediate(root);
                    built++;
                    Debug.Log($"[GDAI] Built prefab: {prefabPath}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[GDAI] Failed to build prefab from {jsonPath}: {e.Message}");
                }
            }

            Debug.Log($"[GDAI] Prefabs: {built} built, {upToDate} up to date.");
        }

        private static async Task FetchAllTemplates(SyncConfig config)
        {
            string uiDir = ResolveUiJsonDirectory();
            if (string.IsNullOrWhiteSpace(uiDir) || !Directory.Exists(uiDir))
            {
                Debug.LogWarning("[GDAI] UI JSON directory not found. Skip template fetch.");
                return;
            }

            const string scriptDir = "Assets/GDAI/UI/Scripts";
            if (!Directory.Exists(scriptDir))
            {
                Directory.CreateDirectory(scriptDir);
            }

            var client = new GDAIBridgeClient(config.SupabaseUrl, config.AnonKey, config.JwtToken);
            var jsonFiles = Directory.GetFiles(uiDir, "*.json");
            int fetched = 0;
            int skipped = 0;

            foreach (var jsonPath in jsonFiles)
            {
                try
                {
                    var root = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(jsonPath));
                    if (root == null)
                    {
                        skipped++;
                        continue;
                    }

                    string slotId = root["slot_id"]?.ToString();
                    if (string.IsNullOrWhiteSpace(slotId))
                    {
                        skipped++;
                        continue;
                    }

                    string screenType = root["screen_type"]?.ToString() ?? "unknown";
                    string slotPrefix = slotId.Length >= 8 ? slotId.Substring(0, 8) : slotId;
                    string className = $"{ToPascalCase(screenType)}Setup_{slotPrefix}";
                    string csPath = $"{scriptDir}/{className}.cs";

                    if (File.Exists(csPath))
                    {
                        skipped++;
                        Debug.Log($"[GDAI] Template exists (not overwriting): {csPath}");
                        continue;
                    }

                    string template = await client.FetchText($"/v1/projects/{config.ProjectId}/ui-export/template/{slotId}");
                    if (string.IsNullOrWhiteSpace(template))
                    {
                        skipped++;
                        continue;
                    }

                    File.WriteAllText(csPath, template);
                    fetched++;
                    Debug.Log($"[GDAI] Fetched template: {csPath}");
                }
                catch (Exception e)
                {
                    skipped++;
                    Debug.LogError($"[GDAI] Failed to fetch template for {jsonPath}: {e.Message}");
                }
            }

            Debug.Log($"[GDAI] Templates: {fetched} fetched, {skipped} skipped.");
        }

        private static SyncConfig ResolveSyncConfig()
        {
            var configuredRunner = FindObjectOfType<GDAITestRunner>();
            if (configuredRunner != null)
            {
                return new SyncConfig
                {
                    SupabaseUrl = configuredRunner.supabaseUrl,
                    AnonKey = configuredRunner.anonKey,
                    JwtToken = configuredRunner.jwtToken,
                    ProjectId = configuredRunner.projectId
                };
            }

            return new SyncConfig
            {
                SupabaseUrl = DefaultSupabaseUrl,
                AnonKey = DefaultAnonKey,
                JwtToken = string.Empty,
                ProjectId = DefaultProjectId
            };
        }

        private static string ResolveUiJsonDirectory()
        {
            const string preferred = "Assets/GDAI/UI";
            if (Directory.Exists(preferred))
            {
                return preferred;
            }

            const string fallback = "Assets/GDAI/Resources/GDAI/UI";
            if (Directory.Exists(fallback))
            {
                return fallback;
            }

            return null;
        }

        private static string ToPascalCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var parts = value.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var output = string.Empty;
            foreach (var part in parts)
            {
                if (part.Length == 1)
                {
                    output += char.ToUpperInvariant(part[0]);
                }
                else
                {
                    output += char.ToUpperInvariant(part[0]) + part.Substring(1).ToLowerInvariant();
                }
            }

            return output;
        }

        private static Task RunEditorEnumeratorAsync(IEnumerator enumerator)
        {
            var tcs = new TaskCompletionSource<bool>();
            EditorEnumeratorRunner.Start(
                enumerator,
                () => tcs.TrySetResult(true),
                ex => tcs.TrySetException(ex));
            return tcs.Task;
        }

        private sealed class EditorEnumeratorRunner
        {
            private static readonly List<EditorEnumeratorRunner> Active = new List<EditorEnumeratorRunner>();
            private readonly Stack<IEnumerator> _stack = new Stack<IEnumerator>();
            private readonly Action _onComplete;
            private readonly Action<Exception> _onError;
            private AsyncOperation _pendingAsyncOperation;

            private EditorEnumeratorRunner(IEnumerator root, Action onComplete, Action<Exception> onError)
            {
                _stack.Push(root);
                _onComplete = onComplete;
                _onError = onError;
            }

            public static void Start(IEnumerator root, Action onComplete, Action<Exception> onError)
            {
                var runner = new EditorEnumeratorRunner(root, onComplete, onError);
                Active.Add(runner);
                EditorApplication.update -= TickAll;
                EditorApplication.update += TickAll;
            }

            private static void TickAll()
            {
                for (int i = Active.Count - 1; i >= 0; i--)
                {
                    if (Active[i].Tick())
                    {
                        Active.RemoveAt(i);
                    }
                }

                if (Active.Count == 0)
                {
                    EditorApplication.update -= TickAll;
                }
            }

            private bool Tick()
            {
                try
                {
                    if (_pendingAsyncOperation != null)
                    {
                        if (!_pendingAsyncOperation.isDone)
                        {
                            return false;
                        }

                        _pendingAsyncOperation = null;
                    }

                    while (_stack.Count > 0)
                    {
                        var current = _stack.Peek();
                        bool moved = current.MoveNext();
                        if (!moved)
                        {
                            _stack.Pop();
                            continue;
                        }

                        object yielded = current.Current;
                        if (yielded == null)
                        {
                            return false;
                        }

                        if (yielded is IEnumerator nested)
                        {
                            _stack.Push(nested);
                            continue;
                        }

                        if (yielded is AsyncOperation op)
                        {
                            _pendingAsyncOperation = op;
                            return false;
                        }

                        return false;
                    }

                    _onComplete?.Invoke();
                    return true;
                }
                catch (Exception ex)
                {
                    _onError?.Invoke(ex);
                    return true;
                }
            }
        }
    }
}
