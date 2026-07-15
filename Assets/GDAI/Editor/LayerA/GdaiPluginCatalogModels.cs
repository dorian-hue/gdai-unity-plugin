using System;
using System.Collections.Generic;

// =====================================================================================
// GDAI Unity Plugin · MVP-C · project & bundle catalog DTOs
// (unity-plugin-projects, unity-plugin-bundles). Metadata only — no file contents.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerA
{
    [Serializable]
    public class GdaiCatalogProjectsResponse
    {
        public List<GdaiCatalogProject> projects = new List<GdaiCatalogProject>();
    }

    [Serializable]
    public class GdaiCatalogProject
    {
        public string project_id;
        public string name;
        public string role;
        public string updated_at;
    }

    [Serializable]
    public class GdaiCatalogBundlesResponse
    {
        public List<GdaiCatalogBundle> bundles = new List<GdaiCatalogBundle>();
    }

    [Serializable]
    public class GdaiCatalogBundle
    {
        public string snapshot_id;
        public string created_at;
        public string bundle_type;
        public string target_engine;
        public int files_count;
        // T4 0J C2: unity-plugin-bundles emits these as `?? null` (historical-undeclared), so the wire model
        // must be nullable or Newtonsoft throws null→bool. Consume with `== true`.
        public bool? compileReadySharedTypes;
        public string integrationControllerStatus;
        public bool? runtimeReadyDashSync;
    }
}
