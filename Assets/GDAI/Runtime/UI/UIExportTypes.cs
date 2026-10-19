using System;
using System.Collections.Generic;

namespace GDAI.Runtime.UI
{
    [Serializable]
    public class UIExportPackage
    {
        public string version;
        public string project_id;
        public string exported_at;
        public List<UIExportScreen> screens;
        public StyleSummary style_summary;
        public FlowGraph flow_graph;
        public string content_hash;
    }

    [Serializable]
    public class UIExportScreen
    {
        public string screen_type;
        public string slot_id;
        public string candidate_id;
        public List<UIExportBinding> bindings;
        public UIExportLayout layout;
        public AtlasRef atlas;
        public List<TransitionRef> transitions;
    }

    [Serializable]
    public class UIExportBinding
    {
        public string node_path;
        public string node_name;
        public string node_type;
        public string group;
        public string source_or_dispatch;
        public string binding_type;
        public string original_name;
        public string args_json;
        public string extras_json;
    }

    [Serializable]
    public class UIExportNode
    {
        public string path;
        public string name;
        public string type;
        public string anchor;
        public Vec2 position;
        public Vec2 size;
        public string parent_path;
        public string layer;
        public float font_size;
        public string label;
        public string content;
    }

    [Serializable]
    public class UIExportLayout
    {
        public Vec2 reference_resolution;
        public List<UIExportNode> nodes;
    }

    [Serializable]
    public class Vec2
    {
        public float x;
        public float y;
        public float width { get => x; set => x = value; }
        public float height { get => y; set => y = value; }
    }

    [Serializable]
    public class AtlasRef
    {
        public string atlas_id;
        public string atlas_url;
        public string slice_metadata_json;
    }

    [Serializable]
    public class TransitionRef
    {
        public string target_screen_type;
        public string trigger_dispatch;
        public string label;
        public string confidence;
    }

    [Serializable]
    public class StyleSummary
    {
        public string genre;
        public string mood;
        public List<string> palette;
    }

    [Serializable]
    public class FlowGraph
    {
        public List<string> nodes;
        public List<FlowEdge> edges;
    }

    [Serializable]
    public class FlowEdge
    {
        public string from;
        public string to;
        public string dispatch;
        public string label;
    }
}
