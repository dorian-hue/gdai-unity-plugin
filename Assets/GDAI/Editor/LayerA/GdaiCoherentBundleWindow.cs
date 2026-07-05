using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// =====================================================================================
// GDAI Unity Plugin · Layer A · EditorWindow ("GDAI Unity Connector").
// Default UI = product flow only: Connection → Project → Bundle → optional Scene Wiring.
// Troubleshooting (collapsed) = manual JWT fallback, diagnostics, raw validation details.
// Developer Mode (compiled only with GDAI_INTERNAL_DEBUG) = direct Supabase transport.
// A persisted "last bundle" summary survives domain reload so the window is never blank
// after import. Layer A still does NOT auto-bind, create scenes/prefabs, or fix packages.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerA
{
    public class GdaiCoherentBundleWindow : EditorWindow
    {
        // Connection config (Advanced)
        private const string PrefUrl = "GDAI_LayerA_SupabaseUrl";
        private const string PrefKey = "GDAI_LayerA_AnonKey";
        private const string PrefProject = "GDAI_LayerA_ProjectId";
        private const string PrefSnapshot = "GDAI_LayerA_SnapshotId";
        private const string PrefAdvanced = "GDAI_LayerA_AdvancedMode";

        // Persisted "last bundle" summary (survives domain reload)
        private const string PrefLastSnapshot = "GDAI_LayerA_LastSnapshotId";
        private const string PrefLastProject = "GDAI_LayerA_LastProjectId";
        private const string PrefLastAssetCount = "GDAI_LayerA_LastAssetCount";
        private const string PrefLastSource = "GDAI_LayerA_LastSource";
        private const string PrefLastBundleType = "GDAI_LayerA_LastBundleType";
        private const string PrefLastCompileReady = "GDAI_LayerA_LastCompileReadySharedTypes";
        private const string PrefLastIcStatus = "GDAI_LayerA_LastIntegrationControllerStatus";
        private const string PrefLastDashStatus = "GDAI_LayerA_LastDashRuntimeSyncStatus";
        private const string PrefLastRuntimeReady = "GDAI_LayerA_LastRuntimeReadyDashSync";
        private const string PrefLastBackup = "GDAI_LayerA_LastBackupPath";

        // Production Bundle Proxy config (non-secret persisted; token persisted only if user opts in)
        private const string PrefProdUrl = "GDAI_Prod_FunctionUrl";
        private const string PrefProdProject = "GDAI_Prod_ProjectId";
        private const string PrefProdRemember = "GDAI_Prod_RememberToken";
        private const string PrefProdToken = "GDAI_Prod_UserToken";

        private const string DefaultProxyUrl = "https://nceajhcsvrweplfhqodt.supabase.co/functions/v1/unity-bundle-proxy";

        // MVP-C · device pairing + scoped token + selectors
        private const string DefaultFunctionsBase = "https://nceajhcsvrweplfhqodt.supabase.co/functions/v1";
        private const string PluginVersion = "0.1.0-alpha.8.0";
        private const string PrefFunctionsBase = "GDAI_MvpC_FunctionsBase";
        private const string PrefPluginToken = "GDAI_MvpC_PluginToken";   // scoped gdai_plugin_v1.* token
        private const string PrefSelProject = "GDAI_MvpC_SelectedProjectId";
        private const string PrefSelSnapshot = "GDAI_MvpC_SelectedSnapshotId";

        private string _functionsBase = DefaultFunctionsBase;
        private string _pluginToken = "";      // scoped token; never logged, prefix-only in UI
        private string _selProjectId = "";
        private string _selProjectName = "";
        private string _selSnapshot = "";

        // device-pairing transient state (never persisted)
        private string _deviceCode, _pollSecret, _userCode, _verificationUrl;
        private int _pollInterval = 3;
        private double _pollExpiresAt;
        private bool _connecting;
        private bool _cancelPoll;

        private List<GdaiCatalogProject> _projects = new List<GdaiCatalogProject>();
        private int _projIndex;
        private List<GdaiCatalogBundle> _bundles = new List<GdaiCatalogBundle>();
        private int _bundleIndex;
        private bool _showManualFallback;   // nested inside Troubleshooting · collapsed by default
        private bool _showSceneWiring;      // "Optional: Scene Wiring" foldout · collapsed by default
        private bool _showTroubleshooting;  // "Troubleshooting" foldout · collapsed by default
        private double _codeCopiedUntil;    // transient · "Copied" feedback window for the device code
        private string _lastErrorDetail = ""; // transient · raw exception/validation text, shown only under Troubleshooting

        private string _prodUrl = DefaultProxyUrl;
        private string _prodProjectId = "";
        private string _prodSnapshotId = "";
        private bool _prodUseLatest = true;
        private string _prodJwt = "";       // in-memory by default; never logged
        private bool _prodRememberToken;

        private const string UrlPlaceholder = "https://YOUR-PROJECT.supabase.co";

        private string _supabaseUrl = UrlPlaceholder;
        private string _anonKey = "";
        private string _projectId = "";
        private string _snapshotId = "";
        private bool _advanced;

        // Live (non-persisted) working state
        private GdaiHotReloadSnapshot _fetched;
        private ValidationResult _validation;
        private BundleStatus _status = BundleStatus.Fetched;
        private string _statusLine = "Idle.";
        private bool _busy;

        // Persisted summary mirror
        private string _sumSnapshot, _sumProject, _sumSource, _sumBundleType, _sumIcStatus, _sumDashStatus, _lastBackupPath;
        private int _sumAssetCount;
        private bool _sumCompileReady, _sumRuntimeReady;

        private Vector2 _scroll, _docScroll;
        private string _docTitle, _docText;

        [MenuItem("GDAI/Layer A · Import Coherent Bundle")]
        public static void Open()
        {
            var w = GetWindow<GdaiCoherentBundleWindow>(false, "GDAI Unity Connector", true);
            w.minSize = new Vector2(520, 560);
            w.Show();
        }

        // OnEnable runs on open AND after every domain reload — so state is restored, not blank.
        private void OnEnable() => LoadPrefs();

        private void LoadPrefs()
        {
            _supabaseUrl = EditorPrefs.GetString(PrefUrl, _supabaseUrl);
            _anonKey = EditorPrefs.GetString(PrefKey, _anonKey);
            _projectId = EditorPrefs.GetString(PrefProject, _projectId);
            _snapshotId = EditorPrefs.GetString(PrefSnapshot, _snapshotId);
            _advanced = EditorPrefs.GetBool(PrefAdvanced, false);

            _sumSnapshot = EditorPrefs.GetString(PrefLastSnapshot, "");
            _sumProject = EditorPrefs.GetString(PrefLastProject, "");
            _sumAssetCount = EditorPrefs.GetInt(PrefLastAssetCount, 0);
            _sumSource = EditorPrefs.GetString(PrefLastSource, "");
            _sumBundleType = EditorPrefs.GetString(PrefLastBundleType, "");
            _sumCompileReady = EditorPrefs.GetBool(PrefLastCompileReady, false);
            _sumIcStatus = EditorPrefs.GetString(PrefLastIcStatus, "");
            _sumDashStatus = EditorPrefs.GetString(PrefLastDashStatus, "");
            _sumRuntimeReady = EditorPrefs.GetBool(PrefLastRuntimeReady, false);
            _lastBackupPath = EditorPrefs.GetString(PrefLastBackup, "");

            _prodUrl = EditorPrefs.GetString(PrefProdUrl, DefaultProxyUrl);
            _prodProjectId = EditorPrefs.GetString(PrefProdProject, "");
            _prodRememberToken = EditorPrefs.GetBool(PrefProdRemember, false);
            _prodJwt = _prodRememberToken ? EditorPrefs.GetString(PrefProdToken, "") : ""; // token only if opted in

            _functionsBase = EditorPrefs.GetString(PrefFunctionsBase, DefaultFunctionsBase);
            _pluginToken = EditorPrefs.GetString(PrefPluginToken, "");   // scoped token persists (revocable)
            _selProjectId = EditorPrefs.GetString(PrefSelProject, "");
            _selSnapshot = EditorPrefs.GetString(PrefSelSnapshot, "");
        }

        private bool IsConnected => !string.IsNullOrEmpty(_pluginToken);
        private static string TokenPrefix(string t) => string.IsNullOrEmpty(t) ? "(none)" : (t.Length > 16 ? t.Substring(0, 16) + "…" : t);

        private void SaveConnectionPrefs()
        {
            EditorPrefs.SetString(PrefUrl, _supabaseUrl);
            EditorPrefs.SetString(PrefKey, _anonKey);
            EditorPrefs.SetString(PrefProject, _projectId);
            EditorPrefs.SetString(PrefSnapshot, _snapshotId);
            EditorPrefs.SetBool(PrefAdvanced, _advanced);
        }

        private void SaveSummary(GdaiHotReloadSnapshot snap)
        {
            var ctx = snap.context_snapshot;
            _sumSnapshot = snap.id ?? "";
            _sumProject = snap.project_id ?? "";
            _sumAssetCount = snap.assets != null ? snap.assets.Count : 0;
            _sumSource = ctx != null ? (ctx.source ?? "") : "";
            _sumBundleType = ctx != null ? (ctx.bundleType ?? "") : "";
            _sumCompileReady = ctx != null && ctx.compileReadySharedTypes;
            _sumIcStatus = ctx != null && ctx.integrationController != null ? (ctx.integrationController.status ?? "") : "";
            _sumDashStatus = ctx != null && ctx.dashRuntimeSync != null ? (ctx.dashRuntimeSync.status ?? "") : "";
            _sumRuntimeReady = ctx != null && ctx.runtimeReadyDashSync;

            EditorPrefs.SetString(PrefLastSnapshot, _sumSnapshot);
            EditorPrefs.SetString(PrefLastProject, _sumProject);
            EditorPrefs.SetInt(PrefLastAssetCount, _sumAssetCount);
            EditorPrefs.SetString(PrefLastSource, _sumSource);
            EditorPrefs.SetString(PrefLastBundleType, _sumBundleType);
            EditorPrefs.SetBool(PrefLastCompileReady, _sumCompileReady);
            EditorPrefs.SetString(PrefLastIcStatus, _sumIcStatus);
            EditorPrefs.SetString(PrefLastDashStatus, _sumDashStatus);
            EditorPrefs.SetBool(PrefLastRuntimeReady, _sumRuntimeReady);
        }

        private bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_supabaseUrl) && _supabaseUrl != UrlPlaceholder &&
            !string.IsNullOrWhiteSpace(_anonKey) && !string.IsNullOrWhiteSpace(_projectId);

        private bool HasLastSnapshot => !string.IsNullOrEmpty(_sumSnapshot);

        // ----------------------------- GUI -----------------------------

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawConnectionSection();      // 1 · Connection (device pairing + scoped token)
            DrawProjectSection();         // 2 · Project (authorized project selector)
            DrawBundleSection();          // 3 · Bundle (Import Latest Bundle)
            DrawSceneWiringSection();     // 4 · Optional: Scene Wiring (collapsed)

            DrawTroubleshootingSection(); // support/debug only (collapsed)

#if GDAI_INTERNAL_DEBUG
            DrawDeveloperSection();       // INTERNAL ONLY · direct Supabase transport
#endif

            DrawStatus();
            DrawDocViewer();

            EditorGUILayout.EndScrollView();
        }

        // ----------------------------- MVP-C sections -----------------------------

        private void DrawConnectionSection()
        {
            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Status", IsConnected ? "Connected" : (_connecting ? "Waiting for browser approval" : "Not connected"));

            using (new EditorGUI.DisabledScope(_busy))
            {
                if (!IsConnected && !_connecting)
                {
                    if (GUILayout.Button("Connect to GameDevs.AI", GUILayout.Height(26))) ConnectStart();
                }
                else if (_connecting)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Code", string.IsNullOrEmpty(_userCode) ? "…" : _userCode);
                        if (!string.IsNullOrEmpty(_userCode))
                        {
                            bool justCopied = EditorApplication.timeSinceStartup < _codeCopiedUntil;
                            if (GUILayout.Button(justCopied ? "Copied" : "Copy", GUILayout.Width(64)))
                            {
                                EditorGUIUtility.systemCopyBuffer = _userCode;
                                _codeCopiedUntil = EditorApplication.timeSinceStartup + 1.5;
                                Repaint();
                            }
                        }
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Open Browser")) { if (!string.IsNullOrEmpty(_verificationUrl)) Application.OpenURL(_verificationUrl); }
                        if (GUILayout.Button("Cancel")) _cancelPoll = true;
                    }
                }
                else
                {
                    if (GUILayout.Button("Disconnect")) Disconnect();
                }
            }
        }

        private void DrawProjectSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Project", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(_busy || !IsConnected))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (_projects.Count == 0)
                    {
                        EditorGUILayout.LabelField(IsConnected
                            ? "No authorized projects. Reconnect and authorize a project in your browser."
                            : "Connect first.");
                    }
                    else
                    {
                        var names = _projects.ConvertAll(p => string.IsNullOrEmpty(p.name) ? p.project_id : p.name).ToArray();
                        int newIndex = EditorGUILayout.Popup(_projIndex, names);
                        if (newIndex != _projIndex) { _projIndex = newIndex; OnProjectSelected(); }
                    }
                    if (GUILayout.Button("Refresh Projects", GUILayout.Width(130))) RefreshProjects();
                }
            }
        }

        private void DrawBundleSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bundle", EditorStyles.boldLabel);

            // alpha.8 default path: one primary CTA only. The bundle selector/list UI is
            // intentionally not exposed here (Refresh Bundles list path is a known follow-up).
            using (new EditorGUI.DisabledScope(_busy || !IsConnected || string.IsNullOrEmpty(_selProjectId)))
            {
                if (GUILayout.Button("Import Latest Bundle", GUILayout.Height(28)))
                    FetchProd(latest: true, import: true);
            }

            if (HasLastSnapshot)
            {
                EditorGUILayout.LabelField("Snapshot", Short(_sumSnapshot) + "…");
                EditorGUILayout.LabelField("Assets", _sumAssetCount.ToString());
                if (!string.IsNullOrEmpty(_sumSource)) EditorGUILayout.LabelField("Source", _sumSource);
                if (!string.IsNullOrEmpty(_lastBackupPath)) EditorGUILayout.LabelField("Backup", _lastBackupPath);
            }
        }

        private static string Short(string id) => string.IsNullOrEmpty(id) ? "(none)" : (id.Length > 8 ? id.Substring(0, 8) : id);

        private void DrawProductionConnector()
        {
            EditorGUILayout.HelpBox("Support fallback: calls the GDAI backend bundle proxy with a pasted user access token. " +
                                    "It never contacts the database directly and never uses a service_role or anon key here.", MessageType.None);

            using (new EditorGUI.DisabledScope(_busy))
            {
                EditorGUI.BeginChangeCheck();
                _prodUrl = EditorGUILayout.TextField("Backend Function URL", _prodUrl);
                _prodProjectId = EditorGUILayout.TextField("Project ID", _prodProjectId);
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetString(PrefProdUrl, _prodUrl);
                    EditorPrefs.SetString(PrefProdProject, _prodProjectId);
                }

                _prodUseLatest = EditorGUILayout.Toggle("Fetch latest coherent bundle", _prodUseLatest);
                using (new EditorGUI.DisabledScope(_prodUseLatest))
                    _prodSnapshotId = EditorGUILayout.TextField("Snapshot ID", _prodSnapshotId);

                _prodJwt = EditorGUILayout.PasswordField("User Access Token / JWT", _prodJwt);

                EditorGUI.BeginChangeCheck();
                _prodRememberToken = EditorGUILayout.ToggleLeft(
                    "Remember token in this Unity Editor (local only — not recommended)", _prodRememberToken);
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetBool(PrefProdRemember, _prodRememberToken);
                    if (_prodRememberToken) EditorPrefs.SetString(PrefProdToken, _prodJwt);
                    else EditorPrefs.DeleteKey(PrefProdToken);
                }

                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Fetch Bundle")) ProdFetch(import: false);
                    if (GUILayout.Button("Fetch and Import", GUILayout.Height(20))) ProdFetch(import: true);
                    if (GUILayout.Button("Clear Token", GUILayout.Width(96))) ProdClearToken();
                }
            }
        }

        private void DrawSimpleHeader()
        {
            EditorGUILayout.LabelField("GDAI Unity Connector", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Connection",
                IsConfigured ? "Internal Debug · Connected" : "Not Connected — open Advanced Debug to configure once");
            EditorGUILayout.LabelField("Project", string.IsNullOrEmpty(_projectId) ? "(not set)" : _projectId);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Last bundle", EditorStyles.boldLabel);
            if (!HasLastSnapshot)
            {
                EditorGUILayout.HelpBox("No bundle fetched yet. Use Import Latest Bundle, or configure once under Advanced Debug.", MessageType.None);
            }
            else
            {
                EditorGUILayout.LabelField("Snapshot", _sumSnapshot);
                EditorGUILayout.LabelField("Assets", _sumAssetCount.ToString());
                EditorGUILayout.LabelField("Type / source", $"{_sumBundleType} / {_sumSource}");
                EditorGUILayout.LabelField("Compile-ready shared types", _sumCompileReady.ToString());
                EditorGUILayout.LabelField("Integration controller", string.IsNullOrEmpty(_sumIcStatus) ? "(none)" : _sumIcStatus);
                EditorGUILayout.LabelField("Dash runtime sync", string.IsNullOrEmpty(_sumDashStatus) ? "(none)" : _sumDashStatus);
            }
        }

        private void DrawPrimaryButtons()
        {
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_busy))
            {
                using (new EditorGUI.DisabledScope(!IsConfigured))
                {
                    if (GUILayout.Button("Import Latest Bundle", GUILayout.Height(28)))
                        ImportLatest();
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!IsConfigured || !HasLastSnapshot))
                    {
                        if (GUILayout.Button("Import Same Snapshot Again")) ImportSameAgain();
                        if (GUILayout.Button("Re-fetch Current Snapshot")) RefetchCurrent();
                    }
                }
            }
            if (!IsConfigured)
                EditorGUILayout.HelpBox("First-time setup: open Advanced Debug and enter the project's Supabase URL, anon/publishable key, and Project ID. " +
                                        "Never paste a service_role key.", MessageType.Info);
        }

        private void DrawSceneWiringSection()
        {
            EditorGUILayout.Space();
            _showSceneWiring = EditorGUILayout.Foldout(_showSceneWiring, "Optional: Scene Wiring", true);
            if (!_showSceneWiring) return;

            using (new EditorGUI.DisabledScope(_busy))
            {
                if (GUILayout.Button("Auto-bind Scene References")) AutoBindScene();
                if (GUILayout.Button("Auto-bind Input Actions")) AutoBindInputActions();
                if (GUILayout.Button("Analyze Playable Scene")) LayerC.GdaiMinimalPlayableSceneBuilder.AnalyzeMenu();
                if (GUILayout.Button("Open Wiring Guide")) ShowDoc("README_WIRING.md");
            }
        }

        private void DrawTroubleshootingSection()
        {
            EditorGUILayout.Space();
            _showTroubleshooting = EditorGUILayout.Foldout(_showTroubleshooting, "Troubleshooting", true);
            if (!_showTroubleshooting) return;

            EditorGUILayout.HelpBox("For support/debugging only. Most users should not change these settings.", MessageType.Warning);
            if (IsConnected) EditorGUILayout.LabelField("Token (prefix)", TokenPrefix(_pluginToken));
            if (!string.IsNullOrEmpty(_selProjectId))
                EditorGUILayout.LabelField("Project (raw)", $"{_selProjectName} ({_selProjectId})");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reveal Generated Folder")) RevealGeneratedFolder();
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_lastBackupPath)))
                {
                    if (GUILayout.Button("Reveal Last Backup")) RevealLastBackup();
                }
            }

            if (!string.IsNullOrEmpty(_lastErrorDetail))
            {
                EditorGUILayout.LabelField("Last error (raw)", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(_lastErrorDetail, EditorStyles.textArea, GUILayout.MinHeight(60));
            }

            EditorGUILayout.Space();
            _showManualFallback = EditorGUILayout.Foldout(_showManualFallback, "Manual Token Fallback · paste a user JWT (support only)", true);
            if (_showManualFallback) DrawProductionConnector();

            DrawValidationDetails(); // raw validation details live here, not in the default UI
        }

#if GDAI_INTERNAL_DEBUG
        private void DrawDeveloperSection()
        {
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            _advanced = EditorGUILayout.Foldout(_advanced, "Developer Mode · INTERNAL ONLY · direct Supabase transport", true);
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetBool(PrefAdvanced, _advanced);
            if (!_advanced) return;

            DrawSimpleHeader();   // anon-key connection status + last bundle
            DrawPrimaryButtons(); // anon-key Import Latest/Same/Refetch
            DrawAdvanced();       // anon-key fields + debug fetch/import + validation
        }
#endif

        private void AutoBindScene()
        {
            var r = LayerB.GdaiSceneReferenceBinder.AutoBind();
            bool ok = r.status == "success";
            SetStatus(ok ? BundleStatus.Validated : BundleStatus.FailedValidation, r.Summary());
            EditorUtility.DisplayDialog("GDAI · Layer B Auto-bind", r.Summary(), "OK");
        }

        private void AutoBindInputActions()
        {
            var r = LayerB.GdaiInputActionReferenceBinder.AutoBindInputActions();
            bool ok = r.status == "success";
            SetStatus(ok ? BundleStatus.Validated : BundleStatus.FailedValidation, r.Summary());
            EditorUtility.DisplayDialog("GDAI · Layer B2 Auto-bind Input Actions", r.Summary(), "OK");
        }

        private void DrawAdvanced()
        {
            EditorGUILayout.HelpBox("Internal debug transport: Channel A · direct hot_reload_snapshots (PostgREST). " +
                                    "The production connector will call a GDAI backend bundle endpoint instead. " +
                                    "Never paste a service_role key — use only the project anon/publishable key.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(_busy))
            {
                EditorGUI.BeginChangeCheck();
                _supabaseUrl = EditorGUILayout.TextField("Supabase URL", _supabaseUrl);
                _anonKey = EditorGUILayout.PasswordField("Anon / Publishable Key", _anonKey);
                _projectId = EditorGUILayout.TextField("Project ID", _projectId);
                _snapshotId = EditorGUILayout.TextField("Snapshot ID (optional)", _snapshotId);
                if (EditorGUI.EndChangeCheck()) SaveConnectionPrefs();

                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Fetch By Snapshot ID")) FetchById();
                    if (GUILayout.Button("Fetch Latest Coherent Bundle")) FetchLatest();
                }

                bool canImport = !_busy && _fetched != null && _validation != null && _validation.Ok;
                using (new EditorGUI.DisabledScope(!canImport))
                {
                    if (GUILayout.Button(canImport ? "Import / Replace Assets" : "Import / Replace Assets (fetch + pass validation first)"))
                        DoImport(_fetched);
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_lastBackupPath)))
                {
                    if (GUILayout.Button("Reveal Last Backup")) RevealLastBackup();
                }
            }

            DrawValidationDetails();
        }

        // Display-only rewrite: keep fixture/project-specific validator wording out of product UI.
        // The validator itself (CoherentBundleValidator) is intentionally untouched.
        private static string FriendlyWarning(string w)
        {
            if (string.IsNullOrEmpty(w)) return w;
            if (w.Contains("expected 'patched' for Project-SLASH")) return "Dash runtime sync status is missing.";
            return w;
        }

        private void DrawValidationDetails()
        {
            if (_fetched == null && _validation == null) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Validation details", EditorStyles.boldLabel);
            if (_fetched != null)
            {
                var ctx = _fetched.context_snapshot;
                EditorGUILayout.LabelField("Fetched snapshot", _fetched.id ?? "(none)");
                if (ctx != null && ctx.missing_modules != null && ctx.missing_modules.Count > 0)
                    EditorGUILayout.LabelField("missing_modules", string.Join(", ", ctx.missing_modules));
            }
            if (_validation != null)
            {
                EditorGUILayout.LabelField(_validation.Ok ? "VALIDATED · safe to import" : "FAILED_VALIDATION · import blocked");
                foreach (var e in _validation.Errors) EditorGUILayout.HelpBox(e, MessageType.Error);
                foreach (var w in _validation.Warnings) EditorGUILayout.HelpBox(FriendlyWarning(w), MessageType.Warning);
            }
        }

        private void DrawStatus()
        {
            EditorGUILayout.Space();
            bool isError = _status == BundleStatus.FailedValidation || _status == BundleStatus.FailedWrite;
            EditorGUILayout.HelpBox(_statusLine, isError ? MessageType.Error : MessageType.Info);
            if (_status == BundleStatus.RefreshTriggered)
                EditorGUILayout.HelpBox("Import complete. If the Console shows compile errors from generated code, " +
                                        "that is a generated bundle issue, not a connector issue.", MessageType.Info);
        }

        private void DrawDocViewer()
        {
            if (string.IsNullOrEmpty(_docText)) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Viewer · {_docTitle}", EditorStyles.boldLabel);
            _docScroll = EditorGUILayout.BeginScrollView(_docScroll, GUILayout.MinHeight(160));
            EditorGUILayout.TextArea(_docText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        // ----------------------------- Actions -----------------------------

        // Simple: fetch latest coherent bundle for the configured project, then import.
        private async void ImportLatest()
        {
            if (!IsConfigured) { SetStatus(BundleStatus.FailedValidation, "Not connected. Configure under Advanced Debug first."); return; }
            await RunFetch(() => CoherentBundleImporter.FetchLatestCoherent(_supabaseUrl, _anonKey, _projectId.Trim()), autoImport: true);
        }

        // Simple: re-fetch the last snapshot by id, then import (no reliance on in-memory state).
        private async void ImportSameAgain()
        {
            if (!HasLastSnapshot) { SetStatus(BundleStatus.FailedValidation, "No previous snapshot recorded."); return; }
            await RunFetch(() => CoherentBundleImporter.FetchById(_supabaseUrl, _anonKey, _sumSnapshot), autoImport: true);
        }

        // Simple: re-fetch the last snapshot by id and validate, without importing.
        private async void RefetchCurrent()
        {
            if (!HasLastSnapshot) { SetStatus(BundleStatus.FailedValidation, "No previous snapshot recorded."); return; }
            await RunFetch(() => CoherentBundleImporter.FetchById(_supabaseUrl, _anonKey, _sumSnapshot), autoImport: false);
        }

        private async void FetchById()
        {
            if (string.IsNullOrWhiteSpace(_snapshotId)) { SetStatus(BundleStatus.FailedValidation, "Snapshot ID is empty."); return; }
            SaveConnectionPrefs();
            await RunFetch(() => CoherentBundleImporter.FetchById(_supabaseUrl, _anonKey, _snapshotId.Trim()), autoImport: false);
        }

        private async void FetchLatest()
        {
            if (string.IsNullOrWhiteSpace(_projectId)) { SetStatus(BundleStatus.FailedValidation, "Project ID is empty."); return; }
            SaveConnectionPrefs();
            await RunFetch(() => CoherentBundleImporter.FetchLatestCoherent(_supabaseUrl, _anonKey, _projectId.Trim()), autoImport: false);
        }

        private async Task RunFetch(Func<Task<GdaiHotReloadSnapshot>> fetch, bool autoImport)
        {
            _busy = true; _fetched = null; _validation = null;
            SetStatus(BundleStatus.Fetched, "Fetching bundle...");
            try
            {
                var snap = await fetch();
                if (snap == null)
                {
                    SetStatus(BundleStatus.FailedValidation, "No matching coherent bundle found for this project.");
                    return;
                }
                _fetched = snap;
                _validation = CoherentBundleValidator.Validate(snap);
                if (_validation.Ok)
                {
                    SaveSummary(snap); // persist so the window survives domain reload
                    _status = BundleStatus.Validated;
                    _statusLine = $"Fetched and validated · snapshot {snap.id} · {snap.assets.Count} assets.";
                }
                else
                {
                    _status = BundleStatus.FailedValidation;
                    _statusLine = $"Fetched {snap.id} · {_validation.Errors.Count} validation error(s) · import blocked.";
                }
            }
            catch (Exception e)
            {
                SetStatus(BundleStatus.FailedValidation, "Fetch failed: " + e.Message);
            }
            finally
            {
                _busy = false;
                Repaint();
            }

            if (autoImport && _validation != null && _validation.Ok && _fetched != null)
                DoImport(_fetched);
        }

        private void DoImport(GdaiHotReloadSnapshot snap)
        {
            if (snap == null) return;

            // Re-validate immediately before writing (defense in depth).
            _validation = CoherentBundleValidator.Validate(snap);
            if (!_validation.Ok)
            {
                SetStatus(BundleStatus.FailedValidation, "Validation failed at import time; nothing written.");
                return;
            }

            string assetList = string.Join("\n", snap.assets.ConvertAll(a => "  " + a.path));
            bool ok = EditorUtility.DisplayDialog(
                "GDAI · Import Coherent Bundle",
                $"Replace Assets/GDAI_Generated with snapshot {snap.id}?\n\n" +
                "The existing folder (if any) will be backed up outside Assets under .gdai/generated_backups, then replaced with:\n\n" +
                assetList,
                "Backup & Replace", "Cancel");
            if (!ok) return;

            _busy = true;
            try
            {
                var st = CoherentBundleImporter.ImportVerbatim(snap, out string msg, out List<string> written, out string backup, out int preservedMetas);
                _status = st;
                _lastBackupPath = backup ?? "";
                EditorPrefs.SetString(PrefLastBackup, _lastBackupPath);
                if (st == BundleStatus.RefreshTriggered)
                {
                    SaveSummary(snap);
                    _statusLine = $"Imported {written.Count} assets. Preserved {preservedMetas} meta GUID(s). Unity is compiling scripts. Backup: {(backup ?? "none")}.";
                    Debug.Log($"[GDAI][LayerA] Imported {written.Count} assets. Preserved {preservedMetas} meta GUID(s). Backup: {backup ?? "none"}");
                }
                else
                {
                    _statusLine = msg;
                    Debug.LogError($"[GDAI][LayerA] {msg}");
                }
            }
            finally
            {
                _busy = false;
                Repaint();
            }
        }

        // Production: fetch a normalized DTO from the backend proxy (user JWT), validate
        // (shape + SHA-256 + path safety + existing coherent-bundle validator), then import
        // through the SAME Layer A core (backup + meta-GUID preservation).
        // Manual JWT fallback (collapsed): reuses the same proxy fetch core with a pasted user JWT.
        private async void ProdFetch(bool import)
        {
            if (string.IsNullOrWhiteSpace(_prodUrl)) { SetStatus(BundleStatus.FailedValidation, "Manual fallback: Backend Function URL is required."); return; }
            if (string.IsNullOrWhiteSpace(_prodProjectId)) { SetStatus(BundleStatus.FailedValidation, "Manual fallback: Project ID is required."); return; }
            if (!_prodUseLatest && string.IsNullOrWhiteSpace(_prodSnapshotId)) { SetStatus(BundleStatus.FailedValidation, "Manual fallback: enter a Snapshot ID or enable 'Fetch latest'."); return; }
            if (string.IsNullOrWhiteSpace(_prodJwt)) { SetStatus(BundleStatus.FailedValidation, "Manual fallback: a user access token is required."); return; }
            if (_prodRememberToken) EditorPrefs.SetString(PrefProdToken, _prodJwt);
            await RunProxyFetch(_prodUrl, _prodProjectId, _prodUseLatest, _prodSnapshotId, _prodJwt, import);
        }

        // Shared proxy fetch + validate + import core. `token` is either a scoped plugin token
        // (production) or a pasted user JWT (manual fallback) — both go out as Authorization: Bearer.
        private async Task RunProxyFetch(string proxyUrl, string projectId, bool latest, string snapshotId, string token, bool import)
        {
            _busy = true; _fetched = null; _validation = null; _lastErrorDetail = "";
            SetStatus(BundleStatus.Fetched, "Fetching bundle via proxy...");
            bool validated = false;
            GdaiHotReloadSnapshot snap = null;
            GdaiBundleProxyDto dto = null; // hoisted: binary asset payloads are imported after DoImport (DOWNSTREAM-BUILD-1)
            try
            {
                dto = latest
                    ? await GdaiBundleProxyClient.FetchLatest(proxyUrl.Trim(), projectId.Trim(), token)
                    : await GdaiBundleProxyClient.FetchBySnapshot(proxyUrl.Trim(), projectId.Trim(), snapshotId.Trim(), token);

                var dtoErrors = GdaiBundleProxyClient.ValidateDto(dto);
                if (dtoErrors.Count > 0)
                {
                    _lastErrorDetail = string.Join("\n", dtoErrors);
                    SetStatus(BundleStatus.FailedValidation, "Bundle rejected by validation. Expand Troubleshooting for details.");
                    return;
                }

                snap = GdaiBundleProxyClient.ToSnapshot(dto);
                _fetched = snap;
                _validation = CoherentBundleValidator.Validate(snap);
                if (!_validation.Ok) { _status = BundleStatus.FailedValidation; _statusLine = $"Fetched {snap.id} · {_validation.Errors.Count} validation error(s) · import blocked."; return; }
                SaveSummary(snap);
                validated = true;
                _status = BundleStatus.Validated;
                _statusLine = $"Fetched and validated via proxy · snapshot {snap.id} · {snap.assets.Count} files.";
            }
            catch (GdaiBundleProxyException e) { SetStatus(BundleStatus.FailedValidation, e.Message); }
            catch (Exception e)
            {
                _lastErrorDetail = e.ToString();
                SetStatus(BundleStatus.FailedValidation, "Bundle fetch failed. Expand Troubleshooting for technical details.");
            }
            finally { _busy = false; Repaint(); }

            if (import && validated && snap != null) DoImport(snap);

            // ---- DOWNSTREAM-BUILD-1 · binary asset payloads (proxy channel only, additive) ----
            // Runs only AFTER the text/code import fully succeeded (RefreshTriggered), so a bad
            // asset payload can never break the existing code import path. Channel A (direct
            // PostgREST fallback) intentionally does NOT carry payloads — proxy resolves them.
            if (import && validated && snap != null &&
                _status == BundleStatus.RefreshTriggered &&
                dto != null && dto.assets != null && dto.assets.Count > 0)
            {
                var assetSummary = AssetPayloadImporter.ImportAll(dto.assets, dto.snapshot_id);
                _statusLine += $" Binary assets: {assetSummary.Imported} imported, {assetSummary.SkippedWithReason.Count} skipped.";
                if (dto.assets_skipped != null && dto.assets_skipped.Count > 0)
                    Debug.Log($"[GDAI][LayerA][Assets] Backend skipped {dto.assets_skipped.Count} asset(s) before delivery (see proxy assets_skipped).");
                Repaint();
            }

            // ---- DOWNSTREAM-BUILD-3A · semantic role map auto-write (additive; never fails import) ----
            if (import && validated && snap != null && _status == BundleStatus.RefreshTriggered && dto != null)
            {
                bool wrote = GDAI.Bridge.Editor.LayerB.GdaiSemanticRoleMap.WriteFromBundle(dto.semantic_role_map, out string roleMapMsg);
                Debug.Log("[GDAI][Assets][RoleMap] " + roleMapMsg);
                if (wrote) _statusLine += " Role map updated.";
            }

            // ---- UNITY-SCENE-BG-BIND-1 · scene background placement (additive; never fails import) ----
            // Layer B owns scene mutation (Layer A import never touches the scene). Runs only after a
            // successful asset import; places/updates the deterministic GDAI_SceneBackground object
            // behind gameplay (sortingOrder < 0, no collider). Idempotent: re-import updates in place,
            // never duplicates. Preserves Player/Enemy semantic sprite binding (never touched here).
            if (import && validated && snap != null &&
                _status == BundleStatus.RefreshTriggered &&
                dto != null && dto.assets != null && dto.assets.Count > 0)
            {
                try
                {
                    bool placed = GDAI.Bridge.Editor.LayerB.GdaiSceneBackgroundBinder
                        .PlaceFromBundle(dto.assets, dto.snapshot_id, out string bgMsg);
                    Debug.Log("[GDAI][Assets][SceneBackground] " + bgMsg);
                    if (placed) _statusLine += " Scene background placed.";
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[GDAI][Assets][SceneBackground] placement skipped (import unaffected): " + e.Message);
                }
                Repaint();
            }
        }

        // ---- MVP-C actions: device pairing + catalog + production fetch ----

        private async void ConnectStart()
        {
            _connecting = true; _cancelPoll = false;
            SetStatus(BundleStatus.Fetched, "Starting connection…");
            try
            {
                var start = await GdaiPluginConnectionClient.Start(_functionsBase.Trim(), "Unity Editor", PluginVersion);
                _deviceCode = start.device_code;
                _pollSecret = start.poll_secret;
                _userCode = start.user_code;
                _verificationUrl = string.IsNullOrEmpty(start.verification_url) ? "https://plugin.gamedevs.ai/unity/connect" : start.verification_url;
                _pollInterval = Mathf.Max(2, start.interval);
                _pollExpiresAt = EditorApplication.timeSinceStartup + (start.expires_in > 0 ? start.expires_in : 300);
                Application.OpenURL(_verificationUrl);
                SetStatus(BundleStatus.Fetched, $"Awaiting approval. Enter code {_userCode} in the browser.");
                Repaint();

                while (!_cancelPoll && EditorApplication.timeSinceStartup < _pollExpiresAt)
                {
                    await EditorDelay(_pollInterval);
                    if (_cancelPoll) break;

                    GdaiConnectionPollResponse poll;
                    try { poll = await GdaiPluginConnectionClient.Poll(_functionsBase.Trim(), _deviceCode, _pollSecret); }
                    catch (GdaiBundleProxyException e) { SetStatus(BundleStatus.FailedValidation, e.Message); break; }

                    if (poll.status == "approved" && !string.IsNullOrEmpty(poll.connection_token))
                    {
                        _pluginToken = poll.connection_token;
                        EditorPrefs.SetString(PrefPluginToken, _pluginToken);
                        SetStatus(BundleStatus.Validated, "Connected to GDAI.");
                        await LoadProjectsInternal();
                        break;
                    }
                    if (poll.status == "denied") { SetStatus(BundleStatus.FailedValidation, "Connection denied in browser."); break; }
                    if (poll.status == "expired") { SetStatus(BundleStatus.FailedValidation, "Connection code expired. Try again."); break; }
                    if (poll.status == "consumed") { SetStatus(BundleStatus.FailedValidation, "Connection already used. Try again."); break; }
                    // pending → keep polling
                }
                if (!IsConnected && !_cancelPoll && EditorApplication.timeSinceStartup >= _pollExpiresAt)
                    SetStatus(BundleStatus.FailedValidation, "Connection timed out. Try again.");
            }
            catch (GdaiBundleProxyException e) { SetStatus(BundleStatus.FailedValidation, e.Message); }
            catch (Exception e) { SetStatus(BundleStatus.FailedValidation, "Connection error: " + e.Message); }
            finally
            {
                _connecting = false;
                _deviceCode = null; _pollSecret = null; // drop secrets once the flow ends
                Repaint();
            }
        }

        private static async Task EditorDelay(double seconds)
        {
            double end = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < end) await Task.Yield();
        }

        private void Disconnect()
        {
            string token = _pluginToken;
            _pluginToken = ""; EditorPrefs.DeleteKey(PrefPluginToken);
            _projects.Clear(); _bundles.Clear(); _projIndex = 0; _bundleIndex = 0;
            _selProjectId = ""; _selProjectName = ""; EditorPrefs.DeleteKey(PrefSelProject);
            _selSnapshot = ""; EditorPrefs.DeleteKey(PrefSelSnapshot);
            SetStatus(_status, "Disconnected.");
            if (!string.IsNullOrEmpty(token)) _ = GdaiPluginConnectionClient.Revoke(_functionsBase.Trim(), token); // best-effort
        }

        private void OnProjectSelected()
        {
            if (_projIndex < 0 || _projIndex >= _projects.Count) return;
            _selProjectId = _projects[_projIndex].project_id;
            _selProjectName = _projects[_projIndex].name;
            EditorPrefs.SetString(PrefSelProject, _selProjectId);
            _bundles.Clear(); _bundleIndex = 0;
            RefreshBundles();
        }

        private async void RefreshProjects() { await LoadProjectsInternal(); }

        private async Task LoadProjectsInternal()
        {
            if (!IsConnected) { SetStatus(BundleStatus.FailedValidation, "Not connected."); return; }
            _busy = true; SetStatus(_status, "Loading projects…");
            try
            {
                _projects = await GdaiPluginCatalogClient.ListProjects(_functionsBase.Trim(), _pluginToken);
                _projIndex = 0;
                if (!string.IsNullOrEmpty(_selProjectId))
                {
                    int idx = _projects.FindIndex(p => p.project_id == _selProjectId);
                    if (idx >= 0) _projIndex = idx;
                }
                if (_projects.Count > 0)
                {
                    _selProjectId = _projects[_projIndex].project_id;
                    _selProjectName = _projects[_projIndex].name;
                    EditorPrefs.SetString(PrefSelProject, _selProjectId);
                }
                SetStatus(_status, $"Loaded {_projects.Count} project(s).");
                if (_projects.Count > 0) await LoadBundlesInternal();
            }
            catch (GdaiBundleProxyException e) { SetStatus(BundleStatus.FailedValidation, e.Message); }
            catch (Exception e)
            {
                // Raw API/JSON exceptions stay out of the default UI (Troubleshooting only).
                _lastErrorDetail = e.ToString();
                SetStatus(BundleStatus.FailedValidation, "Projects could not be loaded. Expand Troubleshooting for details.");
            }
            finally { _busy = false; Repaint(); }
        }

        private async void RefreshBundles() { await LoadBundlesInternal(); }

        private async Task LoadBundlesInternal()
        {
            if (!IsConnected) { SetStatus(BundleStatus.FailedValidation, "Not connected."); return; }
            if (string.IsNullOrEmpty(_selProjectId)) { SetStatus(BundleStatus.FailedValidation, "Select a project first."); return; }
            _busy = true; SetStatus(_status, "Loading bundles…");
            try
            {
                _bundles = await GdaiPluginCatalogClient.ListBundles(_functionsBase.Trim(), _pluginToken, _selProjectId, 20);
                _bundleIndex = 0;
                SetStatus(_status, $"Loaded {_bundles.Count} coherent bundle(s).");
            }
            catch (GdaiBundleProxyException e) { SetStatus(BundleStatus.FailedValidation, e.Message); }
            catch (Exception e)
            {
                // Raw parse/convert exceptions stay out of the default UI (Troubleshooting only).
                _lastErrorDetail = e.ToString();
                SetStatus(_status, "Bundle metadata could not be fully parsed. Import Latest is still available. Expand Troubleshooting for details.");
            }
            finally { _busy = false; Repaint(); }
        }

        private async void FetchProd(bool latest, bool import)
        {
            if (!IsConnected) { SetStatus(BundleStatus.FailedValidation, "Not connected. Connect to GDAI first."); return; }
            if (string.IsNullOrEmpty(_selProjectId)) { SetStatus(BundleStatus.FailedValidation, "Select a project first."); return; }
            string snapshotId = "";
            if (!latest)
            {
                if (_bundleIndex < 0 || _bundleIndex >= _bundles.Count) { SetStatus(BundleStatus.FailedValidation, "Select a bundle first."); return; }
                snapshotId = _bundles[_bundleIndex].snapshot_id;
                _selSnapshot = snapshotId; EditorPrefs.SetString(PrefSelSnapshot, _selSnapshot);
            }
            string proxyUrl = _functionsBase.TrimEnd('/') + "/unity-bundle-proxy";
            await RunProxyFetch(proxyUrl, _selProjectId, latest, snapshotId, _pluginToken, import);
        }

        private void ProdClearToken()
        {
            _prodJwt = "";
            EditorPrefs.DeleteKey(PrefProdToken);
            SetStatus(_status, "Token cleared.");
        }

        private void ShowDoc(string fileName)
        {
            string abs = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                CoherentBundleImporter.GeneratedFolder, fileName);
            if (!File.Exists(abs))
            {
                if (_fetched != null && _fetched.assets != null)
                {
                    var a = _fetched.assets.Find(x => x.path != null && x.path.EndsWith("/" + fileName));
                    if (a != null) { _docTitle = fileName + " (from fetched bundle, not yet imported)"; _docText = a.content ?? ""; Repaint(); return; }
                }
                SetStatus(_status, $"{fileName} not found (import the bundle first).");
                return;
            }
            _docTitle = fileName;
            _docText = File.ReadAllText(abs);
            Repaint();
        }

        private void RevealGeneratedFolder()
        {
            string abs = Path.Combine(Directory.GetParent(Application.dataPath).FullName, CoherentBundleImporter.GeneratedFolder);
            if (Directory.Exists(abs)) EditorUtility.RevealInFinder(abs);
            else SetStatus(_status, "Generated folder does not exist yet.");
        }

        private void RevealLastBackup()
        {
            if (string.IsNullOrEmpty(_lastBackupPath)) { SetStatus(_status, "No backup recorded yet."); return; }
            string abs = Path.Combine(Directory.GetParent(Application.dataPath).FullName, _lastBackupPath);
            if (Directory.Exists(abs)) EditorUtility.RevealInFinder(abs);
            else SetStatus(_status, $"Backup not found at {_lastBackupPath}.");
        }

        private void SetStatus(BundleStatus s, string line)
        {
            _status = s;
            _statusLine = line;
            Repaint();
        }
    }
}
