using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// =====================================================================================
// A3-UNITY-CONSUME-BUILD-1 · Scene Assembly validate-only consumer (Editor, Layer B).
//   (C1 refactor: DTO + loader moved to GdaiSceneAssemblyModels; coordinate math moved to
//    GdaiSceneAssemblyCoordinateUtility. Behavior/output unchanged.)
//
// Reads Assets/GDAI_Generated/GDAI_SceneAssembly.json (imported as a TextAsset by the
// existing verbatim importer), validates structure, logs a summary + a dry-run canvas→world
// coordinate mapping. Creates NO objects, NO colliders, NO layers/tags; never throws.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiSceneAssemblyValidator
    {
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

        public static Result Validate()
        {
            try
            {
                // 1 · locate + parse (shared loader; never throws)
                if (!GdaiSceneAssemblyModels.TryLoad(out SceneAssemblyDto dto, out string loadError))
                    return Fail(loadError);

                // 2 · validate required fields (collect all)
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

                // 3 · summary
                var sb = new StringBuilder();
                bool ok = errors.Count == 0;
                sb.AppendLine(ok ? "[GDAI] Scene Assembly Validate PASS" : "[GDAI] Scene Assembly Validate FAIL");
                sb.AppendLine("Path: " + GdaiSceneAssemblyModels.SceneAssemblyPath);
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

                // 4 · dry-run canvas→world (spawns) — shared converter, no objects created
                if (dto.spawns != null && dto.spawns.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Spawn dry-run (canvas → world, PPU=" + GdaiSceneAssemblyCoordinateUtility.PpuWorld + ", no objects created):");
                    foreach (var s in dto.spawns)
                    {
                        if (s == null) continue;
                        string idShort = GdaiSceneAssemblyModels.ShortId(s.entity_id);
                        if (arenaOk)
                        {
                            Vector3 w = GdaiSceneAssemblyCoordinateUtility.CanvasToWorld(s.x, s.y, dto.arena.width, dto.arena.height);
                            sb.AppendLine("- " + (s.role ?? "?") + " entity=" + idShort +
                                          " canvas=(" + s.x.ToString("0.##") + "," + s.y.ToString("0.##") + ")" +
                                          " world=(" + w.x.ToString("0.##") + "," + w.y.ToString("0.##") + ",0)");
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
                return Fail("unexpected error — " + e.Message);
            }
        }

        private static Result Fail(string reason)
        {
            string log = "[GDAI] Scene Assembly Validate FAIL\nReason: " + reason;
            return new Result { ok = false, log = log, dialog = "FAIL\n\n" + reason };
        }
    }
}
