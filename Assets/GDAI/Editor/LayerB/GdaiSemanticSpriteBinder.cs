using System;
using System.Collections.Generic;
using System.Linq;
using GDAI.Bridge.Editor.LayerA;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// =====================================================================================
// GDAI Unity Plugin · DOWNSTREAM-BUILD-3 · Semantic sprite binder (Editor, Layer B).
//
// Chain:  role → (GdaiSemanticRoleResolver, grounded-only) → entity_id
//              → (GdaiImportedAssetRegistry) → Sprite
//              → scene target located by GENERATED COMPONENT TYPE (Layer C convention)
//              → SpriteRenderer.sprite + GdaiEntitySpriteBinding marker (id anchor).
//
// Scene-target conventions (mirrors GdaiMinimalPlayableSceneBuilder — types, not names):
//   player → the unique scene object carrying generated 'CharacterStateMachine'
//   enemy  → scene objects carrying generated 'EnemyDirector' EXCLUDING the manager
//            (manager disambiguated by GameObject name 'EnemyManager' — the exact
//             precedent Layer C/B already uses for the SAME ambiguity; enemy instances
//             like TestEnemy carry the same component).
// Display names of ENTITIES are never used for matching (ids only). No role map file →
// dormant: precise "contract missing" diagnostics, nothing touched (truthful Path C).
//
// Discipline: per-role isolation · never throws · Undo supported · marks scene dirty,
// NEVER saves · touches only resolved targets · Import Latest never depends on this.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiSemanticSpriteBinder
    {
        // Generated-type names are the plugin's existing scene-role convention (Layer C).
        // Copied here with traceability rather than shared, mirroring KL-cal-39 spirit.
        private const string PlayerComponentType = "CharacterStateMachine";
        private const string EnemyComponentType = "EnemyDirector";
        private const string EnemyManagerName = "EnemyManager"; // Layer C's manager disambiguation

        [MenuItem("GDAI/Assets · Apply Semantic Sprite Bindings")]
        public static void ApplyMenu()
        {
            string summary = Apply();
            EditorUtility.DisplayDialog("GDAI · Semantic Sprite Bindings", summary, "OK");
        }

        /// <summary>Applies grounded role bindings. Returns a human-readable summary. Never throws.</summary>
        public static string Apply()
        {
            var lines = new List<string>();
            int bound = 0, unresolved = 0;

            // 0 · Contract present?
            var map = GdaiSemanticRoleMap.Load(out string loadError);
            if (map == null)
            {
                Debug.Log("[GDAI][Assets][SemanticBinding] No grounded role map — nothing bound. " + loadError);
                return "Semantic role contract not found.\n\n" +
                       $"Expected: {GdaiSemanticRoleMap.RoleMapPath}\n({loadError})\n\n" +
                       "This is the missing semantic contract (BUILD-3 RECON conclusion): nothing in GDD " +
                       "structured fields / world_entities / relations currently grounds player/enemy roles.\n\n" +
                       "To activate binding, create the file with grounded entries, e.g. source " +
                       "\"manual_confirmed\" mapping role → entity_id (ids are listed in " +
                       GdaiImportedAssetRegistry.RegistryPath + "). Low-confidence sources never auto-bind.";
            }

            // 1 · Contract validation (DEBT-X1 asset-field subset).
            var report = GdaiSemanticRoleResolver.Validate(map);
            foreach (var w in report.Warnings) lines.Add("WARN  " + w);
            if (!report.Ok)
            {
                foreach (var err in report.Errors) lines.Add("ERROR " + err);
                Debug.LogWarning("[GDAI][Assets][SemanticBinding] Role map invalid — nothing bound.\n" + string.Join("\n", lines));
                return "Role map failed contract validation — nothing bound.\n\n" + string.Join("\n", lines);
            }

            // 2 · Per-role bind (grounded only; resolver enforces).
            foreach (var entry in map.roles.Where(e => e != null && !string.IsNullOrEmpty(e.role)))
            {
                string role = entry.role.Trim();
                try
                {
                    // ★ROLE-OVERLAY-V2 · scope 消费规则:仅 project_default / first_playable 可绑;
                    //   其余 scope = 合法契约但忽略 + 报告(defense in depth,即使导出闸漏过)。
                    var scope = GdaiSemanticRoleMap.ResolveScope(entry, map);
                    if (!GdaiSemanticRoleMap.BindingSupportedScopes.Contains(scope.type))
                    {
                        unresolved++;
                        lines.Add($"SKIP  {role}: scope_not_supported_by_binder:{scope.type}");
                        continue;
                    }
                    // ★纵深防御:低置信即使出现在导出里也拒绝绑定(双端闸)。
                    if (entry.source != null && entry.source.Trim().Equals("inferred_low_confidence", StringComparison.OrdinalIgnoreCase))
                    {
                        unresolved++;
                        lines.Add($"SKIP  {role}: low_confidence_refused_by_binder");
                        continue;
                    }

                    if (!GdaiSemanticRoleResolver.TryGetEntityIdForRole(role, out string entityId, out string reason))
                    {
                        unresolved++;
                        lines.Add($"SKIP  {role}: {reason}");
                        continue;
                    }
                    if (!GdaiImportedAssetRegistry.TryGetSpriteForEntity(entityId, out Sprite sprite, out string spriteReason))
                    {
                        unresolved++;
                        lines.Add($"SKIP  {role}: sprite unresolved ({spriteReason})");
                        continue;
                    }

                    var targets = FindTargetsForRole(role, out string targetReason);
                    if (targets.Count == 0)
                    {
                        unresolved++;
                        lines.Add($"SKIP  {role}: {targetReason}");
                        continue;
                    }

                    foreach (var target in targets)
                    {
                        var renderer = target.GetComponent<SpriteRenderer>();
                        if (renderer == null)
                        {
                            lines.Add($"SKIP  {role}: '{target.name}' has no SpriteRenderer (not adding one to gameplay objects)");
                            unresolved++;
                            continue;
                        }
                        Undo.RecordObject(renderer, "GDAI · Apply semantic sprite binding");
                        renderer.sprite = sprite;

                        var marker = target.GetComponent<GdaiEntitySpriteBinding>();
                        if (marker == null) marker = Undo.AddComponent<GdaiEntitySpriteBinding>(target);
                        else Undo.RecordObject(marker, "GDAI · Update semantic binding marker");
                        marker.entityId = entityId;                       // DEBT-M3: stable anchor on the scene object
                        marker.assetId = entry.asset_id;
                        marker.worldEntityName = null;                    // names are not binding data
                        marker.role = role;

                        EditorSceneManager.MarkSceneDirty(target.scene);
                        bound++;
                        lines.Add($"BOUND {role} → entity {Short(entityId)} → '{target.name}'");
                    }
                }
                catch (Exception e)
                {
                    unresolved++;
                    lines.Add($"SKIP  {role}: exception:{e.Message}");
                }
            }

            Debug.Log($"[GDAI][Assets][SemanticBinding] bound={bound} unresolved={unresolved}\n" + string.Join("\n", lines));
            return $"Bound: {bound}\nUnresolved/skipped: {unresolved}\n\n" + string.Join("\n", lines) +
                   "\n\nScene marked dirty (not saved). Undo supported.";
        }

        // ---- scene target resolution (generated component types, Layer C convention) ----

        // ★ROLE-OVERLAY-V2 · role 别名族(与 web SOFT_ROLE_MATRIX aliases 一致):
        //   player 族: player / player_primary · enemy 族: enemy / default_enemy / enemy_archetype
        private static readonly HashSet<string> PlayerRoleAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "player", "player_primary" };
        private static readonly HashSet<string> EnemyRoleAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "enemy", "default_enemy", "enemy_archetype" };

        private static List<GameObject> FindTargetsForRole(string role, out string reason)
        {
            reason = null;
            var results = new List<GameObject>();

            if (PlayerRoleAliases.Contains(role))
            {
                var type = ResolveGeneratedType(PlayerComponentType, out reason);
                if (type == null) return results;
                var comps = GdaiLayerBSceneQuery.FindSceneComponents(type);
                if (comps.Count == 0) { reason = $"no scene object carries {PlayerComponentType} (run Layer C Prepare first)"; return results; }
                if (comps.Count > 1) { reason = $"{comps.Count} objects carry {PlayerComponentType} — ambiguous, not binding"; return results; }
                results.Add(comps[0].gameObject);
                return results;
            }

            if (EnemyRoleAliases.Contains(role))
            {
                var type = ResolveGeneratedType(EnemyComponentType, out reason);
                if (type == null) return results;
                var comps = GdaiLayerBSceneQuery.FindSceneComponents(type);
                // Enemy instances = EnemyDirector carriers minus the manager (Layer C precedent).
                var instances = comps.Where(c => c.gameObject.name != EnemyManagerName).ToList();
                if (comps.Count == 0) { reason = $"no scene object carries {EnemyComponentType} (import a bundle / prepare scene first)"; return results; }
                if (instances.Count == 0) { reason = "only the EnemyManager carries EnemyDirector — no enemy instance objects in the open scene"; return results; }
                foreach (var c in instances) results.Add(c.gameObject);
                return results;
            }

            reason = $"role '{role}' has no scene-target convention yet (supported: player, enemy) — contract accepted, binding deferred";
            return results;
        }

        // Reflection resolve by simple type name across loaded assemblies (generated code has
        // no compile-time reference from the plugin). Mirrors Layer C's private ResolveType.
        private static Type ResolveGeneratedType(string typeName, out string reason)
        {
            reason = null;
            var matches = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (t != null && t.Name == typeName && typeof(Component).IsAssignableFrom(t))
                        matches.Add(t);
                }
            }
            if (matches.Count == 1) return matches[0];
            reason = matches.Count == 0
                ? $"generated type '{typeName}' not found (import a bundle first)"
                : $"multiple types named '{typeName}' — ambiguous";
            return null;
        }

        private static string Short(string id)
        {
            return string.IsNullOrEmpty(id) ? "?" : (id.Length > 8 ? id.Substring(0, 8) : id);
        }
    }
}
