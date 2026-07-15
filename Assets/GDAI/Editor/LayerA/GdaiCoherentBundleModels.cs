using System;
using System.Collections.Generic;

// =====================================================================================
// GDAI Unity Plugin · Layer A (Coherent Bundle Import) · data models.
// Parsed with Newtonsoft.Json (Editor asmdef references Unity.Newtonsoft.Json), which
// handles the nested JSONB columns (assets[], context_snapshot) that JsonUtility cannot.
// Unknown JSON fields are ignored. Layer A is import + visibility + guardrails only.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerA
{
    [Serializable]
    public class GdaiHotReloadSnapshot
    {
        public string id;
        public string project_id;
        public string session_id;
        public string status;
        public string target_engine;
        public List<GdaiSnapshotAsset> assets = new List<GdaiSnapshotAsset>();
        public GdaiContextSnapshot context_snapshot;
        public string created_at;
    }

    [Serializable]
    public class GdaiSnapshotAsset
    {
        public string path;
        public string content;
        public string type;
        public string language;
    }

    [Serializable]
    public class GdaiContextSnapshot
    {
        public string source;
        public string bundleType;
        // T4 0J C2: nullable so a raw context_snapshot that never declared these (older rows) deserializes
        // instead of throwing null→bool. Consume with `== true` (null/false/absent = not ready).
        public bool? compileReadySharedTypes;
        public bool? runtimeReadyDashSync;
        public GdaiIntegrationController integrationController;
        public GdaiDashRuntimeSync dashRuntimeSync;
        public List<string> missing_modules = new List<string>();
        public List<string> warnings = new List<string>();
    }

    [Serializable]
    public class GdaiIntegrationController
    {
        public string status; // "generated" | "partial" | ...
    }

    [Serializable]
    public class GdaiDashRuntimeSync
    {
        public string status; // "patched" | ...
    }
}
