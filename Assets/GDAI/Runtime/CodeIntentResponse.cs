using System;

namespace GDAI.Bridge
{
    /// <summary>
    /// C# classes matching bridge-api code_intent JSON response.
    /// </summary>
    [Serializable]
    public class CodeIntentResponse
    {
        public int version;
        public CodeIntentModule[] modules;
    }

    [Serializable]
    public class CodeIntentModule
    {
        public string id;                 // "player_motor" | "hazard_ai" | "level_director"
        public string name;               // "Player Motor"
        public string language;           // "csharp"
        public string code;               // Full C# source (⚠️ generic types may be stripped, see class doc above)
        
        // "namespace" is a C# keyword; payload is preprocessed to map it to "ns".
        public string ns;                 // Maps from "namespace" field after pre-processing
        
        public string filename;           // "PlayerMotor.cs"
        public bool needs_regeneration;
        public string generated_at;       // ISO 8601 timestamp
    }
}
