using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// =====================================================================================
// GDAI Unity Plugin · Layer A · EditorWindow ("GDAI Unity Connector").
// Simple mode = product-credible connector (no Supabase/snapshot jargon).
// Advanced Debug = the internal validation transport (direct hot_reload_snapshots).
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
        }

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

            DrawSimpleHeader();
            DrawPrimaryButtons();
            DrawDocButtons();

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            _advanced = EditorGUILayout.Foldout(_advanced, "Advanced Debug · internal validation transport", true);
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetBool(PrefAdvanced, _advanced);
            if (_advanced) DrawAdvanced();

            DrawStatus();
            DrawDocViewer();

            EditorGUILayout.EndScrollView();
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

        private void DrawDocButtons()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Wiring Guide")) ShowDoc("README_WIRING.md");
                if (GUILayout.Button("Reveal Generated Folder")) RevealGeneratedFolder();
            }
            if (GUILayout.Button("Auto-bind Current Scene (binds existing objects only)"))
                AutoBindScene();
            if (GUILayout.Button("Auto-bind Input Actions (existing assets only)"))
                AutoBindInputActions();
        }

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
                foreach (var w in _validation.Warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);
            }
        }

        private void DrawStatus()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            bool isError = _status == BundleStatus.FailedValidation || _status == BundleStatus.FailedWrite;
            EditorGUILayout.HelpBox(_statusLine, isError ? MessageType.Error : MessageType.Info);
            if (_status == BundleStatus.RefreshTriggered)
                EditorGUILayout.HelpBox("Import complete. Check the Console for compile warnings/errors. " +
                                        "Obsolete-API warnings (e.g. Rigidbody2D.velocity) are codegen debt, not import failures. " +
                                        "Layer A does not auto-wire scene references.", MessageType.Info);
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
