using System;
using System.Collections.Generic;

// =====================================================================================
// GDAI Unity Plugin · Production Bundle Proxy · DTO (normalized, plugin-facing).
// This is what `unity-bundle-proxy` returns — NOT a raw hot_reload_snapshots row.
// The plugin must not require user_id/session_id/diff/assets/context_snapshot raw fields.
// Parsed with Newtonsoft.Json (Editor asmdef references Unity.Newtonsoft.Json).
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerA
{
    [Serializable]
    public class GdaiBundleProxyDto
    {
        public string snapshot_id;
        public string project_id;
        public string target_engine;   // "unity"
        public string bundle_type;     // "unity_core_bundle"
        public string created_at;
        public GdaiBundleProxyMetadata metadata;
        public List<GdaiBundleProxyFile> files = new List<GdaiBundleProxyFile>();
        public object wiring_manifest;
        public string readme;
    }

    [Serializable]
    public class GdaiBundleProxyFile
    {
        public string path;
        public string content;
        public string type;    // "code" | "manifest" | "doc" | ...
        public string sha256;
    }

    [Serializable]
    public class GdaiBundleProxyMetadata
    {
        public bool compileReadySharedTypes;
        public GdaiBundleProxyIntegrationController integrationController;
        public object dashRuntimeSync;
        public bool runtimeReadyDashSync;
        public object unity6ApiCleanup;
        public List<string> sharedTypes = new List<string>();
    }

    [Serializable]
    public class GdaiBundleProxyIntegrationController
    {
        public string status;
        public List<object> unresolved = new List<object>();
    }
}
