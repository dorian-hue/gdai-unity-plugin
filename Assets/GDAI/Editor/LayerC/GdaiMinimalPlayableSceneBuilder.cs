using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GDAI.Bridge.Editor.LayerB; // GdaiLayerBSceneQuery (same GDAI.Editor assembly)

// =====================================================================================
// GDAI Unity Plugin · Layer C0 · Minimal Playable Scene Builder.
// Analyze = READ-ONLY report. Prepare = create ONLY missing roles, after a confirmation
// dialog, with full Undo, marking the scene dirty but NEVER saving it.
// Reflection-only for generated component types (no compile-time dependency). Never
// overwrites/moves/deletes existing objects, never touches ProjectSettings, never creates
// InputAction assets. Binding is left to Layer B / B2 (this tool only prepares structure).
// This is Layer C0 — a minimal preparer, not a full scene generator.
// =====================================================================================
namespace GDAI.Bridge.Editor.LayerC
{
    public static class GdaiMinimalPlayableSceneBuilder
    {
        private const string UndoLabel = "GDAI Layer C · Prepare Minimal Playable Scene";

        private enum St { Ready, Missing, Ambiguous, Unavailable }

        private sealed class RoleResult
        {
            public string role;
            public St status;
            public string detail;
        }

        private sealed class PlanItem
        {
            public enum Kind { CreateCamera, CreateWithComponent, AddComponentToExisting }
            public Kind kind;
            public string goName;
            public Type primaryType;   // component to add to a newly-created GameObject
            public Type extraType;     // optional 2nd component (e.g. CombatLocator on a new Player)
            public Vector3 position;
            public GameObject existingTarget; // for AddComponentToExisting

            public string Describe()
            {
                switch (kind)
                {
                    case Kind.CreateCamera:
                        return $"Create GameObject '{goName}' (+ Camera) at {position}";
                    case Kind.CreateWithComponent:
                        return extraType != null
                            ? $"Create GameObject '{goName}' (+ {primaryType.Name} + {extraType.Name}) at {position}"
                            : $"Create GameObject '{goName}' (+ {primaryType.Name}) at {position}";
                    default:
                        return $"Add {primaryType.Name} to existing {PathOf(existingTarget.transform)}";
                }
            }
        }

        private sealed class Analysis
        {
            public readonly List<RoleResult> roles = new List<RoleResult>();
            public readonly List<PlanItem> plan = new List<PlanItem>();
        }

        // ------------------------------ menus ------------------------------

        [MenuItem("GDAI/Layer C · Analyze Minimal Playable Scene")]
        public static void AnalyzeMenu()
        {
            var a = Analyze();
            EditorUtility.DisplayDialog("GDAI · Layer C Analyze",
                FormatRoles(a.roles) + "\n\nRead-only analysis. No changes made.", "OK");
        }

        [MenuItem("GDAI/Layer C · Prepare Minimal Playable Scene")]
        public static void PrepareMenu()
        {
            var a = Analyze();
            if (a.plan.Count == 0)
            {
                bool allReady = a.roles.All(r => r.status == St.Ready);
                string msg = allReady
                    ? "Scene already has minimal playable structure. No changes made."
                    : "No safe changes are available — some roles are ambiguous or their type is unavailable (import a bundle first / resolve duplicates). No changes made.";
                EditorUtility.DisplayDialog("GDAI · Layer C Prepare", FormatRoles(a.roles) + "\n\n" + msg, "OK");
                return; // no mutation, no dirty
            }

            string planText = string.Join("\n", a.plan.Select(p => "  • " + p.Describe()));
            bool go = EditorUtility.DisplayDialog("GDAI · Layer C Prepare",
                "The following will be created/added. Existing objects are never modified, moved, or overwritten:\n\n" +
                planText + "\n\nApply? (Undo supported. The scene is marked dirty but NOT saved.)",
                "Apply", "Cancel");
            if (!go) return;

            string summary = Apply(a.plan);
            EditorUtility.DisplayDialog("GDAI · Layer C Prepare",
                summary + "\n\nNext:\n1. GDAI ▸ Layer B · Auto-bind Current Scene\n" +
                "2. GDAI ▸ Layer B2 · Auto-bind Input Actions\n3. Save the scene manually if the result looks correct.", "OK");
        }

        // ------------------------------ analysis ------------------------------

        private static Analysis Analyze()
        {
            var a = new Analysis();

            // Main Camera (Camera is a core UnityEngine type — safe to reference directly).
            var cams = GdaiLayerBSceneQuery.FindSceneComponents(typeof(Camera));
            if (cams.Count == 0)
            {
                a.roles.Add(new RoleResult { role = "Main Camera", status = St.Missing, detail = "will create 'Main Camera'" });
                a.plan.Add(new PlanItem { kind = PlanItem.Kind.CreateCamera, goName = "Main Camera", position = new Vector3(0, 0, -10) });
            }
            else if (cams.Count == 1) a.roles.Add(new RoleResult { role = "Main Camera", status = St.Ready, detail = PathOf(cams[0].transform) });
            else a.roles.Add(new RoleResult { role = "Main Camera", status = St.Ambiguous, detail = $"{cams.Count} cameras — not modifying" });

            // Simple single-component roles.
            SimpleRole(a, "InputManager", "InputManager", "InputManager", Vector3.zero);

            // Player (CharacterStateMachine) + CombatLocatorSystem.
            AnalyzePlayer(a);

            AnalyzeEnemyDirector(a);
            SimpleRole(a, "GameIntegrationController", "GameIntegrationController", "GameIntegrationController", Vector3.zero);

            return a;
        }

        // Dedicated EnemyDirector role: EnemyDirector components exist on both the manager and each
        // enemy instance, so a plain single-component check is wrongly "ambiguous". Prefer the manager
        // by GameObject name (matching Layer B's manager binding) instead of mutating.
        private static void AnalyzeEnemyDirector(Analysis a)
        {
            var (type, ambiguousType) = ResolveType("EnemyDirector");
            if (type == null)
            {
                a.roles.Add(new RoleResult
                {
                    role = "EnemyDirector",
                    status = ambiguousType ? St.Ambiguous : St.Unavailable,
                    detail = ambiguousType ? "multiple types named 'EnemyDirector'" : "type 'EnemyDirector' not found (import a bundle first)"
                });
                return;
            }

            var comps = GdaiLayerBSceneQuery.FindSceneComponents(type);
            if (comps.Count == 0)
            {
                a.roles.Add(new RoleResult { role = "EnemyDirector", status = St.Missing, detail = "will create 'EnemyManager'" });
                a.plan.Add(new PlanItem { kind = PlanItem.Kind.CreateWithComponent, goName = "EnemyManager", primaryType = type, position = Vector3.zero });
                return;
            }
            if (comps.Count == 1)
            {
                a.roles.Add(new RoleResult { role = "EnemyDirector", status = St.Ready, detail = PathOf(comps[0].transform) });
                return;
            }

            // Multiple EnemyDirectors (manager + enemy instances): pick the manager by name.
            var named = comps.Where(c => c.gameObject.name == "EnemyManager").ToList();
            if (named.Count != 1)
                named = comps.Where(c => PathOf(c.transform).EndsWith("/EnemyManager", StringComparison.Ordinal)).ToList();

            if (named.Count == 1)
                a.roles.Add(new RoleResult { role = "EnemyDirector", status = St.Ready, detail = PathOf(named[0].transform) });
            else
                a.roles.Add(new RoleResult { role = "EnemyDirector", status = St.Ambiguous, detail = $"{comps.Count} in scene, no unique 'EnemyManager' — not modifying" });
        }

        private static void SimpleRole(Analysis a, string roleLabel, string typeName, string goName, Vector3 pos)
        {
            var (type, ambiguousType) = ResolveType(typeName);
            if (type == null)
            {
                a.roles.Add(new RoleResult
                {
                    role = roleLabel,
                    status = ambiguousType ? St.Ambiguous : St.Unavailable,
                    detail = ambiguousType ? $"multiple types named '{typeName}'" : $"type '{typeName}' not found (import a bundle first)"
                });
                return;
            }

            var comps = GdaiLayerBSceneQuery.FindSceneComponents(type);
            if (comps.Count == 0)
            {
                a.roles.Add(new RoleResult { role = roleLabel, status = St.Missing, detail = $"will create '{goName}'" });
                a.plan.Add(new PlanItem { kind = PlanItem.Kind.CreateWithComponent, goName = goName, primaryType = type, position = pos });
            }
            else if (comps.Count == 1) a.roles.Add(new RoleResult { role = roleLabel, status = St.Ready, detail = PathOf(comps[0].transform) });
            else a.roles.Add(new RoleResult { role = roleLabel, status = St.Ambiguous, detail = $"{comps.Count} in scene — not modifying" });
        }

        private static void AnalyzePlayer(Analysis a)
        {
            var (csmType, csmAmb) = ResolveType("CharacterStateMachine");
            var (combatType, combatAmb) = ResolveType("CombatLocatorSystem");

            if (csmType == null)
            {
                a.roles.Add(new RoleResult { role = "Player / CharacterStateMachine", status = csmAmb ? St.Ambiguous : St.Unavailable,
                    detail = csmAmb ? "multiple 'CharacterStateMachine' types" : "type not found (import a bundle first)" });
                a.roles.Add(new RoleResult { role = "CombatLocatorSystem", status = St.Unavailable, detail = "player unresolved" });
                return;
            }

            var csmComps = GdaiLayerBSceneQuery.FindSceneComponents(csmType);
            if (csmComps.Count > 1)
            {
                a.roles.Add(new RoleResult { role = "Player / CharacterStateMachine", status = St.Ambiguous, detail = $"{csmComps.Count} in scene — not modifying" });
                a.roles.Add(new RoleResult { role = "CombatLocatorSystem", status = St.Ambiguous, detail = "player ambiguous — not modifying" });
                return;
            }

            if (csmComps.Count == 0)
            {
                // Create a Player with CSM, and (if available) CombatLocator in the same action.
                a.roles.Add(new RoleResult { role = "Player / CharacterStateMachine", status = St.Missing, detail = "will create 'Player'" });
                a.plan.Add(new PlanItem
                {
                    kind = PlanItem.Kind.CreateWithComponent,
                    goName = "Player",
                    primaryType = csmType,
                    extraType = combatType, // null if unavailable
                    position = new Vector3(-1.5f, 0, 0)
                });
                a.roles.Add(combatType != null
                    ? new RoleResult { role = "CombatLocatorSystem", status = St.Missing, detail = "will add to new 'Player'" }
                    : new RoleResult { role = "CombatLocatorSystem", status = St.Unavailable, detail = "type not found (import a bundle first)" });
                return;
            }

            // Exactly one CSM — that GameObject is the Player.
            var player = csmComps[0].gameObject;
            a.roles.Add(new RoleResult { role = "Player / CharacterStateMachine", status = St.Ready, detail = PathOf(player.transform) });

            if (combatType == null)
            {
                a.roles.Add(new RoleResult { role = "CombatLocatorSystem", status = combatAmb ? St.Ambiguous : St.Unavailable,
                    detail = combatAmb ? "multiple types" : "type not found (import a bundle first)" });
                return;
            }
            if (player.GetComponent(combatType) != null)
            {
                a.roles.Add(new RoleResult { role = "CombatLocatorSystem", status = St.Ready, detail = PathOf(player.transform) });
            }
            else
            {
                a.roles.Add(new RoleResult { role = "CombatLocatorSystem", status = St.Missing, detail = $"will add to {PathOf(player.transform)}" });
                a.plan.Add(new PlanItem { kind = PlanItem.Kind.AddComponentToExisting, primaryType = combatType, existingTarget = player });
            }
        }

        // ------------------------------ apply ------------------------------

        private static string Apply(List<PlanItem> plan)
        {
            var created = new List<string>();
            var added = new List<string>();

            foreach (var p in plan)
            {
                switch (p.kind)
                {
                    case PlanItem.Kind.CreateCamera:
                    {
                        var go = new GameObject(p.goName);
                        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
                        go.transform.position = p.position;
                        Undo.AddComponent(go, typeof(Camera));
                        TrySetTag(go, "MainCamera");
                        created.Add($"{PathOf(go.transform)} (+ Camera)");
                        break;
                    }
                    case PlanItem.Kind.CreateWithComponent:
                    {
                        var go = new GameObject(p.goName);
                        Undo.RegisterCreatedObjectUndo(go, UndoLabel);
                        go.transform.position = p.position;
                        Undo.AddComponent(go, p.primaryType);
                        string extra = "";
                        if (p.extraType != null) { Undo.AddComponent(go, p.extraType); extra = $" + {p.extraType.Name}"; }
                        created.Add($"{PathOf(go.transform)} (+ {p.primaryType.Name}{extra})");
                        break;
                    }
                    case PlanItem.Kind.AddComponentToExisting:
                    {
                        Undo.AddComponent(p.existingTarget, p.primaryType);
                        added.Add($"{p.primaryType.Name} to {PathOf(p.existingTarget.transform)}");
                        break;
                    }
                }
            }

            // Mark dirty exactly once, only because we actually mutated the scene.
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            var sb = new StringBuilder("Layer C prepared minimal scene.\n");
            if (created.Count > 0) { sb.AppendLine("\nCreated:"); foreach (var c in created) sb.AppendLine("  • " + c); }
            if (added.Count > 0) { sb.AppendLine("\nAdded components:"); foreach (var c in added) sb.AppendLine("  • " + c); }
            foreach (var c in created) Debug.Log($"[GDAI][LayerC] Created {c}");
            foreach (var c in added) Debug.Log($"[GDAI][LayerC] Added {c}");
            return sb.ToString().TrimEnd();
        }

        // ------------------------------ utilities ------------------------------

        private static (Type type, bool ambiguous) ResolveType(string typeName)
        {
            var matches = TypeCache.GetTypesDerivedFrom<MonoBehaviour>().Where(t => t.Name == typeName).ToList();
            if (matches.Count == 1) return (matches[0], false);
            if (matches.Count == 0) return (null, false); // unavailable
            return (null, true); // ambiguous
        }

        private static void TrySetTag(GameObject go, string tag)
        {
            try { go.tag = tag; } // "MainCamera" is a built-in tag; guarded just in case.
            catch { /* leave Untagged if the tag is somehow unavailable */ }
        }

        private static string FormatRoles(List<RoleResult> roles)
        {
            var sb = new StringBuilder("Minimal playable scene analysis:\n");
            foreach (var r in roles) sb.AppendLine($"  {r.role}: {r.status}  ({r.detail})");
            return sb.ToString().TrimEnd();
        }

        private static string PathOf(Transform t)
        {
            if (t == null) return "(null)";
            var stack = new List<string>();
            for (var cur = t; cur != null; cur = cur.parent) stack.Add(cur.name);
            stack.Reverse();
            return "/" + string.Join("/", stack);
        }
    }
}
