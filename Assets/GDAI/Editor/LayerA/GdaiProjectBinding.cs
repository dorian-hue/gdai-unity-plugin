using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GDAI.Bridge.Editor.LayerA
{
    // GDAI project binding · AUTO-0N P1.
    // The exported Unity project carries ProjectSettings/GDAIProjectBinding.json.
    // This reader is the ONLY source of bound-project identity: never EditorPrefs,
    // never Assets/GDAI_Generated, never environment variables. Every validation
    // failure is fail-closed (no fallback identity, no silent default).
    public enum GdaiBindingState
    {
        Unpaired,
        PairedBindingUnavailable,
        PairedBindingUnauthorized,
        PairedBoundReady,
        Syncing,
        SyncComplete,
        SyncFailed,
    }

    [Serializable]
    public sealed class GdaiProjectBindingData
    {
        public string schema_version;
        public string project_id;
        public string project_display_name;
        public string target_engine;
        public string unity_version;
        public string render_pipeline;
        public string plugin_version;
        public string template_revision;
        public string template_tree;
        public string template_allowlist_sha256;
        public string source_project_revision;
        public string gdd_revision;
        public string code_bundle_snapshot_id;
        public string asset_revision;
        public string scene_revision;
        public string[] generated_owned_paths;
        public string created_by;
    }

    public static class GdaiProjectBinding
    {
        public const string RelativePath = "ProjectSettings/GDAIProjectBinding.json";
        public const string ExpectedSchema = "gdai.unity_export.v1";
        public const string ExpectedEngine = "unity";
        public const string ExpectedPipeline = "built_in";
        public static readonly string[] ApprovedGeneratedRoots = { "Assets/GDAI_Generated" };

        public static string DefaultProjectRoot() =>
            Directory.GetParent(Application.dataPath).FullName;

        // Fail-closed load: returns true ONLY for a fully valid binding.
        // absentIsError distinguishes "no binding file" (legacy unbound project)
        // from a malformed/mismatched binding (always an error).
        public static bool TryLoad(string projectRoot, string expectedPluginVersion,
            string expectedUnityVersion, out GdaiProjectBindingData data, out string error,
            out bool fileExists)
        {
            data = null;
            error = null;
            string path = Path.Combine(projectRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));
            fileExists = File.Exists(path);
            if (!fileExists) { error = "BINDING_ABSENT"; return false; }

            GdaiProjectBindingData parsed;
            try
            {
                parsed = JsonUtility.FromJson<GdaiProjectBindingData>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                error = "BINDING_INVALID_JSON: " + e.Message;
                return false;
            }
            if (parsed == null) { error = "BINDING_INVALID_JSON: empty"; return false; }

            error = Validate(parsed, expectedPluginVersion, expectedUnityVersion);
            if (error != null) return false;
            data = parsed;
            return true;
        }

        // Returns null when valid, otherwise the fail-closed reason code.
        public static string Validate(GdaiProjectBindingData b,
            string expectedPluginVersion, string expectedUnityVersion)
        {
            if (b == null) return "BINDING_NULL";
            if (b.schema_version != ExpectedSchema)
                return "BINDING_WRONG_SCHEMA: " + b.schema_version;
            if (!Guid.TryParse(b.project_id, out var guid) || guid == Guid.Empty)
                return "BINDING_BAD_PROJECT_UUID";
            if (b.target_engine != ExpectedEngine)
                return "BINDING_WRONG_ENGINE: " + b.target_engine;
            if (!string.IsNullOrEmpty(expectedUnityVersion) && b.unity_version != expectedUnityVersion)
                return "BINDING_WRONG_UNITY_VERSION: " + b.unity_version;
            if (b.render_pipeline != ExpectedPipeline)
                return "BINDING_WRONG_PIPELINE: " + b.render_pipeline;
            if (NormalizeVersion(b.plugin_version) != NormalizeVersion(expectedPluginVersion))
                return "BINDING_PLUGIN_VERSION_MISMATCH: " + b.plugin_version;

            if (b.generated_owned_paths == null || b.generated_owned_paths.Length == 0)
                return "BINDING_GENERATED_ROOTS_MISSING";
            foreach (var p in b.generated_owned_paths)
            {
                if (string.IsNullOrEmpty(p)) return "BINDING_GENERATED_ROOT_EMPTY";
                string norm = p.Replace('\\', '/').TrimEnd('/');
                if (norm.Contains("..")) return "BINDING_GENERATED_ROOT_TRAVERSAL: " + p;
                if (norm.StartsWith("/") || (norm.Length > 1 && norm[1] == ':'))
                    return "BINDING_GENERATED_ROOT_ABSOLUTE: " + p;
                // the binding file itself must never be generated-owned
                if (RelativePath.StartsWith(norm + "/") || norm == "ProjectSettings" ||
                    norm == RelativePath)
                    return "BINDING_SELF_OWNED: " + p;
                bool approved = false;
                foreach (var root in ApprovedGeneratedRoots)
                    if (norm == root) { approved = true; break; }
                if (!approved) return "BINDING_GENERATED_ROOT_NOT_APPROVED: " + p;
            }
            return null;
        }

        // Pure catalog resolver used by the window: given the bound project id
        // and the approved catalog ids, either binds to the exact project or
        // fails closed. NEVER falls back to the first catalog project.
        public static bool TryResolveCatalogIndex(string boundProjectId,
            IReadOnlyList<string> catalogProjectIds, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(boundProjectId) || catalogProjectIds == null) return false;
            for (int i = 0; i < catalogProjectIds.Count; i++)
                if (string.Equals(catalogProjectIds[i], boundProjectId, StringComparison.OrdinalIgnoreCase))
                { index = i; return true; }
            return false; // absent → PAIRED_BINDING_UNAUTHORIZED, no fallback
        }

        private static string NormalizeVersion(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            return v.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? v.Substring(1) : v;
        }
    }
}
