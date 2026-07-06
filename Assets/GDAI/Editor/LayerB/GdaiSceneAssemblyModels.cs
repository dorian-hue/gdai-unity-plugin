using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// =====================================================================================
// A3-UNITY-CONSUME-LONGRUN-0B · C1 · Shared Scene Assembly DTO + loader (Editor, Layer B).
//
// Single source of the GDAI_SceneAssembly.json shape + a tolerant loader, so the validator
// (BUILD-1) and every consumer (spawn markers / bounds / blockers) parse the SAME model
// instead of each redefining it. Mirrors _shared/code_gen/sceneAssembly.ts SceneAssembly
// (read-only projection; unknown fields ignored, missing → default). `arena` may be null.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiSceneAssemblyModels
    {
        public const string SceneAssemblyPath = "Assets/GDAI_Generated/GDAI_SceneAssembly.json";

        /// <summary>Load + parse the imported GDAI_SceneAssembly.json TextAsset. Never throws.</summary>
        public static bool TryLoad(out SceneAssemblyDto dto, out string error)
        {
            dto = null;
            error = null;
            var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(SceneAssemblyPath);
            if (textAsset == null)
            {
                error = "GDAI_SceneAssembly.json not found at " + SceneAssemblyPath +
                        " (import a bundle that contains sceneAssembly first).";
                return false;
            }
            try { dto = JsonConvert.DeserializeObject<SceneAssemblyDto>(textAsset.text); }
            catch (Exception e) { error = "invalid JSON — " + e.Message; return false; }
            if (dto == null) { error = "invalid JSON — parsed to null."; return false; }
            return true;
        }

        /// <summary>First 8 chars of an entity id (dashes stripped) for deterministic short names.</summary>
        public static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "none";
            string clean = id.Replace("-", "");
            return clean.Length > 8 ? clean.Substring(0, 8) : clean;
        }
    }

    [Serializable]
    public class SceneAssemblyDto
    {
        public int version;
        public string source;
        public string project_id;
        public string scene_layout_id;
        public ArenaDto arena;                 // may be null (no scene_layout)
        public List<PlacementDto> placements;
        public List<SpawnDto> spawns;
        public List<BlockerDto> default_blockers;
        public List<DiagnosticDto> diagnostics;
    }

    [Serializable] public class ArenaDto { public float width; public float height; public string source; }

    [Serializable]
    public class PlacementDto
    {
        public string entity_id;
        public float x;
        public float y;
        public float scale;
        public int z_index;
        public string semantic_role;   // "player" | "enemy" | "unknown"
        public string role_source;
    }

    [Serializable]
    public class SpawnDto
    {
        public string role;            // "player_spawn" | "enemy_spawn"
        public string entity_id;
        public float x;
        public float y;
        public string source;
    }

    [Serializable]
    public class BlockerDto
    {
        public string id;              // "arena_left" | "arena_right" | "arena_top" | "arena_bottom"
        public string kind;            // "arena_edge"
        public string shape;           // "box"
        public float x;
        public float y;
        public float w;
        public float h;
        public string source;
    }

    [Serializable] public class DiagnosticDto { public string code; public string message; }
}
