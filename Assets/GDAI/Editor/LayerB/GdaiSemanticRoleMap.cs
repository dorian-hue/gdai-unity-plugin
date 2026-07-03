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
    // ---- ROLE-OVERLAY-V2 · scope(与 web/EF 契约三端一致;规则改动须三处同步)----
    [Serializable]
    public class GdaiRoleScope
    {
        public string type;        // project_default | first_playable | level | scene | wave | module
        public string id;          // optional (null for project-wide scopes)
    }

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

        // V2 additive (absent on v1 maps — Newtonsoft leaves them null; scope resolves via rule below)
        public GdaiRoleScope scope;
        public List<string> required_assets;
    }

    [Serializable]
    public class GdaiSemanticRoleMapData
    {
        public int version = 1;    // 1 (bootstrap) or 2
        public string project_id;  // informational; binding never branches on it
        public GdaiRoleScope default_scope; // V2 additive
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

        /// <summary>ROLE-OVERLAY-V2 · scopes the current binder consumes. Other scope values are
        /// valid contract but must be ignored + reported (never bound).</summary>
        public static readonly HashSet<string> BindingSupportedScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "project_default", "first_playable",
        };

        /// <summary>
        /// ROLE-OVERLAY-V2 · scope 空缺硬规则(web/EF/plugin 三端一致):
        /// entry.scope → map.default_scope → first_playable.
        /// </summary>
        public static GdaiRoleScope ResolveScope(GdaiSemanticRoleEntry entry, GdaiSemanticRoleMapData map)
        {
            if (entry != null && entry.scope != null && !string.IsNullOrEmpty(entry.scope.type)) return entry.scope;
            if (map != null && map.default_scope != null && !string.IsNullOrEmpty(map.default_scope.type)) return map.default_scope;
            return new GdaiRoleScope { type = "first_playable", id = null };
        }

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

        /// <summary>
        /// DOWNSTREAM-BUILD-3A · Writes a bundle-delivered role map to the local contract file.
        /// Hard rule: a null/empty bundle map NEVER overwrites an existing local file
        /// (backend without roles must not erase locally confirmed truth). Never throws.
        /// </summary>
        public static bool WriteFromBundle(GdaiSemanticRoleMapData data, out string message)
        {
            try
            {
                if (data == null || data.roles == null || data.roles.Count == 0)
                {
                    message = Exists()
                        ? "bundle has no role map — existing local role map preserved"
                        : "bundle has no role map — nothing written";
                    return false;
                }
                string abs = AbsolutePath();
                Directory.CreateDirectory(Path.GetDirectoryName(abs));
                File.WriteAllText(abs, JsonConvert.SerializeObject(data, Formatting.Indented));
                message = $"role map written ({data.roles.Count} role(s)) → {RoleMapPath}";
                return true;
            }
            catch (Exception e)
            {
                message = "role map write failed: " + e.Message;
                return false;
            }
        }

        private static string AbsolutePath()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, RoleMapPath));
        }
    }
}
