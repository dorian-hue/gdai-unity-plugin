using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// =====================================================================================
// A3-UNITY-CONSUME-BUILD-1 · Scene Assembly validate-only consumer (Editor, Layer B).
//
// The coherent bundle now ships scene spatial data as a generated file:
//   Assets/GDAI_Generated/GDAI_SceneAssembly.json   (type "manifest", from codegen-assembly)
// CoherentBundleImporter.ImportVerbatim writes it verbatim, so Unity imports it as a
// TextAsset today — no import-pipeline change needed. This slice ONLY reads/parses/
// validates it and logs a summary + a dry-run canvas→world coordinate mapping.
//
// SCOPE (validate-only — deliberately does NOTHING else):
//   · NO GameObject creation (no GDAI_PlayerSpawn / GDAI_EnemySpawn_* / GDAI_ArenaBounds)
//   · NO colliders, NO layers/tags, NO ProjectSettings, NO scene mutation, NO Save
//   · NEVER throws uncaught — every failure path returns a clear FAIL log
//   · does not touch GDAI_SceneBackground or any Player/Enemy binding
//
// Coordinate contract (dry-run only here; BUILD-2 will apply it to real objects):
//   canvas is 960×540-style px, origin top-left, y-down (same as web SceneCanvas).
//   PPU_WORLD = 100 · arena centered at world origin · y flipped:
//     worldX = (canvasX - arenaWidth  / 2) / PPU_WORLD
//     worldY = -(canvasY - arenaHeight / 2) / PPU_WORLD
//     worldZ = 0
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiSceneAssemblyValidator
    {
        public const string SceneAssemblyPath = "Assets/GDAI_Generated/GDAI_SceneAssembly.json";
        public const float PpuWorld = 100f;

        // ---- DTO (Newtonsoft; unknown fields ignored, missing → default). Mirrors
        //      _shared/code_gen/sceneAssembly.ts SceneAssembly (read-only projection). ----
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
            public string semantic_role;
            public string role_source;
        }

        [Serializable]
        public class SpawnDto
        {
            public string role;        // "player_spawn" | "enemy_spawn"
            public string entity_id;
            public float x;
            public float y;
            public string source;
        }

        [Serializable]
        public class BlockerDto
        {
            public string id;
            public string kind;        // "arena_edge"
            public string shape;       // "box"
            public float x;
            public float y;
            public float w;
            public float h;
            public string source;
        }

        [Serializable] public class DiagnosticDto { public string code; public string message; }

        // ------------------------------ menu ------------------------------

        [MenuItem("GDAI/Scene · Validate Scene Assembly")]
        public static void ValidateMenu()
        {
            var result = Validate();
            if (result.ok) Debug.Log(result.log);
            else Debug.LogWarning(result.log);
            EditorUtility.DisplayDialog("GDAI · Validate Scene Assembly", result.dialog, "OK");
        }

        public struct Result
        {
            public bool ok;
            public string log;      // full multi-line console text
            public string dialog;   // short dialog text
        }

        // ------------------------------ validation core (never throws) ------------------------------

        public static Result Validate()
        {
            try
            {
                // 1 · locate the imported TextAsset
                var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(SceneAssemblyPath);
                if (textAsset == null)
                {
                    return Fail("GDAI_SceneAssembly.json not found at " + SceneAssemblyPath +
                                ".\nImport a bundle that contains sceneAssembly first " +
                                "(GDAI ▸ Layer A ▸ Import Coherent Bundle).");
                }

                // 2 · parse (tolerant, never throws out)
                SceneAssemblyDto dto = null;
                try { dto = JsonConvert.DeserializeObject<SceneAssemblyDto>(textAsset.text); }
                catch (Exception e) { return Fail("invalid JSON — " + e.Message); }
                if (dto == null) return Fail("invalid JSON — parsed to null.");

                // 3 · validate required fields (collect all, don't stop at first)
                var errors = new List<string>();
                if (dto.version < 1) errors.Add("version must be >= 1 (got " + dto.version + ")");

                bool arenaOk = dto.arena != null && dto.arena.width > 0f && dto.arena.height > 0f;
                if (dto.arena == null) errors.Add("arena is missing (cannot map coordinates)");
                else if (dto.arena.width <= 0f || dto.arena.height <= 0f)
                    errors.Add("arena width/height must be > 0 (got " + dto.arena.width + " x " + dto.arena.height + ")");

                if (dto.placements == null) errors.Add("placements array is missing");
                if (dto.spawns == null) errors.Add("spawns array is missing");
                if (dto.default_blockers == null) errors.Add("default_blockers array is missing (may be empty, but must parse)");

                int playerSpawns = 0, enemySpawns = 0;
                if (dto.spawns != null)
                {
                    for (int i = 0; i < dto.spawns.Count; i++)
                    {
                        var s = dto.spawns[i];
                        if (s == null) { errors.Add("spawns[" + i + "] is null"); continue; }
                        if (string.IsNullOrEmpty(s.role)) errors.Add("spawns[" + i + "] missing role");
                        if (string.IsNullOrEmpty(s.entity_id)) errors.Add("spawns[" + i + "] missing entity_id");
                        if (s.role == "player_spawn") playerSpawns++;
                        else if (s.role == "enemy_spawn") enemySpawns++;
                        else if (!string.IsNullOrEmpty(s.role)) errors.Add("spawns[" + i + "] unknown role '" + s.role + "'");
                    }
                }

                int placementCount = dto.placements != null ? dto.placements.Count : 0;
                int blockerCount = dto.default_blockers != null ? dto.default_blockers.Count : 0;
                int diagCount = dto.diagnostics != null ? dto.diagnostics.Count : 0;

                // 4/5/6 · build summary
                var sb = new StringBuilder();
                bool ok = errors.Count == 0;
                sb.AppendLine(ok ? "[GDAI] Scene Assembly Validate PASS" : "[GDAI] Scene Assembly Validate FAIL");
                sb.AppendLine("Path: " + SceneAssemblyPath);
                sb.AppendLine("Version: " + dto.version);
                sb.AppendLine("Source: " + (dto.source ?? "(none)"));
                sb.AppendLine("Scene layout id: " + (string.IsNullOrEmpty(dto.scene_layout_id) ? "(none)" : dto.scene_layout_id));
                sb.AppendLine("Arena: " + (dto.arena != null ? (dto.arena.width + " x " + dto.arena.height) : "(missing)"));
                sb.AppendLine("Placements: " + placementCount);
                sb.AppendLine("Spawns: " + (dto.spawns != null ? dto.spawns.Count : 0) +
                              " (player=" + playerSpawns + ", enemy=" + enemySpawns + ")");
                sb.AppendLine("Default blockers: " + blockerCount);
                sb.AppendLine("Diagnostics: " + diagCount);
                if (diagCount > 0)
                    foreach (var d in dto.diagnostics)
                        sb.AppendLine("  · " + (d?.code ?? "?") + ": " + (d?.message ?? ""));

                if (!ok)
                {
                    sb.AppendLine();
                    sb.AppendLine("Reason(s):");
                    foreach (var e in errors) sb.AppendLine("  - " + e);
                }

                // 7 · dry-run canvas→world (spawns) — only when arena is usable
                if (dto.spawns != null && dto.spawns.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Spawn dry-run (canvas → world, PPU=" + PpuWorld + ", no objects created):");
                    foreach (var s in dto.spawns)
                    {
                        if (s == null) continue;
                        string idShort = ShortId(s.entity_id);
                        if (arenaOk)
                        {
                            float wx = (s.x - dto.arena.width * 0.5f) / PpuWorld;
                            float wy = -(s.y - dto.arena.height * 0.5f) / PpuWorld;
                            sb.AppendLine("- " + (s.role ?? "?") + " entity=" + idShort +
                                          " canvas=(" + s.x.ToString("0.##") + "," + s.y.ToString("0.##") + ")" +
                                          " world=(" + wx.ToString("0.##") + "," + wy.ToString("0.##") + ",0)");
                        }
                        else
                        {
                            sb.AppendLine("- " + (s.role ?? "?") + " entity=" + idShort +
                                          " canvas=(" + s.x.ToString("0.##") + "," + s.y.ToString("0.##") + ")" +
                                          " world=(skipped — arena missing/invalid)");
                        }
                    }
                }

                string log = sb.ToString().TrimEnd();
                string dialog = ok
                    ? "PASS\n\nArena: " + (dto.arena != null ? dto.arena.width + "×" + dto.arena.height : "(missing)") +
                      "\nPlacements: " + placementCount +
                      "\nSpawns: " + (dto.spawns != null ? dto.spawns.Count : 0) + " (player=" + playerSpawns + ", enemy=" + enemySpawns + ")" +
                      "\nDefault blockers: " + blockerCount +
                      "\nDiagnostics: " + diagCount +
                      "\n\nCoordinate dry-run printed to Console. No objects were created."
                    : "FAIL — " + errors.Count + " problem(s). See Console for details:\n\n- " + string.Join("\n- ", errors);

                return new Result { ok = ok, log = log, dialog = dialog };
            }
            catch (Exception e)
            {
                // Absolute backstop: never let the menu throw.
                return Fail("unexpected error — " + e.Message);
            }
        }

        // ------------------------------ helpers ------------------------------

        private static Result Fail(string reason)
        {
            string log = "[GDAI] Scene Assembly Validate FAIL\nReason: " + reason;
            return new Result { ok = false, log = log, dialog = "FAIL\n\n" + reason };
        }

        private static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "(none)";
            string clean = id.Replace("-", "");
            return clean.Length > 8 ? clean.Substring(0, 8) : clean;
        }
    }
}
