using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using GDAI.Bridge.Editor.LayerA;

// =====================================================================================
// GDAI Unity Plugin · ASSET-VERTICAL-2 · Enemy encounter readiness (diagnosis only).
//
// VERTICAL-2 truth audit found (static evidence, 2026-07-04):
//   · Scene "TestEnemy"-style objects carry the EnemyDirector MANAGER class (same guid,
//     main class of the generated file) — NOT EnemyBehavior. Nothing in generated code
//     scans/adopts pre-placed scene enemies (FindObjects* = 0 hits; Initialize only
//     clears the list). So a role-bound scene enemy is a PHYSICAL obstacle with the
//     entity sprite — visible, collidable — but NOT hit-targetable (CombatLocator
//     resolves targets via GetComponentInParent<EnemyBehavior> → TargetId = -1).
//   · True encounters are runtime clones: Instantiate(enemyPrefab) + AddComponent
//     <EnemyBehavior> + Initialize → activeEnemies → TickFSM. Their sprite comes from
//     the PREFAB ASSET, which the semantic binder (scene-scope, Undo-based) must not
//     silently rewrite. So clones fight — with the prefab's placeholder face.
//
// This menu makes that truth mechanically visible per role/object, with precise
// reasons, so "bound=N" logs can never be mistaken for encounter closure again.
// ZERO mutation: no scene writes, no prefab writes, no file writes. DEBT-X1 enemy slice.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiEnemyEncounterReadiness
    {
        private const string EnemyManagerName = "EnemyManager";     // Layer C manager disambiguation precedent
        private const string EnemyComponentType = "EnemyDirector";  // generated manager class (file main class)
        private const string EnemyRuntimeType = "EnemyBehavior";    // runtime-only combat FSM (AddComponent at spawn)

        [MenuItem("GDAI/Assets · Validate Enemy Encounter")]
        public static void ValidateMenu()
        {
            var lines = new List<string>();

            // 1 · Role map → enemy role present & bindable?
            var map = GdaiSemanticRoleMap.Load(out string loadError);
            if (map == null)
            {
                Report(lines, "role_map: MISSING (" + loadError + ") — import a bundle first");
                Finish(lines);
                return;
            }
            GdaiSemanticRoleEntry enemyEntry = null;
            foreach (var e in map.roles)
            {
                if (e != null && !string.IsNullOrEmpty(e.role) &&
                    GdaiSemanticSpriteBinder.CanonicalRole(e.role.Trim()) == "enemy_archetype")
                { enemyEntry = e; break; }
            }
            if (enemyEntry == null)
            {
                Report(lines, "enemy_role: NOT MAPPED — confirm Default Enemy in Worldbuilding, re-Assemble, re-Import");
                Finish(lines);
                return;
            }
            Report(lines, $"enemy_role: {enemyEntry.role} → canonical enemy_archetype · entity {Short(enemyEntry.entity_id)} · source {enemyEntry.source}");

            // 2 · Sprite resolves?
            bool spriteOk = GdaiImportedAssetRegistry.TryGetSpriteForEntity(enemyEntry.entity_id, out Sprite _, out string spriteReason);
            Report(lines, spriteOk ? "registry_sprite: resolved" : "registry_sprite: UNRESOLVED (" + spriteReason + ")");

            // 3 · Scene enemy objects (non-manager EnemyDirector carriers) — per-object anatomy.
            var targets = GdaiSemanticSpriteBinder.FindTargetsForRole("enemy_archetype", out string targetReason);
            if (targets.Count == 0)
            {
                Report(lines, "scene_enemy: NONE (" + (targetReason ?? "no carrier") + ")");
            }
            foreach (var go in targets)
            {
                var sr = go.GetComponent<SpriteRenderer>();
                var col = go.GetComponent<Collider2D>();
                var rb = go.GetComponent<Rigidbody2D>();
                var marker = go.GetComponent<GdaiEntitySpriteBinding>();
                bool hasRuntimeBehavior = go.GetComponents<Component>()
                    .Any(c => c != null && c.GetType().Name == EnemyRuntimeType);

                Report(lines, $"scene_enemy '{go.name}':");
                Report(lines, $"  sprite_renderer={(sr != null ? "yes" : "NO")} sprite_bound={(sr != null && sr.sprite != null && marker != null ? "yes(entity " + Short(marker.entityId) + ")" : "no")}");
                Report(lines, $"  collider2d={(col != null ? "yes" : "NO")} rigidbody2d={(rb != null ? "yes" : "no")} → physically_collidable={(col != null ? "yes" : "NO")}");
                Report(lines, $"  {EnemyRuntimeType}={(hasRuntimeBehavior ? "yes" : "NO(edit-time)")}");
                if (!hasRuntimeBehavior)
                {
                    // Generated-code fact: only spawned clones receive EnemyBehavior; pre-placed
                    // scene enemies are never adopted → not tracked, not hit-targetable.
                    Report(lines, "  encounter_verdict: STATIC OBSTACLE — visible + collidable, " +
                                  "but NOT hit-targetable (CombatLocator needs " + EnemyRuntimeType + "; " +
                                  "no scene-adoption path exists in generated code). Fixing this requires a " +
                                  "generated-code slice — out of scope by STOP rule.");
                }
                else
                {
                    Report(lines, "  encounter_verdict: TRUE ENCOUNTER (runtime behavior present)");
                }
            }

            // 4 · Runtime clone path: manager's enemyPrefab reference (prefab-asset face problem).
            var managerCarrier = FindManager();
            if (managerCarrier == null)
            {
                Report(lines, "enemy_manager: NOT FOUND in open scene");
            }
            else
            {
                var so = new SerializedObject(managerCarrier);
                var prefabProp = so.FindProperty("enemyPrefab");
                var prefabRef = prefabProp != null ? prefabProp.objectReferenceValue : null;
                if (prefabRef == null)
                {
                    Report(lines, "clone_path: enemyPrefab UNSET on manager — no runtime clones will spawn");
                }
                else
                {
                    bool isAsset = EditorUtility.IsPersistent(prefabRef);
                    Report(lines, $"clone_path: enemyPrefab = '{prefabRef.name}' ({(isAsset ? "PREFAB ASSET" : "scene object")})");
                    if (isAsset)
                    {
                        // Clones inherit the prefab asset's sprite, not the scene binding.
                        var prefabGo = prefabRef as GameObject;
                        var prefabSr = prefabGo != null ? prefabGo.GetComponent<SpriteRenderer>() : null;
                        bool prefabRoleBound = prefabGo != null && prefabGo.GetComponent<GdaiEntitySpriteBinding>() != null;
                        Report(lines, $"  prefab_sprite={(prefabSr != null && prefabSr.sprite != null ? prefabSr.sprite.name : "none")} role_bound={(prefabRoleBound ? "yes" : "NO")}");
                        if (!prefabRoleBound)
                            Report(lines, "  clone_verdict: runtime clones are TRUE ENCOUNTERS but wear the prefab placeholder face — " +
                                          "scene-scope binder does not rewrite prefab assets (persistent-asset mutation needs its own ruling/slice).");
                    }
                    else
                    {
                        Report(lines, "  clone_verdict: clones instantiate from a scene object — scene binding would propagate.");
                    }
                }
            }

            Finish(lines);
        }

        private static GameObject FindManager()
        {
            var type = FindGeneratedType(EnemyComponentType);
            if (type == null) return null;
            var comps = GdaiLayerBSceneQuery.FindSceneComponents(type);
            foreach (var c in comps) if (c.gameObject.name == EnemyManagerName) return c.gameObject;
            return comps.Count == 1 ? comps[0].gameObject : null;
        }

        private static Type FindGeneratedType(string typeName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                    if (t != null && t.Name == typeName && typeof(Component).IsAssignableFrom(t)) return t;
            }
            return null;
        }

        private static void Report(List<string> lines, string line)
        {
            lines.Add(line);
            Debug.Log("[GDAI][Assets][EnemyEncounter] " + line);
        }

        private static void Finish(List<string> lines)
        {
            EditorUtility.DisplayDialog("GDAI · Validate Enemy Encounter",
                "Diagnosis only — nothing was changed.\n\n" + string.Join("\n", lines), "OK");
        }

        private static string Short(string id)
        {
            return string.IsNullOrEmpty(id) ? "?" : (id.Length > 8 ? id.Substring(0, 8) : id);
        }
    }
}
