using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

// =====================================================================================
// GDAI Unity Plugin · DOWNSTREAM-BUILD-3 · Semantic entity role map (contract + IO).
//
// THE missing contract identified by BUILD-2/BUILD-3 RECON: nothing in GDD structured
// fields, world_entities.entity_type, relations, or bridge-api taxonomy grounds
// "which entity is the Player / the Enemy". This file defines where that truth LIVES
// and which sources are allowed to auto-bind. It does NOT invent any mapping.
//
// Storage: Assets/GDAI_RoleMap/gdai_semantic_role_map.json
//   · OUTSIDE Assets/GDAI_Generated on purpose — the role map is project-level
//     confirmed truth (survives coherent-bundle clean-replace), not bundle output.
//   · Absent by default. Until a grounded map exists, semantic binding stays dormant
//     and diagnostics report exactly what is missing (truthful Path C behavior).
//
// Grounding rule (hard):
//   source ∈ { gdd, world_entity, bridge_api, manual_confirmed } → may auto-bind
//   source = inferred_low_confidence (or unknown)                → must NOT auto-bind
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    [Serializable]
    public class GdaiSemanticRoleEntry
    {
        public string role;        // "player" | "enemy" | "boss" | "npc" | "party_member" | ...
        public string entity_id;   // world_entities.id — the stable asset-binding anchor
        public string asset_id;    // optional: pin one specific entity_assets row
        public string source;      // gdd | world_entity | bridge_api | manual_confirmed | inferred_low_confidence
        public string confidence;  // optional free text ("high"/"low"/…) — informational
        public string evidence;    // where this mapping came from (human-readable)
        public string confirmed_by;
    }

    [Serializable]
    public class GdaiSemanticRoleMapData
    {
        public int version = 1;
        public string project_id;  // informational; binding never branches on it
        public List<GdaiSemanticRoleEntry> roles = new List<GdaiSemanticRoleEntry>();
    }

    public static class GdaiSemanticRoleMap
    {
        public const string RoleMapDir = "Assets/GDAI_RoleMap";
        public const string RoleMapPath = RoleMapDir + "/gdai_semantic_role_map.json";

        /// <summary>Sources allowed to auto-bind (task hard rule). Everything else is diagnostic-only.</summary>
        public static readonly HashSet<string> GroundedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gdd", "world_entity", "bridge_api", "manual_confirmed",
        };

        public static bool Exists()
        {
            return File.Exists(AbsolutePath());
        }

        /// <summary>Loads the role map; null when absent/unreadable (callers report, never throw).</summary>
        public static GdaiSemanticRoleMapData Load(out string loadError)
        {
            loadError = null;
            try
            {
                string abs = AbsolutePath();
                if (!File.Exists(abs)) { loadError = "role_map_missing:" + RoleMapPath; return null; }
                var data = JsonConvert.DeserializeObject<GdaiSemanticRoleMapData>(File.ReadAllText(abs));
                if (data == null) loadError = "role_map_unreadable";
                return data;
            }
            catch (Exception e)
            {
                loadError = "role_map_parse_failed:" + e.Message;
                return null;
            }
        }

        public static bool IsGrounded(GdaiSemanticRoleEntry entry)
        {
            return entry != null && !string.IsNullOrEmpty(entry.source) && GroundedSources.Contains(entry.source.Trim());
        }

        private static string AbsolutePath()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, RoleMapPath));
        }
    }
}
