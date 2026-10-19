using System.Collections.Generic;
using GDAI.Bridge.Editor.LayerA;

// =====================================================================================
// GDAI Unity Plugin · DOWNSTREAM-BUILD-3 · Role resolver + contract validator.
//
// Resolver: role → entity_id, gated on grounded sources only (never guesses).
// Validator (DEBT-X1 · asset-field subset ONLY): asserts role-map entries carry the
// required asset-binding fields and that entity ids actually resolve in the imported
// asset registry. Event-signature / revenge contract guards are explicitly OUT of
// scope here (future task).
// Pure logic — no scene access, no Unity object mutation — so it is reasoned about
// and reusable by any future Jason-facing API.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerB
{
    public static class GdaiSemanticRoleResolver
    {
        /// <summary>role → entity_id. False + precise reason when the map is missing, the role
        /// is unmapped, duplicated, or the source is not grounded (low-confidence never binds).</summary>
        public static bool TryGetEntityIdForRole(string role, out string entityId, out string reason)
        {
            entityId = null;
            reason = null;
            if (string.IsNullOrEmpty(role)) { reason = "empty_role"; return false; }

            var data = GdaiSemanticRoleMap.Load(out string loadError);
            if (data == null) { reason = loadError; return false; }

            GdaiSemanticRoleEntry found = null;
            int hits = 0;
            foreach (var e in data.roles)
            {
                if (e != null && !string.IsNullOrEmpty(e.role) &&
                    string.Equals(e.role.Trim(), role.Trim(), System.StringComparison.OrdinalIgnoreCase))
                {
                    hits++;
                    found = e;
                }
            }
            if (hits == 0) { reason = "role_not_mapped:" + role; return false; }
            if (hits > 1) { reason = "role_mapped_multiple_times:" + role; return false; }
            if (string.IsNullOrEmpty(found.entity_id)) { reason = "role_entry_missing_entity_id:" + role; return false; }
            if (!GdaiSemanticRoleMap.IsGrounded(found))
            {
                reason = "source_not_grounded:" + (found.source ?? "unknown") + " (low-confidence mappings never auto-bind)";
                return false;
            }
            entityId = found.entity_id;
            return true;
        }

        public class ValidationReport
        {
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public bool Ok { get { return Errors.Count == 0; } }
        }

        /// <summary>
        /// DEBT-X1 asset-field subset: contract-shape + registry-resolvability check.
        /// Errors  = contract violations (missing role/entity_id, unknown source, duplicates).
        /// Warnings = resolvable-but-degraded (entity not in registry / sprite not loadable —
        ///            legitimate when the bundle has not been imported yet).
        /// </summary>
        public static ValidationReport Validate(GdaiSemanticRoleMapData data)
        {
            var r = new ValidationReport();
            if (data == null) { r.Errors.Add("role map is null/unreadable"); return r; }
            if (data.roles == null || data.roles.Count == 0) { r.Errors.Add("role map has no entries"); return r; }

            var seenRoles = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var e in data.roles)
            {
                if (e == null) { r.Errors.Add("null role entry"); continue; }
                string label = string.IsNullOrEmpty(e.role) ? "(no role)" : e.role;

                if (string.IsNullOrEmpty(e.role)) r.Errors.Add("entry missing 'role'");
                else if (!seenRoles.Add(e.role.Trim())) r.Errors.Add($"role '{e.role}' mapped multiple times");

                if (string.IsNullOrEmpty(e.entity_id)) r.Errors.Add($"{label}: missing 'entity_id'");
                if (string.IsNullOrEmpty(e.source)) r.Errors.Add($"{label}: missing 'source'");
                else if (!GdaiSemanticRoleMap.GroundedSources.Contains(e.source.Trim()) &&
                         !string.Equals(e.source.Trim(), "inferred_low_confidence", System.StringComparison.OrdinalIgnoreCase))
                    r.Errors.Add($"{label}: unknown source '{e.source}'");
                else if (!GdaiSemanticRoleMap.IsGrounded(e))
                    r.Warnings.Add($"{label}: source '{e.source}' is not grounded — will NOT auto-bind");

                // Registry resolvability (asset-field half of the contract gate).
                if (!string.IsNullOrEmpty(e.entity_id))
                {
                    if (!GdaiImportedAssetRegistry.TryGetSpriteForEntity(e.entity_id, out _, out string spriteReason))
                        r.Warnings.Add($"{label}: entity '{e.entity_id}' does not resolve to a sprite yet ({spriteReason})");
                }
            }
            return r;
        }
    }
}
