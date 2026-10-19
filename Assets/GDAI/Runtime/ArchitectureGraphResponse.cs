using System;

namespace GDAI.Bridge
{
    /// <summary>
    /// C# classes matching bridge-api architecture_graph JSON response.
    /// Verified against DB ground truth + handler source code.
    /// 
    /// Response shape:
    /// {
    ///   "version": 1,
    ///   "project_id": "238d1ce4-...",
    ///   "generated_at": "2026-04-10T...",
    ///   "modules": [ { "id", "name", "type", "description", "dependencies", "namespace" } ],
    ///   "edges": [ { "from", "to", "type", "label" } ]
    /// }
    /// </summary>
    [Serializable]
    public class ArchitectureGraphResponse
    {
        public int version;
        public string project_id;
        public string generated_at;
        public ArchModule[] modules;
        public ArchEdge[] edges;
    }

    [Serializable]
    public class ArchModule
    {
        public string id;
        public string name;
        public string type;          // "core" | "feature" | "infrastructure" (from game_modules)
        public string description;   // plain text from project_memory.content
        public string[] dependencies;
        public string @namespace;    // C# keyword workaround: @ prefix lets keyword be a field name
    }

    [Serializable]
    public class ArchEdge
    {
        // Note: "from" and "to" are NOT C# reserved words, so they work fine.
        public string from;          // source node id (e.g. "player_motor")
        public string to;            // target node id (e.g. "level_director")
        public string type;          // "dependency"
        public string label;         // "Player Motor → Level Director"
    }
}
