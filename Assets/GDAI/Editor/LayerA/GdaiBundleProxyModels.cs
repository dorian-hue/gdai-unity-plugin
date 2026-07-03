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

        // ---- DOWNSTREAM-BUILD-1 · binary asset payloads (additive; absent on old
        //      backends -> stays an empty list; unknown on old plugins -> ignored). ----
        public List<GdaiBundleProxyAsset> assets = new List<GdaiBundleProxyAsset>();
        public List<GdaiBundleProxyAssetSkip> assets_skipped = new List<GdaiBundleProxyAssetSkip>();
    }

    /// <summary>
    /// DOWNSTREAM-BUILD-1 · One binary asset payload, server-resolved to base64 by
    /// unity-bundle-proxy (payload_ref is resolved backend-side; the plugin NEVER
    /// talks to Supabase Storage and NEVER receives signed URLs or service keys).
    /// sha256 is over the RAW DECODED BYTES (not the base64 string).
    /// </summary>
    [Serializable]
    public class GdaiBundleProxyAsset
    {
        public string asset_id;
        public string asset_type;      // "sprite" | "image" | "audio" | ...
        public string role;
        public string mime_type;       // "image/png" | ...
        public string file_name;
        public string unity_path;      // must live under Assets/GDAI_Generated/
        public string payload_mode;    // "base64" (proxy always inlines; ref-only entries are skipped server-side)
        public string payload_base64;
        public long byte_size;
        public string sha256;          // hex, over decoded bytes
        public GdaiBundleProxyAssetSource source;
    }

    [Serializable]
    public class GdaiBundleProxyAssetSource
    {
        public string project_id;
        public string entity_id;
        public string asset_row_id;
        public string world_entity_name;
    }

    [Serializable]
    public class GdaiBundleProxyAssetSkip
    {
        public string asset_id;
        public string reason;
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
