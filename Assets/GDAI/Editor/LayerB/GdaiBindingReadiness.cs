using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GDAI.Bridge.Editor.LayerA;

// =====================================================================================
// GDAI Unity Plugin · DOWNSTREAM-BUILD-4 · Binding readiness (dry-run) + self-checks.
//
// DEBT-X1 asset-field slice: deterministic, zero-mutation diagnosis of the full chain
//   role → alias/canonical → source gate → scope gate → registry Sprite → scene target.
//
// Two layers:
//   PURE  Evaluate(map, spriteResolver, targetResolver) — no Unity scene/asset access;
//         resolvers are injected so the logic is testable in-memory (self-checks below).
//   MENUS GDAI ▸ Assets · Validate Semantic Sprite Bindings  (dry-run on REAL scene/registry)
//         GDAI ▸ Assets · Run Binding Self-Checks            (7 in-memory contract assertions)
//
// No silent skip: every non-bindable row carries a precise reason. Nothing here mutates
// the scene, the registry, or any file.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public class GdaiRoleReadinessRow
    {
        public string role;
        public string canonicalRole;
        public string entityId;
        public string scopeType;
        public string source;
        public string targetName;      // resolved scene target (or null)
        public bool bindable;
        public string reason;          // null when bindable
    }

    public static class GdaiBindingReadiness
    {
        public delegate bool TrySpriteForEntity(string entityId, out string failReason);
        public delegate bool TryTargetForRole(string role, out string targetName, out string failReason);

        // ---------- PURE EVALUATION (no Unity scene/asset APIs) ----------

        public static List<GdaiRoleReadinessRow> Evaluate(
            GdaiSemanticRoleMapData map,
            TrySpriteForEntity spriteResolver,
            TryTargetForRole targetResolver)
        {
            var rows = new List<GdaiRoleReadinessRow>();
            if (map == null || map.roles == null) return rows;

            foreach (var entry in map.roles)
            {
                if (entry == null || string.IsNullOrEmpty(entry.role)) continue;
                var scope = GdaiSemanticRoleMap.ResolveScope(entry, map);
                var row = new GdaiRoleReadinessRow
                {
                    role = entry.role,
                    canonicalRole = GdaiSemanticSpriteBinder.CanonicalRole(entry.role.Trim()),
                    entityId = entry.entity_id,
                    scopeType = scope.type,
                    source = entry.source,
                };

                if (string.IsNullOrEmpty(entry.entity_id)) { Fail(row, "missing_entity_id"); rows.Add(row); continue; }
                if (entry.source != null && entry.source.Trim().Equals("inferred_low_confidence", StringComparison.OrdinalIgnoreCase))
                { Fail(row, "low_confidence_not_bindable"); rows.Add(row); continue; }
                if (!GdaiSemanticRoleMap.IsGrounded(entry))
                { Fail(row, "source_not_grounded:" + (entry.source ?? "missing")); rows.Add(row); continue; }
                if (!GdaiSemanticRoleMap.BindingSupportedScopes.Contains(scope.type))
                { Fail(row, "scope_not_supported:" + scope.type); rows.Add(row); continue; }

                if (spriteResolver == null || !spriteResolver(entry.entity_id, out string spriteReason))
                { Fail(row, "sprite_unresolved:" + (spriteResolver == null ? "no_resolver" : spriteReason)); rows.Add(row); continue; }

                if (targetResolver == null || !targetResolver(row.canonicalRole, out string targetName, out string targetReason))
                { Fail(row, "target_unresolved:" + (targetResolver == null ? "no_resolver" : targetReason)); rows.Add(row); continue; }

                row.targetName = targetName;
                row.bindable = true;
                rows.Add(row);
            }
            return rows;
        }

        private static void Fail(GdaiRoleReadinessRow row, string reason)
        {
            row.bindable = false;
            row.reason = reason;
        }

        // ---------- DRY-RUN MENU (real registry + real scene · zero mutation) ----------

        [MenuItem("GDAI/Assets · Validate Semantic Sprite Bindings")]
        public static void ValidateMenu()
        {
            var map = GdaiSemanticRoleMap.Load(out string loadError);
            if (map == null)
            {
                string msg = "Role map not found — nothing to validate.\n\n" + loadError +
                             "\n\nRun Import Latest Bundle first (backend role map auto-writes on import)," +
                             "\nor confirm roles in Worldbuilding → Playable Assembly Readiness, then re-Assemble.";
                Debug.Log("[GDAI][Assets][SemanticBinding][Preflight] role_map_missing: " + loadError);
                EditorUtility.DisplayDialog("GDAI · Validate Semantic Sprite Bindings", msg, "OK");
                return;
            }

            var rows = Evaluate(map, RealSpriteResolver, RealTargetResolver);
            int ok = 0, bad = 0;
            var lines = new List<string>();
            foreach (var r in rows)
            {
                if (r.bindable) ok++; else bad++;
                string line = $"role={r.role} canonical={r.canonicalRole} entity_id={Short(r.entityId)} scope={r.scopeType} " +
                              (r.bindable
                                ? $"registry_sprite=resolved target={r.targetName} target_component=SpriteRenderer bindable=YES"
                                : $"bindable=NO reason={r.reason}");
                lines.Add(line);
                Debug.Log("[GDAI][Assets][SemanticBinding][Preflight] " + line);
            }
            Debug.Log($"[GDAI][Assets][SemanticBinding][Preflight] summary bindable={ok} unresolved={bad} (dry-run · nothing mutated)");
            EditorUtility.DisplayDialog("GDAI · Validate Semantic Sprite Bindings",
                $"Dry-run only — nothing was changed.\n\nBindable: {ok}\nUnresolved: {bad}\n\n" + string.Join("\n\n", lines), "OK");
        }

        private static bool RealSpriteResolver(string entityId, out string failReason)
        {
            bool ok = GdaiImportedAssetRegistry.TryGetSpriteForEntity(entityId, out Sprite _, out failReason);
            return ok;
        }

        private static bool RealTargetResolver(string canonicalRole, out string targetName, out string failReason)
        {
            targetName = null;
            var targets = GdaiSemanticSpriteBinder.FindTargetsForRole(canonicalRole, out failReason);
            if (targets.Count == 0) { failReason = failReason ?? "no_target"; return false; }
            var target = targets[0];
            if (target.GetComponent<SpriteRenderer>() == null)
            {
                failReason = $"'{target.name}' has no SpriteRenderer";
                return false;
            }
            targetName = target.name;
            return true;
        }

        // ---------- SELF-CHECKS (BUILD-4 §11.1 · 7 assertions · in-memory · non-vacuous) ----------

        [MenuItem("GDAI/Assets · Run Binding Self-Checks")]
        public static void SelfChecksMenu()
        {
            int failures = 0;
            var log = new List<string>();
            void Check(string name, bool cond, string detail = null)
            {
                if (cond) log.Add("PASS · " + name);
                else { failures++; log.Add("FAIL · " + name + (detail != null ? " · " + detail : "")); }
            }

            GdaiSemanticRoleMapData MapWith(string role, string source, string scopeType = null)
            {
                var e = new GdaiSemanticRoleEntry { role = role, entity_id = "ent-x", source = source };
                if (scopeType != null) e.scope = new GdaiRoleScope { type = scopeType, id = null };
                return new GdaiSemanticRoleMapData { version = 2, roles = new List<GdaiSemanticRoleEntry> { e } };
            }
            bool SpriteOk(string id, out string r) { r = null; return true; }
            bool SpriteMissing(string id, out string r) { r = "asset_file_missing:test"; return false; }
            bool TargetOk(string role, out string n, out string r) { n = "Player"; r = null; return true; }
            bool TargetMissing(string role, out string n, out string r) { n = null; r = "no scene object carries CharacterStateMachine"; return false; }

            // 1 · alias normalize
            Check("player → canonical player_primary",
                GdaiSemanticSpriteBinder.CanonicalRole("player") == "player_primary" &&
                GdaiSemanticSpriteBinder.CanonicalRole("first_playable_player") == "player_primary");
            // 2 · manual_confirmed bindable(happy path)
            var r2 = Evaluate(MapWith("player", "manual_confirmed"), SpriteOk, TargetOk);
            Check("manual_confirmed + sprite + target → bindable", r2.Count == 1 && r2[0].bindable && r2[0].targetName == "Player");
            // 3 · low confidence rejected
            var r3 = Evaluate(MapWith("player", "inferred_low_confidence"), SpriteOk, TargetOk);
            Check("inferred_low_confidence → not bindable", r3.Count == 1 && !r3[0].bindable && r3[0].reason == "low_confidence_not_bindable");
            // 4 · unsupported scopes skipped
            bool allScopesSkipped = true;
            foreach (var s in new[] { "level", "wave", "module" })
            {
                var rr = Evaluate(MapWith("player", "manual_confirmed", s), SpriteOk, TargetOk);
                if (rr.Count != 1 || rr[0].bindable || !rr[0].reason.StartsWith("scope_not_supported:")) allScopesSkipped = false;
            }
            Check("level/wave/module scopes → skipped with reason", allScopesSkipped);
            // 5 · missing registry sprite reason
            var r5 = Evaluate(MapWith("enemy", "manual_confirmed"), SpriteMissing, TargetOk);
            Check("missing sprite → sprite_unresolved reason", r5.Count == 1 && !r5[0].bindable && r5[0].reason.StartsWith("sprite_unresolved:"));
            // 6 · missing target reason
            var r6 = Evaluate(MapWith("player", "manual_confirmed"), SpriteOk, TargetMissing);
            Check("missing target → target_unresolved reason", r6.Count == 1 && !r6[0].bindable && r6[0].reason.StartsWith("target_unresolved:"));
            // 7 · enemy canonical + v1-style default scope resolves first_playable
            var mapV1 = new GdaiSemanticRoleMapData { version = 1, roles = new List<GdaiSemanticRoleEntry> { new GdaiSemanticRoleEntry { role = "enemy", entity_id = "ent-y", source = "manual_confirmed" } } };
            var r7 = Evaluate(mapV1, SpriteOk, TargetOk);
            Check("v1 map (no scope) → first_playable + enemy canonical enemy_archetype",
                r7.Count == 1 && r7[0].bindable && r7[0].scopeType == "first_playable" && r7[0].canonicalRole == "enemy_archetype");

            string summary = (failures == 0 ? "ALL PASS" : failures + " FAILURE(S)") + " · " + log.Count + " checks";
            Debug.Log("[GDAI][Assets][SemanticBinding][SelfCheck] " + summary + "\n" + string.Join("\n", log));
            EditorUtility.DisplayDialog("GDAI · Binding Self-Checks", summary + "\n\n" + string.Join("\n", log), "OK");
        }

        private static string Short(string id)
        {
            return string.IsNullOrEmpty(id) ? "?" : (id.Length > 8 ? id.Substring(0, 8) : id);
        }
    }
}
