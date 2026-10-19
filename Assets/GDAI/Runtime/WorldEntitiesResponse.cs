using System;

namespace GDAI.Bridge
{
    /// <summary>
    /// C# classes matching bridge-api world_entities JSON response.
    /// </summary>
    [Serializable]
    public class WorldEntitiesResponse
    {
        public int version;
        public WorldEntity[] entities;
    }

    [Serializable]
    public class WorldEntity
    {
        public string id;
        public string name;
        public string type;              // "character" | "enemy" | "hazard" | "item" | "objective" | "savepoint"
        public string description;
        public WorldEntityAttributes attributes;
        public WorldEntityRelation[] relations;
        public WorldEntityAsset[] assets;
    }

    /// <summary>
    /// Entity attributes container. Uses a flexible structure:
    /// - stats (nested object with numeric values)
    /// - tags (string array)
    /// - behavior, effect, pickup_type (optional string fields)
    /// - abilities, spawn_rules (optional string arrays)
    /// - starting_position (int array)
    /// 
    /// Not all fields are populated for every entity type. JsonUtility tolerates
    /// missing fields (they remain default/null).
    /// </summary>
    [Serializable]
    public class WorldEntityAttributes
    {
        public WorldEntityStats stats;
        public string[] tags;
        public string behavior;
        public string effect;
        public string pickup_type;
        public string[] abilities;
        public string[] spawn_rules;
        public int[] starting_position;
    }

    /// <summary>
    /// All numeric stats entities may have. Missing stats remain 0.
    /// JsonUtility reads by field name, missing fields default to 0.
    /// </summary>
    [Serializable]
    public class WorldEntityStats
    {
        public int max_health;
        public float move_speed;
        public float jump_force;
        public int coyote_time_ms;
        public float patrol_speed;
        public float detection_radius;
        public int damage;
        public string trigger_type;
        public int heal_amount;
        public int respawn_seconds;
        public float trigger_radius;
        public float activation_radius;
    }

    [Serializable]
    public class WorldEntityRelation
    {
        public string related_entity_id;
        public string relation_type;      // "combat" | "damaged_by" | "collects" | "reaches" | "activates" | "avoids" | "restores_health" | "follows"
        public string direction;          // "incoming" | "outgoing"
        public string description;
    }

    [Serializable]
    public class WorldEntityAsset
    {
        public string id;
        public string type;
        public string url;
        public string mime;
    }
}
